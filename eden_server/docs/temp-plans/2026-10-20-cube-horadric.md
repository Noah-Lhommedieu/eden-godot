# Lot — Cube Horadric (S_ItemChest) — portage CItemFactory::CheckCubeCosts/ExecuteCubeOp

Date : 2026-10-20. Statut : IMPLANTÉ (build 0/0, 12 tests verts) — à valider en client live.

## Preuves natives (zone/decompiled.c)

- `SimpleAction` code 101 ('e') → `CCubeOp` (0x68B, IsA 0x52) + 432 `[0][0x1c][0][-1][0]` — déjà en place (ShopOpType 28).
- `CNE_CZ_ZoneServerVerifyCubeCosts` NCID **232** : `[u32 recipe][u16 n][s16 idx×n][u16 n][s16 slot×n][s16 luckyIdx][s16 luckySlot]`
  (Deserialize @0x829f4xx — les 2 vecteurs sont parallèles : idx=container, slot=loc).
- `InitCubeOp` @0x80bb0f0 : guard op IsA 0x52 + TargetInTalkingRange(op+4) →
  `CheckCubeCosts(char,recipe,vecIdx,vecSlot,luckyIdx,luckySlot,validate=1)` ;
  échec → **434 [s16 0x1c]** ; succès → `SetItemData` + op[100]=1 +
  `EffectToLife(CubeCrafting,0xf)` + **433 [s16 0x1c][s16 0xf][s16 0]**.
- `CCubeOp` : +8 recipe, +0xc vec idx, +0x18 vec slot, +0x24/0x26 lucky,
  +0x28 vec\<u32\> auxIds, +0x34 vec\<s16\> auxQuals, +0x40/0x4c/0x58 prod
  (container,slot,count), +0x64 casting. `Cancel` → remove enchant + 434 [0x1c].
- `CheckCubeCosts` @0x80ba380 :
  - blacklist zone+0x41c (vide) → fail ; vec sizes égaux requis ; recipe via GameData+0x1b4.
  - req map = {mainId:mainAmt} + 4 sous-ingrédients (SIngredient{id,name,amt}).
  - `local_12 = GetEmptySlotNum(0)` puis +1 par slot entièrement consommé.
  - pour chaque (idx,slot) posé : item→id ; match req par id direct OU par
    groupe (item col75 ∋ reqId) ; sinon AUX → push (id, qualité clone+0x2c),
    contrôle qualité aux vs recipe col5 (0=libre, 1=accepte qualité 0).
  - fin : toutes les req à 0 sinon fail silencieux ; gold < recipe.gold → texte 0x232 ;
    recipe.reqLevel ≤ char.level sinon fail ; produit non-stack → amount ≤ local_12,
    stackable → local_12 > 2, sinon texte 0x4f9.
  - lucky (idx,slot ≠ -1) : item CChanceItem cmd 8007, cap +0x140 ≥ reqLevel
    sinon texte 0x15af ; stack==1 → local_12++ ; recipe∈[4567,4590] et bonus<1
    → texte 0x21c4.
  - validate → `SetAuxData(auxIds, auxQuals)` + stocke req map côté exec.
- `ExecuteCubeOp` @0x80bceb0 :
  - DeductItem réel par slot posé (ItemConsume + texte 0x15b4 "%s x %d" par item).
  - consomme lucky aussi ; `GainGold(-gold)`.
  - `bound` : vrai seulement si TOUT input est lié/locké (itemdata+0x30 &0x80
    ou !(flags&0x84) lock) — sinon produit libre.
  - succès : `rand(100) < recipe.rate(col20) + lucky.bonus(ench 8007 param1)`.
  - pool produits : product==0 → auxIds ; <100000 → {product} ;
    product==mainId → groupe − auxIds (ou groupe complet si vide) ;
    sinon groupe complet (index col75 inversé, `LoadCubeGroup2Item`).
  - non-stack : boucle `productAmount` picks (erase si reste ≤ pool) ;
    stackable : répartit amount en ≤3 stacks aléatoires.
  - `PickAColor` : roll cumulé ×10000 sur map qualité {0,1,2,4,6} (cols25-29) ;
    clé 0 → qualité d'un aux random ; 1 → 0 ; sinon la clé ; défaut qualité item (col19).
  - émission : `ClientPlayerAction` '+' broadcast, **435 [0x1c,1,0,0,0,0]**,
    **572 ClientLoadCubeItems** `[u16 n][s16 cont×n][u16 n][s16 slot×n][u16 n][s16 count×n]`.
  - échec/pool vide : **435 [0x1c,0,0,0,0,0]** + PlayerAction '-'/'/'/'*' broadcast.
- `ClientLoadCubeItems` NCID **572** (généré incorrect : 6 s16 → corps manuel 3 vecteurs).
- `CItemQuery::Deserialize` : col19→+0x50 qualité, col29→+0x76 maxStack,
  col75→+0x104 set\<int\> groupes cube (chaîne `;`), col11→+0x30 flags bind.
- Lucky cube : item UseEnchantId → S_Enchant bloc cmd **8007**, p1=bonus%,
  p2=levelCap (`56865` → 8007,30,75).

## Implémentation

- [x] `ItemDef` : + `Quality` (col19), + `CubeGroupIds` (col75 `;`).
- [x] `GameDataParsers` : `CubeRecipe` complet (32 cols : subs[4], gold, rate,
      productKey, reqLevel, productName, productAmt, qualityWeights{0,1,2,4,6},
      param30, applicableSet col31).
- [x] `CubeService` : table recettes + index groupe→items (col75) +
      CheckCosts (req/groupes/aux/gold/niveau/place/lucky) + résolution pool +
      PickItem/PickColor.
- [x] `EntryState` : `CubeOp` (recipe, idx[], slot[], lucky, aux, casting).
- [x] `ItemEntity` : + `Quality` (colonne `quality` lue/écrite — présente dans
      le schéma natif).
- [x] `ZoneEntryFlow.Cube.cs` : 232 → validations natives + textes + cast
      433 [0x1c,15,0] ; complétion 1.5 s → deduct + or + roll + produit +
      435/anim/572 ; StrengthenClose 28 → 434 [0x1c] + cancel timer.
- [x] Wiring `ZoneServices`/`Program.cs` + build 0 err/0 warn + commit.

## Limites assumées (marquées HYPOTHESIS/TODO en code)

- `RandItemCombo` (affixes aléatoires équipement) non roulé — le produit
  reçoit sa qualité mais pas les combos (S_ItemCombo = lot futur).
- `ChangeEmbeddedAmount` (sockets) non roulé → embedded_amount 0.
- Blacklists recette/item vides (config native absente de server_us).
- TargetInTalkingRange(0) → vrai (cube self-ouvert, op+4=0).
