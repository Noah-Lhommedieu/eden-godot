# Plan — Autels Eden Crystal / Eden Gem (lottery natif)

Date : 2026-10-19. Objectif : porter fidèlement le système lottery du
ZoneServer natif (3 autels) — tables DB réelles, probas natives,
persistance complète.

## Preuves natives (zone/decompiled.c)

- **NCID 129 `CNE_CZ_ZoneServerGoLottery`** — Deserialize @0x825db00 :
  `[s16 autel][u16 item][s16 a][s16 b][s16 c][u32 d]` = 16B. Execute
  @0x825d8c0 : autel 0→GoLottery, 1→GoInGameLottery, 2→GoDiamondLottery.
- **Fenêtres** : `MailOpenBox` (NCID 95) op 150 → `CInGameLotteryOp`
  (type 0x54) + 432 F4=-1 ; op 151 → `CLotteryOp`/`CDiamondLotteryOp`
  (type 0x56) + 432 F4=2. Op stockée char+0x670 ; op+8 = flag "going".
- **Cast** : `ClientStrengthenCasting` 433 — ingame `[0x96][10][0]`,
  normal `[0x97][10][0]`, diamond `[0x97][10][2]` ; enchant EffectID
  dédié, durée 10×100ms ; op->going=1.
- **GoXxxLottery** (@0x811b130 / @0x811a5e0 / @0x81197d0) :
  - item==0 → ReceiveItem ; champ b!=0 → ReceiveWeekCountItem (normal
    et diamond uniquement).
  - stage==0 : valide le ticket (ingame : 0x66ac→(0x927c→type3) |
    0x66ab | 0x66ac ; normal : 0x9cef→(0xac63→type3)|0x9cef|0x9d31 ;
    diamond : 0x6c91→(0x927c→type3)|0x6c90|0x6c91). Crystal-payment :
    ingame 0x66ad(crystal_b>9)/0x66ae(crystal_c>9) ; normal 0x9d32/0x9d33 ;
    diamond 0x6c92(crystal>9).
  - stage 1..6 (diamond 1..4) + item!=0 → cast continue (gratuit).
  - can_go_next==0 → silence.
- **XxxLotteryResult** (@0x8118220 / @0x8118f70 / @0x8117590) : guard
  op type+going+stage<7(<5 diamond). stage 0 → consomme le ticket
  (DropItemByItemID ×1) ou 10 crystals (stage→2/4) ; stage+=1 ; dice.
- **XxxDiceLottery** (@0x8117740 / @0x8118490 / @0x8116ad0) : map
  `round→entries` (variante de la semaine courante) ; roll uniforme
  [0,total) sur probabilités cumulées → item/amount/reset_bonus/
  can_go_next=!cant_gonext. bulletin=1 → BroadCastScreenMessage type 8
  via DBA (textes 0x1c3e/0x1c3f ingame, 0xa2d/0xa2e normal, marquee 2/3).
  Nouveau max stage (+0x924/+0x8fc/+0x94c) → extra_item_id aléatoire
  uniforme parmi les entrées du round + extra_multiple selon params
  formule +0x938..+0x944 (ids 590-593 = 100/0/0/0 → ×2).
  Win : plus += 1 (type 1) ou +10 ; clamp [500,5000] ; stage<2 INSERT
  sinon UPDATE ; UpdatePlus. Lose (round absent) : plus += 2/20, reset
  complet, DELETE. Émet **448** `[s16 type][u16 item][s16 counts]
  [s16 stage][i32 plus][i32 event][s16 crysB][s16 crysC][u8 reset]
  [u8 gonext][s16 exItem][s16 exCounts][s16 exMult][i32 weekPoint]
  [s16 weekStage][s16 1]`.
