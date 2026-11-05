# R2 — Recon natif `zone` : work/récolte, lifeskill, strengthen, enchant, itemcombo, magicbox, shop, collection

Binaire : `re/ghidra/zone/decompiled.c` (zone serveur, symboles `lapis::*`, non-strippé).
Convention : `zone @ 0x<addr>` + symbole qualifié. `[VERIFIED]` = lu dans le décompilé,
`[HYPOTHESIS]` = déduction raisonnable, `[GAP]` = non confirmé.

Offsets CCharacter récurrents : `+0x34` charId, `+0x1c` CNode, `+0x3c` anti-bot flags,
`+0x470` niveau, `+0x4aa` race (high nibble), `+0x4b0` or, `+0x4bc` cristaux,
`+0x670` IOperation courante, `+0x4e4/0x4e8` map<loc,IItemContainer*>,
`+0xb20..0xb48` 11 slots CWork, `+0xb84` hashtable lifeskill, `+0xd80` compteur
d'échecs strengthen ("TempStrengthenFailTime"), `+0x564` collection, `+0x7ec` stall.
Garde commune à tous les handlers : sender vtable+8 == 7 (player) + pas de flag
`iVar+0x10` (packet déjà traité).

## 1. WORK (récolte / plans) [VERIFIED]

### Entrée réseau
- `zone @ 0x08270ef0` `CNE_CZ_ZoneServerPlayerWork::Execute` : exige op courante
  `+0x670` type (vtable+0x10) : `0x27` → `CItemFactory::CombineItem` (fusion NPC) ;
  `0x3d..0x42` → `GetShopType` (`zone @ 0x08270dc0`) mappe `0x3d→0xc … 0x42→0x11`
  puis `CWorkManager::ToWork`. `CWorkOperation::IsCasting`
  (`zone @ 0x08571240`) refuse si déjà en cast (FindEnchant actif).
- `zone @ 0x08270ef0` types d'op : `0xc,0xd,0xe` = collect (récolte),
  `0xf,0x10,0x11` = combine (plans de production NPC).
- `zone @ 0x082740b0` `CNE_CZ_ZoneServerPlayerWorkResultOK::Execute` →
  `CWorkManager::WorkResultOK(char, slot)`. Flag anti-bot `char[0x3c]&1` → log
  "use illegal bot!" et drop. Idem `ElfWorkResultOK` (`zone @ 0x0824b560`) :
  **ne fait qu'alerter GM** "illegal ElfWorkResultOK" — honeypot.
- `zone @ 0x082744f0` `PlayerWorkCheckData::Execute` → `WorkCheckData`
  (`zone @ 0x08169350`) : recalcule les 11 slots CWork, items/exp via
  `CWorkEventCmd`, renvoie `CNC_CZ_ClientPlayerWorkUpdate`.

### Mécanique
- **Slots** : 11 `CWork*` à `char+0xb20+slot*4` ; `CWork+0x10` type, `+0x14`
  workId, `+0x30` flag exec-event, `+0x36` en-cours, `+0x38/+0x5c/+0x80/+0xa4`
  = **4 slots résultat SWorkItem** {itemId,count,~10 shorts combo}. Work de type
  `0xc/0xd` compté dans la limite (10 slots actifs max → err `0x5a`)
  (`zone @ 0x0816aff0` `ToWork`).
- **Outil** : l'item passé en param (container+slot) doit avoir `CItemLimit`
  (`item+0xfc`) dont `+0x22` ≤ niveau joueur ≤ `+0x24` (err `0xb76`). Matériau
  requis : `CCollectData+8` itemId, `+0x10` quantité, kind item == `0x15`,
  sous-type 0x33 (shop 0xc) / 0x32 (0xd) / 0x34 (0xe) — `CheckToCollect`
  `zone @ 0x08162a00`.
- **Work level** : `CCollectData+0x14` = niveau requis. Solo (pas de family
  instance) → doit être ≤ 2 (err `0x9f1`). En territoire de famille :
  `SFamily::GetBuildingItemByItemNetID`, bâtiment `+0x94` ≥ requis, `+0xc` ≥ 1
  (errs `0x392`,`0xa09`). Coût or = `data+0x34 + workOpLevel(+0x18) × data+0x34`
  via `FamilyLevyBusinessTaxes` (taxe guilde) — `zone @ 0x08162a00`.
