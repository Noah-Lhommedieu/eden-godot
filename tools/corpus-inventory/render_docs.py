#!/usr/bin/env python3
"""Génère docs/reference/corpus/*.md à partir de
artifacts/corpus-inventory/files.tsv (produit par inventory.py).

Un document par type + INDEX.md global. Usage :
  python3 tools/corpus-inventory/render_docs.py
"""
import os, re, collections
from xml.etree import ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CORPUS = os.path.join(ROOT, "corpus", "fr-full")
TSV = os.path.join(ROOT, "artifacts", "corpus-inventory", "files.tsv")
OUT = os.path.join(ROOT, "docs", "reference", "corpus")
REF = os.path.join(ROOT, "docs", "reference")

rows = [l.rstrip("\n").split("\t") for l in open(TSV, encoding="utf-8")][1:]
# colonnes : chemin, ext, categorie, sous_categorie, octets, meta, description

def tsv(path):
    lines = [l.rstrip("\n").split("\t") for l in open(path, encoding="utf-8")]
    return lines[0], lines[1:]

def maps_names():
    h, rs = tsv(os.path.join(REF, "maps.tsv"))
    i, n, l, k = h.index("code_client"), h.index("nom_fr"), h.index("niveaux"), h.index("kind")
    return {r[i].lower(): (r[n], r[l], r[k]) for r in rs if len(r) > k}

MAPS = maps_names()

def w(name, text):
    with open(os.path.join(OUT, name), "w", encoding="utf-8") as f:
        f.write(text)

def fmt_size(n):
    n = int(n)
    if n < 1024: return f"{n} o"
    if n < 1048576: return f"{n/1024:.0f} Ko"
    if n < 1073741824: return f"{n/1048576:.1f} Mo"
    return f"{n/1073741824:.2f} Go"

def ini_rows(path):
    try:
        with open(path, "rb") as f:
            lines = f.read().split(b"\n")
        n = len(lines) - 1
        first = lines[0].strip()
        if not (first.startswith(b"|") or first[:2] == b"V."
                or first.startswith(b"[") or first.startswith(b"//")
                or first.startswith(b"#") or first.startswith(b"<?xml")):
            n += 1  # pas de ligne d'en-tête : toutes les lignes sont des données
        return n
    except OSError:
        return "?"

DOCS = {}  # name -> (titre, nb fichiers) pour INDEX

# ================================================================ tables db
def doc_tables():
    db = [r for r in rows if r[2] == "table-données" and r[3] == "db"]
    # inclure aussi les ini de data/db (catégorie table-données, sous db)
    t = ["# Tables de données — `corpus/fr-full/data/db/`",
         "",
         f"{len(db)} fichiers `.ini` natifs. Conventions :",
         "",
         "- **`c_*` / `C_*` = tables client**, `S_*` = serveur, **`t_*` = textes FR**",
         "- Encodage : **Big5** partout sauf `t_*` en **CP1252** — un `cat` brut",
         "  affiche du mojibake ; parser via `IniTable`/`EdenEncoding` (code :",
         "  `scripts/data/`, `src/Eden.Core/Data/`).",
         "- En-tête `|V.x|…|` (format natif, ignoré par les parsers).",
         "- Les `t_*.ini` décrivent la table `c_*`/`C_*` homonyme (id → texte FR).",
         "- Détail item : `docs/reference/ITEMS.md` + `items.tsv` (26 736 lignes)",
         "  déjà joint c_item+t_item. Noms de maps : `maps.tsv`.",
         "",
         "| Fichier | Lignes | Rôle |",
         "|---|---|---|"]
    for r in sorted(db, key=lambda x: x[0].lower()):
        stem = os.path.basename(r[0])[:-4]
        n = ini_rows(os.path.join(CORPUS, r[0]))
        role = r[6].split(" — table")[0]
        t.append(f"| `{stem}` | {n} | {role} |")
    t += ["", "Voir aussi `data/scene/*.ini` (réglages par map) dans",
          "`scenes-et-cartes.md`."]
    w("tables-donnees.md", "\n".join(t) + "\n")
    DOCS["tables-donnees.md"] = ("Tables de données `data/db/` (141 INI, rôle de chacune)", len(db))

