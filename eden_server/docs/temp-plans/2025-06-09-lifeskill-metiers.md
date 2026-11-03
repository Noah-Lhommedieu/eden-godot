# Lot — Métiers / LifeSkill (S_LifeSkill, recettes, analyse & production)

Preuves : `re/ghidra/zone/decompiled.c` —
`CNE_CZ_ZoneServerLifeSkill::Execute` @0x8260e40, `CLifeSkillFactory::{Produce,
ProduceCheck,DoProduce,JewelryAnalysis,AnalysisCheck,DoJewelryAnalysis,
GainLifeSkillExp,ApperceptionLifeSKill,LevelUpLifeSkill,ForgotLifeSkill,
CheckLifeSkillType}` @0x80d0370+,
`CLifeSkillQuery::Deserialize` @0x86058d0, `CEC_LifeSkillCast::RollBack`
@0x85416c0, `CNE_CZ_ZoneServerQueryLifeSkill::Execute` @0x8278470,
`CNC_CZ_ClientPlayerLifeSkill::Serialize` @0x81ff0a0.

## Modèle natif décodé

- **S_LifeSkill.ini** : `id|group(1-5)|itemId|name|count|reqLevel|expBase|
  expVar|expCap|gold|nextRecipe|prevRecipe|3×(matId,name,qty)|N×(resId,name,poids)`.
  - group 1 = analyse (joaillerie) : consomme `count` × `itemId` du slot visé,
    les "résultats" sont des recettes apprises (pick pondéré).
  - group 2-5 = production : consomme `Materials` + `gold` → produit
    `count` × `itemId`.
- **Nibble métier** `player_characters.life_skill` (char+0x4aa, byte haut) :
  g1→{0x00,0x10}, g2→{0x20,0x30}, g3→{0x40,0x50}, g5→{0x60,0x70},
  g4→{0x80,0x90}.
- **Recettes apprises** = `player_life_skill` (life_skill_id, exp, due_date).
- **Wire 214 LifeSkill** `[u8 type][u16 recipe][s16 cont][s16 slot]` :
  t0 analyse(cont,slot), t1/t2 produce(recipe, opType), t3 forget,
  t4 apperception, t5 levelup. Requiert op fenêtre 0x68 ouverte
  (PlayerAction 16 → `ClientEnterStrengthen` déjà émis).
- **Wire 213 QueryLifeSkill** → 543 `ClientPlayerLifeSkill`
  `[s16 life_skill][u16 n][u16×ids][u16 n][u32×exps][u16 n][u32×dues]`.
- **Cast** : `ClientStrengthenCasting` [0x68|0x69, 50, 0] + enchant 211
  (LifeSkillCastID) 2 s → fin → `EndLifeSkill` (opType0=analyse,1/2=produce).
- **DoProduce** : revérif mats → `DropItemByItemID` par paire (texte 0x534
  "%s x %d") → `GainGold(-gold)` → `AddItemFromLifeSkill(itemId,count)`
  → 435 `[0x68,1,0,0,0,0]` ; opType 2 ⇒ `GainLifeSkillExp` obligatoire.
- **DoJewelryAnalysis** : revérif slot/stack → `GainLifeSkillExp` → consomme
  `count` du slot → 435 `[0x68,1,0,0,0,0]`.
- **GainLifeSkillExp** : `gain = base - var + rand(2*var+1)` si base≥var ;
  clamp cap ; cap atteint → g1:Apperception / g2-5:LevelUp ; DBA
  UpdateLifeSkillExp + 316 idx 0xf99 "%s,%d" + 544 `[u8 0][id][exp]`.
- **Apperception** (g1, manuel t4) : exp≥cap (sinon 0xf84), aucune recette
  résultat connue (sinon 0xfe3), pick pondéré → g1: ReplaceLifeSkill
  + 544 `[3][old][new]` ; autre : insert + 544 `[1][new][0]`.
- **LevelUp** (g2-5, manuel t5) : exp≥cap (sinon 0xf83), next=col10 →
  si next.group∈2-5 : insert + 544 `[1][next][0]` ; si next.group==1 :
  replace + 544 `[3][old][next]`.
- **Forget** (t3) : recette connue, nibble match, prevRecipe(col11)≥2 →
  g1 : replace par prev + 544 `[3][old][prev]` ; g2-5 : DELETE +
  544 `[2][old][0]` + reset exp du prev + 544 `[0][prev][0]`.

## Points d'édition

- [x] `LifeSkillParser` (S_LifeSkill.ini) + `GameDataService.LifeSkillRecipes`
      (+ index `ByItemId` pour AnalysisCheck).
- [x] `LifeSkillRepository` (CRUD player_life_skill) + `ILifeSkillRepository`.
- [x] `PlayerCharacterEntity.LifeSkill` + SELECT/UPDATE repo.
- [x] `EntryState` : `LifeSkillNibble`, `LifeSkillExp`, `PendingLifeSkill`.
- [x] `ZoneEntryFlow.LifeSkill.cs` : 213→543 custom ; 214 dispatch ;
      cast 433 + résolution 2 s (PendingLifeSkill) ; consommation/produit
      avec deltas inventaire (274/275/291) ; or via SetGold+attr0x02.
- [x] `TryHandleAsync` : cases 213/214.
- [x] Supprimer le stub 214 dans ItemActionHandlers.
- [x] Charger life_skill + recettes à l'entrée (Ticket/entry flow).
- [x] Build + commit ; mise à jour MASTER-IMPLEMENTATION-PLAN.
