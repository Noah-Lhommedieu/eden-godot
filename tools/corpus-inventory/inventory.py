#!/usr/bin/env python3
"""Inventaire du corpus EDENFR (corpus/fr-full) — génère :

- artifacts/corpus-inventory/files.tsv : 1 ligne/fichier (chemin, ext,
  catégorie, sous-catégorie, octets, métadonnées, description, refs)
- docs/reference/corpus/*.md : un document par type + INDEX.md global

Jointures : docs/reference/{MODELS-CATALOG,maps,items}.tsv,
corpus/fr-full/data/db/{monsterlist,boundinfo,c_item}.ini.
Stdlib seule. Usage : python3 tools/corpus-inventory/inventory.py
"""
import os, re, struct, sys, wave, contextlib
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CORPUS = os.path.join(ROOT, "corpus", "fr-full")
OUT_TSV = os.path.join(ROOT, "artifacts", "corpus-inventory", "files.tsv")
OUT_DOC = os.path.join(ROOT, "docs", "reference", "corpus")
REF = os.path.join(ROOT, "docs", "reference")

# ---------------------------------------------------------------- joins

def load_tsv(path):
    try:
        with open(path, encoding="utf-8") as f:
            rows = [l.rstrip("\n").split("\t") for l in f]
        return rows[0], rows[1:]
    except OSError:
        return [], []

def load_models_catalog():
    """fichier -> (catégorie, race/genre, lien)"""
    h, rows = load_tsv(os.path.join(REF, "MODELS-CATALOG.tsv"))
    try:
        fi, ci, ri, li = h.index("fichier"), h.index("catégorie"), \
            h.index("race/genre"), h.index("lien 4pequip/item")
    except ValueError:
        return {}
    return {r[fi].lower(): (r[ci], r[ri], r[li]) for r in rows if len(r) > li}

def load_maps():
    """code s### (lower) -> (nom_fr, niveaux, kind)"""
    h, rows = load_tsv(os.path.join(REF, "maps.tsv"))
    try:
        ci, ni, li, ki = h.index("code_client"), h.index("nom_fr"), \
            h.index("niveaux"), h.index("kind")
    except ValueError:
        return {}
    return {r[ci].lower(): (r[ni], r[li], r[ki]) for r in rows if len(r) > ki}

def load_icon_items():
    """code icône (UI012) -> liste de noms d'items (max 3)"""
    h, rows = load_tsv(os.path.join(REF, "items.tsv"))
    try:
        ii, ni = h.index("icone"), h.index("nom")
    except ValueError:
        return {}
    d = defaultdict(list)
    for r in rows:
        if len(r) > max(ii, ni) and r[ii]:
            d[r[ii]].append(r[ni])
    return d

def load_monsterlist():
    """code monstre -> (kfm, skin dds) ; et nif -> code via boundinfo"""
    codes, nif2code = {}, {}
    try:
        for line in open(os.path.join(CORPUS, "data/db/monsterlist.ini"),
                         encoding="big5", errors="replace"):
            m = re.match(r"DB\d+=([A-Za-z0-9]+),([A-Za-z0-9._]+).*?," 
                         r"(?:NULL|([A-Za-z0-9._]+))?,", line)
            mm = re.match(r"DB\d+=(\w+),(\w+\.kfm),[^,]*,[^,]*,[^,]*,"
                          r"[^,]*,[^,]*,(\w+\.dds)", line)
            if mm:
                codes[mm.group(1)] = (mm.group(2), mm.group(3))
        for line in open(os.path.join(CORPUS, "data/db/boundinfo.ini"),
                         encoding="big5", errors="replace"):
            p = line.strip().split(",")
            if len(p) >= 2 and p[1].lower().endswith(".nif"):
                nif2code.setdefault(p[1].lower(), []).append(p[0])
    except OSError:
        pass
    return codes, nif2code

# ---------------------------------------------------------------- extract

def dds_meta(path):
    with open(path, "rb") as f:
        h = f.read(128)
    if len(h) < 128 or h[:4] != b"DDS ":
        return "en-tête DDS invalide"
    height, width = struct.unpack_from("<II", h, 12)
    mips = struct.unpack_from("<I", h, 28)[0]
    fourcc = h[84:88]
    if fourcc.strip(b" \0"):
        fmt = fourcc.rstrip(b" \0").decode("ascii", "replace")
    else:
        fmt = "RGBA non compressé"
    return f"{width}×{height} {fmt}, {max(mips,1)} mip(s)"

