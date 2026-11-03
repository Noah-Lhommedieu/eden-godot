# Maintenabilité du projet — analyse lead dev

> État au 28/10/2026. Ce doc répond à : « est-ce que je peux maintenir
> ce projet, modifier le client, ajouter/modifier des panels UI ? »

## 1. Maintenabilité du serveur C# (pour toi)

**Points forts :**

- **Une classe = une responsabilité** — le flux zone est découpé en
  `ZoneEntryFlow.{Combat,Sight,Elf,Family,Migration,…}.cs` (partial
  par domaine). Tu ne lis jamais un fichier de 5000 lignes pour
  toucher à une feature.
- **Formats réseau isolés dans des codecs** (`*Packets.cs`, `.g.cs`
  générés) — pour toucher un paquet tu vas dans le codec, pas dans la
  logique métier.
- **Chaque implémentation cite la preuve native** (`@0x…` + symbole) —
  tu peux vérifier un comportement en 1 `reindex lookup`, pas en
  devinant.
- **860 tests dont 254 golden byte-à-byte** — un codec cassé par une
  modif est détecté immédiatement.
- La DB reflète le schéma natif → quand tu sais ce que fait le natif,
  tu sais où le mettre en C#.

**Points de friction connus :**

- `ZoneMessages.g.cs` fait 31k lignes — c'est une base de layouts
  générée, pas du code à maintenir. Règle : on n'y touche pas à la
  main, on écrit un codec dédié pour les formats variables.
- `ZoneServices` est un gros record d'injection (~100 champs) — ça
  grandit avec chaque feature, acceptable mais à surveiller.
- Quelques tables maison cohabitent avec les natives
  (`guilds`/`family`, `player_friends`/`friends`) — dette à purger.
- Le reverse d'un NCID non documenté prend du temps (trouver la
  classe native → décoder → mapper) — mais la méthode est rodée.

## 2. Modifier le CLIENT — ce qui est possible

### 2.1 Modifier une UI existante — ✅ possible aujourd'hui

Toute l'UI du client `client/` est en clair en **CEGUI XML** :

| Ressource | Ce que tu peux faire |
|---|---|
| `client/UIData/layouts/*.layout` (434 fichiers) | Repositionner, redimensionner, masquer/ajouter des widgets dans **n'importe quelle fenêtre existante** (JobWnd, MissionLog, RaidWnd, inventaire…) — édition XML, pas de compilation |
| `imagesets/`, `looknfeel/`, `schemes/`, `fonts/` | Re-skin complet (couleurs, textures, polices) |
| `client/UI/*.dds`, `uiicon/`, `loadingframe/` | Icônes, écrans de chargement (DDS via GIMP/Paint.NET) |
| `client/themes/` | Thèmes alternatifs déjà présents |
| `T_*.ini` | **Tous les textes** — traduction/renommage complet |

**La limite** : un `.layout` seul ne peut créer que ce que le code C++
du client sait afficher. Un nouveau bouton qui appelle une fonction
inexistante ne fera rien. Modifier l'apparence/composition = OK ;
ajouter un comportement = non, sans code.

### 2.2 Ajouter un NOUVEAU panel — ⚠️ possible mais il faut le hook

Le natif charge les fenêtres depuis le code. Pour **créer un panel
inédit** (ex. un panneau de stats custom, un HUD supplémentaire) il
faut injecter du code client :

- **La voie propre : une `d3d9.dll` proxy** — exactement ce que fait
  Reforged (son `d3d9.dll` forward Direct3D et patch la mémoire au
  runtime ; `eden_hook.log` liste 43 features installées ainsi). On a
  `rf_d3d9` **déjà décompilé** dans `re/ghidra/rf_d3d9/` → on peut
  écrire notre propre proxy en C++/MinGW qui :
  1. charge les vrais appels D3D9,
  2. patche des bytes en mémoire (hooker une fonction client),
  3. charge un dossier `mods/` de features comme Reforged
     (`gameFix.dll`, `ItemMall.dll`, `NeoInventoryBank.dll` existent
     déjà dans `mods/_testing/`).
- Avec ce hook, tu peux **instancier une fenêtre CEGUI depuis ton
  layout .layout**, binder des événements à des NCIDs serveur, et donc
  avoir un **vrai nouveau panel fonctionnel**.

**Effort** : construire le proxy d3d9 = un projet C++ ciblé (le modèle
est décompilé). Ensuite chaque panel = un `.layout` + du Lua/du hook.

### 2.3 Modifier la logique client — ⚠️ patch de bytes

Corriger/modifier une fonction existante du client (un check, une
constante, un comportement) passe par :
- **patch mémoire** via le proxy d3d9 (reversible), ou
- **patch disque** de `game_.exe` (risqué, le moindre offset faux
  casse tout — à réserver aux sauts identifiés dans le décompilé).

Le décompilé `client_game/` (~500k lignes pseudo-C) + `rf_game/` sont
disponibles pour localiser les fonctions à hooker.

### 2.4 Reforged spécifiquement

- Assets packés dans `pkg/pkg001-029.pkg` — extraction via
  **QuickBMS + `aeriagames.bms`** (déjà dans le repo). Réinjection =
  reimport QuickBMS.
- Reforged = notre référence de « ce qui est moddable » : il prouve
  qu'on peut patcher UI + logique sans recompiler le client.

### 2.5 Édition du contenu (maps, mobs, drops, textes)

Déjà faisable **à la main** (skill `eden-content-editing` +
`TUTO-CLIENT.md`) — tout est texte `.ini`/CSV en Big5. Un éditeur
graphique (`Eden.Tools`, web app réutilisant nos parsers `Eden.Core.Data`)
est proposé dans `docs/ANALYSE-MODDING-ET-OUTILS.md` §B — ce serait
l'outil pour poser des mobs/NPC/éditer drops en visuel.

## 3. Verdict

| Demande | Faisable ? | Comment |
|---|---|---|
| Modifier une fenêtre UI existante | ✅ oui | éditer le `.layout` XML |
| Re-skin / traduire tout le client | ✅ oui | imagesets + `T_*.ini` |
| Ajouter un nouveau panel avec logique | ⚠️ oui via hook | `d3d9.dll` proxy + `.layout` + bind NCID |
| Modifier une fonction client | ⚠️ oui via hook/patch | proxy d3d9 ou patch de bytes localisé |
| Réécrire le client | ❌ pas réaliste | mois de travail — seulement si besoin |
| Éditer maps/mobs/drops | ✅ oui | fichiers `.ini` (éditeur à construire) |

**La brique qui manque** pour les panels custom : le proxy `d3d9.dll`
maison (modèle `rf_d3d9` déjà décompilé). Une fois écrit, ajouter un
panel devient un exercice `layout + hook`.
