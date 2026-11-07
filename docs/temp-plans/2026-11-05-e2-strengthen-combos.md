# E2 — Combos de fortification (AddComboStrengthen)

Date : 2026-11-05. Suivi de la phase E (serveur) du plan craft-guild.

## Preuve native (zone @ 0x080a2160 `lapis::CItemFactory::AddComboStrengthen`,
## appelée par DoStrengthenItem @ 0x080a2b80)

- Appel après succès quand `kind != 0x51` et `nouveauNiveau ∈ {6,8,10,12,14}`.
- Cible : `clone+0x34` = vecteur u16[11] = `extra_combo_id1..11`.
  Le slot = premier index LIBRE dans [6..10] (0-based) — donc extra_combo_id7..11.
  Tous pleins → retour 0.
- `uVar31` = type d'item (`CItemData+0x20` = col8 `Kind`) ; doit figurer
  dans le vecteur de types du combo (col3 `17#18#...`).
- Niveau : `CItemLimit+0x22` (RequiredLevel) ∈ [col5, col6].
- Si flag col9 (u8, +0x1b) == 0 (toutes les lignes EDENFR) :
  - exclure si l'id du combo (+0x04) est déjà dans les slots 6..10 ;
  - exclure si le **groupe** col7 (+0x18, s16) égale un groupe déjà posé
    (vecteur complété à 5 entrées par 0 → groupe 0 aussi exclu) ;
  - exclure si col4 (+0x14, u16) > 3 — les pools 4/5/6 sont la magicbox.
- Poids cumulatif : `total += (s8)col8` dans un map ordonné ;
  `random() % total` → premier cumul > roll.
- Écrit `vecteur[slot] = combo->id` puis
  `CItemDBO::ModifyItemCombo` (les 11 u16) + `CNC_CZ_ClientModifyItemInfo` (369)
  au possesseur.

## Colonnes `S_ItemCombo.ini` (|V.4|42|, 5858 lignes EDENFR)

| col | champ natif | sémantique |
|-----|-------------|------------|
| 0   | id u32/u16  | clé map + id stocké |
| 1/2 | strings     | code `G00003`, nom Big5 |
| 3   | vecteur u16 | kinds applicables |
| 4   | u16 +0x14   | pool/tier (≤3 fortif ; 4/5/6 magicbox) |
| 5/6 | u8 +0x16/17 | niveau requis min/max |
| 7   | s16 +0x18   | groupe (dedup fortif) / star (magicbox) |
| 8   | s8  +0x1a   | **poids** |
| 9   | u8  +0x1b   | flag (≠0 → saute les exclusions) |
| 10  | long        | EnchantId (magicbox) |
| 11+ | …           | rang/effets |

Parser actuel : `AttrType`=col4, `MinLevel`=col5, `MaxLevel`=col6,
`Tier`=col7, `Value`=col8, `EnchantId`=col10, `Rank`=col11 — mapping
correct, ajouter `Flag` (col9).

## Implémentation — **FAIT** (2026-11-05)

- [x] `CraftRecipeParser.ItemCombo` : + `Flag` (col9), doc sémantique col7/8.
- [x] `ItemEntity` : `GetExtraCombo`/`SetExtraCombo` index 0..10
      (0-8 → ExtraCombos, 9 → ExtraComboId10, 10 → ExtraComboId11).
- [x] `StrengthenComboService` (`Eden.Core/Game/Services/`) : roll natif —
      premier slot libre 6..10, filtre type/niveau/groupe/tier≤3,
      pick pondéré cumulatif, RNG injectable, écrasement cum-égal.
- [x] `InventoryRepository.UpdateItemCombosAsync` — miroir
      `CItemDBO::ModifyItemCombo` (extra_combo_id1..11, inventory1/2).
- [x] `ZoneEntryFlow.Strengthen` : après succès, si niveau ∈ {6,8,10,12,14}
      et kind ≠ 0x51 → roll → `UpdateItemCombosAsync` + 369 si combo posé.
- [x] Tests `StrengthenComboTests` : **11/11 verts** — slot libre,
      exclusions id/groupe/niveau/type/tier>3, flag≠0, pick pondéré,
      groupe 0, slots pleins → 0.

## Preuves

- `dotnet build Eden.sln` : 0 erreur, 0 warning.
- `dotnet test --filter StrengthenCombo` : 11/11.
- Suite complète Eden.Tests : 1696/1697 — seul échec
  `GrantExp_Level30Repro`, un `throw` de debug **pré-existant** dans
  `ProgressionTests.cs:79` (commit MAJ), sans rapport.

## Reste

- [x] Validation live 2026-11-06 : item 28597 monté à +14 par le chemin
      natif (action 13 → fenêtre `strengthenwnd_na.layout` → 14×
      `StrengthenBtn`/NCID 119). Les 5 paliers ont émis 369 et le client
      les a tous décodés après correction de
      `ZoneClientModifyItemInfo` (`color` i64, queue `[u8][u8]`, branche
      mode 8). DB : `extra_combo_id7..11 = 195,145,2795,245,95`,
      `strengthen=14`. Captures
      `godot-shots/2026-11-06-strengthen-plus14-*.png`.

---

## E7 — RandItemCombo (produits work) — **FAIT** (2026-11-05)

Preuve : `lapis::CItemFactory::RandItemCombo` zone @0x080a5480,
call site `EndCombine` @0x081657b5, `AddWrokItem` @0x084d17f0,
`SelectWorkItemCombo` @0x080a69f0.

Port : `Eden.Core/Game/Services/ItemComboRollService.cs` —
- pools par col4 (0=« General », 1=« PartKnock », 2=q6-seul,
  3=collectée jamais tirée) ; pool0 OU pool1 vide → sortie sans combo
  (« get_combos empty! » natif) ;
- tirages par qualité : q0(1,1) q2(2,2) q3/5(1,0) q4/6(3,3), q6 +1
  tier2 ; roll % total figé, premier cumul > roll ; trouvé=consommé,
  flag==0 → effacé, raté → rejoué ;
- sortie : extras = ids en ordre (pool0, pool1, tier2), padé 11 ;
  main = groupe (col7) majoritaire.
- `WorkOrderService.RollCombineProduct` : équipement kind 1..0x22
  ≠0x10, qualité 0 → `p.Combo`/`p.ExtraCombos`.
- `WorkProduct.ExtraCombos` → 11 slots mémoire (9 persistés, comme
  SWorkItem natif) ; claim copie via `entity.SetExtraCombo` (10/11
  compris) ; INSERT couvre déjà les 11 colonnes.

Non porté (documenté) : `DiceItemCombo` drops (qualité d'instance
non modélisée), chemin param_6 rank-target (toujours 0 aux call
sites), set d'exclusion param_7 (vide pour work).

Tests : `ItemComboRollTests` (20) + 2 intégration `WorkOrderTests` —
**50/50 verts** sur filtres kind/niveau, counts qualité, pondération,
sans-remise/flag, q6 tier2, groupe dominant, pool vide → 0, padding 11.