- **Durée** : `CCollectData+0x24` : `==1` ou shopType `0xe` → cast immédiat
  (`CNC_CZ_ClientStrengthenCasting` durée = `+0x28` ticks) ; `==0` →
  durée `+0x28 × 600` (work de fond). `zone @ 0x0816a400` `ToCollect`.
- **Outils CChanceItem** (kind `+0x122`) : `0x1f43`=8003 → durée × bonus
  (`+0x12e`) ; `0x1f44`=8004 → %chance `+0x134` d'ajouter `+0x136/+0x138`
  rendement selon cible `+0x130` (shop 0xc→1, 0xd→0, 8=les deux) ;
  `0x1f45`=8005 → bonus fixe `+0x13a` ; `0x1f48`=8008 → %chance `+0x142`
  → `+0x144` (collect-all). Consommé ×1 + matériau `data+0x10` + or taxé.
- **Résolution** : `WorkResultOK` (`zone @ 0x08164790`) : slot <0xb,
  vérif place inventaire (GetContainerIndexByStackItem / GetEmptySlotNum),
  `CItemFactory::AddItemFromWork` (`zone @ 0x080afc00`, prototype depuis
  `CZoneServer+0x10c` map<TID,CItem*>) ; work `0x10` → `HandleRACombine`,
  `0xc/0xd` → `HandleRASpecificItem` (succès activity/achievement).
  `CWork+0x30` → `ToExecCmd`/`ExecCmd` (`zone @ 0x08162df0`) : liste
  {itemId,count} + exp. `EndWork` (`zone @ 0x08167eb0`) →
  `CNC_CZ_ClientPlayerWorkResultEnd`.
- **ElfWork** (pet/compagnon) : `zone @ 0x0824b0b0` `ElfWork::Execute` →
  `CKFElfManager::ToWork(elfIdx, cmd)` ; cmd 1/2 selon flags node `+0x10`
  (0x20 sortie, 0x40 dispo), bloqué en BattleRoyale, errs `0x209e/f/a0`.

## 2. LIFESKILL (craft racial) [VERIFIED]

- `zone @ 0x08260e40` `CNE_CZ_ZoneServerLifeSkill::Execute` : exige op `0x4c`
  (CLifeSkillOp). Sous-commande `this+0x12` : 0=`JewelryAnalysis`, 1/2=`Produce`
  (opType 1/2), 3=`ForgotLifeSkill`, 4=`ApperceptionLifeSKill`,
  5=`LevelUpLifeSkill` — `CLifeSkillFactory`.
- `zone @ 0x08278470` `QueryLifeSkill::Execute` → `CNC_CZ_ClientPlayerLifeSkill`
  : dump hashtable `char+0xb84` {id→{id,exp,lvldata}} + `char+0xcc8`.
- **Races** : `CLifeSkillData+4` type 1..5 ↔ `char[0x4aa]&0xf0` ∈
  {0x10|0x00, 0x20|0x30, 0x40|0x50, 0x90|0x80, 0x70|0x60} — 5 métiers raciaux.
- **Produce** `zone @ 0x080d0370` : niveau ≥30 (`0x1e`) ET ≥ `data+0x12`
  (niveau recette), or `char+0x4b0` ≥ `data+0x20`, `CheckLifeSkillType`,
  `ProduceCheck` (`zone @ 0x080d0010` : vérif matériaux `data+0x2c` +
  `CombineCheckBagSlot` + slot libre) → cast `LifeSkillCastID` durée `0x32`
  ticks + `StrengthenCasting`. `DoProduce` (`zone @ 0x080d23b0`) : consomme
  matériaux, or `-data+0x20`, `AddItemFromLifeSkill(result=data+8,
  count=data+0x10)`, `GainLifeSkillExp` si opType==2 ; **pas de jet d'échec
  visible** → succès garanti après checks [VERIFIED].
- **JewelryAnalysis** `zone @ 0x080d05f0`/`DoJewelryAnalysis @ 0x080d2690` :
  analyse bijou (item template `+0x120` → analysisId), consomme `data+0x10`
  items, gain exp.
- **Apperception** `zone @ 0x080d08e0` : lvl≥30, points `puVar3` ≥ `data+0x1c`
  coût (`0xf84`), tirage `GetShortRand(data+0x14)` dans arbre {poids→skillId} ;
  type 1 = remplace le métier de base, types 2-5 = `InsertNewSkill` →
  `CNC_CZ_ClientLifeSkillUpdate` (op 1=add, 2=del, 3=replace) + DBA command.
- **LevelUpLifeSkill** `zone @ 0x080d16a0` : lvl≥30, points ≥ `data+0x1c`,
  upgrade vers `data+0x24` (next id) ; type 1 → remplace, 2-5 → insert si absent.
