#!/usr/bin/env python3
"""export_web_data.py — pipeline P3 : exporte les données du portail web.

Sources :
  - numérique : server_us/Data/db_decoded/S_*.ini (Big5, layouts
    identiques aux c_*.ini client — vérifié par version header)
  - libellés  : reforged/EdenReforged/data/db/T_*.ini (EN, latin-1 ;
    2 lignes d'en-tête : nb de colonnes + indices 1-basés des colonnes
    traduites), jointure par id
  - icônes    : reforged/EdenReforged/UI/{itemicon,skillicon,uiicon}/
    <code>.dds|png|tga → WebP 64x64

Sorties :
  - limitless/web/backend/App_Data/wiki/<dataset>.json
  - limitless/web/backend/App_Data/wiki/index.json (compteurs + version)
  - limitless/web/frontend/public/icons/<CODE>.webp

Semantique des colonnes : prouvée par les parsers C#
server/src/Eden.Core/Data/Parsers/*.cs — voir
docs/generated/WEB-DATA-AUDIT.md.

Usage : tools/export_web_data.py [--icons-only] [--json-only]
        [--limit N] (debug : tronque chaque dataset)
"""

import json
import os
import re
import sys
from concurrent.futures import ProcessPoolExecutor

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
S_DIR = os.path.join(ROOT, "server_us", "Data", "db_decoded")
T_DIR = os.path.join(ROOT, "reforged", "EdenReforged", "data", "db")
UI_DIR = os.path.join(ROOT, "reforged", "EdenReforged", "UI")
OUT_JSON = os.path.join(ROOT, "limitless", "web", "backend",
                      "App_Data", "wiki")
OUT_ICONS = os.path.join(ROOT, "limitless", "web", "frontend",
                         "public", "icons")

ICON_SETS = ("itemicon", "skillicon", "uiicon")  # ordre de recherche

# C_Talent du client Reforged (arbres réellement joués par le client
# QoL) — servi en parallèle de S_Talent (« classic ») sur le site.
C_TALENT_PATH = os.path.join(
    ROOT, "reforged", "EdenReforged", "data", "db", "C_Talent.ini")

# Référence scrapée de ee-tools.pages.dev (autorisée par l'utilisateur
# — lot12 W6) : combos de certificats par classe + arbres Reforged.
EE_REFERENCE = os.path.join(
    os.path.dirname(os.path.abspath(__file__)),
    "data", "ee_tools_reference.json")

# Marqueurs de mise en forme des textes ($7$, $12$… = codes couleur
# client). On les retire pour l'affichage web.
FMT_MARK = re.compile(r"\$\d+\$")

# Libellés EN dérivés des kinds d'items (S_Item col8 — kinds prouvés
# dans ItemParser.cs : armes 17-30/61-66, spellbook 22/25, certificat
# 40, sac 43, gemme 45, destiny 24). Le reste = catégorie générique.
KIND_LABELS = {
    22: "Skill book", 25: "Skill book", 40: "Certificate",
    43: "Bag", 45: "Gem", 24: "Special",
}
WEAPON_KINDS = set(range(17, 31)) | set(range(61, 67))

_RIDE_ENCHANTS = None


def ride_enchant_ids() -> set:
    """Enchants de monture — lignes S_Enchant contenant la commande
    native 2134 (ride ; ex. 58991 « 2134|14010|1 » + « 2051|40 »
    = +40% vitesse, preuve ItemParser.UseEnchantId). Un item dont
    useEnchant ∈ ce set est une MONTURE (correction P4 : les montures
    étaient classées « Consumable »)."""
    global _RIDE_ENCHANTS
    if _RIDE_ENCHANTS is None:
        s = set()
        for r in parse_s_table("S_Enchant.ini"):
            if len(r) < 10 or not celli(r, 0):
                continue
            if any(cell(r, i) == "2134" for i in range(8, len(r))):
                s.add(celli(r, 0))
        _RIDE_ENCHANTS = s
    return _RIDE_ENCHANTS


def ride_speed(enchant_id: int) -> int:
    """Bonus de vitesse d'un enchant de monture — commande 2051 du
    bloc d'enchant (2051|N = MoveSpeed +N%, ex. 58991 → 40)."""
    for r in parse_s_table("S_Enchant.ini"):
        if len(r) < 10 or celli(r, 0) != enchant_id:
            continue
        for i in range(8, len(r) - 1):
            if cell(r, i) == "2051":
                try:
                    return int(float(cell(r, i + 1) or 0))
                except ValueError:
                    return 0
        return 0
    return 0


# Flags hauts de S_Item col11 (i64) — bits natifs :
# 8 = arme, 0x20 = armure, 0x10 = accessoire
# (CheckMagicBoxCosts @0x8088200, doc ItemParser.FlagsHi).
_ENCHANT_ROWS = None


def enchant_rows():
    """{enchantId: row} — S_Enchant brute (cache module)."""
    global _ENCHANT_ROWS
    if _ENCHANT_ROWS is None:
        _ENCHANT_ROWS = {celli(r, 0): r
                         for r in parse_s_table("S_Enchant.ini")
                         if len(r) > 42 and celli(r, 0)}
    return _ENCHANT_ROWS


_T_ENCHANT = None


def t_enchant():
    """T_Enchant EN (cache) — cols traduites 1|6|56|57|58."""
    global _T_ENCHANT
    if _T_ENCHANT is None:
        _T_ENCHANT = parse_t_table("T_Enchant.ini")
    return _T_ENCHANT


# Événement déclencheur d'un proc (S_Enchant col41 — sémantique des
# déclencheurs CEC_OnAttack*/CEC_OnBeAttack* du binaire ; 1 = attaque,
# 2 = attaque subie, les autres événements existent mais sont rares —
# la desc EN T_Enchant reste la source affichée).
PROC_EVENT = {1: "onAttack", 2: "onBeAttacked", 3: "onCast",
              4: "onBeCast", 5: "onCrit", 6: "onBeCrit", 7: "onKill",
              8: "onBlock", 9: "onDodge"}


def enchant_effect(enchant_id: int):
    """Effet d'un enchant pour la fiche item — proc d'arme/armure
    affiché « comme une stat » (demande lot12 W1). Champs :
    name/desc/short depuis T_Enchant (col57 = « desc longue|résumé »,
    col6 = nom), event/chance/slots depuis S_Enchant cols 40-42
    (sémantique prouvée : col40 = slots d'équipement « 1;2 », col41 =
    événement déclencheur, col42 = chance %)."""
    row = enchant_rows().get(enchant_id)
    if row is None:
        return None
    t = t_enchant().get(enchant_id, {})
    desc_raw = clean_text(t.get(57, ""))
    desc, _, short = desc_raw.partition("|")
    eff = {}
    name = clean_text(t.get(6, ""))
    if name:
        eff["name"] = name
    if desc.strip():
        eff["desc"] = desc.strip()
    if short.strip():
        eff["short"] = short.strip()
    slots = cell(row, 40).strip()
    if slots:
        eff["slots"] = slots
    ev = celli(row, 41)
    if ev:
        eff["event"] = PROC_EVENT.get(ev, f"event{ev}")
    chance = celli(row, 42)
    if chance and chance != 100:
        eff["chance"] = chance
    return eff or None


def item_category(kind: int, equip_loc: int, flags_hi: int,
                  use_type: int, use_enchant: int = 0) -> str:
    if use_enchant and use_enchant in ride_enchant_ids():
        return "Mount"
    if kind in KIND_LABELS:
        return KIND_LABELS[kind]
    if kind in WEAPON_KINDS or (flags_hi & 0x8):
        return "Weapon"
    if flags_hi & 0x20:
        return "Armor"
    if flags_hi & 0x10:
        return "Accessory"
    if equip_loc > 0:
        return "Armor"
    if use_type in (1, 2):
        return "Consumable"
    return "Item"