def nif_meta(path):
    with open(path, "rb") as f:
        head = f.read(96)
    if head[:2] == b"MZ":
        return "exécutable PE nommé .nif (pas un modèle — G12)"
    line = head.split(b"\n", 1)[0].decode("ascii", "replace").strip()
    m = re.search(r"Version ([\d.]+)", line)
    return f"Gamebryo/NIF {m.group(1) if m else line[:48] or 'binaire'}"

def ini_meta(path):
    """(format_header, nb_lignes, sections)"""
    with open(path, "rb") as f:
        raw = f.read(4096)
    first = raw.split(b"\n", 1)[0].strip()
    try:
        head = first.decode("ascii")
    except UnicodeDecodeError:
        head = first.decode("big5", "replace")
    m = re.match(r"\|?(V\.[\d.]+)\|?(\d+)?\|?", head)
    ver = f"{m.group(1)}" if m else "INI"
    sections = re.findall(rb"\[([^\]\r\n]+)\]", raw)
    secs = ",".join(s.decode("ascii", "replace") for s in sections[:4])
    try:
        n = sum(1 for _ in open(path, "rb")) - 1
    except OSError:
        n = 0
    return ver, max(n, 0), secs

def wav_meta(path):
    try:
        with contextlib.closing(wave.open(path, "rb")) as w:
            return f"{w.getnframes()/w.getframerate():.1f} s, {w.getframerate()} Hz"
    except Exception:
        return "WAV"

def img_meta(path):
    with open(path, "rb") as f:
        h = f.read(32)
    if h[:8] == b"\x89PNG\r\n\x1a\n":
        w, hh = struct.unpack(">II", h[16:24]); return f"{w}×{hh} PNG"
    if h[:2] == b"BM":
        w, hh = struct.unpack("<ii", h[18:26]); return f"{w}×{hh} BMP"
    if h[:2] == b"\xff\xd8":
        return "JPEG"
    return "image"

def xml_name(path):
    with open(path, "rb") as f:
        raw = f.read(2048).decode("utf-8", "replace")
    m = re.search(r'name="([^"]+)"', raw)
    return m.group(1) if m else ""

def magic(path, n=16):
    with open(path, "rb") as f:
        return f.read(n)

# ---------------------------------------------------------------- classify

