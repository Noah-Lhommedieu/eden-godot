"""Generic Gamebryo NIF reader driven by tools/nif2gltf/nif.xml.

Handles the 20.x non-Bethesda stream layout:

    header line | u32 version | u8 endian | u32 user_version | u32 num_blocks
    u16 num_block_types | SizedString types | u16[num] block type index
    u32[num] block sizes | u32 num_strings | u32 max_len | SizedString strings
    u32 num_groups | u32[num] groups | block data ... | u32 num_roots | i32 roots

Block bodies are decoded field-by-field per nif.xml; unknown block types are
skipped via the declared block size, so the reader never loses sync.
"""

from __future__ import annotations

import struct
import numpy as np

from . import nif_spec
from .nif_spec import ver_to_int

# basic name -> (struct format char, size)
BASIC_FMT = {
    "uint": ("<I", 4), "int": ("<i", 4), "ulittle32": ("<I", 4),
    "ushort": ("<H", 2), "short": ("<h", 2),
    "byte": ("<B", 1), "sbyte": ("<b", 1), "char": ("<b", 1),
    "normbyte": ("<B", 1), "bool": ("<B", 1),
    "float": ("<f", 4), "hfloat": ("<e", 2),
    "uint64": ("<Q", 8), "int64": ("<q", 8),
    "BlockTypeIndex": ("<H", 2), "FileVersion": ("<I", 4),
    "Ptr": ("<i", 4), "Ref": ("<i", 4),
    "StringOffset": ("<I", 4), "NiFixedString": ("<I", 4),
    "StringIndex": ("<i", 4),
}
NP_FMT = {k: v[0] for k, v in BASIC_FMT.items()}

# types that decode to a string (via header string table when idx based)
_IDX_STRING_TYPES = ("string", "FilePath", "NiFixedString", "StringOffset")
_SIZED_STRING_TYPES = ("SizedString", "NiString")


class NifParseError(Exception):
    pass


class Block:
    __slots__ = ("type", "index", "fields", "size")

    def __init__(self, type_, index, fields, size):
        self.type = type_
        self.index = index
        self.fields = fields or {}
        self.size = size

    def get(self, name, default=None):
        return self.fields.get(name, default)


class NifFile:
    def __init__(self):
        self.version = 0
        self.user_version = 0
        self.strings = []
        self.blocks = []          # list[Block]
        self.roots = []
        self.warnings = []
        self.skipped_types = {}
        self.header_str = ""

    def block(self, ref):
        if ref is None or ref < 0 or ref >= len(self.blocks):
            return None
        return self.blocks[ref]

    def name_of(self, block_index):
        b = self.block(block_index)
        if b is None:
            return ""
        n = b.fields.get("Name", "")
        return n if isinstance(n, str) else ""


def _fixed_dtype(spec, t, ver, user, bsver, cache={}):
    """numpy dtype for a fixed-layout type, or None."""
    key = (t, ver)
    if key in cache:
        return cache[key]
    dt = None
    if t in NP_FMT:
        dt = np.dtype(NP_FMT[t])
    elif t in spec.storage:
        st = spec.storage[t]
        if st in NP_FMT:
            dt = np.dtype(NP_FMT[st])
    elif t in spec.structs and spec.is_fixed_struct(t, ver, user, bsver):
        sub = []
        for f in spec.fields_for(t, ver, user, bsver):
            sdt = _fixed_dtype(spec, f.type, ver, user, bsver)
            if sdt is None:
                break
            sub.append((f.name, sdt))
        else:
            dt = np.dtype(sub)
    cache[key] = dt
    return dt


class _Cursor:
    __slots__ = ("buf", "pos", "end")

    def __init__(self, buf, pos, end):
        self.buf = buf
        self.pos = pos
        self.end = end

    def take(self, n):
        p = self.pos
        if p + n > self.end:
            raise NifParseError("overrun %d > %d" % (p + n, self.end))
        self.pos = p + n
        return p

    def u32(self):
        p = self.take(4)
        return struct.unpack_from("<I", self.buf, p)[0]

    def i32(self):
        p = self.take(4)
        return struct.unpack_from("<i", self.buf, p)[0]