def parse_s_table(name: str, sep: str = "|"):
    """Parse une table S_*.ini — mêmes règles qu'IniTable.cs :
    accumulate les lignes jusqu'à cols+1 champs (le séparateur final
    laisse un champ vide), les textes peuvent contenir des CRLF.
    Un chemin absolu est accepté tel quel (C_Talent Reforged)."""
    path = name if os.path.isabs(name) else os.path.join(S_DIR, name)
    with open(path, encoding="big5", errors="replace") as f:
        lines = f.read().splitlines()
    if not lines:
        return []
    header = lines[0]
    cols = 0
    for part in re.split(r"[|,]", header):
        if part.isdigit():
            cols = int(part)
            break
    rows = []
    pending = []
    for line in lines[1:]:
        if not line.strip() and not pending:
            continue
        pending.append(line)
        joined = "\r\n".join(pending)
        if cols > 0 and len(joined.split(sep)) < cols + 1:
            continue
        rows.append(joined.split(sep))
        pending.clear()
    if pending:
        rows.append("\r\n".join(pending).split(sep))
    return rows


def parse_t_table(name: str):
    """Parse une table T_*.ini (EN) → {id: {col1idx: texte}}.
    Header : ligne0 = nb de colonnes, ligne1 = indices 1-basés des
    colonnes traduites. Un record commence par `chiffres|` ; les
    lignes suivantes prolongent le dernier champ."""
    path = os.path.join(T_DIR, name)
    if not os.path.exists(path):
        return {}
    with open(path, encoding="latin-1", errors="replace") as f:
        lines = f.read().split("\n")
    if len(lines) < 2:
        return {}
    col_idx = [int(x) for x in lines[1].strip().rstrip("|").split("|")
               if x.strip().isdigit()]
    out = {}
    cur_id, cur_vals = None, []
    for raw in lines[2:]:
        line = raw.rstrip("\r")
        m = re.match(r"^(\d+)\|", line)
        if m:
            if cur_id is not None:
                out[cur_id] = cur_vals
            cur_id = int(m.group(1))
            cur_vals = line.split("|")
        elif cur_id is not None:
            cur_vals[-1] = cur_vals[-1] + "\n" + line
    if cur_id is not None:
        out[cur_id] = cur_vals
    # rec[0]=id ; rec[i] = valeur de la colonne col_idx[i] (1-idx)
    mapped = {}
    for rid, vals in out.items():
        d = {}
        for i, col in enumerate(col_idx):
            if i < len(vals):
                d[col] = vals[i]
        mapped[rid] = d
    return mapped


def cell(row, i):
    return row[i] if 0 <= i < len(row) else ""


def celli(row, i):
    try:
        return int(cell(row, i))
    except ValueError:
        return 0


def cellf(row, i):
    try:
        return float(cell(row, i))
    except ValueError:
        return 0.0


def clean_text(s: str) -> str:
    """Retire les marqueurs $N$, le séparateur `|` terminal résiduel
    et normalise les fins de ligne."""
    s = FMT_MARK.sub("", s)
    s = s.replace("\r\n", "\n").replace("\r", "\n").rstrip("|")
    lines = [ln.rstrip() for ln in s.split("\n")]
    while lines and not lines[0]:
        lines.pop(0)
    while lines and not lines[-1]:
        lines.pop()
    return "\n".join(lines)


ICONS: set = set()  # codes d'icônes référencés


def icon(code: str):
    code = code.strip()
    if code:
        ICONS.add(code)
        return code
    return ""


def icon_file_exists(code: str) -> bool:
    """Vrai si UI/{itemicon,skillicon,uiicon}/<code>.dds|png|tga
    existe — même recherche que convert_icons(). On ne publie un
    code icône que si l'image est convertible (sinon le front
    retombe sur le médaillon-initiale, moins bien que le glyphe)."""
    for sub in ICON_SETS:
        for ext in (".dds", ".png", ".tga"):
            if os.path.exists(os.path.join(UI_DIR, sub, code + ext)):
                return True
    return False


# Visages de mobs : les icônes « succès » S_Achievement col3 sont les
# portraits des familles de modèles (uiicon/M###.dds — ex. succès
# « Earth's Relic Justin Allspore » icône M025 ↔ monstre modèle
# M025013 ; ~94 % des succès « Defeat X » matchent model[:4]).
# Le code modèle d'un mob est M### + variante 3 chiffres → préfixe
# « M### » = famille/skin, et donc sa vignette portrait.
MOB_MODEL_RE = re.compile(r"^[Mm](\d{3})\d{3}$")


def mob_face_icon(model: str):
    """Modèle S_Monster col1 (« M025013 », parfois « m133014 ») →
    code icône portrait « M025 » si l'image uiicon existe."""
    m = MOB_MODEL_RE.match(model.strip())
    if not m:
        return ""
    code = "M" + m.group(1)
    return code if icon_file_exists(code) else ""


_NPC_MODEL_INDEX = None


def npc_model_index():
    """{code npc 'n106010': 'm106'} — server_us/Data/db_decoded/
    NPCList.ini : `DB<n>=<code>,m###.kfm,…` (modèle 3D du PNJ).
    Les PNJ réutilisent les familles de modèles des mobs → même
    portrait uiicon/M### (vérif. : n010010 « Frog Knight » → m010
    ↔ famille M010 « Anura »)."""
    global _NPC_MODEL_INDEX
    if _NPC_MODEL_INDEX is None:
        idx = {}
        path = os.path.join(S_DIR, "NPCList.ini")
        if os.path.exists(path):
            with open(path, encoding="latin-1") as f:
                for mm in re.finditer(
                        r"(?m)^DB\d+=(\w+),m(\d+)\.kfm", f.read()):
                    idx[mm.group(1)] = "M" + mm.group(2)
        _NPC_MODEL_INDEX = idx
    return _NPC_MODEL_INDEX


_MOB_DROP_INDEX = None


def mob_drop_index():
    """{itemId: [{"id":mobId,"name":EN}]} — mobs droppant chaque item
    (S_DropItem indexée par mobId + S_ClassDropItem via les RefIds des
    mobs, cols 40-49). Cap 16 mobs par item — les tables de classe
    sont très partagées."""
    global _MOB_DROP_INDEX
    if _MOB_DROP_INDEX is not None:
        return _MOB_DROP_INDEX
    mon_names = {i: clean_text(v.get(3, ""))
                 for i, v in parse_t_table("t_monster.ini").items()}
    cdt = class_drop_tables()
    idx = {}
    for r in parse_s_table("S_Monster.ini"):
        if len(r) < 10 or not celli(r, 0):
            continue
        rid = celli(r, 0)
        entries = [e for e in drop_tables().get(rid, [])
                   if e["fmt"] == "A"]
        for ref in (celli(r, c) for c in range(40, 50)
                    if celli(r, c) > 0):
            entries += cdt.get(ref, [])
        if not entries:
            continue
        nm = mon_names.get(rid) or None
        for e in entries:
            lst = idx.setdefault(e["item"], [])
            if len(lst) < 16 and all(x["id"] != rid for x in lst):
                lst.append({"id": rid, "name": nm})
    _MOB_DROP_INDEX = idx
    return idx


