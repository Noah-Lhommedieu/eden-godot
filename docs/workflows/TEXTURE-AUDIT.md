# Audit des textures (aucun modèle sans texture)

Commande : `tools/native-coverage/native-coverage.sh --textures` (inclus dans `--all`, ~9 s).
Sorties dans `artifacts/coverage/` : `textures.tsv` (un écart par ligne : statut, catégorie,
source, slot, nom demandé, raison), `textures.fuzzy.tsv` (références satisfaites par un repli
du résolveur), `textures.exceptions.tsv` (absences authentiques, avec leur ligne G). Code de
sortie 1 s'il reste un écart non documenté. Code : `tools/native-coverage/AllCoverage/TextureAudit*.cs`.

## Méthode

1. **Résolveur** : port fidèle de `NifMaterials.EnsureIndex/ResolveCore`
   (`eden_godot/godot_project/scripts/native/NifMaterial.cs`, dépendant de Godot) dans
   `TextureAudit.cs`. Le script compare le corps des deux `ResolveCore` à chaque lancement et
   échoue s'ils divergent (testé : une modification du port est détectée).
2. **Tous les .nif** de `corpus/fr-full` (6 420 lus ; `map/map.nif` est un exécutable PE,
   exception G12) : chaque `NiTexturingProperty` (base, dark, detail, gloss, glow, bump, normal,
   parallax, decals, shader maps), chaque source des `NiFlipController` (une `.ifl` serait
   dépliée en ses frames), et toute autre `NiSourceTexture` externe. Une référence = (nif, slot,
   nom) distincts ; chaque nom doit se résoudre ET le `.dds` choisi doit se décoder (en-tête +
   taille, mêmes règles que `AllCoverage.CheckDds`).
3. **Personnage**, rejoué comme `CharVisual`/`CharModelSpec` :
   - 7 corps (codes 0/1/2/4/6/8/9 = g/b/m/f/k/l/c) : modèle `char/model/<L>.nif` présent ;
   - **visages** : chaîne exacte de `FaceTexture` (`<L>FACES<ss><variante A-F>` -> sans lettre -> `A`)
     pour tous les styles de création (`CharCreateBodySpec.BoundsFor` : face 0..4, Ursun 0..1) x
     skin 0..9 ; les styles hors bornes qui ont des fichiers (06-12, 70-76) sont comptés en `info`
     (le serveur refuse ces valeurs à la création) ;
   - **cheveux** : coiffures 0..10 (Ursun 0..7) ; chaque shape affichable du méga-modèle (`*FACE`,
     `*HAIR`, `#<L>…`, `S<L>…`) doit avoir un albedo nommé ;
   - **tenues** : toutes les lignes de `4pequip.ini` (7 tables), `Equip`, `NewEquip`, `SuitEquip`,
     `ElfEquip`, `ELFSuit` (les quatre dernières sont vides ou quasi) : mesh `#…` présent dans le
     méga-modèle de la lettre, sinon `char/model/<mesh>.nif`, plus masque `<skin>B.dds` et
     artwork `<skin>A.dds` (`DyeMeshSurfaces`) ; `4pequipmap.ini` (les deux noms de chaque paire).
4. **Tables** : `weapon.ini` + tous les codes `W#####` de `c_item/monsterlist/npclist`
   (`item/model/W###.nif`, skin `item/texture/W#####.dds`), `monsterlist`/`npclist` (nif + texture
   variante), `ridelist`, `decorate`, `4PHouse`, `c_monster`/`c_partner` (modèle = code sans ses 3
   derniers caractères, comme `GameScene.ResolveMonster`/`PartnerTable`), `c_npc` (code présent
   dans `npclist`, comme `NpcModelTable`).

**Faces des corps non-B** : le NIF cite `<L>FACES01A/B.dds` (qui existent) ; le fichier sans lettre
`<L>FACES01.dds` n'existe que pour B (01-03). Il n'est qu'un premier essai de `FaceTexture`
(skin 0), suivi de la variante puis de `A` : l'outil rejoue cette chaîne, donc aucun faux écart
(320 combinaisons de création, 320 trouvées sans aucun repli de variante).

## Résultats (2026-11-02, après correction du résolveur)

