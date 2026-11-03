# Lot 2 — Remplacer les dérivés (GLB/PNG/JSON) par les formats natifs EDENFR

> **Remplacé par `PLAN-ACTION-NATIF-EDENFR.md` (2026-11-02)** — garder pour l'historique.

Statut : **NOT_STARTED** — plan du 2026-11-02. Sortie : `godot_project/assets/`
supprimé, plus aucun convertisseur dans la chaîne, tout lu depuis `corpus/fr-full`.

## État de départ (audit 2026-11-02)

| Dérivé | Source native | Consommateurs côté client | Loader pur existant (`src/Eden.Core/Formats`) |
|---|---|---|---|
| `textures/**.png` (35 k) | `.dds` (BC1/2/3/5, A8R8G8B8, parfois tronqués) | `AssetCatalog.GetTexture`, matériaux des GLB, UI, `TexBake` | `DdsInfo` (en-tête, mips), `DdsDecode` (repli RGBA) |
| `models/**.glb` (6,4 k) | `.nif` | `AssetCatalog.LoadGlbNode/GetProto/PreloadGlbProtos` → `MapLoader`, `PropBatcher`, `CharVisual`, `EntityRegistry`, `SpellFx`, `TempleBackdrop` | `NifFile` : shapes, UV0, texture de base, alpha, 2 faces, helpers de collision, nœuds, skin top-4 |
| `anims/**.slim.glb` (2,1 k) | `.kf` | `GlbAnimFile`, `AnimRetarget`, `EntityAnimator` | `KfFile` (Linear, Quadratic, TBC, XYZ, B-spline compressé, text keys), `KfPose` |
| `anims/**.kfm.json` | `.kfm` | table de motions et transitions | `KfmFile` (anims et transitions) |

Point clé : `convert.py` exportait **dans le repère NIF** (Z-up, unités natives),
et ce sont les appelants qui appliquent la rotation `(-90, 180, 0)`. Un
adaptateur natif qui garde ce repère laisse donc les consommateurs
inchangés.

## Principe : un seul entonnoir, le même contrat d'arbre de nœuds

On garde l'API d'`AssetCatalog`. Seule l'implémentation change derrière.
L'adaptateur natif doit produire **le même arbre de nœuds** que l'import
GLB : mêmes noms (`Bip01 R Hand`…), mêmes types (`MeshInstance3D`,
`Skeleton3D`, `Skin`) et mêmes métadonnées (`collision_helper`,
éligibilité `PropBatcher`). Ainsi, ni `MapLoader`, ni `CharVisual`, ni
`EntityAnimator` ne changent au début.

```text
src/Eden.Core/Formats/*      (pur, déjà testé)   NifFile, KfFile, KfmFile, DdsInfo
src/Eden.Godot/Native/*      (nouveau, adaptateur, seul à voir Godot.*)
  DdsTexture   : DdsInfo → Image.CreateFromData(w, h, mips, Dxt1|Dxt3|Dxt5|RgtcRg, octets BC tels quels)
                 A8R8G8B8 → Rgba8 (swizzle) ; tronqué → DdsDecode (repli)
  NifScene     : NifFile → Node3D { MeshInstance3D(ArrayMesh) par shape, Skeleton3D + Skin si skin }
  KfAnimation  : KfSequence → Animation (pistes rotation/position/échelle,
                 échantillonnées à 30 Hz par l'évaluateur KfFile : TBC et B-spline préservés)
  KfmMotions   : KfmFile → la table de motions qui remplace .kfm.json
AssetCatalog : bascule par type  EDEN_NATIVE=tex,mesh,anim  (A/B, défaut = dérivés tant que non validé)
```

## Phases (chacune livrée avec ses preuves, dans cet ordre)

| # | Phase | Travail | Preuves de sortie |
|---|---|---|---|
| 0 | **Banc de parité** | Outil `tools/native-parity` (headless) : pour un `.nif`, construire l'arbre GLB **et** l'arbre natif, puis les comparer (noms, types, sommets, triangles, AABB, nom de texture, os). Même chose pour une séquence KF (durée, pistes, pose à t = 0 / ½ / fin) | Rapport de parité sur un échantillon de 200 modèles et 50 anims ; la liste des écarts devient le backlog |
| 1 | **Textures DDS** | `DdsTexture` dans `GetTexture` ; matériaux et UI | Captures A/B S001, S002, HUD ; `benchmark` : VRAM, RSS, chargement ; 0 texture absente de plus |
| 2 | **Modèles statiques** (props, décor, maps) | `NifScene` rigide, helpers de collision, alpha, 2 faces, `PropBatcher` | Parité ≥ 99 % des props des maps testées ; captures A/B ; bench avant/après |
| 3 | **Personnages, monstres, montures** (skinnés) + **animations** | `Skeleton3D` et `Skin` depuis `NifSkin`, `KfAnimation`, `KfmMotions` ; retarget (`AnimRetarget`) sur les noms d'os natifs | Parité des poses ; captures du joueur, d'un monstre et d'une monture en mouvement ; les anims TBC et B-spline et les transitions `.kfm`, perdues avant, sont vérifiées à l'image |
| 4 | **Effets** (sorts, particules, flipbooks `.ifl`) | `NifFile` ne lit pas `NiPSys*` : il faut l'étendre. Voir ce que `SpellFx` attend | Captures de 5 sorts de référence |
| 5 | **Retrait** | Supprimer `godot_project/assets/` et le défaut « dérivés ». Les convertisseurs passent dans `tools/legacy/`. `validate-assets.sh` devient une **couverture native** (chaque référence de table FR résolue dans fr-full) | `tools/test.sh` vert, `capture` de toutes les maps de référence, `benchmark` S001 et S002, 0 fichier sous `assets/` |

On peut paralléliser entre agents **après la phase 0** : textures, statiques
et skinnés touchent des fichiers différents. Chaque agent possède son
fichier `Native/*`, et un seul édite `AssetCatalog`.

## Ce que le natif ne règle pas (décision propriétaire)

Les données dégradées de fr-full restent dégradées, quel que soit le
lecteur :
- ~1 061 NIF sans UV ni triangles (G3) ;
- 37 KF de montures (G2) ;
- 19 layouts mal formés (G4).

Sans seconde source EDENFR (`EDENFR/extracted/`), le passage au natif ne
fera pas revenir les arbres et structures manquants sur S002. Ce que le
natif apporte : fidélité des animations, moins de mémoire et de disque
(27 Go de dérivés supprimés), chargement plus rapide (**hypothesis**, à
mesurer), et la fin de la reconversion.

## Risques

- Contrat d'arbre implicite : `GltfDocument` crée des nœuds intermédiaires
  que le code suppose peut-être. Le banc de parité (phase 0) le révèle
  avant tout branchement.
- Upload BC direct : il faut que le renderer Forward+/Vulkan accepte les
  formats S3TC/RGTC, ce qui est vrai sur desktop. Les DDS tronqués passent
  par le repli décodé.
- Double source du Core (ADR-0003) : faire le Lot 1 avant, ou au minimum
  référencer `src/Eden.Core/Formats` depuis le csproj Godot, plutôt que de
  copier les loaders une troisième fois.
