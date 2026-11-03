#!/usr/bin/env python3
"""Audit id-par-id : ids que le serveur référence vs ids des tables client.

Usage: ini_audit.py [--json out.json] [--missing out.txt]

Corpora (codés en dur — passer en argv si besoin) :
  SRV = eden_server/server_us/Data/db_decoded + scene/
  FR  = Téléchargements/EDENFR/extracted (data/db complet, vérifié vs manifeste)
  US  = eden_server/client

Formats gérés : pipe `|V.x|N|`, comma `V.x,N,`, `[INFO]`, count `N|cols`,
Big5/cp950. Fichiers lus en octets.
"""
import re, os, sys, json, glob, collections

ROOT = '/home/ster2lune/Bureau/reverseden'
SRV = ROOT + '/eden_server/server_us/Data/db_decoded'
SCENE = ROOT + '/eden_server/server_us/Data/scene'
FR = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith('--') \
    else ROOT + '/portage/corpus/official'
US = ROOT + '/eden_server/client'

# ---------- parsing ----------

def read(path):
    try: return open(path, 'rb').read()
    except OSError: return b''

def pipe_ids(path):
    """ids en col1 d'une table `id|...`."""
    return {int(m.group(1)) for m in re.finditer(rb'(?m)^(\d+)\|', read(path))}

def pipe_rows(path):
    """{id: [fields]} pour table pipe."""
    out = {}
    for l in read(path).split(b'\n'):
        m = re.match(rb'^(\d+)\|', l)
        if m: out[int(m.group(1))] = l.rstrip(b'\r\n').split(b'|')
    return out

def comma_ids(path):
    return {int(m.group(1)) for m in re.finditer(rb'(?m)^(\d+),', read(path))}

def nums(line, lo=0, hi=999999):
    for m in re.finditer(rb'[|,](\d+)[|,]', line + b'|'):
        v = int(m.group(1))
        if lo <= v <= hi: yield v

def find(root, *names):
    """cherche un fichier data/db peu importe la casse."""
    for n in names:
        for base in (root + '/data/db/', root + '/data/Db/', root + '/data/DB/'):
            p = base + n
            if os.path.exists(p): return p
    # fallback scan
    try:
        for f in os.listdir(root + '/data/db'):
            if f.lower() in [n.lower() for n in names]:
                return root + '/data/db/' + f
    except OSError: pass
    return None

# ---------- corpus client ----------

def client_ids(root):
    db = root + '/data/db'
    c = {}
    for key, names in {
        'item': ['c_item.ini'],
        'itemmall': ['c_itemmall.ini'],
        'itemchest': ['c_itemchest.ini'],
        'itemcombo': ['c_itemcombo.ini'],
        'monster': ['c_monster.ini'],
        'npc': ['c_npc.ini'],
        'mission': ['c_mission.ini'],
        'dialogue': ['c_dialogue.ini'],
        'partner': ['c_partner.ini'],
        'spell': ['c_spell.ini', 'C_Spell.ini'],
        'enchant': ['c_enchant.ini'],
        'node': ['c_node.ini'],
        'title': ['c_title.ini'],
        'achievement': ['c_achievement.ini'],
        'collection': ['c_collection.ini'],
        'collectionpoint': ['c_collectionpoint.ini'],
        'transport': ['c_transport.ini', 'C_Transport.ini'],
        'store': ['c_store.ini', 'C_Store.ini'],
        'dailyaward': ['C_DailyAward.ini', 'c_dailyaward.ini'],
        'talent': ['c_talent.ini'],
        'classrankbuff': ['c_classrankbuff.ini'],
        'equipset': ['c_equipset.ini', 'C_EquipSet.ini'],
        'dropitem': ['c_dropitem.ini'],
        'lifeskill': ['c_lifeskill.ini'],
        'ridelist': ['ridelist.ini', 'RideList.ini'],
        'npclist': ['npclist.ini', 'NPCList.ini'],
    }.items():
        ids = set()
        for n in names:
            p = find(root, n)
            if p: ids |= pipe_ids(p)
        c[key] = ids
    c['item_all'] = c['item'] | c['itemmall'] | c['itemchest'] | c['itemcombo']
    # scenes clientes : data/scene/*.ini + map/model/S*/
    c['scene_ini'] = {re.match(r'[sS](\d+)', f).group(1) for f in
                      (os.path.basename(p) for p in glob.glob(root + '/data/scene/*.ini'))
                      if re.match(r'[sS](\d+)', f)}
    c['scene_map'] = set()
    for pat in (root + '/map/model/[sS]*', root + '/map/Model/[sS]*'):
        for d in glob.glob(pat):
            m = re.match(r'[sS](\d+)', os.path.basename(d))
            if m: c['scene_map'].add(m.group(1))
    c['scene_map'] |= {re.match(r'[sS](\d+)\.smp', f).group(1) for f in
                       (os.path.basename(p) for p in glob.glob(root + '/map/sknodemap/*.smp'))
                       if re.match(r'[sS](\d+)\.smp', f)}
    # modèles présents (monster/model m*, ride/model r*)
    c['models'] = set()
    for pat in ('/monster/model/*', '/Monster/model/*', '/monster/*', '/ride/*', '/char/*'):
        for f in glob.glob(root + pat):
            b = os.path.basename(f)
            if b.lower().endswith(('.nif', '.kfm', '.glb')):
                c['models'].add(b.lower())
    return c

