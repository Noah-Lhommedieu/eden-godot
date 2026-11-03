# Rollback corpus — revenir aux données d'avant (Reforged)

Le passage EDENFR ne détruit rien **des données d'avant** :
**l'ancien contenu est intact** dans `eden_server/reforged/EdenReforged/`
(oracle lecture seule). Revenir en arrière = basculer des **pointeurs**.

⚠️ Exception : pendant la migration, **1169 fichiers** de `corpus/official/`
ont été **remplacés par leurs équivalents US** (1051 `.nif` FR non
convertibles + 15 `.fsm` + fichiers UI complétés). Les originaux FR sont
sauvegardés dans `corpus/original-fr/` (manifeste exact + script de
restauration — voir § « Restaurer les originaux FR »).

## Pointeurs à basculer

| Quoi | Avant (Reforged) | Maintenant (officiel) | Comment basculer |
|---|---|---|---|
| `eden_godot/raw_assets` (lien) | `eden_server/reforged/EdenReforged` | `corpus/official` | `eden_godot/switch-corpus.sh reforged|official` |
| `EDEN_REFORGED_DB` serveur (tables talents/enchants) | probing `reforged/…/data/db` | `corpus/official/data/db` | `EDEN_CLIENT_DB=off ./eden_server/start-native.sh` (= natives `S_*` complètes) ou `EDEN_CLIENT_DB=<autre db>` |
| `godot_project/assets/` (GLB/PNG convertis) | dérivés de Reforged | sur-ensemble corpus+Reforged | régénérable — pas de copie nécessaire |
| `dist/packs-test/cfgfix.epk` | — (pack d'overlay) | reconstruit sur corpus | `tools/edenpack` rebuild après bascule |

## Procédure

```bash
# 1. Bascule données client (t_*, c_*, modèles sources…)
eden_godot/switch-corpus.sh reforged

# 2. Serveur : tables natives S_* (fallback complet, 915 talents / 26k enchants)
EDEN_CLIENT_DB=off eden_server/start-native.sh
#   ou l'ancienne surcouche exacte :
EDEN_CLIENT_DB=../eden_server/reforged/EdenReforged/data/db eden_server/start-native.sh

# 3. Assets convertis — l'arbre actuel est un sur-ensemble compatible
#    (les GLB Reforged identiques restent valides). Pour un état pur :
rm -rf eden_godot/godot_project/assets/{models,anims,textures}
#    puis relancer la conversion (tools/, ~4 min pour le delta).
#    Après retour en official : idem, la conversion reprend seule.

# 4. Pack : reconstruire après toute bascule si les packs sont utilisés
#    (EDEN_ALLOW_LOOSE=0).
```

## Restaurer les originaux FR (inside corpus/official)

`corpus/original-fr/` contient les 1169 fichiers FR d'origine (copiés
depuis `corpus/fr-full`, lui-même identique à la source EDENFR
`~/Téléchargements/EDENFR/extracted`), avec `MANIFEST.txt` la liste
exacte des substitutions.

```bash
# Annule les substitutions US → remet les fichiers EDENFR purs
corpus/original-fr/restore.sh
```

Effets attendus après restauration : les ~1051 nifs FR ne se reconvertissent
pas en GLB (c'est la raison de la substitution) → les entités concernées
redeviennent invisibles. Reconversion/suppression à prévoir dans
`godot_project/assets/`, puis rebuild des packs.

Pour repartir d'un corpus 100 % EDENFR pur, alternative complète :
rebuild `eden_server/tools/build_official.py` puis rejouer `restore.sh`.

## Ce qui n'a PAS besoin de rollback

- `gamedata/data/db/lang/fr/` : surcouche additive — les lignes FR ne
  font qu'enrichir l'affichage, inoffensives sur base Reforged.
- `TextResolver` (fusion par id) : repli par id sur la base = robuste
  dans les deux sens.
- `AssetCatalog.ResolveFile` : correctif orthogonal, utile aux deux corpus.
- PostgreSQL : aucune écriture destructive faite (audits lecture seule) —
  aucune migration à annuler.
- `corpus/fr-full/` : source FR intacte — régénération possible.
