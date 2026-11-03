# Analyse — modding des clients + outils d'édition de contenu

> 18 Oct. Sources : arborescences des deux clients, `re/ghidra`
> (client_game, rf_d3d9, rf_game…), `docs/QUICKBMS-USAGE.md`,
> `docs/TUTO-CLIENT.md`, `eden_hook.log` Reforged.

## A. Modding des clients — ce qui est possible

### A.1 Sans toucher au binaire (faisable aujourd'hui)

Le client `client/` expose **toute son UI en clair** :

| Chemin | Contenu | Modification |
|---|---|---|
| `client/UIData/layouts/` | **434 fichiers `.layout` XML CEGUI** — chaque fenêtre (JobWnd, MissionLog, RaidWnd, CreateCharacter…) | ✅ positionner/redimensionner/masquer des widgets, ajouter des fenêtres référencées |
| `client/UIData/imagesets/`, `looknfeel/`, `schemes/`, `fonts/`, `animations/` | skins, polices, images | ✅ re-skin complet (10 thèmes d'imagesets déjà présents) |
| `client/UIData/lua_scripts/` | `main.lua` CEGUI | ⚠️ hook CEGUI : scripts appelés par les layouts — permet de la logique UI légère |
| `client/UI/` | `title.dds`, `loadingframe/*.dds`, icônes `itemicon/skillicon/uiicon` | ✅ écrans de chargement, icônes (DDS = Paint.NET/GIMP plugin) |
| `client/themes/` (Blue/Coffee/Green/Purple/Red) | thèmes de l'UI | ✅ thèmes alternés |
| `T_*.ini` (Big5) | **tous les textes** : noms, dialogues, descriptions | ✅ localisation complète (déjà fait pour la traduction) |
| `data/scene/sXXX.ini`, `connect.ini`, `client.ini`, `User.ini` | logique scène, config | ✅ |

→ **Modification d'UI = édition XML/INI, pas de recompilation.** On peut
refaire des fenêtres, repositionner, créer des layouts. Limite : les
*événements* côté code natif (un bouton qui appelle une fonction C++
inexistante ne fera rien) — on modifie l'UI existante, on ne crée pas de
nouvelle logique native.

### A.2 Patching binaire (comme Reforged)

Le client Reforged montre la voie : `eden_hook.log` liste **43 features**
installées par `d3d9.dll` (strengthen20 = « 24 patches applied »,
buff_panel_resize, pet_limit…) — **des patches de bytes en mémoire au
runtime**.

Pour nous, deux niveaux :

1. **Patch disque** de `game_.exe` : modifier des bytes (sauter un check,
   changer une constante). Risqué (hash/casse au moindre offset faux) —
   à réserver aux petits sauts déjà identifiés dans le décompilé.
2. **`d3d9.dll` proxy** (= le modèle Reforged) : une DLL `d3d9.dll` posée
   à côté de `game_.exe` est chargée avant la vraie ; elle forward les
   appels Direct3D et patch le code en mémoire. **C'est le vecteur propre
   pour les mods de logique** — réversible (supprimer la DLL = retour
   vanilla), pas de modification du binaire.

`rf_d3d9` est déjà décompilé (`re/ghidra/rf_d3d9/decompiled.c`) → on
peut s'en inspirer pour écrire **notre propre `d3d9.dll`** (C++, MinGW-w64)
qui charge un répertoire `mods/` de features (comme Reforged).

### A.3 « Code source complet » — réalité

- Décompilation Ghidra = **pseudo-C non compilable** (12,7 Mo, ~500k lignes).
  Reconstituer un `game_.exe` compilable from scratch = des mois, peu réaliste.
- Alternative sérieuse à terme : **client réécrit** (Unity/Godot + assets
  extraits `.nif/.dds` — Gamebryo = NIFlisible par NifSkope/convertisseurs).
  C'est un projet à part entière — à considérer seulement si les limites
  des §A.1/A.2 deviennent bloquantes.

### A.4 Client Reforged : données packées

Tout est dans `EdenReforged/pkg/pkg001-029.pkg` (format Aeria `.pkg` +
`pkg.idx`). Extraction : **QuickBMS + `aeriagames.bms` déjà dans le repo**
(`quickbms/`, procédure dans `QUICKBMS-USAGE.md`). Réinjection : QuickBMS
mode `reimport` ou repack — à tester sur un pkg non critique.

### A.5 Outillage modding (recommandé)

| Besoin | Outil | État |
|---|---|---|
| Analyser le binaire | Ghidra headless (`re/decompile.sh`) | ✅ déjà fait (client_game, rf_game) |
| Patch mémoire / logique | `d3d9.dll` proxy C++ (MinGW-w64), modèle rf_d3d9 | à construire |
| Debug runtime | x64dbg (32-bit), Cheat Engine | à installer sous Windows/Wine |
| Extraction pkg Reforged | `quickbms/` + `aeriagames.bms` | ✅ dans le repo |
| Textures | GIMP/Paint.NET + plugin DDS, NifSkope (.nif/.kfm) | léger |
| UI/layouts | éditeur XML + connaissance CEGUI | aucun outil requis |

## B. Outils d'édition de contenu — faisabilité et proposition

### B.1 Tout est texte — l'éditeur est faisable

Les données de gameplay sont des `.ini` décoded/CSV + sections scène :

| Donnée | Fichier | Format |
|---|---|---|
| Spawns/respawn/patrol par map | `server_us/Data/scene/SXXX.ini` `[reborn_monsters]` | CSV (id, monstre, ordre, n, respawn, x,y,z, patrol, area, events) |
| NPC posés | `SXXX.ini` `[npc]` | CSV (uid, id, x, y, dir, area, nom) |
| Téléporteurs | `SXXX.ini` `[gateway_area]` | CSV |
| Events | `SXXX.ini` `[event]`, `[event_area]` | CSV + références |
| Drops normaux/class/BP | `S_DropItem.ini`, `S_ClassDropItem.ini` | table id→items+taux |
| Monstres/items/NPC/sorts | `S_Monster/S_Item/S_Npc/S_Spell` (+noms `T_*`) | `|V.x|N|` Big5 |
| Quêtes/dialogues | `S_Mission.ini`, `T_Dialogue.ini` | langage de quêtes |

→ un éditeur graphique **n'a qu'à parser/écrire du CSV/INI** — aucun format
binaire à casser. **Nos parsers `Eden.Core.Data` existent déjà.**

### B.2 Proposition : `Eden.Tools` — suite d'éditeurs

Recommandation : **app web locale** (ASP.NET minimal + front léger,
servie par `dotnet run`, navigateur = UI). Pourquoi :

- Réutilise `Eden.Core.Data` **tel quel** (parsers déjà écrits/testés —
  zéro duplication de format).
- Multi-écran (map d'un côté, tables de l'autre) — meilleure UX qu'un
  formulaire WinForms/Avalonia.
- Alternative si tu préfères du desktop natif : **Avalonia** (même stack
  C#, une fenêtre, canvas pour la map).

Modules par priorité (valeur immédiate → avancée) :

1. **Map viewer + Spawn editor** — rendu 2D de la map (la minimap
   `UI/map*.dds`/`.fsm` → image), markers des `[reborn_monsters]`/`[npc]`
   posés par coordonnées x/y ; drag pour déplacer, clic pour ajouter un
   mob d'une liste (`S_Monster` + nom `T_Monster`), édition respawn/
   patrol/area, sauvegarde `SXXX.ini` **+ `sXXX.ini` client en synchro**.
2. **Drop editor** — par monstre : tables `S_DropItem`/`S_ClassDropItem`
   (normal / class drop / BP), ajout d'item avec taux, aperçu du pool.
3. **Monster/Item/NPC editor** — champs typés de `S_*` + nom `T_*`
   (Big5 géré), duplication d'une ligne existante comme modèle.
4. **Shop/Store editor** — `S_Store` + contenus.
5. **Quest/Dialog editor** (phase avancée) — `S_Mission` + `T_Dialogue` :
   le langage de quêtes est à visualiser (arbre étapes/options) — plus
   complexe, mais le plus utile à terme.

### B.3 Ce qui n'existe pas ailleurs

Pas d'éditeur open-source Eden Eternal publié trouvable (projet de niche,
communautés fermées). Les éditeurs « officiels » étaient des outils
internes X-Legend. → **la voie réaliste est notre propre outil**, qui a
l'énorme avantage de réutiliser nos parsers déjà fidèles au format natif.

### B.4 Point d'attention format

- **Encodage Big5/GBK** obligatoire en écriture (le client lit du Big5) —
  un éditeur qui réenregistre en UTF-8 casse l'affichage. Le writer doit
  sortir en Big5.
- `server_us/Data/scene/SXXX.ini` **et** `client/data/scene/sXXX.ini`
  doivent rester synchronisés (l'outil écrit les deux).
- Versionner/sauvegarder (`.bak`) avant toute écriture.

### B.5 Quick-win immédiat (sans outil)

Le skill `eden-content-editing` + `TUTO-CLIENT.md` permettent déjà d'éditer
ces fichiers à la main — l'éditeur viendra **automatiser** ce qui se fait
déjà (spawn, drop, items) avant d'attaquer les quêtes.
