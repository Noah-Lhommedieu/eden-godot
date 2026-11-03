# Lot Territoire + Family Battle — NCIDs 189/190/225 → 515/516/561

## Décodage natif (preuves)

### 189 `CNE_CZ_ZoneServerTerritoryQuery` — wire VIDE (aucun champ)
- Execute zone @0x82944a0 :
  - char présent, type objet 7, flag +0x10==0.
  - `CCharacter::IsInNewerTown` @0x8409xx : `nodeData+4 ∈ {94(0x5e), 768(0x300)}`
    = **node_id** (夢之聖徽城 / 聖城水脈) → texte **0x2080**, retour.
  - rate-limit **500 ms** sur le champ partagé char+0x650/0x654.
  - → AgentDBA `TerritoryDataQuery` → mission `TC_TerritoryDataQuery`
    @0x8211860 construit `CNC_ZMis_ZoneServerTerritoryQuery` → zone
    `CNE_ZMis_ZoneServerTerritoryQuery::Execute` @0x83388a0 recopie en
    `CNC_CZ_ClientTerritoryQuery` (**515**) et NotifyClient.

### 190 `CNE_CZ_ZoneServerTerritoryJoin` — wire `[i32 territory_id][s16 type]`
- Execute zone @0x8249970 :
  - `IsEncaged` (node_id==996 監牢) → log + silence.
  - rate-limit 500 ms (char+0x650).
  - `char+0x3b & 0x40` (versus/instance lock) → silence.
  - pending RecruitInstance (char+0xc40==-1 && +0xc48==+0xc44 && +0xc50==0)
    → sinon texte **0x1160**.
  - équipe > 10 membres (team+0x54) → texte **0x1368**.
  - `IsInTerritory` (node type 8) && territory_id == -1 →
    `CTerritoryWarFactory::CharacterLeave` (quitte la guerre).
  - level < 30 OU family_id == 0 → texte **0xd16**.
  - `IsInBattleRoyale` (type 0xf) → 0x18cd ; `IsInVersusInstance` (0x11)
    → 0x1d98 ; node type ∈ {4-9,0xa,0xc-0x13} OU IsInGreedyIsland(0xd)
    OU IsInBattleCrusade(0x13) → texte **0x678**.
  - type@+0x18 : 0 → `TerritoryJoin territory_id=%d type=1` ;
    1 → `type=2` ; puis `LeaveFromTeam`.
- Mission `TC_TerritoryJoin` @0x8236860 : type 1 → `GetTerritory(id)` ;
  type 2 → `GetFamilyBattle(fam_id)` (map globale, vfn==2, phase 1/2) ;
  battle+8 (phase) ∉ {1,2} → texte **0x67a** ; type 2 : famille doit être
  dans un des 2 camps (maps +0x30/+0x48) sinon silence ;
  `CTerritory::Join` == 3 → `CNC_ZMis_ZoneServerTerritoryJoin`
  (s16 node = terr+0xc) → zone `TransportCharacterToNode(node)` ;
  résultat 1 → texte **0x1177** (nom territoire).

### 225 `CNE_CZ_ZoneServerFamilyBattleAskInfo` — wire `[s16 type]`
- Execute zone @0x8253740 :
  - type 0 → AgentDBA `FamilyBattleJudgeQuery` → mission
    `TC_FamilyBattleJudgeQuery` @0x820a1b0 : itère `FamilyBattleRanks`
    (map<phase 0-4, vector>) → `CNC_ZMis_ZoneServerFamilyBattleInfo`
    type 0 → 561 `[s16 0][u16 n](str name, i32 energy)×n`
    (name = family_name, i32 = rank rec +0x10 = `energy` de
    `familybattle_ranks`).
  - type 1 → `TerritoryDataQuery` (=515) + `FamilyBattleCheckJoin` →
    mission `TC_FamilyBattleCheckJoin` @0x81f51e0 :
    `GetFamilyBattle(exec+0xb0=fam_id)` → battle+4 (phase) ou 0 →
    561 `[s16 1][i32 phase]`.
  - type 2 → `Join` @0x8253430 : rate-limit 500 ms (char+0x650) ;
    char+0x3b&0x40 → silence ; pending RecruitInstance → texte **0x678** ;
    IsInTerritory → CharacterLeave ; famille != 0 ; node type ∉
    {4-9,0xa,0xc-0x13} (autorisé : <4, 0xb, ≥0x14) ; level (char+0x470)
    ≥ `CFamilyBattle::DF_LevelLimit` = **30** (rodata 0x88901b8=0x1e) ;
    famille dans Ins+0x460 → `TerritoryJoin territory_id=0 type=2` +
    `LeaveFromTeam`.

