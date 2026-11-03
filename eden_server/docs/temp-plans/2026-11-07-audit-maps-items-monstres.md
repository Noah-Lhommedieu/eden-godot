# Audit vague 3 — maps, items, monstres (résultats)

Date : 2026-11-07. Méthode : audit runtime headless
(`godot --headless scenes/game.tscn --map=Sxxx --solo`, succès =
ligne `[GameScene] map=` + objets GLB montés) + audit offline des
tables ini vs les assets convertis.

## Maps — 139/139 chargées ✅

- 139 répertoires `assets/models/map/model/S***` audités, **tous OK**.
- Aucun échec de chargement. Les `errs=` résiduels sont des
  diagnostics GLB bénins (avertissements texture/stream), pas des
  échecs — chaque map atteint `[GameScene] map=` avec spawn + HUD.
- Maps « mono-modèle » (pas de .fsm, ex. S095) : GLB unique chargé,
  NPCs + anims retargetées OK.
- Fix associé : spam « end quaternion must be normalized »
  (~11 k erreurs par chargement) éliminé — sanitisation des clés de
  track + rest poses des squelettes + clés NaN de la lib retargetée
  (`EntityAnims`). Preuve `--quat-audit` : `animKeys=0 boneRests=0
  bonePoses=0 nodes=0`, zéro spam ensuite.

## Monstres — couverture spawnés complète ✅

- `c_monster.ini` : 13 033 lignes / 722 stems uniques.
- 22 lignes DB sans GLB modèle+anim — **aucun spawné** en scène.
- Scan des `[reborn_monsters]` des 433 `data/scene/*.ini` :
  206 stems uniques réellement spawnés.
- 2 anomalies trouvées et traitées :
  - `M0M01` (S039, monstre 65532 « Headless Knight 04 ») : typo du
    code scène — le vrai stem est `M363` (c_monster `M363040`).
    Fix : `GameScene.ResolveMonster` n'utilise le code scène que
    s'il résout un GLB, sinon repli sur les candidats c_monster
    (`[^2]`/`[^3]`/brut). Plus de mob invisible.
  - `M912` (S912, template 54910) : aucun asset nulle part
    (`M912.glb` inexistant chez le natif aussi) — entrée
    statue/marqueur spéciale (famille 54901-54905 documentée dans
    `SceneTable.cs`), **bénin**.
- Animations : `assets/anims/monster/animation/<stem>.glb` couvre
  les stems spawnés (retarget validé à chaud : « 14 anims
  retargetées » par proto).

## Items — icônes à 99,7 % ✅

- `c_item.ini` : 21 485 lignes (séparateur `|`, code col 1,
  icône = `UI/itemicon/<code>.png`).
- 51 codes uniques sans PNG (87 items, ~0,4 %) :
  - **45 récupérés** : les codes `E0xxx` vivent dans le pool
    `UI/skillicon/` Reforged — DDS → PNG convertis dans
    `assets/textures/UI/itemicon/` (46 fichiers écrits).
  - **6 introuvables** (`Q00331`, `W33001`, `J010`, `W17201`,
    `W17301`, `W28501`) : aucun asset chez le natif non plus —
    icône défaut, bénin.
- Modèles d'armes (codes `W*`) : 0 manquant.
- La colonne icône `UI012`/`UI013` (col 4) indexe les imagesets
  CEGUI, pas les fichiers — le client utilise `itemicon/<code>`.

## Reste connu (bénin, documenté)

- 22 monstres DB jamais spawnés sans assets (contenu inutilisé).
- 6 codes d'icônes sans source.
- Entrées statue/marqueur (54910/M912…) sans modèle par design.
