# M4-FIX — KF/KFM, joueur animé, alpha-scissor (2026-11-01)

## Livré (Eden.Core, pur — testé)

- `Formats/KfFile.cs` : lecteur `.kf` Gamebryo 20.3.1.0, décodage **paresseux**
  par séquence (g.kf = 40 Mo / 383 séq.). `NiControllerSequence`,
  `NiTransformInterpolator`+`NiTransformData` (clés linéaire / quadratique
  (Bézier, tangentes entrante/sortante) / TBC (Kochanek–Bartels) / const,
  rotations quaternion **ou XYZ** → quaternion `qz·qy·qx`),
  `NiBSpline(Comp)TransformInterpolator` (+`NiBSplineData`/`BasisData`,
  de Boor ouvert uniforme, ré-échantillonné 15 Hz comme l'oracle
  `tools/nif2gltf/convert.py`), `NiTextKeyExtraData`, cycles
  loop/reverse/clamp. Rotations TBC : slerp linéaire (non fidèle).
- `Formats/KfmFile.cs` : manifeste KFM 2.2.0.0b (événement → fichier+index
  de séquence + transitions), tolérant (resync 0..2 octets comme l'oracle).
  g.kfm : 101→`move01`(6), 102→7, 103→8, 104→9, 1→`stand01`(0).
- `Formats/KfPose.cs` : pose squelette (TRS locaux, fondu de séquences) +
  **skinning CPU top-4** (`NiSkinData` poids par os). Convention row-vector.
- `NifFile` : `Nodes`/`NodeByBlock` (hiérarchie NiNode, TRS de bind),
  `NifShape.Skin/LocalVertices/LocalNormals/BlockIndex`.
- Tests `Tests/KfTests.cs` (vrais fichiers g.kf/b.kf/g.kfm/g.nif) :
  B-spline, 383 séquences décodées, 329 pistes `move01` toutes mappées sur
  le squelette, bind-pose skinning = vertices cuits (médiane < 5 cm, max
  5 mm sur #G204), KFM 101→6.

## Livré (Eden.Game)

- `Anim/CharacterRig.cs` : Model virtuel, un mesh par texture, ré-upload
  `Mesh.UpdateMesh` à 30 Hz ; `Play(name)` / `PlayEvent(101)` avec fondu 0,15 s.
  Visibles (HYPOTHESIS CharVisual) : `#<L>FACE`, `#<L>000`, `#<L>100`,
  `#<L>202`, `S<L>202`.
- `Player/PlayerController.cs` : WASD relatif caméra, Maj = course,
  collision `.smp` + glissement par axe, snap terrain, locomotion
  `DirectionalLocomotion` (101/102/103/104 → KFM → séquence), facing ±45°
  aux diagonales. `EDEN_AUTOWALK=w|a|s|d|run` (preuves sans humain),
  `EDEN_PLAYER_LOG=1` (log 1 Hz).
- `OrbitCamera` : `Current`, `Yaw`, `Follow` (suit le joueur).
- Alpha-scissor : `Content/Materials/EdenMaskedMaterial.flax`
  (`tools/gen_mat.py <guid> --masked`, GUID `3ded3de1-0114-4c4c-8d8d-9a9a1a1a1a1b`,
  Mask = alpha texture, seuil 0.5, 2 faces) ; `TerrainMaterials.ForObject(tex,
  alphaScissor)` ; props/joueur dont le shape porte une `NiAlphaProperty`
  et **toute l'herbe** l'utilisent.

## Limites connues

- ~~Texture peau `SG*` = masque rouge~~ → teintée (C_CharColor.ini,
  sélecteurs starter 0 — le blob d'apparence n'est pas encore filaire,
  HYPOTHESIS dans CharacterRig). Visage `GFACES01A` substitué au masque
  `*B` du NIF. Reste : soudure des normales des 2 moitiés de visage
  (trait central) et composite dye des tenues (`*B` → artwork `*A`
  affiché tel quel) non portés.
- Tenue = starter `#G202` (hypothèse), pas d'équipement réel.
- Interpolation TBC des rotations et canaux non-transform (alpha, matériau)
  ignorés. Pas de root motion (accum flags 0x40 = aucun).
- Rig re-uploadé en CPU (~30 Hz) : OK pour 1 joueur, à revoir pour N entités
  (M9 : SkinnedModel/AnimGraph si le moteur l'expose).
