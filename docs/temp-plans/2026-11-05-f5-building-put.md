# Plan F5 — pose de bâtiment VDG : tid S_Buliding natif + housekeeper auto

Date : 2026-11-05 · Lot : mission craft & guilde, phase F.

## Preuves natives

- `CItemFactory::FamilyBuildingPutItemFromInventroy` @0x808a770 (zone) :
  - branche bâtiment : `GetBuildingData(IGameData+0x170, CItem+0x124)` —
    +0x124 = `CItemData+0x12c` (col conditionnelle row[35], kinds
    0x29/0x2a/0x45-0x4d dans `CItemQuery::Deserialize` @0x86038xx).
  - `while (fam.level != bld.level) def = GetBuildingData(def+0x30)`
    pour les types dont `type-4 (unsigned) > 5` → types {1,2,10}
    (EDENFR) — +0x30 = lien **next** de chaîne (pas de colonne :
    construit par inversion des `prev`), échec « No Data » sinon.
  - INSERT `tid = S_Buliding tid` (pas l'item contrat), `item_tid =
    item`, durabilité = `def+0x20` (col8 = max_durability), niveau =
    `def+0x10` (col4), position = 0,0,0 pour le bâtiment.
  - Housekeeper auto : si `def+0x18` (col6 = item NPC) ≠ 0 → lookup
    item NPC → `CNpcItem+0x124` = tid template NPC → position
    `GetNpcPositionByNpcLoc(loc+1, house_code)` → INSERT 0x2a à loc+1
    (item_tid = item NPC, tid = template NPC). Échec position → put
    refusé entièrement.
- Données EDENFR (GBK, lignes jointes) : kind-41 35071→1 (H00101),
  35073→51, 35075-77→107-109, 35078-80→110-112, 35081-86→101-106 ;
  kind-42 35001-35012 → NPC 53007-53018.
- `family_building_container.tid` : `CItemDBO::GetAllFamilyBuildingItems`
  (zone l.716695) lookup `GetBuildingData(proto+0x12c)` → tid =
  S_Buliding confirmé. Notre GM `give_building` stocke déjà ça.

## Écart actuel

`HandleFamilyContainerPutAsync` écrit `storeTid = item.ItemId` pour
0x29 → tid invalide pour `GameData.Buildings` → bâtiment zombie
(GetDef toujours null, chaîne de level-up morte, mauvais tid dans 487)
et pas de housekeeper auto.

## Implémentation

1. `ItemDef.FamilyBuildingTid` : row[35] pour kind 0x29 (= S_Buliding
   tid) — même colonne conditionnelle que `FamilyNpcTid` (0x2a/0x52).
2. `GameDataService` : vue `prev → next` lazy pour le walk natif
   (ou helper `NextBuilding(tid)`).
3. `HandleFamilyContainerPutAsync` branche 0x29 conteneur 0 :
   - résoudre `def.FamilyBuildingTid` → bdef ; 0/absent → rejet muet ;
   - types {1,2,10} : remonter les `next` jusqu'à `Level == guild.Level`,
     échec → rejet ; types 4-9 : tid direct ;
   - INSERT tid résolu, item_tid = item, durabilité = MaxDurability,
     level = def.Level, position résolue hall ;
   - housekeeper : `bdef.NpcItemTid > 0` → item NPC → `FamilyNpcTid` →
     position `ResolveHallSlot(loc+1, bdef.Code)` → INSERT 0x2a loc+1
     + `InjectHallNpc` dans la vue du poseur ; échec position → rejet.
4. Tests unitaires : résolution contrat→bâtiment, walk niveau famille,
   housekeeper inséré à loc+1, rejet contrat sans lien, conteneur 1
   (stockage) sans housekeeper.

## Bilan (2026-11-05) — IMPLÉMENTÉ + TESTÉ

Réalisé (en plus des 4 étapes prévues) :

- `ItemParser` : `FamilyBuildingTid` (col35, kind 0x29) +
  `FamilyNpcTid` (col35, 0x2a/0x52). Données réelles vérifiées par
  test : 35081→101, 35071→1, 35001→53007.
- `GameDataService.NextBuildingTid` : map prev→next inversée (lien
  `def+0x30` natif).
- `FamilyHallRules.ResolveBuildingPut` : types 4-9 → échelon direct ;
  types {1,2,10} → walk des next jusqu'à `def.Level == guild.Level`,
  chaîne épuisée → null (rejet muet natif).
- `HandleFamilyContainerPutAsync` (ZoneEntryFlow.FamilyInstance.cs) :
  - `storeTid` = tid S_Buliding résolu (0x29) / tid NPC (0x2a/0x52) ;
  - **position 0,0,0 pour les lignes 0x29** (littéraux 0.0 natifs —
    correction vs la version initiale qui calculait une position) ;
  - ligne NPC : durability = `itemdef+0xc` (`def.MaxDurability`),
    level = proto NPC `+0xc` (`GameData.Npcs` par storeTid) ;
  - housekeeper auto à loc+1 : item_tid = `bdef.NpcItemTid`, tid =
    `FamilyNpcTid` du contrat NPC, level = `npcTemplate.Level`,
    **due_date = durée du contrat** (DueDateFor 0x2a), position via
    `ResolveHallSlot(loc+1, bdef.Code)` + spawn `InjectHallNpc` +
    push 298 aux membres du hall ; échec de résolution → log
    `[Zone-Family]` (le natif rejette le put entier — divergence
    documentée : la ligne est déjà persistée à ce stade) ;
  - refresh 484 au demandeur + `PushContainerRefreshToHallMembersAsync`
    (repli du `FamilyContainerUpdate` natif 485/486 — Execute
    @0x8315cb0).
- **Correction gate holy** (découverte relecture) : le natif teste le
  SUBTYPE (virtual itemdef+0x10 == 0x16 = col9 `EquipLoc`), pas le
  kind — l'ancien code `kind != 0x16` refusait les 8 items sacrés
  EDENFR (26217-26272, kind 0x52, col9=22, col35→53584+ records holy)
  et aurait admis 68 armes kind-22. Corrigé en `def.EquipLoc != 0x16`.
- **Position holy** : `GetHolyItemPosition` @0x8465d60 = record
  [position] aire 0x38 **brut** (pas d'offsets 4PHouse) — appliqué
  via `houseCode=null` dans `ResolveHallSlot` ; couvre aussi un NPC
  0x2a posé au holy loc (branche 0x14 du natif).

Preuves :
- `dotnet build Eden.sln` : 0 erreur 0 warning.
- `dotnet test --filter "FamilyHall|ResolveBuildingPut"` : 18/18
  (5 nouveaux `ResolveBuildingPutTests` : direct 4-9, walk maison
  niveau famille, échec fin de chaîne, contrat inconnu, données
  EDENFR réelles c_item).
- Suite complète : **1778/1779** — seul échec `GrantExp_Level30Repro`
  (throw de debug pré-existant, non lié).

## Test live headless (2026-11-05) — PROUVÉ serveur

Scénario `/tmp/f5_put_v3.py` (RSA/RC4, `EDEN_TRANSPORT=legacy`) :
perso 200000002, famille 5000 lv5 house_open=501, contrat 35081
sac 0 loc 22.

1. Entrée zone + NCID 176(fam=5000) + MigrationDone → node 501.
2. `[FamilyHall] spawn … guild=lv5 house=501` + `housekeeper bld=3
   npcItem=35001 npcTid=53007 pos=106,4,124,0` — injecté en Replayed.
3. Marche réelle : le housekeeper est **derrière un mur nodemap**
   (S501.smp) — chemin BFS obligatoire (~230 cases : nord le long de
   x=115 puis contour ouest de la maison). Le serveur prend `f[0]`
   (cur) comme position finale → un move `cur=cur` en fin de parcours.
4. NCID 10 (handle 0x88c8…) → **285** : option 0 = displayId **4521**
   (family building), option 1 = 4568.
5. NCID 11 (index 0) → **483** (refresh conteneur).
6. NCID 130 (container 0, loc 0, sac 0, slot 22) → **484** reçu.

Persistance vérifiée (`docker compose exec postgres psql`) :
- `family_building_container` fam 5000 : ligne `tid=101` (H00401),
  item_type=41, loc=0, x=y=face=0, level=3, durability=200 — exactement
  le schéma natif ; + ligne housekeeper auto `tid=53008` (item 35002),
  item_type=42, loc=1, pos (133,19,198,83) face 0,785.
- `inventory1` : le contrat 35081 a quitté le loc 22 (consommé).
- `item_tid` affiché `-30455`/`-30534` = u16 natif stocké dans un int
  signé — wire identique, cosmétique DB.
- Réentrée (`/tmp/f5_reentry.py`) : les DEUX housekeepers 53007 (base)
  et 53008 (bât. 101) respawnent depuis l'état persisté et entrent en
  vue aux bonnes positions.

Attribution : serveur prouvé de bout en bout (headless). Reste côté
client Godot : le housekeeper/le bâtiment doivent être visibles et
cliquables en jeu, le panneau family_building doit s'ouvrir sur
l'option 4521, et le modèle 3D du bâtiment posé n'est pas encore
rendu (F6+). Validation visuelle Flameshot en phase G / §2.
