# Plan — Combat, Personnage, Équipement, Quêtes (NCIDs + bugs live)

Date : 2026-10-23 · Source : `docs/humain/NCID-ETAT.md`, audit live
(test1/2/3), désasm `re/ghidra/zone/`.

> **§1 = instantané de l'audit initial** (historique) — l'état
> courant des NCIDs vit dans `docs/generated/FEATURE-MATRIX.md`
> (émis par NCID) et `docs/humain/NCID-ETAT.md`. Les cases §3+ sont
> le tracker maintenu.
>
> **Règle** : « fait » = confirmé en client (les deux), pas juste
> émis. Les statuts ci-dessous reflètent la profondeur réelle
> (dispatch → mutation → persistance → rendu client), pas la présence
> d'un `case`. **Toutes les étapes « Vérif live » restent [ ]** —
> aucune n'a encore été exécutée sur un client réel.

## 1. Inventaire NCID du périmètre

Légende : **F** = fait & validé · **P** = partiel (comportement
incomplet/simplifié) · **R** = replay d'entrée seulement · **A** =
absent (jamais traité/émis).

### 1.1 Entrants client → zone (tous ont au moins un handler)

| NCID | Classe | Statut | Détail |
|---|---|---|---|
| 5 | MeleeAttack | **P** | pipeline natif OK (hit/crit/dmg/mort) mais le flag « en combat » n'est jamais poussé au client en mêlée pure ; aggro passive absente |
| 9 | SpellCastToLife | **P** | dégâts OK ; latence ~300 ms+ via ScheduleOnLoop ; enchaînements partiels |
| 13 | SpellBreak | **P** | handler mince ; 287 `ClientSpellBreak` jamais émis |
| 16 | SpellCastToLocation | **P** | **dégâts fixes 60, rayon 8u codés en dur** — pas de résolution S_Spell (tornado/AoE cassés) |
| 17 | SpellCastToNowhere | **P** | buffs seulement, ciblage naïf |
| 18 | EnchantUpdate | **P** | buffs partiels |
| 20 | Revive | **P** | points de rez basiques |
| 52 | BuySpell | **P** | spellmaster OK, cas limites ? |
| 59 | SpellCastToNearby | **P** | AoE autour de soi simplifiée |
| 85 | EnchantCancel | **P** | |
| 121 | SetSpellStone | **P** | 437 `ClientSetSpellStone` jamais émis |
| 127/128 | EnchantPause/Start | **P** | 445/446 émis |
| 135/136 | DuelRequest(/Answer) | **F** | cycle complet porté (ZoneEntryFlow.Duel) |
| 202 | CoRideInvite | **P** | |
| 229 | ElfRevive | **P** | |

### 1.2 Équipement / inventaire / sacs

