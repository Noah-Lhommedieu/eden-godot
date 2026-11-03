"""Parse nif.xml (niftools schema 0.10) into a field-level spec usable to
drive a generic NIF block reader.

Only what is needed for reading Gamebryo 20.x non-Bethesda streams is
implemented:

- basics (with fixed sizes), enums / bitfields / bitflags resolved to their
  storage type, structs (incl. generic Key/QuatKey/KeyGroup via #T#/#ARG#),
  niobjects with inherit chains.
- field filtering on ``since``/``until`` (ordered version list), ``vercond``
  (expression over VER/USER/BSVER), ``onlyT``/``excludeT`` (concrete type).
- ``cond``/``length``/``width``/``arg`` are compiled to Python expressions
  evaluated against the fields read so far.
"""

from __future__ import annotations

import re
import xml.etree.ElementTree as ET
from functools import lru_cache

# ---------------------------------------------------------------------------
# versions

def ver_to_int(s: str) -> int:
    """'20.2.0.8' -> 0x14020008 ; tolerates 1..4 components."""
    parts = [int(p, 16) if p.lower().startswith("0x") else int(p)
             for p in s.split(".")]
    while len(parts) < 4:
        parts.append(0)
    v = 0
    for p in parts[:4]:
        v = (v << 8) | (p & 0xFF)
    return v


def _vid_to_num(vid: str) -> str:
    """V20_2_0_7_FO3 -> '20.2.0.7' ; '20.2.0.7' stays as-is."""
    m = re.match(r"V(\d+(?:_\d+){0,3})", vid)
    if m:
        return m.group(1).replace("_", ".")
    return vid


class Field:
    __slots__ = ("name", "type", "template", "arg", "length", "width",
                 "cond", "since", "until", "vercond", "onlyT", "excludeT",
                 "default")

    def __init__(self, el):
        a = el.attrib
        self.name = a.get("name", "?")
        self.type = a.get("type", "uint")
        self.template = a.get("template")
        self.arg = a.get("arg")
        self.length = a.get("length")
        self.width = a.get("width")
        self.cond = a.get("cond")
        self.since = a.get("since")
        self.until = a.get("until")
        self.vercond = a.get("vercond")
        self.onlyT = a.get("onlyT")
        self.excludeT = a.get("excludeT")
        self.default = a.get("default")

    def applies(self, spec, ver: int, user: int, bsver: int, concrete: str) -> bool:
        if self.onlyT and self.onlyT != concrete:
            return False
        if self.excludeT and self.excludeT == concrete:
            return False
        if self.since and ver < spec.ver_floor(self.since):
            return False
        if self.until and ver > spec.ver_ceil(self.until):
            return False
        if self.vercond:
            try:
                if not spec.eval_vercond(self.vercond, ver, user, bsver):
                    return False
            except Exception:
                return False
        return True