# ================================================================ scenes/maps
def doc_scenes():
    by_map = collections.defaultdict(set)
    other = []
    for r in rows:
        if r[0].startswith(("map/", "data/scene/")):
            m = re.search(r"[sS](\d{3})", r[0])
            if m:
                by_map[m.group(0).upper()].add(r[1])
                continue
            other.append(r)
        elif r[0].startswith("ui/map/") or r[0].startswith("UI/map/"):
            m = re.search(r"[sS](\d{3})", r[0])
            if m:
                by_map[m.group(0).upper()].add("carte-ui")
    t = ["# Scènes et cartes — `map/`, `data/scene/`, `ui/map/`",
         "",
         "Chaque map a un **code `S###`**. Ses fichiers :",
         "",
         "| Type | Où | Rôle |",
         "|---|---|---|",
         "| `s###.fsm` | `map/model/s###/` | terrain (grille V7 EDENFR — spec `eden_godot/docs/FSM-FORMAT.md`) |",
         "| `*.area` | `map/model/s###/` | zones/déclencheurs de la map |",
         "| `*.ini` | `map/model/s###/` | config locale (Big5) |",
         "| `*.nif` | `map/model/s###/` | props/modèles propres à la map |",
         "| `s###.smp`/`.fsmp` | `map/sknodemap/` | objets nodaux animés (`SMP2.0`) |",
         "| `s###.pgp` | `map/sknodemap/` | grille de pathfinding (`PathMapVer0001`) |",
         "| `s###.csm` | `data/scene/` | ombres/lumière (Cascaded Shadow Maps) |",
         "| `s###.ini` | `data/scene/` | réglages de scène (météo, ambiant…) |",
         "| `S###s.dds`/`S###m.dds` | `ui/map/` | carte région 1024² / minimap 512² |",
         "",
         "⚠️ Casse : le dossier `s002/` (fsm) et `S002/` (area/ini) coexistent",
         "— résolution insensible à la casse obligatoire (`EdenVfs`).",
         "`map/map.nif` = exécutable PE, pas un modèle (G12). Textures :",
         "`map/texture/` (9 326 DDS), nommées par map ou partagées.",
         "",
         "## Par map",
         "",
         "| Map | Nom FR | Niveaux | Fichiers présents |",
         "|---|---|---|---|"]
    for code in sorted(by_map):
        nom, lv, kind = MAPS.get(code.lower(), ("?", "?", "?"))
        exts = ", ".join(sorted(by_map[code]))
        t.append(f"| {code} | {nom} | {lv} | {exts} |")
    if other:
        groups = collections.defaultdict(list)
        for r in other:
            groups[(os.path.dirname(r[0]), r[1])].append(r)
        t += ["", "## Fichiers `map/` hors code S###", "",
              "Regroupés par dossier/extension (détail unitaire : `files.tsv`).",
              "", "| Dossier | Ext | Nb | Exemples |", "|---|---|---|---|"]
        for (d, e), rs in sorted(groups.items()):
            ex = ", ".join(os.path.basename(r[0]) for r in rs[:3])
            t.append(f"| `{d}/` | {e} | {len(rs)} | {ex} |")
    w("scenes-et-cartes.md", "\n".join(t) + "\n")
    n = sum(1 for r in rows if r[0].startswith(("map/", "data/scene/")))
    DOCS["scenes-et-cartes.md"] = ("Scènes/cartes par map (fsm, area, sknodemap, csm, minimaps)", n)

