# Parité visuelle personnages : live (boot.tscn) vs bancs (VisTest / capture.sh)

Statut : `verified` pour les points marqués PROUVÉ, `hypothesis` sinon.
Moteur : Godot 4.8-beta2 mono. Date : 2026-11-03. Audit en lecture seule
(aucun code modifié ; captures et logs dans le scratchpad de session).

## Résumé

Les personnages « plus moches » viennent surtout de **trois causes cumulées**,
dont aucune n'est le chemin de chargement (async/sync, EPK/loose, caches) :

1. **Gain de teinte peau trop faible** (×1,15 au lieu de ~×2) : la peau sort
   ~45 % trop sombre, brun/olive. Cause racine la plus probable.
2. **Le banc `--chars` ne charge pas la table de couleurs** (`C_CharColor.ini`)
   : il rend toujours le repli « carnation claire » (1,0,82,0,66), donc des
   persos clairs et uniformes, **non représentatifs** du jeu réel. Les tests
   « jolis » et le live « moche » n'utilisent pas les mêmes couleurs.
3. **Brouillard/ambiance de map** (S002 : brume grise) et ombrage toon
   (palier ombre ×0,66) assombrissent encore le tout : c'est le décor natif,
   identique live/banc, mais il amplifie 1.

Le chemin live et `--appearance-test` / `--skin-sheet` utilisent le même
code de teinte : la capture jointe par l'utilisateur (6 figurines, peaux
sombres/olive, cheveux de couleurs aléatoires) est reproduite à l'identique
par `vis_test --appearance-test --skin-sheet=3 --skin-body=0` sur S002.

## Preuves obtenues

| Commande | Résultat |
|---|---|
| `vis_test --map=S003 --chars --look-log` | `[look] … skin=(1, 0.82, 0.66) hair=(1, 0.82, 0.66)` : table VIDE (repli) |
| `vis_test --map=S003 --appearance-test --look-log` | `skin=(0.78,0.54,0.36)`, cheveux rose/vert/jaune : table chargée |
| `vis_test --map=S002 --appearance-test --skin-sheet=3 --skin-body=0` | 6 figurines g0..g5 : peaux sombres/olive, brume grise = plainte utilisateur |
| `tools/run.sh -- --login=test123:test123 --channel --char --look-log --shot=…` | live : `[look] body=0 hair=0/0 skin=0 → skin=(0.81,0.60,0.45) hair=(0.86,0.71,0.59)` ; perso brun sombre, mains brun foncé |
| Offline (Python, `gx01as01a.dds` masque R, moyenne 0,58) | skin 201, gain 1,15 → (138,102,76) ; gain 2,0 → (206,177,132) |
| `ref-shots/skin exemple classe chevalier.png` (visage) | moyenne mesurée (232,186,127) : très proche du gain ×2 (206,177,132), très loin du ×1,15 |

Le perso du compte `test123` : `player_characters.appearance_data` est **NULL**
(DB : race 1, class 1, gender_id 1, eyes_color2 26). Le blob envoyé est donc
nul : body 0 (« g »), coiffure/visage/couleurs 0, yeux 0/0. Le live rend
donc un corps `g` avec skin 201, hair 101, alors que la DB dit gender 1 (`b`).
C'est une incohérence de données de test, pas un bug client.

## Diffs classés par probabilité d'expliquer « plus moche »

### 1. Gain peau ×1,15 (PROUVÉ en chiffres, cause racine probable)

- `eden_godot/godot_project/scripts/game/CharVisual.cs:395` et `:403` :
  `floor = -1.15f` pour peau `S<L>*` et corps nu `#<L>000`. Dans `Tinted`
  (`CharVisual.cs` ~l.797), `floor<0` = « gain direct » : `out = tint × gain × R/255`.
- Le masque peau `gx01as01a.dds` est rouge pur, moyenne R = 0,58, max 0,84
  (les masques cheveux : max 0,48). Il est conçu pour un `modulate ×2` Gamebryo
  (même constat documenté pour cheveux et armures dans
  `docs/reports/FIX-B-textures-2026-11-02.md`, sessions 4 et 7). Avec ×1,15 la
  peau 201 donne 0,67 × teinte = brun sombre ; avec ×2 : 1,16 × teinte =
  carnation attendue, proche de la ref-shot.
- Le rapport de session 3 dit déjà « teinte peau/cheveux NON revue (peau
  orangée) » ; la session 4 a choisi ×1,15 sans mesure contre la ref.
- Les peaux de la table sont déjà sombres pour certains sélecteurs (207 =
  109,75,58 ; 208 olive 149,132,100). Avec ×1,15 elles tombent à (73,50,39) et
  (99,88,67) = « sombre/olive » exact de la plainte.

Correctif proposé : `CharVisual.cs:395` et `:403` passer `-1.15f` en `-2f`
(gain ×2, clamp déjà fait par `Math.Clamp` dans `Tinted`).
Vérifier ensuite le visage : `FaceComposite` (`CharVisual.cs:864-866`)
applique la peau avec un gain 1 sur `kr` ; la ref-shot (visage 232,186,127)
suggère un facteur ~1,5-2 sur le terme `kr·skin` (pas sur le terme artwork `A`).
Test à faire : multiplier seulement `kr * skin.R/G/B` par 2 et comparer.

