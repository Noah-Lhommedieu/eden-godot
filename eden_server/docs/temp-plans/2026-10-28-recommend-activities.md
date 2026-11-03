# Lot : Recommend Activities (activités recommandées) + relais CNE_CZ

Preuves désasm : zone @0x840be30 (CheckFinished), @0x840c0b0
(FinishedProcess), @0x8108e60 (ResetByWeek), @0x810ba80
(GiveReward), Deserialize S_RecommendActivity @0x8620880 ; mission
@0x81f2800 (TC_UpdateRecommendActivity), @0x80780a0
(RefreshRecommendActivities), @0x8143890 (SetTracing).

## S_RecommendActivity.ini — 22 colonnes (Deserialize @0x8620880)
c1 id | c2 group (lu, ignoré) | c3 +0x04 max_lv u8 | c4 +0x05 min_lv u8 |
c5 +0x06 s16 type | c6 +0x08 bool flag (0 = éligible) | c7 +0x0c i32 |
c8 +0x10 titre | c9 +0x14 u32 | c10 +0x18 desc | c11 +0x1c str |
c12 +0x20 enchant | c13 +0x24 durée | c14 +0x28 item | c15 +0x2c qté |
c16 +0x30 exp_mult | c17 +0x34 cp_mult | c18 +0x38 lifelong | c19 +0x3c gold |
c20 +0x40 u32 | c21 +0x44 u32 | c22 +0x48 target

## Wire
- 558 CZ QueryRecommendActivities [u8 flag][u16 id]
  flag=0 → 559 [s16][i32 ts][u16 n][n×(u16,u32,u8)][u16 m][m×u16]
  flag!=0 → CheckFinished : pending && progress>=target && reward →
  claimed + DBA "Update RecommendActivity ActivityID=N Parameter=P
  Finished=1" + 560 [u16 id][i32 count][u16 1]
- 226 SetRecommendActivityTracing [u16][u16] → mission 835 [i32][u16][u16]
- 831 Z→M refresh [i32][i32][u16 n][n×(u16,u32,u8)][u16 m][m×u16] →
  mission : DELETE + INSERT recommend_activities +
  UPDATE player_characters.recommend_activities_time
- 832 M→Z UpdateRecommendActivities [i32 char][u16 id][u32 count] →
  progress map ; si count>=target → pending + FinishedProcess →
  DBA "Update RecommendActivity ... Finished=0" + 560 flag=0
- 835 mission : UPDATE is_tracing + renvoi des lignes en 832

## Reset hebdo (RefreshRecommendActivities @0x81098a0)
recommend_activities_time != semaine courante OU map vide →
ResetByWeek : clear, peuple les defs éligibles (min_lv<=lv<=max_lv,
flag==0), refresh 831, recommand_activities_time=now.

## Récompenses (GiveRecommendActivitiesReward @0x810ba80)
item (+0x28,+0x2c) → AddItem ; enchant (+0x20,+0x24×10) ; exp
(+0x30×level base), cp (+0x34×base cp), gold (+0x3c) ;
lifelong (+0x38) → DBA "GMGainLifelong Money = %d".

## Divers dans le lot
- ChurchReserve 250 [i32] : -1 → "CloseWedding" ; sinon
  "VisitWeddingInstance LoverID= %d" (mission : 0x1c64 si pas de
  wedding, 0x1c65 si non ouvrable ; TP instance = sous-système).
- ExchangeItemPin 66 [str][str] → monde texte 636
  "exchange item %d %hd %s %s" (TC Server_TC_ExchageItem monde).
- QueryRecommendActivities flag=0 → 559 ; flag!=0 → CheckFinished.
- UpdateBotData : Execute natif = garde sans effet → mort, annoté.

## États EntryState
RecommendProgress{id→count}, RecommendFinished{id→0/1},
RecommendTracing[], RecommendTime.