# ================================================================ modèles
def doc_models():
    nifs = [r for r in rows if r[1] == "nif"]
    by_dir = collections.Counter(r[0].split("/")[0] for r in nifs)
    t = ["# Modèles 3D — `.nif` Gamebryo (6 421 fichiers)",
         "",
         "NetImmerse/Gamebryo (header `…File Format, Version 20.2.0.8`).",
         "Parser : `src/Eden.Core/Formats/NifFile` ; spec `tools/nif2gltf/nif.xml` ;",
         "classement par fichier : `MODELS-CATALOG.tsv` (7 991 nif/kf/kfm).",
         "",
         "## Répartition",
         "",
         "| Dossier | Fichiers | Contenu | Nommage |",
         "|---|---|---|---|",
         "| `char/model/` | ~2 096 | corps/tenues perso (peau, cheveux, classes) | lettre corps `B F G K L M` + `b/f/g/k/l/m/x…` |",
         "| `effect/model/` | ~1 692 | sorts/FX (`sNNNNN`) | `sNNNNN.nif` |",
         "| `map/model/` | ~1 320 | terrain/props par map | `map/model/s###/` |",
         "| `monster/model/` | 531 | monstres/PNJ | `m###`, `M###`, `n###`, `N###` |",
         "| `item/model/` | 465 | armes/objets portés | `w#####` |",
         "| `ride/model/` | 159 | montures | `r###` |",
         "| `decorate/model/` | 141 | décorations guilde/villa | `p###` |",
         "| `house/model/` | 17 | bâtiments maison | `h###` |",
         "",
         "Liens : `monster/model/m###.nif` ↔ codes `monsterlist`/`boundinfo`",
         "(kfm + skin) ; tenues de classe `A0xxxx` (`c_class.ini`) → meshes",
         "`#F…` via `4pequip.ini` ; skins d'arme `W#####` (`weapon.ini`).",
         "",
         "## Fichiers notables",
         "",
         "- `map/map.nif` (12,3 Mo) : **exécutable Windows PE**, pas un",
         "  modèle — authentique mais inutilisable (G12).",
         "- 7 « corps complets » méga-modèles race/genre (MODELS-CATALOG).",
         "- 1 « cabine d'essayage », 107 modèles raciaux/spéciaux `x…`.",
         "",
         "Liste unitaire : `artifacts/corpus-inventory/files.tsv`",
         "(`ext=nif`, jointe au catalogue)."]
    w("modeles.md", "\n".join(t) + "\n")
    DOCS["modeles.md"] = ("Modèles 3D .nif par domaine (6 421)", len(nifs))

# ================================================================ animations
def doc_anims():
    anims = [r for r in rows if r[1] in ("kf", "kfm")]
    by_dir = collections.Counter(os.path.dirname(r[0]) for r in anims)
    t = ["# Animations — `.kf` / `.kfm` Gamebryo",
         "",
         "`.kf` = clip d'animation (courbes TBC/Quadratic/B-spline — **perdues",
         "dans les GLB actuels**, c'est un gain attendu du Lot 2).",
         "`.kfm` = graphe de séquences (transitions entre clips).",
         "Parser : `src/Eden.Core/Formats/{KfFile,KfmFile,KfPose}`.",
         "",
         "## Répartition",
         "",
         "| Dossier | Fichiers | Contenu |",
         "|---|---|---|"]
    for d, n in sorted(by_dir.items()):
        kf = sum(1 for r in anims if os.path.dirname(r[0]) == d and r[1] == "kf")
        kfm = sum(1 for r in anims if os.path.dirname(r[0]) == d and r[1] == "kfm")
        desc = {"char/animation": "clips du corps de perso (b.kf ≈ 24 Mo…)",
                "monster/animation": "1 clip par monstre (m###.kf ↔ m###.kfm)",
                "ride/animation": "montures", "house/animation": "maison",
                "decorate/animation": "décorations", "char": "graphes par corps (b/f/g/k/l/m.kfm)",
                "monster": "graphes par monstre", "ride": "graphes par monture",
                "house": "graphes maison", "decorate": "graphes déco"}.get(d, "")
        t.append(f"| `{d}/` | {n} ({kf} kf / {kfm} kfm) | {desc} |")
    t += ["", "Chaîne monstre : `monsterlist.ini` `DB<code>,<kfm>,…,<skin dds>`",
          "+ `boundinfo.ini` `<code>,<nif>` — relie animation↔modèle↔skin.",
          "Transitions `.kfm` documentées dans `NATIVE-LOADING-ANALYSIS.md`."]
    w("animations.md", "\n".join(t) + "\n")
    DOCS["animations.md"] = ("Animations .kf/.kfm Gamebryo (1 572)", len(anims))

