# ADR-0002 — Données EDENFR pures, anglais en pause

- Statut : **accepté** — 2026-11-02
- Contexte : le client 4.7 lisait `corpus/official` (EDENFR + compléments
  US/merge) et des assets convertis en partie depuis Reforged. Le
  propriétaire veut **EDENFR seul**, en français uniquement.

## Décision

1. Source unique : `corpus/fr-full` (`raw_assets`, `client_assets`). Pas
   de `corpus/official` dans eden-godot.
2. Casse : fr-full est surtout en minuscules (`schemes/dejavu.scheme`, `s002/`
   à côté de `S002/`). `EdenVfs.LoosePath` résout donc les chemins sans
   tenir compte de la casse, en essayant chaque variante (DFS), puis sans
   préfixe de table `C_/T_/S_` (`C_EquipType.ini` → `EquipType.ini`).
   `AssetCatalog` réutilise `EdenVfs.CaseResolve`.
3. Encodage : `t_*.ini` EDENFR est en **CP1252** (« Champignoïde rouge ») ;
   les autres tables restent en Big5 (`EdenEncoding.TableFallback`).
4. Langue : `TextResolver.NativeOnly = true` au démarrage, sans overlay
   `lang/` ni repli sur l'anglais. `EDEN_LANG_EN=1` réactive l'ancienne voie,
   pour comparaison uniquement.
5. Assets dérivés : seuls ceux dont la source native existe dans fr-full
   sont admis (`tools/validate-assets.sh`). Ceux issus de Reforged ou du
   merge US sont exclus, même s'ils « marchaient ».
6. Contenu de mods (layouts `limitless/` Reforged) : retiré.
7. Une donnée absente de fr-full n'est jamais comblée en silence : elle va
   dans `workflows/EDENFR-GAPS.md`, et le propriétaire décide.

## Conséquences

- Les tests qui mesuraient le corpus fusionné sont recalés sur les comptes
  EDENFR mesurés : monstres 10 923, objets 18 794, succès 1 746. Les tests
  propres à l'anglais ou à Reforged sont `Skip` avec leur raison.
- Le contenu visible baisse là où fr-full est incomplet ou dégradé : voir
  la liste des lacunes.