class Spec:
    def __init__(self, xml_path: str):
        tree = ET.parse(xml_path)
        root = tree.getroot()

        self.basics = {}          # name -> size in bytes (None = variable)
        self.storage = {}         # enum/bitfield/bitflags name -> storage basic
        self.structs = {}         # name -> (fields, is_generic)
        self.niobj_fields = {}    # name -> [Field]
        self.niobj_inherit = {}   # name -> parent
        self.niobj_version = {}   # name -> (since_id, until_id, versions_ids)
        self.tokens = {}          # '#TOKEN#' -> expansion string
        self.versions = []        # ordered [(id, num_int, userset)]
        self.ver_by_id = {}
        self._field_cache = {}
        self._expr_cache = {}

        for el in root:
            tag = el.tag
            name = el.get("name")
            if tag == "token":
                for t in el:
                    self.tokens[t.get("token")] = t.get("string") or ""
            elif tag == "version":
                vid = el.get("id")
                num = el.get("num")
                vnum = ver_to_int(num) if num else 0
                users = set()
                for u in (el.get("user") or "").split():
                    try:
                        users.add(int(u, 0))
                    except ValueError:
                        pass
                bsv = set()
                for u in (el.get("bsver") or "").split():
                    try:
                        bsv.add(int(u, 0))
                    except ValueError:
                        pass
                self.versions.append((vid, vnum, users, bsv))
                self.ver_by_id[vid] = vnum
            elif tag == "basic":
                size = el.get("size")
                self.basics[name] = int(size) if size else None
            elif tag in ("enum", "bitfield", "bitflags"):
                self.storage[name] = el.get("storage", "uint")
            elif tag == "struct":
                generic = el.get("generic") == "true"
                self.structs[name] = ([Field(f) for f in el if f.tag == "field"], generic)
            elif tag == "niobject":
                self.niobj_fields[name] = [Field(f) for f in el if f.tag == "field"]
                self.niobj_inherit[name] = el.get("inherit")
                self.niobj_version[name] = (el.get("since"), el.get("until"), el.get("versions"))

        # convenience: version ordinals for since/until comparisons.
        # We compare on the *num* value; for ids we take the num of the entry.
        self._since_cache = {}

    # -- version helpers -----------------------------------------------------

    def ver_floor(self, token: str) -> int:
        """Minimal file version (as int) that satisfies 'since=token'."""
        v = self._since_cache.get(token)
        if v is not None:
            return v
        if token in self.ver_by_id:
            v = self.ver_by_id[token]
        elif token and token[0] == "V":
            v = ver_to_int(_vid_to_num(token))
        else:
            v = ver_to_int(token)
        self._since_cache[token] = v
        return v

    def ver_ceil(self, token: str) -> int:
        return self.ver_floor(token)

    # -- expression compilation ----------------------------------------------

    def _expand_tokens(self, s: str, depth: int = 0) -> str:
        """Replace #TOKEN# occurrences; verexpr tokens expand recursively."""
        if depth > 12:
            return s
        def repl(m):
            tok = "#" + m.group(1) + "#"
            if tok in ("#ARG#", "#T#", "#VER#", "#USER#", "#BSVER#"):
                return tok[1:-1]
            if tok == "#FLT_MIN#":
                return "-3.402823466e+38"
            mm = re.fullmatch(r"#LEN\[([^\]]+)\]#", tok)
            if mm:
                return 'LEN(%r)' % mm.group(1)
            mm = re.fullmatch(r"#LEN2\[([^\]]+)\]#", tok)
            if mm:
                return 'LEN2(%r)' % mm.group(1)
            val = self.tokens.get(tok)
            if val is None:
                return tok  # unknown, leave (eval will fail -> caller handles)
            if re.search(r"\bV\d", val) and not re.search(r"[#<>=&|+*/!()]", val):
                # a bare list of version ids (#BETHESDA# etc.) -> never true
                return "False"
            return self._expand_tokens(val, depth + 1)
        prev = None
        while prev != s:
            prev = s
            s = re.sub(r"#([A-Za-z0-9_\[\]]+)#", repl, s)
        return s

    def _compile(self, expr: str, names_key):
        """Compile a nif.xml expression to a python code object.

        names_key: frozenset of field names usable in the expression.
        """
        key = (expr, names_key)
        code = self._expr_cache.get(key)
        if code is not None:
            return code
        s = self._expand_tokens(expr)
        # ternary:  (A) #THEN# B #ELSE# C   (tokens already expanded to ? no)
        if " #THEN# " in s or "#THEN#" in s:
            m = re.split(r"\s*#ELSE#\s*", s, maxsplit=1)
            else_part = m[1] if len(m) > 1 else "0"
            cond_then = re.split(r"\s*#THEN#\s*", m[0], maxsplit=1)
            s = "((%s) if (%s) else (%s))" % (cond_then[1], cond_then[0], else_part)
        # V-tokens appearing as operands -> their num int
        s = re.sub(r"\bV(\d+(?:_\d+){0,3})(?:_[A-Za-z0-9_]+)?\b",
                   lambda m: str(ver_to_int(m.group(1).replace("_", "."))), s)
        # dotted versions -> int
        s = re.sub(r"\b(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})\b",
                   lambda m: str(ver_to_int(m.group(0))), s)
        s = s.replace("&&", " and ").replace("||", " or ")
        s = re.sub(r"!(?!=)", " not ", s)
        # integer division: nif exprs are integer; keep '/' then callers int()
        # field references -> N('name').  We match the LONGEST PascalCase
        # multi-word span (e.g. "BS Data Flags") in a single pass and wrap it
        # wholly: names not present in the current env resolve to 0 via the
        # tolerant N() lookup, so version-filtered fields still compile.
        s = _NAME_PAT.sub(_name_repl, s)
        s = re.sub(r"\btrue\b", "True", s)
        s = re.sub(r"\bfalse\b", "False", s)
        s = re.sub(r"\bINFINITY\b", "float('inf')", s)
        try:
            code = compile(s.strip(), "<nifexpr>", "eval")
        except SyntaxError:
            code = None
        self._expr_cache[key] = code
        return code

    def eval_vercond(self, vercond: str, ver: int, user: int, bsver: int) -> bool:
        code = self._compile(vercond, frozenset())
        if code is None:
            return False
        env = _EvalEnv({}, ver, user, bsver)
        return bool(eval(code, {"N": env.n, "LEN": env.l, "LEN2": env.l2,
                               "ARG": 0, "T": "", "VER": ver, "USER": user,
                               "BSVER": bsver}))

    # -- field lists -----------------------------------------------------------

    def type_kind(self, t: str):
        """'basic' | 'stored' (enum storage) | 'struct' | 'niobject' | 'generic_struct' | None"""
        if t in self.basics or t in ("string", "FilePath", "SizedString",
                                     "LineString", "NiString", "HeaderString",
                                     "ShortString", "NiFixedString",
                                     "StringOffset", "Ref", "Ptr", "bool"):
            return "basic" if t in self.basics else "special"
        if t in self.storage:
            return "stored"
        if t in self.structs:
            return "struct"
        return None

    def fields_for(self, type_name: str, ver: int, user: int, bsver: int,
                   concrete: str = None):
        """Ordered field list for a niobject or struct type."""
        concrete = concrete or type_name
        key = (type_name, ver, user, concrete)
        r = self._field_cache.get(key)
        if r is not None:
            return r
        fields = []
        # inheritance first (parents' fields come first in the stream)
        if type_name in self.niobj_fields:
            parent = self.niobj_inherit.get(type_name)
            if parent:
                fields.extend(self.fields_for(parent, ver, user, bsver, concrete))
            own = self.niobj_fields[type_name]
        elif type_name in self.structs:
            own = self.structs[type_name][0]
        else:
            own = []
        for f in own:
            if f.applies(self, ver, user, bsver, concrete):
                fields.append(f)
        self._field_cache[key] = fields
        return fields

    def field_names(self, fields) -> frozenset:
        return frozenset(f.name for f in fields)

    def is_fixed_struct(self, t: str, ver: int, user: int, bsver: int) -> bool:
        """True if struct type has a fixed byte layout (no conds/arrays/generics)."""
        if t not in self.structs:
            return False
        fields, generic = self.structs[t]
        if generic:
            return False
        for f in fields:
            if not f.applies(self, ver, user, bsver, t):
                continue
            if f.cond or f.length or f.width:
                return False
            k = self.type_kind(f.type)
            if k == "basic":
                if self.basics[f.type] is None:
                    return False
            elif k == "stored":
                continue
            elif k == "struct":
                if not self.is_fixed_struct(f.type, ver, user, bsver):
                    return False
            else:
                return False
        return True