DB_ROLES = {
 "4pequip":"tenue/équipement → mesh/suffixe par code item (DB<code>=…)",
 "4pequipmap":"tenue → fichiers texture (B…/G… .dds)",
 "4PHouse":"maison de guilde/joueur (config)",
 "Assassin":"données spécifiques classe Assassin",
 "Berserker":"données spécifiques classe Berserker",
 "boundinfo":"boîtes englobantes par code modèle (m010010 → m010.nif)",
 "c_achievement":"succès/hauts faits (définitions)",
 "c_activity":"activités journalières",
 "c_ai":"comportements IA des monstres",
 "c_battlefield":"champs de bataille PvP",
 "c_buliding":"bâtiments de guilde (orthographe d'origine)",
 "c_charcolor":"couleurs/teintes personnage",
 "c_class":"classes jouables : stats, skills, certificats, tenues A0xxxx/level",
 "C_Classes":"classes — noms/codes (table compacte)",
 "c_classdropitem":"objets obtenus par classe",
 "c_classrankbuff":"bonus « Class Ability » par palier de 5 niv. (6e champ = enchant)",
 "c_collection":"collections d'objets",
 "c_destinyevent":"événements de destinée",
 "c_destinyroad":"Destiny Road (progression)",
 "c_dialogue":"arbres de dialogue PNJ",
 "c_dropitem":"tables de butin",
 "C_Elf":"familiers (elf) — base",
 "C_ElfCollect":"familiers — collecte",
 "C_ElfCombine":"familiers — combinaison",
 "C_ElfCombineElf":"familiers — combinaison (variante)",
 "c_enchant":"enchants (bonus appliqués — ex. bonus de classe)",
 "c_equipset":"sets d'équipement (bonus de set)",
 "c_equiptrain":"entraînement d'équipement",
 "c_familycollect":"famille — collecte",
 "c_familycombine":"famille — combinaison",
 "c_familyevent":"événements de famille",
 "c_festival":"festivals/événements saisonniers",
 "c_growequip":"équipements évolutifs",
 "character":"paramètres de base du personnage",
 "c_item":"items (référentiel principal, code Q…, icône UI…)",
 "c_itemchest":"coffres et contenus",
 "c_itemcombo":"combinaisons d'objets",
 "c_itemmall":"boutique (item mall)",
 "c_itemmallenchant":"enchants de la boutique",
 "c_level":"courbes de niveau/XP",
 "c_lifeskill":"compétences de vie (métiers)",
 "C_Lottery":"loterie",
 "C_LuckyBag":"sacs chanceux",
 "C_Mask":"masques (apparences)",
 "c_mission":"quêtes/missions (référentiel)",
 "c_monster":"monstres (référentiel)",
 "c_monsterstep":"paliers/évolution monstres",
 "c_node":"nodes de carte (codes S### ↔ scènes)",
 "c_npc":"PNJ (référentiel)",
 "c_parameter":"paramètres globaux du jeu (S_Parameter)",
 "c_partner":"partenaires",
 "c_partnergrow":"croissance des partenaires",
 "c_party":"groupes/escouades",
 "c_paysystem":"système de paiement",
 "c_pvp":"PvP général",
 "C_Races":"races jouables",
 "c_rankaward":"récompenses de rang (class rank — lié au bonus de classe)",
 "c_recommendactivity":"activités recommandées",
 "c_rewardscore":"scores/récompenses",
 "c_ridestep":"paliers de monture",
 "c_sceneevent":"événements déclenchés par scène",
 "c_schedule":"planification d'événements",
 "c_script":"scripts de jeu",
 "c_spell":"sorts (référentiel principal)",
 "c_spellreference":"références croisées de sorts",
 "c_statedependence":"dépendances entre états/buffs",
 "c_store":"magasins PNJ",
 "c_syssetup":"réglages système",
 "c_talent":"talents (arbres de connaissance KP)",
 "c_territory":"territoires (guerre de territoire)",
 "c_textindex":"index des textes",
 "c_textlimit":"limites de texte",
 "c_title":"titres de joueur",
 "c_transport":"transports/téléportations",
 "Collection":"collections (ancienne casse)",
 "color":"couleurs UI",
 "decorate":"décorations (guilde/villa)",
 "dyneffect":"effets dynamiques",
 "effect":"effets de sorts/compétences (FX)",
 "ElfEquip":"équipement de familier",
 "ElfEvent":"événements de familier",
 "ELFList":"liste des familiers",
 "ELFSuit":"tenues de familier",
 "ElfTrain":"entraînement de familier",
 "Equip":"équipements (référentiel)",
 "EquipType":"types d'équipement",
 "FarmEvent":"événements de ferme",
 "GuildFunction":"fonctions de guilde",
 "lic":"licences (craft/de classe ?)",
 "monsterlist":"liens code monstre → kfm + skin (m010010 → m010.kfm, m01001.dds)",
 "motion":"motions/animations nommées",
 "NewEquip":"équipements (nouvelle table)",
 "NormalFemale":"corps de base — femelles (race humaine?)",
 "NormalMale":"corps de base — mâles",
 "NpcAvatar":"avatars de PNJ",
 "npclist":"liste des PNJ → modèles/skins",
 "PVP":"PvP (ancienne casse)",
 "ridelist":"liste des montures",
 "rootcmds":"commandes console (racine)",
 "rootcmds_st":"commandes console (variante)",
 "smallFemale":"corps de base — petites femelles (Zumi?)",
 "smallMale":"corps de base — petits mâles (Zumi?)",
 "startsetting":"réglages de démarrage du client",
 "SuitEquip":"tenues/suits d'équipement",
 "textindex":"index des textes (ancienne casse)",
 "UIConfig":"configuration UI",
 "weapon":"armes — modèles/skins (codes W#####)",
}

def t_role(name):
    base = name[2:]
    r = DB_ROLES.get("c_" + base) or DB_ROLES.get("C_" + base)
    return ("textes FR — " + r) if r else "textes FR (traductions id→chaîne, CP1252)"

def db_role(stem):
    if stem.startswith(("t_",)):
        return t_role(stem)
    return DB_ROLES.get(stem, "table native (usage à confirmer — chercher le parseur C#)")

MAP_RE = re.compile(r"[sS](\d{3})")

