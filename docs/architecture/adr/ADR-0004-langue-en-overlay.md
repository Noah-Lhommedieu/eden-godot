# ADR-0004 — L'anglais est une couche dérivée du FR (2026-11-07)

**Statut** : accepté (propriétaire, 2026-11-06/07).

**Décision**
- Le FR (`name_fr`, tables `t_*.ini` EDENFR) est la SEULE vérité. L'anglais vit dans `eden_content.text.translation` (+ `text.glossary`), exporté dans `content/lang/en/`.
- Périmètre : contenu présent en jeu, niveau ≤ 90, nom FR utilisable (`content/rules/scope.json`, règle `ContentScope`). Hors périmètre : non traduit, repli FR.
- Une traduction = `(type, id, champ, langue)` avec `fr_hash` ; si le FR change, la vue `text.v_translation_status` la marque `outdated`.
- Contrôles automatiques UNIQUES (`TranslationChecks`) à l'import et dans l'admin : codes de mise en forme conservés, pas de CJK, longueur, glossaire strict.
- Retour arrière : `tools/content/rollback-fr-only.sh` supprime toute l'EN et prouve que l'empreinte du contenu FR est identique avant/après.

**Conséquences**
- Aucune table FR, aucun identifiant n'est modifié pour ajouter l'EN.
- Les sources EN héritées (`ui_text_en.tsv`, wiki EN) ne sont jamais des dépendances : banque d'information accessoire, filtrée par le contenu FR courant.
- Statut de relecture `auto → reviewed → validated` ; seules les lignes `auto` sont écrasées par un réimport.

**État** : voir `PLAN-ACTION-3-EXECUTION.md` (cartes traduites ; objets, monstres, PNJ, quêtes à traduire par vagues).
