"""NIF/KF/KFM -> glTF 2.0 (GLB) batch converter.

Layout notes (Eden Eternal Reforged assets, Gamebryo 20.2/20.3):

- Geometry is exported in the *NIF coordinate system* (Z-up, NIF units —
  measured ~1.8 unit for a humanoid => unit ~= metre, see docs/nif_report.txt).
  No axis conversion, no unit scaling: Godot will apply a rotation node.
- UVs are exported AS STORED (glTF uv origin = top-left, same convention as
  the DirectX-style NIF data; no v-flip needed).
- .nif -> godot_project/assets/models/<rel>.glb
- .kf  -> godot_project/assets/anims/<rel>.glb   (one glTF animation per
  NiControllerSequence)
- .kfm -> godot_project/assets/anims/<rel>.kfm.json (manifest of referenced
  .kf files + transitions)
"""

from __future__ import annotations

import argparse
import concurrent.futures as cf
import json
import math
import os
import struct
import sys
import time
from collections import Counter

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from nif2gltf.nif_reader import Reader, NifParseError  # noqa: E402
from nif2gltf.gltf_writer import Gltf  # noqa: E402

PORTAGE = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RAW = os.environ.get("EDEN_RAW_ASSETS") or os.path.join(PORTAGE, "raw_assets")
MODELS = os.path.join(PORTAGE, "godot_project", "assets", "models")
ANIMS = os.path.join(PORTAGE, "godot_project", "assets", "anims")
TEXTURES = os.path.join(PORTAGE, "godot_project", "assets", "textures")
REPORT = os.path.join(PORTAGE, "docs", "nif_report.txt")

FLT_MAX = 3.402823466e38

# block types we attach as scene-graph nodes
NODE_TYPES = {"NiNode", "NiBillboardNode", "NiSortAdjustNode", "NiSwitchNode",
              "NiLODNode", "NiRoomGroup", "NiRoom", "NiPortal", "NiCamera",
              "NiAmbientLight", "NiDirectionalLight", "NiPointLight",
              "NiSpotLight", "NiDeferredPointLight"}
GEOM_TYPES = {"NiTriShape", "NiTriStrips", "NiLines", "NiScreenElements"}
GEOM_DATA_TYPES = {"NiTriShapeData", "NiTriStripsData", "NiLinesData",
                   "NiScreenElementsData"}
PSYS_TYPES = {"NiParticleSystem", "NiParticles", "NiPSysData"}
EXTRA_DATA_TYPES = {"NiStringExtraData", "NiIntegerExtraData",
                    "NiIntegersExtraData", "NiFloatExtraData",
                    "NiBooleanExtraData", "NiColorExtraData",
                    "NiTextKeyExtraData", "NiBinaryExtraData"}
TRANSFORM_CTRL = {"NiTransformController", "NiKeyframeController",
                  "NiMultiTargetTransformController"}

# ---------------------------------------------------------------------------
# math helpers


def mat3_cols(f):
    """parsed Matrix33 dict -> (3,3) **as stored**.

    NIF stores rotation matrices in row-vector (Gamebryo) convention:
    the stored rows are the basis vectors, i.e. the stored matrix is the
    TRANSPOSE of the column-vector matrix we want.  Verified empirically
    on char/model/B.nif: NiSkinData BoneData.SkinTransform equals
    inv(world_bone) @ world_mesh only when every local rotation is
    transposed (5555/5573 bones match; the rest are posed, not bound).
    Callers must use ``mat3_cols(f).T``.
    """
    return np.array([[f["m11"], f["m12"], f["m13"]],
                     [f["m21"], f["m22"], f["m23"]],
                     [f["m31"], f["m32"], f["m33"]]], dtype=np.float64)


def quat_from_mat3(m):
    """(3,3) rotation -> (x,y,z,w) quaternion."""
    t = m[0, 0] + m[1, 1] + m[2, 2]
    if t > 0:
        s = math.sqrt(t + 1.0) * 2
        w, x, y, z = 0.25 * s, (m[2, 1] - m[1, 2]) / s, \
            (m[0, 2] - m[2, 0]) / s, (m[1, 0] - m[0, 1]) / s
    elif m[0, 0] > m[1, 1] and m[0, 0] > m[2, 2]:
        s = math.sqrt(1.0 + m[0, 0] - m[1, 1] - m[2, 2]) * 2
        w, x, y, z = (m[2, 1] - m[1, 2]) / s, 0.25 * s, \
            (m[0, 1] + m[1, 0]) / s, (m[0, 2] + m[2, 0]) / s
    elif m[1, 1] > m[2, 2]:
        s = math.sqrt(1.0 + m[1, 1] - m[0, 0] - m[2, 2]) * 2
        w, x, y, z = (m[0, 2] - m[2, 0]) / s, (m[0, 1] + m[1, 0]) / s, \
            0.25 * s, (m[1, 2] + m[2, 1]) / s
    else:
        s = math.sqrt(1.0 + m[2, 2] - m[0, 0] - m[1, 1]) * 2
        w, x, y, z = (m[1, 0] - m[0, 1]) / s, (m[0, 2] + m[2, 0]) / s, \
            (m[1, 2] + m[2, 1]) / s, 0.25 * s
    n = math.sqrt(x * x + y * y + z * z + w * w) or 1.0
    return (x / n, y / n, z / n, w / n)


def nitransform_to_mat4(tr):
    """NiTransform dict -> (4,4) column-vector matrix (R_stored^T)."""
    r = mat3_cols(tr["Rotation"]).T * float(tr.get("Scale", 1.0))
    t = tr["Translation"]
    m = np.eye(4)
    m[:3, :3] = r
    m[:3, 3] = (t["x"], t["y"], t["z"])
    return m


def trs_from_block(fields):
    """NiAVObject fields -> (translation, quat_xyzw, scale, full_matrix|None)."""
    t = fields.get("Translation") or {}
    r = fields.get("Rotation") or {}
    s = fields.get("Scale", 1.0)
    trans = (t.get("x", 0.0), t.get("y", 0.0), t.get("z", 0.0))
    try:
        m = mat3_cols(r).T
    except KeyError:
        m = np.eye(3)
    # orthonormality check
    c0, c1, c2 = m[:, 0], m[:, 1], m[:, 2]
    n0, n1, n2 = np.linalg.norm(c0), np.linalg.norm(c1), np.linalg.norm(c2)
    ortho = (abs(n0 - 1) < 0.01 and abs(n1 - 1) < 0.01 and abs(n2 - 1) < 0.01
             and abs(np.dot(c0, c1)) < 0.01 and abs(np.dot(c0, c2)) < 0.01
             and abs(np.dot(c1, c2)) < 0.01)
    quat = None
    if ortho:
        quat = quat_from_mat3(m)
        if abs(quat[3] - 1.0) < 1e-5 and all(abs(q) < 1e-5 for q in quat[:3]):
            quat = None  # identity
    else:
        # fall back to matrix form (skewed transform)
        M = np.eye(4)
        M[:3, :3] = m * s
        M[:3, 3] = trans
        return None, None, None, M.T.flatten().tolist()
    scale = None if abs(s - 1.0) < 1e-6 else (s, s, s)
    if all(abs(v) < 1e-9 for v in trans):
        trans = None
    return trans, quat, scale, None


# ---------------------------------------------------------------------------
# texture resolution


def source_texture_name(src):
    """Nom de fichier d'un NiSourceTexture : « File Name » ; à défaut
    (texture embarquée Use External=0, File Name vide — S501 dust_01)
    le « Name » du bloc quand il ressemble à un fichier image."""
    fn = str(src.get("File Name", "") or "")
    if fn:
        return fn
    nm = str(src.get("Name", "") or "")
    if nm.lower().endswith((".dds", ".tga", ".png", ".bmp")):
        return nm
    return ""


