"""Unit tests for the nif2gltf toolchain: synthetic NIF/KF bytes in, GLB out."""

import json
import os
import struct
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from nif2gltf.nif_reader import Reader
from nif2gltf.gltf_writer import read_glb
from nif2gltf.convert import (destrip, convert_one, TextureIndex,
                              quat_from_mat3, bspline_eval, read_kfm)
import numpy as np

HEADER = b"Gamebryo File Format, Version 20.2.0.8\n"
VER = 0x14020008


def pack_nif(types, blocks, strings, roots=(0,)):
    """Build a synthetic NIF stream."""
    out = bytearray(HEADER)
    out += struct.pack("<IBII", VER, 1, 0, len(blocks))
    out += struct.pack("<H", len(types))
    for t in types:
        b = t.encode()
        out += struct.pack("<I", len(b)) + b
    for _ti, _d in blocks:
        pass
    # block type indices come from the types list in order given per block
    return out


def build_nif(blocks, strings=(), roots=(0,), version=VER):
    """blocks: list of (typename, bytes). Returns full file bytes."""
    types = []
    tidx = {}
    for tn, _ in blocks:
        if tn not in tidx:
            tidx[tn] = len(types)
            types.append(tn)
    out = bytearray(HEADER)
    out += struct.pack("<IBII", version, 1, 0, len(blocks))
    out += struct.pack("<H", len(types))
    for t in types:
        b = t.encode()
        out += struct.pack("<I", len(b)) + b
    for tn, _d in blocks:
        out += struct.pack("<H", tidx[tn])
    for _tn, d in blocks:
        out += struct.pack("<I", len(d))
    out += struct.pack("<II", len(strings), max((len(s) for s in strings),
                                                default=0))
    for s in strings:
        b = s.encode()
        out += struct.pack("<I", len(b)) + b
    out += struct.pack("<I", 0)  # num groups
    for _tn, d in blocks:
        out += d
    out += struct.pack("<I", len(roots))
    for r in roots:
        out += struct.pack("<i", r)
    return bytes(out)


def stridx(strings, s):
    try:
        return strings.index(s)
    except ValueError:
        return -1


def node_block(strings, name, children=(), props=(), controller=-1,
               t=(0, 0, 0), scale=1.0):
    """NiNode / NiAVObject block body (ver 20.2.0.8)."""
    out = bytearray()
    out += struct.pack("<i", stridx(strings, name))
    out += struct.pack("<I", 0)          # num extra data
    out += struct.pack("<i", controller)
    out += struct.pack("<H", 0)          # flags
    out += struct.pack("<3f", *t)        # translation
    out += struct.pack("<9f", 1, 0, 0, 0, 1, 0, 0, 0, 1)  # rotation
    out += struct.pack("<f", scale)
    out += struct.pack("<I", len(props))
    for p in props:
        out += struct.pack("<i", p)
    out += struct.pack("<i", -1)         # collision object
    out += struct.pack("<I", len(children))
    for c in children:
        out += struct.pack("<i", c)
    out += struct.pack("<I", 0)          # num effects
    return bytes(out)


def tristrips_block(strings, name, data, skin=-1, props=()):
    out = bytearray(node_block(strings, name, props=props))
    # replace trailing "num effects" — node_block layout ends with it; for a
    # geometry the Data/Skin refs come after properties, before collision.
    # Rebuild explicitly to keep ordering simple:
    out = bytearray()
    out += struct.pack("<i", stridx(strings, name))
    out += struct.pack("<I", 0)
    out += struct.pack("<i", -1)
    out += struct.pack("<H", 0)
    out += struct.pack("<3f", 0, 0, 0)
    out += struct.pack("<9f", 1, 0, 0, 0, 1, 0, 0, 0, 1)
    out += struct.pack("<f", 1.0)
    out += struct.pack("<I", len(props))
    for p in props:
        out += struct.pack("<i", p)
    out += struct.pack("<i", -1)          # collision
    out += struct.pack("<i", data)        # Data ref (NiGeometry)
    out += struct.pack("<i", skin)        # Skin Instance
    # MaterialData: num materials + names + active + needs update
    out += struct.pack("<I", 0)           # num materials
    out += struct.pack("<i", -1)          # active material
    out += struct.pack("<B", 0)           # material needs update
    out += struct.pack("<I", 0)           # num children
    out += struct.pack("<I", 0)           # num effects
    return bytes(out)