# ---------- référentiel serveur ----------

def server_refs():
    """{canal: {type_cible: set(ids)}} — ce que le serveur envoie/exécute."""
    R = collections.defaultdict(lambda: collections.defaultdict(set))

    # ---- missions ----
    for l in read(SRV + '/S_Mission.ini').split(b'\n'):
        m = re.match(rb'^(\d+)\|', l)
        if not m: continue
        mid = int(m.group(1))
        R['mission']['mission'].add(mid)
        for g in re.finditer(rb'(?:ItemCollect|AcceptInventoryItem|ItemGiven|NPCItem|RewardItem|RewardChooseItem) ([0-9]+)', l):
            R['mission']['item'].add(int(g.group(1)))
        for g in re.finditer(rb'MonsterTreasureGot ([0-9]+) [0-9]+ ([0-9]+)', l):
            R['mission']['item'].add(int(g.group(2)))
        for g in re.finditer(rb'MonsterKilled ([0-9]+)', l):
            R['mission']['monster'].add(int(g.group(1)))
        for g in re.finditer(rb'[|,](5[0-9]{4})[|,]', l):
            R['mission']['npc'].add(int(g.group(1)))

    # ---- npc ----
    for l in read(SRV + '/S_Npc.ini').split(b'\n'):
        m = re.match(rb'^(\d+)\|', l)
        if not m: continue
        R['npc']['npc'].add(int(m.group(1)))
        for g in re.finditer(rb'Talk ([0-9 ]+)', l):
            for v in g.group(1).split():
                R['npc']['dialogue'].add(int(v))
        for g in re.finditer(rb'Service ([0-9]+)', l):
            R['npc']['store'].add(int(g.group(1)))
        for g in re.finditer(rb'(?:TerritoryAward|InventoryItem|If_InventoryItem) ([0-9]+)', l):
            R['npc']['item'].add(int(g.group(1)))

    # ---- monstres ----
    R['monster']['monster'] = pipe_ids(SRV + '/S_Monster.ini')

    # ---- drops ----
    for f in ['S_DropItem.ini', 'S_ClassDropItem.ini']:
        for v in nums(read(SRV + '/' + f)):
            R['drops']['item'].add(v)

    # ---- item chest outputs (ids présents dans S_Item/S_ItemMall seulement — filtré ensuite) ----
    for v in nums(read(SRV + '/S_ItemChest.ini')):
        R['itemchest']['item'].add(v)

    # ---- combos ----
    for v in nums(read(SRV + '/S_ItemCombo.ini')):
        R['itemcombo']['item'].add(v)

    # ---- items + enchants référencés ----
    items = pipe_ids(SRV + '/S_Item.ini') | pipe_ids(SRV + '/S_ItemMall.ini')
    R['item']['item'] = items
    for f in ['S_Item.ini', 'S_ItemMall.ini']:
        for l in read(SRV + '/' + f).split(b'\n'):
            for g in re.finditer(rb'[|,]([0-9]{4,6})[|,]', l):
                R['item']['enchant?'].add(int(g.group(1)))

    # ---- enchants / rides ----
    for f in ['S_Enchant.ini', 'S_ItemMallEnchant.ini']:
        for l in read(SRV + '/' + f).split(b'\n'):
            m = re.match(rb'^(\d+)\|', l)
            if m: R['enchant']['enchant'].add(int(m.group(1)))
            for g in re.finditer(rb'2134[|,]([0-9]+)', l):
                R['enchant']['ride'].add(int(g.group(1)))

    # ---- shops ----
    for l in read(SRV + '/S_Store.ini').split(b'\n'):
        f = l.split(b'|')
        for i in range(1, len(f) - 1, 3):
            if re.match(rb'^\d+$', f[i] or b''):
                R['store']['item'].add(int(f[i]))
    R['store']['store'] = pipe_ids(SRV + '/S_Store.ini')

    # ---- life skill ----
    for v in nums(read(SRV + '/S_LifeSkill.ini')):
        R['lifeskill']['item'].add(v)

    # ---- collection / daily / family / reward / rank / lottery / luckybag ----
    for v in nums(read(SRV + '/S_Collection.ini')):
        R['collection']['item'].add(v)
    R['collection']['collection'] = pipe_ids(SRV + '/S_Collection.ini')
    for v in nums(read(SRV + '/S_CollectionPoint.ini')):
        R['collectionpoint']['item'].add(v)
    for v in nums(read(SRV + '/S_DailyAward.ini')):
        R['dailyaward']['item'].add(v)
    for v in nums(read(SRV + '/S_FamilyCombine.ini')):
        R['familycombine']['item'].add(v)
    for v in nums(read(SRV + '/S_RewardScore.ini')):
        R['rewardscore']['item'].add(v)
    for v in nums(read(SRV + '/S_RankAward.ini')):
        R['rankaward']['item'].add(v)
    for v in nums(read(SRV + '/S_Lottery.ini')):
        R['lottery']['item'].add(v)
    for v in nums(read(SRV + '/S_LuckyBag.ini')):
        R['luckybag']['item'].add(v)

    # ---- partenaires ----
    R['partner']['partner'] = pipe_ids(SRV + '/S_Partner.ini')
    # œufs kind-60 dans S_ItemMall : type/kind col — chercher lignes contenant 60 dans le champ kind
    for l in read(SRV + '/S_ItemMall.ini').split(b'\n'):
        f = l.split(b'|')
        if len(f) > 3 and f[3] == b'60':
            m = re.match(rb'^(\d+)', l)
            if m: R['partner']['egg_item'].add(int(m.group(1)))

    # ---- sorts ----
    R['spell']['spell'] = pipe_ids(SRV + '/S_Spell.ini')
    for v in nums(read(SRV + '/S_Spell.ini'), 10000, 69999):
        R['spell']['item?'].add(v)

    # ---- zones / transport / titres / succès ----
    R['node']['node'] = pipe_ids(SRV + '/S_Node.ini')
    R['transport']['transport'] = pipe_ids(SRV + '/S_Transport.ini')
    R['title']['title'] = pipe_ids(SRV + '/S_Title.ini')
    R['achievement']['achievement'] = pipe_ids(SRV + '/S_Achievement.ini')

    # ---- scènes : spawns + portails ----
    for f in glob.glob(SCENE + '/*.ini'):
        d = read(f)
        for v in re.finditer(rb'[|,\r\n]([0-9]{5})[|,\r\n]', d):
            R['scene']['entity'].add(int(v.group(1)))
        m = re.match(r'[sS](\d+)', os.path.basename(f))
        if m: R['scene']['scene'].add(m.group(1))
    return R