class TextureIndex:
    def __init__(self, tex_root=TEXTURES):
        self.root = tex_root
        self.by_rel = {}
        self.by_base = {}
        if os.path.isdir(tex_root):
            for dp, _dn, fns in os.walk(tex_root):
                for fn in fns:
                    rel = os.path.relpath(os.path.join(dp, fn), tex_root)
                    rel = rel.replace(os.sep, "/")
                    low = rel.lower()
                    self.by_rel[low] = rel
                    self.by_base.setdefault(fn.lower(), []).append(rel)
        self.png_bases = sorted(k for k in self.by_base
                                if k.endswith(".png"))

    def resolve(self, nif_path: str, preferred_prefix: str = ""):
        """NIF texture path -> textures/<rel>.png relative path or None.

        Basenames are not unique in the client pack (M094.dds exists as a
        monster texture, a skill icon and a UI icon).  Prefer the texture
        tree matching the source model before falling back to basename.
        """
        p = nif_path.replace("\\", "/").strip()
        while p.startswith("./") or p.startswith("/"):
            p = p.lstrip("./") if p.startswith("./") else p.lstrip("/")
        low = p.lower()
        stem, _ext = os.path.splitext(low)
        cand = stem + ".png"
        hit = self.by_rel.get(cand)
        if hit:
            return hit
        base = os.path.basename(stem) + ".png"
        hits = self.by_base.get(base, [])
        bstem = os.path.basename(stem)
        if not hits:
            # Suffixes d'export (« _1 », « _nrm ») absents du pack :
            # « S0165102_1 » -> « S0165102 ».
            import re as _re0
            m0 = _re0.match(r"^(.+?)(_\d+|_nrm|_n|_s)$", bstem)
            if m0 and len(m0.group(1)) >= 4:
                bstem = m0.group(1)
                hits = list(self.by_base.get(bstem + ".png", []))
        if not hits:
            # Texture-set variants : « UA00151 » est un set alternatif
            # du modèle « UA001 » — le pack ne livre qu'une partie des
            # sets (01-07, 51-55). On retombe sur un set livré du même
            # stem plutôt qu'un aplat rose.
            if len(bstem) > 2 and bstem[-2:].isdigit():
                for v in ("01", "02", "03", "04", "05", "06", "07",
                          "08", "09", "51", "52", "53", "54", "55"):
                    hits = self.by_base.get(bstem[:-2] + v + ".png", [])
                    if hits:
                        break
        if not hits and len(bstem) >= 3:
            # Variantes suffixées : le pack livre « UD07501jungle » ou
            # « UB05055AK » quand le modèle demande « UD07501 » /
            # « UB05055 » — nom livré = stem requis + suffixe libre.
            # Candidats triés par basename croissant (le plus court =
            # la variante la plus proche) ; < 3 chars = trop vague.
            cands = [k for k in self.png_bases if k.startswith(bstem)]
            for k in sorted(cands, key=lambda s: (len(s), s)):
                hits.extend(sorted(self.by_base[k]))
        if not hits and len(bstem) >= 4:
            # Préfixe de pack : « UD00302 » demandé, livré « akud00302 »
            # (set « ak » des modèles vgncustom/objectak) — nom livré =
            # préfixe 1-3 lettres + stem requis. Cause des ~1300 décors
            # (murs, plantes, feuilles) convertis sans texture.
            suf = bstem + ".png"
            cands = [k for k in self.png_bases
                     if k.endswith(suf) and len(k) - len(suf) <= 4]
            for k in sorted(cands, key=lambda s: (len(s), s)):
                hits.extend(sorted(self.by_base[k]))
        if not hits and len(bstem) > 5 and bstem[-2:].isdigit():
            # Famille : « UE13202 » absent du pack, « tsUE13201 » /
            # « akUE13203 »… livrés → autre set du même modèle (le plus
            # proche numériquement) plutôt qu'un aplat blanc.
            import re as _re
            fam = _re.compile(r"^[a-z]{0,4}" + _re.escape(bstem[:-2]) +
                              r"\d\d.*\.png$")
            want = int(bstem[-2:])
            cands = [k for k in self.png_bases if fam.match(k)]
            def _dist(k):
                m = _re.search(_re.escape(bstem[:-2]) + r"(\d\d)", k)
                return (abs(int(m.group(1)) - want) if m else 99, len(k), k)
            for k in sorted(cands, key=_dist)[:1]:
                hits.extend(sorted(self.by_base[k]))
        if not hits and len(bstem) >= 7:
            # Préfixe de pack en trop côté NIF : « tsM02301 » demandé,
            # « M02301 » livré (lettre(s) de set « ts »/« ak » ajoutées
            # par l'export, l'asset de base n'en a pas).
            import re as _re2
            m2 = _re2.match(r"^(?:ts|ak|dm|dvgn|a)([a-z]+\d{4,}.*)$", bstem)
            if m2:
                hits = list(self.by_base.get(m2.group(1) + ".png", []))
        if preferred_prefix:
            pref = preferred_prefix.lower().rstrip("/") + "/"
            for rel in hits:
                if rel.lower().startswith(pref):
                    return rel
        for rel in hits:
            if not rel.lower().startswith(("ui/",)):
                return rel
        return hits[0] if hits else None


def _lst(v):
    return [] if v is None else v


def _v3(arr):
    """structured/loose array of Vector3 -> float32 (n,3)."""
    a = np.asarray(arr)
    if a.dtype.names:
        return np.column_stack([a["x"], a["y"], a["z"]]).astype(np.float32)
    return a.reshape(len(a), -1)[:, :3].astype(np.float32)


def _v4(arr):
    a = np.asarray(arr)
    if a.dtype.names:
        return np.column_stack([a["r"], a["g"], a["b"], a["a"]]
                               ).astype(np.float32)
    return a.reshape(len(a), -1)[:, :4].astype(np.float32)


def _tri(arr):
    a = np.asarray(arr)
    if a.dtype.names:
        return np.column_stack([a["v1"], a["v2"], a["v3"]]).astype(np.uint16)
    return a.reshape(len(a), -1)[:, :3].astype(np.uint16)


# ---------------------------------------------------------------------------
# triangle strips


def destrip(strip_lengths, points_list):
    """strips -> triangle index array (u32)."""
    tris = []
    for ln, pts in zip(strip_lengths, points_list):
        pts = np.asarray(pts, dtype=np.int64).ravel()
        for i in range(int(ln) - 2):
            a, b, c = pts[i], pts[i + 1], pts[i + 2]
            if i & 1:
                a, b = b, a
            if a == b or b == c or a == c:
                continue
            tris.append((a, b, c))
    if not tris:
        return np.zeros((0, 3), np.uint32)
    return np.asarray(tris, np.uint32)


# ---------------------------------------------------------------------------
# scene builder