# ================================================================ textures
def doc_textures():
    dds = [r for r in rows if r[1] == "dds"]
    ifls = [r for r in rows if r[1] == "ifl"]
    by_dir = collections.Counter(os.path.dirname(r[0]) for r in dds)
    fmts = collections.Counter()
    dims = collections.Counter()
    for r in dds:
        m = re.match(r"(\d+)×(\d+) (\w+)", r[5])
        if m:
            fmts[m.group(3)] += 1
            dims[f"{m.group(1)}×{m.group(2)}"] += 1
    t = ["# Textures — `.dds` (34 927) + `.ifl` (45)",
         "",
         "DirectDraw Surface, majoritairement **DXT1/DXT5** (mip-mappées).",
         "Parser : `src/Eden.Core/Formats/{DdsInfo,DdsDecode}`.",
         "",
         "## Répartition par dossier",
         "",
         "| Dossier | Fichiers | Contenu |",
         "|---|---|---|"]
    domains = {"ui": "interface", "UI": "interface", "map": "terrain/cartes",
               "char": "personnage", "item": "objets/armes", "monster": "monstres",
               "effect": "sorts/FX", "ride": "montures", "decorate": "décorations",
               "house": "maison", "emblem": "emblèmes guilde", "sound": "(étrange)",
               "uidata": "CEGUI"}
    for d, n in by_dir.most_common():
        t.append(f"| `{d}/` | {n} | {domains.get(d.split('/')[0], '?')} |")
    t += ["", "## Formats (header DDS)", "",
          "| Format | Fichiers |", "|---|---|"]
    for f_, n in fmts.most_common(8):
        t.append(f"| {f_} | {n} |")
    t += ["", "## Dimensions les plus fréquentes", "",
          "| Dimensions | Fichiers |", "|---|---|"]
    for d_, n in dims.most_common(10):
        t.append(f"| {d_} | {n} |")
    t += ["", "## `.ifl` — textures animées (45)",
          "",
          "Listes d'images jouées en boucle (eau, effets). Parser `IflFile`.",
          ""]
    t += [f"- `{r[0]}` — {r[6]}" for r in sorted(ifls)]
    t += ["", "Textures référencées mais **jamais livrées** : G13/G14/G16/G18/",
          "G20/G21 dans `docs/workflows/EDENFR-GAPS.md` — ne pas inventer de",
          "remplaçant."]
    w("textures.md", "\n".join(t) + "\n")
    DOCS["textures.md"] = ("Textures .dds par domaine + formats + .ifl (34 972)", len(dds) + len(ifls))