def export_items(limit=None):
    srows = parse_s_table("S_Item.ini")
    tmap = parse_t_table("t_item.ini")  # cols 1|8|35 → nom c7, desc c34
    inames = item_name_map()
    dropped_by = mob_drop_index()
    tables = drop_tables()
    out = []
    for r in srows:
        if len(r) < 9 or not celli(r, 0):
            continue
        rid = celli(r, 0)
        t = tmap.get(rid, {})
        flags = celli(r, 11)
        flags64 = 0
        try:
            flags64 = int(cell(r, 11) or 0)
        except ValueError:
            pass
        hi = (flags64 >> 32) & 0xFFFFFFFF if flags64 >= 0 else 0
        kind = celli(r, 8)
        equip_loc = celli(r, 9)
        use_type = celli(r, 12)
        use_ench = celli(r, 23)
        name = clean_text(t.get(8, ""))
        desc = clean_text(t.get(35, ""))
        item = {
            "id": rid,
            "icon": icon(cell(r, 1)),
            "name": name or None,
            "kind": kind,
            "category": item_category(kind, equip_loc, hi, use_type,
                                      use_ench),
            "reqLevel": celli(r, 14),
            "sellPrice": celli(r, 31),
            "maxStack": celli(r, 29),
            "quality": celli(r, 19),
        }
        if not name:
            item["untranslated"] = True
        if desc:
            item["desc"] = desc
        if equip_loc:
            item["equipLoc"] = equip_loc
        if celli(r, 15):
            item["maxLevel"] = celli(r, 15)
        if use_ench:
            item["useEnchant"] = use_ench
            if use_ench in ride_enchant_ids():
                item["mount"] = True
                spd = ride_speed(use_ench)
                if spd:
                    item["moveSpeedBonus"] = spd
            else:
                # Proc / effet spécial de l'enchant lié (W1) —
                # affiché dans la fiche comme une stat.
                eff = enchant_effect(use_ench)
                if eff:
                    eff["enchantId"] = use_ench
                    item["effects"] = [eff]
        stats = {}
        for key, col in (("hp", 37), ("mp", 38), ("str", 39),
                         ("agi", 40), ("int", 41), ("wis", 42),
                         ("lck", 43), ("patk", 45), ("matk", 46),
                         ("def", 50), ("hit", 51), ("dodge", 52),
                         ("crit", 53), ("critDmg", 54), ("mcrit", 55),
                         ("mcritDmg", 56)):
            v = celli(r, col)
            if v:
                stats[key] = v
        if cellf(r, 48):
            stats["atkRange"] = cellf(r, 48)
        if cellf(r, 49):
            stats["atkSpeed"] = cellf(r, 49)
        if celli(r, 44):
            stats["element"] = celli(r, 44)
        res = [celli(r, 63 + i) for i in range(9)]
        if any(res):
            stats["resist"] = res
        if stats:
            item["stats"] = stats
        if celli(r, 72):
            item["sockets"] = celli(r, 72)
        if celli(r, 74):
            item["durability"] = celli(r, 74)
        # Navigation croisée : mobs qui droppent l'item + contenu des
        # objets ouvrables (S_DropItem indexée par itemId, format B).
        if rid in dropped_by:
            item["droppedBy"] = dropped_by[rid]
        contents = [e for e in tables.get(rid, []) if e["fmt"] == "B"]
        if contents:
            item["contains"] = resolve_drops(contents, inames)
        # WD6 — index inversé S_ItemCombo : dans quels crafts l'item
        # est-il un matériau (« ressource utilisée pour tel craft »).
        uses = material_usage_index().get(rid)
        if uses:
            item["usedInCrafts"] = uses
        out.append(item)
        if limit and len(out) >= limit:
            break
    return out


_MATERIAL_USAGE = None


def material_usage_index():
    """{itemId: [comboId]} — S_ItemCombo c3 '#id#id#' résolue par
    matériau (filtre wiki « ressource utilisée pour tel craft »)."""
    global _MATERIAL_USAGE
    if _MATERIAL_USAGE is None:
        idx = {}
        for r in parse_s_table("S_ItemCombo.ini"):
            if len(r) < 4 or not celli(r, 0):
                continue
            cid = celli(r, 0)
            for tok in cell(r, 3).split("#"):
                tok = tok.strip()
                if tok.isdigit():
                    idx.setdefault(int(tok), []).append(cid)
        _MATERIAL_USAGE = idx
    return _MATERIAL_USAGE


_MOB_SPAWNS = None


def mob_spawn_index():
    """{mobId: [{map,node,name,x,y,count,respawn}]} — spawns réels
    depuis server_us/Data/scene/*.ini section [reborn_monsters]
    (spawnId,mobId,level,count,respawnSec,x,y,z,…). Le nom de map
    vient de S_Node (code Sxxx) → t_node."""
    global _MOB_SPAWNS
    if _MOB_SPAWNS is not None:
        return _MOB_SPAWNS
    # code map (S001) → (nodeId, nom EN)
    tmap = parse_t_table("t_node.ini")
    code2node = {}
    for r in parse_s_table("S_Node.ini"):
        if len(r) < 4 or not celli(r, 0):
            continue
        for c in r:
            m = re.match(r"^(S\d{3})$", c.strip())
            if m:
                code2node[m.group(1)] = (
                    celli(r, 0),
                    clean_text(tmap.get(celli(r, 0), {}).get(2, "")))
                break
    idx = {}
    scene_dir = os.path.join(ROOT, "server_us", "Data", "scene")
    if os.path.isdir(scene_dir):
        import csv as _csv
        for fn in os.listdir(scene_dir):
            code = os.path.splitext(fn)[0]
            node = code2node.get(code)
            section = None
            try:
                with open(os.path.join(scene_dir, fn),
                          encoding="big5", errors="replace") as f:
                    for line in f:
                        line = line.strip()
                        if line.startswith("["):
                            section = line
                            continue
                        if section != "[reborn_monsters]," or not line:
                            continue
                        parts = next(_csv.reader([line]))
                        if len(parts) < 8 or not parts[1].isdigit():
                            continue
                        ent = {"map": code, "x": _f(parts[5]),
                               "y": _f(parts[6]),
                               "count": int(float(parts[3] or 0)),
                               "respawnSec": int(float(parts[4] or 0))}
                        if node:
                            ent["nodeId"], ent["name"] = node
                        idx.setdefault(int(parts[1]), []).append(ent)
            except OSError:
                continue
    _MOB_SPAWNS = idx
    return _MOB_SPAWNS


def _f(s):
    try:
        return round(float(s), 2)
    except ValueError:
        return 0.0


def split_monster_name(raw: str):
    """t_monster c2 = « <Titre>\\nNom » — sépare le titre du nom
    (recherche/affichage wiki : « Justin Allspore » doit matcher le
    nom, pas le titre)."""
    raw = raw.strip()
    title = None
    if raw.startswith("<") and "\n" in raw:
        head, _, rest = raw.partition("\n")
        title = head
        raw = rest.strip() or head
    return raw, title


def export_monsters(limit=None):
    srows = parse_s_table("S_Monster.ini")
    tmap = parse_t_table("t_monster.ini")  # cols 1|3 → nom c2
    tsp = parse_t_table("T_Spell.ini")     # noms de sorts c17
    spell_names = {i: clean_text(v.get(17, ""))
                   for i, v in tsp.items()}
    spawns = mob_spawn_index()
    out = []
    for r in srows:
        if len(r) < 10 or not celli(r, 0):
            continue
        rid = celli(r, 0)
        t = tmap.get(rid, {})
        name, title = split_monster_name(clean_text(t.get(3, "")))
        m = {
            "id": rid,
            "name": name or None,
            "level": celli(r, 4),
            "type": celli(r, 5),      # 1 normal, 2 élite, 3 boss
            "hp": celli(r, 8),
            "mp": celli(r, 9),
            "model": cell(r, 1),
            "exp": celli(r, 53),
            "classExp": celli(r, 54),
            "atkPower": celli(r, 14),
            "pDef": celli(r, 18),
            "aggro": round(celli(r, 37) * 0.5, 1),
        }
        if not name:
            m["untranslated"] = True
        if title:
            m["title"] = title
        # Portrait du mob — famille du modèle (cf. mob_face_icon ;
        # icônes « succès » = visages de mobs, retour joueur W2).
        face = mob_face_icon(m["model"] or "")
        if face:
            m["icon"] = icon(face)
        res = [celli(r, 25 + i) for i in range(9)]
        if any(res):
            m["resist"] = res
        # Cols 40-49 = références mixtes (cf. MonsterParser.RefIds) :
        # sorts S_Spell, enchants, tables S_ClassDropItem — tri par
        # appartenance réelle aux tables, comme le loader natif.
        refs = list(dict.fromkeys(
            celli(r, c) for c in range(40, 50) if celli(r, c) > 0))
        if refs:
            m["refIds"] = refs
            skills = [{"id": sid, "name": spell_names.get(sid)}
                      for sid in refs if sid in spell_id_set()]
            if skills:
                m["skills"] = skills
        # Maps de spawn réelles (scène [reborn_monsters]).
        if rid in spawns:
            m["maps"] = spawns[rid]
        # Drops normaux : table propre S_DropItem[mobId] (format A) —
        # affichés « sans BP ». Les drops BP (Break Point natif —
        # barre violette des boss, cf. CItemFactory::ClassItemDrop)
        # viennent des S_ClassDropItem référencées et sont séparées
        # par groupe de classe du tueur.
        entries = [e for e in drop_tables().get(rid, [])
                   if e["fmt"] == "A"]
        drops = resolve_drops(entries, item_name_map())
        if drops:
            m["drops"] = drops
        bp = []
        for ref in refs:
            bp += class_drop_tables().get(ref, [])
        if bp:
            m["bpDrops"] = bp_drops(bp)
        out.append(m)
        if limit and len(out) >= limit:
            break
    return out