class SceneBuilder:
    def __init__(self, nif, tex_index, glb_dir, tex_prefix=""):
        self.nif = nif
        self.tex = tex_index
        self.glb_dir = glb_dir
        self.tex_prefix = tex_prefix
        self.g = Gltf()
        self.node_map = {}      # block idx -> gltf node idx
        self.mesh_cache = {}
        self.mat_cache = {}
        self.img_cache = {}
        self.warn = []
        self.stats = Counter()
        self._building = set()
        self._claimed = set()   # gltf node idx already parented (DAG->tree)
        self._mesh_skins = {}   # mesh idx -> gltf skin idx
        self.bbox_min = np.full(3, np.inf)
        self.bbox_max = np.full(3, -np.inf)

    def _bbox_update(self, pos):
        self.bbox_min = np.minimum(self.bbox_min, pos.min(axis=0))
        self.bbox_max = np.maximum(self.bbox_max, pos.max(axis=0))

    # -- helpers -------------------------------------------------------------

    def w(self, msg):
        self.warn.append(msg)
        self.stats["warn"] += 1

    def ref_list(self, v):
        if v is None:
            return []
        return [int(x) for x in np.asarray(v).ravel() if int(x) >= 0]

    def image_for(self, nif_tex_path):
        if not nif_tex_path:
            return None
        rel = (self.tex.resolve(nif_tex_path, self.tex_prefix)
               if self.tex else None)
        if rel is None:
            self.stats["missing_textures"] += 1
            self.w("missing texture %s" % nif_tex_path)
            return None
        uri = os.path.relpath(
            os.path.join(TEXTURES, rel), self.glb_dir).replace(os.sep, "/")
        key = uri
        if key in self.img_cache:
            return self.img_cache[key]
        img = self.g.add_image(uri)
        texi = self.g.add_texture(img)
        self.img_cache[key] = texi
        return texi

    def decal_composite(self, base_rel, decal_nif_path):
        """Décal 0 du NiTexturingProperty : le natif le pose PAR-DESSUS la
        base avec son propre alpha ; glTF n'a qu'une base -> on cuit
        `<base>__d_<decal>.png` (opt-in EDEN_BAKE_DECAL=1, sinon None =
        comportement historique : base seule)."""
        if os.environ.get("EDEN_BAKE_DECAL") != "1" or not self.tex:
            return None
        drel = self.tex.resolve(decal_nif_path, self.tex_prefix)
        if drel is None:
            return None
        out_rel = os.path.splitext(base_rel)[0] + "__d_" + \
            os.path.splitext(os.path.basename(drel))[0] + ".png"
        out = os.path.join(TEXTURES, out_rel)
        if not os.path.exists(out):
            try:
                from PIL import Image
                base = Image.open(os.path.join(TEXTURES, base_rel)).convert("RGBA")
                dec = Image.open(os.path.join(TEXTURES, drel)).convert("RGBA")
                if dec.size != base.size:
                    dec = dec.resize(base.size)
                base.alpha_composite(dec)
                # assets/ = liens physiques vers portage : délier avant d'écrire
                if os.path.lexists(out):
                    os.unlink(out)
                base.save(out)
            except Exception as e:  # noqa: BLE001
                self.w("decal bake failed %s: %s" % (out_rel, e))
                return None
        return out_rel

    def image_for_rel(self, rel):
        uri = os.path.relpath(
            os.path.join(TEXTURES, rel), self.glb_dir).replace(os.sep, "/")
        if uri in self.img_cache:
            return self.img_cache[uri]
        texi = self.g.add_texture(self.g.add_image(uri))
        self.img_cache[uri] = texi
        return texi

    # -- materials -------------------------------------------------------------

    def material_for(self, geom_block):
        props = self.ref_list(geom_block.get("Properties"))
        # Les meshes « #X2nn » (tenues, masque dye + artwork gérés à
        # l'exécution par CharVisual.Dyed) et « S… » (peau teintée) ne
        # reçoivent PAS le décal cuit.
        gname = str(geom_block.get("Name") or "")
        bake_ok = not (gname.startswith("#") or
                       (len(gname) > 2 and gname[0] == "S" and gname[1].isalpha()))
        key = tuple(props) + (bake_ok,)
        if key in self.mat_cache:
            return self.mat_cache[key]
        tex_prop = mat_prop = alpha_prop = vc_prop = None
        double_sided = False
        for pr in props:
            b = self.nif.block(pr)
            if b is None:
                continue
            if b.type == "NiTexturingProperty" and tex_prop is None:
                tex_prop = b
            elif b.type == "NiMaterialProperty" and mat_prop is None:
                mat_prop = b
            elif b.type == "NiAlphaProperty" and alpha_prop is None:
                alpha_prop = b
            elif b.type == "NiVertexColorProperty" and vc_prop is None:
                vc_prop = b
            elif b.type == "NiStencilProperty":
                # Flags bits 10-11 = DrawMode ; 3 = DRAW_BOTH (double face :
                # murs fins, feuillages, drapeaux).
                if (int(b.get("Flags", 0)) >> 10) & 3 == 3:
                    double_sided = True
            else:
                self.stats["prop_" + b.type] += 1
        mat = {"pbrMetallicRoughness": {"metallicFactor": 0.0,
                                        "roughnessFactor": 0.8}}
        base = [1.0, 1.0, 1.0, 1.0]
        texcoord = 0
        if mat_prop is not None:
            f = mat_prop.fields
            d = f.get("Diffuse Color") or {}
            base = [d.get("r", 1.0), d.get("g", 1.0), d.get("b", 1.0),
                    float(f.get("Alpha", 1.0))]
            e = f.get("Emissive Color") or {}
            ev = [e.get("r", 0.0), e.get("g", 0.0), e.get("b", 0.0)]
            if any(v > 0.001 for v in ev):
                mat["emissiveFactor"] = [min(v, 1.0) for v in ev]
        mat["pbrMetallicRoughness"]["baseColorFactor"] = [
            float(min(max(v, 0.0), 1.0)) for v in base]
        if tex_prop is not None:
            f = tex_prop.fields
            # Slot d'albedo : Base ; à défaut (NIF d'artiste qui a posé
            # la texture en Detail/Dark/Decal 0 sans Base : item W345,
            # UE031…) le premier slot disponible dans cet ordre.
            slot = next((sl for sl in ("Base", "Detail", "Dark", "Decal 0")
                         if f.get("Has %s Texture" % sl)), None)
            if slot is not None:
                td = f.get("%s Texture" % slot) or {}
                src = self.nif.block(td.get("Source", -1))
                if src is not None and src.type == "NiSourceTexture":
                    t = None
                    if slot == "Base" and bake_ok and f.get("Has Decal 0 Texture"):
                        dd = f.get("Decal 0 Texture") or {}
                        dsrc = self.nif.block(dd.get("Source", -1))
                        brel = self.tex.resolve(source_texture_name(src),
                                                self.tex_prefix) if self.tex else None
                        if dsrc is not None and brel is not None:
                            comp = self.decal_composite(
                                brel, source_texture_name(dsrc))
                            if comp is not None:
                                t = self.image_for_rel(comp)
                    if t is None:
                        t = self.image_for(source_texture_name(src))
                    if t is not None:
                        flags = int(td.get("Flags", 0))
                        texcoord = flags & 0xFF
                        mat["pbrMetallicRoughness"]["baseColorTexture"] = {
                            "index": t, "texCoord": texcoord}
                if f.get("Has Glow Texture") or f.get("Has Bump Map Texture") \
                        or f.get("Has Normal Texture"):
                    self.stats["tex_extra_maps"] += 1
        if alpha_prop is not None:
            flags = int(alpha_prop.get("Flags", 0))
            blend = bool(flags & 0x0001)
            test = bool(flags & 0x0200)
            if blend:
                mat["alphaMode"] = "BLEND"
            elif test:
                mat["alphaMode"] = "MASK"
                mat["alphaCutoff"] = int(alpha_prop.get("Threshold", 128)) / 255.0
            self.stats["alpha_materials"] += 1
        if vc_prop is not None:
            self.stats["vcolor_materials"] += 1
        if double_sided:
            mat["doubleSided"] = True
        idx = self.g.add_material(mat)
        self.mat_cache[key] = idx
        self.stats["_texcoord"] = texcoord  # consumed by caller
        return idx

    # -- meshes ---------------------------------------------------------------

    def build_mesh(self, geom_block):
        data_ref = geom_block.get("Data", -1)
        mat_idx = self.material_for(geom_block)
        texcoord = self.stats.pop("_texcoord", 0)
        skin_ref = geom_block.get("Skin Instance", -1)
        key = (data_ref, skin_ref, mat_idx)
        if key in self.mesh_cache:
            return self.mesh_cache[key]
        data = self.nif.block(data_ref)
        if data is None or "_parse_error" in data.fields:
            self.w("geometry data %d unreadable" % data_ref)
            return None
        f = data.fields
        verts = f.get("Vertices")
        if verts is None or len(verts) == 0:
            self.w("geometry %d has no vertices" % data_ref)
            return None
        n = len(verts)
        pos = _v3(verts)
        attrs = {"POSITION": self.g.add_accessor(pos, 34962, minmax=True)}
        self.stats["verts"] += n
        self._bbox_update(pos)
        nrm = f.get("Normals")
        if nrm is not None and len(nrm) == n:
            attrs["NORMAL"] = self.g.add_accessor(_v3(nrm), 34962)
        uvs = _lst(f.get("UV Sets"))
        for i, s in enumerate(uvs):
            s = np.asarray(s)
            if len(s) == n:
                uv = np.column_stack(
                    [s["u"] if s.dtype.names else s[:, 0],
                     s["v"] if s.dtype.names else s[:, 1]]).astype(np.float32)
                attrs["TEXCOORD_%d" % i] = self.g.add_accessor(uv, 34962)
                self.stats["uv_sets"] += 1
        vc = f.get("Vertex Colors")
        if vc is not None and len(vc) == n:
            attrs["COLOR_0"] = self.g.add_accessor(_v4(vc), 34962)
            self.stats["vcolor_meshes"] += 1
        # indices
        if data.type == "NiTriShapeData" or data.type == "NiScreenElementsData":
            tris = f.get("Triangles")
            if tris is None or len(tris) == 0:
                self.w("trishape %d no triangles" % data_ref)
                return None
            idx = _tri(tris)
            self.stats["tris"] += len(idx)
        elif data.type == "NiLinesData":
            idx = None
            self.stats["lines_data"] += 1
        else:  # strips
            sl = f.get("Strip Lengths")
            pts = f.get("Points")
            if pts is None:
                # legacy layout: points array flat
                pts = f.get("Points")
            if sl is None or pts is None:
                self.w("strips %d missing strips data" % data_ref)
                return None
            idx = destrip(sl, pts)
            if len(idx) == 0:
                self.w("strips %d no triangles after destrip" % data_ref)
                return None
            idx = idx.astype(np.uint16)
            self.stats["tris"] += len(idx)
        prim = {"attributes": attrs, "mode": 4,
                "material": mat_idx}
        if idx is not None:
            # glTF index accessors must be SCALAR -> flatten (n,3) -> (3n,)
            prim["indices"] = self.g.add_accessor(
                np.asarray(idx).reshape(-1), 34963)
        # skinning
        skin = self.build_skin(geom_block, n)
        skin_idx = None
        if skin is not None:
            jarr, warr, skin_idx = skin
            attrs["JOINTS_0"] = self.g.add_accessor(jarr, 34962)
            attrs["WEIGHTS_0"] = self.g.add_accessor(warr, 34962)
        mesh_idx = self.g.add_mesh([prim])
        if skin_idx is not None:
            self._mesh_skins[mesh_idx] = skin_idx
        self.mesh_cache[key] = (mesh_idx, skin_idx)
        self.stats["meshes"] += 1
        return self.mesh_cache[key]

    def build_skin(self, geom_block, nverts):
        """returns (joints_u16array(n,4), weights_f32(n,4), gltf_skin_idx)."""
        si_ref = geom_block.get("Skin Instance", -1)
        if si_ref < 0:
            return None
        si = self.nif.block(si_ref)
        if si is None:
            return None
        sd = self.nif.block(si.get("Data", -1))
        sp = self.nif.block(si.get("Skin Partition", -1))
        if sp is None:
            sp = self.nif.block(sd.get("Skin Partition", -1) if sd else -1)
        bones = self.ref_list(si.get("Bones"))
        if not bones:
            self.w("skin instance %d no bones" % si_ref)
            return None
        joints_gltf = [self.node_for(b) for b in bones]
        # inverse bind matrices from NiSkinData BoneList
        ibms = np.zeros((len(bones), 16), np.float32)
        bone_list = sd.get("Bone List") if sd else None
        for i, bd in enumerate(bone_list or []):
            try:
                m = nitransform_to_mat4(bd["Skin Transform"])
                ibms[i] = m.T.flatten().astype(np.float32)
            except Exception:
                ibms[i] = np.eye(4).T.flatten().astype(np.float32)
        ibm_acc = self.g.add_accessor(ibms, minmax=False)
        skel_root = si.get("Skeleton Root", -1)
        skel_node = self.node_for(skel_root) if skel_root >= 0 else None
        skin_idx = self.g.add_skin(joints_gltf, ibm_acc, skeleton=skel_node)
        self.stats["skins"] += 1
        # per-vertex influences
        jarr = np.zeros((nverts, 4), np.uint16)
        warr = np.zeros((nverts, 4), np.float32)
        filled = np.zeros(nverts, bool)
        if sp is not None and "_parse_error" not in sp.fields:
            for part in _lst(sp.get("Partitions")):
                pv = int(part.get("Num Vertices", 0))
                pbone = [int(x) for x in np.asarray(
                    part.get("Bones")).ravel()] if part.get("Bones") is not None else []
                vmap = part.get("Vertex Map")
                weights = part.get("Vertex Weights")
                bindices = part.get("Bone Indices")
                nwpv = int(part.get("Num Weights Per Vertex", 4))
                if weights is None or bindices is None:
                    continue
                weights = np.asarray(weights)
                bindices = np.asarray(bindices)
                vmap_i = (np.asarray(vmap).ravel().astype(np.int64)
                          if vmap is not None else np.arange(pv))
                for vi in range(pv):
                    dv = int(vmap_i[vi]) if vi < len(vmap_i) else vi
                    if dv < 0 or dv >= nverts or filled[dv]:
                        continue
                    wrow = weights[vi] if vi < len(weights) else None
                    brow = bindices[vi] if vi < len(bindices) else None
                    if wrow is None or brow is None:
                        continue
                    pairs = []
                    for k in range(min(nwpv, len(brow))):
                        lb = int(brow[k])
                        wv = float(wrow[k]) if k < len(wrow) else 0.0
                        if wv <= 0:
                            continue
                        if lb < len(pbone):
                            pairs.append((pbone[lb], wv))
                        else:
                            pairs.append((lb, wv))
                    pairs.sort(key=lambda p: -p[1])
                    if len(pairs) > 4:
                        self.stats["weights_gt4"] += 1
                    pairs = pairs[:4]
                    tot = sum(p[1] for p in pairs) or 1.0
                    for k, (jb, wv) in enumerate(pairs):
                        jarr[dv, k] = min(jb, 65535)
                        warr[dv, k] = wv / tot
                    filled[dv] = True
        elif bone_list:
            # fallback: BoneData.Vertex Weights (BoneVertData)
            for bi, bd in enumerate(_lst(bone_list)):
                if not isinstance(bd, dict):
                    continue
                for vw in _lst(bd.get("Vertex Weights")):
                    try:
                        dv = int(vw["Index"] if not isinstance(vw, dict)
                                 else vw.get("Index", -1))
                        wv = float(vw["Weight"] if not isinstance(vw, dict)
                                   else vw.get("Weight", 0.0))
                    except Exception:
                        continue
                    if 0 <= dv < nverts and wv > 0:
                        k = int((warr[dv] > 0).sum())
                        if k < 4:
                            jarr[dv, k] = bi
                            warr[dv, k] = wv
                            filled[dv] = True
            tot = warr.sum(axis=1, keepdims=True)
            tot[tot == 0] = 1
            warr /= tot
        if not filled.any():
            self.w("skin %d: no vertex weights resolved" % si_ref)
            return None
        miss = int((~filled).sum())
        if miss:
            self.stats["unskinned_verts"] += miss
        # normalize unfilled rows to avoid zero weights
        rows = ~filled
        jarr[rows, 0] = 0
        warr[rows, 0] = 1.0
        self.stats["skinned_meshes"] += 1
        return jarr, warr, skin_idx

    # -- extras -----------------------------------------------------------------

    def extras_for(self, block):
        ex = {}
        for r in self.ref_list(block.get("Extra Data List")):
            b = self.nif.block(r)
            if b is None:
                continue
            name = b.get("Name", "")
            if b.type == "NiStringExtraData":
                ex[name or "string"] = b.get("String Data", "")
            elif b.type == "NiIntegerExtraData":
                ex[name or "int"] = b.get("Integer Data", 0)
            elif b.type == "NiFloatExtraData":
                ex[name or "float"] = b.get("Float Data", 0.0)
            elif b.type == "NiBooleanExtraData":
                ex[name or "bool"] = bool(b.get("Boolean Data", 0))
            elif b.type == "NiColorExtraData":
                c = b.get("Data") or {}
                ex[name or "color"] = [c.get("r", 0), c.get("g", 0),
                                       c.get("b", 0), c.get("a", 0)]
            elif b.type == "NiIntegersExtraData":
                ex[name or "ints"] = [int(x) for x in np.asarray(
                    b.get("Data", [])).ravel()]
            elif b.type == "NiTextKeyExtraData":
                keys = []
                for k in _lst(b.get("Text Keys")):
                    keys.append({"time": float(k.get("Time", 0.0)),
                                 "value": str(k.get("Value", ""))})
                ex[name or "textkeys"] = keys
            else:
                self.stats["extra_" + b.type] = self.stats.get(
                    "extra_" + b.type, 0) + 1
        if block.type in GEOM_TYPES:
            fx = self.fx_controllers(block)
            if fx:
                ex["fx"] = fx
        if block.type == "NiBillboardNode":
            ex["billboard"] = True
            self.stats["billboards"] += 1
        if block.type in ("NiParticleSystem", "NiParticles"):
            ex["psys"] = True
            self.stats["psys_nodes"] += 1
        if block.type == "NiLODNode":
            ex["lod_node"] = True
            self.stats["lod_nodes"] += 1
        if block.type == "NiPortal":
            ex["portal"] = True
            self.stats["portals"] += 1
        if block.type.endswith("Light") or block.type == "NiCamera":
            ex[block.type] = True
            self.stats["lights_cams"] += 1
        if block.type == "NiRoom" or block.type == "NiRoomGroup":
            ex[block.type] = True
            self.stats["rooms"] += 1
        return ex or None

    def _ctrl_chain(self, owner):
        """Contr\u00f4leurs d'une propri\u00e9t\u00e9 (NiAlphaController...)."""
        out = []
        ctrl = owner.get("Controller", -1)
        seen = 0
        while ctrl is not None and ctrl >= 0 and seen < 12:
            seen += 1
            cb = self.nif.block(ctrl)
            if cb is None:
                break
            out.append(cb)
            ctrl = cb.get("Next Controller", -1)
        return out

    def _ctrl_keys(self, cb, kind):
        """Cl\u00e9s (temps, valeur) d'un contr\u00f4leur via son interpolateur
        (NiFloatData / NiPosData) ; None si absent."""
        interp = self.nif.block(cb.get("Interpolator", -1))
        if interp is None:
            return None
        d = self.nif.block(interp.get("Data", -1))
        if d is None:
            return None
        kg = d.get("Data")
        keys = keygroup_keys(kg, "f" if kind == "f" else "v3")
        if not keys:
            return None
        return keys

    def fx_controllers(self, geom_block):
        """Animations de mat\u00e9riau d'un effet (alpha, couleur, UV) :
        perdues par le glTF standard, export\u00e9es en extras pour que le
        client les rejoue (sinon toutes les couches d'un effet sont
        visibles en m\u00eame temps \u00e0 alpha plein)."""
        fx = {}
        for pr in self.ref_list(geom_block.get("Properties")):
            b = self.nif.block(pr)
            if b is None:
                continue
            for cb in self._ctrl_chain(b):
                meta = {"stop": round(float(cb.get("Stop Time", 0.0)), 4),
                        "start": round(float(cb.get("Start Time", 0.0)), 4),
                        "cyc": (int(cb.get("Flags", 0)) >> 1) & 3,
                        "freq": round(float(cb.get("Frequency", 1.0)), 4)}
                if cb.type == "NiAlphaController":
                    k = self._ctrl_keys(cb, "f")
                    if k:
                        fx["a"] = [[round(t, 4), round(v, 4)] for t, v in k]
                        fx["am"] = meta
                elif cb.type == "NiMaterialColorController":
                    k = self._ctrl_keys(cb, "v3")
                    if k:
                        tc = int(cb.get("Target Color", 0))
                        fx["c%d" % tc] = [[round(t, 4)] + [round(x, 4)
                                          for x in v] for t, v in k]
                        fx["cm"] = meta
                elif cb.type == "NiTextureTransformController":
                    k = self._ctrl_keys(cb, "f")
                    if k:
                        fx.setdefault("uv", []).append({
                            "op": int(cb.get("Operation", 0)),
                            "k": [[round(t, 4), round(v, 4)] for t, v in k],
                            **meta})
        return fx

    # -- node graph ------------------------------------------------------------

    def children_of(self, block):
        kids = self.ref_list(block.get("Children"))
        if block.type == "NiRoomGroup":
            kids += [r for r in (block.get("Shell", -1),)
                     if r >= 0]
            kids += self.ref_list(block.get("Rooms"))
        elif block.type == "NiRoom":
            kids += self.ref_list(block.get("Fixtures"))
        return kids

    def node_for(self, bidx):
        if bidx in self.node_map:
            return self.node_map[bidx]
        block = self.nif.block(bidx)
        if block is None or "_parse_error" in block.fields:
            return None
        if bidx in self._building:
            return None
        f = block.fields
        name = f.get("Name") or block.type
        trans, quat, scale, mat = trs_from_block(f)
        mesh_idx = skin_idx = None
        if block.type in GEOM_TYPES:
            r = self.build_mesh(block)
            if r is not None:
                mesh_idx, skin_idx = r
        extras = self.extras_for(block)
        idx = self.g.add_node(name=name, translation=trans, rotation=quat,
                              scale=scale, matrix=mat, mesh=mesh_idx,
                              skin=skin_idx, extras=extras)
        self.node_map[bidx] = idx
        self.stats["nodes"] += 1
        self._building.add(bidx)
        node_children = []
        for c in self.children_of(block):
            cn = self.node_for(c)
            if cn is not None:
                node_children.append(self._claim(cn))
        self._building.discard(bidx)
        if node_children:
            self.g.gltf["nodes"][idx]["children"] = node_children
        return idx

    # -- DAG -> tree -----------------------------------------------------------
    # The NIF scene graph is a DAG: the same NiAVObject block can be
    # referenced as a child by several parents (S406 : 82 such nodes).
    # glTF requires a strict tree — Godot's GltfDocument rejects a node
    # whose parent is already set ("parent != -1", renders black).
    # So when a node is attached a second time we duplicate its glTF
    # subtree instead of re-parenting it (meshes/skins/accessors stay
    # shared — only the node records are cloned).

    def _claim(self, gidx):
        """Node index safe to attach (child or scene root) — duplicates
        the subtree if the node already has a parent."""
        if gidx in self._claimed:
            gidx = self._dup_subtree(gidx)
            self.stats["multi_parent_dups"] += 1
        self._claimed.add(gidx)
        return gidx

    def _dup_subtree(self, gidx):
        """Deep-copies node gidx + descendants (new node records sharing
        mesh/skin/material/accessor indices)."""
        src = self.g.gltf["nodes"][gidx]
        node = dict(src)
        kids = src.get("children")
        if kids:
            node["children"] = [self._dup_subtree(k) for k in kids]
        self.g.gltf["nodes"].append(node)
        new_idx = len(self.g.gltf["nodes"]) - 1
        self._claimed.add(new_idx)
        return new_idx

    def build(self):
        roots = [r for r in self.nif.roots if r >= 0]
        if not roots:
            # heuristics: every block without a parent
            parented = set()
            for b in self.nif.blocks:
                for c in self.children_of(b):
                    parented.add(c)
            roots = [b.index for b in self.nif.blocks
                     if b.index not in parented and
                     (b.type in NODE_TYPES or b.type in GEOM_TYPES
                      or b.type in PSYS_TYPES)][:8]
            if not roots:
                roots = [0]
        top = []
        for r in roots:
            ni = self.node_for(r)
            if ni is not None:
                top.append(self._claim(ni))
        if not top:
            top = [self.g.add_node(name="Root")]
        self.g.gltf["scenes"][0]["nodes"] = top
        # orphan joint nodes: attach to scene so skins resolve
        attached = set()
        def mark(ni):
            attached.add(ni)
            for c in self.g.gltf["nodes"][ni].get("children", []):
                mark(c)
        for ni in top:
            mark(ni)
        for sk in self.g.gltf.get("skins", []):
            for j in sk["joints"]:
                if j not in attached:
                    top.append(j)
                    attached.add(j)
                    self._claimed.add(j)
        # embedded transform animations (node controllers)
        self.build_embedded_animation()
        return self.g

    def build_embedded_animation(self):
        anim_channels = []
        anim_samplers = []
        for bidx, gidx in self.node_map.items():
            block = self.nif.blocks[bidx]
            ctrl = block.get("Controller", -1)
            seen = 0
            while ctrl >= 0 and seen < 8:
                seen += 1
                cb = self.nif.block(ctrl)
                if cb is None:
                    break
                if cb.type in TRANSFORM_CTRL or \
                        cb.type == "NiTransformController":
                    interp = self.nif.block(cb.get("Interpolator", -1))
                    if interp is None:
                        d = self.nif.block(cb.get("Data", -1))
                        interp = None
                    else:
                        d = self.nif.block(interp.get("Data", -1))
                    if d is not None and d.type == "NiTransformData":
                        self._emit_transform_channels(
                            anim_channels, anim_samplers, gidx, d.fields)
                ctrl = cb.get("Next Controller", -1)
        if anim_channels:
            self.g.add_animation("embedded", anim_channels, anim_samplers)
            self.stats["anims"] += 1
            self.stats["anim_channels"] += len(anim_channels)

    def _emit_transform_channels(self, channels, samplers, gidx, tf):
        """NiTransformData fields -> glTF channels on node gidx."""
        rot_keys = []
        if tf.get("Num Rotation Keys", 0):
            rt = tf.get("Rotation Type", 1)
            if rt == 4:
                rot_keys = xyz_to_quat_keys(_lst(tf.get("XYZ Rotations")))
            else:
                for k in _lst(tf.get("Quaternion Keys")):
                    v = k.get("Value") or {}
                    rot_keys.append((float(k.get("Time", 0.0)),
                                     (v.get("x", 0.0), v.get("y", 0.0),
                                      v.get("z", 0.0), v.get("w", 1.0))))
        for keys, path in ((rot_keys, "rotation"),
                           (keygroup_keys(tf.get("Translations"), "v3"),
                            "translation"),
                           (keygroup_keys(tf.get("Scales"), "f"), "scale")):
            if not keys:
                continue
            times = np.asarray([k[0] for k in keys], np.float32)
            vals = np.asarray([k[1] for k in keys], np.float32)
            if path == "scale" and vals.ndim == 1:
                vals = np.repeat(vals[:, None], 3, axis=1)
            si = self.g.add_accessor(times)
            so = self.g.add_accessor(vals)
            samplers.append({"input": si, "output": so,
                             "interpolation": "LINEAR"})
            channels.append({"sampler": len(samplers) - 1,
                             "target": {"node": gidx, "path": path}})