# ================================================================ UI/CEGUI
def doc_ui():
    ui_rows = [r for r in rows if r[2] == "ui-cegui" or r[2] == "texture-ui"]
    layouts = [r for r in ui_rows if r[1] == "layout"]
    imagesets = [r for r in ui_rows if r[1] == "imageset"]
    by_sub = collections.Counter(r[3] for r in ui_rows)
    # imageset -> fichier atlas + nb régions
    iset = []
    for r in sorted(imagesets):
        p = os.path.join(CORPUS, r[0])
        try:
            root = ET.parse(p).getroot()
            img = root.get("Imagefile", "?")
            n = len(root.findall(".//Image"))
            iset.append((r[0], root.get("Name", r[5]), img, n))
        except Exception:
            iset.append((r[0], r[5], "XML mal formé", "?"))
    t = ["# Interface — CEGUI (`uidata/`) + textures UI (`ui/`, `UI/`)",
         "",
         "## `uidata/` — l'UI en fichiers CEGUI",
         "",
         "| Type | Fichiers | Rôle |",
         "|---|---|---|",
         f"| `.layout` | {len(layouts)} | fenêtres/panneaux (XML) — **19 FR mal formés** (G4) |",
         f"| `.imageset` | {len(imagesets)} | atlas : nom → PNG + régions nommées |",
         "| `.font` / `.font.kr` | 5 / 6 | polices logiques CEGUI (XML) |",
         "| `.scheme` / `.looknfeel` | 1 / 1 | skin CEGUI |",
         "| `.ufo` | 1 | conteneur OLE2 (données fontes) |",
         "| `.lua` | 2 | scripts UI |",
         "",
         "## Imagesets → atlas",
         "",
         "| Imageset | Nom | Atlas | Régions |",
         "|---|---|---|---|"]
    for path, name, img, n in iset:
        t.append(f"| `{os.path.basename(path)}` | {name} | `{img}` | {n} |")
    t += ["", "## Textures UI (`ui/` + `UI/`, ~16 000 DDS)",
          "",
          "| Sous-dossier | Rôle |",
          "|---|---|",
          "| `itemicon/` | icônes d'items (code `UI…` dans `c_item.ini`) |",
          "| `skillicon/` | icônes de sorts |",
          "| `uiicon/` | icônes d'interface |",
          "| `map/` | cartes région `S###s.dds` 1024² + minimaps `S###m.dds` 512² |",
          "| `loadingframe/` | écrans de chargement `loading_###.dds` |",
          "| `UI/` racine | `title`, `angel`, `SystemCursors`… (casse jumelle) |",
          "",
          "Thèmes custom `ui_textures/themes/` = non-EDENFR (G8).",
          "Catalogue des captures UI : `eden_godot/docs/client-assets/",
          "ref-shots-catalog.md`.",
          "",
          "## Sous-catégories", "",
          "| Sous-catégorie | Fichiers |", "|---|---|"]
    for s, n in by_sub.most_common():
        t.append(f"| {s} | {n} |")
    w("ui-et-cegui.md", "\n".join(t) + "\n")
    DOCS["ui-et-cegui.md"] = ("Interface CEGUI + textures UI (uidata/, ui/, UI/)", len(ui_rows))

# ================================================================ audio/vidéo
def doc_av():
    av = [r for r in rows if r[2] in ("audio", "vidéo/flash")]
    n_ogg = sum(1 for r in av if r[1] == "ogg")
    n_cut = sum(1 for r in av if r[0].startswith("cutscene/"))
    pref = collections.Counter()
    for r in av:
        if r[1] == "wav":
            m = re.match(r"([A-Za-z]+)", os.path.basename(r[0]))
            pref[m.group(1) if m else "?"] += 1
    t = ["# Audio & vidéo — `sound/`, `bgm/`, `movies/`, `Movies/`, `cutscene/`",
         "",
         "| Dossier | Fichiers | Format | Contenu |",
         "|---|---|---|",
         "| `sound/` | 2 448 | WAV | effets sonores — préfixes : `m###` monstres,",
         "|  |  |  | `e###` effets, `s###`/`S###` scènes, `r###` montures,",
         "|  |  |  | `UI###` interface/usage item (id partagé avec les icônes),",
         "|  |  |  | `atks/ms/con/cond/trig/add` combat |",
         "| `bgm/` | " + str(n_ogg) + " | OGG | musiques `bgmNNN.ogg` |",
         "| `movies/`, `Movies/` | 12 | SWF/OGV | cinématiques + éléments Flash |",
         "| `cutscene/` | " + str(n_cut) + " | — | cinématiques de coupe |",
         "",
         "Rien à décoder : WAV PCM et OGG/Ogv standard. Le mapping",
         "son↔événement vit dans les tables (`c_spell`, `monsterlist`,",
         "`c_sceneevent`) et non dans les noms."]
    w("audio-video.md", "\n".join(t) + "\n")
    DOCS["audio-video.md"] = ("Sons WAV, musiques OGG, cinématiques SWF/OGV", len(av))