MISSION_TYPE = [
    ("\u4e3b\u7dda", "Main"),          # 主線
    ("\u6bcf\u65e5", "Daily"),         # 每日
    ("\u91cd\u8907", "Repeatable"),    # 重複
    ("\u7279\u6b8a", "Special"),       # 特殊
    ("\u5947\u9047", "Adventure"),     # 奇遇
    ("\u4e8b\u4ef6", "Event"),         # 事件
    ("\u804c\u4e1a", "Class"),         # 职业
]


def mission_type(tag: str) -> str:
    for big5, en in MISSION_TYPE:
        if big5 in tag:
            return en
    return "Quest"


_DROP_TABLES = None
_CLASS_DROPS = None
_SPELL_IDS = None


def drop_tables():
    """S_DropItem.ini — même logique que DropAndSpeedParsers.ParseDropItem
    (deux formats de groupes de 4 à partir de la col 10) :
      A (mobs)  : rate%|itemId|nom|qty   — table indexée par mobId
      B (items) : itemId|nom|qty|rate%   — contenu d'objet ouvrable
    Retourne {id: [{"item":i,"name":Big5,"qty":n,"chance":f,"fmt":"A"|"B"}]}."""
    global _DROP_TABLES
    if _DROP_TABLES is not None:
        return _DROP_TABLES
    out = {}
    for r in parse_s_table("S_DropItem.ini"):
        if len(r) < 14 or not celli(r, 0):
            continue
        entries = []
        i = 10
        while i + 3 < len(r):
            f0, f1, f2, f3 = (cell(r, i), cell(r, i + 1),
                              cell(r, i + 2), cell(r, i + 3))
            f0n = f0.replace(".", "", 1).isdigit()
            f1i = f1.isdigit()
            f2n = f2.replace(".", "", 1).isdigit()
            f3n = f3.replace(".", "", 1).isdigit()
            if f0n and f1i and int(f1) > 0 and not f2n:
                # Format A : rate|itemId|nom|qty
                entries.append({
                    "item": int(f1), "bigname": f2,
                    "qty": int(float(f3)) if f3n and float(f3) > 0 else 1,
                    "chance": float(f0), "fmt": "A"})
                i += 4
            elif f0n and not f1i and f2n and f3n:
                # Format B : itemId|nom|qty|rate
                entries.append({
                    "item": int(float(f0)), "bigname": f1,
                    "qty": int(float(f2)), "chance": float(f3),
                    "fmt": "B"})
                i += 4
            else:
                i += 1
        out[celli(r, 0)] = entries
    _DROP_TABLES = out
    return out


def class_drop_tables():
    """S_ClassDropItem.ini — drops BP (Break Point) par groupe de
    classe du tueur. Sémantique native
    (CClassDropItemQuery::Deserialize @0x85c2fxx) : la ligne est lue
    en itérations de 4 tokens (rate double, itemId strtol, nom skip,
    qty strtol) et la CLÉ de la map = l'index d'itération (1..21,
    le 21ᵉ reçoit la clé 0x3b = 59). CItemFactory::ClassItemDrop
    @0x8093dxx lookup cette clé avec CCharacter::GetClassGroup =
    DisplayIndex de la classe (col2 S_Class : 1-20 base + 59
    Inquisitor — les éveillés partagent le groupe du parent).
    Retourne {tableId: [{"item","name","qty","chance","group"}]} où
    group = groupe de classe (1-20, 59)."""
    global _CLASS_DROPS
    if _CLASS_DROPS is not None:
        return _CLASS_DROPS
    out = {}
    for r in parse_s_table("S_ClassDropItem.ini"):
        if len(r) < 5 or not celli(r, 0):
            continue
        entries = []
        # Itérations natives : 21 groupes de 4 tokens à partir de
        # l'index 1 (slots 1-84).
        for k in range(1, 22):
            i = 1 + (k - 1) * 4
            if i + 3 >= len(r):
                break
            c, iid, name, qty = (cell(r, i), cell(r, i + 1),
                                 cell(r, i + 2), cell(r, i + 3))
            try:
                chance = float(c) if c else 0.0
            except ValueError:
                continue
            if chance <= 0 or not iid.isdigit() or int(iid) <= 0:
                continue
            entries.append({"item": int(iid), "bigname": name,
                            "qty": int(qty) if qty.isdigit() else 1,
                            "chance": chance,
                            "group": 59 if k == 21 else k})
        out[celli(r, 0)] = entries
    _CLASS_DROPS = out
    return out


_CLASS_GROUP_NAMES = None


def class_group_names():
    """{groupe: nom EN} — groupe de classe natif = DisplayIndex
    (col2 S_Class ; classes de base id 1-20 + 59 Inquisitor).
    Utilisé pour annoter les drops BP."""
    global _CLASS_GROUP_NAMES
    if _CLASS_GROUP_NAMES is None:
        tcls = parse_t_table("T_Class.ini")
        m = {}
        for r in parse_s_table("S_Class.ini"):
            if len(r) < 3 or not celli(r, 0) or celli(r, 0) > 61:
                continue
            rid = celli(r, 0)
            if celli(r, 2) == rid:   # classe de base (parent==soi)
                m[rid] = (CLASS_NAME_OVERRIDES.get(rid)
                          or clean_text(
                              tcls.get(rid, {}).get(2, "")) or None)
        _CLASS_GROUP_NAMES = m
    return _CLASS_GROUP_NAMES


def bp_drops(entries):
    """Entrées class-drop brutes → [{classGroup,className,id,name,
    icon,qty,chance}] EN. group = DisplayIndex de la classe de base
    (1-20 = classe du même id, 59 = Inquisitor)."""
    names = item_name_map()
    icons = item_icon_map()
    cls = class_group_names()
    out = []
    for e in entries:
        g = e["group"]
        ic = icons.get(e["item"], "")
        if ic:
            icon(ic)
        out.append({"classGroup": g,
                    "className": cls.get(g),
                    "id": e["item"], "name": names.get(e["item"]),
                    "icon": ic or None,
                    "qty": e["qty"], "chance": e["chance"]})
    return sorted(out, key=lambda d: (d["classGroup"], -d["chance"]))


def spell_id_set():
    """Ids présents dans S_Spell (pour trier les RefIds monstres —
    cols 40-49 mélangent sorts/enchants/tables de drop, cf.
    MonsterParser.RefIds)."""
    global _SPELL_IDS
    if _SPELL_IDS is None:
        _SPELL_IDS = {celli(r, 0)
                      for r in parse_s_table("S_Spell.ini")
                      if len(r) > 1 and celli(r, 0)}
    return _SPELL_IDS


def item_name_map():
    """{itemId: nom EN} depuis t_item.ini (cache module)."""
    if not hasattr(item_name_map, "_cache"):
        items_t = parse_t_table("t_item.ini")
        item_name_map._cache = {
            i: clean_text(v.get(8, "")) for i, v in items_t.items()}
    return item_name_map._cache


def item_icon_map():
    """{itemId: code icône} depuis S_Item.ini col1 (cache module)."""
    if not hasattr(item_icon_map, "_cache"):
        item_icon_map._cache = {
            celli(r, 0): cell(r, 1)
            for r in parse_s_table("S_Item.ini")
            if len(r) > 1 and celli(r, 0)}
    return item_icon_map._cache