def strips_data(verts, strips):
    """NiTriStripsData for given verts (n,3) and list of strip lists."""
    n = len(verts)
    total_pts = sum(len(s) for s in strips)
    ntri = sum(len(s) - 2 for s in strips)
    out = bytearray()
    out += struct.pack("<i", 0)            # group id
    out += struct.pack("<H", n)           # num vertices
    out += struct.pack("<BB", 0, 0)       # keep/compress flags
    out += struct.pack("<B", 1)           # has vertices
    out += struct.pack("<%df" % (n * 3), *np.asarray(verts).ravel())
    out += struct.pack("<H", 1)           # data flags: 1 uv set
    out += struct.pack("<B", 1)           # has normals
    out += struct.pack("<%df" % (n * 3), *np.tile([0, 0, 1.0], n))
    out += struct.pack("<4f", 0, 0, 0, 1.0)   # bounding sphere
    out += struct.pack("<B", 0)           # has vertex colors
    uv = np.tile([0.5, 0.5], n)
    out += struct.pack("<%df" % (n * 2), *uv)
    out += struct.pack("<H", 0)           # consistency
    out += struct.pack("<i", -1)          # additional data
    out += struct.pack("<H", ntri)        # num triangles
    out += struct.pack("<H", len(strips))
    for s in strips:
        out += struct.pack("<H", len(s))
    out += struct.pack("<B", 1)           # has points
    for s in strips:
        out += struct.pack("<%dH" % len(s), *s)
    return bytes(out)


def mat_prop(strings, name, diffuse=(1, 1, 1), alpha=1.0):
    out = bytearray()
    out += struct.pack("<i", stridx(strings, name))
    out += struct.pack("<I", 0)
    out += struct.pack("<i", -1)
    out += struct.pack("<3f", 1, 1, 1)     # ambient
    out += struct.pack("<3f", *diffuse)
    out += struct.pack("<3f", 0, 0, 0)     # specular
    out += struct.pack("<3f", 0, 0, 0)     # emissive
    out += struct.pack("<f", 10.0)         # glossiness
    out += struct.pack("<f", alpha)
    return bytes(out)


def tex_prop(source_ref, apply_mode=2):
    out = bytearray()
    out += struct.pack("<i", -1)           # name
    out += struct.pack("<I", 0)
    out += struct.pack("<i", -1)
    out += struct.pack("<H", 0)            # flags
    out += struct.pack("<I", apply_mode)
    out += struct.pack("<I", 7)            # texture count
    # base
    out += struct.pack("<B", 1)
    out += struct.pack("<i", source_ref)
    out += struct.pack("<H", 0)            # map flags (uv set 0)
    out += struct.pack("<B", 0)            # has transform
    for _ in range(4):                     # dark/detail/gloss/glow
        out += struct.pack("<B", 0)
    out += struct.pack("<B", 0)            # has bump
    out += struct.pack("<B", 0)            # has normal (texcount>6)
    out += struct.pack("<I", 0)            # num shader textures
    return bytes(out)


def source_tex(strings, name):
    out = bytearray()
    out += struct.pack("<i", -1)
    out += struct.pack("<I", 0)
    out += struct.pack("<i", -1)
    out += struct.pack("<B", 1)            # use external
    out += struct.pack("<i", stridx(strings, name))
    out += struct.pack("<III", 6, 2, 3)    # format prefs
    out += struct.pack("<BB", 1, 0)
    out += struct.pack("<B", 0)
    return bytes(out)