| Catégorie | Références | OK | Repli | Manquant | Illisible | Exception (G) | Info |
|---|---:|---:|---:|---:|---:|---:|---:|
| NIF char : corps (méga) | 1 875 | 1 871 | 0 | 0 | 0 | 4 (G13) | |
| NIF char : tenues `<L>nnnn` | 5 302 | 5 299 | 0 | 0 | 0 | 3 (G13) | |
| NIF char : tenues `X…` | 507 | 507 | 0 | 0 | 0 | 0 | |
| NIF effets | 13 912 | 13 905 | 3 | 0 | 0 | 4 (G13) | |
| NIF items | 1 611 | 1 610 | 1 | 0 | 0 | 0 | |
| NIF maisons | 91 | 91 | 0 | 0 | 0 | 0 | |
| NIF décor | 148 | 148 | 0 | 0 | 0 | 0 | |
| NIF carte : objets | 2 012 | 2 011 | 1 | 0 | 0 | 0 | |
| NIF carte : ciel | 65 | 65 | 0 | 0 | 0 | 0 | |
| NIF carte : autres | 8 849 | 8 843 | 1 | 0 | 0 | 5 (G13) | |
| NIF monstres/PNJ | 1 537 | 1 534 | 2 | 0 | 0 | 1 (G13) | |
| NIF montures | 965 | 964 | 1 | 0 | 0 | 0 | |
| **Sous-total NIF** | **36 874** | 36 848 | 9 | 0 | 0 | 17 | |
| Corps joueur (modèles) | 7 | 7 | 0 | 0 | 0 | 0 | |
| Corps joueur (shapes affichables) | 1 782 | 1 782 | 0 | 0 | 0 | 0 | |
| Visages (création) | 320 | 320 | 0 | 0 | 0 | 0 | |
| Visages (hors bornes) | 640 | 134 | 497 | 0 | 0 | 0 | 9 |
| Coiffures (création) | 74 | 74 | 0 | 0 | 0 | 0 | |
| Table `4pequip` (7 corps) | 37 601 | 37 552 | 0 | 0 | 0 | 49 (G14) | |
| Table `4pequipmap` | 273 | 273 | 0 | 0 | 0 | 0 | |
| Autres tables d'équipement | 6 | 6 | 0 | 0 | 0 | 0 | |
| Armes (`weapon.ini`, codes `W#####`) | 6 001 | 5 998 | 0 | 0 | 0 | 3 (G12, G16) | |
| Monstres (`monsterlist`) | 8 012 | 8 007 | 0 | 0 | 0 | 5 (G12) | |
| Monstres (`c_monster` -> modèle) | 10 923 | 10 911 | 0 | 0 | 0 | 12 (G17) | |
| Partenaires (`c_partner` -> modèle) | 446 | 446 | 0 | 0 | 0 | 0 | |
| PNJ (`npclist` / `c_npc`) | 1 319 / 2 486 | tous | 0 | 0 | 0 | 0 | |
| Montures (`ridelist`) | 298 | 298 | 0 | 0 | 0 | 0 | |
| Décor (`decorate`) | 464 | 436 | 0 | 0 | 0 | 28 (G15) | |
| Maisons (`4PHouse`) | 30 | 30 | 0 | 0 | 0 | 0 | |
| Frames des 45 `.ifl` (eau, montures, armes) | 500 | 500 | 0 | 0 | 0 | 0 | |
| **Total** | **108 056** | 107 427 | 506 | **0** | **0** | 114 | 9 |

Le « repli » des visages hors bornes (497) est la chaîne de variante normale de `FaceTexture`
(styles 06-12 : seules A et B existent). Les 9 « repli » des NIF sont listés en G18.

Slots des références NIF : base 26 830, dark 3 293, flip 3 100, decal0 3 023, detail 572,
bump 54, glow 1, normal 1 (aucune `.ifl` n'est citée par un NIF ; les 45 `.ifl` du corpus sont
auditées à part : 500 frames, toutes résolues). Le rendu ne lit que l'albedo
(`AlbedoMap` : base, detail, dark, decal 0) ; les autres slots sont audités quand même.

## Écarts restants : tous des absences authentiques

Preuve pour chaque nom : absent de `~/Téléchargements/EDENFR/GameDataFileList.txt` (manifeste
officiel), de `extracted/`, de `qb_out/` et de tout `corpus/fr-full` (recherche par nom de base,
tous dossiers). Lignes G13 à G17 de `docs/workflows/EDENFR-GAPS.md` (liste exacte), codées dans
`AuthenticMap` : le total des exceptions est 114 références (17 NIF, 49 `4pequip`, 28 `decorate`,
12 `c_monster`, 3 armes et 5 `monsterlist` (G12, G16)).

## Correction du résolveur (NifMaterial.cs)

Avant : `M109.dds` (absent du jeu) se résolvait en `m106.dds`, texture d'un autre monstre : la
règle « remplacer les 2 derniers chiffres par 01..09 » s'appliquait à un stem de 4 caractères
(`m1` + `06`). Après : cette règle exige un stem à variante (>= 5 caractères) ; `M109` tombe sur
le repli par préfixe et prend `M10901.dds`. Mesuré sur tout le corpus : cette seule référence
change (diff avant/après de `textures.fuzzy.tsv`), aucune autre résolution ne bouge. Le port de
l'outil a la même ligne (le script vérifie la synchro).

## Limites

- Les chemins d'import GLB historiques et `AssetCatalog.GetTexture` (png dérivés) ne sont pas
  audités : seul le chemin natif DDS l'est.
- Un repli du résolveur (506 refs, dont 497 faces hors bornes) est compté « présent » : il évite
  l'absence mais n'est pas la texture d'origine (G18).
- Les fichiers en dehors des dossiers indexés (`ui/`, `uidata/`) ne servent jamais de texture de modèle.