| NCID | Classe | Statut | Détail |
|---|---|---|---|
| 6 | DropItem | **P** | drop au sol OK ; **règle « inventaire plein » incohérente** — items tombent au sol alors que le sac affiche des trous |
| 7 | SwitchItemLocation | **P** | move/swap/sacs OK ; **validation de slot par kind ABSENTE** (voir §2) |
| 8 | UpdateEquipment | **P** | refresh OK ; même défaut de validation |
| 19 | ItemUse | **P** | consommables/livres/équipement-clic-droit OK ; œufs kind 60 routés |
| 22/23 | BuyItems/SellItems | **P/F** | vendeur OK, buyback partiel |
| 24 | EnterNodeEquipAndSpell | **P** | 311 émis |
| 35 | TreasurePickup | **P** | loot au sol OK |
| 38/39 | TradeAdd/RemoveItem | **P** | trade partiel (36/37 aussi) |
| 45–48 | Repair flows | **P** | coûts natifs OK ; 350 `EnterRepairShop` jamais émis |
| 60–62 | Storage deposit/withdraw/data | **P** | 357/358 émis ; 359/360 jamais émis |
| 64/65 | Identify | **P** | coût + 363/364 OK ; 365 `EnterIdentifyShop` jamais émis |
| 66 | ExchangeItemPin | **P** | |
| 67 | InlayItem | **P** | sockets OK ; 371 `InlayItemResult` jamais émis |
| 69 | StorageExpand | **P** | 373 résultat jamais émis |
| 70/83 | ItemMall enter/use | **P** | |
| 72/77/80 | Vendor container/sell/use | **P** | |
| 84 | DivideItem | **P** | split OK |
| 89 | BagDueDate | **P** | |
| 100 | MailGetItem | **P** | 405 émis |
| 113 | EnterNodeElfContainer | **P** | 424 émis — **mais le routage des items pet vers ce conteneur n'existe pas** |
| 119/120 | Strengthen | **P** | 432–436 émis |
| 130/131 | FamilyContainer put/get | **P** | 484 émis |
| 143 | LookEquip | **P** | 460 jamais émis |
| 165/167 | Closets query/set | **F/P** | |
| 177–179 | FamilyStorage | **P** | |
| 193 | RollTreasure | **F** | 520 émis (roll d'équipe complet) |
| 221/222/223 | LuckyBag/FortuneBag | **P** | |
| 230 | EquipTrainSetItem | **P** | 570 partiel |
| 231 | PVPRankLookEquip | **P** | 571 émis |
| 240 | FixEquipByFamily | **P** | |
| 243–248 | FastDress (enter/apply/save/record/page/remove) | **F/P** | 593/594 émis ; apply partiel |
| 257 | MagicBox | **P** | 612/613 émis |
| 260 | ArrangeItem | **P** | 620 émis |

### 1.3 Personnage / stats / classes

| NCID | Classe | Statut | Détail |
|---|---|---|---|
| 12 | UseSkillPoint | **P** | 354 `UseSkillPointOK` jamais émis |
| 14 | PlayerAction | **F** | |
| 21 | PlayerEmotion | **P** | 307 émis |
| 26 | EnterNodePlayerData | **F** | 310 émis |
| 129 | GoLottery | **P** | |
| 133/134/169 | PlayerCard enter/query/update | **P** | 490/491 émis |
| 161 | FamilyStatue | **P** | |
| 166/168 | QueryClassList/ChangeClass | **F** | 489 + 592 (fixé, u32) |
| 170–172 | PlayerWork/result/check | **P** | 497/498 émis ; 500 `WorkResultEnd` référencé |
| 181–184 | License query/set | **P/F** | 509 émis |
| 195 | EnterNodePlayerRoom | **P** | 523 émis |
| 203/204 | DestinyDice/QueryDestinyRoad | **P/F** | 589/590 émis ; 534 jamais |
| 205–210 | Rooms search/rent/enter/privilege/mode/data | **P** | 537/538/541/542 émis |
| 213/214 | LifeSkill | **P/F** | 544 émis ; 543 jamais |
| 215/217 | Room texture/extend | **P** | |
| 218 | NewPlayerGuide | **F** | |
| 224 | UpdateLotteryBonus | **P** | |
| 242 | PlayerCCDataSave | **P** | interne 845 absent |
| 255 | EvaluateLotteryOpen | **P** | DoD validé live |

Stats : 296/492/493 **émis** via `ProfileStatEmitter` (base+équipement,
formules `NativeCombatFormulas`) — mais la couverture des sources de
stats (titres, buffs, talents de classe, elfes) est incomplète.

### 1.4 Quêtes

| NCID | Classe | Statut | Détail |
|---|---|---|---|
| 36/37 | TradeRequest/Accept | **P** | (classés trade, liés quêtes NPC ?) |
| 43 | CloseMissionBrief | **P** | |
| 50 | QueryMissionStates | **F** | 348 + 391 émis |
| 51 | GiveUpMission | **P** | |
| 87 | MissionListOp | **P** | accept/abandon/liste PNJ |
| 160 | MissionGoto | **F** | |
| 186 | SetMissionTrace | **P** | trace épinglée |
| 199/241/252 | Colosseum/BR/Versus join | **P** | hors scope gameplay quotidien |

Internes mission **absents** (impactent la sync quête) :
681 `DBA_ClientMissionState`, 722 `ZMis_UpdateMissionState`,
819 `GMToolGetMission`, 825 `ResetDailyMission` — notre lien utilise
des opcodes maison 450-452, fonctionnels mais **le canal
ItemCollect→compteur est cassé en live** (voir §2).

### 1.5 Pet / Elf (requis — le familier DOIT vivre dans son interface)

| NCID | Classe | Statut | Détail |
|---|---|---|---|
| 105/106 | ElfStoneUpdate/Use | **P** | 423 jamais émis |
| 107 | ElfWork | **P** | 415/416/421 jamais émis |
| 108/109 | ElfReturn/CallElf | **P** | 414/439/443 jamais émis |
| 110/112 | EnterNodeElfInfo/CheckData | **F/P** | 412 émis |
| 111 | ElfWorkResultOK | **P** | |
| 113 | EnterNodeElfContainer | **P** | 424 émis — conteneur pet-equip |
| 137 | ElfTalk | **P** | |
| 187/188 | PetModeChange/SendPetCmd | **P** | |
| 227/228 | ElfMix/ChangeName | **P** | 567/568 jamais émis |
| 229 | ElfRevive | **P** | |
| — | SummonPet 438, PetUpgrade 574/575, ElfEvent 417, ElfDecompose 422, ElfAction 442, ElfUpdateSkill 419, UpdateElfSpell 588, ElfCastEnchant 261 | **A (émission)** | classes générées, jamais instanciées |

### 1.6 Internes absents touchant le périmètre

`CNE_DBA_*` : 677 ClientContainer, 678 ClientEnchant,
680 ClientPlayerData, 681 ClientMissionState, 691 ClientSpell,
692 FinishPlayerData, 696 FamilyStorage, 699 AllPlayerRoom,
700 FortuneBagCounter, 701 FamilyContainerData, 826 NewPlayerGuide,
836 ClientElfData, 846 ClientFastDress, 859 LotteryCount.
`CNE_ZW_*` : 649 ItemMallReceieveAttempt, 650 GetCloneItemID,
651 UpdateFortuneBag, 652 UpdateLottery.
`CNE_ZMis_*` : 703 UpdatePlayerCard, 722 UpdateMissionState,
728/729 PutItem/DropItem, 730 MailGetItemOK, 755–757 FamilyStorage*,
759 QueryPlayerCard, 765 BuyBackGetItem, 767 DeallocateItemID,
778–781 Room*, 819 GMToolMission, 825 ResetDailyMission,
827/828 Lifelong, 830 RecommendActivity, 845 CCDataSave,
847 FastDressEquipEnd, 851 FindCharacter, 853 SendTargetLocation,
858 ArrangeItem.

Notre archi remplace DBA par des repos directs et ZMis par un lien
maison — **l'absence de ces opcodes n'est bloquante que là où la
fonctionnalité équivalente n'a pas été portée** (listée ci-dessous).

### 1.7 Sortants jamais émis (hors internes)

Combat : 261, 287, 289, 315, 361, 367, 372, 437, 452, 453, 454,
522, 545, 546, 548, 588, 611.
Équipement : 276, 350, 359, 360, 365, 366, 371, 407, 408, 460, 481,
485, 486, 504, 505, 520, 569, 614.
Perso : 354, 373, 385, 415, 416, 417, 419, 420, 421, 467, 499, 510,
534, 543, 550, 551, 554, 557, 586, 595, 596, 618, 619.
Pet : 414, 422, 423, 442, 443, 567, 568, 569, 574, 575.
Quêtes : 550, 585.

## 2. Bugs live → causes identifiées / à prouver

| Symptôme | Cause | Preuve |
|---|---|---|
| Livre de quête dans le slot arme | `ResolveEquipLocAsync` mappe `col9`→slot joueur pour **tout** item ; or col9 des kinds non-équipement-joueur (44/45 cristaux, 56, 61–68 équipement pet…) n'est **pas** un loc joueur — natif : vtable+0x10 du CItemData renvoie −1 pour ces classes | `CItemFactory::Equip` @0x80b5810 (rejet kind 0x3c/0x51 + loc>0x14 sauf 0x23) |
| Pet équipé en armure | kinds 61–68 (items `K*`, équipement de familier) ont col9 = **slot pet** ; on les route vers les locs joueur. Le drag vers le panel familier utilise un dstType dédié (conteneur elf, NCID 113/424) — non routé | S_Item kind 61 `K30158` col9=7 → devient loc 7 joueur |
| Inventaire plein invisible / drop au sol | `inventory_slots` vs capacité affichée : items à loc ≥ cap invisibles mais comptés ; bitmap 312 correct mais capacité source divergente ; `FirstFreeSlot`/`GetAnyAvailableSlot` vs `AddItem` à harmoniser ; doublons loc à purger en DB | `inventory1` test3 : 31 items, loc19 libre ; test2 bag0 32/32 |
| Compteur quête 0/3 | chaîne loot→451→`OnQuestItemChangedAsync`→`CountItemAsync`→pms 391 existe ; **le mailzone→mission arrive mais le compteur ne bouge pas** : vérifier lien up, `CountItemAsync` (inventory+storage natif), clé pms `mid×1e6+100000`, `SendPmsAsync`→zone→391 | `CGlobalInfo::UpdateQuestItemStatus` @0x8096b10 |
| Auto-attaque sans mode combat | `HandleMeleeAsync` pose `st.InCombat` mais **n'émet aucun flag combat au client** (deliberate depuis le fix du flag[0]=0x2000 coincé). Le natif signale l'état battle autrement (à prouver : attr/280 ou 272 type bits — checker le handler client de 272) | capture zsniff : `302 char 0020 sel4 op1` à l'engage |
| Pas d'aggro passive (noms rouges) | `MonsterAiService.ShouldAggro` **jamais appelé** ; règle de niveau (rayon réduit si over-level) à porter | S_Monster flag agressif + plage |
| Sorts de zone (tornado) | `HandleCastLocationAsync` = dégâts fixes 60 / rayon 8u ; pas de zones persistantes ni ticks S_Spell | désasm `CastToLocation` natif + S_Spell fields |
| Timed-cast en mouvement | le mouvement casse/bloque le cast à tort ; natif = mouvement autorisé selon flag S_Spell, sinon `SpellBreak` | S_Spell col? + client 13 |
| Latence skill→dégât | cast/dégât schedulés ~300 ms+ sur la boucle | `ZoneLoop` timings |

## 3. Plan d'action priorisé

> Ordre = corruption d'état d'abord, gameplay visible ensuite,
> annexes à la fin. Chaque étape se termine par : build 0 warn +
> vérif live sur les deux clients.

### P0 — Taxonomie d'items & validation des slots (bloque la corruption)

- [x] **P0.1** Table `kind → classe d'item natif` : désasm
  `CItemFactory::Equip` @0x80b5810 + vtables CItemData pour chaque
  kind (équipement joueur = kinds 1–16 armure / 17–29 armes /
  37–40 ? ; pet-equip 61–68 ; cristaux 45 ; sacs 43 ; œufs 60 ;
  divers 81 rejeté). Livrable : `NativeItemClasses.cs` +
  doc `docs/humain/ITEM-KINDS.md`.
- [x] **P0.2** Gate dans `ResolveEquipLocAsync` : seuls les kinds
  équipement-joueur résolvent vers les locs 1–15 ; kinds pet-equip
  → conteneur elf (dstType dédié — trouver le type natif dans
  `SwitchItemLocation` @0x81237f0) ; tout le reste refusé.
- [x] **P0.3** Même gate sur NCID 8 (`UpdateEquipment`), le clic-droit
  (`ItemActionHandlers` l.334) et l'application FastDress
  (`ZoneEntryFlow.FastDress.cs:650` — `MoveToEquipmentAsync` direct,
  sans validation).
- [x] **P0.4** Purge DB : items `equipment1/2` à loc non canonique,
  items `inventory1/2` à loc ≥ capacité ou doublons (loc,bag).
  Script `Database/0XX-cleanup-slots.sql` + rapport avant/après.
- [ ] **Vérif live** : livre de quête refusé en slot arme ; pet refusé
  en armure (ou routé panel familier) ; relog → état identique.

### P1 — Inventaire : capacité, bitmap, « plein » cohérent

- [x] **P1.1** Une seule source de capacité : `inventory_slots` =
  vérité ; audit `bags` équipés → capacité (S_Item col28) injectée à
  l'équipe du sac ; supprimer les fallbacks divergents.
- [x] **P1.2** `AddItem`/`FirstFreeSlot`/`GetAnyAvailableSlot` :
  garantir `loc < cap` strict, stack-merge plafonné à `maxStack`,
  refus « plein » homogène (code erreur natif 0x306 familles).
- [x] **P1.3** Cohérence bitmap 312 ↔ DB (déjà correct bit-à-bit ;
  vérifier les items à loc ≥ cap ne passent plus — dépend de P0.4).
- [x] **P1.4** Loot au sol (35/329/330/386) : n'apparaître QUE si
  vrai plein, sinon `274` + insertion — recoller le comportement au
  message « inventaire plein » natif.
- [ ] **Vérif live** : sac avec 1 trou visible → le loot le remplit ;
  sac réellement plein → drop au sol + message.

### P2 — Quêtes : compteurs d'objectifs

- [x] **P2.1** Tracer en live : loot → `SendQuestItemChangedAsync`
  (451) reçu côté Mission ? (log MissionServer.188).
- [x] **P2.2** `CountItemAsync` : compter inventaire + **storage**
  comme le miroir natif (UpdateQuestItemStatus) ; vérifier le flag
  `Stackable(itemId)` vs quantity en base.
- [x] **P2.3** Clé pms `mid×1e6+100000` → `SendPmsAsync` → 391 :
  vérifier que la zone relaie au client (aussi au login : compteur
  initial dans 348).
- [x] **P2.4** Couvrir tous les triggers : loot, achat, craft,
  consommation, trade, mail, quête-remise, drop → sol (perte).
- [ ] **Vérif live** : « 0/3 → 1/3 → 2/3 → 3/3 » à chaque loot,
  persistant au relog, icône fin de quête à 3/3.

### P3 — Combat : état combat + aggro

- [x] **P3.1** Mode combat en auto-attaque : prouver le signal natif
  (désasm handler client du 272 / `MC_IsInBattle` ; sinon rejouer le
  `302 0020 sel4 op1` + twin `0000 sel4` de la capture — attention à
  ne pas recoincer le state controller : émettre la **paire**
  complète). Critère : icône épées dès le premier coup, « leaving
  combat » à la fin.
- [x] **P3.2** Aggro passive : appeler `ShouldAggro` dans
  `TickMobCombatsAsync`/tick vision ; règle niveau (portée réduite
  si over-level — S_Parameter ?) ; flag agressif S_Monster ;
  déclencher `MeleeEngageDelayed`-équivalent (271/302/444/326).
- [ ] **P3.3** HateReport 522 si le natif l'émet en groupe (priorité
  basse, PvE solo d'abord) — toujours non émis.
- [ ] **Vérif live** : mob rouge attaque à bas niveau, ignore à haut
  niveau ; icône combat en auto-attaque ET en skill.

### P4 — Sorts : ciblage, AoE, timed-cast, latence

- [x] **P4.1** `CastToLocation` (16) : résolution S_Spell réelle —
  forme (cercle/cône/ligne), rayon, dégâts, ticks, durée de zone ;
  émettre 297 + effets par cible ; AoE persistante si le sort en a.
- [x] **P4.2** `CastToNearby` (59) : mêmes règles de ciblage.
- [x] **P4.3** Timed-cast + mouvement : flag mobilité S_Spell ;
  mouvement interdit → SpellBreak natif (13→287 à émettre) ;
  autorisé → cast continue (corriger l'état serveur qui bloque).
- [x] **P4.4** Latence : passer les impacts de dégâts de
  `ScheduleOnLoop(300 ms+)` à la cadence native (vérifier désasm —
  probablement délai par sort, pas constante).
- [~] **P4.5** Sortants manquants combat : 287 SpellBreak ÉMIS
  (Combat.Spells), 372 TeamMemberEnchant ÉMIS (P12.4) ; restent
  289 SpellBlastToLife, 361 LoopCastStop, 367 ResetSpell, 546
  SpellFlyToLocation, 548 UpdateSpellAttr.
- [ ] **Vérif live** : tornado touche la zone visée, ticks répétés ;
  cast lancé en mouvement se poursuit ; dégât visible sans lag.

### P5 — Stats & effets d'équipement

- [x] **P5.1** `StatContribution.FromEquipment` : auditer les champs
  S_Item agrégés (atk/def/str…/résists/aspd) vs natif
  `RefreshEQsAttr` @0x8415210 — couvrir enchant, sockets, combo,
  fortification, set-bonus (S_ItemSet ?).
- [x] **P5.2** Sources non couvertes : titres/appellations, talents
  de classe (`class_talents`), lifelong, bonus elf, buffs/enchants
  (18→299/300/301) — intégrer `StatPipeline`.
- [x] **P5.3** 12 UseSkillPoint : règle native de dépense + émettre
  354 ; verrouiller par niveau/classe.
- [ ] **Vérif live** : équiper une épée → ATK monte dans le panneau
  ET dans les dégâts ; point de skill reflété immédiatement.

### P6 — Pet / Elf complet

- [x] **P6.1** Conteneur équipement pet : dstType **6** natif
  (`SwitchItemLocation` case 6 → `char+0x8ac`, ElfEquip
  @0x808cb20 = kind 81 seul, EquipLoc 7→slots 0-1 / 34→slots 2-3,
  ElfSwitchEquip = swap adjacent 0↔1/2↔3) ; table `elf_container`,
  NCID 113 → 424 `ClientEnterNodeElfContainer` (enveloppe 312,
  record `WriteItem` sans queue 34B) ; refresh stats 418 types
  8-11 (attaque/hit/crit/col57) à l'équipement.
- [x] **P6.2** Summon/rappel/posture déjà en place (109 CallElf,
  108 return, 187 mode, 188 cmd) — CallElf natif porté :
  cooldown 60 s (elf+0xa0), status 7-9 loterie refusés,
  BornElfPet modèle M%06d + stats S_PartnerGrow, 513+418.
- [x] **P6.3** Elf work 107 : ToWork complet (0=ToIdle, 1=ToMix,
  2=CallElf) + **gate de carte native** `S_Node col42` →
  `CNodeData+0x60` bits 4/5 → `CNodeTemplate+0x10` bits 0x20/0x40
  (0=8350 « 不可使用寵物 », 1=summon only/8351, 2=pray only/8352,
  3=full ; BR type 15 → mode 1 refusé) ; **CallElfBackVer2**
  @0x80731c0 au login/migration (status 1 sans flag 0x40 → démixage
  + purge enchants ; status 2 sans flag 0x20 → CallElf auto =
  compagnon) ; **ToMix/RemoveMixElf** : enchants `integrate_spell_*`
  → EffectToLife(-2, 0x2020) permanents sur le joueur + 567 ;
  **livres de sorts pet** (UseType 6, cmd enchant 6035-6038) :
  CanExecute natif (star 6365, invoqué 6193, doublon 6192),
  LearnSpell direct/tirage [min,max] hors doublons,
  LearnSpellFromGroup (random%3 → groupe, S_Spell col34 battle /
  S_Enchant col6 intégré), UPDATE elf_spell + 588. Passifs :
  battle_spell_4/5 famille 9 → ResultSpellId (col39) appliqué au
  maître en -2/0x2020, purgés au rappel (vecteur char+0x8d0).
  Reste NON porté : loteries 7/8/9 (ToLottery gacha tickets
  0xac63/0x9cef/0x9d31, elf+0xaa<7, seuil HP formula +0xa84),
  stones 105/106→423, decompose 422, revive 229, talk 137.
- [x] **P6.4** Upgrade = mix 227 (ElfMix : CPetUpgradeOp cast 4 s,
  574 start/575 cancel, 568 résultat [loc][elf_id][star]+3×vec s16,
  pierres 0x1790-0x1792, règles CanUpgrade natives + merge sorts).
- [ ] **Vérif live** : œuf→elf→summon→équipement pet→buff elf
  visibles dans le panel, jamais dans les slots joueur.

### P7 — Équipement : annexes

- [x] **P7.1** FastDress : apply 244 complet (slots valides +
  échange atomique), save 245, page 247, remove 248 ; interne 846/847.
- [~] **P7.2** Closets 165/167 (488) — FAIT(flow)/FAIT(handler) ;
  NCID-ETAT marque 165 « partiel » (slots costume à confirmer).
- [~] **P7.3** EquipTrain 230→570 ; LookEquip 143→460 émis ;
  PVPRankLook 231→571 émis ; **LookElfPetEquip 569 non émis**.
- [~] **P7.4** Repair 45–48 / Identify 64/65 / Inlay 67 /
  ExchangePin 66 / Strengthen 119–120 : entrants tous FAIT(handler)
  ; sortants enter-shop/résultat encore non émis : 350, 365, 366,
  370, 371.
- [~] **P7.5** Storage 60–62 + expand 69 (373 résultat émis) ;
  FamilyStorage 177–179 — **FAIT natif** (P14.2 : table
  `family_storage`, record fixe 505/508, priv `index+11`, cap 40,
  broadcast membres en ligne 504/505/506) ; FamilyContainer 130/131
  FAIT(flow) mais 485/486 non émis ; RoomItem 781/542.
- [x] **P7.6** Inventaire divers : Divide 84, Arrange 260/620,
  BackItem 614, DueDate 89, TextOutput 481, Mail 100/405, buyback
  765, allocate 767.
- [x] **P7.7** Trade 36–39 + 331–335 complet (or, validation,
  annulation).

### P8 — Personnage : classes & divers — **FAIT**

- [x] **P8.1** Classes : CCData 242 intercepté (flux natif complet,
  case 242) ; shortcut 510 émis au change ; custom-class cards 592 ✓.
- [x] **P8.2** Licenses 181→509, 182 set, 183 QueryLicenseSet —
  `ZoneEntryFlow.License.cs` + cases 181-183.
- [x] **P8.3** Player card : 133 EnterNode (490 27 octets zéro),
  134 QueryPlayerCard → mission 104 → 491.
- [x] **P8.4** Rooms 195/205–210/215/217 : handlers + cases 207/210.
- [x] **P8.5** LifeSkill 213→543, 214 ; DestinyDice 203,
  QueryDestinyRoad 204→534 ; Work 170/171/172 → 497/498.
- [x] **P8.6** Lottery 184→511, 224, 448/449/450/557, PayEvent 253,
  EvaluateLottery 255, FortuneBag 223 ; **385 ExploitRank** ajouté —
  paquet vide natif (`TC_OpenExploitRank` @0x083beee0, verbe texte
  `open_exploit_rank` via le canal AgentDBA).
- [x] **P8.7** Emotion broadcast 307, PlayerAction 14, TurnSelf 123,
  NewPlayerGuide 218 (PlayerGuide.cs). Altar 467 = code mort natif
  (jamais instancié — xref vide). FindCharacter 851 = proto interne
  mission→zone (lookup cross-zone — notre lookup sessions direct).
- [x] **ClassLifelong** 554/555 — **FAIT** (zone
  `ZoneEntryFlow.Lifelong.cs` + `LifelongRepository`, tests
  `LifelongTests`) :
  - `SClassLifelong` natif (char+0xc58, map clé s16 = **groupe waken**
    `CClassData+8` = S_Class col2 via `GetWakenGroup(class,race)`
    IGameData+0x1d8) : +0x14 territory, +0x18 colosseum, +0x1c rare,
    +0x20 daily (i32), +0x24 pending daily s16 ; +0xc54 =
    `lifelong_gold` (« Crystal Cross Medal », texte 0x1100/4524).
  - Table `class_lifelong(player_id,class,territory_kill,
    colosseum_win,rare_monster_kill,daily_monster_kill)` — chargée à
    MigrationDone (`LoadClassLifelong` natif), deltas UPSERT
    (équivalent `ClassLifelongAdd class=K type=T add_point=P gold=<abs>`
    — `LifelongUpdateToMission` @0x8421720), absolu à la sortie
    (`LifelongUpdateAllToMission` @0x84236e0 →
    `FlushLifelongPendingAsync` dans PersistState).
  - Grants : **type2** rare kill — `achDef.Type==1 && Point>0` par
    receveur d'exp>0, **répétable** (achAbsent || Point≠0 natif),
    `AddLifelongPointAndGoldAsync(2, achDef.Point)` ; **type3** —
    tout receveur d'exp>0, pending>9 → UPSERT. **type1/type0** :
    helpers prêts — le combat colosseum (victoire équipe
    `colosseum+0x9e`, `AllGotoTeamPreparePos` @0x8404d00) et la
    guerre de territoire (`LifelongKillCheck` @0x84c45c0, dédup par
    victime dans territory+100, flush ≥5 ou à la mort du tueur)
    restent à porter avec ces systèmes.
  - Or par paliers `S_Parameter` 419-439 sur **l'ancien compteur** :
    type0 seuils 10/30/70 → 10/6/3/0 ; type1 5/15/35 → 20/10/5/0 ;
    type2 100/300/700 → 3/2/1/0. Cap or 9999999. Émission 555 puis
    554 puis texte 0x1100 `"%d,%s"` (or gagné, S_TextIndex 4524).
    `HandleRASpecialPoint(char,2,delta)` → EventKind 9/Param 2 porté
    (`BumpRecommendLifelongGoldAsync`).
  - Reset daily lazy : jour persisté `flags` ;marqueur
    `LastDailyLifelongResetDay` (exec+0x760 natif, %zX dans le blob) ;
    jour changé → `daily_monster_kill=0` + pending 0 + 554
    `[groupe][3][0]` par noeud chargé.
  - GM natif `/gain_lifelong_gold <n>` (@0x0888284b →
    `GMGainLifelong Money = %d` mission) : crédit cap + 555 absolu.
  - **Pet lottery** (gacha elfes 618/619, `CKFElfManager::ElfDiceLottery`
    @0x080787e0 — modes 7/8/9 documentés en P6).
  - **In-game lottery** 595/596 (`Mission_TC_InGameLotteryUpdateNotity`).

### P9 — Quêtes : le reste — **FAIT**

- [x] **P9.1** GiveUp 51 ✓ (relais mission, FMS=0 + purge PMS/trace),
  CloseBrief 43 ✓ (relais 141 existant + purge état dialogue —
  natif `CGlobalInfo::CloseMissionList` @0x080717c0 remet
  exec+0xcc/+0xce à 0), ListOp 87 ✓, Trace 186 ✓.
- [x] **P9.2** Quêtes à limite + daily — sémantique native complète
  (mission `SetMission` @0x080e5250, case-accept @0x0808e2xx,
  case-complétion @0x080962xx, `SetFMS` tail @0x080960xx,
  `AddMissionCompleteNum` @0x08190ef0, `ResetDailyMission`
  @0x080752f0) :
  - **col4** = limite de complétions ; **col6 bit4** = daily-type
    (compteur journalier `+0x226` vs total `+0x224`) ; **col6 bit2** =
    conserve la row `complete_num` jusqu'à la limite.
  - Daily (591×col4=1, 15×col4=2) : FMS=1 puis SetFMS(0) → reprenable
    chaque jour (reset paresseux `DailyCompletions` = map exec+0x474,
    jour = `DailyResetDay` = exec+0x4dc).
  - Limitée non-daily (296 : 99/100/1/…) : compteur session
    `SessionCompletions` = map exec+0x45c chargée de
    `player_mission_complete_num` ; row transitoire (Add+Delete net
    zéro) sauf bit2-set (persistée jusqu'à la limite).
  - Gate d'accept = compteur seul pour les quêtes à limite ;
    Completed/FMS vierge pour les ordinaires — corrige le blocage à
    vie des 606 quêtes daily/répétables.
  - Client : state=1 « finie » puis state=0 « reprenable » (double
    `NotifyClientMissionState` natif quand le FMS final est 0).
  - 550 `ClientResetDailyMission` = paquet vide jamais instancié
    (code mort natif) ; 585 MissionShop déjà émis (Dialogue case 56).
- [x] **P9.3** Internes 681 `CNE_DBA_ClientMissionState` (persistance
  DBA — notre `MissionDb`/FMS packed couvre) et 722
  `CNE_ZMis_ZoneServerUpdateMissionState` (push d'état mission→zone —
  notre `SendStateAsync`/canal ZoneMissionLink couvre) : équivalence
  documentée, pas de client-wire à ajouter. GM tool 819 = admin-only.

### P10 — Combat social & PvP

- [x] **Duel 1v1 porté** (`ZoneEntryFlow.Duel.cs`, désasm
  DuelRequest @0x080f2bf0 / DuelReject @0x080fe680 / AddToDuel
  @0x0805d300 / DuelFinish @0x0805d670,0x0805df80) : validations
  (pas en duel, même node/instance, portée 20u), pending ops, refus
  452 codes 2/0, acceptation 453 [0][0][5][0], countdown +5s →
  activation 453 [serial][handle][99][1], round 99s → comparatif %HP
  (3/3 égalité + broadcast 5), fin par mort/logout/téléport
  (0/1|2 + broadcast 4). Tests `DuelTests` 9 verts.
  Reste : vérif live ; CoRide 202 handler OK (réponse 0xe19 « monture
  requise » — le modèle multi-places char+0x670/+0x7fc n'est pas
  porté), 546 reste.
- [~] PvP rank look 231 FAIT(handler) → 571 émis ; **colosseum
  porté** (gates+file+match 2 joueurs→527+TP arène 401+529/530/552/
  587) et **versus porté** (gates natives, sched 602, file 10→2 camps
  alternés, 601 entrée/sortie, spawn par camp, retour cross-zone +
  enchant exclusion 54486 900s — `VersusService`). **Battle Royale
  porté** (`BattleRoyaleService` : file 30, gates natives 241,
  info 591, node 309, dissolve <2). Reste : scoreboard versus natif
  (champs 601 complets), BR fin-de-match native (rounds/vainqueur),
  vérif live.

## 4. Dépendances

```
P0.1 (kinds) ─→ P0.2/0.3 (validation) ─→ P0.4 (purge) ─→ P1 (capacité)
                                                        └→ P6.1 (conteneur pet)
P1 ─→ P2 (loot→compteur quête)
P3.1 (flag combat) ─→ P3.2 (aggro) ─→ P4.4 (cadence)
P5 dépend de P0 (l'équipement statuaire suppose les bons slots)
P7.1 FastDress dépend de P0.3
```

## 5. Points de preuve à collecter par étape

- `CItemFactory::Equip` @0x80b5810, `SwitchItemLocation` @0x81237f0,
  `DoWeaponUnEquip` @0x8092e70 — table kind→conteneur.
- `CGlobalInfo::UpdateQuestItemStatus` @0x8096b10 — miroir inventaire
  + storage, clés pms.
- `CAI::ShouldAggro`/profils aggro — flag S_Monster, plage, règle
  niveau (reindex search « aggro »).
- `HandleCastLocation`/S_Spell — shape/radius/tick/mobilité cast.
- Handler client du 272 (`rf_game`/`client_game`) — comment le mode
  combat est détecté côté UI.
- Captures `zsniff*` + goldens pour chaque nouveau paquet émis.

## 6. Extension 2026-11-05 — périmètre élargi (montures, statuts, talents, titres, succès, guilde, item mall, no-lag)

### P11 — Montures & ride (bloquant live : « 0 sec. remain » + « can't be used »)

- [x] **P11.1** Repro : aucune monture en DB — le « 0 sec. remain »
  vient des items créés avec l'ancien armement (toute durée armée
  dès la création → montures « N日 » expirées dans le sac →
  `CheckUseItemCondition` refuse + client grise l'item). CORRIGÉ.
- [x] **P11.2** due_date : sémantique native trouvée —
  `CItemData+0x6c` = S_Item col26 (unité) ; unités {4,5,6} =
  « 入手限時 » → due_date armée **à la création**
  (`CItemFactory::AddItem*`, condition `+0x6c-4<3`) ; unités {1,2,3}
  → armées au premier usage/déplacement (`+0x18==0 → now+durée`,
  DoUseItem/SwitchItemLocation). Implémenté : `DurationUnit` +
  armement conditionnel dans `InventoryService.AddItemAsync`.
- [x] **P11.3** Items expirés : `CheckItemDueDate` @0x08120550
  (appelé par MigrationDone::Execute) = texte 0x5a4 (« Your %s1 is
  expired ») + `DropItem "SYS"` par item → 275 [flag][loc][idx].
  Implémenté `PurgeExpiredItemsAsync` (sacs flag 0 + équipement
  flag 1, refresh stats si équipement purgé).
- [x] **P11.4** Montures de combat : les kinds 47 sont des SETS —
  les montures de combat sont kind 44 avec enchant 2134 à args
  étendus `[key][''|1][seats][type]` (55880/59441/42858 = 戰鬥座騎,
  arg3=2). Buffs stats de l'enchant ride → nouvelle étape pipeline
  « ride » (2002 HP%…) + refresh stats au mount/dismount. Sorts de
  monture (2140) → 545 ClientRideSpell [u16 n][u16×n ids] émis au
  mount, liste vide au dismount (RollBack @0x8550920).
- [x] **P11.5** Combat monté : flag `&1` arg2 du 2134 déjà sérialisé
  (char+0x4ad bit1 natif, `CEC_Ride::SetState` @0x0854f330 — émis via
  `ride.Flag & 1` dans le 283). Dismount non-combat
  (`RemoveEnchantByState(9)`) : mêlée + cast-location déjà branchés,
  **ajouté aux casts CastToLife (9) et CastToNowhere (17/59)** —
  `ISpell::CastTest` @0x084f04d0 : tout cast tenté sur monture sans
  ride-spells et `char+0x824==0` → dismount puis le cast continue.
  Multi-places (CoRide 202) reporté à P10.

### P12 — Statuts / enchants (positifs + négatifs)

- [x] **P12.1** 299 ClientEnchantAdd + 300 Remove implémentés —
  format natif (Serialize @0x81e9840), broadcast can-see,
  persistance `player_enchants`, restauration à l'entrée,
  tick 1 s (3001 HoT / 3002 DoT signé), chaîne 1921 EffectNextID.
- [x] **P12.2** Debuffs mobs → joueur : `MobCastSpellHitAsync`
  applique `ResultSpellId` (c39) de S_Spell à l'impact — roll de
  chance c40 (nouveau `SpellDef.ResultSpellChance`), jamais sur miss,
  durée c41 (Value). Buffs joueurs : même gate de chance dans le
  chemin sort→vie.
- [x] **P12.3** CEC_State* : commande S_Enchant 2055 décodée
  (comisd var → StateNo* : var1 NoMove/1002, var2 NoAttack/1003,
  var3 NoUseSkill Kungfu/1005, var4 NoUseSkill Magic/1004,
  var5 NoUseItem/1006, var6 SetNonSneak). Verrous agrégés
  `EntryState.StateLocks` + attrs ClientUpdateAttr 1002-1006 +
  gates move/mêlée/cast×3/useitem.
- [x] **P12.4** 372 TeamMemberEnchant émis aux membres locaux à
  chaque add/remove — format natif `[s16 serial][i32 charId][u8 n]
  ×n{s16 effect, s16 counter, s16 level, i32 rest}` (ctor
  NotifyNewMember + Serialize @0x80f9xx). 315 EnchantReset = code
  mort natif (jamais instancié — générateur seul).

### P13 — Talents de classe, titres, succès

- [x] **P13.1** Class talents : 148/149→471 existant + enchant
  permanent AA par point (EffectToLife -2,0x1000 natif) ajouté au
  learn classe-courante.
- [x] **P13.2** Titres/appellations — SetPresentAppellation natif
  (@0x080f2400) + QueryAppellation (@0x082748c0) :
  - 93 [u16] → vérif possession (player_appellation), seq u8++,
    397 [i32] au joueur, persist `player_characters.appellation`,
    396 [char][titre][seq] aux voyants, refresh stats.
  - 94 [u32 cible] → 396 de la cible + 280 attr 0x2c (≈ nb titres).
  - Étape stats « titles » CORRIGÉE : seul l'enchant du titre ACTIF
    compte (S_Title col3) — la branche « cumul à l'obtention »
    d'AddAppellation est du code mort natif (CTitleQuery::Deserialize
    fixe data+0x10=4≠2). Avant : sur-buff de tous les titres possédés.
  - Handlers dispatcher 93/94 retirés (interceptés sur la boucle).
- [x] **P13.3** Succès/achievements — pipeline complet porté
  (CGlobalInfo::SetAchievement mission @0x0808cd70 → appliqué en
  zone) :
  - Parser S_Achievement complet (16 col : NeedNum/Type/GroupKey
    col6*100+col7/Point col8/seuil col9/appellation col10/item col11/
    prestige col12-13/membres ';'/desc).
  - Persistance `player_characters_achievement` (upsert), load au
    login, synchro 480 type 0 (paquets de 315, complétés seuls).
  - Grant : nouvelle ligne success=1 (type4→complet), point si
    Need<2 ; existant success++ point si %Need==0 (def.Point à
    complétion, delta ensuite) ; caps 99999/999999999→texte 0x6ab ;
    silencieux persisté sans notif ; cascade groupes (membres
    présents→grant delta -1, récursif).
  - 480 variable kinds 0-5 corrigé + 3 tests codec + 3 cas golden.
  - Déclencheurs : Event_SetAchievement(/NPC) scènes, transition
    `set_achievement`, kill mob (types 1/2, receveurs exp, dedup
    présent&&point==0), items collection (loot/quest/lifeskill,
    dedup absent-only, SetItemAchievement @0x080aed40).
  - Récompenses seuil col9 : appellation→398+player_appellation,
    mail item (MailIdAllocator+ItemSerialAllocator), prestige exp.
  - Rank : player_achievement_rank upsert total + monster_kill
    (key1) + family_instance (key5) ; collection/class/instance*
    = agrégats natifs non portés (documenté).
  - NON PORTÉ : TownAchievement_Month (itemdata+0x14==5), clés 50/51
    et colonnes lv*_instance_node/family_* du rank (agrégats mission
    natifs).

### P14 — Guilde / famille

- [x] **P14.0** Crash Reforged à l'ouverture du panneau guilde —
  CAUSE : NCID 80 `CNC_CW_ClientQueryFamilyRecruitList` sérialisé
  au format inventé (2×str+u8) au lieu du natif
  `i32+2×str+3×s16 + 2×s16` (@0x8062d40) → lecture hors buffer →
  int3. CORRIGÉ + pagination 20/page + filtre `recruit>0 && !full`.
- [ ] **P14.1** Vérifier en live : ouverture panneau guilde sans
  guilde (prestige 60 réel) + avec guilde.
- [~] **P14.2** Family ops restants : prestige data réel (stub 60),
  family storage, skills, town, emblem, recruitment join.
  - [x] **Prestige réel porté** — natif reversé
    (mission @0x0807cb90/0x0808c1b0/0x0808b5a0,
    world @0x0812bff0, zone @0x084096d0/0x08419ee0) :
    - Table `prestige(player_id, prestige_id, level, exp)` — INSERT
      (level=1, exp=0) au premier gain (AddPrestige), UPDATE ensuite.
    - Ids valides natifs : 1-38, 51-59 (0x33-0x3b), 81-85 (0x51-0x55).
    - `GainPrestigeExp` : exp = clamp(old+delta, 0, S_Parameter 361
      = 75000), level forcé à 1, UPDATE + broadcast.
    - Chaîne : mission → monde `CNC_MW_WorldServerUpdatePrestigeData`
      `[i32 charId][u16 n][n×(s16 id, s16 level, i32 exp)]` → monde
      émet NCID 60 `CNC_CW_ClientUpdatePrestigeData`
      `[u16 n][n×(s16 id, s16 level, i32 exp)]` (Serialize @0x0812c8b0)
      au client + texte `update_prestige <ch> <node> <id> <lv> <exp>`
      par entrée vers la zone (ZW 23) → `Server_TC_UpdatePrestige`
      zone @0x083b9930 → miroir `CCharacter` map+0x6c4.
    - Synchro complète native : `TC_QueryFriend` → `AllPrestigeData`
      (toutes les entrées → 60). Chez nous : NCID 10 (la capture
      `10 -> 60` natif) + retour de chaque grant zone→monde.
    - Grants natifs : récompenses de succès S_Achievement cols 12/13
      (AlignId/AlignExp), battlefield, pay events, CEC
      `AttrGainPrestigeExp <exp> <id> <chance>`, GM
      `/gain_prestige_exp` `/set_prestige_level`.
    - Implémenté : `PrestigeRepository` (LoadAll/GainExp/SetLevel —
      clamp 75000, gate ids natifs), `PrestigePackets.UpdatePrestigeData`
      (60), monde émet 60 + `update_prestige` ZW 23 à la query famille
      (capture `10 -> 60` confirmée mitm_native_entry.log:33-36) et sur
      tout grant ; verbes ZW 636 `gain_prestige`/`set_prestige` ;
      zone : EntryState.Prestige (miroir map+0x6c4) via NCID 23 →
      `HandleWorldTextCommandAsync`, grants achievement/pay-event/CEC
      AttrGainPrestigeExp/GM routés monde ; MissionDb aligné.
      Golden ncid_60 + codec généré marqué aplati (known_incomplete).
      Reste : grants mission-side (RewardReputation) sans notification
      client (pas de lien mission→monde — limite connue), vérification
      live panneau guilde (P14.1), family skills/town/emblem.
  - [x] **Family storage natif** (177/178/179 → 504/505/506/507/508)
    — réécrit sur la table `family_storage` (rows `ItemEntity`
    complètes, id réutilisé, `container_index` 0-2 × loc 0-39) :
    ordre des champs natifs `[index][loc][bag][slot]`, privilège
    `index+11` via `GuildService.CheckPrivilege` (gates niveau 3/5/6,
    leader bypass), cap 40/coffre, validation flags item (S_Item
    cols 69/70 → itemdata+0xf0/+0xf2) ; pushes aux membres famille en
    ligne (miroir `CNE_ZMis_ZoneServerFamilyStorageUpdate::Execute`,
    boucle family+0x94) : 504 delete / 505 put (record item fixe via
    `FamilyStoragePackets` — généré aplati → known_incomplete) / 506
    log ; dialogue service 19 → 507 + 508 par coffre non vide ;
    journal `family_storage_log` (types 0 insert / 1 delete). Tests
    golden codec. Reste dans P14.2 : **skills, town, emblem
    (73/74→384), recruitment join**.
- [ ] **P14.3** Territory/battle — internes 804–808, 820–823 ;
  sortants non émis : 516 `TerritoryInfo`, 547 `TerritoryEnd`,
  562 `FamilyBattleKilledRank`, 565 `R25UpdateFamily`, 566
  `TerritoryFamilyScore` (515/561 émis).

### P15 — Item mall + commandes admin AP/EP

- [x] **P15.1** Diagnostiquer pourquoi les commandes admin AP/EP « ne
  marchent pas » : tracer la commande (AdminTool/GM) → compte →
  paquet item mall (704/705) → solde client.
  - CAUSE 1 : `//gain_aa_point` créditait `tb_user.pvalues` (AP item
    mall) alors que le natif `/gain aa point` = « advanced ability »
    (string @0x0888242a → `lapis::CCharacter::GainAAPoint` @0x840c7f0)
    — pool **aa_point** du perso, pas la monnaie boutique. Il n'y
    avait donc AUCUNE commande pour l'AP item mall. CORRIGÉ :
    `gain_aa_point` crédite `player_characters.aa_point` (cap 0x78 =
    120 natif, émet attr 0x31), nouvelle commande maison
    `gain_ap_point` → `pvalues` (monnaie 1), `gain_ep_point`
    inchangée → `bonus` (monnaie 2).
  - CAUSE 2 : `UserRepository` contenait des méthodes mortes sur un
    schéma inventé (`member_id`, `point_values`, `bonus_points`,
    `currentuser.member_id`…) — colonnes absentes du `tb_user` réel
    (`mid`, `pvalues`, `bonus`). SUPPRIMÉ (aucun appelant).
  - CAUSE 3 : le pool AA n'était pas au bon endroit — `AddAAEffectPoint`
    natif (@0x841ebb0) puise `param_2==0` dans `char+0xa1c` =
    `player_characters.aa_point` et `param_2!=0` dans la ligne
    `player_classlist` de la classe ciblée ; le switch de classe
    (@0x8104xxx PlayerFactory) range `char+0xa1c` dans la classlist de
    l'ancienne classe puis recharge celle de la nouvelle. Notre code
    tapait toujours `player_classlist`. CORRIGÉ :
    `TrySpendCurrentAaPointAsync`/`AddAaPointAsync` →
    `player_characters.aa_point` (+ miroir classlist de la classe
    active), swap `SwapAaPoolOnClassChangeAsync` au ChangeClass,
    émission attr 0x31 au GainAAPoint (level-up de classe + GM).
- [~] **P15.2** Catalogue : vérifier l'envoi liste/promo/achat
  (CNC_CW item mall buy/list) sur les deux clients.
  - AUDIT code OK : NCIDs 15/17/18/23/24/25 → 56/57/58/62/63/64/79
    tous enregistrés (`ItemMallHandlers`), catalogue `ItemMallCatalog`
    chargé depuis `itemmall` (498 entrées sell=1), limites
    `itemmall_limit_amount`, soldes lus frais à l'ouverture (pas de
    push nécessaire — le client renvoie 24/25 à chaque ouverture).
  - Reste : confirmation visuelle client.
- [~] **P15.3** Mail item-mall : grant réel d'items avec
  `ItemSerialAllocator` + notification client.
  - AUDIT code OK : 18 → claim state 1→2 → ZW 24 → zone
    `HandleItemMallDeliveryAsync` (AddItemAsync → serial allocator,
    EmitItemSlotsAsync 274/291 merge-aware, texte 0x89) → ZW 12 →
    monde `item_receipt` + 58 au client. Requeue state→1 si zone
    absente ou livraison échouée.
  - Reste : confirmation visuelle client.

### P16 — No-lag combat & freeze personnage (top priorité ressenti)

- [x] **P16.1** Freeze perso au cast — diff wire natif
  (`zsniff_native_entry`) vs C# (`zsniff_cs_entry_success`), NCID par
  NCID. Ordre des paquets déjà conforme (279/302/288 → aggro →
  302s → 281). **3 écarts corrigés** :
  - `328 ClientModifyAttr` : était `-11` codé en dur unicast →
    désormais `-CostMp` réel (`ISpell::OnBlast` @0x084f0be0 :
    `char+0x70 -= CostMp` puis NotifyClient + BroadCastToCanSee —
    deux émissions), à la phase swing (+600 ms natif), plus à
    l'impact.
  - Déduction MP : était au cast via 280 attr1/0x12 (le client
    appliquait le coût deux fois : UpdateAttr au cast puis ModifyAttr
    au blast) → déduite au blast via `ApplyMpCostAsync`, check MP
    inchangé au cast (410).
  - Release flag[4]=0 : était à swing+vol → recalé au temps `hit` de
    la motion (EventSpellBlasting) — le client se libère ~300 ms
    plus tôt, comme le natif.
  - Sémantique 281 amount clarifiée : porte la valeur de hit pour les
    sorts à effet (`piVar26[4] = local_1c` — captures `2f`=47,
    `37`=55) ; 0 dans la branche `SSpellData+0xc0/c2/c4==0`
    (capture `00000000` pour 0x66c3) — on garde le dmg calculé.
  - **Reste (cosmétique, non bloquant)** : bloc aggro 271/302/280
    émis au swing (~+570 ms) vs natif ~+280 ms (réaction émergente de
    l'AI mob) ; le 444 lock est dans le bloc aggro vs natif ~+120 ms
    après. Vérif live client à faire.
- [x] **P16.2** Multi-aggro vérifié : état de combat par-mob
  (`_mobCombats[handle]`, `mob.InCombat`/`TargetCharId`), aucun gate
  global `st.InCombat` ne bloque N mobs ; leash par-mob déjà en place.
  **Fix** : `TickPassiveAggroAsync` dédupliqué par mob — le roll de
  chance S_Ai se relançait une fois par joueur en ligne par période
  au lieu d'une fois par timer natif (CAI+0x88 ~2 s,
  DoSeekEnemy @0x084d5b40). Reste (non bloquant) : `CanSeekSneak`
  natif — ignoré tant que la furtivité n'est pas implémentée.
- [x] **P16.3** Timing natif swing→impact (sous-plan
  `2026-11-05-swing-timing-natif.md`) : délais fixes 300/600 ms
  remplacés par le temps `hit` de la motion KFM (Motion.ini → ds,
  CKfm::GetMotionTime @0x080e0b00) + vol projectile dist/flySpeed
  (GetBlastTime @0x084f7db0, défaut 40 u/s) — sorts ciblés, casts
  nowhere et mêlée (kfId d'attaque + vol pour armes à distance).
  KFM lettre par nibble genre (GetBlastID @0x080e03f0).
  `MotionTimings` chargé dans GameDataService ; tests
  MotionTimingTests (8 cas). **Reste** : profilage tick ZoneLoop
  sous charge + vérif live ressenti.
- [x] **P16.4** CPU-spin audité sur tous les services : Login
  (`Count==0 → break`, fix 98cc61a), World + ZoneLinkServer (idem),
  Zone (idem), Mission (`ReadAsync<=0 → EndOfStream`), Ticket
  (`ReadExactAsync` jette sur EOF), Gateway démo (`ReceiveAsync>0`).
  Aucune boucle vulnérable au socket-mort-avec-Connected-vrai.
- [x] **P16.5** Gear upgrade : verbes zone natifs portés —
  `repairshop`→350, `identify_shop`→365,
  `enter_exchange_item_service`→366 (corps vides natifs),
  `inlay_shop <npc>`→370 [i32 npc] + op `CInlayShopOp` 0x23
  (TC_* @0x083bf5b0-0x083c6de0). 67 InlayItem → gate op 0x23 +
  talking-range → 371 [s16 0] fidèle au stub natif
  `CItemFactory::InlayItem` (feature NPC morte ; le vrai sertissage
  = UseItemToItemControl 0x2d via InlayService, déjà implémenté).
  Strengthen (119/120) + ops shops déjà portés (P16.5 recoupé).

### P17 — Balayage final NCID-ETAT

- [~] Trier les NON PROUVE restants de la FEATURE-MATRIX : les protos
  internes DBA/ZMis/ZW sont couverts par repos/liens maison
  (documenté §1.6) ; isoler les sortants client réellement manquants
  (384 select emblem, 485/486 container updates,
  289/361/367/546/548 sorts, 516/547/562/565/566 territoire/battle,
  520 roll treasure, 569 look-elf-equip, 614 back-item, 342/343
  repair…) et les porter par lots.
  - [x] **384 select emblem** — verbe `select_family_emblem` →
    paquet vide (TC_SelectFamilyEmblem @0x83bef60).
  - [x] **548 UpdateSpellAttr** — canaux 0-6 (range/radius/cooldown/
    cri-rate/dmg phy+mag) via S_Enchant 2141/2143/2150/2096-2099 ;
    apply+rollback natifs (CEC_AttrSpell*), tests `SpellAttrTests`.
  - [x] **516/532/547/562/565/566 territoire/battle** — cycle de
    guerre complet (P14.3 commit `dc6940b`).
  - [x] **520 roll treasure + 193** — `RollTreasureService`
    (RollitemManager natif : jets dédupliqués %99, need>greed
    décroissant, force-pass 120 s/déco, giveup all-pass) +
    gate `TryRollGate` (team≥2 sur node, loot≠3, seuil RollRule×
    quality, TrainMask∉{5,7,0xc,0xe,0x12}), textes 0xb7a-b7d,
    `IsRolled`/giveup→public, award `RollPickUpTreasure`-like.
    Tests `RollTreasureServiceTests` (10).
  - [x] **289 SpellBlastToLife** — visuel de blast diffusé aux
    observateurs (NotifyBlastToOthers @0x84f7e40 : CanSee lanceur +
    CanSee cible). 3 sites : sort→mob, sort→joueur, sort de mob
    (ce dernier n'émet plus le 288 spéculatif — le natif n'envoie
    que le 289, le lanceur étant un mob sans session).
  - [x] **485/486 family containers** — refresh du conteneur poussé
    à tous les membres de la famille migrés sur le node du hall
    (ZMis FamilyContainerUpdate @0x8315cb0) via 484 prouvé.
  - [x] **342/343 AskRepairGold** — déjà émis (InventoryHandlers).
  - [x] **361 SpellLoopCastStop / 569 LookElfPetEquip** — aucun
    émetteur dans tout le corpus serveur : paquets morts natifs.
  - [ ] **367 ResetSpell** — différé : nécessite le parse
    S_SpellMaster (gate `CanLearn`) + le modèle de slots par classe
    (char+0x684). Spec : `/reset_skill` → ResetSkill @0x80fe840 =
    ResetAllSpellMaster (order=0/level=1 en DB) + slots type-2>100→-1
    + 367 [i32 CP][vec masters][vec<vec<i32>> slots].
  - [ ] **614 BackItem** — différé : lié à `CItemShift::Execute`
    (reroll d'item via item mall — feature entière non portée).
  - [ ] **546 SpellFlyToLocation** — interpolation 4 waypoints non
    prouvée (P10).

### P18 — 263 DisplayChar : inspect/WhoIs (P17 → résolu)

- [x] **263 ClientDisplayChar** — `CCharacter::OnDisplayTo`
  @0x84187e0 n'est PAS le chemin sight : le sight passe par
  `CNode::OnDisplayEnvironment` → `ILifeEntity::OnSee` →
  `CCharacter::OnSeen` → le 265 SeeChar **caché** (char+0x7d8,
  rebuilt par MoveSelf) + 326 — notre flux 265 était donc déjà
  fidèle. `OnDisplayTo` (vtable +0x30) n'a qu'un seul appelant réel :
  `CNE_CZ_ZoneServerWhoIs::Execute` @0x82a1340 (NCID 2 —
  `[i32 charId][u8 flag]`, flag → param_2). Implémenté :
  `ClientDisplayCharCodec` (record bitmask complet : nom, blob
  apparence, HP/MP i64/i32 chaînés, 2 sets d'équipement map
  char+0x4fc, famille id+nom char+0x5c/0x640, titre +0x6fc,
  monture +0x7f0/+0x7fc, transform +0x28c, mix-elf +0xbf8) +
  `HandleWhoIsAsync` (CanSee → `st.Sent`, 410 en échec comme le
  natif) + 301 EnchantUpdate si statuses. 3 tests wire.
  - 323 `ClientDisplayCharSimple` — aucun émetteur natif (même
    constat que 361/569) — non implémenté.
  - Morts natifs (aucun émetteur, corpus complet) : 294, 315, 319,
    325, 354, 360, 374, 390, 417, 419, 420, 422, 423, 437, 443, 447,
    569 — à annoter dans NCID-ETAT plutôt qu'implémenter.

### P18.1 — 276 ClientChangeEquip : delta d'équipement (résolu)

- [x] **276 ClientChangeEquip** — le vrai mécanisme natif de refresh
  d'équipement côté observateurs : `CCharacter::NotifyChangeEquipment`
  @0x843df10, appelé par `Equip`/`UnEquip`/`EquipForSwitch`/
  `UnEquipForSwitch`/`EquipNoCheck`/`DropItem`, broadcast
  `CanSee(soi exclu)`. Layout (Serialize @0x843exxx) :
  `[i32 char][u16 loc][u16 tid(0 si vidé)][s16 cont][i8 rev char+0x4dc]
  [u8 flags(0xff si vidé)][str][u24 mask chaîné][34B tail]` — corps
  identique au record 571 (`WriteEquipViewItemBody` factorisé).
- [x] Correction de sémantique : le port émettait 282+283 à TOUTES les
  sessions sur chaque swap d'équipement — or le natif n'émet 282/283
  que sur `UpdateEquipment(8)`. Remplacé par : 276 par loc aux
  observateurs (chemins 0→1 / 1→0 / 1→1 / drop-equip / fast-dress
  par loc) + `RefreshEQsAttr` → 280 à soi (`EmitSelfEquipRefreshAsync`).
  `BroadcastEquipmentAsync` (282/283) conservé pour UpdateEquipment(8).
- 3 tests wire `ClientChangeEquipCodecTests` (mask, branche vide,
  prefixe).

### P18.2 — balayage sortants zone (résolu / documenté)

- [x] **270 ClientResetCoord** `[f32 x][f32 z]` — `MoveSelf`
  @0x8111cb0 : refus de mouvement (CanMove==0 / speed==0 → log
  "Can't Move!!" ou CheckLineOfMove) → snap-back de la position
  autoritaire au client + 326 broadcast. Branché sur notre
  NoMove-lock. `CheckLineOfMove` différé : exige la grille de
  collision .csm non parsée.
- [x] **273 ClientRestore** `[u32 charId]` — broadcast CanSee quand
  le perso quitte l'état d'instance (BattleRoyaleFactory::Restore,
  VersusInstanceFactory::Restore, EvaluateFactory::Restore). Émis
  aux pairs du node avant la remigration dans LeaveBR/LeaveVersus.
- [x] **392 ClientTeamMemberPosition** `[i32 char][s16 node][f32 x]
  [f32 z]` — `NotifyTeamMemberPos` @0x80fc7c0 (appelé par
  NotifyTeamData ← OnMigrationDone @0x843fba0 et
  RecvWorldTeamJoinTeam @0x81135e0) : un paquet par coéquipier
  migré, au destinataire uniquement. Hooks : MigrationDone +
  TeamInviteAnswer via delegate TeamMemberPositions.
- [ ] **362 ClientQueryNpc** — `TC_QueryNpc` @0x83d2d40 = verbe de
  commande texte qui écho simplement `[i32 arg]` ; le verbe exact
  vit dans la table FAILED (non greppable) — documenté, non
  implémenté (sémantique = écho, aucun usage client trouvé).
- [ ] **380 ClientMobTransform** `[i32 handle][s16 transformId]` —
  `CEC_MobTransform::Execute` (commande d'enchant
  "MobTransform <number>") : transforme un mob via la table
  CZoneServer+0x44. **Aucun S_Enchant n'utilise la commande** dans
  les données → feature morte côté données, documentée.
- Morts natifs confirmés (aucun émetteur hors générateur) : 294,
  319, 325, 374, 447.

