# Limites et pièges connus (moteur, plateforme, données)

Chaque entrée : statut, version, date.

## Godot 4.8-beta2

- **verified 2026-11-02** — Ces erreurs existaient **déjà en 4.7.2**, la
  4.8 n'en ajoute pas : 122 × `Basis must be normalized in order to be casted
  to a Quaternion` au chargement d'S001 (`AssetCatalog.SanitizeRestPoses`) ;
  fuites RID et `PagedAllocator` à la sortie.
- **verified** — Au premier `--import` d'un projet sans `.godot/`, le thème
  `eden_default_theme.tres` est chargé avant l'import de `FONT01.ttf`
  (erreurs). Un second import est propre.
- **verified** — `--headless` charge la scène et le C#, mais ne prouve
  aucun rendu (RendererDummy).
- **verified** — `--gpu-profile` affiche le temps GPU par passe dans
  stdout : c'est la méthode pour séparer le coût CPU du coût GPU.

## C# / .NET 10

- **verified** — Ordre d'initialisation statique : les initialiseurs de
  propriétés statiques s'exécutent **avant** le constructeur statique. Un
  `Encoding.GetEncoding(1252)` placé en initialiseur lève une exception,
  car `RegisterProvider` n'a pas encore tourné. Il faut l'assigner dans le
  constructeur statique.
- **verified** — `System.Text.Encoding.CodePages` est inclus dans net10
  (NU1510) : ne pas le référencer.

## Scripts shell

- **verified** — Sous `set -o pipefail`, `cmd | head -1` peut tuer `cmd`
  par SIGPIPE. Le script sort alors **en silence** (vu sur `unzip -Z1 | head`).
  Utiliser `sed -n '1p'`, qui lit tout le flux.

## Plateforme et disque

- **verified** — ext4 sans reflink. `chattr +F` (casefold) refusé : le
  système de fichiers n'a pas la fonction et il faudrait root. La casse se
  gère donc dans le code (ADR-0002).
- **verified** — Les liens physiques partagent l'inode : écrire en place
  (`open("wb")`, `shutil.copy2`, `PIL.save`) modifie aussi l'original.

## Données EDENFR

- **verified** — fr-full contient des dossiers jumeaux de casses
  différentes (`map/model/s002/` contient le `.fsm`, `map/model/S002/` le
  `.area`/`.ini`). Une résolution « premier match » échoue : il faut un DFS.
- **verified** — FSM V7 (~15 maps, dont s002) : queue non framée après
  LAY0, et `RecordCount` qui englobe des blocs non-tuile. Le `FsmFile` issu
  de Flax gère ce cas ; la grille régulière garde la lecture séquentielle.
- **verified** — La capture `--shot` (~240 frames) montre ou non l'eau
  animée d'S001 selon le timing, de façon non déterministe. A/B confirmé.
  Refaire la capture avant de conclure à une régression.
