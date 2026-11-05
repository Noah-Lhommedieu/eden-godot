# R1 — Logique native guilde (FAMILY) + VDG, côté zone

Recon du 2026-11-03. Source : `re/ghidra/zone/decompiled.c` (ZoneServer i386,
symboles `lapis::*`). Binaire : `eden_server/server_us/ZoneServer/ZoneServer`.
Chaque fait cite `zone @ 0x<addr>` + symbole + ligne decompiled.c.
Statuts : [VERIFIED] = lu dans le corps décompilé ; [HYPOTHESIS] = déduit ;
[GAP] = hors zone (mission/world/DB agent) ou non tranché.

## 1. Structures côté zone

[VERIFIED] `SFamily` (alloué 0x100 octets, `SFamily::SFamily` zone @ 0x083527d0,
l.711420+) — offsets observés :

| Off | Rôle | Preuve |
|---|---|---|
| +0x04 | familyID | `NewFamily` zone @ 0x08101950 l.147136 |
| +0x0c | nom (std::string) | idem ; recherché par nom dans `TC_VisitFamilyInstance` zone @ 0x083cab… l.751891 |
| +0x10 | charID du chef (comparé à `char+0x34`) | `FamilyStatue::Execute` zone @ 0x08257f10 l.452147 ; `FamilyContainerPutItem::Execute` zone @ 0x08255bf0 l.449242 |
| +0x18 | niveau guilde (char) | `TC_FamilyLevelUp` zone @ 0x083c1350 l.765504 ; storage tab gates |
| +0x1c | leader/member field renvoyé au client | `CNE_CZ_ZoneServerUpdateFamily::Execute` zone @ 0x0829cf20 l.544160 |
| +0x20 | emblème (ushort) | `UpdateFamily::Execute` idem |
| +0x2c | trésor (int, gold) | `FamilyDonateMoney::Execute` zone @ 0x082568d0 l.450398 |
| +0x32 | flag bâtiment business (short ≠0 requis pour taxe business) | `FamilyLevyBusinessTaxes` zone @ 0x0840c960 l.812484 |
| +0x34 | taxes accumulées en attente de flush | `CEventFamilyTaxationTimer::Execute` zone @ 0x086676d0 l.1207347 |
| +0x3c | nodeTID de l'instance VDG (short) | `InitFamilyNpc` zone @ 0x0816f5d0 l.236471 ; `Mission_TC_VisitFamily` zone @ 0x083acda0 l.751891 |
| +0x50 | netID du NPC "house keeper" du bâtiment central | `InitFamilyNpc` ; `CenterBuildingUpgreade` zone @ 0x084689d0 l.866089 |
| +0x5c | CNodeGroup | `CenterBuildingUpgreade` |
| +0x64/0x68 | map<short tab, map<short loc, CCloneItem*>> coffre guilde | `FamilyStoragePutItem::Execute` zone @ 0x08258a00 l.452829 |
| +0x7c/0x80 | map<short contType, conteneur> items de famille | `FamilyContainerPutItem::Execute` zone @ 0x08255bf0 |
| +0xc8/0xd8 | log du coffre (liste) | `FamilyStorageQueryLog::Execute` zone @ 0x08259340 l.453476 |
| +0xec/0xf4 | set<int> charIDs des membres | `AddEnchantToMembers` zone @ 0x08464dc0 l.863866 |

Constantes (`.rodata`, lues via `objdump`, elf32) :
- `SFamily::DefaultCharFamilyID` @ 0x0888fb30 = **0** [VERIFIED]
- `SFamily::MaxTreasuryValue` @ 0x0888fb34 = **999 999 999** (0x3B9AC9FF) [VERIFIED]
- `CFamilyBattle::DF_LevelLimit` @ 0x088901b8 (2 o) = **30** ; `DF_EnergyCalPerTime` @ 0x088901b2 = **5** ; `DF_EnergyCalReduceValue` @ 0x088901b4 = **1** [VERIFIED]

