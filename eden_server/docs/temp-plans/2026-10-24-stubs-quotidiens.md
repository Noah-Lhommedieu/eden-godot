# Lot « petits stubs » — 2026-10-24

## Analyse des restes (désasm zone)

Mort nativement (Execute vide ou IsA-vcall seul) — rien à porter,
stubs fidèles à commenter :
- 73 SloganUpdate, 105/106/121 ElfStone*, 234 GreedyIslandRankQuery,
  254 PayEventResponse.
- 142 MessageExit — reset op board 0x2b, feature bridée nativement
  (pas de state à garder chez nous).
- 163 NodeGroupReset — dépend du sous-système CNodeGroup (réservation
  d'instances de party) non porté ; reporté avec sa preuve.
- 220/231/253/255 — dépendent de sous-systèmes (recruit board mission
  CTeamManager, rangs PVP live zone+0x61c, CPayManager/CEvaluate) ;
  reportés avec preuves en commentaires de handlers.

### 162 QueryBattlefieldData — FAIT
`[]` → 482 `ClientUpdateBFData` `[u16 n][n×{u8 idx][i32 val}]` :
bloc char+0x97c[26] = 12 stats `battlefield_month` + 12
`battlefield_career` (+2 inutilisés). `BattlefieldRepository`
(insert-à-zéro comme le DBO natif), chargement lazy au premier
query.

### 224 UpdateLotteryBonus — FAIT
`[s16 autel][u16 extra_item][s16 extra_counts]` — validation
anti-dérive du bonus exact (`CPlayerFactory::UpdateLotteryBonus`
@0x81078xx) : si le couple client ≠ état serveur ou si le round
`extra_counts` ne contient plus `extra_item` (AllElfLotterysDBO
natif → `table.Round(CurrentWeek, counts)`) → reset_bonus=1,
extra_*=0, `ResetBonusAsync`, resync 448.

## Implémenté dans ce lot

### 164 UpdateBotData — TRIVIAL
`char+0xaa8/0xaa9 = u8[0x12], u8[0x13]` — deux booléens bot client.

### 218 NewPlayerGuide
`[s16 op][u16 guideId]` : op1 TriggerNewPlayerGuide (map
char → {id → count}, « déjà triggered » si count>0, count =
max+1), op0 Close, op2 Skip. Persistance `new_player_guide`
(`UPDATE … SET id_%02d_trigger/closed`) — colonnes id_01..id_20.

### 258 DailyAwardOP — système calendrier de connexion
Preuves : Execute @0x8246930, DailyUpdate @0x8420970, DayOut
@0x8411360, DailyLoginBegin @0x8420e10, GetDailyAward @0x8420f50,
UpdateDailyAwardToDB @0x8420610.
- Wire in : `[s16 op][i32 param]` — op0 check jour : DayOut →
  (nouveau jour : param∈{1,2}→DailyUpdate(false), status=0) ;
  DailyUpdate(true) INCONDITIONNEL ; 615 toujours émis, type =
  param==2 ? 5 : 0, champ +0x1e = local_26 (même jour ET status≠0
  → 1). op1 : DailyUpdate(true) → type 1. op2 : claim(param =
  jour 1..32) → type 2. Désactivé → type 4 champs nuls.
- Wire out 615 : `[s16 day_index][i32 online_s][i64 award_days]
  [i64 award_records][s16 type][s16 flag]` — 26 octets ; classe
  générée FAUSSE (aplatie 16B) → codec dédié `DailyAwardPackets`.
  flag = local_26 pour op 0, constante 1 sinon (tail partagé).
- char+0xdd0 day_index s16, +0xdd4 login_time, +0xdd8 cooldown
  (secondes cumulées), +0xddc award_days u64 (jours complétés),
  +0xde4 award_records u64 (jours réclamés), +0xdec login_status,
  +0xdf0 delta session, +0xdf4 flag session.
- `S_DailyAward.ini` : `[id][item_id][amount][?][required_s][?]`
  (CDailyAwardData +4/+8/+0xc). Jours 1..27 (+0x1c garde).
- `daily_awards` (id = player_id, INSERT zéro au premier login) ;
  `daily_awards_set` tag=-2 → flags (3 = activé, zone +0x638/+0x639).
- GetDailyAward : jour complété (award_days bit) & non réclamé →
  item (stackable → stack sinon slot libre, plein → 0x4f9) via
  AddItemFromDailyAward, bit records posé, save.
- DayOut = frontière minuit passée depuis login_time.
- DailyLoginBegin à l'entrée : nouveau jour → status=0, cooldown=0,
  day_index+1 si bit courant et def(jour+2) et jour+1<28 ;
  login_time=now + save toujours.

## Fichiers
- `Eden.Core/Data/Parsers/DailyAwardParser.cs` (S_DailyAward)
- `GameDataService` : DailyAwards + DailyAwardEnabled
- `Eden.Core/Database/Repositories/DailyAwardRepository.cs`
- `Eden.Zone/ZoneEntryFlow.DailyAward.cs` + `Codecs/DailyAwardPackets.cs`
- `EntryState.Daily`, chargement Lifecycle, cases 164/218/258
- `GuideRepository` minimal (new_player_guide) + EntryState maps
- Tests : codec 615 + parser + logique jours.
