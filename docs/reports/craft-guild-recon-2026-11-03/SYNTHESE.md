# SYNTHÈSE — Repérage CRAFT + GUILDE (2026-11-03/04)

> 20 rapports détaillés dans ce dossier. Ce fichier = la matrice croisée
> métier × données × natif × serveur × client × visuel + le plan d'action
> de reconstruction. Chaque affirmation détaillée est citée dans le
> rapport indiqué — **lire le rapport pour les preuves `fichier:ligne` /
> `binaire @ 0x<addr>`**.

## 0. Vocabulaire pivot (indispensable)

| Concept joueur | Nom code | Où |
|---|---|---|
| Guilde | **Family** | `lapis::CFamily*`, `c_family*.ini`, `family*.layout`, `FamilyHandlers.cs` |
| Ville de guilde (VDG) | **FamilyTown / FamilyInstance** | `CFamilyTown*`, `c_buliding.ini`, `familybuilding*.layout`, maps S501/S502 |
| Plans de récolte/craft | **Work** | `CWorkManager`, `c_familycollect/combine`, `worklistwnd`, NCID 170-172 |
| Craft racial | **LifeSkill** | `c_lifeskill.ini` (595 recettes, 5 races), NCID 213/214 |
| Fortification | **Strengthen** | `CStrengthenOp`, `strengthenwnd*`, NCID 119/120, `S_FamilyCombine` id 1-20 |
| Fusion (commission) | **Combine** (catégorie 6) | `combinewnd`, `CCombineOp` op 0x27 |
| Boîte mystique | **Cube** (op 0x52, fenêtre 0x1c) | `magiccube*`, recettes `S_ItemChest` |
| Magic Box (étoiles/affixes) | **MagicBox** (op 0x58, fenêtre 0x1f) | `magicboxwnd`, `c_itemcombo` (5 413 combos) |
| NPCs de ville | items-PNJ 35001-35080 | `t_item`, `c_npc`, `c_store` |
| Bâtiments | 12 types × nv 3-12 | `c_buliding.ini`, `4PHouse.ini`, `decorate.ini` |

## 1. Matrice d'état par système

États : ✅ fonctionnel prouvé · ◐ partiel · ❌ absent · ⛔ jamais câblé côté client.