[VERIFIED] Types d'opération (`IOperation`, retour de `IsA()`) :
9=`CFamilyCreateOp` (0x083eb880), 0x3b=`CFamilyBuildingOp` (0x086771d0),
0x43=`CFamilyTownCreateOp` (0x083eb940), 0x44=`CFamilyStorageOp` (0x08677c30),
0x45=`CFamilyTownListOp` (0x085798f0). Les ops portent le netID du NPC
(`TargetInTalkingRange(op[1])`).

## 2. Flux création de guilde

[VERIFIED] Étape 1 — dialogue NPC : `TC_Shop` zone @ 0x083d9450 (l.7644xx)
instancie `CFamilyCreateOp` + émet `CNC_CZ_ClientEnterFamilyCreation`
(NCID 347). `TC_CreateFamily` zone @ 0x083bf800 fait pareil, refuse si déjà
en guilde (err 0x355).

[VERIFIED] Étape 2 — `CNE_CZ_ZoneServerFamilyCreate::Execute`
zone @ 0x082547d0 l.448121 : nom vide → err 0x64f ; sinon
`CPlayerFactory::FamilyCreate` zone @ 0x080f3260 l.138549 :

- nom ≤ **16** chars (err 0x368), pas d'espace, `IllegalEncodingName`, nom
  disponible via GameData (vtable+0x1e4) ;
- op courante == 9 (`CFamilyCreateOp`), sinon DebugLog "not a family creation type" ;
- pas déjà en guilde (`char+0x5c==0`) ;
- **niveau ≥ 20** (`char+0x470 < 0x14` → err 0x369) ;
- **gold ≥ 50 000** (`char+0x4b0 ≤ 49999` → err 0x367) ;
- puis `CNC_ZW_WorldServerFamilyCreation` (NCID 634) → world.

[VERIFIED] Étape 3 — retour world : `CNE_ZW_ZoneServerNewFamily::Execute`
zone @ 0x082ad060 appelle `CPlayerFactory::NewFamily` zone @ 0x08101950 :
familyID −1 → err 0x913 (nom pris) ; −2 → échec silencieux ; ≥0 → `SFamily`
créée (niveau 1), insérée dans `CZoneServer+0x460` (map familyID→SFamily),
`FinishOperation`, **débit `GainGold(−50000)`** + `ItemLog` ')' + log
"Create Family Successfully". Membre inséré dans le set +0xec.

[GAP] Validation nom déjà pris / création DB / liste des membres = world+DBA
(`family`/`family_log` tables côté mission, strings mission 0x084eeb79).

## 3. VDG (ville de guilde / FamilyInstance)

[VERIFIED] Achat terrain — `CNE_CZ_ZoneServerFamilyInstanceCreate::Execute`
zone @ 0x082563f0 l.449900 (rate-limit 0,5 s, op 0x43
`CFamilyTownCreateOp` posée par `TC_Shop` + `CNC_CZ_ClientEnterFamilyInstanceCreation`,
op épuisée par `FinishOperation`) → `CPlayerFactory::FamilyInstanceCreate`
zone @ 0x080f0790 l.136958 :

- `type < 2` (param_2 short) ; **niveau perso ≥ 30** (err 0x599) ;
- **réservé au chef** (`SFamily+0x10 == char+0x34`, err 0x598) ;
- pas d'instance existante (`SFamily+0x3c != 0` → err 0x91a) ;
- **niveau guilde ≥ 3** (`SFamily+0x18 < 3` → err 0x59a) ;
- coût prélevé sur le **trésor** : vip==0 → **500 000** ; vip≠0 → **10 000 000**
  (`SFamily+0x2c`, err 0x6ae) ;
- émet DBA `FamilyInstanceCreate fm_id=%d type=%hd vip=%hhd` via
  `CDBAgent::Command` (mission applique en DB). Type de node template requis :
  +0xc==4 (ville), vérifié dans `TC_CreateTown` zone @ 0x083cd9…
  (commande GM `GMFamilyInstanceCreate`).

