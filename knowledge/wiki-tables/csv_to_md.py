#!/usr/bin/env python3
"""Convertit les CSV du wiki Vendetta en tableaux markdown.

Chaque CSV est une grille de tableur (souvent plusieurs tableaux côte à
côte). La conversion est volontairement fidèle : on découpe en blocs sur
les lignes vides, on rend chaque bloc en tableau markdown (1re ligne =
en-tête), les lignes à une seule cellule deviennent des séparateurs en
gras, et les colonnes entièrement vides d'un bloc sont omises. Aucune
donnée n'est perdue.
"""
import csv
import json
import re
from pathlib import Path

HERE = Path(__file__).resolve().parent
SRC = HERE / "src_csv"

PREFIX_DIRS = {
    "BM_": "BM", "C_": "C", "D_": "D", "G_": "G",
    "M_": "M", "R_": "R", "T_": "T",
}

# Marquage périmètre serveur (contenu de base = niv. 90 max).
SCOPE = {
    "C_ Lv95 Awaken Sets.csv": ("out", "Sets éveillés niv. 95"),
    "C_ Lv100 Awaken Weps..csv": ("out", "Armes éveillées niv. 100"),
    "C_ Lv110 Awaken Sets.csv": ("out", "Sets éveillés niv. 110"),
    "D_ Abyss.csv": ("out", "Donjon Vendetta niv. ~115"),
    "D_ Deep Abyss.csv": ("out", "Donjon Vendetta niv. ~125"),
    "D_ Celestial Corridor.csv": ("out", "Donjon Vendetta niv. ~117"),
    "D_ Celestial Corridor II.csv": ("out", "Donjon Vendetta niv. ~127"),
    "D_ Haven of Oblivion.csv": ("out", "Donjon endgame Vendetta (HoO, niv. 95+)"),
    "D_ Lv98 GoP.csv": ("out", "Gate of Pandemonium niv. 98"),
    "D_ Lv110 Dragonridge Sanctuary.csv": ("out", "Donjon niv. 110"),
    "D_ Trials Lv95.csv": ("out", "Trials niv. 95"),
    "D_ Trials Lv100.csv": ("out", "Trials niv. 100"),
    "D_ Trials Lv110.csv": ("out", "Trials niv. 110"),
    "D_ Trials Lv130.csv": ("out", "Trials niv. 130"),
    "D_ Viridian Gorge.csv": ("out", "Donjon niv. 115"),
    "BM_ Demonic Valley.csv": ("out", "Battle map niv. 100+"),
    "BM_ Dimension of Souls.csv": ("out", "Battle map niv. 95"),
    "BM_ Dragonside Ridge.csv": ("out", "Battle map niv. 100+"),
    "G_Arcane Box Lv95_105_110.csv": ("out", "Gemmes Arcane Box niv. 95/105/110"),
    "D_ Dream of Destruction 90 & 95.csv": ("partial",
        "Section « DREAM OF DESTRUCTION 90 » dans le périmètre ; « 95 » hors périmètre"),
    "D_ Guardian Colosseum.csv": ("partial",
        "Brackets niv. 80/85/90 dans le périmètre ; 95/100 hors périmètre"),
    "D_ Elemental's Battlefield.csv": ("uncertain",
        "Contenu custom Vendetta, niveau non indiqué dans la feuille"),
    "M_ Honor Medals & CCM.csv": ("partial",
        "Farm par carte/classe : cartes niv. 92-102 hors périmètre, le reste (niv. 56-85) dans le périmètre"),
    "M_ Holy Skills.csv": ("partial",
        "Holy Skills niv. 90 dans le périmètre ; notes d'usage niv. 95/100+ hors périmètre"),
    "T_ Trophies.csv": ("partial",
        "Liste de référence niv. 1-123 : les trophées de zones niv. >90 sont hors périmètre"),
}

BANNERS = {
    "out": "> ⚠️ **Hors périmètre (lvl>90)** : {why}. Données conservées pour référence.",
    "partial": "> ⚠️ **Partiellement hors périmètre** : {why}.",
    "uncertain": "> ❓ **Niveau à vérifier** : {why}.",
}


def group_of(name: str) -> str:
    for p, d in PREFIX_DIRS.items():
        if name.startswith(p):
            return d
    return "misc"


def slugify(name: str) -> str:
    """'C_ Lv65-75 Weps' -> 'Lv65-75_Weps'"""
    for p in PREFIX_DIRS:
        if name.startswith(p):
            name = name[len(p):]
    name = name.strip().replace("&", " and ")
    name = re.sub(r"[^A-Za-z0-9.\-]+", "_", name).strip("._")
    return re.sub(r"_+", "_", name)


def esc(cell: str) -> str:
    return (cell.replace("\r\n", "\n").replace("\r", " ")
                .replace("\n", "<br>").replace("|", "\\|").strip())


def render(rows: list[list[str]]) -> list[str]:
    """Rend la grille en markdown : blocs séparés par lignes vides,
    lignes mono-cellule = séparateurs en gras."""
    out, block = [], []

    def flush():
        if not block:
            return
        # colonnes utilisées dans ce bloc
        n = max(len(r) for r in block)
        keep = [c for c in range(n)
                if any(c < len(r) and r[c].strip() for r in block)]
        if not keep:
            block.clear()
            return
        cells = [[esc(r[c]) if c < len(r) else "" for c in keep] for r in block]
        out.append("| " + " | ".join(cells[0]) + " |")
        out.append("|" + "---|" * len(keep))
        for r in cells[1:]:
            out.append("| " + " | ".join(r) + " |")
        out.append("")
        block.clear()

    for row in rows:
        nonempty = [(i, c) for i, c in enumerate(row) if c.strip()]
        if not nonempty:
            flush()
            continue
        # titre de section = cellule unique en colonne 0 ;
        # ailleurs c'est une ligne de continuation -> reste dans la table
        if len(nonempty) == 1 and nonempty[0][0] == 0:
            flush()
            out.append(f"**{esc(nonempty[0][1])}**")
            out.append("")
            continue
        block.append(row)
    flush()
    return out


def main():
    manifest = []
    for csv_path in sorted(SRC.glob("*.csv")):
        name = csv_path.name
        group = group_of(name)
        title = csv_path.stem
        for p in PREFIX_DIRS:
            if title.startswith(p):
                title = title[len(p):].strip()
        md_rel = f"{group}/{slugify(csv_path.stem)}.md"
        md_path = HERE / md_rel
        md_path.parent.mkdir(parents=True, exist_ok=True)

        with csv_path.open(newline="", encoding="utf-8-sig") as f:
            rows = list(csv.reader(f))

        lines = [f"# {title}", "",
                 f"> Source : `src_csv/{name}` — feuille du classeur "
                 "`Eden_Eternal_Vendetta_Wiki.xlsx`."]
        if name in SCOPE:
            kind, why = SCOPE[name]
            lines.append(BANNERS[kind].format(why=why))
        lines.append("")
        lines += render(rows)
        md_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        manifest.append({"csv": name, "md": md_rel,
                         "scope": SCOPE.get(name, ("in", ""))[0]})
        print(f"{name:45s} -> {md_rel}")

    (HERE / "manifest.json").write_text(
        json.dumps(manifest, indent=1, ensure_ascii=False), encoding="utf-8")
    print(f"\n{len(manifest)} fichiers convertis.")


if __name__ == "__main__":
    main()