| Système | Métier (K) | Données (D) | Natif (R) | Serveur C# (S) | Client Godot (G) | Visuel de réf. |
|---|---|---|---|---|---|---|
| LifeSkill racial (5 races) | K1 complet | `c_lifeskill` 595 + parsers | R2 `0x08260e40` | ✅ `ZoneEntryFlow.LifeSkill.cs` (213/214/543/544, 6 ops, cast 5 s) | ❌ aucune fenêtre ; 213/214 jamais émis ; `lifeskillitem`/`engineeringstudy`/`jewelrystudy`/`jobglyph` ABS | layouts ABS ; aucun ref-shot |
| Work orders plans (mine/ferme/autel) | K3 complet | `c_familycollect` 478 + seeds stores | R2 `PlayerWork @0x08270ef0` | ✅ implémenté — **MAIS durée plafonnée 10 s DEV** vs 20-30 min natif | ◐ `CraftWindow` custom (wire 170/171 + 496-500 OK) ; `worklistwnd`/`workresultwnd`/`collectwnd` jamais instanciés | layouts IMPL/ABS ; `item hover plan mine` ref-shot |
| Combine bâtiments (fonderie/labo/mare) | K4 §4 | `c_familycombine` 3 178 recettes, cat 3/4/6/7 | R2 `DoCombineItem @0x080b0410` | ✅ `CombineStation.cs` (cat 6, 4 mats, cast 0x28) | ❌ `combinewnd` ABS, bouton sac → notice | `FusionPanel Craft` ref-shot (Magic Pool) |
| Fortification (Strengthen +14) | K4 §1 | recettes `S_FamilyCombine` 1-20, scrolls 40002-40019 | R2 `DoStrengthenItem @0x080a2b80` (destruction, pity, pierre 8002) | ✅ `Strengthen.cs` fidèle — sauf combos +6/8/10/12/14 non modélisés | ❌ stub « Enclume ouverte » ; 119/120 codecs orphelins ; 432-437 non décodés | `strengthenwnd_na` = cible ; aucun ref-shot |
| Enchants item (scrolls 56/79) | K4 §3 | `c_enchant` 18 487, `t_enchant` | R2 EnchantUpdate/Cancel | ✅ `EnchantService` + `InlayService` (drag scroll→item) ; KB-01 corrigé | ◐ tooltip enchant OK ; application non testée ; kind 59/84 absents | tooltips ref-shots |
| Fusion boîte mystique (Cube) | K4 §5a | `c_itemchest` 1 538 recettes | R2 op 0x52 | ✅ `Cube.cs`+`CubeService` (recettes, lucky 8007) | ❌ 232 orphelin ; `magiccube*` tous ABS | `magiccube00`/`newmagiccubewnd` ABS ; aucun ref-shot |
| MagicBox étoiles/affixes | K4 §5b | `c_itemcombo` 5 413 (1 780 utilisables) | R2 `DoMagicBox @0x080a8b90` | ✅ `MagicBox.cs` complet (params 609-653, cat. 8022) | ❌ binds 612/613 sans UI ; 257 orphelin | `magicboxwnd` PART ; aucun ref-shot |
| Guilde (création→membres→droits) | K2 §1-2 | `GuildFunction.ini`, modèle 14 privilèges | R1 `FamilyCreate @0x080f3260` (niv 20, 50 k) | ✅ quasi complet (création, 23 sous-ops monde 9, privilèges, emblem, prestige) | ◐ `GuildWindow` custom + `FamilyInfoWnd_NA` ; 29/40 layouts family ABS | `FindGuild.png`, layouts ABS |
| VDG (achat, entrée, instance) | K2 §3 | instance node type 4, S501/S502 | R1 `FamilyInstanceCreate @0x080f0790` (chef, guilde≥3, 500 k/10 M) | ✅ complet (gates, 49 villes cap, cross-zone visit) | ❌ aucune entrée VDG jouable (`familytownlistwnd` ABS, NCIDs 173-176 émis ? oui mais pas d'UI) | `shot_0062` Town List ; layouts ABS |
| Bâtiments VDG | K2 §4 | `c_buliding` 136 (12 types), `4PHouse`/`decorate` 232 | R1 `CFamilyItem`+0x86, loc 5×10 | ✅ containers put/get/repair/refresh ; ❌ lifecycle (décroissance/upgrade/due_date) hors GM | ❌ `familybuilding{,_forest,_mountain}` ABS | layouts ABS ; aucun ref-shot |
| NPCs VDG (vendeurs/crafteurs) | K2 §5 | `c_npc`→`c_store`→items : chaîne complète | R1 `InitFamilyNpc`/`CTalkOp` | ◐ spawns `InitFamilyNpc` côté natif — état C# à vérifier ; shops génériques OK | ❌ pas de VDG → pas de NPC VDG rendus | `npc talk*.png` ; layouts shop OK |
| Stockage guilde | K2 §10 | 3 coffres ×40, lv 3/5/6 | R1 op 0x44 | ✅ atomique + logs + priv | ◐ `FamilyStorageWindow` fonctionnelle custom | `familystorage*` layouts |
| Reliques/statues | K2 §5d | items 23817-23842, `statues_scenes` | R1 `FamilyStatue @0x08257f10` (500 k, guilde≥4) | ✅ cycle complet prouvé headless (pose/persist/retrait 2026-11-06) | ✅ menu membre chef (Erect Statue) | aucune capture |
| Shops NPC | K5 §7/11 | `c_store` 659 boutiques, `S_Store` serveur | R2 `BuyItems @0x080aca40`, devises ×6 | ✅ buy/sell/buyback (deque 10) | ◐ `ShopWindow` custom (colonnes perdues vs `shopitem`) | `shopwnd`/`shopitem` ; ref-shot marchand |
| Hôtel des ventes | K5 §6 | `active_auctions`/`auction_history` | — | ✅ 114-118 (dépôt 5 % + commission 8 %, 48 h, mail) | ◐ `AuctionWindow` custom | `Auction*.png` (colonnes) |
| Étals joueurs | K5 §6 | zones 600-609 | R2 `VendorSellItem @0x081145e0` | ✅ `ZoneEntryFlow.Vending.cs` | ◐ `VendingWindow` custom | `stallui`/`stallbuy` ABS |
| Trade J↔J | — | `trade_items` | — | ✅ `TradeHandlers` + settle atomique | ◐ `TradeWindow` custom | `tradewnd` |
| Item mall / Loyalty | K5 §8 | `c_itemmall` 7 461 + `t_itemmall` | — | ◐ flow CW complet ; `ItemMallService` AP/EP non câblé | ◐ `ItemMallWindow` ? | `ItemMallPanel*.png` (3 colonnes + stock hebdo) |
| Fame shop / special coin | K5 §2 | `c_store` + monnaies natives (devise 2=prestige, 0xb=colisée…) | R2 devises `itemData+0x78` | ✅ débit typé (5/0xa/0xb/0xc-0x17/0x13/0x14) + refresh client ; lover (4) refusé = gap G32 | ◐ `specialcoinwnd` affichage seul (soldes à relier) | `specialcoinui*` |
| Outils récolte (pioche/gant/boule) | K3 §4 | ids 36819-37123 blancs, verts, bleus mall ; durabilité 180/540/1440 | R2 outils CChanceItem (kinds 8003-8008) | ◐ use-item générique ; `UseAreaNodes` S501/502 | ◐ use-item OK ; pas de cast-récolte dédié VDG | tooltips outils |
| Accélérateurs (−95 %, −50 %, +8 %, +2 rendement) | K3 | 40414/15 (−95 %), 43315/16 (−50 %), 40078/96 (+8 %), 22418 fert. | R2 booster slot NCID 170 | ◐ slot booster lu ; substitution +30 documentée | ❌ pas d'UI slot booster | — |
| Collection (archive) | K4/D1 | `c_collection` 7 970 + `S_CollectionPoint` | R2 Query/Save/Get | ✅ complet + awards | ◐ `ArchiveWindow` custom | `collectionwnd` IMPL |
| EquipTrain / Mana Infusion | K4 §5c | `c_equiptrain` 100 | — | ✅ `EquipTrainService` (param 496) | ❌ 230 orphelin | `growthequipment` ABS |
| Inlay gemmes | K4 §6 | gemmes kind 45, sockets | R2 case 0x2d | ✅ `InlayService` (NCID 19) | ❌ aucune UI inlay | `gembreaker` ABS |
| Quêtes de guilde | K2 §7 | `c_mission` (non audité) | — | ◐ via mission service | ◐ quêtes génériques OK | — |
| Family Battle / territoire | K2 §9 | `c_territory` | R1 TerritoryJoin | ◐ join/close/leave OK ; matching/rewards mission-side | ❌ `familybattle*` ABS | layouts ABS |

## 2. Ce qui est déjà solide (à ne pas refaire)

- **Serveur C#** : la quasi-totalité des opérations craft/guilde est implémentée
  et testée (work, combine, lifeskill, strengthen, cube, magicbox, collection,
  inlay, equiptrain ; guilde complète : création, ville, bâtiments, stockage,
  statue, dons, emblème ; commerce : vendor, auction, stall, trade, item mall).
  → Le travail restant côté serveur est des *trous* précis, pas des systèmes.
- **Données** : toutes les tables utiles existent en fr-full (schémas décodés
  dans D1/D2/D3) ; la chaîne NPC→boutique→recette est entièrement mappée.
- **Natif** : logique zone entièrement documentée avec adresses (R1/R2) ;
  dispatch client extrait : 621 handlers S→C + 210 senders C→S (R3, fichiers
  /tmp/genmap.txt, sender_callers.txt — à sauvegarder dans docs/ !).
- **Layouts** : les 415 .layout du corpus contiennent TOUTE la géométrie des
  panels à reconstruire (D4/D5) — aucune invention visuelle nécessaire.
- **Tooltips client** : `ItemTooltip.cs` est déjà fidèle (qualité, +N, enchants,
  level requis) — manque seulement matériaux colorés et champs magicbox.

## 3. Verrous structurels (à lever en premier)

1. **`CeguiBuilder` ne sait pas faire un slot item interactif**
   (`SlotImageItem` → TextureRect statique, G1). Toutes les fenêtres craft
   exigent des slots drag&drop + tooltip + compteur. **C'est LE déblocage.**
2. **NCIDs écrits mais jamais émis** (G2) : 119/120 strengthen, 213/214
   lifeskill, 232 cube, 257 magicbox, 230 equiptrain, 173-176 VDG, 180 repair,
   239/240. Le serveur répond déjà — il manque juste les UIs qui les émettent.
3. **Plafond DEV 10 s** sur les work orders (`ZoneEntryFlow.Work.cs:45`)
   — à remettre à la durée native (20-30 min) pour tout test réel.
4. **Aucune entrée jouable vers la VDG** : pas de `familytownlistwnd`, pas de
   téléport fonctionnel côté client, pas de map instanciée chargée (S501/S502
   existent dans `UseAreaNodes`, c_node n'a pas de node VDG — instanciation
   dynamique côté serveur à porter).
5. **Visuel** : pas de `ref-shots/` pour strengthen / family building /
   cube / study — MAIS `web-shots/` (ce dossier) apporte des captures
   natives issues des wikis : `Guildinterface` (fenêtre guilde complète
   + privilèges), `Assign` (assignation ouvrier), `Molding` (Foundry avec
   matériaux possédé/requis), `Shoprecipe`, `Racial_crafting_button`,
   `Guildtown`/`EdenEternalForet_Guilde_Ville`/`Montagne` (VDG 3D),
   `Item_mall`, `Eden_Warehouse`, shops PNJ. `knowledge/raw/` = présent
   (13 265 HTML, images distantes uniquement — corrige le GAP signalé
   par K1/K2) ; voir `web-shots/MANIFEST.md`.

## 4. Plan d'action ordonné

### Phase A — Fondations UI (débloque tout)
- **A1** Étendre `CeguiBuilder`/`CeguiToGodot` : widget `ItemSlot` interactif
  (icône atlas + compteur + tooltip `ItemTooltip.Bind` + drag&drop payload
  `StorageItemCell`) — s'inspirer de `StorageItemCell`/`TradeCell`.
- **A2** Widgets manquants : `MultiColumnList` (colonnes triables — requis par
  shopitem/auction/strengthen), `TabControl` à pages-layout séparées
  (familywnd/auctionwnd/itemmall injectent leurs onglets au runtime),
  `MpProgressBar`, `Listbox2`, `Tree`, `RedCheckbox`, `Zframe`, pagination
  `PageItems` (magiccube01).
- **A3** Icônes métier : mapper `UseEnchantIds`/codes items → `ui/itemicon/`
  (pipeline `AssetCatalog` déjà OK) ; atlas imagesets craft (MagicCube,
  JobGlyph, newhand) via `CeguiImageset`.
- **A4** Compléter `ItemRecord`/`InventorySlot` client : conserver enchant ids,
  combo/magicbox, embedded sockets (aujourd'hui jetés — G2) sinon les panels
  ne peuvent rien afficher.

### Phase B — Fenêtres « fil chaud » (wire déjà complet)
Ordre croissant de complexité, chacune = [IMPLEMENTED→TESTED→VISUALLY_VALIDATED] :
- **B1** `shopwnd`+`shopitem` fidèle (MultiColumnList, prix or/argent,
  quantités) — valide A1-A2 sur le cas le plus simple.
- **B2** `worklistwnd`+`workresultwnd` → remplacer `CraftWindow` custom par le
  layout natif (wire 170/171/496-500 déjà complet).
- **B3** `collectwnd` + `combinewnd` (commissions récolte/fusion : UseItem +
  Charm/booster + mats + coût + durée + produit possible) — émet 170 op
  collect/combine.
- **B4** `strengthenwnd_na` : slots équipement/parchemin/虚寶, MList paliers,
  grand % ChanceText, WarningText destruction+pity ; émet 119/120, décode
  432-437 ; refaire `FeatureDialogWindow` strengthen.
- **B5** LifeSkillWindow : liste `lifeskillitem` (ExpBar, ForgetBtn, LvUpBtn,
  FullExpText) + `engineeringstudy`/`jewelrystudy` par race + `jobglyph_na`
  roue 15-20 slots ; émet 213/214, décode 543/544.
- **B6** `familytownlistwnd` + entrée VDG (Town List : filtres
  Farm/Mine/Altar/AlchemyLab/Foundry/MagicPool, colonnes Leader/Fame/Member/
  Tax) — requis pour rendre la VDG accessible.

### Phase C — Guilde complète
- **C1** `familywnd`(+onglets : info, membres, `familyprivilege`,
  `familyorderwnd` 7 rangs, `familyjobnamedlg`, `familydonatedlg`,
  `familybulletindlg`, `familysetprivilegedlg`).
- **C2** `familybuilding{,_forest,_mountain}` : grille placement bâtiments
  (slots Area0-4 + relique Building5 vs plein-écran 48 emplacements selon
  génération), achat/upgrade/réparation.
- **C3** `familystoragewnd`+tabs natifs + log ; `familyfinding*`/`familyappwnd`
  (candidatures) ; `cnfamilyemblem` → vérifier présence éditeur blason EDENFR.
- **C4** `familybattle*` (7 layouts — bracket 31, rank, schedule) — priorité
  basse, matching mission-side.

### Phase D — Cube/MagicBox/études/commerce avancé
- **D1** `magiccube00` (Kradia 4×4 + preview + catalyseur + % + recettes
  `magiccube01/02`) ou `newmagiccubewnd` — trancher la génération EDENFR ;
  émet 232, décode 572.
- **D2** `magicboxwnd` (transmutation/scouring, étoiles, Power01-03) ; émet 257.
- **D3** `specialcoinui*` (fame shop monnaies dédiées) — **dépend du serveur**
  (devises non implémentées, §5-S1).
- **D4** Item mall fidèle (3 colonnes + Hot Items + stock hebdo + NT).
- **D5** `tradewnd`, `auctionwnd/buywnd/sellwnd` (colonnes MCL),
  `stallui/stallbuy`, `hotelwnd/hotelstoragewnd`, `paysystemwnd` (dons
  factions — le seul ref-shot commerce FR !).
- **D6** Tooltips : matériaux possédé/requis colorés (le `$13$` existe),
  work level, magicbox combos, champs `ZoneItemUse` Target*.

### Phase E — Serveur : trous à combler
- **E1** ✅ Retirer le plafond DEV 10 s → durées natives `c_familycollect`
  (20/25/30 min) + flag DEV optionnel.
- **E2** ✅ (code, live +14 à valider) Strengthen : `AddComboStrengthen`
  paliers 6/8/10/12/14 porté (`StrengthenComboService`, 11 tests).
  Safety stones kind 8002 : non modélisé (items c_itemmall sont kind
  44/49 — slot chance natif exige 0x1f42, absent des données).
- **E3** ✅ (code, live à valider) Fame shop : devises natives —
  priceType col30 routé (`ShopCurrencyPort`) : 5=lifelong, 0xa=trésor
  famille, 0xb=colisée, 0xc-0x17=special coins, 0x13=war, 0x14=alpaca
  ; lover 4 refusé (pas de stockage, gap G32) ; prestige 2/item-34928
  6/crystal : aucun item vendu EDENFR → refus natif-fidèle. Plan
  `docs/temp-plans/2026-11-05-e3-shop-currencies.md`.
- **E4** ✅ (code, live à valider) Bâtiments : lifecycle complet porté —
  résolution station par NPC (net-id → 0x2a → group base → 0x29),
  due_date contrats (0xc56), gates 0x251/0x392/0xa09, taxe de ville
  (op+0x18), usure start (non-instantané) / fin (instantané),
  EXP bâtiment `End*` (même sur échec), level-up chaîne S_Buliding +
  `CheckBuildingLevelUp`, `GainExp` famille natif (col11, remplace le
  placeholder). 28 tests. Plan
  `docs/temp-plans/2026-11-05-e4-building-lifecycle.md`.
- **E5** ✅ (code, live à valider) : succès de famille portés depuis
  `CFamily::UpdataAchievement` (mission @0x081be030, appelé par
  `SetAchievement`) — `ApplyFamilyAchievementAsync` monde (upsert
  `family_achievement` clé GroupKey, caps 999999999, compteurs
  dual-key Type+GroupKey, `spell_point` cap 100 gaté `elfking_tid>0`,
  `family_log` type 4, `family_achievement_rank` synchrone), ZW
  `family_achievement` zone→monde, broadcast **NCID 67** flag=0 +
  push 68 single-entry aux membres, synchro login flag=3. Codec
  variable prouvé (monde @0x080cdfb0 — flag=1 = i32 0, flag=3 =
  charId/channel à 0) côté serveur ET client (`WorldClientFamily
  AchievementUpdate` réécrit — le généré aplati lisait 33 o fixes et
  aurait throw sur chaque push). Bug msgboard corrigé : NCID 51 émet
  `fam.Messages` (`family_msgboard` vivante) — `family_schedule`
  retirée (table morte, zéro réf binaire). `DisbandAsync` supprime
  les 10 tables filles comme `CFamily::Disband`. Ops monde vérifiées
  couvertes (9=emblème, 0xc/0xd→0x0f msgboard, 0x10→0x13 memo,
  0x14=achievement, 0x22/0x23 recruit). `FixEquipByFamily` audité —
  gate privilège client-side natif, textes 0x621/0x8f0/0x5fd déjà
  émis. 11 tests + suite 1773/1774. Plan
  `docs/temp-plans/2026-11-05-e5-family-achievement.md`.
- **E6** ✅ (code, live à valider) : `auction_history` = table maison
  morte — le natif n'en a pas (déjà archivée migration 016).
  `BuySpell` 52 neutralisé (`Execute` natif vide) ; `ItemMallService`
  zone retiré (code mort — vrai item mall en monde `tb_user`).
  Filtres `AuctionBuyQuery` portés (`QueryAuctionBuy` mission
  @0x08160710) : `ItemDef.ItemType` (col33→+0x84) + `ClassMask`
  (col18→+0x48 i64), colonnes dénorm `active_auctions` (migration
  026 : item_type/level/class/quality_filter=1<<quality/item_name),
  tri natif level/quality/duedate/price asc+desc, pagination 20,
  adaptation wire « 0/vide = pas de filtre » documentée. 10 tests +
  suite verte. Plan `docs/temp-plans/2026-11-05-e6-auction-filters.md`.
- **E7** ✅ `RandItemCombo` porté (zone @0x080a5480) :
  `ItemComboRollService` + hook `RollCombineProduct` (kind 1..0x22≠0x10)
  ; 22 tests verts.

### Phase F — VDG monde réel
- **F1** ✅ Instanciation map VDG (S501/S502) : `family.house_open` →
  migration dans le hall (`ZoneEntryFlow.FamilyInstance.cs`,
  `EnsureFamilyInstanceNodes`).
- **F2** ✅ Spawn `InitFamilyNpc` : `InjectHallNpc` rejoue les NPCs du
  conteneur 0 (tid = proto NPC), housekeeper auto à loc+1 (F5).
- **F3** ✅ Zones de récolte : `AttrGainCollectItem` + restrictions de
  zone VDG (pioche/gant/boule).
- **F4** ✅ Statues : `FamilyStatue` 161 serveur complet (pose/retrait,
  gates natifs, CSV `BornStatue`, EntityId 54901-5 côté client) —
  décoratif pur (aucun buff natif). **Test live headless PROUVÉ**
  (2026-11-06) : pose pos=1 → trésorerie -500k, `statues_scenes`
  persisté (snapshot équipements), entité 298 eid=54901, textes
  natifs 0xb4e/0x38a ; réentrée → statue ré-injectée ; retrait pos=0 →
  nom vidé + 269 + 0x38b. UI client ajoutée (menu membre chef,
  « Erect Statue »). **Équipements du clone** (2026-11-06) : packing
  natif `NotifyStatue` reproduit — paires u16 dans les champs
  optionnels du 298 (`Hp`=tête|torse … `Unk48`=distance|action),
  décodées client → `CharVisual.ApplyEquipment` ; arme attachée
  prouvée (mesh-dump `w40101` sur statue équipée). Reste : gel
  action/frame (Unk44/Unk5C) non implémenté.
- **F5** ✅ Pose de bâtiment fidèle (2026-11-05) :
  `ItemDef.FamilyBuildingTid` (col35 kind 0x29), walk `PrevTid→next`
  jusqu'au niveau famille (types {1,2,10}), tid S_Buliding persisté,
  durabilité/niveau natifs, housekeeper auto (due_date incluse),
  position NPC via scène+4PHouse (0,0,0 pour le bâtiment), gate holy
  corrigé (subtype `EquipLoc`+0x10==0x16), refresh 484 aux membres du
  hall. 18 tests verts, suite 1778/1779. Plan
  `docs/temp-plans/2026-11-05-f5-building-put.md`. **Test live headless
  PROUVÉ** (2026-11-05) : housekeeper 53007 injecté, marche réelle
  (chemin BFS sur S501.smp — mur nodemap), dialog 285 option 4521,
  NCID 130 → 484, contrat 35081 consommé, `tid=101` persisté +
  housekeeper auto 53008 à loc 1, les deux NPCs respawnent à la
  réentrée. **Rendu 3D client validé en capture** (2026-11-05) :
  `HouseTable` (c_buliding+4PHouse) + `FamilyHallBuildings` spawnent
  `house/model/*.nif` aux ancres scène+offsets ; le manoir h001 rendu
  à son ancre réelle (capture `artifacts/shots/20261105-122714-
  S501-halltest.png`). Reste : validation en session réelle (reporté G).

### Phase G — Validation globale
- `tools/capture.sh` par fenêtre (clés `--hud-open` à ajouter :
  strengthen/combine/magicbox/lifeskill/familytownlist/familybuilding) ;
  comparaison aux layouts (pas aux ref-shots — absents) + aux captures
  dispo (FusionPanel, FindGuild, Town List, paysystem, ItemMall, Auction).
- Tests wire : `wire_diff.py` sur les flux complets craft/guilde vs natif.

## 5. Backlog des questions ouvertes (consolidé des GAPs)

1. Durée/expiration des glyphes 14 j : effacement equip ou re-craft ? (K1)
2. `life_skill` nibble écrit où ? (K1) · items kind 8002/0x1f41-48 présents
   dans `c_itemmall` mais inexistants dans `c_item` — activer ou non ? (K4/D3)
3. `c_familycombine` catégories exactes (3/4/6/7 confirmées, autres ?) (D2)
4. `c_transport` row 19 VDG ? entrée VDG par quel canal exact ? (D2)
5. Double modèle fortification : `EquipmentEnchantService` (+20, downgrade)
   vs natif (+14, destruction) — garder le natif, archiver l'autre ? (K4/S1)
6. Produits autel 58901+/56711+/56821+ : ids `c_monster` (K3), absents de
   `c_item` — normal (ce sont des spawns de mobs, pas des items) (D3)
7. Éditeur de blason EDENFR : `cnfamilyemblem` existe, variante `_na` absente
   — emblème upload possible ou non sur EDENFR ? (D5)
8. `hotelstoragewnd` quasi vide → grille au runtime, spec à retrouver en
   désasm client (R3 méthode : genmap + senders fournis) (D5/R3)
9. `/tmp/genmap.txt`, `sender_callers.txt`, `sender_ctor.txt` (R3) : **à
   copier dans `docs/reports/` avant purge /tmp** — map NCID→handler client
   inestimable pour la phase B/C/D.
10. Prix PO des 36 items fame-shop et des plans par rareté : extraire
    `c_store` (K5) — script d'extraction `IniTable` à écrire.
11. `CombineStation` op10 vs stations craft VDG : le même NCID 170 sert les
    deux — routage serveur confirmé, à vérifier côté client (R2/S1).
12. `knowledge/raw/` absent : régénérer si des détails EN manquent encore.