[VERIFIED] Entrée — `CNE_CZ_ZoneServerFamilyTownEnter::Execute`
zone @ 0x08259d30 l.454507 : `IsInNewerTown` → err 0x207f ; `IsEncaged` →
abort ; rate-limit 0,5 s (`char+0x650/0x654`). Si en instance et
`familyID arg == −1` → `TransportCharacterToNode(char+0xa98)` (retour au
node sauvegardé). Sinon : team `char+0x284` avec `team+0x54 ≥ 11` membres →
err 0x1430 ; `char+0xbb4 != 0` (état trade/stall [HYPOTHESIS]) → err 0xd1f ;
puis `CZoneServer::VisitFamilyInstance` zone @ 0x08171440 l.237783 :
`(char+0x3c)&4` → err 0xb80 ; node type ∈ {1,3,4} sinon err 0xb89 ;
SFamily trouvée avec node (`+0x14!=0`, `+0x3c!=0`) → DBA
`VisitFamilyInstance FamilyID=%d` ; absente → `CNC_CZ_ClientTextOutput`
"Can't find family" (0x9b). Téléport effective : `Mission_TC_VisitFamily`
zone @ 0x083acda0 → `TransportCharacterToNode(SFamily+0x3c)`.

[VERIFIED] Données instance — `CNE_CZ_ZoneServerFamilyInstanceGetData::Execute`
zone @ 0x08256ee0 l.450912 : sérialise `CNC_CZ_ClientFamilyInstanceData`
(NCID 487) = nom, niveau, nodeTID instance, puis pour chaque `CFamilyItem`
du conteneur 0 : loc(+0x10), item netID(+0x80), type(+0x86),
durabilité(+0xc), niveau(+0x94), exp(+0x98)…

[VERIFIED] Liste des villes — `CNE_CZ_ZoneServerFamilyTownQuery::Execute`
zone @ 0x0825a890 l.455532 (rate-limit 3 s) → `CPlayerFactory::FamilyTownQuery`
zone @ 0x08110830 l.156119 : filtre `param_2 ≤ SFamily+0x18 ≤ param_3`
(niveau guilde min/max), `param_4` = masque de facilities
(`CFamilyItem::GetBuildingValue` : catégories template 4..9 → bits
1,2,4,8,0x10,0x20), `param_5` = sous-chaîne de nom de NPC ville (map
`CZoneServer+0x48c` nom→set familyIDs, construite par `InsertTownNpcMap`
zone @ 0x08178260 l.241989). Réponse = `CNC_CZ_ClientFamilyTownQuery`
(NCID 502), paquets de **10 entrées max**. `FamilyTownMemberQuery` → DBA
`FamilyTownMemberQuery fm_id=%d` → retour `CNC_CZ_ClientFamilyTownMemberQuery`
(NCID 503).

## 4. Bâtiments

[VERIFIED] Items famille (`CFamilyItem`, PTR__CFamilyItem_0885b5b8) :
template type +0x86 : **0x29(41)=bâtiment**, **0x2a(42)** et **0x52(82)** =
NPC de ville. Bâtiment : itemID +0x80, template via GameData vtable+0x170 ;
`template+0xc` = catégorie : **2=maison (1 max)**, **10=business (1 max)**,
**4..9 = production (3 max)** ; durabilité +0xc (max template+0x20, coût
unitaire +0x1c), niveau +0x94, exp +0x98, revenu +0xa0.
Preuves : `IsHouseFull` zone @ 0x08465300 l.864106 ; `IsBusinessFull`
0x084653c0 ; `IsProductionBuildingFull` 0x08465480 (count>2) ;
`GetBuildingValue` 0x08464870 ; `GetAllBuildingRepaircosts` 0x08465570.

