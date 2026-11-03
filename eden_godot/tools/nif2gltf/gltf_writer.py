"""Minimal glTF 2.0 binary (.glb) writer — stdlib + numpy only.

Usage:
    g = Gltf()
    acc = g.add_accessor(np_array, target=34962, minmax=True)
    ...
    g.write(path)
"""

from __future__ import annotations

import json
import os
import struct
import numpy as np

COMPONENT = {
    np.dtype("uint8"): 5121, np.dtype("int8"): 5120,
    np.dtype("uint16"): 5123, np.dtype("int16"): 5122,
    np.dtype("uint32"): 5125, np.dtype("int32"): 5124,
    np.dtype("float32"): 5126,
}
NCOMP = {1: "SCALAR", 2: "VEC2", 3: "VEC3", 4: "VEC4", 9: "MAT3", 16: "MAT4"}

DTYPE_MAP = {
    "uint8": np.uint8, "int8": np.int8,
    "uint16": np.uint16, "int16": np.int16,
    "uint32": np.uint32, "int32": np.int32,
    "float32": np.float32,
}


class Gltf:
    def __init__(self):
        self.bin = bytearray()
        self.buffer_views = []
        self.accessors = []
        self.gltf = {
            "asset": {"version": "2.0", "generator": "eden nif2gltf"},
            "scene": 0,
            "scenes": [{"nodes": []}],
            "nodes": [],
        }

    # -- low level -----------------------------------------------------------

    def add_buffer_view(self, data: bytes, target: int = None) -> int:
        pad = (4 - len(self.bin) % 4) % 4
        self.bin += b"\0" * pad
        bv = {"buffer": 0, "byteOffset": len(self.bin), "byteLength": len(data)}
        if target:
            bv["target"] = target
        self.bin += data
        self.buffer_views.append(bv)
        return len(self.buffer_views) - 1

    def add_accessor(self, array, target=None, minmax=False, normalized=False,
                     component=None) -> int:
        """array: (n,) or (n,k) ndarray. component: force conversion dtype."""
        a = np.asarray(array)
        if component is not None:
            a = a.astype(component)
        if a.dtype == np.float64:
            a = a.astype(np.float32)
        if a.dtype == np.int64:
            a = a.astype(np.int32)
        if a.dtype == np.bool_:
            a = a.astype(np.uint8)
        flat = a.reshape(len(a), -1) if a.ndim > 2 else a
        ncomp = 1 if flat.ndim == 1 else flat.shape[1]
        ctype = COMPONENT.get(a.dtype)
        if ctype is None:
            raise ValueError("unsupported dtype %s" % a.dtype)
        bv = self.add_buffer_view(a.tobytes(), target)
        acc = {"bufferView": bv, "componentType": ctype,
               "count": len(a), "type": NCOMP[ncomp]}
        if normalized:
            acc["normalized"] = True
        if minmax:
            acc["min"] = flat.min(axis=0).astype(np.float64).tolist() if flat.ndim > 1 else [float(flat.min())]
            acc["max"] = flat.max(axis=0).astype(np.float64).tolist() if flat.ndim > 1 else [float(flat.max())]
        self.accessors.append(acc)
        return len(self.accessors) - 1

    # -- scene helpers ---------------------------------------------------------

    def add_node(self, name=None, translation=None, rotation=None, scale=None,
                 matrix=None, mesh=None, skin=None, children=None, extras=None):
        node = {}
        if name:
            node["name"] = name
        if translation is not None:
            node["translation"] = [float(x) for x in translation]
        if rotation is not None:
            node["rotation"] = [float(x) for x in rotation]
        if scale is not None:
            node["scale"] = [float(x) for x in scale]
        if matrix is not None:
            node["matrix"] = [float(x) for x in matrix]
        if mesh is not None:
            node["mesh"] = mesh
        if skin is not None:
            node["skin"] = skin
        if children:
            node["children"] = list(children)
        if extras:
            node["extras"] = extras
        self.gltf["nodes"].append(node)
        return len(self.gltf["nodes"]) - 1

    def add_mesh(self, primitives):
        self.gltf.setdefault("meshes", []).append({"primitives": primitives})
        return len(self.gltf["meshes"]) - 1

    def add_material(self, mat):
        self.gltf.setdefault("materials", []).append(mat)
        return len(self.gltf["materials"]) - 1

    def add_texture(self, source, name=None):
        t = {"source": source}
        if name:
            t["name"] = name
        self.gltf.setdefault("textures", []).append(t)
        return len(self.gltf["textures"]) - 1

    def add_image(self, uri):
        self.gltf.setdefault("images", []).append({"uri": uri})
        return len(self.gltf["images"]) - 1

    def add_skin(self, joints, inverse_bind_matrices, skeleton=None, name=None):
        sk = {"joints": list(joints),
              "inverseBindMatrices": inverse_bind_matrices}
        if skeleton is not None:
            sk["skeleton"] = skeleton
        if name:
            sk["name"] = name
        self.gltf.setdefault("skins", []).append(sk)
        return len(self.gltf["skins"]) - 1

    def add_animation(self, name, channels, samplers, extras=None):
        anim = {"channels": channels, "samplers": samplers}
        if name:
            anim["name"] = name
        if extras:
            anim["extras"] = extras
        self.gltf.setdefault("animations", []).append(anim)
        return len(self.gltf["animations"]) - 1

    # -- serialization -----------------------------------------------------------

    def write(self, path):
        g = dict(self.gltf)
        g["bufferViews"] = self.buffer_views
        g["accessors"] = self.accessors
        g["buffers"] = [{"byteLength": len(self.bin)}]
        js = json.dumps(g, separators=(",", ":")).encode("utf-8")
        js += b" " * ((4 - len(js) % 4) % 4)
        binchunk = bytes(self.bin) + b"\0" * ((4 - len(self.bin) % 4) % 4)
        total = 12 + 8 + len(js) + 8 + len(binchunk)
        # assets/ = liens physiques vers portage : délier avant d'écrire
        if os.path.lexists(path):
            os.unlink(path)
        with open(path, "wb") as f:
            f.write(b"glTF")
            f.write(struct.pack("<II", 2, total))
            f.write(struct.pack("<I", len(js)) + b"JSON" + js)
            f.write(struct.pack("<I", len(binchunk)) + b"BIN\0" + binchunk)


def read_glb(path):
    """Debug helper: return (json_dict, bin_bytes) of a .glb file."""
    with open(path, "rb") as f:
        data = f.read()
    assert data[:4] == b"glTF", "bad magic"
    jlen, jtype = struct.unpack_from("<I4s", data, 12)
    gltf = json.loads(data[20:20 + jlen])
    off = 20 + jlen
    binchunk = b""
    if off + 8 <= len(data):
        blen, btype = struct.unpack_from("<I4s", data, off)
        binchunk = data[off + 8:off + 8 + blen]
    return gltf, binchunk
