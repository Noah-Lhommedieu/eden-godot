# Lot — Sous-système Evaluate (« gladiator trial ») + NCID 255

## Contexte natif (preuves zone/mission désasm)

Le NCID 255 `CNE_CZ_ZoneServerEvaluateLotteryOpen` n'est pas une loterie
isolée : `Execute` (zone @0x08251ff0) exige `char+0xbb4` (evalId actif)
et délègue à `CEvaluate::LotteryOpen`. Le système complet = instances
d'évaluation en équipe dans les nœuds S_Node type 18 (655-674, arènes
S101M/S101S…), score kills/temps/morts → grade → loterie de boîtes →
persistence `player_evaluate`/`team_evaluate` → rang 600.

### Flux d'entrée

1. Leader envoie `TeamOperation [s16 4][s16 3][s16 nodeTid]` → mission
   `CTeamManager::TeamCheck` (@0x80c6a30) : gates puis
   `ResetCheckState(team, 3)` + event timeout 60 s (`CEventTeamCheck`
   @0x81e9660) + `WorldTeamCheck` par membre → zone relaie
   `CNC_CZ_ClientTeamCheckNotify` 577 `[s16 3][s16 nodeTid]`
   (`Mission_TEC_WorldTeamCheck` zone @0x857d5c0).
2. Chaque membre répond `TeamOperation [5][answer]` →
   `TeamCheckAnswer` (@0x80c6520) → `SetCheckState` (sets oui=3/non=1/
   attente=0) → `IsCheckOver` (set attente vide) → `EndCheck`.
3. `EndCheck` type 3 (@0x81cb4e0) : groupe les membres par zone,
   `WorldTeamEnterEvaluate` par zone → zone
   `Mission_TEC_WorldTeamEnterEvalue` (@0x857e0e0)
   `[u16 n][n×u32 charId][s16 worldId][s16 nodeTid][i32 evalId]`
   → `CPlayerFactory::EnterEvaluate` (@0x80fa620).
4. `EnterEvaluate` : par char — mort → 0x115a abort ; map types
   interdits {4-9,0xc,0xe-0x13,10} → 0x17a6 ; `CheckNodeTimes` →
   0x146c ; `team+0x9c = evalId` (1er char) ; transport aire
   `nodeTid<<16|0x15`. Puis `CEvaluateFactory::StartEvaluate` →
   `CreateEvaluate` (@0x8084190 : evalId→CEvaluate, char+0xbb4=evalId)
   et `OnStatusTimeUp(evalId, 0, false)`.

### Cycle de vie (CEvaluateFactory::OnStatusTimeUp @0x8082bb0)

| transition | action | prochain statut | délai (AllEVStatusTime) |
|---|---|---|---|
| status 0 | `CEvaluate::Start` (marque +0x34, texte 0x1d92 au groupe) | 1 | 10800 s |
| status 1 flag=0 (boss tué) | `CEvaluate::End` | 2 | 300 s |
| status 1 flag=1 (timeout) | `AllLeaveEvaluate` | -1 (stop) | — |
| status 2 | `SendAllReward` | 3 | 10 s |
| status 3 | `CloseEvaluate` | — | — |

L'event `CEventEVStatus` porte le statut cible et appelle
`OnStatusTimeUp(id, status, flag = status==1)`. Chaque transition
pousse 607 type0 `[0][status][secondes restantes]` + type1 (scores).

### Compteurs & score (CEvaluate @0x845c000+)

- `GainKillScore(killType, score)` : type 2→+0x4c (cap +0x10),
  3/4→+0x50 (cap +0x14), 5/6→+0x54 (cap +0x18) ; +0x44 += score brut
  (ExpReward du mob, S_Monster col 53 = `MonsterDef.ExpReward`) ;
  broadcast 607 type1.
- `CharacterDie` : +0x48++ cappé +0xc.
- `CharacterKillMonster` (@0x8083f40) : gate killer.evalId ==
  team+0x9c du mob? non — `char+0xbb4` du tueur == evalId ; si
  `monsterDefId == eval+0x8` (bossId S_RewardScore) →
  `OnStatusTimeUp(id,1,false)` → End.
- `End` (@0x845d340) : +0x38 = elapsed ; scores DSL →
  total = `monScore*2 + dieBonus + eliteScore + bossScore + rawScore
  + timeBonus` ; grade via GetEndSetting ; tirage 5 boîtes/membre
  (GetRewardItems pondéré, flag==0 → sans remise) ; 607 type2/membre ;
  DBA `UpdatePlayerEvaluateInfo` + `UpdateEvaluateRank` (membres ×10).

### Wires