[VERIFIED] Niveaux bâtiment : objet spawné type `obj+0x122 ∈ 0x18..0x1d`
(6 types = les 6 facilities craft [HYPOTHESIS : Mining/Planting/Summon/
Alchemy/Casting/Enchanting]) ; `SFamily::GetBuildingNextExp` zone @
0x08465130 l.864027 lit LevelData (GameData vtable+0xe8) champs +0x34..+0x48
par type. `GainBuildingExp`/`GainBuildingExpByItemID` zone @ 0x08465cd0/
0x08465b80 : n'accordent d'exp que si `nextExp != 0` et `exp < nextExp`
→ DBA `UpdateBuildingExp fm_id loc exp` (`CFamilyDBO::UpdateBuildingExp`
zone @ 0x08352940). Level-up réel = mission (`CFamily::CheckBuildingLevelUp`,
`TC_UpdateBuildingExp` mission @ 0x081b1f40/0x081ef760) [GAP côté mission].

[VERIFIED] Usure & réparation : `CNE_CZ_ZoneServerFamilyBuildingRepair::Execute`
zone @ 0x08253b80 l.447044 — `repairAll==0` : coût =
`(maxDur−curDur) * template+0x1c` sur le trésor (err 0x8f0 insuffisant,
0x8f2 déjà plein) ; `!=0` : somme `+0x1c` par bâtiment cat.4-9 endommagé
(err 0x8f1 rien à réparer) → DBA `FamilyBuildingRepair Loc=%hd RepairAll=%hd`.
Usure : `SFamily::UpdateBuildingDurability` zone @ 0x08465a60 → DBA
`UpdateBuildingDurability fm_id loc durability` ; déclenchée aléatoirement
à l'usage (`EndCollect` : `workdata[9]==1` && rand<workdata[0xb] → −workdata[0xc]).

[VERIFIED] Placement : `CheckNpcLocInvalid` zone @ 0x08464a20 — locs
multiples de 10 et 0x33(51) réservées ; `CheckNpcSizeLimit` zone @
0x08464ea0 — grille de 5 zones de 10 (locs 0-50). `CenterBuildingUpgreade`
zone @ 0x084689d0 l.866089 : respawn du house-keeper NPC (`GetCenterBuildingHouseKeeperPos`)
+ re-broadcast `ClientFamilyInstanceData` à tous les persos du node.

[VERIFIED] Achat items : `Mission_TC_BuyFamilyItem` zone @ 0x083af510
relais DBA (prix/candidat = mission) [GAP prix]. Suppression :
`Mission_TC_FamilyRemoveBuilding` zone @ 0x083acad0 ; conteneur maj via
`CNE_ZMis_ZoneServerFamilyContainerUpdate::Execute` zone @ 0x08315c…
(l.6565xx) → `BornNPC` pour items 0x2a/0x52, `UpdateTownNpcMap`, packets
`CNC_CZ_ClientFamilyContainerPutItem/DeleteItem` + broadcast texte
0xb6f/0xb70 aux membres.

## 5. Statues

[VERIFIED] `CNE_CZ_ZoneServerFamilyStatue::Execute` zone @ 0x08257f10
l.452147 : `type (this+0x18) ≤ 4` ; **chef seul** (err 0x382) ; doit être
dans son instance (err 0x38e). type 0 = **renommer** une statue (compare
`this+0x1c` au nom, `BornStatue` + `CStatueDBO::SaveStatueToDB`, err 0x38f,
log `FamilyTextIndex 0x38b`). types 1-4 = **poser** statue modèle
**0xd675/6/7/8** via `CPlayerFactory::SetStatue` zone @ 0x08100ff0 l.146740
(`SetStatue` insère dans la map `CZoneServer+0x2bc` clé nodeTID<<16|pos,
nom=template NPC, sauvegarde DB) : **niveau guilde ≥4**, types 3-4 exigent
**≥6** ; coût **500 000** prélevé du trésor (DBA `FamilyDonateMoney money=-500000`,
err 0x38c) ; broadcast 0xb4e + log `FamilyTextIndex 0x38a`. Spawn au boot du
node : `CZoneServer::InitStatue` zone @ 0x0816f8f0 l.236574 (map statues,
clé = nodeTID<<16|position, +0x298b). Dissolution : `CNE_ZW_ZoneServerRemoveFamily::Execute`
zone @ 0x082ad580 l.564312 efface les noms des statues 0xd675..0xd679
(5 positions [VERIFIED]).

