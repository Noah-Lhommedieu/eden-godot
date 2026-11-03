# UI — compteur de talents (ClassWindow) et balayage de cooldown (HotbarPanel)

Statut : **verified** (build + captures GPU, Godot 4.8-beta2, 2026-11-03).

## Compteur « pts/max » des cases de talents — FIXED

- **Symptôme** : sur l'onglet « Connaissances certifiées » (License
  knowledge), aucun compteur lisible par case — seulement un trait sombre
  de ~2 px au bord droit, comme « une couche » au-dessus.
- **Cause** : `ClassWindow.RefreshLicense` créait le `Label` compteur avec
  `ClipText = true` puis `SetAnchorsAndOffsetsPreset(BottomRight)`.
  En Godot 4, un label clippé a un *minimum width* de 0 : le preset
  dimensionne au minimum → le rect résultant faisait ~2 px de large
  (marges du StyleBox seules). Le texte « X/Y » n'avait nulle part où
  s'afficher. Ce n'était ni un z-order ni un cover — `_talentCover` /
  `_certCover` restent cachés dès que la classe a le niveau requis.
- **Fix** (`scripts/hud/ClassWindow.cs` ~l.1390) : rect explicite
  `HudRoot.Pin(lv, 1, 1, 1, 1, -33, -15, -1, -1)` — pastille 32×14 px en
  bas à droite de la case, texte aligné à droite. Le vert
  (saisie non validée) et le doré (max atteint) sont inchangés.
- **Preuve** : `artifacts/shots/20261103-203042-S001-talent-fix.png`
  (`--hud-open=class --class-mock=1;1;1:30 --class-tab=1`) — glyphes
  « 0/5 » nets au bas de chaque case ; avant :
  `artifacts/shots/20261103-183306-S001-class-talents.png` (compteur
  réduit au trait sombre).
- **Limite** : capture avec `pts/pend = 0` ; le rendu « x/y » non nul et
  la couleur verte saisie suivent le même chemin de code.

## Balayage de cooldown — avancée uniforme (aire ∝ temps)

- **Symptôme** : l'assombrissement de cooldown progressait « par vagues »
  — le front accélérait au passage de chaque coin.
- **Cause** : `CooldownOverlay._Draw` échantillonnait un arc à **angle
  uniforme** clippé au carré du slot. La distance du centre au bord varie
  (×√2 au coin) : l'aire couverte n'est pas linéaire en temps — 4
  poussées par tour (le coin est « englouti » d'un coup).
- **Fix** (`scripts/hud/CooldownOverlay.cs` `_Draw`) : le front parcourt
  le **périmètre à débit d'aire constant** (paramétrage par aire
  cumulée : chaque segment de périmètre balayé ajoute
  `longueur × apothème / 2`). L'aire sombre = `frac × aire du slot`
  **exactement**, pour tout rectangle w×h. Le balayage reste un « clock
  wipe » partant du milieu haut, sens horaire, comme le natif.
- **Mesure** : captures GPU au flag QA `--cd-frac=<0..1>` (ajouté dans
  `_Process`, fige tous les overlays) —
  - `20261103-203100-S001-cd-25.png` → quart supérieur droit de l'icône
    (aire mesurée 28 %, attendu 25 % — bruit de seuil AA) ;
  - `20261103-203117-S001-cd-50.png` → moitié droite (53 %) ;
  - `20261103-203141-S001-cd-75.png` → tout sauf le coin haut-gauche
    (75 %).
  Référence pixel : `20261103-203042-S001-talent-fix.png` (sans overlay).
  Les trois formes sont contiguës, front net, aire ∝ frac.
- **Rappel** : `_frac` = `Remain/Total` fourni par
  `CooldownTracker` (opcode 368) ; les durées natives sont déjà en
  restant/total (rf_game `FUN_00a0e150` & cie) — le ratio était bon, c'est
  la géométrie qui ne l'était pas.
