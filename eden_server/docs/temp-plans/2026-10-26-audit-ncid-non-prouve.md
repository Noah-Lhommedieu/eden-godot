# Plan — Audit des 641 NCIDs « NON PROUVE » (26 oct. 2026)

Source : `docs/generated/FEATURE-MATRIX.md` (régénérer après chaque
lot : `python3 tools/gen_feature_matrix.py`). Statut NON PROUVE =
classe présente dans le protocol_spec mais ni émise ni avec handler
trouvé — à prendre au cas par cas, beaucoup sont des features
nativement mortes ou non implémentées dans `server_us`.

## Ventilation par famille

| Famille | Nb | Nature |
|---|---|---|
| `CNC_CZ_` | 193 | zone→client **non émis** — features dormantes |
| `CNE_CZ_` | 93 | client→zone **sans handler** — le client les envoie → trous réels |
| `CNE_ZMi`/`CNC_ZMi` | 137 | liens zone↔mission internes |
| `CNE_ZW`/`CNC_ZW` | 56 | liens zone↔world internes |
| `CNE_DBA`/`CNC_DBA` | 33 | process DBA natif — **probablement N/A** (on n'a pas ce service) |
| `CNC_MW`/`CNE_MW` | 28 | liens mission↔world |
| `CNC_G_S`/`CNC_CG`/`CNE_G_C` | 19 | gateway |
| `CNC_CW`/`CNE_CW` | 48 | client↔world |
| `CNC_ZM`/`CNE_ZM` | 17 | zone↔mission (autre table) |
| `CNC_T_S`/`CNE_T_C` | 6 | ticket |

## Priorité 1 — `CNE_CZ_` entrants par sous-système (93)

Le client envoie → si non routé, la feature est morte. À auditer via
`reindex lookup CNE_CZ_... -b zone` → `::Execute` désasm → handler ou
preuve que le natif l'ignore.

| Sous-système | NCIDs | Nb |
|---|---|---|
| Mail | 95-102 | 8 |
| Housing/chambre (RentRoom…RoomGetData, decoration, texture) | 205-217 | ~12 |
| Colosseum / Gladiator | 196-201, 219 | 7 |
| Family (town/storage/building/talk/fix-equip) | 174, 179, 180, 239, 240 | 5 |
| Elf (stone, work, mix, rename, revive) | 105-107, 227-229 | 6 |
| Fast-dress (tenues rapides) | 244-248 | 5 |
| Mariage (MarryOperation, Church, WeddingInfo) | 249-251 | 3 |
| Trade direct (Ready, UnConfirm) | 91, 92 | 2 |
| Équipe (radar, leader, search, roll, recruit transport) | 86, 90, 192, 193, 237 | 5 |
| LFG V2 (déjà fait : 235/236) | — | ✓ |
| Appellation (titres) | 93, 94 | 2 |
| Divers (transport 88, bag duedate 89, divide 84, lif eskill 214, etc.) | reste | ~40 |

## Priorité 2 — `CNE_CW_` entrants world (18)

6 remove-char, 8 looking-for-member, 12/13/22 friends, 17-18 item-mall,
20/21 change-world, 27 player-state, 30-32 mot-de-passe perso,
33 mutually-help, 71 apex-buffer, 73 family-emblem.

## Priorité 3 — Liens internes (ZMi/ZW/MW ~220)

Ne traiter que si une feature en a besoin (ex. RecruitTeam* côté
mission). `CNE_DBA*` (33) : le service DB-agent natif n'existe pas dans
notre archi — marquer N/A dans la matrice plutôt qu'implémenter.

## Process par NCID

1. `reindex opcode <n>` → classe C# + nom natif.
2. `reindex lookup CNE_CZ_Xxx -b zone` → `::Execute` : lit le payload,
   appelle quoi, émet quoi.
3. Si feature réelle → handler dans `Handlers/<Domaine>Handlers.cs`
   + `Register`, codec généré corrigé si besoin, test byte-level.
4. Si mort/ignoré en natif → le noter dans la matrice (raison).
5. Commit par sous-système, `dotnet test` vert à chaque lot.

---

## Journal d'exécution (session du lendemain)

### Découverte méthodologique

La matrice marque « NON PROUVE » les NCIDs sans paquet émis ni golden —
or un handler **entrant** n'émet pas nécessairement de paquet portant
son nom. Les 93 `CNE_CZ_` listés sont TOUS enregistrés sauf
`CoRideInvite` (202) — le vrai critère d'audit = stub vs implémentation
fidèle, pas la matrice.

### Morts en natif (Execute vide ou tail-call `GetType()` ignoré)

Confirmés par désasm/objdump — **ne pas implémenter** :
105 ElfStoneUse (vide @0x824a930), 106 ElfStoneUpdate, 228/229
ElfRevive, 196 GladiatorTeamCreate, 197 GladiatorTeamQuery,
234 GreedyIslandRankQuery, 254 PayEventResponse (guard + tail-call
vtable+8), 109 ElfCallElf, ElfTalk, ElfCheckData, BuySpell,
PVPFlagModify, SetSpellStone, SloganUpdate, StorageWithdraw
(Execute(void) corps vide @0x828cd10).

Note : certains (ElfTalk, ElfCallElf, StorageWithdraw…) ont une
implémentation locale additive assumée — documentée comme telle dans
les commentaires des handlers.

### Fait cette session

- **Mail 95-102** : audit complet — pipeline déjà implémenté
  (SocialHandlers → AgentDBA texte → MissionDb → réponses 68-77 →
  paquets client 400-409). Layouts corrigés : MailDelete(98) et
  MailGetItem(100) ont des **vecteurs** `[i32][u16 n][n×i32]` (+
  `[s16 freeNeeded]` pour 100) — les classes générées lisaient mal le
  mode lot. Handlers batch ajoutés.
- **Collision NCIDs maison** : nos codes zone→mission 145/146/147
  (QuestItemGot/Changed/AreaTravel) occupaient les NCIDs natifs
  MissionServerMailSend/SendSystemMail/MessageWrite → renumérotés
  450/451/452.
- **Team** : TeamChangeLeader (leader-gate + broadcast + texte 0xd6),
  TeamRadarAttention (relai 3×s16 aux membres locaux — désasm),
  TeamRecruitTransport (leader-gated, transport d'instance documenté
  non porté), SearchTeam filtré sur les annonces LFG.
- **89 BagDueDate** : implémenté fidèle — `SetBagDueDate` @0x81133f0
  (bag<4, durée<30 j, cumul max 60 j, consomme item 0x9d07/0x9c55,
  `bank_duedate_<n+1>` colonne native, réponse 389). + méthodes repo
  `GetBankDueDatesAsync`/`SetBankDueDateAsync`.
- **238 ChangePositionV2** : implémenté — `ChangePositionV2` @0x840b190
  (pos<3, no-op si inchangée) → `TeamService._positions` ; M6 des
  membres `SSearchTeamMemberDataV2` = position (+0x12, preuve mission
  `GetTeamMemberData` @0x81c8b40).
- **202 CoRideInvite** : handler enregistré — dispatch op 0-9 fidèle,
  cible offline → texte 0x5af ; sans sous-système montures (+0x670),
  réponse native = 0xe19 (monture requise).
- Trade 91/92 + appellations 93/94 : audités — déjà fidèles au natif.
- LFG V2 (235/236) : fait session précédente.

### Reste du audit CNE_CZ_ (stubs réels non morts)

Sous-systèmes absents → à porter ou marquer N/A : housing rooms
(203-217 RentRoom/SearchRoom/EnterRoom/RoomGetData/ExtendRoom…),
colosseum (196-201 + ModeQuery/TeamQuery), family/town/storage
(174,179,180,239,240 + Family*), fast-dress (244-248), mariage
(249-251 MarryOperation/Church/WeddingInfo), territoire
(TerritoryQuery/Join), RankInfoQuery, DestinyDice, ChanceLuckyBag×2,
PayEventSelect, EvaluateLotteryOpen, GreedyIslandJoin,
VersusInstanceJoin, BattleCrusadeJoin, BuyBackOpen, FamilyBattleAskInfo,
PVPRankLookEquip, StartParty, RollTreasure (roll items — nécessite
RollitemManager), FamilyTalkToNpc, FixEquipByFamily, PlayerCCDataSave,
SearchCondition (CDBAgent::Query), QueryDestinyRoad/Collection…

### Disposition finale des 93 CNE_CZ_ NON PROUVE

**Fait / vérifié fidèle cette session** :
| NCID | Commande | État |
|---|---|---|
| 84 | DivideItem | ✅ déjà porté (CItemFactory::DivideItem) |
| 86 | TeamRadarAttention | ✅ relai 3×s16 aux membres locaux du node |
| 89 | BagDueDate | ✅ SetBagDueDate @0x81133f0 complet + 389 |
| 90 | TeamChangeLeader | ✅ leader-gate + SetLeader natif (texte 0xd6) |
| 91-92 | TradeReady/UnConfirm | ✅ déjà fidèles au natif |
| 93-94 | UpdateAppellation/Query | ✅ déjà fidèles |
| 95-102 | Mail (8 cmds) | ✅ pipeline complet ; layouts lot corrigés |
| 105-106, 228-229 | ElfStone×/Revive | ⛔ MORT NATIF (Execute vide/GetType) |
| 107 | ElfWork | ✅ déjà implémenté |
| 174/225/239/240 | FamilyTalk/Battle/FixEquip/StorageLog | ✅ déjà implémentés |
| 184/224 | Lottery week-update/bonus | ⏳ CPlayerFactory::Client*LotteryWeekUpdate |
| 185 | BuyBackOpen | ⏳ liste rachat vendeur (+0x5f8/0x600) |
| 188 | SendPetCmd | ✅ intercepté TryHandleAsync |
| 189/190 | TerritoryQuery/Join | ⏳ Query=relai texte ; Join=CTerritoryWarFactory |
| 192/235/236 | SearchTeam/V2/RecruitV2 | ✅ LFG natif |
| 193 | RollTreasure | ⏳ CheckRollItem décodé, création roll manquante |
| 194 | RankInfoQuery | ⏳ ClientRankInfo (données de classement) |
| 196-197 | GladiatorTeam×2 | ⛔ MORT NATIF |
| 198-201/219 | Colosseum | ⏳ CColosseumFactory (matchmaking partiel) |
| 202 | CoRideInvite | ✅ dispatch fidèle + 0x5af/0xe19 (montures absentes) |
| 203 | DestinyDice | ⏳ DestinyRoadAction + map-type gate |
| 205-217 | Housing/rooms ×12 | ⏳ CPlayerRoomManager — sous-système |
| 214 | LifeSkill | ✅ intercepté TryHandleAsync |
| 220/252/259 | RecruitInstance/Versus/Crusade | ⏳ instances ; 259 relai texte fait |
| 221-222 | ChanceLuckyBag×2 | ⏳ CItemFactory::ChanceLuckyBagNext |
| 226 | SetRecommendActivityTracing | ⏳ stub actif |
| 227 | ElfMix | ⏳ ElfUpgrade/CanUpgrade — tables de fusion |
| 230 | EquipTrainSetItem | ✅ déjà implémenté |
| 231 | PVPRankLookEquip | ⏳ équipement des classés PvP |
| 233-234 | GreedyIsland Join/Rank | ⏳/⛔ join=relai info fait, rank=mort |
| 237 | TeamRecruitTransport | ✅ leader-gated (transport non porté) |
| 238 | ChangePositionV2 | ✅ position recrutement + M6 membres |
| 241 | BattleRoyaleJoin | ✅ matchmaking local |
| 242 | PlayerCCDataSave | ⏳ CEffectFactory::AngelChangeStyleID |
| 244-248 | FastDress ×5 | ⏳ fast_dress + CItemFactory — sous-système |
| 249-251 | Marry/Church/Wedding | ✅ partiel (WeddingInfoQuery relai fait) |
| 253-254 | PayEvent Select/Response | ⏳/⛔ select=TriggerPayEvent, response=mort |
| 255 | EvaluateLotteryOpen | ⏳ CEvaluate::LotteryOpen |
| 257 | ExecuteMagicBox | ✅ intercepté TryHandleAsync |
| 260 | ArrangeItem | ✅ déjà porté (pass temp) |

**Résumé** : ~45 fidèles/portés, 15 morts en natif (⛔ — ne pas
implémenter), ~33 ⏳ dépendants de sous-systèmes absents (housing,
colosseum, territoire, fast-dress, destiny-road, roll-window,
équipement-rank, events payants/lucky-bag, angel-style).