- **XxxReceiveItem** (@0x81119f50 / @0x811aab0 / @0x81191f0) :
  bonus (stage==extra_counts && item==extra_item) → count×extra_multiple,
  reset_bonus=1. Items 0x66ad/0x66ae (ingame), 0x9d32/0x9d33 (normal),
  0x6c92 (diamond) → +crystals (cap 999, textes 0x1c40-43/0xa38-3b/0x1f56).
  Sinon slots libres ≥ ⌈count×mult/stack⌉ (stack = item+0x76) →
  AddItemFromElf (bind si lottery_type==3) ; plein → texte 0x4f9, garde.
  Reset extra si consommé ; **449** `[s16 crysB][s16 crysC][s16 flag]
  [u8 resetBonus][i32 weekPoint][s16 weekStage]` (flag : 0 normal,
  1 ingame, 4 weekcount ; probablement 2 diamond) ; reset état ;
  Update*LotteryData (ligne persistée à zéro) + CharacterLotteryCounting.
- **CharacterLotteryCounting** (@0x80ed890) : bulletin→+P670 ; exact
  bonus→+P669 ; !gonext→+P668 ; sinon +P667 ; cap P672=250 (diamond,
  +0xe08/+0xe0c) / P671=0 (normal → mort). Points sur
  `player_lottery_point` (account_id).
- **WeekCountItem** (@0x80eeda0 / @0x80ef110) : paliers depuis round 0
  de la table (probability=seuil, item_id, item_amount) : point≥seuil →
  item (ou crystal si itemId==spécial) ; stage+1, >4 → reset point+stage
  + texte 0x230a ; 449 F3=4 ; persist weekcount.
- **NCID 184** `[u16 v][u16][s16 autel]` → `ClientUpdateLottery` (511) :
  `[s16 type][u16 version][u16 0][u16 n][n×u32 itemIds][u16 n][n×s16
  amounts][u16 n][n×s16 seuils][u16 n][n×u32 palierIds][u16 n][n×s16
  palierQty]`. Items d'affichage = entrées des rounds de la semaine,
  UNK_087d1e74 = [9,6,6,6,6,6,6,6] par round.
- **Semaine** : `DateCheck` = tm_wday (dim→7) → week = wday (dim=0).

## Tables DB (réelles, peuplées)

- `fnaccount.ingame_lottery` : rounds 1-7 × weeks 0-6 × 6 entrées.
- `fnaccount.new_lottery` : round 0 (5 paliers : seuils 10/15/50/100/250
  → 27792/41320/41322/41323/45370) + rounds 1-5 × weeks 0-6 × 9 entrées.
- `fnaccount.lottery` : rounds 1-7 × weeks × 6 entrées, pas de round 0.
- `fndb1.in_game_lottery` / `new_lottery` / `lottery` : état joueur
  (schémas quasi identiques ; new_lottery sans crystal_c).
- `fndb1.player_lottery_point` : (account_id, normal_point, normal_stage,
  diamond_point, diamond_stage).

## Éditions

1. `Eden.Core/Game/Lottery/LotteryModels.cs` — rows, table par autel,
   état joueur runtime+persisté.
2. `Eden.Core/Database/Repositories/LotteryRepository.cs` — 2 connexions
   (fnaccount rewards, fndb1 état) ; CRUD exact des DBO natifs.
3. `Eden.Core/Game/Lottery/LotteryService.cs` — tables chargées, roll
   pondéré, paliers, week courante.
4. `Eden.Zone/ZoneEntryFlow.Lottery.cs` — handlers 129/184, casts,
   results, receive, weekcount, bulletin (DBA→mission→20→474).
5. `EntryState` : LotteryOp/Going, 3 états, PendingLottery, week points,
   AccountId.
6. `ZoneServices` + `Program.cs` : repo+service, ACCOUNT_DB_CONN.
7. `MissionServer` : cas "BroadCastScreenMessage" → NCID 20 str.
8. `ZoneEntryFlow.Mission.cs` : case 20 → 474 broadcast (type 10→2).
9. `SocialHandlers` : retrait ops 150/151 (interceptés par le flow pour
   créer l'op) ; `HandleStrengthenCloseAsync` : cancel lottery.
10. Build + tests codec 448/449/511.

## Vérification

- [x] dotnet build 0 erreur/0 warning (wiring complet : 95/129/184
  dispatchés, close 150/151, entry load, mission BroadCast→20→474).
- [ ] Test live des deux autels (ticket + cristal + palier hebdo).