# ================================================================ divers
def doc_misc():
    misc = [r for r in rows if r[2] in ("système-launcher", "divers")]
    t = ["# Système, launcher & divers",
         "",
         "Fichiers racine du client d'origine : référence seulement, le client",
         "Godot n'en a pas besoin.",
         "",
         "| Fichier | Taille | Rôle |",
         "|---|---|---|"]
    for r in sorted(misc):
        t.append(f"| `{r[0]}` | {fmt_size(r[4])} | {r[6]} |")
    t += ["", "Extensions numériques (`.949/.950/.936/.1258`) : artefacts de",
          "patch/version du launcher d'origine.",
          "",
          "**Conteneur source** (hors corpus, dans l'install d'origine",
          "`~/Téléchargements/EDENFR`) : `pkg.idx` + `pkg001…pkgNNN.pkg` +",
          "`GameDataFileList.txt` (manifeste CRC32C). Spec :",
          "`docs/formats/{PKG-FORMAT,MANIFEST-CRC}.md`."]
    w("systeme-et-divers.md", "\n".join(t) + "\n")
    DOCS["systeme-et-divers.md"] = ("Racine launcher + formats orphelins", len(misc))

# ================================================================ INDEX
def doc_index(total):
    t = ["# Inventaire du corpus EDENFR — `corpus/fr-full/`",
         "",
         f"**{total} fichiers** extraits des paquets EDENFR",
         "(`~/Téléchargements/EDENFR`, 448 `pkgNNN.pkg` + `pkg.idx`),",
         "**lecture seule**. Ancienne extraction : `corpus/fr-full-pak-2026-10`.",
         "",
         "Un document par type ; le détail unitaire de chaque fichier est dans",
         "`artifacts/corpus-inventory/files.tsv` (1 ligne/fichier : chemin,",
         "catégorie, taille, métadonnées, description).",
         "",
         "| Document | Contenu | Fichiers |",
         "|---|---|---|"]
    order = ["tables-donnees.md", "scenes-et-cartes.md", "modeles.md",
             "animations.md", "textures.md", "ui-et-cegui.md",
             "audio-video.md", "systeme-et-divers.md"]
    for name in order:
        titre, n = DOCS[name]
        t.append(f"| [{name}]({name}) | {titre} | {n} |")
    t += ["",
          "## Avant de lire un fichier",
          "",
          "- **Encodages** : tables `c_*`/`C_*`/`S_*` en **Big5**, `t_*` (textes FR)",
          "  en **CP1252** — jamais de `File.*` direct, passer par `EdenVfs`/",
          "  `AssetCatalog`/`TextResolver` (casse insensible).",
          "- **Pas de `.glb` dans ce dépôt** : le client 4.8 lit les natifs",
          "  (Lot 2). Les dérivés du portage 4.7 vivent dans `../portage/`.",
          "- **Absences authentiques** : des tables citent des fichiers jamais",
          "  livrés → `docs/workflows/EDENFR-GAPS.md` avant de « réparer ».",
          "- Compléments : `MODELS-CATALOG` (modèles), `items.tsv` (items),",
          "  `maps.tsv` (maps), `INVENTAIRE-QUETES-MONTURES.md`,",
          "  `reference/harvest/` (synthèses gameplay), `knowledge/` (wiki).",
          "",
          "## Régénération",
          "",
          "```bash",
          "python3 tools/corpus-inventory/inventory.py    # → artifacts/corpus-inventory/files.tsv",
          "python3 tools/corpus-inventory/render_docs.py  # → docs/reference/corpus/*.md",
          "```"]
    w("INDEX.md", "\n".join(t) + "\n")

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    doc_tables(); doc_scenes(); doc_models(); doc_anims()
    doc_textures(); doc_ui(); doc_av(); doc_misc()
    doc_index(len(rows))
    print("docs écrits dans", OUT)
    for k, v in DOCS.items():
        print(" ", k, v[1])