class TestReader(unittest.TestCase):
    def test_simple_scene(self):
        strings = ["Root", "Mesh0"]
        nif = build_nif([
            ("NiNode", node_block(strings, "Root", children=[1])),
            ("NiTriStrips", tristrips_block(strings, "Mesh0", data=2)),
            ("NiTriStripsData", strips_data(
                np.array([[0, 0, 0], [1, 0, 0], [0, 1, 0], [1, 1, 0],
                          [0, 0, 1]], np.float32),
                [[0, 1, 2, 3, 4]])),
        ], strings)
        n = Reader().read(nif)
        self.assertEqual(len(n.blocks), 3)
        self.assertEqual(n.blocks[0].fields["Name"], "Root")
        self.assertEqual(list(n.blocks[0].fields["Children"]), [1])
        d = n.blocks[2].fields
        self.assertEqual(d["Num Vertices"], 5)
        self.assertEqual(int(d["Num Triangles"]), 3)
        self.assertEqual(len(d["UV Sets"]), 1)

    def test_destrip(self):
        tris = destrip([5], [np.array([0, 1, 2, 3, 4])])
        self.assertEqual(tris.tolist(), [[0, 1, 2], [2, 1, 3], [2, 3, 4]])
        # degenerate skip
        tris = destrip([5], [np.array([0, 1, 1, 2, 3])])
        self.assertEqual(tris.tolist(), [[1, 2, 3]])

    def test_unknown_block_skipped(self):
        nif = build_nif([("NiNode", node_block([], "R")),
                         ("NiMadeUpThing", b"\x00" * 32)], [])
        n = Reader().read(nif)
        self.assertEqual(n.blocks[1].type, "NiMadeUpThing")
        self.assertIn("NiMadeUpThing", n.skipped_types)


class TestConvert(unittest.TestCase):
    def _tmp(self):
        return tempfile.mkdtemp()

    def test_glb_output(self):
        strings = ["Root", "Mesh0", "tex.dds"]
        nif = build_nif([
            ("NiNode", node_block(strings, "Root", children=[1])),
            ("NiTriStrips", tristrips_block(strings, "Mesh0", data=2,
                                            props=[3, 4])),
            ("NiTriStripsData", strips_data(
                np.array([[0, 0, 0], [1, 0, 0], [0, 1, 0], [1, 1, 0],
                          [0, 0, 1]], np.float32),
                [[0, 1, 2, 3, 4]])),
            ("NiMaterialProperty", mat_prop(strings, "Mesh0")),
            ("NiTexturingProperty", tex_prop(5)),
            ("NiSourceTexture", source_tex(strings, "tex.dds")),
        ], strings)
        src = os.path.join(self._tmp(), "m.nif")
        with open(src, "wb") as f:
            f.write(nif)
        import nif2gltf.convert as conv
        old = conv.RAW, conv.MODELS, conv.TEXTURES
        try:
            tmp = self._tmp()
            conv.RAW = self._tmp()
            conv.MODELS = os.path.join(tmp, "models")
            conv.TEXTURES = os.path.join(tmp, "tex")
            st = convert_one(src, TextureIndex())
        finally:
            conv.RAW, conv.MODELS, conv.TEXTURES = old
        self.assertEqual(st["status"], "ok")
        out = st.get("_out") or None
        # find produced glb
        for dp, _dn, fns in os.walk(tmp):
            for fn in fns:
                if fn.endswith(".glb"):
                    out = os.path.join(dp, fn)
        g, bin_ = read_glb(out)
        self.assertEqual(g["asset"]["version"], "2.0")
        self.assertEqual(len(g["meshes"]), 1)
        prim = g["meshes"][0]["primitives"][0]
        self.assertIn("POSITION", prim["attributes"])
        self.assertIn("TEXCOORD_0", prim["attributes"])
        self.assertEqual(g["accessors"][prim["attributes"]["POSITION"]]["count"], 5)
        self.assertEqual(g["accessors"][prim["indices"]]["count"], 9)
        self.assertEqual(len(g["nodes"]), 2)
        self.assertEqual(g["nodes"][0]["name"], "Root")
        self.assertEqual(g["nodes"][1].get("mesh"), 0)

    def test_quat_identity(self):
        q = quat_from_mat3(np.eye(3))
        self.assertAlmostEqual(q[3], 1.0)

    def test_bspline(self):
        cp = np.array([[0.], [1.], [2.], [3.], [4.]])
        v = bspline_eval(cp, 0.0)
        self.assertAlmostEqual(float(v[0]), 0.0, places=4)
        v = bspline_eval(cp, 1.0)
        self.assertAlmostEqual(float(v[0]), 4.0, places=4)
        v = bspline_eval(cp, 0.5)
        self.assertTrue(1.0 < float(v[0]) < 3.0)