def classify(rel, base, stem, ext, models_cat, maps, icons, mcodes, nif2code):
    parts = rel.split("/")
    top = parts[0]
    cat, sub, desc = "divers", "", "fichier corpus"

    if top == "data" and parts[1] == "db":
        cat = "table-données"
        enc = "CP1252 (FR)" if stem.startswith("t_") else "Big5"
        return cat, "db", f"{db_role(stem)} — table {stem}, {enc}"
    if top == "data" and parts[1] == "scene":
        m = MAP_RE.search(base)
        nom = f" « {maps[m.group(0).lower()][0]} »" if m and m.group(0).lower() in maps else ""
        cat = "config-scène"
        return cat, ext, (f"réglages ombres/lumière de {m.group(0)}{nom}" if ext == "csm"
                          else f"config de scène {m.group(0)}{nom}")
    if top == "uidata":
        cat, sub = "ui-cegui", parts[1] if len(parts) > 2 else "."
        roles = {"layouts":"layout de fenêtre CEGUI", "imagesets":"imageset CEGUI (atlas)",
                 "fonts":"police CEGUI", "schemes":"scheme CEGUI", "looknfeel":"looknfeel CEGUI",
                 "lua_scripts":"script Lua UI", "imagesets ":"imageset CEGUI"}
        return cat, sub, roles.get(sub, "fichier CEGUI")
    if top in ("ui", "UI"):
        cat = "texture-ui"
        subs = {"itemicon":"icône d'item", "skillicon":"icône de sort",
                "uiicon":"icône d'interface", "map":"carte région/minimap",
                "loadingframe":"écran de chargement"}
        sub = parts[1] if len(parts) > 2 else "."
        if sub == "map":
            m = MAP_RE.search(base)
            kind = "carte région 1024²" if base.lower().endswith("s") else "minimap 512²"
            if m and m.group(0).lower() in maps:
                return cat, sub, f"{kind} de {m.group(0)} « {maps[m.group(0).lower()][0]} »"
            return cat, sub, kind
        if sub == "itemicon" and stem in icons:
            n = icons[stem]
            return cat, sub, f"icône d'item {stem} — utilisée par {len(n)} item(s) (ex. {n[0]})"
        if top == "sound" or ext == "wav" and stem in icons:
            return cat, sub, f"son lié à l'id {stem} ({len(icons[stem])} item(s) : {icons[stem][0]})"
        return cat, sub, subs.get(sub, "texture d'interface")
    if top in ("sound", "bgm"):
        cat = "audio"
        if stem in icons:
            return cat, "son-item", f"son d'usage {stem} ({len(icons[stem])} item(s) : {icons[stem][0]})"
        return cat, top, "musique de fond" if top == "bgm" else "effet sonore"
    if top in ("movies", "Movies", "cutscene"):
        return "vidéo/flash", top, "cinématique/élément Flash"
    if top == "emblem":
        return "texture-ui", "emblème", "emblème de guilde"

    # domaines 3D
    dom = {"char":"personnage", "monster":"monstre", "item":"objet/arme",
           "effect":"effet", "map":"carte", "ride":"monture",
           "house":"maison", "decorate":"décoration"}.get(top)
    if dom:
        sub = parts[1] if len(parts) > 1 else "."
        if ext == "nif" or ext in ("kf", "kfm"):
            mc = models_cat.get(rel.lower())
            catinfo = f" ({mc[0]}{' — '+mc[1] if mc[1] else ''}{' ; '+mc[2] if mc[2] else ''})" if mc else ""
            cat = "animation" if ext in ("kf", "kfm") else "modèle-3d"
            if rel.lower() == "map/map.nif":
                return cat, "carte", "exécutable PE nommé .nif — authentique mais inutilisable (G12)"
            if top == "monster":
                codes = nif2code.get(base.lower())
                extra = (f" — code {codes[0]}"
                         + (f" (+{len(codes)-1})" if len(codes) > 1 else "")
                         + ", kfm/skin via monsterlist") if codes else ""
                return cat, f"{top}", f"{ext.upper()} {dom}{extra}{catinfo}"
            if top == "map":
                m = MAP_RE.search(rel)
                nom = f" « {maps[m.group(0).lower()][0]} »" if m and m.group(0).lower() in maps else ""
                return cat, "carte", f"{ext.upper()} de {m.group(0) if m else top}{nom}{catinfo}"
            return cat, dom, f"{ext.upper()} {dom}{catinfo}"
        if ext == "dds":
            cat = "texture-3d"
            return cat, dom, f"texture {dom}"
        if ext == "fsm":
            m = MAP_RE.search(base)
            nom = f" « {maps[m.group(0).lower()][0]} »" if m and m.group(0).lower() in maps else ""
            return "terrain-scène", "fsm", f"terrain {m.group(0) if m else base}{nom} (FSM V7 EDENFR)"
        if ext in ("smp", "fsmp", "smp2", "pgp", "area", "csm"):
            m = MAP_RE.search(base)
            nom = f" « {maps[m.group(0).lower()][0]} »" if m and m.group(0).lower() in maps else ""
            roles = {"smp":"objets nodaux de scène (SMP2.0)",
                     "fsmp":"objets nodaux de scène (SMP2.0)",
                     "smp2":"objets nodaux de scène (SMP2)",
                     "pgp":"grille de pathfinding (PathMapVer0001)",
                     "area":"zones/déclencheurs", "csm":"ombres/lumière"}
            return "terrain-scène", ext, f"{roles.get(ext,'donnée scène')} {m.group(0) if m else base}{nom}"
        if ext == "ifl":
            return "texture-3d", dom, f"liste de textures animées ({dom})"
        if ext == "ini":
            m = MAP_RE.search(base)
            nom = f" « {maps[m.group(0).lower()][0]} »" if m and m.group(0).lower() in maps else ""
            return "config-scène" if top == "map" else "table-données", top, \
                   f"config {top} {m.group(0) if m else ''}{nom} (Big5)"
        return "divers", dom, f"{ext} {dom}"

    # racine
    root_roles = {
        "Launcher.exe":"lanceur Windows d'origine (référence)",
        "game.bin":"binaire de jeu d'origine",
        "ACDC.dat":"données anti-cheat (AhnLab?)",
        "cltax.xmf":"binaire propriétaire (index média ?, non documenté)",
        "ftrace.dos":"trace/debug DOS d'origine",
        "packver.txt":"version du pack",
        "updatedate.txt":"date de mise à jour",
        "Banner.ini":"bannière launcher",
        "connects.ini":"adresses serveur d'origine",
        "LauncherMod.ini":"config launcher",
        "locate.ini":"localisation/langue launcher",
        "ALAudio.dll":"audio (DLL Windows)",
        "CrashRpt.dll":"rapport de crash (DLL)",
        "dbghelp.dll":"debug Microsoft (DLL)",
        "WebBrowser.dll":"navigateur intégré (DLL)",
    }
    return "système-launcher", ".", root_roles.get(base, "fichier racine du client d'origine")

