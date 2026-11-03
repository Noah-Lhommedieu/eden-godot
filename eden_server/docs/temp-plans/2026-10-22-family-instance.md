# Lot — FamilyInstance (173/132) + données bâtiments famille

## Preuves natives (zone @ decompiled.c, mission @ idem)

### Entrant 173 `CNE_CZ_ZoneServerFamilyInstanceCreate` — `[s16 type][u8 vip]`
- `Execute` zone @0x08256600 : anti-spam 0.5 s (char+0x650), puis
  `CPlayerFactory::FamilyInstanceCreate` @0x080f0790 :
  - `type < 2` sinon retour silencieux ;
  - niveau < 30 → texte **0x599** ;
  - famille absente (map Ins+0x460) → silencieux ;
  - `SFamily+0x10 != char+0x34` → **0x598** (+0x10 = charId créateur =
    leader_id, +0x34 = charId → chef uniquement) ;
  - `SFamily+0x3c != 0` (house_open) → **0x91a** ;
  - `lv < 3` → **0x59a** ;
  - trésorerie < 500 000 (vip 0) / 10 000 000 (vip 1) → **0x6ae** ;
  - opération char+0x670 type 0x43 requise (UI hall) — non modélisée,
    laissée tolérante ;
  - relais `CDBAgent::Command(charId, "FamilyInstanceCreate
    fm_id=%d type=%hd vip=%hhd")` → mission.
- Mission `TC_FamilyInstanceCreate` @0x81ef6d0 → `CFamily::InstanceCreate`
  @0x81bf660 :
  - house_open ≠ 0 → **0x91a** ; niveau ≤ 29 → **0x599** ;
  - leader ≠ demandeur → **0x598** ; lv < 3 → **0x59a** ;
  - vip 0 && nombre de villes (familles house_open≠0) > 49 → **0x59c** ;
  - trésorerie → **0x6ae** ; membres < 30 → **0x59b** ;
  - famille dans AllBeRecycledFamilyIDs → **0xa4b** ;
  - **type 1 → node 0x1f5 (501)** ; **type 0 → node 0x1f6 (502)** ;
  - `CFamilyDBO::UpdateInstanceNodeID` →
    `UPDATE family SET house_open = <node>` (@0x8179430) ;
  - débit trésorerie 500k/10M (`DonateMoney(-cost)`) ;
  - `UpdateLog` type 2 ; event `CEventFamilyInstanceRecycleCheck` ;
  - broadcast `CNC_DBA_ClientChangeFamily(fam, 0x1a, node)` à toutes
    les zones.
- Textes renvoyés par `CGlobalInfo::TextOutputIndex(charId, node, id,
  0x97)` → chez nous `CNC_CW_ClientTextOutputIndex` (43) direct client.

### Sortant 487 `CNC_CZ_ClientFamilyInstanceData` — bâtiments du hall
- `Serialize` zone @0x081e82e0 : `[i32 family_id][str nom
  (SFamily+0xc — assign natif, PAS vide)][s16 lv (SFamily+0x18)]
  [s16 node (house_open, SFamily+0x3c)][s16 container_key = 0]` puis
  `[u16 n]` et `n × {s16 loc, i32 item_tid, i32 tid, s16 item_type,
  s16 durability, s16 level, u32 exp}` (vecteurs parallèles natifs —
  `item_tid` lu u16 non signé → i32).
- Gate `CNE_CZ_ZoneServerFamilyInstanceGetData::Execute` @0x08256ee0 :
  `char+0xaac` (famille visitée) ≠ 0 requis (sinon silencieux) ;
  `IsInFamilyInstance` faux → `ClientTextOutput` type **0x9b**
  "GetFamilyInstanceData - Not In FamilyInstance" ; ensuite bâtiments
  du conteneur 0 (SFamily+0x7c map<container, map<loc, item>>).
- Mapping mission↔zone item (Serialize mission @0x8113a00 ↔
  Deserialize zone @0x82eda40 + clone `LoadBuildingContainer`
  @0x82ec5f0) : mission +8=id→zone +4, +0xc=item_tid→zone +0x84(u16),
  +0xe=durability→+0xc, +0x30=due_date→+0x18, +0x84=tid→+0x80,
  +0x88=item_type→+0x86, +0x8c/90/94=x/y/face→+0x88/8c/90,
  +0x98=level→+0x94, +0x9c=exp→+0x98.
- Colonnes `family_building_container` ↔ champs (INSERT natif
  `InsertItemIntoBuilding` @0x8178ec0 — autoritatif) :
  id, item_tid, tid, item_type, durability, container_index, loc,
  x, y, face_dir, level, exp, due_date.

## Implémentation

- [x] `FamilyRepository` : `SetHouseOpenAsync`, `CountOpenTownsAsync`
      ; lecture bâtiments déplacée dans `SocialRepository.
      GetFamilyBuildingsAsync` (la zone y a accès, pas le repo monde).
- [x] `FamilyService` : `InstanceCreateGate` (pur, testable) +
      `InstanceCreateAsync(charId, type, vip, charLevel)` — mutation
      house_open/trésorerie/log sous verrou ; `InstanceNode`/
      `InstanceCost` exposés.
- [x] Monde `ZoneLinkServer.HandleZwTextCommandAsync` :
      `family_instance_create <charId> <type> <vip>` → service →
      mutation + push ZW 35 + refresh membres (type 0) ; erreurs →
      `ClientTextOutputIndex` 43 ; type hors {0,1} → silence.
- [x] ZW NCID 35 `FamilyDataUpdate` `[i32 familyId][s16 house_open]`
      → zone `Guild.HouseOpen` (`FamilyPushKind.DataUpdate`).
- [x] Zone `ZoneEntryFlow.FamilyInstance.cs` 173 : gates natifs zone
      + relais texte ZW ; remplace le stub Dungeons erroné
      (intercepté dans `TryHandleOnLoopAsync` pour `st.Level` live).
- [x] Zone 132 : gate `VisitedFamilyInstanceId` ≠ 0 (mort tant que le
      flux visite n'existe pas — silence natif correct) + codec 487
      dédié `FamilyPackets.FamilyInstanceData` (classe générée aplatie
      → `known_incomplete.json`).
- [x] `EntryState` : `VisitedFamilyInstanceId` (+0xaac natif).
- [x] `GuildRow`/`Guild` portent `house_open` (SELECT family + cache
      zone).
- [x] Migration `Database/009-family-building-container.sql` (schéma
      natif pour bases sans dump).
- [x] Tests codec 487 + gates service (25/25 famille) ; build 0/0 ;
      suite 847/847.

## Hors scope (lot ville/hall suivant)

- 174/175/176 TownQuery/Member/Enter (502 multi-vecteurs ~15 col,
  spawn d'instance hall), 161 statue, 180 repair, 225 battle,
  130/131 conteneur bâtiment (op NPC 0x3b dans le hall).