class TestKfm(unittest.TestCase):
    def test_manifest(self):
        raw = bytearray(b";Gamebryo KFM File Version 2.2.0.0b\n")
        raw += b"\x01"
        raw += struct.pack("<I", 8) + b"model.nf"
        raw += struct.pack("<I", 5) + b"Bip01"
        raw += struct.pack("<IIffI", 1, 0, 0.1, 0.1, 1)
        raw += struct.pack("<i", 42)
        raw += struct.pack("<I", 9) + b"anim\\B.kf"
        raw += struct.pack("<ii", 7, 1)
        raw += struct.pack("<ii", 3, 5)  # transition type 5
        raw += struct.pack("<i", 0)
        src = os.path.join(tempfile.mkdtemp(), "x.kfm")
        with open(src, "wb") as f:
            f.write(raw)
        data = read_kfm(src)
        self.assertEqual(data["nif"], "model.nf")
        self.assertEqual(len(data["animations"]), 1)
        self.assertEqual(data["animations"][0]["kf"], "anim\\B.kf")
        self.assertEqual(data["animations"][0]["transitions"], [3])


class TestTextureIndex(unittest.TestCase):
    def _index(self, rels):
        tmp = tempfile.mkdtemp()
        for rel in rels:
            p = os.path.join(tmp, rel)
            os.makedirs(os.path.dirname(p), exist_ok=True)
            open(p, "w").close()
        return TextureIndex(tmp)

    def test_resolve_variants(self):
        idx = self._index([
            "map/texture/UD07501jungle.png",   # suffix variant of UD07501
            "map/texture/UA00101.png",         # numeric set variant
            "map/texture/W12401.png",          # shortest wins vs W124long
            "map/texture/W124longname.png",
        ])
        # exact basename
        self.assertEqual(idx.resolve("UA00101.dds"),
                         "map/texture/UA00101.png")
        # 2-digit set variant (existing behavior): UA00155 -> UA00101
        self.assertEqual(idx.resolve("UA00155.dds"),
                         "map/texture/UA00101.png")
        # suffix variant: UD07501 -> UD07501jungle.png
        self.assertEqual(idx.resolve("UD07501.dds"),
                         "map/texture/UD07501jungle.png")
        # prefix picks the shortest matching basename
        self.assertEqual(idx.resolve("W124.dds"),
                         "map/texture/W12401.png")
        # case-insensitive, mixed separators
        self.assertEqual(idx.resolve("ud07501.dds"),
                         "map/texture/UD07501jungle.png")
        # no match
        self.assertIsNone(idx.resolve("nope_nothing.dds"))
        # too-short stem never prefix-matches
        idx2 = self._index(["map/texture/011.png"])
        self.assertIsNone(idx2.resolve("01.dds"))

    def test_resolve_pack_prefix_and_suffix(self):
        idx = self._index([
            "map/texture/akud00302.png",        # prefixe 'ak'
            "map/texture/dvgnub22301.png",      # prefixe 4 lettres
            "map/texture/S0165102.png",
            "monster/texture/M02301.png",
        ])
        self.assertEqual(idx.resolve("UD00302.dds"),
                         "map/texture/akud00302.png")
        # famille numerique : set 54 absent -> dvgn..01
        self.assertEqual(idx.resolve("UB22354.dds"),
                         "map/texture/dvgnub22301.png")
        # suffixe d'export « _1 »
        self.assertEqual(idx.resolve("S0165102_1.dds"),
                         "map/texture/S0165102.png")
        # prefixe « ts » en trop cote NIF
        self.assertEqual(idx.resolve("tsM02301.DDS"),
                         "monster/texture/M02301.png")
        self.assertIsNone(idx.resolve("UE99999.dds"))

    def test_source_texture_name(self):
        from nif2gltf.convert import source_texture_name

        class S(dict):
            pass
        self.assertEqual(source_texture_name(S({"File Name": "a.dds"})), "a.dds")
        self.assertEqual(source_texture_name(
            S({"File Name": "", "Name": "dust_01.dds"})), "dust_01.dds")
        self.assertEqual(source_texture_name(
            S({"File Name": "", "Name": "Material"})), "")


if __name__ == "__main__":
    unittest.main()