## 6. Dons & trésor & taxes

[VERIFIED] `CNE_CZ_ZoneServerFamilyDonateMoney::Execute` zone @ 0x082568d0
l.450398 : amount>0, guilde, gold joueur ≥ amount (err 0x388),
trésor+amount ≤ MaxTreasuryValue (err 0x370) → `GainGold(-amount)` +
ItemLog ')' "FS:%d" + DBA `FamilyDonateMoney money=%d` (crédit en DB =
mission). Cap trésor = **999 999 999** (constante).

[VERIFIED] Taxes : `CCharacter::FamilyLevyTaxes` zone @ 0x0840cad0
l.812532 — sur gain d'or : taxe ≈ `gold*10*coef+const` (coeff float
DAT_087cc690/88 [HYPOTHESIS ~taux table]) plafonnée par trésor max ;
accumulée dans `SFamily+0x34`, msg 0x36c. `FamilyLevyBusinessTaxes` zone @
0x0840c960 l.812484 : seulement si le perso travaille dans un VDG **tiers**
(`char+0xaac` = familyID visité, node type==4, `SFamily+0x32!=0`) →
pending += amount, msg 0xd15 au profit de la ville hôte. Flush :
`CEventFamilyTaxationTimer::Execute` zone @ 0x086676d0 l.1207347 — réarmement +3 s,
si `+0x34 != 0` → DBA `FamilyLevyTaxes Taxation=%d FamilyID=%d`, reset.

## 7. Coffre de guilde

[VERIFIED] Ouverture : `CFamilyStorageShop::OpenShop` zone @ 0x08579440
l.1063568 — guilde requise, pose `CFamilyStorageOp` (0x44), émet
`CNC_CZ_ClientEnterFamilyStorage` (NCID 507) + 1
`CNC_CZ_ClientFamilyStorageData` (508) **par onglet** (map +0x64).
- `PutItem` zone @ 0x08258a00 l.452829 : rate-limit 0,5 s ; op 0x44 +
  `TargetInTalkingRange` ; item interdit si template+0xf2&1 (err 0x20a3) ou
  flags clone+0x24&0x10 / template+0xf0&(8|0x10) (err 0x7eb) ;
  **onglet 1 → guilde ≥5, onglet 2 → ≥6** ; **40 slots/onglet** (`<0x28`,
  err 0x7ef) ; loc=−1 → premier libre ; DBA `FamilyStorageInsertItemRequest`.
- `GetItem` zone @ 0x082577d0 l.451549 : op 0x44 + range ; ≥1 slot
  inventaire (err 0x4f9) ; DBA `FamilyStorageGetItemRequest index loc
  inv_index inv_loc item_id`.
- `QueryLog` zone @ 0x08259340 → `CNC_CZ_ClientFamilyStorageLog` (506)
  depuis le cache `SFamily+0xc8`.
- Maj : `CNE_ZMis_ZoneServerFamilyStorageUpdate::Execute` zone @ 0x083188…
  → `CNC_CZ_ClientFamilyStoragePutItem/DeleteItem` ; `Mission_TC_FamilyStorage*`
  zone @ 0x083b3540/0x083b32c0 relaient les requêtes DBA.
  [GAP] aucun check de rang zone pour get/put (juste op+range) → droits
  fins côté mission/DBA.

## 8. NPCs de ville