def resolve_drops(entries, item_names, cap=24):
    """Entrées drop brutes → [{id,name,icon,qty,chance}] EN,
    dédupliqué par item (chance max — une même pièce peut venir de la
    table mob ET d'une table de classe), tri chance desc."""
    icons = item_icon_map()
    best = {}
    for e in entries:
        cur = best.get(e["item"])
        if cur is None or e["chance"] > cur["chance"]:
            ic = icons.get(e["item"], "")
            if ic:
                icon(ic)  # référence pour la conversion webp
            best[e["item"]] = {"id": e["item"],
                               "name": item_names.get(e["item"]),
                               "icon": ic or None,
                               "qty": e["qty"], "chance": e["chance"]}
    out = sorted(best.values(), key=lambda d: -d["chance"])
    return out[:cap]


def parse_script_rewards(script: str, item_names: dict):
    """Parse '#:RewardGold 7:RewardExp 38:RewardItem 44025 1::::'
    → dict structuré. Syntaxe ':Cmd arg…' (MissionParser col20)."""
    body = script.lstrip("#:").strip()
    rewards = {"gold": 0, "exp": 0, "items": [], "reputation": [],
               "buff": [], "chooseItems": [], "other": []}
    if not body:
        return rewards
    # Les commandes se chaînent par ':' ; ':::' termine le bloc.
    for tok in body.split(":"):
        tok = tok.strip()
        if not tok:
            continue
        parts = tok.split()
        cmd, args = parts[0], parts[1:]
        if cmd == "RewardGold" and args:
            rewards["gold"] += int(args[0]) if args[0].lstrip(
                "+-").isdigit() else 0
        elif cmd == "RewardExp" and args:
            rewards["exp"] += int(args[0]) if args[0].lstrip(
                "+-").isdigit() else 0
        elif cmd == "RewardItem" and args:
            try:
                iid = int(args[0])
                qty = int(args[1]) if len(args) > 1 else 1
                rewards["items"].append(
                    {"id": iid, "qty": qty,
                     "name": item_names.get(iid)})
            except ValueError:
                pass
        elif cmd == "RewardChooseItem" and args:
            try:
                iid = int(args[0])
                qty = int(args[1]) if len(args) > 1 else 1
                rewards["chooseItems"].append(
                    {"id": iid, "qty": qty,
                     "name": item_names.get(iid)})
            except ValueError:
                pass
        elif cmd == "RewardReputation":
            rewards["reputation"].append(" ".join(args))
        elif cmd == "RewardBuff" and args:
            rewards["buff"].append(" ".join(args))
        else:
            rewards["other"].append(tok)
    return rewards


def parse_script_conditions(script: str):
    """Conditions col13 → liste 'Cmd arg…' lisible (If_CharLevel…)."""
    body = script.lstrip("#:").strip()
    if not body:
        return []
    return [t.strip() for t in body.split(":") if t.strip()]


def export_missions(limit=None):
    srows = parse_s_table("S_Mission.ini")
    tmap = parse_t_table("t_mission.ini")  # 1|3|11|28 → nom c2, map c10,
    items_t = parse_t_table("t_item.ini")  # desc c27
    item_names = {i: clean_text(v.get(8, ""))
                  for i, v in items_t.items()}
    out = []
    for r in srows:
        if len(r) < 21 or not celli(r, 0):
            continue
        rid = celli(r, 0)
        t = tmap.get(rid, {})
        name = clean_text(t.get(3, ""))
        rewards = parse_script_rewards(cell(r, 20), item_names)
        conds = parse_script_conditions(cell(r, 13))
        m = {
            "id": rid,
            "name": name or None,
            "map": clean_text(t.get(11, "")) or None,
            "minLevel": celli(r, 5),
            "maxLevel": celli(r, 6),
            "type": mission_type(cell(r, 23)),
            "desc": clean_text(t.get(28, "")) or None,
            "rewards": rewards,
        }
        if celli(r, 4):
            m["completeLimit"] = celli(r, 4)
        if celli(r, 6) & 0x10:
            m["daily"] = True
        if conds:
            m["conditions"] = conds
        if not name:
            m["untranslated"] = True
        out.append(m)
        if limit and len(out) >= limit:
            break
    return out


_CERT_ROMAN = re.compile(r"\s+(I{1,3}|IV|V|VI{0,3}|IX|X)$")


def export_certificates(limit=None):
    """Certificats de classe — les items kind 40 octroyés à la montée
    d'une classe (S_Class certsBase..+7, index = niveau/10 - 1,
    GetLicense natif dans GainClassPoint). Ce sont des BUFFS
    permanents équipés en slot — PAS des consommables : l'export
    groupe par (classe, nom de base) et fusionne les niveaux I/II
    en une entrée {levels:[{itemId,classLevel,buff}]}.
    Champs : buff (enchant de l'item col23 → T_Enchant nom/effet),
    classId/className, classLevels d'obtention, stat altérée."""
    items = {i["id"]: i for i in export_items()}
    tench = parse_t_table("T_Enchant.ini")
    grouped = {}
    order = []
    for cls in export_classes():
        for cert in (cls.get("certificates") or []):
            it = items.get(cert["itemId"])
            if it is None:
                continue
            raw_name = it.get("name") or f"#{cert['itemId']}"
            base = _CERT_ROMAN.sub("", raw_name)
            ench = it.get("useEnchant", 0)
            et = tench.get(ench, {})
            lvl = {
                "itemId": cert["itemId"],
                "name": raw_name,
                "classLevel": cert["classLevel"],
                "buff": {
                    "enchantId": ench or None,
                    "name": clean_text(et.get(6, "")) or None,
                    "effect": clean_text(et.get(57, "")) or None,
                },
            }
            key = (cls["id"], base)
            if key not in grouped:
                grouped[key] = {
                    "id": f"{cls['id']}:{base}",
                    "icon": it.get("icon"),
                    "name": base,
                    "classId": cls["id"],
                    "className": cls.get("name"),
                    "color": cls.get("color"),
                    "stat": _cert_stat(et.get(57, "")),
                    "levels": [],
                }
                order.append(key)
            grouped[key]["levels"].append(lvl)
    out = []
    combos = ee_cert_combos()
    for k in order:
        g = grouped[k]
        g["levels"].sort(key=lambda l: l["classLevel"])
        cb = combos.get(_cert_slug(g["name"]))
        if cb:
            g["combos"] = cb
        out.append(g)
    return out[:limit] if limit else out


def _cert_slug(name: str) -> str:
    """« Devoted Belief » → « devoted-belief » — id ee-tools."""
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


_EE_CERT_INDEX = None


def ee_cert_combos():
    """{slug: [{with,withClass,name,desc}]} — combos de certificats
    scrapés ee-tools (W6) : équiper cert1+cert2 octroie le buff
    `combo` (mécanique native). Indexé par slug du certificat —
    les deux participants recoivent l'entrée."""
    global _EE_CERT_INDEX
    if _EE_CERT_INDEX is not None:
        return _EE_CERT_INDEX
    idx = {}
    try:
        ref = json.load(open(EE_REFERENCE, encoding="utf-8"))
    except OSError:
        _EE_CERT_INDEX = idx
        return idx
    # id ee-tools → libellé EN officiel (leurs ids sont déjà des
    # slugs : « dragons-roar », pas le slug du nom affiché).
    labels = {c["id"]: c["name"] for c in ref.get("certificates", [])}
    for cls in ref.get("certCombos", {}).values():
        for cb in cls.get("combos", []):
            pair = [cb["certificate1Id"], cb["certificate2Id"]]
            combo = cb.get("combo", {})
            for slug in pair:
                other = pair[1] if slug == pair[0] else pair[0]
                idx.setdefault(slug, []).append({
                    "with": labels.get(other, other),
                    "name": combo.get("name"),
                    "effect": combo.get("description"),
                })
    _EE_CERT_INDEX = idx
    return idx


