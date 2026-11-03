# Lot Colosseum — NCIDs 196-201/219 → 524-530/552/587

## Décodage natif (preuves)

### Architecture
- `CColosseumFactory` : zone (Ins local : mode +4, matchs par id via
  char+0xbb4) ET mission (Ins global : files par catégorie +0x30,
  schedId +100, flag open +8, durée +6).
- 4 events schedule mission `CEventCS{Normal,Challenge,Twenty,
  ClassPK}Schedule` liés aux types S_Schedule **200-203**. À
  l'ouverture/fermeture : `TuneMode` @0x8056c50 → schedId courant
  (200→0 Normal, 201→1 Challenge, 202→2 Twenty, 203→3 ClassPK) +
  broadcast `CNC_ZMis_ZoneServerColosseumModeUpdate` [s16 schedId]
  → `CNE_...::Execute` zone @0x833… : `factory+4 = schedId`.
- Modes : Normal (level≥30), Challenge (+?), **Twenty = schedId 2
  exige level > 59**, **ClassPK = schedId 3 exige SLV (char+0x47e,
  job/class level) > 49**. GetCategory mission @0x8056a80 :
  schedId 1→cat 1 ; 3→gamedata (class/race) ; sinon level/10 cap 8.
- Arène = node type **10** (`IsInColosseum` @0x8409a00), ex. 401
  炎鷹競技場.

### Entrants (zone)
- 196 `GladiatorTeamCreate` [str nom][u8 flag] — Execute @0x825cd90
  = tail-call `char->IsA()` : **no-op en V.9** (système gladiator
  legacy, le handler réel a été retiré).
- 197 `GladiatorTeamQuery` — idem no-op @0x825dd10.
- 198 `ColosseumJoinAnswer` [s16 answer] — @0x8244720 : relais
  mission `CNC_ZMis_MissionServerColosseumJoinAnswer` [i32 charId]
  [s16 answer].
- 199 `ColosseumJoinRequest` (vide) — @0x8244d30, gates dans
  l'ordre :
  - mode==2 : level ≤59 → texte 0x12a5 + JoinResult(1) ; level≥60 ok
  - mode!=2 : level <30 → texte 0xfc9 + JoinResult(1)
  - BattleRoyale → 0x18cd ; VersusInstance → 0x1d98 ;
    BattleCrusade → 0x225f
  - pending : char+0xbb0/+0xbb4 (file/match colosseum), +0xc40/-1,
    +0xc48==+0xc44, +0xc50==0, team<11 membres, +0x3b&0x40 →
    JoinResult(**5**) silencieux si en instance (node type ∈
    {4-9,0xa,0xb,0xc,0xe,0xf,0x10-0x13})
  - GreedyIsland → 0x1661 ; mode==3 && SLV≤49 → 0x16d7
  - OK → `MissionServerColosseumJoinRequest` [i32 charId] → mission
    `CColosseumFactory::Join` @0x8058… : zone colosseum absente →
    0xdc8 ; catégorie → file par catégorie (factory+0x30 map).
- 200 `ColosseumTeamQuery` (vide) — @0x825dd10 zone : char+0xbb4>0
  (en match) → `CColosseumFactory::ColosseumTeamQuery` → 529.
- 201 `ColosseumLeaveJoin` (vide) — @0x8245410 : si +0xbb4!=0 (en
  match) → texte 0xd1f + 530 [i32 charId][s16 0] ; sinon → mission
  `ColosseumLeaveJoin` → 530.
- 219 `ColosseumModeQuery` (vide) — @0x8245d30 : IsInNewerTown →
  0x2080 ; sinon `ColosseumDataQuery` mission (→ 587) +
  `ModeQueryResult` local → 552 [s16 mode].

### Sortants (client)
- 524 `EnterGladiatorTeamCreation` [i32][s16]
- 525 `GladiatorTeamInfo` [i32][i32][u32][u8]
- 526 `DisbandGladiatorTeam` [i32]
- 527 `EnterColosseum` [u8]
- 528 `ColosseumJoinResult` [s16 result] (1/5 = codes zone)
- 529 `ColosseumTeamInfo` [s16][u16 n]{s16 teamId, s16 nMembres,
  puis ×membre : i32,u8,s16,u8,s16,str,i32,u16,u8}[i8][i32][i32]
- 530 `ColosseumLeaveJoinResult` [i32 charId][s16 result]
- 552 `ColosseumModeQueryResult` [s16 mode(=schedId)]
- 587 `ColosseumQuery` — TC_ColosseumDataQuery @0x81fa770 :
  `[s16][u16 n i32 schedType][u16 n i32 beginEpoch][u16 n i32
  endEpoch][u16 n u32 extraTime][u16 n u8 actif][s16][u16 n s16
  beginHHMM][u16 n s16 endHHMM]` — pour chaque sched 200-203, les
  entrées du JOUR COURANT (entry+0x16 == tm_wday) ; le créneau actif
  (schedType == 200+factory+100 si ouvert) a flag=1 et extraTime=now.

## Implémentation
- [x] `ColosseumService` (Core) : mode (schedId courant depuis
      S_Schedule 200-203 + TuneMode priority), flag open, durée,
      files par catégorie, matchs runtime, catégorie(level/classLv),
      join/leave/answer.
- [x] `ColosseumPackets.cs` : codecs 524-530/552/587.
- [x] `ZoneEntryFlow.Colosseum.cs` : 7 handlers fidèles.
- [x] Inscription match → EnterColosseum 527 + téléport node 401
      (rounds cycliques pendant l'ouverture — approximation : match
      formé dès que 2 teams/queues complètes dans une catégorie).
- [x] Tests : wire codecs, gates, catégories (ColosseumTests, 13 cas).
