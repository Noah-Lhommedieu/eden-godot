# Lot : Gladiator Trial / CEvaluate (NCID 255 + entrée TeamOperation)

## Système décodé (preuves natives)

Arènes d'équipe "幻境" sur les nodes **type 0x12** (655-674, `S_RewardScore.ini`
20 defs). Instance = simple présence dans l'**event-area** `nodeTid<<16|0x15`
de la même scène — pas d'isolation, plusieurs équipes coexistent.

### Flux d'entrée (zone @ 0x82937b0, 0x80fa620, 0x80844190, mission @ 0x80c6a30)

- `TeamOperation [s16 op][s16 p1][s16 p2]` :
  - op 4, p1=3, p2=nodeTid — **check/rejoin** : si un membre est sur un node
    0x12 ≠ p2 → texte 0x230b ; si un membre est sur node 0x12 avec
    `char+0xbb4 != 0` → hérite son evalId → `EnterEvaluate` direct ;
    sinon (leader) → `TeamCheck check_type=3 node_id` → consentement.
  - op 4, p1=2 — tour solo (hors scope).
  - op 5 — `TeamCheckAnswer answer=<p1>` (réponse au dialogue de
    consentement) → relaisé mission.
- `ClientTeamCheckNotify` (577) `[s16 checkType][s16 param]` — envoyé à
  chaque membre par la mission via `WorldTeamCheck` ; `[4][0]` = check terminé.
- `EnterEvaluate` (@0x80fa620) : par char → mort=0x115a, node type
  interdit {4-9,0xc-0x13,10}=0x17a6, `CheckNodeTimes` (limite/jour par node,
  `player_node_times` vs `CNodeData+0x8a`) = 0x146c ; `team+0x9c = evalId` ;
  transport → event-area `nodeTid<<16|0x15` ; `CreateEvaluate` pose
  `char+0xbb4 = evalId` pour chaque membre ; fin → `OnStatusTimeUp(id,0,0)`.

### Machine d'états (@0x8082bb0, AllEVStatusTime = [0,10800,300,10])

- `OnStatusTimeUp(id, 0, _)` → `Start` → status=1 + event(1) 10800s →
  broadcast 607 type0 + type1.
- timer status 1 (3h) → `OnStatusTimeUp(id, 1, TRUE)` → **AllLeaveEvaluate**
  (revive + CharacterLeave + transport revive node) — pas de récompense.
- kill du mob `bossDefId` (S_RewardScore col2, def id) →
  `OnStatusTimeUp(id, 1, FALSE)` → **End** → status=2 + event(2) 300s.
- timer status 2 (loterie 5min) → `SendAllReward` (revive +
  LotteryOpen(7) auto-open + SendRewardMail + CharacterLeave par membre)
  → status=3 + event(3) 10s.
- timer status 3 → `CloseEvaluate`.

### Score (@0x845f410 GainKillScore, @0x8083f40 KillMonster, @0x845d340 End)

- kill : `kind==0xb` + `team+0x9c == evalId` → `GainKillScore(
  MonsterType[col5], ExpReward[col53])` : buckets 2→mon, 3/4→elite,
  5/6→boss (compteurs cappés aux max DSL), `rawScore += ExpReward` →
  broadcast 607 type1.
- mort : `deaths++` cappé `maxDeaths` → 607 type1.
- End : `elapsed = Timer-start` ; `monScore=Σ{thr≤monKills}` ;
  `eliteScore/bossScore` idem ; `deathBonus=Σ{deaths<thr}` ;
  `timeBonus` = 1re plage PassTime matchée (ops 1=>= 2=<= 3=> 4=<) ;
  `total = monScore*2 + deathBonus + eliteScore + bossScore + raw +
  timeBonus` ; grade = 1re plage TotalScore matchée →
  `{name, opens, buys, cost}` ; 5 RewardItems pondérés par membre ;
  607 type2 par membre ; `UpdatePlayerEvaluateInfo` + `UpdateEvaluateRank`
  (DBA→mission) ; `UpdateEvaluateInfo` (best record → 608).

### Loterie (NCID 255 `[i32 param]`, @0x845dfd0)

- `SEVLotteryInfo{opens=gradeA(+0xbc), buys=gradeB(+0xc0)}` par char
  (lazy à la 1re demande) ; cost = `gradeC(+0xc4)×1000` gold.
- **0-4** : ouvre la boîte i → opens-- ; déjà ouverte → rend l'open ;
  `chosen[i]=param` + item ajouté aux gagnés ; 607 type3
  `[3][opens][itemId][count][i]` ; opens<0 → texte 0x1fab.
- **-1** : claim — exige opens==0 → `SendRewardMail` +
  `[3][0][0][0][-1]` + révèle les non-ouvertes `[3][0][itemId][count][i]`.
- **5** : achète un open → buys==0 → texte 0x1fac ; `cost*1000` gold
  (échec → 0x232) ; buys--, opens++ ; `[3][opens][0][0][5 si buys>0 sinon 6]`.
- **7** : auto-open toutes les non-ouvertes tant qu'il reste des opens.

### CharacterLeave (@0x8082310) — revive si mort, `ClearPlayerEvaluate`,
si status==2 → LotteryOpen(7)+mail, `bb4=0`, 607 `[0][-1][0]` +
`[3][0][0][0][-2]`. 525 `ClientGladiatorTeamInfo [i32 evalId][i32 team?]
[u32 ?][u8 flag]` poussé par la mission (info trial UI).

## Implémentation (zone-locale — la couche mission de consentement
est internalisée dans TeamService zone-side ; le protocole client est
identique)

- [ ] 1. `RewardScoreParser` + `RewardScoreDef` (DSL `#:Cmd ...:`)
- [ ] 2. `GameDataService` : `RewardScores` loader
- [ ] 3. `NodeDef.MaxEntryTimes` (col 46) + `player_node_times` repo
- [ ] 4. `EvaluatePackets` : 607 types 0-3 + 608 + helpers
- [ ] 5. `EvaluateInstance` + `EvaluateService` (cycle complet,
      timers ZoneLoop.Schedule, scoring, loterie, mail, persistence)
- [ ] 6. `EntryState.EvalId`, `TeamEntity.EvalId`, hooks kill/death/
      leave/logout dans les partials Combat/Lifecycle
- [ ] 7. `TeamOperation` op4/p1=3 (check+rejoin) + op5 (answer) +
      577 consentement zone-local avec timeout
- [ ] 8. `EvaluateRepository` : `player_evaluate` upsert (best record),
      `team_evaluate` insert, `player_node_times` read/inc
- [ ] 9. Mission `OnDbaCommandAsync` : `UpdatePlayerEvaluateInfo`,
      `UpdateEvaluateRank`, `ClearPlayerEvaluate` → DB + rank 600 refresh
- [ ] 10. Handler 255 (remplace le stub) + tests unitaires
- [ ] 11. Build + tests + commit ; validation in-game ensuite