# ---------- rapport ----------

def main():
    print('[*] parsing corpus FR…'); fr = client_ids(FR)
    print('[*] parsing corpus US…'); us = client_ids(US)
    print('[*] parsing références serveur…'); R = server_refs()

    target = {'item': 'item_all', 'npc': 'npc', 'monster': 'monster',
              'mission': 'mission', 'dialogue': 'dialogue', 'partner': 'partner',
              'spell': 'spell', 'enchant': 'enchant', 'node': 'node',
              'title': 'title', 'achievement': 'achievement',
              'collection': 'collection', 'transport': 'transport',
              'store': 'store', 'scene': 'scene_map'}

    report = {}
    for canal, refs in R.items():
        for kind, ids in refs.items():
            tgt = target.get(kind)
            if not tgt: continue
            fr_ids = fr.get(tgt, set()); us_ids = us.get(tgt, set())
            known_srv = ids & (R['item']['item'] | fr_ids | us_ids) if kind == 'item' else ids
            # pour les items référencés : ne garder que ceux qui existent comme items serveur ou client
            if kind == 'item':
                known_srv = {v for v in ids if v in R['item']['item'] or v in fr['item_all'] or v in us['item_all']}
            elif kind == 'store':
                known_srv = {v for v in ids if v in R['store']['store'] or v >= 400000}
            else:
                known_srv = ids
            miss_fr = sorted(known_srv - fr_ids)
            report[f'{canal}.{kind}'] = {
                'refs': len(known_srv),
                'missing_fr': miss_fr,
                'missing_also_us': sorted(set(miss_fr) - us_ids),
            }

    for k, v in sorted(report.items()):
        n = len(v['missing_fr']); nu = len(v['missing_also_us'])
        if n or v['refs']:
            print(f'{k:26s} refs={v["refs"]:6d}  manquants FR={n:5d}  (dont absents US aussi={nu})')
            if n and n <= 40: print('   ', v['missing_fr'][:40])

    if '--json' in sys.argv:
        out = sys.argv[sys.argv.index('--json') + 1]
        json.dump(report, open(out, 'w'), indent=1)
        print('[*] json →', out)

if __name__ == '__main__':
    main()