def keygroup_keys(kg, kind):
    """KeyGroup dict -> [(time, value)] ; kind 'f'|'v3'|'quat'."""
    if kg is None:
        return []
    out = []
    for k in _lst(kg.get("Keys")):
        t = float(k.get("Time", 0.0))
        v = k.get("Value")
        if kind == "f":
            out.append((t, float(v if v is not None else 1.0)))
        elif kind == "v3":
            v = v or {}
            out.append((t, (v.get("x", 0.0), v.get("y", 0.0), v.get("z", 0.0))))
        else:
            v = v or {}
            out.append((t, (v.get("x", 0.0), v.get("y", 0.0),
                            v.get("z", 0.0), v.get("w", 1.0))))
    return out


def xyz_to_quat_keys(xyz_groups):
    """3 KeyGroup<float> (euler rad) -> [(time, quat_xyzw)]."""
    if len(xyz_groups) < 3:
        return []
    series = []
    for grp in xyz_groups[:3]:
        series.append(keygroup_keys(grp, "f"))
    if not any(series):
        return []
    times = sorted({t for s in series for t, _v in s})
    if len(times) > 4096:
        times = times[::max(1, len(times) // 4096)]

    def interp(s, t):
        if not s:
            return 0.0
        ts = [k[0] for k in s]
        i = np.searchsorted(ts, t)
        if i <= 0:
            return s[0][1]
        if i >= len(s):
            return s[-1][1]
        t0, v0 = s[i - 1]
        t1, v1 = s[i]
        f = 0.0 if t1 == t0 else (t - t0) / (t1 - t0)
        return v0 + (v1 - v0) * f

    out = []
    for t in times:
        rx, ry, rz = (interp(s, t) for s in series)
        cx, sx = math.cos(rx / 2), math.sin(rx / 2)
        cy, sy = math.cos(ry / 2), math.sin(ry / 2)
        cz, sz = math.cos(rz / 2), math.sin(rz / 2)
        # File composes the stored (row-vector) matrix as Rx*Ry*Rz
        # ("rotate X then Y then Z"); transposing to column-vector
        # convention reverses the product -> quat qz*qy*qx.
        # Verified on char/animation/B.kf: 35/35 decisive bones match.
        qx = (sx, 0.0, 0.0, cx)
        qy = (0.0, sy, 0.0, cy)
        qz = (0.0, 0.0, sz, cz)
        q = quat_mul(quat_mul(qz, qy), qx)
        out.append((t, q))
    return out


def quat_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)


# ---------------------------------------------------------------------------
# B-spline evaluation (NiBSpline*Interpolator)


def bspline_eval(ctrl, u):
    """Evaluate an open (clamped) uniform cubic B-spline at u in [0,1].

    ctrl: (n, k) control points, n >= 4."""
    n = len(ctrl)
    deg = 3
    if n < 4:
        return ctrl[0] if n else None
    knots = np.zeros(n + deg + 1)
    interior = n - deg - 1          # n-4 interior knots, uniform in (0,1)
    if interior > 0:
        knots[deg + 1:n] = np.arange(1, interior + 1) / (interior + 1.0)
    knots[n:] = 1.0
    u = min(max(u, 0.0), 1.0)
    if u >= 1.0:
        k = n - 1
    else:
        k = max(deg, int(np.searchsorted(knots, u, side="right")) - 1)
    d = [ctrl[j] for j in range(k - deg, k + 1)]
    for r in range(1, deg + 1):
        for j in range(deg, r - 1, -1):
            i = k - deg + j
            span = knots[i + deg + 1 - r] - knots[i]
            a = 0.0 if span == 0 else (u - knots[i]) / span
            d[j] = (1 - a) * d[j - 1] + a * d[j]
    return d[deg]


def sample_bspline_interp(nif, iblock, stats, warn):
    """NiBSpline(Comp)TransformInterpolator -> (times, pos, rot, scale)."""
    f = iblock.fields
    sd = nif.block(f.get("Spline Data", -1))
    bd = nif.block(f.get("Basis Data", -1))
    start, stop = float(f.get("Start Time", 0.0)), float(f.get("Stop Time", 1.0))
    dur = max(stop - start, 1e-6)
    ncp = int(bd.get("Num Control Points", 0)) if bd else 0
    tr = f.get("Transform") or {}
    defq = tr.get("Rotation") or {}
    base_t = (tr.get("Translation") or {})
    base_s = float(tr.get("Scale", 1.0))
    if sd is None or ncp < 4:
        return None
    compact = iblock.type == "NiBSplineCompTransformInterpolator"
    if compact:
        cps_src = np.asarray(sd.get("Compact Control Points", []),
                             np.float64)
        offs = {"Translation": (float(f.get("Translation Offset", 0.0)),
                                float(f.get("Translation Half Range", 0.0))),
                "Rotation": (float(f.get("Rotation Offset", 0.0)),
                             float(f.get("Rotation Half Range", 0.0))),
                "Scale": (float(f.get("Scale Offset", 0.0)),
                          float(f.get("Scale Half Range", 0.0)))}
    else:
        cps_src = np.asarray(sd.get("Float Control Points", []), np.float64)
        offs = None
    handles = {"Translation": (int(f.get("Translation Handle", 0xFFFFFFFF)), 3),
               "Rotation": (int(f.get("Rotation Handle", 0xFFFFFFFF)), 4),
               "Scale": (int(f.get("Scale Handle", 0xFFFFFFFF)), 1)}
    nkeys = max(2, min(480, int(round(dur * 15.0)) + 1))
    times = np.linspace(start, stop, nkeys)
    us = np.linspace(0.0, 1.0, nkeys)
    pos = rot = scl = None
    for chan, (h, dim) in handles.items():
        need = h + ncp * dim
        if h < 0 or h >= 0xFFFF or need > len(cps_src):
            stats["bspline_incomplete_" + chan] += 1
            continue
        cp = cps_src[h:h + ncp * dim].reshape(ncp, dim)
        if compact:
            off, half = offs[chan]
            if not np.isfinite(half) or half >= FLT_MAX:
                continue
            cp = off + cp * half / 32767.0
        vals = np.array([bspline_eval(cp, u) for u in us], np.float32)
        if chan == "Rotation":
            nrm = np.linalg.norm(vals, axis=1, keepdims=True)
            nrm[nrm == 0] = 1.0
            # nif quats are w,x,y,z -> glTF x,y,z,w
            vals = (vals / nrm)[:, [1, 2, 3, 0]]
            rot = vals
        elif chan == "Translation":
            pos = vals
        else:
            scl = vals
    if pos is None:
        pos = np.tile([base_t.get("x", 0.0), base_t.get("y", 0.0),
                       base_t.get("z", 0.0)], (nkeys, 1)).astype(np.float32)
    if rot is None:
        rot = np.tile([defq.get("x", 0.0), defq.get("y", 0.0),
                       defq.get("z", 0.0), defq.get("w", 1.0)],
                      (nkeys, 1)).astype(np.float32)
    if scl is None:
        scl = np.tile(base_s, (nkeys, 1)).astype(np.float32)
    return times.astype(np.float32), pos, rot, scl


def static_transform_channels(iblock):
    """Emit the interpolator's base Transform as 1-key constant channels."""
    tr = iblock.get("Transform")
    if not tr:
        return None
    t = tr.get("Translation") or {}
    q = tr.get("Rotation") or {}
    s = float(tr.get("Scale", 1.0))
    return {"rot": [(0.0, (q.get("x", 0.0), q.get("y", 0.0),
                           q.get("z", 0.0), q.get("w", 1.0)))],
            "pos": [(0.0, (t.get("x", 0.0), t.get("y", 0.0),
                           t.get("z", 0.0)))],
            "scl": [(0.0, s)],
            "times": [0.0]}


def transform_data_channels(nif, iblock, stats, warn):
    """Interpolator block -> (times, pos, rot, scl) numpy channels or None."""
    t = iblock.type
    if t == "NiTransformInterpolator":
        d = nif.block(iblock.get("Data", -1))
        if d is None or d.type != "NiTransformData":
            stats["interp_nodata"] += 1
            # constant pose kept in the interpolator's Transform field
            return static_transform_channels(iblock)
        tf = d.fields
        rot = []
        if tf.get("Num Rotation Keys", 0):
            if tf.get("Rotation Type", 1) == 4:
                rot = xyz_to_quat_keys(_lst(tf.get("XYZ Rotations")))
            else:
                for k in _lst(tf.get("Quaternion Keys")):
                    v = k.get("Value") or {}
                    rot.append((float(k.get("Time", 0.0)),
                                (v.get("x", 0.0), v.get("y", 0.0),
                                 v.get("z", 0.0), v.get("w", 1.0))))
        pos = keygroup_keys(tf.get("Translations"), "v3")
        scl = keygroup_keys(tf.get("Scales"), "f")
        times = sorted({k[0] for k in rot} | {k[0] for k in pos}
                       | {k[0] for k in scl})
        return {"rot": rot, "pos": pos, "scl": scl, "times": times}
    if t in ("NiBSplineCompTransformInterpolator",
             "NiBSplineTransformInterpolator"):
        r = sample_bspline_interp(nif, iblock, stats, warn)
        if r is None:
            stats["bspline_failed"] += 1
            return static_transform_channels(iblock)
        times, pos, rot, scl = r
        return {"rot": [(t_, tuple(q)) for t_, q in zip(times, rot)],
                "pos": [(t_, tuple(p)) for t_, p in zip(times, pos)],
                "scl": [(t_, float(s)) for t_, s in
                        zip(times, scl[:, 0] if scl.ndim > 1 else scl)],
                "times": list(times)}
    stats["interp_skip_" + t] += 1
    return None


# ---------------------------------------------------------------------------
# .kf -> glTF animations


def convert_kf(nif, stats):
    g = Gltf()
    warn = stats["_warnlist"]
    node_for_name = {}
    for b in nif.blocks:
        if b.type != "NiControllerSequence" or "_parse_error" in b.fields:
            continue
        seq = b.fields
        name = seq.get("Name") or "seq_%d" % b.index
        channels = []
        samplers = []
        used_targets = set()
        for cb in _lst(seq.get("Controlled Blocks")):
            node_name = cb.get("Node Name") or cb.get("Target Name") or ""
            ctype = cb.get("Controller Type") or ""
            interp = nif.block(cb.get("Interpolator", -1))
            if interp is None:
                ctrl = nif.block(cb.get("Controller", -1))
                if ctrl is not None:
                    interp = nif.block(ctrl.get("Interpolator", -1))
            if interp is None:
                stats["cb_nointerp"] += 1
                continue
            if ctype and ctype not in TRANSFORM_CTRL and \
                    "Transform" not in ctype:
                stats["cb_skip_" + ctype.split("Ni")[-1][:20] or "?"] += 1
                continue
            res = transform_data_channels(nif, interp, stats, warn)
            if res is None:
                continue
            if node_name not in node_for_name:
                node_for_name[node_name] = g.add_node(name=node_name or "node")
            gidx = node_for_name[node_name]
            for keys, path in ((res["pos"], "translation"),
                               (res["rot"], "rotation"),
                               (res["scl"], "scale")):
                if not keys:
                    continue
                times = np.asarray([k[0] for k in keys], np.float32)
                vals = np.asarray([k[1] for k in keys], np.float32)
                if path == "scale" and vals.ndim == 1:
                    vals = np.repeat(vals[:, None], 3, axis=1)
                si = g.add_accessor(times)
                so = g.add_accessor(vals)
                samplers.append({"input": si, "output": so,
                                 "interpolation": "LINEAR"})
                channels.append({"sampler": len(samplers) - 1,
                                 "target": {"node": gidx, "path": path}})
                used_targets.add(gidx)
        extras = {}
        tk = nif.block(seq.get("Text Keys", -1))
        if tk is not None and tk.type == "NiTextKeyExtraData":
            extras["text_keys"] = [
                {"time": float(k.get("Time", 0.0)),
                 "value": str(k.get("Value", ""))}
                for k in (_lst(tk.get("Text Keys")))]
        for k in ("Start Time", "Stop Time", "Frequency", "Cycle Type",
                  "Weight"):
            if k in seq:
                extras[k.lower().replace(" ", "_")] = seq[k]
        if channels:
            g.add_animation(name, channels, samplers, extras or None)
            stats["anims"] += 1
            stats["anim_channels"] += len(channels)
        else:
            stats["seq_empty"] += 1
    g.gltf["scenes"][0]["nodes"] = list(node_for_name.values()) or \
        [g.add_node(name="kf")]
    return g


# ---------------------------------------------------------------------------
# .kfm -> json manifest


def read_kfm(path):
    with open(path, "rb") as f:
        raw = f.read()
    nl = raw.find(b"\n")
    hdr = raw[:nl].decode("latin-1", "replace") if nl >= 0 else ""
    if not hdr.startswith(";Gamebryo KFM"):
        raise NifParseError("not a KFM: %r" % hdr[:40])
    cur = nl + 1
    major2 = raw[cur] if " 2." in hdr or " 3." in hdr else 0
    if major2:
        cur += 1

    def ss(c):
        n, = struct.unpack_from("<I", raw, c)
        s = raw[c + 4:c + 4 + n].decode("latin-1", "replace")
        return s, c + 4 + n

    nif_name, cur = ss(cur)
    try:
        master, c2 = ss(cur)
    except Exception:
        master, c2 = "", cur
    if len(master) > 255 or (master and not master.replace(
            "\\", "/").replace(" ", "_").isascii()):
        # stray byte(s) inside the nif-name string (seen in r00200.kfm)
        # -> resync by scanning for a plausible master record
        for shift in range(1, 8):
            try:
                m2, c3 = ss(cur + shift)
            except Exception:
                continue
            if 0 < len(m2) <= 255 and m2.isascii() and \
                    not m2.startswith(".\\"):
                master, c2 = m2, c3
                break
    cur = c2
    unk1, unk2, f1, f2, num_anims = struct.unpack_from("<IIffI", raw, cur)
    cur += 20
    def parse_anim(start_cur):
        """Parse one anim record at start_cur -> (dict, end_cur) or raise."""
        cur2 = start_cur
        event, = struct.unpack_from("<i", raw, cur2)
        cur2 += 4
        nm = ""
        if has_name:
            nm, cur2 = ss(cur2)
        kf, cur2 = ss(cur2)
        if not kf.endswith(".kf") and "\\" in kf and cur2 < len(raw) \
                and raw[cur2:cur2 + 1] == b"f":
            # corrupt file writes len-1 with the real 'f' as next byte
            kf += "f"
            cur2 += 1
        index, ntrans = struct.unpack_from("<ii", raw, cur2)
        cur2 += 8
        if index < 0 or index > 0x100000 or ntrans < 0 or ntrans > 1000:
            raise ValueError("implausible index/ntrans")
        if not kf.endswith(".kf") and "\\" in kf:
            raise ValueError("kf name truncated: %r" % kf)
        trans = []
        for _t in range(ntrans):
            ta, ty = struct.unpack_from("<ii", raw, cur2)
            cur2 += 8
            tr = {"anim": ta, "type": ty}
            if ty != 5:
                dur, nia = struct.unpack_from("<fi", raw, cur2)
                cur2 += 8
                tr["duration"] = dur
                inter = []
                for _i in range(min(nia, 1000)):
                    _u, = struct.unpack_from("<i", raw, cur2)
                    ev, c2 = ss(cur2 + 4)
                    cur2 = c2
                    inter.append(ev)
                tr["intermediate"] = inter
                ntk, = struct.unpack_from("<i", raw, cur2)
                cur2 += 4
                for _k in range(min(ntk, 1000)):
                    _tm, = struct.unpack_from("<f", raw, cur2)
                    _v, cur2 = ss(cur2 + 4)
            trans.append(tr)
        rec = {"event": event, "kf": kf, "index": index,
               "transitions": [t["anim"] for t in trans],
               "transition_detail": trans}
        return rec, cur2

    anims = []
    has_name = "1.2" in hdr  # Name field only in KFM <= 1.2.4b
    truncated = False
    for _ in range(min(num_anims, 100000)):
        parsed = None
        for shift in (0, 1, 2):
            try:
                rec, cur = parse_anim(cur + shift)
                parsed = rec
                break
            except Exception:
                continue
        if parsed is None:
            truncated = True
            break
        anims.append(parsed)
    out = {"kfm": hdr, "nif": nif_name, "master": master,
           "animations": anims}
    if truncated:
        out["truncated"] = True
    return out


# ---------------------------------------------------------------------------
# per-file driver


def out_path_for(src, kind):
    rel = os.path.relpath(src, RAW)
    if kind == "nif":
        return os.path.join(MODELS, os.path.splitext(rel)[0] + ".glb")
    if kind == "kfm":
        return os.path.join(ANIMS, rel + ".json")
    return os.path.join(ANIMS, os.path.splitext(rel)[0] + ".glb")


def _tex_prefix_for(src):
    rel = os.path.relpath(src, RAW).replace(os.sep, "/")
    marker = "/model/"
    if marker in rel:
        return rel.split(marker, 1)[0] + "/texture/"
    return ""


def convert_one(src, tex_index):
    """-> stats dict (json-serializable)."""
    st = Counter()
    warns = []
    st["_warnlist"] = warns
    kind = os.path.splitext(src)[1].lower().lstrip(".")
    out = out_path_for(src, kind)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    try:
        if kind == "kfm":
            data = read_kfm(src)
            # assets/ = liens physiques vers portage : délier avant d'écrire
            if os.path.lexists(out):
                os.unlink(out)
            with open(out, "w") as f:
                json.dump(data, f, indent=1)
            st["kfm_anims"] = len(data["animations"])
            st["files"] = 1
            st["status"] = "ok"
            return st
        rdr = Reader()
        nif = rdr.read(src)
        warns.extend(nif.warnings)
        for t, c in nif.skipped_types.items():
            st["skip_type_" + t] += c
        st["files"] = 1
        if kind == "kf":
            g = convert_kf(nif, st)
            g.write(out)
            st["status"] = "ok"
        else:
            glb_dir = os.path.dirname(out)
            sb = SceneBuilder(nif, tex_index, glb_dir,
                              _tex_prefix_for(src))
            g = sb.build()
            g.write(out)
            st.update(sb.stats)
            warns.extend(sb.warn)
            if np.isfinite(sb.bbox_min).all():
                st["bbox_min"] = [round(float(v), 3) for v in sb.bbox_min]
                st["bbox_max"] = [round(float(v), 3) for v in sb.bbox_max]
            st["status"] = "ok"
    except Exception as e:
        st["status"] = "fail:%s" % str(e)[:200]
        os.path.exists(out) and os.unlink(out)
    return st


# ---------------------------------------------------------------------------
# CLI / batch


def _worker_init(tex_dir):
    global _TEX, _RDR
    _TEX = TextureIndex(tex_dir)


def _worker(src):
    global _TEX
    return src, convert_one(src, _TEX)


def scan_sources():
    out = []
    for dp, _dn, fns in os.walk(RAW, followlinks=True):
        for fn in fns:
            ext = os.path.splitext(fn)[1].lower()
            if ext in (".nif", ".kf", ".kfm"):
                out.append(os.path.join(dp, fn))
    out.sort()
    return out


def main(argv=None):
    ap = argparse.ArgumentParser(description="NIF/KF/KFM -> GLB batch converter")
    ap.add_argument("--limit", type=int, default=0)
    ap.add_argument("--only", type=str, default="")
    ap.add_argument("--jobs", type=int, default=max(1, os.cpu_count() - 1))
    ap.add_argument("--out", type=str, default=REPORT)
    ap.add_argument("--no-resume", action="store_true")
    ap.add_argument("--list", type=str, default="",
                    help="fichier : NIF relatifs a RAW a (re)convertir "
                         "(force, ignore le resume)")
    args = ap.parse_args(argv)

    sources = scan_sources()
    if args.only:
        sources = [s for s in sources if args.only in s]
    if args.list:
        want = {l.strip().replace("\\", "/").lower()
                for l in open(args.list) if l.strip()}
        sources = [s for s in sources if os.path.relpath(s, RAW)
                   .replace(os.sep, "/").lower() in want]
        args.no_resume = True
    if args.limit:
        sources = sources[:args.limit]

    todo = []
    for s in sources:
        kind = os.path.splitext(s)[1].lower().lstrip(".")
        out = out_path_for(s, kind)
        if not args.no_resume and os.path.exists(out) and \
                os.path.getmtime(out) >= os.path.getmtime(s):
            continue
        todo.append(s)
    print("sources=%d todo=%d jobs=%d" % (len(sources), len(todo), args.jobs))

    t0 = time.time()
    agg = Counter()
    failures = []
    warn_counter = Counter()
    bboxes = []
    done = 0
    if args.jobs > 1 and todo:
        with cf.ProcessPoolExecutor(
                max_workers=args.jobs,
                initializer=_worker_init, initargs=(TEXTURES,)) as ex:
            for src, st in ex.map(_worker, todo, chunksize=16):
                done += 1
                _accumulate(agg, failures, warn_counter, src, st, bboxes)
                if done % 500 == 0:
                    print("  %d/%d (%.0fs)" % (done, len(todo), time.time() - t0))
    else:
        _worker_init(TEXTURES)
        for s in todo:
            src, st = _worker(s)
            done += 1
            _accumulate(agg, failures, warn_counter, src, st, bboxes)
            if done % 100 == 0:
                print("  %d/%d (%.0fs)" % (done, len(todo), time.time() - t0))

    write_report(args.out, agg, failures, warn_counter, bboxes,
                 time.time() - t0, len(todo))
    print("done in %.0fs -> %s" % (time.time() - t0, args.out))


def _accumulate(agg, failures, warn_counter, src, st, bboxes):
    status = st.pop("status", "?")
    warns = st.pop("_warnlist", [])
    bmin, bmax = st.pop("bbox_min", None), st.pop("bbox_max", None)
    if bmin and bmax and len(bboxes) < 30:
        bboxes.append((os.path.relpath(src, RAW), bmin, bmax))
    if status == "ok":
        agg["converted"] += 1
        agg["ext_" + os.path.splitext(src)[1].lower()] += 1
    else:
        agg["failed"] += 1
        failures.append((src, status))
    for w in warns:
        key = w.split(":")[0][:80]
        warn_counter[key] += 1
    for k, v in st.items():
        if isinstance(v, int):
            agg[k] += v


def write_report(path, agg, failures, warn_counter, bboxes, elapsed, nfiles):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    lines = []
    lines.append("nif2gltf report — %s" % time.strftime("%Y-%m-%d %H:%M:%S"))
    lines.append("files processed: %d  converted: %d  failed: %d  (%.0fs)"
                 % (nfiles, agg["converted"], agg["failed"], elapsed))
    for ext in sorted(k for k in agg if k.startswith("ext_")):
        lines.append("  %s : %d" % (ext, agg[ext]))
    lines.append("")
    lines.append("== measured bounding boxes (NIF units, Z-up) ==")
    for s, bmin, bmax in bboxes:
        size = [round(bmax[i] - bmin[i], 3) for i in range(3)]
        lines.append("  %-60s min=%s size=%s" % (s, bmin, size))
    lines.append("")
    interesting = ["meshes", "verts", "tris", "nodes", "skins",
                   "multi_parent_dups",
                   "skinned_meshes", "uv_sets", "vcolor_meshes",
                   "alpha_materials", "missing_textures", "anims",
                   "anim_channels", "kfm_anims", "billboards", "psys_nodes",
                   "weights_gt4", "unskinned_verts", "seq_empty",
                   "bspline_failed"]
    lines.append("== counters ==")
    for k in interesting:
        if agg.get(k):
            lines.append("  %-24s %d" % (k, agg[k]))
    lines.append("")
    sk = [(k, v) for k, v in agg.items() if k.startswith("skip_type_")]
    if sk:
        lines.append("== skipped block types (unparsed) ==")
        for k, v in sorted(sk, key=lambda p: -p[1])[:40]:
            lines.append("  %-50s %d" % (k[10:], v))
    isk = [(k, v) for k, v in agg.items()
           if k.startswith(("interp_", "cb_", "bspline_", "prop_",
                            "tex_", "extra_"))]
    if isk:
        lines.append("== other skip counters ==")
        for k, v in sorted(isk, key=lambda p: -p[1])[:40]:
            lines.append("  %-50s %d" % (k, v))
    lines.append("")
    lines.append("== top warnings ==")
    for w, c in warn_counter.most_common(30):
        lines.append("  %6d  %s" % (c, w))
    lines.append("")
    lines.append("== failures ==")
    for s, e in failures[:200]:
        lines.append("  %s : %s" % (os.path.relpath(s, RAW), e))
    with open(path, "w") as f:
        f.write("\n".join(lines))


if __name__ == "__main__":
    main()