### 515 `CNC_CZ_ClientTerritoryQuery` — Serialize zone @0x82043d0
```
[s16 garbage]                          // ebp-0x144 lu jamais écrit
                                       // (asm @0x82119c9) → on émet 0
[u16 n]  [i32 territory_id]×n          // terr+4 (map croissante)
[u16 m]  [i32 sched_id][i32 begin][i32 end]×m   // map schedules epoch
[u16 n]  [i32 own_family_id]×n         // terr+0x14
[u16 n]  [str name]×n                  // terr+0x18 (Big5)
[u16 n]  [s16 fam_level]×n             // (char)fam+0x18 ou 0
[u16 n]  [u32 time]×n                  // flag? terr+0x1c+0x20+Prepare
[u16 n]  [s16 state]×n                 // terr+8
[u16 n]  [u8 flag]×n                   // terr+0x24
```
Contenu (TC_TerritoryDataQuery @0x8211860) : entrées = map globale
territoires (ordre id croissant, inclut les CFamilyBattle type 301-315),
puis **id 101** = Greedy Island (time = GreedyBegin+Duration, sched 501),
**id 102** = Battle Royale (time = BR+0x14+0x10 si ouvert, flag = BR+0xc,
sched 502), puis **ids 27,28,29** = versus instances (time =
Versus+0x18+0x14 si actif, flag = Versus+0xc==id, sched 601/602/603).
Le vec7 i32 (terr+0x28 own_times) est construit côté mission mais NON
copié vers le client (zone Execute ne le recopie pas — prouvé).

`GetScheduleTypeByTerritoryType` @0x8071ec0 : types 1-15 → type-1 ;
types 0x12d-0x13b → identique ; sinon -1.

### 561 `CNC_CZ_ClientFamilyBattleInfo` — Serialize zone @0x81e8580
```
[s16 type]
type 0 : [u16 n] [str][i32]×n    // noms + énergie
type 1 : +[i32 phase]
type 3 : [i32 3] + 3×[i32][s16][i32]   // 3 camps
```

## Données
- `S_Territory.ini` : `id|nom|group|col4|type|desc|rewards` —
  type = col5 (index CTerritoryData+0x10) : 2-11 = guerres,
  301-316 = CFamilyBattle (公會淘汰 = élimination de guildes).
- `S_Schedule.ini` : `row|schedType|entryId|beginHHMM|endHHMM|`.
- DB `territory_war` (territory_id, own_family_id, own_times),
  `familybattle_ranks` (family_id, family_name, family_honor,
  phase_type, phase_index, energy).
- CTerritory ctor @0x81db380 : +4=id +8=phase(0) +0xc=node(0)
  +0x14=own_fam +0x18=nom +0x1c/+0x20=times +0x24=flag
  +0x28=own_times.

## Implémentation
- [x] GameDataService : `S_Territory` (id→{name,type}) + `S_Schedule`
      (type→[(dayId,begin,end)]) — Big5.
- [x] SocialRepository : `GetTerritoryWarsAsync`, `GetFamilyBattleRanksAsync`
      (ORDER BY phase_type, phase_index).
- [x] `FamilyBattlePackets.cs` : codec 561 (3 types) + 515.
- [x] `TerritoryWarService` : territoires chargés (id, own_fam, name,
      type, sched times epoch) + phases runtime ; `GetBattle`,
      `TryJoin(terrId|familyBattle)`, leave.
- [x] ZoneEntryFlow.Territory.cs : 189 (gates → 515), 190 (gates →
      join/leave/teleport), 225 (types 0/1/2, gates Join fidèles).
- [x] `IsInNewerTown` = node∈{94,768} ; `IsEncaged` = node 996.
- [ ] Tests : codec 561/515 wire, gates statiques.

## Cycle guerre natif (désasm zone — sortants)

État `CTerritory+8` : 0=prépare, 1=guerre, 2=closing (poussé par
`TerritoryWarFinishByNewEnding` @0x081597e0 → `CEventTerritoryClose`).
Perso : `char+0xb70`=territory_id (i32), `char+0xb74`=camp (u16,
0xffff=aucun), `char+0xb9c`=kills perso, posés depuis l'exec à la
migration (`@0x08433564` : exec+0x118/+0x11c) ; remis à 0/0xffff au
leave (`@0x0815af00` → `TransportCharacterToNode(char+0xa98)` =
point de retour sauvegardé ≡ CrossZoneNode).