class _EvalEnv:
    """Evaluation environment for compiled expressions."""

    __slots__ = ("fields", "ver", "user", "bsver", "arg", "t")

    def __init__(self, fields: dict, ver, user, bsver, arg=0, t=""):
        self.fields = fields
        self.ver = ver
        self.user = user
        self.bsver = bsver
        self.arg = arg
        self.t = t

    def n(self, name):
        v = self.fields.get(name, 0)
        if isinstance(v, dict):  # struct value used numerically -> bogus
            return 0
        return v

    def l(self, name):
        v = self.fields.get(name)
        try:
            return len(v)
        except TypeError:
            return 0

    def l2(self, name):
        v = self.fields.get(name)
        try:
            return sum(len(r) for r in v)
        except TypeError:
            return 0


RESERVED = {"ARG", "T", "VER", "USER", "BSVER", "LEN", "LEN2", "N",
            "INFINITY", "and", "or", "not", "if", "else", "True", "False",
            "None"}

_NAME_PAT = re.compile(
    r"(?<![A-Za-z0-9_'])"
    r"([A-Z][A-Za-z0-9_]*(?: +[A-Za-z0-9_]+)*)"
    r"(?![A-Za-z0-9_'])")


def _name_repl(m):
    name = m.group(1)
    if name in RESERVED:
        return name
    return "N(%r)" % name

_SPEC = None


def get_spec(path=None):
    global _SPEC
    if _SPEC is None:
        import os
        if path is None:
            path = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                "nif.xml")
        _SPEC = Spec(path)
    return _SPEC


def eval_expr(spec: Spec, expr: str, fields: dict, ver, user, bsver,
              arg=0, t="", names=None):
    code = spec._compile(expr, names or frozenset(fields.keys()))
    if code is None:
        raise ValueError("bad expr %r" % expr)
    env = _EvalEnv(fields, ver, user, bsver, arg, t)
    return eval(code, {"N": env.n, "LEN": env.l, "LEN2": env.l2,
                       "ARG": arg, "T": t, "VER": ver, "USER": user,
                       "BSVER": bsver})