- **607** `CNC_CZ_ClientEvaluateInfo` (Serialize @0x80846b0) :
  - t0 `[i32 0][i32 status][i32 sec]` — statut + temps restant ;
    reset `[0][-1][0]` à CharacterLeave.
  - t1 `[i32 1][raw][deaths][mon][elite][boss][maxDeaths][maxMon]
    [maxElite][maxBoss]` — compteurs + plafonds.
  - t2 `[i32 2][elapsed][timeBonus][deaths][mon][elite][boss]
    [maxDeaths][maxMon][maxElite][maxBoss][dieBonus][monScore]
    [eliteScore][bossScore][rawScore][totalScore][str grade][opens]
    [buys][cost]` — 16 ints + string + 3 ints.
  - t3 `[i32 3][opens][itemId][count][index]` — pick ; sentinelles :
    `[3][0][0][0][-1]` après mail ; `[3][0][0][0][-2]` au leave ;
    `[3][opens][0][0][5|6]` après achat (5=buys restants, 6=plus).
- **608** `CNC_CZ_ClientUpdateEvaluateInfo` = best record perso :
  `[u32 node][u32 time][u32 score][str grade]` — émis au login
  (UpdateEvaluateInfo parcours map) + après End.
- **577** `CNC_CZ_ClientTeamCheckNotify` `[s16 checkType][s16 param]`
  — consent (3=evaluate) ; `[4][0]` = fin de check.
- **255** entrant : `[i32 value]` — 0-4 pick, -1 claim, 5 buy-open,
  7 auto-open (interne/SendAllReward).

### LotteryOpen (@0x845dfd0)

- `SEVLotteryInfo` par char : {opens = grade.a (+0xbc), buys =
  grade.b (+0xc0)} créé au 1er appel.
- pick 0-4 : opens-- (<0 → erreur 0x1fab + restore) ; boîte déjà
  ouverte (chosen≥0) → return ; chosen=pick ; push dans vec
  chosen(+0xd0) ; send `[3][opens][itemId][count][pick]`.
- -1 : si opens>0 → return silencieux ; SendRewardMail (mail système,
  itemId&0xffff + count (s16), strings gamedata 0x1fd2/3/4) ;
  `[3][0][0][0][-1]` ; puis liste boîtes non ouvertes
  `[3][0][itemId][count][idx]`.
- 5 : buys<1 → erreur 0x1fac ; cost<1 → log+return ; GainGold
  (-cost*1000) échec → 0x232 ; buys--, opens++ ; `[3][opens][0][0]
  [5 si buys>0 sinon 6]`.
- 7 : tant que opens>0, auto-ouvre la 1re boîte non ouverte
  (chosen=i, push chosen) ; utilisé par SendAllReward/CharacterLeave.
- CharacterLeave : revive si mort ; ClearPlayerEvaluate ; si
  status==2 → LotteryOpen(7)+SendRewardMail ; char+0xbb4=0 ;
  `[0][-1][0]` + `[3][0][0][0][-2]`.
- AllLeaveEvaluate : revive + CharacterLeave + transport node sauvé
  (char+0x638).
- SendAllReward : par char du groupe : revive + LotteryOpen(7) +
  SendRewardMail + CharacterLeave.
- CloseEvaluate : ClearPlayerEvaluate ×membres + remove.

## DSL S_RewardScore.ini

`id|nodeTid|bossDefId|challengeCmds|timeCmds|gradeCmds|flag|
(itemId|name|count|weight)×20` — Big5.

- `KillMonster <cat 2|3-4|5-6> <count> RewardScore + <score>` →
  règle (threshold≤kills → +score), buckets mon/elite/boss.
- `TeamDieNum <count> RewardScore + <score>` → deaths < threshold
  → +score (bonus survie).
- `PassTime <op> <v1> <op2> <v2> RewardScore + <score>` → 1re plage
  matchée. Ops : `>=`→1, `<=`→2, `>`→3, `<`→4.
- `TotalScore <op> <v1> <op2> <v2> Evaluate <grade> <opens> <buys>
  <cost>` → 1re plage → grade. Données réelles : opens 1-3, buys=0.
- Caps = max threshold par bucket (GetKillMon/Elite/Boss/TeamDie).
- Tirage : map poids cumulé → `random() % totalWeight` ×5 ;
  flag==0 → sans remise (5 uniques).

## Implémentation

### Fichiers

- [ ] `src/Eden.Core/Data/Parsers/RewardScoreParser.cs` — parser +
  `EvaluateDef` (règles, caps, grade, tirage pondéré). Preuves DSL
  citées ci-dessus.