- **516 `ClientTerritoryInfo`** `[s16 type][i32 remaining][i32 own_fam]
  [u16 side]` — `CCharacter::SendTerritoryData` @0x08424ab0
  (appelé depuis `OnMigrationDone` @0x0843fba0) :
  - terr+8==0 → type **0**, remaining = `PrepareDuration - (now - terr+0x24)`
  - terr+8==1 → type **1**, remaining = `terr+0x1c - (now - terr+0x20)`
    (durée restante, +0x1c=duration +0x20=begin_epoch)
  - `TerritoryWarStart` @0x08158c90 : terr+8←1, broadcast 516 type 1
    à tous les persos du node.
  - `FinishByNewEnding` : 516 type **2** (remaining=0) à chaque perso.
- **532 `ClientTerritoryKilledInfo`** `[i32 killsA][i32 killsB]
  [s16 rec+4][s16 rec+2][s16 ownKills]` — `CEventTerritoryKilledInfo
  ::Execute` @0x0866e6e0 : terr+0x30/+0x34 = compteurs kills courants
  par camp, +0x38/+0x3c = snapshot émis (envoyé seulement si changé) ;
  les 2 s16 centraux viennent d'un record terr+0x16c (dernier kill) ;
  char+0xb9c = kills perso.
- **566 `ClientTerritoryFamilyScore`** `[s16 index][u16][str famName]
  [u16][i32 score]` — une émission par entrée du vecteur familles
  (terr+0xf4, stride 0xc), à la fin (`FinishByNewEnding`) et
  périodiquement pendant la guerre.
- **562 `ClientFamilyBattleKilledRank`** `[s16 n][per entry:
  str player, str family, s16 kills, s16 rank]` — leaderboard des
  kills, émis dans `KilledInfo` (vide si guerre territoire seule)
  et à la fin.
- **547 `ClientTerritoryEnd`** corps **vide** —
  `CEventTerritoryClose::Execute` @0x0819c240 : chaque perso du node
  reçoit 547, `char+0xb74 ← 0xffff`, puis transport retour.
- **565 `ClientR25UpdateFamily`** `[s16 slot][str name]` —
  mission `CFamily::{Disband,KickMember,QuitFamily,AddMember}` →
  `CTeamManager::R25UpdateFamily` @0x080c65f0 → zone
  `Mission_TEC_R25UpdateFamily` @0x0857ea30 → `CForceTeam::
  R25UpdateFamily` @0x0846d160 : broadcast aux membres de la team du
  perso dont la famille a changé (mise à jour du tag famille dans
  l'UI équipe). Relais mission↔zone interne, pas de gate zone.

## Cycle implémenté (P14.3)
- [x] EntryState : `TerritoryId`/+Camp/+Kills (char+0xb70/+0xb74/+0xb9c)
      + `TerritoryPhaseSeen` (dernière phase notifiée via 516).
- [x] Join → set + migrate ; leave/deco → clear + retour CrossZoneNode
      (`LeaveBattleNodeAsync` + cleanup déconnexion).
- [x] `SendTerritoryDataAsync` : 516 après MigrationDone si TerritoryId
      (Lifecycle) ; type 0/1/2 selon phase + remaining fidèle.
- [x] Tick (StatusTickStep, 1 s) : refresh phases → 516 à chaque
      transition de phase (PhaseSeen) ; `ClosePending` (active→fin)
      → 566 par famille + 516 type2 + 562 rank + 547 vide + persist
      `territory_war` (vainqueur = famille top-score du camp aux
      kills max — approximation documentée) + teleport retour pour
      tous les persos du territoire (`CloseTerritoryWarAsync`).
- [x] PvP létal sur node de bataille (TerritoryId identique, camps
      opposés) → `PlayerDeathAsync` + `RecordTerritoryKillAsync` →
      broadcast 532 `[killsA][killsB][0][0][ownKills]` à tout le node
      (les 2 s16 centraux = record de dernier kill natif non
      reproduit — approximation).
- [x] 565 : `EmitR25UpdateFamilyAsync` à la fin de
      `EmitChangeFamilyAsync` (push W→Z 31/33) — `[s16 slot][str
      nomFamille]` aux membres de la team du perso dont la famille
      change (join/quit/kick/disband).
- [ ] Restes : kills par cristaux/mobs (capture crystal 200..120 pts,
      `OnCrystalCapture` non câblé — les cristaux sont des mobs du
      node de bataille à scorer dans KillMob) ; `TerritoryWarCheckClose`
      (fin anticipée quand un camp se vide) ; récompenses
      `HandleRABattlefield`/`GainNp` ; répétition périodique du 532
      natif (KilledInfo event ~1 s) — chez nous émis à chaque kill.