def _cert_stat(effect: str):
    """« P-ATK +5% » → « P-ATK » (stat altérée par le certificat —
    sert au filtre WD6)."""
    s = clean_text(effect or "")
    m = re.match(r"^(.*?)[\s+±-]*\d", s)
    return m.group(1).strip(" +") if m and m.group(1) else None


def export_spells(limit=None):
    srows = parse_s_table("S_Spell.ini")
    tmap = parse_t_table("T_Spell.ini")  # 1|17|45 → nom c16, desc c44
    out = []
    for r in srows:
        if len(r) < 18 or not celli(r, 0):
            continue
        rid = celli(r, 0)
        t = tmap.get(rid, {})
        name = clean_text(t.get(17, ""))
        s = {
            "id": rid,
            "icon": icon(cell(r, 1)),
            "name": name or None,
            "family": celli(r, 17),
            "type": celli(r, 18),
            "reqLevel": celli(r, 22),
            "castDs": celli(r, 26),
            "cooldownDs": celli(r, 27),
            "mpCost": celli(r, 32),
            "power": celli(r, 35),
            "healPower": celli(r, 36),
        }
        desc = clean_text(t.get(45, ""))
        if desc:
            s["desc"] = desc
        if cell(r, 15):
            s["category"] = cell(r, 15)
        if not name:
            s["untranslated"] = True
        out.append(s)
        if limit and len(out) >= limit:
            break
    return out


# Branche de classe — S_Class col11 (cellule ancrée certsBase-11,
# valeurs prouvées 1-5 : Warrior=1, Assassin=2, Trainer=3, Druid=4,
# Magician=5). Couleurs officielles EE des 5 branches.
BRANCH = {1: ("Defense", "yellow"), 2: ("Melee DPS", "orange"),
          3: ("Ranged DPS", "green"), 4: ("Healing & Support",
          "violet"), 5: ("Magic DPS", "blue")}

# Corrections de noms — T_Class porte des placeholders (« Ranger 2 »,
# « Sage 3 »…) pour les éveils ajoutés après coup. Noms officiels
# (wikis EN + noms Big5 S_Class) : 47 絕箭→Lethal Arrow,
# 48 聖箭→Celestial Arrow, 53 聖嘆者→Holy Lamentor (joueur),
# 54 詠訟者→Litigator (joueur), 55 龍魂→Dragon Soul,
# 56 馭龍使→Dragon Emperor, 57 黑劍→Darkness Blade,
# 58 光劍→Holy Blade (joueur), 39 幻魔操縱者→Demon Summoner
# (le 49 闇獄使 = Demon Tamer — « Demon Tamer » dupliqué sinon).
CLASS_NAME_OVERRIDES = {
    39: "Demon Summoner", 47: "Lethal Arrow", 48: "Celestial Arrow",
    53: "Holy Lamentor", 54: "Litigator", 55: "Dragon Soul",
    56: "Dragon Emperor", 57: "Darkness Blade", 58: "Holy Blade",
}


def srows_items():
    """Lignes S_Item brutes (cache — réutilisé par classes/crafts)."""
    if not hasattr(srows_items, "_cache"):
        srows_items._cache = parse_s_table("S_Item.ini")
    return srows_items._cache


def _spell_block_start(row):
    """Premier run de ≥4 cellules ≥50000 = début du bloc sorts
    (ancre du ClassStatsParser C# — les descriptions contiennent
    des '|' qui décalent la ligne)."""
    run = 0
    for i, v in enumerate(row):
        run = run + 1 if celli(row, i) >= 50000 else 0
        if run >= 4:
            return i - 3
    return -1


_REQ_CHAR_LV = re.compile(r"Character Level\s+(\d+)")
_REQ_CLASS_LV = re.compile(r"^\*?\s*(.+?)\s+(?:LV|Level)\s*(\d+)",
                           re.I)


def parse_class_requirements(text: str, id_by_name):
    """« Class Requirement: * Character Level 65 * Cleric Level 30 »
    → {charLevel, reqClasses:[{id,name,level}]}."""
    char_lv = 0
    reqs = []
    m = _REQ_CHAR_LV.search(text or "")
    if m:
        char_lv = int(m.group(1))
    for part in re.split(r"[\n|]+", text or ""):
        mm = _REQ_CLASS_LV.match(part.strip())
        if not mm:
            continue
        cname, lv = mm.group(1).strip(), int(mm.group(2))
        if cname.lower().startswith("character"):
            continue
        reqs.append({"id": id_by_name.get(cname.lower()),
                     "name": cname, "level": lv})
    return char_lv, reqs


_STAT_KEYS = ("hpPct", "mpPct", "strPct", "intPct", "conPct",
              "agiPct", "wisPct", "lckPct", "miscPct")


def export_classes(limit=None):
    """Classes jouables — race 1 uniquement (ids 1-61 ; les lignes
    101+/201+/301+/401+ sont les variantes des 4 autres races, mêmes
    classes). Layout ancré identique à ClassStatsParser :
    spellBase = 1er run ≥4 ids ≥50000 ; certs = 8 avant ; stats = 9
    avant eux ; branche = certsBase-11 ; sorts = 19 cellules
    (+0x50×4 + +0x5c×15, classés actif/passif par coût) ; talents AA
    = 6 après ; progression = 10 codes licence après."""
    srows = parse_s_table("S_Class.ini")
    tcls = parse_t_table("T_Class.ini")   # 1|2|5|6|7|8
    tsp = parse_t_table("T_Spell.ini")    # noms+desc sorts 17/45
    spell_info = {}
    for r in parse_s_table("S_Spell.ini"):
        if len(r) < 33 or not celli(r, 0):
            continue
        sid = celli(r, 0)
        t = tsp.get(sid, {})
        spell_info[sid] = {
            "name": clean_text(t.get(17, "")) or None,
            "desc": clean_text(t.get(45, "")) or None,
            "mpCost": celli(r, 32), "castDs": celli(r, 26),
            "cooldownDs": celli(r, 27)}
    id_by_name = {}
    for rid, t in tcls.items():
        if rid <= 61 and t.get(2):
            id_by_name[clean_text(t[2]).lower()] = rid
    for rid, n in CLASS_NAME_OVERRIDES.items():
        id_by_name[n.lower()] = rid
    rank_buffs = {b["classId"]: b["tiers"]
                  for b in class_rank_buffs()}
    items_t = parse_t_table("t_item.ini")
    items_s = {celli(r, 0): r for r in srows_items()}
    tench = parse_t_table("T_Enchant.ini")
    out = []
    for r in srows:
        if len(r) < 8 or not celli(r, 0) or celli(r, 0) > 61:
            continue
        rid = celli(r, 0)
        t = tcls.get(rid, {})
        name = CLASS_NAME_OVERRIDES.get(rid) \
            or clean_text(t.get(2, ""))
        sp = _spell_block_start(r)
        if sp < 0:
            sp = 30
        cb = sp - 8                       # certsBase
        branch_id = celli(r, cb - 11)
        branch = BRANCH.get(branch_id)
        parent = celli(r, 2)
        stats = {}
        for k, key in enumerate(_STAT_KEYS):
            v = celli(r, cb - 9 + k)
            if v:
                stats[key] = v
        certs = []
        licenses = []
        for i in range(8):
            cid = celli(r, cb + i)
            if cid <= 0:
                continue
            certs.append({"classLevel": (i + 1) * 10,
                          "itemId": cid})
            # « Licenses » affichées en haut de la fiche classe dans
            # le client : certificat gagné à ce palier + son buff.
            srow = items_s.get(cid)
            ench = celli(srow, 23) if srow is not None else 0
            et = tench.get(ench, {})
            icode = cell(srow, 1) if srow is not None else ""
            if icode:
                icon(icode)
            licenses.append({
                "classLevel": (i + 1) * 10, "itemId": cid,
                "name": clean_text(items_t.get(cid, {}).get(8, ""))
                    or None,
                "icon": icode or None,
                "buff": {"enchantId": ench or None,
                         "name": clean_text(et.get(6, "")) or None,
                         "effect": clean_text(et.get(57, "")) or None},
            })
        actives, passives = [], []
        for i in range(19):
            sid = celli(r, sp + i)
            if sid <= 0:
                continue
            si = spell_info.get(sid, {})
            entry = {"id": sid, "name": si.get("name"),
                     "desc": si.get("desc")}
            # passif = sort sans coût/cast/cooldown (un enchant de
            # stats déguisé — ex. « Unshakable » du Warrior).
            if (si.get("mpCost") or si.get("castDs")
                    or si.get("cooldownDs")):
                actives.append(entry)
            else:
                passives.append(entry)
        char_lv, req = parse_class_requirements(
            clean_text(t.get(8, "")), id_by_name)
        c = {
            "id": rid,
            "icon": icon(cell(r, 3)),
            "name": name or None,
            "branchId": branch_id or None,
            "branch": branch[0] if branch else None,
            "color": branch[1] if branch else None,
            "awakened": parent != rid,
            "parentClassId": parent if parent != rid else None,
            "charLevel": char_lv or None,
            "requiredClasses": req or None,
            "requirement": clean_text(t.get(8, "")) or None,
            "short": clean_text(t.get(5, "")) or None,
            "trait": clean_text(t.get(6, "")) or None,
            "desc": clean_text(t.get(7, "")) or None,
            "statPct": stats or None,
            "licenses": licenses or None,
            "certificates": certs or None,
            "skills": actives,
            "passives": passives,
            "rankBuffs": rank_buffs.get(rid),
        }
        if not name:
            c["untranslated"] = True
        out.append(c)
        if limit and len(out) >= limit:
            break
    return out


