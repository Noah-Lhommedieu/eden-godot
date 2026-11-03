# ADR-0006 — Thèmes et atlas UI du portage 4.7 (exception à « EDENFR pur »)

- Statut : **accepté** — 2026-11-02, décision du propriétaire.

## Contexte
Les panneaux du client `portage/` (Godot 4.7) sont jugés visuellement meilleurs que ceux rendus avec les seuls atlas EDENFR
(`uidata/imagesets`). Les scripts HUD sont **identiques** entre `portage/` et eden-godot (diff du 2026-11-02) : la différence
est uniquement dans les images d'atlas : 5 atlas de base (`DejaVu`, `main`, `DejaVuImage`, `Radar`, `mapworld02`), 9 atlas
absents de fr-full (`Emoji`, `RepairAllBtn`, `AutoSellBg`, `AutoSellUI`, `EdenQualityTier`, `ChatEmojiUI`, `BulkUse`,
`EdenAltarBatch`, `HideEffect`) et les thèmes `themes/{Blue,Coffee,Green,Purple,Red}` (EDENFR-GAPS G8).

## Décision
Ces images sont reprises telles quelles, hors EDENFR, dans `eden_godot/godot_project/gamedata/` :
`themes/<Thème>/*.png` et `uipatch/*.png` (overlay routé dans `bootstrap.epk`). `CeguiToGodot.LoadFromVfs` les essaie avant
`uidata/imagesets/`. Tout le reste de l'UI (layouts, textes, polices, looknfeel, schémas) reste EDENFR.

## Conséquences
- `tools/validate-assets`/provenance : ces fichiers sont `OVERLAY-PROJET`, sans contrôle de CRC officiel.
- Retour à l'UI EDENFR stricte : supprimer `gamedata/uipatch` et `gamedata/themes`.