# ---------------------------------------------------------------- meta per file

def meta_for(path, ext):
    try:
        if ext == "dds": return dds_meta(path)
        if ext == "nif": return nif_meta(path)
        if ext in ("wav",): return wav_meta(path)
        if ext in ("png", "bmp", "jpg", "jpeg"): return img_meta(path)
        if ext == "ufo":
            return "conteneur OLE2 (données fontes UI)"
        if ext in ("layout", "imageset", "scheme", "looknfeel", "font", "kr"):
            n = xml_name(path)
            return f'"{n}"' if n else ext
        if ext in ("smp", "fsmp", "smp2", "pgp"):
            h = magic(path, 20)
            m = re.search(rb"(SMP2\.0|PathMapVer\d+)", h)
            return m.group(1).decode() if m else "binaire"
        if ext == "swf":
            return "Flash " + magic(path, 3).decode("ascii", "replace")
        if ext == "fsm":
            return "FSM (V7 EDENFR)"
        return ""
    except OSError:
        return ""

def main():
    models_cat = load_models_catalog()
    maps = load_maps()
    icons = load_icon_items()
    mcodes, nif2code = load_monsterlist()

    rows, stats = [], defaultdict(int)
    for dirpath, _dirs, files in os.walk(CORPUS):
        for fn in files:
            full = os.path.join(dirpath, fn)
            rel = os.path.relpath(full, CORPUS).replace(os.sep, "/")
            base = fn
            stem, dot, ext = base.rpartition(".")
            ext = ext.lower() if dot else ""
            size = os.path.getsize(full)
            cat, sub, desc = classify(rel, base, stem or base, ext,
                                      models_cat, maps, icons, mcodes, nif2code)
            meta = meta_for(full, ext)
            clean = lambda s: "".join(c for c in str(s)
                                      if c == "«" or c == "»" or c >= " ")
            rows.append((rel, ext, cat, sub, size,
                         clean(meta), clean(desc)))
            stats[cat] += 1

    os.makedirs(os.path.dirname(OUT_TSV), exist_ok=True)
    with open(OUT_TSV, "w", encoding="utf-8") as f:
        f.write("chemin\text\tcategorie\tsous_categorie\toctets\tmeta\tdescription\n")
        for r in sorted(rows):
            f.write("\t".join(str(x) for x in r) + "\n")
    print(f"{len(rows)} fichiers → {OUT_TSV}")
    for c, n in sorted(stats.items()):
        print(f"  {c}: {n}")
    return rows, stats

if __name__ == "__main__":
    main()