_CLASS_RANK_BUFFS = None


def class_rank_buffs():
    """S_ClassRankBuff.ini — 20 paliers de stats passives par classe
    (« talents » gagnés tous les 5 niveaux de classe : rang natif =
    floor(min(classLv,100)×0.2) — RefreshJPAward @0x8417c10 ; la
    colonne 2 = classId, groupes de 6 champs, l'enchant est le 6ᵉ,
    le 3ᵉ = level requis affiché). Retourne [{classId,tiers}]."""
    global _CLASS_RANK_BUFFS
    if _CLASS_RANK_BUFFS is not None:
        return _CLASS_RANK_BUFFS
    tench = parse_t_table("T_Enchant.ini")
    out = []
    for r in parse_s_table("S_ClassRankBuff.ini"):
        if len(r) < 8 or not celli(r, 0):
            continue
        tiers = []
        rank = 0
        for i in range(2, len(r) - 5, 6):
            rank += 1
            ench = celli(r, i + 5)
            if ench <= 0:
                continue
            et = tench.get(ench, {})
            tiers.append({
                "rank": rank, "classLevel": rank * 5,
                "reqLevel": celli(r, i + 2),
                "enchantId": ench,
                "name": clean_text(et.get(6, "")) or None,
                "effect": clean_text(et.get(57, "")) or None})
        out.append({"classId": celli(r, 1) or celli(r, 0),
                    "tiers": tiers})
    _CLASS_RANK_BUFFS = out
    return out


def _talents_from(path):
    """Parse une table talents (S_Talent serveur ou C_Talent client
    Reforged — même layout |V.2|10|) → {classId: {tree: [slots]}}.
    Chaque slot porte l'icône de son enchant (S_Enchant col1 =
    E#### → skillicon/E####.dds)."""
    rows = parse_s_table(path)
    tench = t_enchant()
    ench = enchant_rows()
    trees = {}
    for r in rows:
        if len(r) < 3 or not celli(r, 0):
            continue
        tid = celli(r, 0)
        if tid >= 1000:                 # variantes des races 2-5
            continue
        cid, tno = tid // 10, tid % 10
        if not 1 <= tno <= 3:
            continue
        slots = []
        for pos, raw in enumerate(r[2:], start=1):
            parts = raw.strip().split()
            if len(parts) < 6:
                continue
            try:
                eff, mx, req_e, req_p, tier, req_pts = (
                    int(parts[i]) for i in range(6))
            except ValueError:
                continue
            t = tench.get(eff, {})
            icode = ""
            er = ench.get(eff)
            if er is not None:
                icode = cell(er, 1).strip()
            if icode:
                icon(icode)
            slots.append({
                "pos": pos,
                "effectId": eff,
                "name": clean_text(t.get(6, "")) or None,
                "desc": clean_text(t.get(57, "")) or None,
                "maxPts": mx, "preEffect": req_e or None,
                "prePoints": req_p or None, "tier": tier,
                "reqPoints": req_pts or None,
                "icon": icode or None,
            })
        trees.setdefault(cid, {})[tno] = slots
    return trees


def export_talents(limit=None, source="S_Talent.ini"):
    """Arbres de talents AA par classe — S_Talent regroupée par
    classId (l'id natif = (raceBase*100 + classId)*10 + {1,2,3} ;
    raceBase = race-1 → les lignes <1000 sont la race 1 ; chaque
    classe a TROIS arbres — AddAAEffectPoint @0x841ebb0).
    Slot natif = « effectId maxPts preEffect prePts tier reqPoints »
    (coût par point = AARequestTable : paliers 1-5→1pt, 6-15→2pts,
    16-20→3pts ; pool = +1 aa_point par niveau de classe, capé par
    le niveau perso ≤ 100). `source` = « S_Talent.ini » (classic)
    ou le chemin C_Talent.ini Reforged."""
    trees = _talents_from(source)
    cls_names = {c["id"]: c.get("name") for c in export_classes()}
    out = []
    for cid in sorted(trees):
        out.append({
            "classId": cid,
            "className": cls_names.get(cid),
            "trees": [{"tree": n, "slots": trees[cid][n]}
                      for n in sorted(trees[cid])],
        })
        if limit and len(out) >= limit:
            break
    return out


def export_talents_reforged(limit=None):
    """Variante Reforged des arbres — C_Talent.ini du client joueur
    (reforged/EdenReforged/data/db), même layout |V.2|10| ; les
    maxPts/prérequis diffèrent de S_Talent sur ~60 % des slots
    (495/915 lignes). Servie via /api/talents?variant=reforged."""
    return export_talents(limit=limit, source=C_TALENT_PATH)


# Règles du système de talents (constantes natives — servies via
# /api/talents/rules pour le simulateur) :
#  · +1 aa_point par niveau de classe (GainAAPoint, cap 120)
#  · le niveau de classe ne peut pas dépasser le niveau du perso
#    (cap perso 100)
#  · coût d'un point d'effet : AARequestTable lapis @0x888c140 —
#    palier 1-5 = 1 pt, 6-15 = 2 pts, 16-20 = 3 pts ; coût cumulé
#    du niveau N = AARequestTableTotal[N].
TALENT_RULES = {
    "pointsPerClassLevel": 1,
    "maxCharacterLevel": 100,
    "maxAaPoints": 120,
    "costPerRank": [0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
                    3, 3, 3, 3, 3],
    "costTableTotal": [0, 1, 2, 3, 4, 5, 7, 9, 11, 13, 15, 17, 19,
                       21, 23, 25, 28, 31, 34, 37, 40],
}


ACHIEVEMENT_POINTS = re.compile(r"Achievement Points:\s*(\d+)")


def export_achievements(limit=None):
    srows = parse_s_table("S_Achievement.ini")
    tmap = parse_t_table("T_Achievement.ini")  # 1|5|15 → nom c4, desc c14
    out = []
    for r in srows:
        if len(r) < 6 or not celli(r, 0):
            continue
        rid = celli(r, 0)
        t = tmap.get(rid, {})
        name = clean_text(t.get(5, ""))
        desc = clean_text(t.get(15, ""))
        # Points : extraits du texte EN (« Achievement Points: N ») ;
        # col6 serveur = objectif/count, PAS les points (correction P3.5).
        pts = 0
        m = ACHIEVEMENT_POINTS.search(desc or "")
        if m:
            pts = int(m.group(1))
        a = {
            "id": rid,
            "icon": icon(cell(r, 3)),
            "name": name or None,
            "category": celli(r, 2),
            "points": pts,
            "target": celli(r, 6),
            "count": celli(r, 7),
            "desc": desc or None,
        }
        if not name:
            a["untranslated"] = True
        out.append(a)
        if limit and len(out) >= limit:
            break
    return out