[VERIFIED] Spawn : `CZoneServer::InitFamilyNpc` zone @ 0x0816f5d0 l.236471 — node
type 4 nommé `Family_%d` → house-keeper (`+0x50`) + `BornNPC` par item
0x2a/0x52 à leur pos (+0x88/8c/90, netID→item+0x9c). Dialogue :
`CNE_CZ_ZoneServerFamilyTalkToNpc::Execute` zone @ 0x082598d0 l.454024 →
pose `CTalkOp` + `CNC_ZMis_MissionServerListDialogue` (NCID 782) → la
mission choisit le dialogue ; la réponse exécute `TC_Shop` qui ouvre :
`CFamilyStorageShop`, `CFamilyBuildingShop` (+`ClientEnterFamilyBuilding` 483
+ `ClientFamilyContainerData` 484), `CFamilyTownListShop` (+`ClientEnterFamilyTownList` 514),
`CFamilyTownCreateOp` (+`ClientEnterFamilyInstanceCreation`), les 6 shops
craft `CMiningShop/CPlantingShop/CSummonShop/CAlchemyShop/CCastingShop/CEnchantingShop`
(+`CNC_CZ_ClientEnterWorks`, ex. `CMiningShop::OpenShop` zone @ 0x0857a080
l.1064277 ; restreint si `char+0xaac != char+0x5c` — visiteur dans un VDG tiers
[HYPOTHESIS droits limités]), `CTalkOp`+`ClientOpenBattleCrusadeDialog`,
`CShopOp`/`CStrengthenOp`/`CCombineOp`/`CLotteryOp`/`CMagicBoxOp`/`CChurchpOp`.

## 9. FixEquipByFamily

[VERIFIED] `CNE_CZ_ZoneServerFixEquipByFamily::Execute` zone @ 0x0825ae00
l.456036 : rate-limit >1 s (err 0x621), `CItemFactory::GetItemRepairAllPrice`,
DBA `FamilyFixEquip Money=%d` → la mission applique `Mission_TC_FamilyFixEquip`
zone @ 0x083ad450 → `CPlayerFactory::RepairAllEquipments` zone @ 0x08115460
l.158781 (répare chaque équipement porté, `ModifyItemDurability`, packets update).
[GAP] débit trésor vs or perso tranché côté DBA/mission.

## 10. Guerre/battle

[VERIFIED] `CNE_CZ_ZoneServerFamilyBattleAskInfo::Execute` zone @
0x08253740 l.446566 : type1 → DBA `TerritoryDataQuery`+`FamilyBattleCheckJoin` ;
type2 → `Join` : rate-limit 0,5 s ; état file `char+0xc40/44/48/50` vide ;
si `IsInTerritory` → `CTerritoryWarFactory::CharacterLeave` (leave) ;
guilde requise ; **node type interdit** ∈ {4,6,8,9,0xa,0xc-0x13} ;
**niveau ≥ 30** (`DF_LevelLimit`) ; team → `LeaveFromTeam` ; DBA
`TerritoryJoin territory_id=0 type=2`. type0 → `FamilyBattleJudgeQuery`.
`CTerritoryWarFactory::CreateFamilyBattle` zone @ 0x081590… crée
`CFamilyBattle` (0x20c) ; `CEventFamilyBattleTimer::Execute` zone @
0x08666b80 calcule l'énergie `(Δscore)*DF_EnergyCalReduceValue` (période
`DF_EnergyCalPerTime`=5). `CaculateFinishReward` zone @ 0x0846b3e0 =
**stub vide** [VERIFIED — récompense = mission]. Packets :
`CNC_CZ_ClientTerritoryFamilyScore`, `CNC_CZ_ClientFamilyBattleKilledRank`,
`CNC_CZ_ClientFamilyBattleInfo`, `CNE_ZMis_ZoneServerFamilyBattleInfo`
(NCID 833). Enchants vainqueur : `Mission_TC_(All)FamilyWinnerEnchant` →
`SFamily::AddEnchantToMembers` zone @ 0x08464dc0 (effet aux membres online).
[GAP] matchmaking/juge/terrain côté mission (`FAMILYBATTLE::AllFamilyCamp`).

## 11. Niveau guilde / XP

