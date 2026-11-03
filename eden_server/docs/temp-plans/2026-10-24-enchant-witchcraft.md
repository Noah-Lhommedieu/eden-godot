# Lot — enchant witchcraft (kind 56/79) + GM cleanup inventaire

Date : 2026-10-24

## Symptômes live (trace 19:33-19:37)

- `ItemUse` slot7 cible (0,0,17) = geste « enchant → arme » : notre
  handler ne connaît que kind 45 (gemme) → ack neutre, l'enchant ne
  s'applique jamais. L'icône posée par le client restait fantôme.
- Déséquipements OK en fait (277 result=1) — l'« item qui reste » =
  icône fantôme, purge 275 désormais émise au 2ᵉ essai.
- Inventaire plein : sac rempli de clones GM — pas de commande pour
  vider → ajout `del_item` / `clear_bag`.

## Preuves natives

- `UseItemToItemControl::UseItem` @0x84eb780 — dispatch kind :
  - 0x2d (45) gemme → embedded (porté).
  - **0x38 (56) / 0x4f (79) → `target+0x4c = itemId du scroll`
    (witchcraft), `UpdateCloneItemAddAttr`, `RefreshEQAttr` si porté,
    `NotifyRefreshAttr`, texte 0x9f7, `CItemDBO::ModifyItemWitchCraft`
    (`UPDATE <table> SET witchcraft=…`).**
  - 0x3b (59) teinture → `item+0x50/0x54` = color, `ModifyItemColor`.
  - 0x54 (84) → `DoTransGold`.
  - default → `InvokeItemEffect`.
  - Queue commune : `CNC_CZ_ClientModifyItemInfo` (369) record
    complet de l'item cible + consommation du scroll.
- `CheckExecute` @0x84ecd60 case 0x38 — règles cible :
  - cible existe ; classe item ∈{0,1} ; kind ≠ 0x51 et < 0x28.
  - targetKind==0x10 → srcEquipLoc==6 ; ==0x27 → srcEquipLoc==0xc.
  - srcEquipLoc==7→targetKind 0x13 ; ==0xb→0x14 ; ==9→0x23 ;
    sinon srcEquipLoc≠0 → srcEquipLoc==targetKind ; ==0 → tout.
  - `target+0x4c == srcItemId` → déjà enchanté → refus.
  - niveau : `src+0x92 <= limite` (char lvl si target+0x114≠0 sinon
    lvl requis cible) — sinon texte 0x9f8.
- `UpdateCloneItemAddAttr` @0x8097d00 : witchcraft (clone+0x4c)
  lookup itemId → CItemAttr mergé comme les gemmes (les scrolls
  n'ont pas de colonnes d'attr → zéro serveur, le client affiche la
  ligne enchant depuis sa donnée locale).
- kind 79 = « Costume Enchant » (8 items, UseType 5, EquipLoc 1-9) —
  cible = classe 0xd (costume), srcEquipLoc==0 ou ==EquipLoc cible.

## Implémentation — FAIT

1. [x] **Raccourci ItemUse→Equip SUPPRIMÉ** — le natif n'a aucun
   chemin : `UseItemParam` ne crée un control que pour UseType
   1/2/3/5/6 ; équipement = UseType 0 → `DoUseItem(NULL)` →
   `return 0` (rien). L'équipe au clic droit = NCID 7 côté client.
   Items UseType 0 → `UseItemFinalAsync` → ack sans consommation.
2. [x] `HandleItemOnItemAsync` : case kind 56/79 →
   `ApplyWitchcraftAsync` (résolution cible 0/1/4, validation,
   écriture `witchcraft`, texte 0x9f7, 369 record complet, refresh
   profil si porté, consommation 291/303/275).
3. [x] `InlayService.ValidateWitchcraft` (Eden.Core, testable) —
   rules 0x38/0x4f + textes `TextCannotEnchant` 0x9f6 /
   `TextEnchantAdded` 0x9f7 / `TextEnchantLevel` 0x9f8
   (T_TextIndex 2550-2552).
4. [x] `InventoryRepository.UpdateWitchcraftAsync(itemUid, w)` —
   balayage des 6 tables par serial global (ModifyItemWitchCraft).
5. [x] GM `del_item <bag> <slot>` + `clear_bag <bag>` — DELETE +
   275 par slot vidé.
6. [x] 7 tests `WitchcraftValidationTests` (scroll universel,
   EquipLoc↔kind, paire 7→collier 19, re-application, gate niveau,
   costume 79, cible non-équipement) — 1073 verts total.

## Données clés

- kind 56 EquipLoc = kind cible : 0→universel, 2→kind 2, 7→kind 19
  (collier), 9→kind 35 (trophée), 0xb→kind 20 (anneau) — paires
  natives {7→0x13, 0xb→0x14, 9→0x23}, sinon srcLoc==targetKind.
- kind 79 = enchants costume : EquipLoc 0 ou == EquipLoc cible.
- Équipement joueur = kinds 1-40 (UseType 0) ; 157 pièces ont
  UseType 2 (équipable ET consommable — life control natif).

## À vérifier client

- Enchant scroll → équipement : ligne enchant affichée, scroll
  consommé, persistance au relog (369 + UPDATE witchcraft).
- Clic droit équipement = NCID 7 (plus jamais ItemUse→equip).
- `//del_item`, `//clear_bag` pour nettoyer le sac de test.