### 2. Banc `--chars` sans table de couleurs (PROUVÉ)

- `eden_godot/godot_project/scripts/debug/VisTest.cs:469` (boucle `_chars`) ne
  fixe jamais `TextResolver.ResolveFile` ; seul `AppearanceTest` le fait
  (`VisTest.cs:281`). `CharColorTable.EnsureLoaded`
  (`scripts/data/tables/CharColorTable.cs:103-124`) tombe alors sur
  `StoreTable.ResolveDefault`, qui cherche `raw_assets/data/db/C_CharColor.ini`.
  Le fichier réel est en **minuscules** (`c_charcolor.ini`) : introuvable sur
  Linux (sensible à la casse). Résultat : `_byId` vide, **verrouillé pour toute
  la vie du processus** (`_byId = map` même vide, `:124`), et `GetById` renvoie
  la carnation de repli (1,0,82,0,66) pour peau ET cheveux.
- Conséquence : les captures « jolies » (`--chars`, `capture.sh` avec figurines)
  sont plus claires que le jeu réel (peau ×1,15 × repli = 0,67×(1,0,82,0,66) clair).
  Les tests ne voient jamais les vraies couleurs, les peaux sombres et les
  cheveux roses/verts du live.
- `capture.sh` (game.tscn `--solo`) **n'affiche aucun personnage** : le corps
  n'est construit que si `_attached` (GameScene.cs ~l.510). Il ne peut donc
  rien prouver sur les persos.

Correctifs proposés :
- `VisTest.cs` en tête de `_Ready` : `TextResolver.ResolveFile ??= rel =>
  AssetCatalog.ResolveRawAsset(rel) ?? DataPaths.ResolveFile(rel);` (comme
  `:281`), pour tous les modes.
- `CharColorTable.cs:103-124` : ne pas verrouiller une table vide
  (`if (map.Count > 0) _byId = map;`), pour éviter la course ci-dessous.
- `StoreTable.ResolveDefault` : recherche insensible à la casse (ou nom
  `c_charcolor.ini`) ; hors périmètre de cet audit, à noter.

### 3. Course potentielle au premier chargement de la table (hypothèse)