- **ForgotLifeSkill** `zone @ 0x080d1020` : lvl≥30, race check, type1 →
  remplace par `data+0x28`, types 2-5 → delete → LifeSkillUpdate op 3/2.
- `EndLifeSkill` `zone @ 0x080d28a0` : fin de cast → op+0xc==0 →
  DoJewelryAnalysis, 0..2 → DoProduce.

## 3. STRENGTHEN (fortification) [VERIFIED]

- `zone @ 0x0828c090` `StrengthenItem::Execute` : `this+0x12==9` →
  `CItemFactory::StrengthenItem` (`zone @ 0x0808be80`) : op `0x26`
  (CStrengthenOp), NPC en talking range, `CheckStrengthenItem`
  (`zone @ 0x08087430`) ; coût = `GetRepairPrice × DAT_087cc618 + DAT_087cc620`
  or ; cast `StrengthenItemID` durée `0x14` (param9==1) sinon `0x28` ticks →
  `CNC_CZ_ClientStrengthenCasting` type 9.
- **CheckStrengthenItem** : item kind (vtable+8) 0 ou 1 selon canal
  (param2∈[0xb,0x14] → kind 0 requis, type≠0x22 ; param2∈[1,0xa] → kind 1 ou
  type 0x22) ; **borne `item+0x10 < 0xe` → +14 max** [VERIFIED] ; type ≠0x10 ;
  parchemin (slot p5/p6) : GameData `piVar5[0x23]` ≥ niveau requis de l'item,
  `piVar5[8]==0x2e || piVar5[9]==0x2e` (kind 46 = strengthen scroll),
  `piVar5[0x24]` = cap du parchemin (> niveau actuel), itemId `0x9d00/0x9d01`
  = parchemins haut niveau exigeant item déjà ≥+10.
- **Pierre de sécurité** (slot p7/p8) : `CChanceItem`, **kind `+0x122 == 0x1f42`
  (8002)** [VERIFIED] ; fenêtre `+300 ≤ niveau ≤ +0x12a` (0=libre), sinon
  err `0x151c`.
