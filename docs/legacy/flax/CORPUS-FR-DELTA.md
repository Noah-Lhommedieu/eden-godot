# CORPUS-FR-DELTA — inventaire `fr-full` vs `official` (M0)

Décision : **EDENFR pur** (`corpus/fr-full` = seule source). Le merge
`corpus/official` (EN + overlay FR + compléments CN/Reforged) est exclu.
Ce document liste ce que `official` contenait **en plus**, et où se
trouve l'équivalent FR (ou pourquoi c'est omis).

## Méthode

`comm` sur `data/db` des deux corpus (2026-10-31) : **141 fichiers
dans fr-full, 150 dans official, 9 entrées propres à official, 0
propre à fr-full** — fr-full est quasi complet.

## Delta `data/db`

| Entrée `official` absente de `fr-full` | Statut EDENFR |
|---|---|
| `S_ItemExtra.ini` | Supplément maison du portage (items Reforged ex. monture 1290). **Omis** — régénérable depuis les tables FR si besoin (`tools/audit/`). |
| `S_ItemChestExtra.ini` | Idem — omis. |
| `S_ItemComboExtra.ini` | Idem — omis. |
| `C_DailyAward.ini` | **Absent de fr-full** (seuls `c_rankaward`/`t_rankaward`). Récompenses de connexion quotidienne : données FR à retrouver sinon feature partielle. TODO M5+. |
| `c_collectionpoint.ini` | `c_collection.ini` + `t_collection.ini` présents ; la table des points d'enchant manque → Archive/collection partielle. TODO. |
| `T_CharColor.ini` | `c_charcolor.ini` présent (même donnée, nommage client vs serveur — résolution insensible à la casse + oracle `db_decoded`). Couvert. |
| `t_level.ini` | `c_level.ini` présent + `gamedata/levels.tsv`. Couvert. |
| `C_EquipType.ini` | `EquipType.ini` présent (casse seule). Couvert. |
| `lang/` (overlay EN) | N/A par design — la base EST le FR. |

## Tables serveur (`S_*`, `T_*` natives)

Le serveur charge ses tables canoniques depuis
`server_us/Data/db_decoded` (oracle natif, inchangé) — `S_Item`,
`S_Class`, `T_Item`… `EDEN_REFORGED_DB` (= `corpus/fr-full/data/db`)
fournit noms localisés FR (`t_item`, `t_spell`, `t_enchant`,
`motion.ini`, `EquipType.ini`) — **tous présents**.

## Scènes

- `fr-full/data/scene/` = **574 fichiers** (minuscules, `s002.ini`),
  sections `[npc]`/`[reborn_monsters]` présentes → données de spawn
  FR disponibles.
- `server_us/Data/scene/` = 411 fichiers US (Majuscules).
- Correctif `start-native.sh` : dossier de liens
  `/tmp/edenfr_scene_links` avec noms normalisés `${b^^}.ini`
  (MapCode attendu `S002`). **Risque résiduel** : dans portage, le
  merge US était « prioritaire car scènes EDENFR → 0 spawn au
  parseur » — si les spawns restent vides, investiguer le parseur
  (`SCENE_DIR`/`SceneTable`) plutôt que de revenir au merge.

## Seeds PostgreSQL (§8.2)

- Bases dédiées `edenfr_db`/`edenfr_account`/`edenfr_member`
  (PG17, port 5433) — isolation des données du portage.
- `itemmall`, `sknodemap/fix`, `t_npc_extra` : uniquement si présents
  en EDENFR — sinon omis (pas de stub EN).

## Suivi

- [ ] Régénérer `S_ItemExtra` depuis les tables FR si les montures/
  itemmall EDENFR le demandent (preuve : `c_itemmall`/`t_itemmall` FR).
- [ ] `C_DailyAward` : localiser la table FR native ou marquer la
  feature daily-login comme absente d'EDENFR.
- [ ] `c_collectionpoint` : idem — vérifier si la donnée vit dans
  `c_collection`/`t_collection` sous un autre format.
- [ ] Valider spawns ≥1 map en live (S002) — critère M3.