- [ ] `src/Eden.Core/Game/Evaluate/EvaluateInstance.cs` — domaine
  runtime : compteurs, statut, membres, boîtes tirées, SEVLotteryInfo,
  chosen. Méthodes : GainKillScore, CharacterDie, End (score+grade),
  LotteryOpen (logique pure → liste d'effets paquets), AutoOpen.
- [ ] `src/Eden.Core/Game/Evaluate/EvaluateService.cs` — registre
  evalId→instance + compteur d'ids.
- [ ] `src/Eden.Core/Database/Repositories/EvaluateRepository.cs` —
  `player_evaluate` (best record), `team_evaluate`,
  `player_node_times` (gate CheckNodeTimes).
- [ ] `src/Eden.Zone/Codecs/EvaluatePackets.cs` — builder corps 607
  t0-t3 (OutBinStream cru — le .g.cs aplati est incorrect : layout
  conditionnel).
- [ ] `src/Eden.Zone/ZoneEntryFlow.Evaluate.cs` — orchestration :
  TeamCheck consent (check d'équipe zone-local), EnterEvaluate
  (gates + transport aire), timers via Loop.Schedule (10800/300/10),
  hook kill (FinishMobDeathAsync), hook mort joueur, NCID 255,
  CharacterLeave/AllLeave/SendAllReward/Close, mails, persistence.
- [ ] `EntryState.EvaluateId` + `EntryState.TeamCheckPending`.
- [ ] `TeamHandlers` op 4 (param1=3 → check evaluate) + op 5
  (réponse consentement) — routed via `s.Loop.PostOrRunAsync`.
- [ ] `GameDataService` loader `S_RewardScore.ini` + `ZoneServices`.
- [ ] `Program.cs` wiring EvaluateService + repository.

### Décisions de portage

- Consentement implémenté zone-local (TeamEntity) : la mission
  native ne fait qu'orchestrer ; le client voit le même 577.
  Refus/timeout → annulation pour tous (577 [4][0]).
- evalId = compteur zone (sémantique native = id gladiator team
  alloué mission ; chez nous l'instance suffit).
- `CheckNodeTimes` : gate via `player_node_times` (entrées/jour
  limitées — colonne S_Node +0x8a). Si non implémentable proprement
  → log + pas de gate (entrée libre) plutôt que blocage.
- Pas de cross-world : `param_6=true` toujours (monde unique).
- `ClientGladiatorTeamInfo` 525 (enregistrement battle-royale) :
  hors scope du lot — layer d'inscription schedule, pas requis pour
  le trial op4. Marqué dépendance dans le handler.
- Boîtes tirées à `End` par membre connecté à ce moment (natif :
  itère les chars du CNodeGroup) — chez nous les `CharIds`
  enregistrés à l'entrée, session résolue au moment voulu.

### Tests

- [x] Parser : ligne réelle 655 (DSL complet + 14 items, Big5) —
      872 records réels parsés + bug `RangeRule.Matches` (op2
      inversé) trouvé par les tests et corrigé vs
      `GetTimeBonusScore` @0x0845fdd0.
- [x] Scoring : thresholds cumulés, die <, plages, formule End.
- [x] Tirage : flag=0 → 5 uniques, poids respecté.
- [x] LotteryOpen : pick/opens/buys/cost/-1/5/7 états + erreurs.
- [x] Wire 607 t0-t3 : longueurs + ordre des champs vs désasm.
- [x] `dotnet build` 0 erreur / suite complète (17 tests
      EvaluateTests + 934/934).

### Validation client

- [x] Op4 headless → consent 577 → entrée arène (2 sessions, node
      655, groupe d'instance 3).
- [x] HUD trial (607 t0 [1][10800] + t1 compteurs) au start ;
      texte 316 0x1d92.
- [x] Boss kill 63399 → End → 607 t0 [2][300] + t2 résultat 85 B
      (elapsed 21, time 1604, death 401, total 2005, grade 'C').
- [x] NCID 255 : pick → 0x1fab, achat → 0x1fac (grade C = 0
      ouverture/0 achat — fidèle, les clauses KillMonster du nœud
      655 sont structurellement inatteignables : aucun mob spawné
      n'a de KillScore col 51), claim -1 → 5 boîtes non ouvertes.
- [x] Persistence : player_evaluate (score/grade/time ×2 membres) +
      team_evaluate (id=3).
- [x] Sortie : auto-open 7 + mail si gains (0 gain → pas de mail,
      natif), CharacterLeave à la déco, destruction groupe 655,
      cycle 2→3→close complet (~300 s + 10 s, close eval=3 loggé).
- [x] Limite journalière player_node_times — 4ᵉ entrée refusée
      (texte 0x146c) : la rotation/quota marche.
- [x] Fix score natif : KillType=col 4 1-idx (CMonsterData+0x10,
      param_1 de GainKillScore) + KillScore=col 51 1-idx (+0xc4,
      param_2) — avant : MonsterType/ExpReward → 161755/grade null.
      Commit 3a099ac.
- [ ] Client graphique (référence + Reforged) : même parcours —
      reste à faire avec un vrai client.
- [ ] Docs : NCID-EN-JEU.md, BASE-DE-DONNEES.md (player_evaluate),
  FEATURE-MATRIX.