- **Résolution** `CItemFactory::DoStrengthenItem` `zone @ 0x080a2b80` :
  - Base : scroll data via `IGameData+0x178` (même table CCombineQuery),
    balayage `data+0x40` vec{12B} match scroll itemId → taux `data+0x32`.
  - Taux `local_254` : niveau 0 → taux scroll ; **niv 1-9 → scrollRate / 2^niv** ;
    niv 10-13 → `CFormula::AllFormulaParas+0x814[+10]` (types 0x11-0x21) ou
    `+0x824[+10]` (types 1-0x10/0x22) indexés `niv%10`.
  - Bonus échecs : `char+0xd80` (TempStrengthenFailTime, reset à 0 au succès,
    +1 à l'échec, persisté via DBA `TempStrengthenFailTime=`) ×
    `EquipAddition[niv]` (armure) ou `EquipAddition[niv+1]` (arme, niv≤13).
  - Pierre : `+0x124` = bonus taux, `+0x126` = seuil — si `itemNiv > +0x126` :
    taux += scrollRate + pierre ; sinon taux = `DAT_087cc648` (≈100%).
    `+0x129` ≠0 → **bind l'item** (bit 0x10 byte+0x24, `CItemDBO::SetItemBind`).
  - Jet `GetDoubleRand(DAT_087cc648) < taux` → **succès** : niv+1,
    `AddComboStrengthen` aux paliers 6/8/10/12/14 (sauf type 0x51),
    `UpdateCloneItemAddAttr`, `CNC_CZ_ClientModifyItemStrengthen` + résultat=1,
    `CItemDBO::ModifyItemStrengthen` + `FlushAll`, `HandleRAEQStrengthen`,
    broadcast monde si niv>7 (string 0x69d, `BroadCastScreenMessage` type 8).
  - **Échec sans pierre** : `EradicateItem` = **item détruit** +
    `CNC_CZ_ClientDropItem`, résultat=0, PlayerAction 0x2d. **Avec pierre** :
    item conservé, résultat=2, compteur échecs+1. Or + scroll + pierre
    consommés dans tous les cas.
- `zone @ 0x0828c5c0` `StrengthenClose::Execute` : handler générique de
  fermeture — `this+0x12` : 9→op0x26, 10→0x27, 0x16→0x39, 0x1c→0x52,
  0x96→0x54 (InGameLottery), 0x97→0x56 (BaseLottery), 0x98→0x57 (StarWay),
  0x1f→0x58 (MagicBox) → `ResetOperation` + retrait de la file concernée.

## 4. ENCHANT [VERIFIED — = buffs, pas enchant d'item]

- `zone @ 0x0824c5f0` `EnchantUpdate::Execute` → `ILifeEntity::GetUpdateEnchants`
  → `CNC_CZ_ClientEnchantUpdate` (set de `CEnchant*` + flags + `this_00[0x280]`).
- `zone @ 0x0824bab0` `EnchantCancel::Execute` →
  `CEffectFactory::CancelPlayerEnchant(char, id@+0x12, 0)`.
- xref `PTR__CNC_CZ_ClientEnchantAdd` : émis par `ILifeEntity::UpdateEnchant`
  (`zone @ 0x0847e880`) / `AddEnchant` (`0x0847ea90`). Donc **Add/Remove/Update/
  Cancel = synchronisation des buffs/debuffs**, table `S_Enchant.ini` +
  `S_ItemMallEnchant.ini` (CEnchantQuery `zone @ CGameData+0x200`,
  `Init` 2× avec flag mall) [VERIFIED]. Pas d'« enchant scroll d'équipement »
  dans ce flux (côté item, le renfort passe par strengthen/magicbox).

## 5. ITEMCOMBO (fusion + combos d'attributs) [VERIFIED]

- Table `GameData::CItemComboQuery` (`Init` `zone @ 0x08600a70`,
  `CGameData+0x130`), accès `QueryItemCombo` (`zone @ 0x08597420`) /
  `GetAllItemCombos` (`0x08596fd0`). **Deux usages distincts** :
  1. **Fusion NPC** : `CombineItem` `zone @ 0x0809b8e0` (op 0x27 CCombineOp,
     NPC range, cast `CombineItemID` 0x28 ticks). `CheckCombineItem`
     `zone @ 0x0809b570` : data via `IGameData+0x178`, `data+4 == 6`,
     vec `data+0x40` = **4 ingrédients {itemId,?,count}** ; slot principal =
     entrée 0 (id+count), vérif `CheckItemNum` des 3 autres, slot libre si
     nouvel item, or ≥ `data+0x24`. Charm optionnel kind `0x1f41` (8001).
     `DoCombineItem` `zone @ 0x080b0410` : consomme ingrédients + charm,
     or `-data+0x24`, **jet `GetShortRand(100) < data+0x32 + charm(+0x124)`**
     → succès `AddItemFromCombine(result=data+0x28, ×1)` + résultat 1 +
     PlayerAction 0x2c ; échec → résultat 0 + PlayerAction 0x2b (matériaux
     perdus) [VERIFIED].
  2. **Combo/qualité sur loot** : `SelectItemCombo` `zone @ 0x080a6ae0` —
     drop tables `SMonsterDrop` (stride 0x54) : roll 1e8 vs `+0x2c` → qualité
     `+0x14` (0/1→4?, 2, 4) stockée `item+0x2c` ; `RandItemCombo`
     (`zone @ 0x080a5430`) choisit comboId dans map{taux→id} selon qualité +
     `item+0x122` (kind) + `CItemLimit` ; `DiceItemCombo` `zone @ 0x080a6860`
     (même mécanique hors drop), `SelectWorkItemCombo` `zone @ 0x080a69f0`
     applique aux produits de work (`item+0xf4` = comboId forcé).
     `SelectKFElfItemCombo` `zone @ 0x080a2060` variante elf.

## 6. MAGICBOX [VERIFIED]

- `zone @ 0x08252a60` `ExecuteMagicBox::Execute` : op `0x58` (CMagicBoxOp)
  → `CItemFactory::InitMagicBox` (`zone @ 0x080894a0`) avec slots du paquet.
- `zone @ 0x080ab7f0` `ExecuteMagicBox` (résolution) : type `local_12` 1 ou 2
  (errs 0x2141/0x2142) → `CheckMagicBoxCosts` (`zone @ 0x08088200`) +
  `DoMagicBox` (`zone @ 0x080a8b90`, ~1400 l.) ; annul →
  `StrengthenItemCancel` type `0x1f`.
- `CheckMagicBoxCosts` : item principal en inventaire ; **OpFlag
  `itemData+0x34` doit avoir bit 0x8 ou 0x30** (sinon "cant magic"), classe
  local_3c : 0 (bit8) / 1 (bit0x20) / 2 (bit0x10) ; vecteurs need_idx/need_loc
  (matériaux) + slot optionnel p8/p9.
- Persistance `CItemDBO::UpdateMagicBoxData` `zone @ 0x083537f0` : champs DB
  **`magicbox_enchant1/2/3`, `magicbox_energy`, `magicbox_starlevel`**
  → la MagicBox = forge étoile + 3 enchâssements d'équipement [VERIFIED].
  `GetRandMagicBoxItemCombo` `zone @ 0x080a3eb0` tire les affixes via itemcombo.
- `DoMagicBoxShift` `zone @ 0x080b6f00` = variante (shift/reroll) [GAP : corps
  non lu en détail].

## 7. SHOP / VENDOR [VERIFIED]

- **Ouverture** : `zone @ 0x08239c30` `AskVendor::Execute` → dispatch RootCmds
  script `"askvendor"` (dialog NPC) avec `char+0x63c` forcé à 6 ; `IsNoTrade`
  → err `0x1176`. `LeaveShop` `zone @ 0x082613a0` → `ResetOperation`.
  `AskVendorContainer` (`zone @ 0x0823a840` env.) = ouverture stall joueur.
- **Achat NPC** : `CNE_CZ_ZoneServerBuyItems::Execute` (~l.416372) →
  `CItemFactory::BuyItems` `zone @ 0x080aca40` : op `0x21`, NPC range ;
  shop = `CZoneServer+0x230` map<shopId,vec<storeId>> → store =
  `CZoneServer+0x10c` map<storeId,entry> (entry `+4` itemId, `+8` nom,
  `+0x10` prix float, `+0xf0`&1 = stackable, `+0xfc` CItemLimit).
  - **Gate prestige** : itemLimit `+0x26` type prestige, `+0x28` seuil →
    `GetPrestigeExp` ≥ seuil (err `0x261`).
  - **Devise** = `itemData+0x78` : 1 = or/cristal — itemId<0x9c40 (40000) →
    `GainCrystal` (cap 100 000) ; ≥40000 → or + `local_2b0 = prix × shopTax
    (op+10) × DAT_087cc690` taxe → `FamilyLevyBusinessTaxes`, cap 1e9 ;
    remise prestige : `AllFormulaParas+0x114[prestigeLv]` ×`DAT_087cc690`
    cap `_DAT_087b2754` (si prestigeLv≥2). 2 = prestige battlefield
    (`AddPrestigeExp` 0x51), 4 = "Love", 6 = item 0x8870 (34928) achievement,
    10 = trésor de famille (broadcast `0xa73` à la guilde), 0xb = or colisée.
  - Stackable → refill des piles existantes (`ModifyItemDurability` = +count)
    puis `PutIntoContainer` ; non-stackable → `AddCloneItemWorld` 1 par slot.
- **Vente NPC** : `zone @ 0x082872a0` `SellItems::Execute` : op 0x21, NPC range,
  mode `"buy_back"` via `char+0x5fc` ; prix = **`GetSysPrice ×
  AllFormulaParas+0x568`** /unité, non-stackable → ×dur/maxDur, cap
  `_DAT_087b2754`, or total <1e9, `EradicateItem` + `GainGold`
  [VERIFIED]. `BuyBackItems` `zone @ 0x08092cd0` (rachat liste buy-back).
- **Stall joueur** : `zone @ 0x0829f5e0` `VendorSellItem::Execute` →
  `CPlayerFactory::VendorSellItem` `zone @ 0x081145e0` :
  `CheckBeginStallForPlayer/StallGoods`, nom stall <0x5b, init `char+0x7ec`
  (2 maps goods), prix ≤999 999 999, ≤14 entrées gratuites. `VendorBuy`
  `zone @ 0x080f9770` : achat chez un stall (`CNode::GetCharacter`, flag
  `char[0x3b]&0x40` = stall ouvert, `TargetInVendorRange`, ≤1 ligne/req,
  slot libre, goods `stall+0x7ec+0x1c`). Err "ElfWork 5".

## 8. COLLECTION [VERIFIED]

- `zone @ 0x082762d0` `QueryCollection::Execute` → itère map `char+0x564`
  (collection archive) → `CNC_CZ_ClientUpdateCollection` {itemIds} +
  `char+0xb68` (points).
- `zone @ …` `SaveCollection::Execute` (l.507948) : **rate-limit 0,5 s**
  (`char+0x650/654` timeval), `this+0x16==0` → 1 `PutItemToCollection(id,loc)`,
  sinon batch (2 vecteurs parallèles) ; puis `RefreshCollectionPointAward(true)`.
- `zone @ 0x0825b4x` `GetCollection::Execute` → `GetItemFromCollection`
  (`zone @ 0x080a0590`, retire l'item, contrôle `CollectionPoint < 0` warning)
  + `RefreshCollectionPointAward`. Tables : `CCollectionQuery` (+0x5fc),
  `CCollectionPointQuery` (+0xa84), `CCollectQuery` (+0x710 — **= la table
  work/récolte**, `IGameData+0x174`), `QueryCollection`/`QueryCollectionPoint`
  `zone @ 0x08597f80`/`0x085989a0`.

## 9. TABLES GameData (`CGameData::Init`, l.1091416+) [VERIFIED]

Ordre d'init/offsets : Level+0x10, Class+0x38, Monster+0x60, Ai+0x88
(S_Ai.ini), Item+0xac (S_Item.ini + S_ItemMall.ini), DropItem+0xdc,
Title+0x108, **ItemCombo+0x130**, EquipSet+0x154, GrowEquip+0x178,
Spell+0x1b4, SpellRef+0x1dc, **Enchant+0x200 (S_Enchant.ini +
S_ItemMallEnchant.ini)**, TextIndex+0x22c, Dialogue+0x250, Npc+0x274,
Mission+0x29c, RecommendActivity+0x35c, **Store+0x2c4 (S_Store = shops)**,
Nodes+0x2e8, Parameter+0x310, EquipType+0x384, StateDependence+0x3a8,
RootCmds+0x3f0, Races+0x414, Transport+0x438, SysSetup+0x460,
ElfCombine+0x4a8, ElfCollect+0x4cc, Lottery+0x650, TextLimit+0x560,
PVP+0x588, Festival+0x5d4, **Collection+0x5fc**, Color+0x6c8,
CharColor+0x7a4, Building+0x6ec, **Collect+0x710 (= c_collect work)**,
**Combine+0x734 (= c_combine : recettes fusion + scrolls strengthen,
`IGameData+0x178`)**, HoradricCube+0x77c, House+0x758, Schedule+0x7c8,
Territory+0x7f0, Achievement+0x628, RankAward+0x67c, Activity+0x334,
Script+0x6a4, GMToolCmds+0x818, DestinyRoad+0x83c, DestinyEvent+0x860,
Talent+0x884, **LifeSkill+0x8a8 (= c_lifeskill, `IGameData+0x1a4`)**,
Party+0x8cc, Partner+0x8f0, PartnerGrow+0x91c, **EquipTrain+0x944**,
SceneEvent+0x96c, ClassDropItem+0x994 (S_ClassDropItem.ini),
ClassRankBuff+0x9c0, PaySystem+0x9e8, RewardScore+0xa0c, Crusade+0xa5c,
DailyAward+0xa34, **CollectionPoint+0xa84**.
Vtable IGameData constatée : `+0xf8` item, `+0x100` ?, `+0x114` textindex,
`+0x174` collect, `+0x178` combine, `+0x1a4` lifeskill, `+0x12c` (map/scene
strings, usage shops).

## GAP / points à creuser

- [GAP] `DoMagicBox` (l.83474-84889) : formule coût énergie/starlevel et taux
  de succès des enchant1-3 non extraits (fonction ~1400 lignes).
- [GAP] `WorkCheckData` détail exact du contenu UDP renvoyé (map
  `CWorkEventCmd` à 0x89ba10c — events serveur périodiques ?).
- [GAP] `CEC_PlayerWork`/`CEC_ElfWork` (events commit) : rollback path lu à
  peine — `CEC_Work` event = commit item DB ?
- [GAP] `IGameData+0x178` confirmé partagé strengthen-scrolls ET recettes
  fusion (même CCombineData, `+4` type : 6=fusion ; type strengthen non
  revérifié — scrolls matchés par balayage de `data+0x40`).
- [GAP] `ToCombine` (l.230737) non lu — plan de production (type 0xf-0x11) :
  probablement symétrique à ToCollect avec `SWorkItem` résultats multiples.
- [GAP] Prix "BuyUp" (`CNC_CZ_ClientBuyUp` existe) : le refill de piles dans
  BuyItems couvre-t-il le BuyUp flèches/potions ? Non confirmé.
- [GAP] NCID exacts des commandes : `InitPCCommandTable` est `[FAILED]` dans
  l'export → passer par `objdump` si besoin (règle re/AGENTS.md).
