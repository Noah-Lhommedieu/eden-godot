#!/usr/bin/env python3
"""Génère ``godot_project/gamedata/ui_text_en.tsv`` : dictionnaire
« chaîne chinoise exacte » → « anglais » pour les propriétés
``Text``/``Tooltip`` des ``.layout`` CEGUI de ``raw_assets/uidata``.

Sources de traduction, dans l'ordre de priorité :

  1. ``tools/ui_text_overrides.tsv``  — dictionnaire manuel curé.
  2. Alignement par id des tables textindex :
     ``data/db/c_textindex.ini`` (Big5) ↔ ``t_textindex.ini`` (EN
     Reforged) puis ``_T_TextIndex.ini`` (EN Aeria officiel) en
     repli — le client natif remplace les textes de layout par ces
     index depuis le code fenêtre ; on reproduit l'effet par
     correspondance de contenu.
  3. Dépréfixage des marqueurs d'exemple concepteurs (``x``/``X``/
     ``Test``/``String``/``暫代``) puis nouveau lookup.

Tout texte encore non couvert est reporté sur stderr (sortie
``--miss``) : le runtime gardera la valeur brute, le texte reste
chinois mais la fenêtre reste fonctionnelle.

Usage : ``tools/gen_ui_text.py [--raw PATH] [--out PATH] [--miss]``
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OVERRIDES = Path(__file__).resolve().parent / "ui_text_overrides.tsv"

TEXT_RX = re.compile(
    r'<Property\s+Name="(Text|Tooltip)"\s+Value="([^"]*)"', re.S)

# Marqueurs que les concepteurs X-Legend collent devant les textes
# d'exemple des cellules gabarit (jamais destinés à l'affichage réel,
# mais traduits quand même pour rester EN-only).
SAMPLE_PREFIXES = ("x", "X", "Test", "test", "String", "string", "暫代")


WS_RX = re.compile(r"\s+")


def norm_key(s: str) -> str:
    """Clé de lookup canonique : toute suite d'espaces Unicode
    (\\n, \\r, \\t, \\x0b, \\u3000 fullwidth…) → une espace simple,
    trim en bord. La même canonisation est appliquée côté client
    (``UiTextEn.Canon``) — le chinois n'a pas d'espaces
    significatifs, c'est sans risque."""
    return WS_RX.sub(" ", s).strip()


def load_ini_fields(path: Path, enc: str) -> dict[int, list[str]]:
    """Charge un ini pipe-delimité ``id|col1|col2|…`` → {id: [cols]}."""
    rows: dict[int, list[str]] = {}
    try:
        for line in path.read_text(encoding=enc, errors="replace").splitlines():
            parts = line.split("|")
            if len(parts) >= 2 and parts[0].isdigit():
                rows[int(parts[0])] = parts[1:]
    except OSError:
        pass
    return rows


def build_table_map(raw: Path, extra_table: Path | None = None
                    ) -> dict[str, str]:
    """c_textindex(Big5) → EN via les variantes T_ par id partagé.
    ``extra_table`` (ex. t_textindex FR) est consultée en premier —
    les ids absents retombent sur les tables EN."""
    src = load_ini_fields(raw / "data/db/c_textindex.ini", "big5")
    # Ordre : t_ = traduction Reforged (termes « Quest »), _T_ = US.
    en_tables = []
    if extra_table is not None:
        en_tables.append(load_ini_fields(extra_table, "utf-8"))
    en_tables += [
        load_ini_fields(raw / "data/db/t_textindex.ini", "utf-8"),
        load_ini_fields(raw / "data/db/_T_TextIndex.ini", "utf-8"),
    ]
    out: dict[str, str] = {}
    for idx, cols in src.items():
        en: str | None = None
        for table in en_tables:
            row = table.get(idx)
            if row and row[0]:
                en = row[0]
                break
        if en is None:
            continue
        for zh in cols:
            zh = norm_key(zh)
            if zh and zh not in out:
                out[zh] = en
    return out


def load_overrides(path: Path) -> dict[str, str]:
    out: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#") or "\t" not in line:
            continue
        zh, en = line.split("\t", 1)
        out[norm_key(zh)] = en.replace("\\n", "\n")
    return out


def layout_strings(layout_dir: Path) -> dict[str, set[str]]:
    """{valeur_non_ascii: {fichiers}} pour Text + Tooltip."""
    found: dict[str, set[str]] = {}
    for f in sorted(layout_dir.glob("*.layout")):
        try:
            raw = f.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        for m in TEXT_RX.finditer(raw):
            val = m.group(2)
            if any(ord(c) > 127 for c in val):
                found.setdefault(val, set()).add(f.name)
    return found


def deprefix(s: str) -> str | None:
    """Retire un marqueur d'exemple de tête (x/Test/String/暫代) —
    renvoie le reste ou None s'il n'y a plus rien d'utile."""
    for p in SAMPLE_PREFIXES:
        if s.startswith(p) and len(s) > len(p):
            rest = s[len(p):]
            if any(ord(c) > 127 for c in rest):
                return rest
    return None


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--raw", default=str(ROOT / "raw_assets"))
    ap.add_argument("--overrides", default=str(OVERRIDES))
    ap.add_argument(
        "--table", default=None,
        help="table textindex prioritaire (ex. FR) — repli sur les tables EN")
    ap.add_argument(
        "--out", default=str(ROOT / "godot_project/gamedata/ui_text_en.tsv"))
    ap.add_argument("--miss", action="store_true",
                    help="liste les chaînes non traduites sur stderr")
    args = ap.parse_args()

    raw = Path(args.raw)
    table_map = build_table_map(
        raw, Path(args.table) if args.table else None)
    overrides = load_overrides(Path(args.overrides))
    strings = layout_strings(raw / "uidata/layouts")

    result: dict[str, str] = {}
    misses: list[tuple[str, set[str]]] = []
    for val, files in strings.items():
        key = norm_key(val)
        en = overrides.get(key) or table_map.get(key)
        if args.table:
            en = table_map.get(key) or overrides.get(key)
        if en is None and (rest := deprefix(key)) is not None:
            en = overrides.get(rest) or table_map.get(rest)
            if args.table:
                en = table_map.get(rest) or overrides.get(rest)
        if en is None:
            misses.append((val, files))
            continue
        # Le TSV est indexé par la forme canonique — le client
        # canonise la propriété de la même façon au lookup.
        result.setdefault(key, en)

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    with out.open("w", encoding="utf-8") as fh:
        fh.write("# ui_text_en.tsv — généré par tools/gen_ui_text.py ;"
                 " ne pas éditer à la main\n")
        fh.write("# clé canonique (espaces Unicode → 1 espace, trim)"
                 "\ttraduction anglaise\n")
        for zh in sorted(result, key=lambda s: s.encode("utf-8")):
            en = result[zh]
            fh.write(f"{zh.replace(chr(9),' ').replace(chr(10),'\\\\n')}"
                     f"\t{en.replace(chr(9),' ').replace(chr(10),'\\\\n')}\n")

    covered_pct = 100 * len(result) / max(1, len(strings))
    print(f"layouts strings: {len(strings)}  translated: {len(result)}"
          f"  ({covered_pct:.1f}%)  -> {out}")
    if misses:
        print(f"untranslated: {len(misses)}", file=sys.stderr)
        if args.miss:
            for val, files in misses:
                print(f"  {val!r}   [{', '.join(sorted(files))}]",
                      file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