def export_crafts(limit=None):
    """Combos d'items S_ItemCombo — matériaux c3 '#id#…#' résolus EN.
    Pas de colonne « résultat » prouvée : présenté comme ensemble."""
    srows = parse_s_table("S_ItemCombo.ini")
    tmap = parse_t_table("T_ItemCombo.ini")  # 1|3 → nom c2
    items_t = parse_t_table("t_item.ini")
    item_names = {i: clean_text(v.get(8, ""))
                  for i, v in items_t.items()}
    items_s = {celli(r, 0): cell(r, 1)
               for r in parse_s_table("S_Item.ini")
               if len(r) > 1 and celli(r, 0)}
    out = []
    for r in srows:
        if len(r) < 4 or not celli(r, 0):
            continue
        rid = celli(r, 0)
        t = tmap.get(rid, {})
        name = clean_text(t.get(3, ""))
        mats = []
        for tok in cell(r, 3).split("#"):
            tok = tok.strip()
            if not tok.isdigit():
                continue
            iid = int(tok)
            icode = items_s.get(iid, "")
            if icode:
                icon(icode)
            mats.append({"id": iid, "name": item_names.get(iid),
                         "icon": icode or None})
        c = {"id": rid, "name": name or None, "materials": mats}
        if not name:
            c["untranslated"] = True
        out.append(c)
        if limit and len(out) >= limit:
            break
    return out


def export_titles(limit=None):
    srows = parse_s_table("S_Title.ini")
    tmap = parse_t_table("T_Title.ini")  # 1|2|3|5|6
    out = []
    for r in srows:
        if len(r) < 2 or not celli(r, 0):
            continue
        rid = celli(r, 0)
        t = tmap.get(rid, {})
        name = clean_text(t.get(2, ""))
        out.append({
            "id": rid,
            "name": name or None,
            "category": clean_text(t.get(3, "")) or None,
            "bonus": clean_text(t.get(5, "")) or None,
            "desc": clean_text(t.get(6, "")) or None,
            **({"untranslated": True} if not name else {}),
        })
        if limit and len(out) >= limit:
            break
    return out


def export_npcs(limit=None):
    srows = parse_s_table("S_Npc.ini")
    tmap = parse_t_table("t_npc.ini")  # 1|3|21 → nom c2, info c20
    models = npc_model_index()       # code n→ famille modèle M###
    out = []
    for r in srows:
        if len(r) < 3 or not celli(r, 0):
            continue
        rid = celli(r, 0)
        t = tmap.get(rid, {})
        # T_NPC c2 = "Nom\nPrénom|Role" — ex. "Mining Captain\nLaz|Quest".
        # On scinde : nom affiché (espaces), rôle séparé (detail page).
        raw = clean_text(t.get(3, ""))
        name, _, role = raw.partition("|")
        name = " ".join(name.split())
        role = role.strip() or None
        n = {"id": rid, "code": cell(r, 1), "name": name or None,
             "info": clean_text(t.get(21, "")) or None}
        # Portrait : le code nXXXXXX résout son modèle m### via
        # NPCList.ini → même famille que les mobs → icône M###.
        fam = models.get(n["code"] or "", "")
        if fam and icon_file_exists(fam):
            n["icon"] = icon(fam)
        if role:
            n["role"] = role
        if not name:
            n["untranslated"] = True
        out.append(n)
        if limit and len(out) >= limit:
            break
    return out


def export_maps(limit=None):
    srows = parse_s_table("S_Node.ini")
    tmap = parse_t_table("t_node.ini")  # 1|2|49|51|… → nom c1, desc c48
    out = []
    for r in srows:
        if len(r) < 4 or not celli(r, 0):
            continue
        rid = celli(r, 0)
        t = tmap.get(rid, {})
        name = clean_text(t.get(2, ""))
        # code map de la scène (Sxxx) — jointure avec monsters.maps[].
        code = next((c for c in r if re.match(r"^S\d{3}$", c.strip())),
                    None)
        n = {"id": rid, "name": name or None,
             "type": celli(r, 3),
             "mapCode": code,
             "desc": clean_text(t.get(49, "")) or None}
        if not name:
            n["untranslated"] = True
        out.append(n)
        if limit and len(out) >= limit:
            break
    return out


DATASETS = {
    "items": export_items,
    "certificates": export_certificates,
    "monsters": export_monsters,
    "missions": export_missions,
    "spells": export_spells,
    "classes": export_classes,
    "talents": export_talents,
    # Variante Reforged (C_Talent client) — dataset technique servi
    # par /api/talents?variant=reforged, exclu du rail wiki.
    "talents_reforged": export_talents_reforged,
    "achievements": export_achievements,
    "crafts": export_crafts,
    "titles": export_titles,
    "npcs": export_npcs,
    "maps": export_maps,
}


def _convert_one(job):
    """(src_path, dst_path) → convertit DDS/TGA/PNG → WebP 64px."""
    src, dst = job
    try:
        from PIL import Image
        im = Image.open(src)
        if im.mode != "RGBA":
            im = im.convert("RGBA")
        im.save(dst, "WEBP", quality=85)
        return True
    except Exception:
        return False


def convert_icons():
    """Convertit tous les codes référencés vers public/icons/<CODE>.webp.
    Recherche itemicon → skillicon → uiicon (dds, png, tga)."""
    os.makedirs(OUT_ICONS, exist_ok=True)
    jobs = []
    missing = []
    for code in sorted(ICONS):
        src = None
        for sub in ICON_SETS:
            for ext in (".dds", ".png", ".tga"):
                p = os.path.join(UI_DIR, sub, code + ext)
                if os.path.exists(p):
                    src = p
                    break
            if src:
                break
        if not src:
            missing.append(code)
            continue
        dst = os.path.join(OUT_ICONS, code + ".webp")
        if os.path.exists(dst) and \
                os.path.getmtime(dst) >= os.path.getmtime(src):
            continue  # déjà converti
        jobs.append((src, dst))
    done = 0
    if jobs:
        with ProcessPoolExecutor(max_workers=8) as ex:
            for ok in ex.map(_convert_one, jobs, chunksize=64):
                done += ok
    print(f"icons: {done} converted / {len(ICONS)} referenced "
          f"/ {len(missing)} missing")
    if missing:
        print("  missing sample:", ", ".join(missing[:20]))


def main():
    args = set(sys.argv[1:])
    limit = None
    for a in list(args):
        if a.startswith("--limit="):
            limit = int(a.split("=", 1)[1])
            args.discard(a)
    icons_only = "--icons-only" in args
    json_only = "--json-only" in args

    os.makedirs(OUT_JSON, exist_ok=True)
    index = {}
    if not icons_only:
        for name, fn in DATASETS.items():
            rows = fn(limit=limit)
            path = os.path.join(OUT_JSON, name + ".json")
            with open(path, "w", encoding="utf-8") as f:
                json.dump(rows, f, ensure_ascii=False,
                          separators=(",", ":"))
            nt = sum(1 for r in rows if r.get("untranslated"))
            index[name] = {"count": len(rows), "untranslated": nt}
            print(f"{name:14s} {len(rows):6d} rows "
                  f"({nt} untranslated) → {path}")
        with open(os.path.join(OUT_JSON, "index.json"), "w",
                  encoding="utf-8") as f:
            json.dump({"version": 2, "datasets": index}, f, indent=1)

    if not json_only:
        # Convertit uniquement les icônes référencées — relire les
        # JSON si --icons-only a sauté l'export.
        if icons_only:
            for name in DATASETS:
                p = os.path.join(OUT_JSON, name + ".json")
                if not os.path.exists(p):
                    continue
                with open(p, encoding="utf-8") as f:
                    for row in json.load(f):
                        if row.get("icon"):
                            ICONS.add(row["icon"])
                        for m in row.get("materials", []):
                            if m.get("icon"):
                                ICONS.add(m["icon"])
        convert_icons()


if __name__ == "__main__":
    main()