[VERIFIED] `SFamily::GainExp` zone @ 0x08465230 → `CFamilyDBO::GainFamilyExpByID`
zone @ 0x083528b0 → DBA `GainFamilyExpByID fm_id exp` — **la montée de
niveau est côté mission/DBA** (`CFamily::GainExp`, `TC_GainFamilyExp`,
`TC_FamilySetLevel` mission @ 0x081beec0/0x081ef170…) [GAP seuils].
Sources d'XP : `CWorkManager::EndCollect` zone @ 0x08167110 —
`SFamily::GainExp(family, char, workdata[6])` + `GainBuildingExpByItemID`
si `work+0x2c` (bâtiment lié) + usure aléatoire ; revenu bâtiment via
`UpdateBuildingRevenue` depuis `EndSummon`/`ToCombine`/`ToCollect`.
[HYPOTHESIS] même schéma dans `EndCombine`/`WorkResultOK`/`EndSummon`
(listés callers `SFamily::GainExp`). GM : `TC_GainFamilyExp`/`TC_FamilyLevelUp`
/`TC_SetFamilyLevel` envoient `gain_family_exp`/`set_family_level` au world.
Niveau guilde pilote : VDG (≥3), statues (≥4, ≥6), onglets coffre (≥5, ≥6).

## 12. Paquets émis (NCID, table Eden.Core = mapping C#)

C→Z : FamilyCreate 49, UpdateFamily 81, SetFamilyEmblem 82, ContainerPut/GetItem 130/131,
InstanceGetData 132, DonateMoney 159, Statue 161, InstanceCreate 173,
TownQuery 174, TownMemberQuery 175, TownEnter 176, StoragePut/GetItem 177/178,
StorageQueryLog 179, BuildingRepair 180, BattleAskInfo 225, TalkToNpc 239,
FixEquipByFamily 240.
Z→C : TextOutput(Index) 316, ClientChangeFamily 346, EnterFamilyCreation 347,
SelectFamilyEmblem 384, EnterFamilyBuilding 483, ContainerData 484,
ContainerPut/DeleteItem (zone ~485/486), InstanceData 487, TownQuery 502,
TownMemberQuery 503, StorageLog 506, EnterFamilyStorage 507, StorageData 508,
StoragePut/DeleteItem, EnterFamilyTownList 514, R25UpdateFamily,
ClientTerritoryFamilyScore, ClientFamilyBattleKilledRank/Info, EnterWorks,
ReplyDialogue/BattleCrusadeDialog.
Z→World : FamilyCreation 634, WorldServerTextCommand (`set_family_*`,
`disband_family`, `gain_family_exp`…) ; ZW→Z : NewFamily 646, RemoveFamily 647,
ChangeFamily 645. Z↔Mission/DBA : AgentDBACommand 676 (toutes les commandes
texte `Family*`), ListDialogue 782, ZMis updates 755-758/833.

## 13. GAPs restants

- Tables DB `.ini` lues par `LoadDB` pour `family_town`/`family_building`
  (strings zone 0x08880a2b/0x088809cb) — schéma non lu.
- Droits membres/rangs (recrutement, storage get/put, dialogue droits) —
  pas de check de rang vu en zone → mission/world (`CNE_DBA_ClientAddFamilyMember`,
  `CheckFamilyInvite`, `MC_IfFamilyCadre`, `MC_IfFamilyLevel`).
- Seuils XP niveau guilde, prix bâtiments/NPC/statues détaillés, débit
  FamilyFixEquip, recrutement/quit, matching battle — mission/DBA/world.
- NCID : valeurs ci-dessus = table Eden.Core ; mapping natif non re-vérifié
  (Init*CommandTable zone = FAILED, non objdumpé).
- `char+0x284`=team confirmé par usage (`LeaveFromTeam`) ; `char+0xbb4`
  (err 0xd1f) non identifié [HYPOTHESIS trade/stall] ; `SFamily+0x1c`
  (champ "leader" du packet ChangeFamily) à confirmer.
- `CFamilyItem` pos/statue (0x24 float direction) et conteneurs type≥3 :
  partiellement cartographiés.
