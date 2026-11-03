# Visionneuse de skins (`tools/skinview.sh`)

Outil QA : affiche un personnage avec le même code que le jeu
(`CharVisual.BuildBody`, `CharColorTable`, tenue de classe `CharModelSpec`).
Scène `eden_godot/godot_project/scenes/skin_viewer.tscn`, script
`scripts/debug/SkinViewer.cs`.

## Lancer

```
tools/skinview.sh                       # interface interactive (panneau à gauche)
tools/skinview.sh --body=1 --class=1 --hc=3 --sc=2 --shot=/tmp/k.png --no-ui   # capture puis quit
tools/skinview.sh --all-bodies --class=1 --shot=/tmp/7.png --no-ui           # les 7 corps
```

Verrou `/tmp/godot.lock` : un seul client graphique à la fois.

## Panneau

| Réglage | Sens |
|---|---|
| Corps | code corps natif : 0 g Humain F, 1 b Humain M, 2 m Zumi, 4 f Anuran, 6 k Ursun, 8 l Halfkin F, 9 c Halfkin M (5 races, 7 corps ; Zumi/Anuran/Ursun n'ont qu'un corps) |
| Classe / Style | classe 0 = tenue de base `202` ; classe N = tenue du `c_class.ini` (style = index de garde-robe 0..8) |
| Coiffure, Visage | index du blob d'apparence (0..63) |
| Peau, Cheveux, Yeux 1/2 | sélecteurs 6 bits du blob : base de section selon le corps (peau 201/401/501/601, cheveux 101/701, yeux 301) + `C_CharColor.ini`. Les pastilles montrent la couleur résolue. |
| Gain peau corps / visage | `CharVisual.SkinGain` (défaut 2) et `FaceSkinGain` (défaut 1,35) |
| 7 corps côte à côte | compare toutes les races avec les mêmes réglages |

Souris : glisser = tourner, molette = zoom.

## Arguments

`--body --class --style --hair --face --hc --sc --ec --ec2 --yaw --dist
--cam-y --sun --amb --skin-gain --face-gain --all-bodies --no-ui --bg=r,g,b
--shot=png --shot-frames=N` (arguments après `--`, ou directement).
Chaque rebuild loggue `[skinviewer] …` avec les couleurs résolues.

## Mesure de teinte

Le fond uni et l'éclairage blanc rendent les ratios G/R et B/R comparables
à ceux des `ref-shots/` (voir `docs/knowledge/live-vs-test-visual-parity.md`).