`PreGameWarmup.Start` (`LoginScreen`, thread worker) construit les corps du
dernier login (`user://pregame_hint.cfg`) pendant que l'utilisateur tape son
mot de passe. `TextResolver.ResolveFile ??=` n'est posé que par
`CharSelectScreen.FillSlots` (`CharSelectScreen.cs:340`) ou `GameScene.cs:190`,
**après**. Si le warmup atteint `TintFor` avant, la table se verrouille vide
pour toute la session (cf. point 2) : teintes de repli live, y compris en jeu.
Dans mon run auto (`--login` immédiat) l'ordre était sain (`[look]` avec vraies
couleurs). Le timing humain (>5 s sur l'écran login) reste à reproduire :
ajouter un `sleep` côté login ou lire `[look]` sur un run manuel. Même
correctif : poser `ResolveFile` dans `ScreenManager._Ready`/`BootScreen`
(avant tout warmup) et ne pas verrouiller une table vide.

### 4. Ambiance de map et ombrage (identique live/banc, amplifie 1)

- Tous les chemins passent par `MapLoader.BuildEnvironment`
  (`world/MapLoader.cs:668-777`) : brouillard `FogDepthEnd = sight/FogMax`,
  ambiance `AmbientEnergy 0.55` (min 0,4), soleil `SunEnergy` ; map S002
  (« Monts Calcaires ») est brumeuse grise, S003 est ensoleillée. Le live
  spawn en S002 (la scène montrée). Les bancs utilisent S003 par défaut : le
  décor change donc la perception, pas la teinte.
- Shader toon `shaders/toon_lit.gdshader` : `t` = 0,66 / 0,86 / 1,0 par
  palier, appliqué à l'albedo : le côté ombre est ×0,66.
- Réglages de `gamedata/settings.cfg` (identiques partout) : preset ultra,
  `ssao=true`, `ssil=true`, `dof=true` (flou lointain 120 m), `fx_photo=true`
  (contraste 1,08, saturation 1,15), `fx_glare=true`, MSAA 8x. Appliqués par
  `SettingsApplier.ApplyGraphics/ApplyColorGrade` à la scène courante, 5 frames
  après un changement de scène. SSAO/SSIL assombrissent les creux du perso.
- Non prouvé : en live la map s'instancie peut-être plus de 5 frames après le
  changement de scène (StartLive) ; dans ce cas SSAO/SSIL/glow/photo ne
  seraient pas appliqués au `WorldEnvironment` créé après. Vérifier par
  `--settings-reapply` (existant) ou en loguant `env.SsaoEnabled` à 10 s.

### 5. Différences d'entrée (apparence, tenue) : sans effet sur la teinte

| Élément | Live | Bancs |
|---|---|---|
| Blob | serveur (NULL → zéros, body `g`, skin/hair 0) | `--chars` : body 1 (`b`), skin 0, hair 0, coiffure 5 ; `--appearance-test` : table de combos fixes ; `--skin-sheet` : aléatoire |
| Tenue | classe via `LocalClass()` (`CharVisual.cs:1126`) ; `BuildBody` à froid avec class 0 → repli `202` (`BaseEquipIds`, `:113`), puis `ApplyEquipment` avec classe 1 → `202` | `classId=0` sauf `--skin-classes` |
| Ordre | `BuildBody` : `ApplyLook` → `ApplyFaceVariant` → `TintMeshes` ; `ApplyEquipment` re-teinte (idempotent, `IsTintProduct`) ; `CharDressing.Apply` fait `ApplyLook` puis `RetintVisible` | identique (même fonctions) |
| Caches | `CharBodyCache` (preview) + `_tintCache` statique (clé tex+teinte+plancher) ; aucune invalidation si la table change entre deux builds | idem, processus neuf à chaque run |
| Async/sync | `Tinted` utilise `Texture2D.GetImage()` (readback) : séquentiel, jamais en parallèle ; pas de différence de résultat | idem |
| Packs | `tools/run.sh` et `capture.sh` ne posent ni `EDEN_PACKS` ni `EDEN_ALLOW_LOOSE` : loose dans les deux. Même contenu | idem |
| Thème/réglages | `user://clientSettings.json` ne contient pas de réglage vidéo ; `gamedata/settings.cfg` est partagé | idem |

Aucun de ces points ne change la formule de teinte. Le clone du perso en
sélecteur passe par `TempleBackdrop` (Reinhard, exposition 1,15) ou
`CharacterPreview` (Filmic, ambiance 0,8) : éclairage différent du jeu ; à
comparer à part (le temple GLB était « introuvable/illisible » dans mon run :
`S218_room10b.glb`, fond uni).

## Formule native (décompilé, `re/reindex`)

- `FUN_0073e970` @0x0073e970 : peau, `GetData(base + sel)`, base 201/401/501/601
  selon le code corps (2→401, 4→501, 6→601, sinon 201) ; couleur dye
  **slot 2**. Déjà conforme dans `CharColorTable.SkinBase`.
- `FUN_0073e8d0` : cheveux `101 + sel` (701 pour corps 6), **slot 1**.
- `FUN_00ab72f0(slot, rgb*, idx)` : écrit le triplet dans la propriété matériau
  du slot (`+0x34 + idx*12`) et dans la couleur ambiante/diffuse
  (`+0x2c` = couleur × constante `DAT_00ee0e0c`, `+0x44` = couleur). Donc
  la teinte native n'est pas « tex×teinte » seul : la constante `DAT_00ee0e0c`
  (valeur non lue : dans `.rdata`, à extraire avec `objdump -s`) participe à
  l'éclairage. Le facteur exact reste à lire pour fermer le point 1.
- Indice isolé : `FUN_00ab8000` choisit `%cFACES%02dA/B` (artwork + masque
  peau/iris) : le visage est bien `A + Σ Bᵢ·cᵢ`.

## Comment prouver le correctif

1. Poser `ResolveFile` dans `VisTest._Ready` (point 2), puis
   `vis_test --map=S003 --chars --look-log` doit afficher des couleurs de la
   table (skin=(0.81,0.60,0.45), plus le repli).
2. Appliquer le gain ×2 (point 1), reconstruire
   (`flock /tmp/eden-build.lock tools/build.sh`), puis capturer :
   `vis_test --map=S003 --appearance-test --skin-classes=1 --skin-style=0`
   (ou `ClassWindow` avec la classe chevalier) sous le cadrage de
   `ref-shots/skin exemple classe chevalier.png`. Mesurer la moyenne du visage
   et des mains (Python/PIL) : cible ≈ (232,186,127) sur ref, tolérance ±15 par
   canal (éclairage différent : comparer plutôt les ratios G/R et B/R).
3. Comparer côte à côte S003 et S002 avec `--no-fog` pour isoler le brouillard.
4. Course (point 3) : lancer `tools/run.sh -- --login=test123:test123 --look-log`
   à la main en laissant ≥ 10 s sur l'écran login ; `[look]` doit rester aux
   couleurs de la table.
5. `tools/test.sh` : ajouter un test `CharColorTable` qui vérifie que
   `GetById(201)` ≠ repli quand `ResolveFile` est posé, et qu'une table vide
   n'est pas verrouillée.

## Limites de cet audit

- Pas de commit ni de modification de code ; aucune capture « après » (le
  correctif n'est pas appliqué).
- Le facteur de gain natif exact n'est pas lu dans le binaire (`DAT_00ee0e0c`) :
  ×2 est inféré (masques R max 0,48/0,84, ref-shot, doc FIX-B).
- Le sélecteur de personnage (temple) n'a pas été capturé (GLB temple absent).
- La course de warmup (point 3) est plausible mais non reproduite.
