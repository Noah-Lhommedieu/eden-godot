# Lot 10 — notes G1-G4 (serveur/jeu)

Agent : Eden.Zone/Eden.Core — 2026-10-24.

## G1 — NPC manquants (Gisèle & autres)

**Convention réelle du mask Locate (preuve désasm + data)**
- `CMonsterDBO::GetAllNPCTemplate` @0x08368260 : `mask==0 ||
  ((mask >> (CGlobalInfo::Locate & 0x1f)) & 1) != 0` → template créé.
- `CNpcData+0x38` = token 19 du record S_Npc (`CNpcQuery::Deserialize`
  @0x86103xx — tok19 lu juste après le vecteur de 8 scripts). En C# :
  `body[17]` (body = ligne 2 du record ; body[k] = token k+2 car la
  colonne nom/titre est scindée par un saut de ligne interne).
- `ConvertLocate` @0x08589260 : HK=1,JP=2,CN=3,MY=4,US=5,KR=6,FR=7,
  DE=8,ES=9,PT=10,RU=11,TH=12,SG=13,ID=14,VN=15. setup.ini = US → bit5.
- Preuve que bit5=US : 51254-6 « 美版專用NPC » ont mask=160 (US|FR).

**Audit data** : 90 templates masqués sur 2068 ; 33 spawns scène
filtrés sous Locate=US (S001×27, S004×2, S031×3, S096×1, S224×1).
Gisèle = 51283 (« Eden Gem Merchant », S001 (312.09,374.59)) — son
mask = **64 (bit KR)**. Elle n'aurait donc pas spawné non plus sur le
natif *avec cette base* : notre S_Npc est une data internationale où
le mask marque la région d'origine du NPC, pas sa présence en US.

**Discriminateur trouvé** : `client/data/db/T_Npc(1).ini` (client US,
2647 ids) contient **37 des 90** masqués — dont TOUS les NPC
« normaux » (Gisèle, Aven Knight 50890, Knowledge Researcher 50590,
Grandma 50430, Class Master 50955, téléporteurs 53288/53292,
Eden Envoys 51273/4…) ; les **53 restants** sont des festivals
CN/HK/JP/KR (Santa, Nouvel An, Qixi, Olympiques, pumpkins…) absents
de la localisation US. Le client US a aussi la même scène S001 que le
serveur (Gisèle y figure).

**Fix** : `ZoneLiveWorld.UsContentNpcIds` (les 37 ids) — `NpcSpawnable`
= mask visible OU tid ∈ allowlist. Effet : 13 spawns scène restaurés
(Gisèle + 12 autres), 20 spawns festival restent filtrés.
Reste à vérifier en jeu : Gisèle présente à Aven, pas de retour des
festivals ; les masks MOBS (S_Monster col59, 24 lignes filtrées)
suivent la même convention — si un mob « important » manque, même
règle via T_Monster client possible (audit non fait — demandé si
besoin).

## G2 — NPC tous tournés pareil

**Preuve** : `CNC_CZ_ClientSeeMob` ctor @0x849df90 + appel
`CMonster::OnSeen` @0x849e100 : les 4 floats = **x, y(ground),
dir(entité+0x58), GetMovement(vtable+0xd8)** — PAS x,y,z,dir.
Capture : NPC 50260 scène S002 `(369.885,275.426,facing 5.49779)` →
floats wire `[369.885,275.426,5.4978,0.0]` ; les 8 cas golden
ncid_298 confirment (float3=facing .ini, float4=0 pour NPC).
`GetMovement` = `CCharFormula::Movement` @0x83f3e10 = attr+0xa0 ×
template speed : idle → template+0x1c = **S_Monster col34 ×0.5**
(roam), flag +0x3c&0x10 → +0x18 = **col35 ×0.5** (battle) ;
`CElfPet::GetMovement` → +0x18 aussi. `DAT_087cc688`=0.5. Pas de
hauteur sur le fil — le client la déduit du terrain.

**Bug** : `BuildSeeMob` écrivait `Z=e.Z` (0 pour NPC) en float3 et
`Facing` en float4 → dir vue = 0 pour tous les NPC (et vitesse =
facing pour les mobs).

**Fix** : `Z=facing`, `Facing=moveSpeed` (+ param `moveSpeed` :
RoamSpeed mob, BattleSpeed elf-pet, 0 NPC/statues).
`MonsterDef.RoamSpeed/BattleSpeed` ajoutés (cols 34/35 ×0.5).
Commentaires .g.cs corrigés (Z=dir, Facing=speed).

## G3 — NPC de la 1re map sur les autres maps

**Cause** : le replay d'entrée (capture faite sur node 2) indexe les
298 capturés dans `st.Replayed` à CHAQUE login (`NodeChanged` n'est
posé qu'à la migration portail, pas au login). `UpdateSightAsync`
réémet ces entités dès que le joueur est à ±48 de leur position
capturée — les coordonnées des maps se recoupent → les NPC de la map
de capture apparaissent sur n'importe quelle map en plus des siens
(et étaient aussi envoyés en clair dans le dump MigrationDone).

**Fix** : `HandleMigrationDoneAsync` — les 298 du replay ne sont
rejoués/indexés que si `st.NodeId == ReplayNodeId` (2) ou nœud
inchangé (login direct sur la map de capture).

## G4 — Sacred Duty (3e itération)

**Vérifié conforme au natif** : gate de palier
(`AddAAEffectPoint` @0x841ebb0 — somme `AARequestTableTotal[pts]` des
slots byte+7 ≤ palier sur les 3 branches), coût AARequestTable, pool
char+0xa1c (classId=0) vs SCharacterClass+0xc (classId≠0), wire
`[s16 effectId][s16 classId]` ✓.

**Bug trouvé** : `TrySpendAaPointAsync` (path classId≠0) décrémentait
`player_classlist.aa_point` SANS resynchroniser
`player_characters.aa_point` quand la classe ciblée = classe active
→ les deux pools divergeaient : classlist[active] tombait à 0 pendant
que le pool affiché (attr 0x31) restait positif → talents refusés
avec des points visiblement dispo. Fix : miroir UPDATE
`player_characters` quand `class_id=@ClassId`.

**Autres talents même cause** : TOUS les talents d'arbre de classe
sont impactés pareil (le refus n'est pas propre à Sacred Duty) — un
talent de la classe courante demandé avec classId≠0 vidait le pool
classlist sans toucher l'affiché. Le diagnostic fin dépend du
classId réellement émis par le client : logs ajoutés sur les 3 chemins
de refus silencieux (slot introuvable + treeBase, pré-requis,
palier déjà logué).

**Reste à vérifier en jeu** : validation Sacred Duty (tree 41,
slot `54813 10 0 0 1 25` pour clerc humain ; 40182/40210 arbres
271/281 classes avancées). Si toujours refusé → le client ne
transmet probablement pas (gate UI client-side sur les points
cumulés affichés — vérifier compteur « X/25 » de l'arbre et les
logs `[Zone] SetAAPoint`).

## Vérifs

- `dotnet build` : 0 erreur, 0 warning.
- `dotnet test` : 1088/1088 verts (254 cas golden 298 inclus — le
  round-trip est inchangé, seuls les champs passés changent).