class Reader:
    def __init__(self, spec=None):
        self.spec = spec or nif_spec.get_spec()
        self.nif = None

    # -- strings ---------------------------------------------------------------

    def _sized_string(self, cur):
        n = cur.u32()
        if n > 1 << 22:
            raise NifParseError("string too long %d" % n)
        p = cur.take(n)
        return self.nif._raw[p:p + n].decode("latin-1", "replace")

    def _idx_string(self, cur):
        i = cur.u32()
        if i == 0xFFFFFFFF or i >= len(self.nif.strings):
            return ""
        return self.nif.strings[i]

    # -- generic field reading -------------------------------------------------

    def _env_names(self, fields):
        return self._names_cache(fields)

    def _names_cache(self, fields):
        key = id(fields)
        r = self._names_dict.get(key)
        if r is None:
            r = self.spec.field_names(fields)
            self._names_dict[key] = r
        return r

    def _eval(self, expr, env):
        names = frozenset(env.keys()) | _BUILTIN_NAMES
        return nif_spec.eval_expr(self.spec, expr, env,
                                  self.nif.version, self.nif.user_version, 0,
                                  arg=env.get("ARG", 0), t=env.get("T", ""),
                                  names=names)

    def read_struct(self, tname, cur, env_parent, template=None, arg=None):
        """Read a struct; returns dict of field values."""
        spec, nif = self.spec, self.nif
        fields = spec.fields_for(tname, nif.version, nif.user_version, 0,
                                 concrete=env_parent.get("T0", tname))
        env = dict(env_parent)
        if arg is not None:
            env["ARG"] = arg
        if template is not None:
            env["T"] = template
        names = self.spec.field_names(fields) | frozenset(env.keys()) | _BUILTIN_NAMES
        for f in fields:
            if f.cond:
                try:
                    if not nif_spec.eval_expr(spec, f.cond, env, nif.version,
                                              nif.user_version, 0,
                                              arg=env.get("ARG", 0),
                                              t=env.get("T", ""), names=names):
                        continue
                except Exception:
                    continue
            val = self.read_typed(f, cur, env, names)
            env[f.name] = val
        return env

    def read_typed(self, f, cur, env, names):
        spec, nif = self.spec, self.nif
        n = 1
        width = None
        is_arr = f.length is not None
        if is_arr:
            try:
                n = int(nif_spec.eval_expr(spec, f.length, env, nif.version,
                                           nif.user_version, 0,
                                           arg=env.get("ARG", 0),
                                           t=env.get("T", ""), names=names))
            except Exception:
                n = 0
            if n < 0 or n > 1 << 24:
                raise NifParseError("bad length %d for %s" % (n, f.name))
        if f.width is not None:
            try:
                width = nif_spec.eval_expr(spec, f.width, env, nif.version,
                                           nif.user_version, 0,
                                           arg=env.get("ARG", 0),
                                           t=env.get("T", ""), names=names)
            except Exception:
                width = None
        # generic resolution
        t = f.type
        farg = f.arg
        ftemplate = f.template
        if farg is not None:
            try:
                farg = int(nif_spec.eval_expr(spec, farg, env, nif.version,
                                              nif.user_version, 0,
                                              arg=env.get("ARG", 0),
                                              t=env.get("T", ""), names=names))
            except Exception:
                farg = 0
        if ftemplate == "#T#":
            ftemplate = env.get("T", "float")
        if t == "#T#":
            t = env.get("T", "float")

        # fast vectorized path: flat array of fixed-size elements, no width
        if is_arr and width is None:
            dt = _fixed_dtype(spec, t, nif.version, nif.user_version, 0)
            if dt is not None and not _needs_env(f, env):
                size = dt.itemsize * n
                p = cur.take(size)
                arr = np.frombuffer(nif._raw, dtype=dt, count=n, offset=p)
                return arr

        if width is None:
            if is_arr:
                return [self.read_one(t, ftemplate, farg, cur, env, f)
                        for _ in range(n)]
            return self.read_one(t, ftemplate, farg, cur, env, f)

        # 2D array: n rows of width elements (width scalar or per-row list)
        out = []
        for i in range(n):
            w = width[i] if isinstance(width, (list, tuple, np.ndarray)) else width
            w = int(w)
            dt = _fixed_dtype(spec, t, nif.version, nif.user_version, 0)
            if dt is not None:
                p = cur.take(dt.itemsize * w)
                out.append(np.frombuffer(nif._raw, dtype=dt, count=w, offset=p))
            else:
                out.append([self.read_one(t, ftemplate, farg, cur, env, f)
                            for _ in range(w)])
        return out

    def read_one(self, t, template, arg, cur, env, field=None):
        spec, nif = self.spec, self.nif
        ver = nif.version
        # index-based strings (>= 20.1.0.3) vs sized strings
        if t in _IDX_STRING_TYPES:
            return self._idx_string(cur) if ver >= 0x14010003 else ""
        if t == "string" or t == "FilePath":
            return self._sized_string(cur) if ver < 0x14010003 else self._idx_string(cur)
        if t in _SIZED_STRING_TYPES:
            return self._sized_string(cur)
        if t == "LineString":
            p = cur.pos
            end = nif._raw.find(b"\n", p, cur.end)
            if end < 0:
                end = cur.end
            cur.pos = end + 1
            return nif._raw[p:end].decode("latin-1", "replace")
        if t in ("Ref", "Ptr"):
            return cur.i32()
        if t in BASIC_FMT:
            fmt, size = BASIC_FMT[t]
            p = cur.take(size)
            return struct.unpack_from(fmt, nif._raw, p)[0]
        if t in spec.storage:
            st = spec.storage[t]
            fmt, size = BASIC_FMT[st]
            p = cur.take(size)
            return struct.unpack_from(fmt, nif._raw, p)[0]
        if t in spec.structs or t in spec.niobj_fields:
            return self.read_struct(t, cur, env, template=template, arg=arg)
        raise NifParseError("unknown type %s" % t)

    # -- whole file --------------------------------------------------------------

    def _hash_to_name(self):
        """DJB1 (seed 0) -> type name table for 20.3.1.2 hash-typed files."""
        if getattr(self, "_h2n", None) is None:
            def djb1(s):
                h = 0
                for c in s.encode():
                    h = (h * 33 + c) & 0xFFFFFFFF
                return h
            h2n = {}
            for n in list(self.spec.niobj_fields) + list(self.spec.structs):
                h2n.setdefault(djb1(n), n)
            self._h2n = h2n
        return self._h2n

    def read(self, path_or_bytes) -> NifFile:
        nif = NifFile()
        self.nif = nif
        self._names_dict = {}
        if isinstance(path_or_bytes, (bytes, bytearray)):
            raw = bytes(path_or_bytes)
        else:
            with open(path_or_bytes, "rb") as fh:
                raw = fh.read()
        nif._raw = raw
        nl = raw.find(b"\n")
        if nl < 0:
            raise NifParseError("no header line")
        nif.header_str = raw[:nl].decode("latin-1", "replace")
        if "File Format" not in nif.header_str:
            raise NifParseError("not a NIF: %r" % nif.header_str[:60])
        off = nl + 1
        ver, endian, user, num_blocks = struct.unpack_from("<IBII", raw, off)
        off += 13
        if endian != 1:
            raise NifParseError("big-endian file unsupported")
        nif.version = ver
        nif.user_version = user
        ntypes, = struct.unpack_from("<H", raw, off)
        off += 2
        types = []
        if ver == 0x14030102:
            # 20.3.1.2 (Aura Kingdom): block types are DJB1 hashes, no names
            hashes = struct.unpack_from("<%dI" % ntypes, raw, off)
            off += 4 * ntypes
            h2n = self._hash_to_name()
            for h in hashes:
                types.append(h2n.get(h, "Hash#%08x" % h))
        else:
            for _ in range(ntypes):
                ln, = struct.unpack_from("<I", raw, off)
                off += 4
                if ln > 256:
                    raise NifParseError(
                        "unsupported header layout (ver %#x, packed variant?)"
                        % ver)
                types.append(raw[off:off + ln].decode("latin-1", "replace"))
                off += ln
        bidx = struct.unpack_from("<%dH" % num_blocks, raw, off)
        off += 2 * num_blocks
        # "Block Size" array only exists since 20.2.0.5; older files must
        # be parsed sequentially (next block starts where parser stopped)
        if ver >= 0x14020005:
            sizes = list(struct.unpack_from("<%dI" % num_blocks, raw, off))
            off += 4 * num_blocks
        else:
            sizes = [None] * num_blocks
        # string table
        if ver >= 0x14010001:
            nstr, _maxlen = struct.unpack_from("<II", raw, off)
            off += 8
            cur = _Cursor(raw, off, len(raw))
            for _ in range(nstr):
                nif.strings.append(self._sized_string(cur))
            off = cur.pos
        if ver >= 0x05000006:
            ngroups, = struct.unpack_from("<I", raw, off)
            off += 4 + 4 * ngroups
        # blocks
        for i in range(num_blocks):
            tname = types[bidx[i]] if bidx[i] < len(types) else "?"
            size = sizes[i]
            blk_start = off
            if size is not None:
                blk_end = min(off + size, len(raw))
            else:
                blk_end = len(raw)
            fields = {}
            parsed = False
            if tname in self.spec.niobj_fields or tname in self.spec.structs:
                cur = _Cursor(raw, blk_start, blk_end)
                try:
                    fields = self.read_struct(tname, cur,
                                              {"T0": tname, "ARG": 0, "T": ""})
                    parsed = True
                    if size is not None and cur.pos != blk_end:
                        nif.warnings.append(
                            "block %d %s consumed %d/%d bytes" % (
                                i, tname, cur.pos - blk_start, size))
                except Exception as e:
                    fields = {"_parse_error": str(e)}
                    nif.warnings.append("block %d %s parse failed: %s" % (i, tname, e))
            else:
                nif.skipped_types[tname] = nif.skipped_types.get(tname, 0) + 1
            nif.blocks.append(Block(tname, i, fields, size))
            if size is not None:
                off = blk_start + size
            else:
                # sequential parse: continue from where the parser stopped
                if not parsed or cur.pos <= blk_start:
                    nif.warnings.append(
                        "block %d %s: cannot resync without block sizes, "
                        "stopping at %d/%d blocks" % (
                            i, tname, i + 1, num_blocks))
                    break
                off = cur.pos
        # footer roots
        if off + 4 <= len(raw):
            nr, = struct.unpack_from("<I", raw, off)
            off += 4
            if 0 <= nr <= 64 and off + 4 * nr <= len(raw):
                nif.roots = list(struct.unpack_from("<%di" % nr, raw, off))
        return nif


_BUILTIN_NAMES = frozenset(nif_spec.RESERVED)


def _needs_env(f, env):
    return False
