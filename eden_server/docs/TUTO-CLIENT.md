# TUTO CLIENT Eden — fonctionnement, modification, ajout de contenu

> Comment le client `client/` fonctionne, ce qui est modifiable sans recompiler, et comment
> faire aider l'IA pour ajouter du contenu (maps, monstres, items, NPC…).
> Client : `game_.exe` **PE32 Windows x86**, moteur **Gamebryo + CEGUI**. Il se lance déjà via
> **Lutris/Proton** sur votre Nobara.

---

## 1. Vue d'ensemble : le client est piloté par la DONNÉE, pas par le code

Bonne nouvelle : **90 % du contenu est dans des fichiers de données** (`.ini`, `.nif`, `.fsm`,
`.dds`, `.wav`…). Ajouter une map/un monstre/un item ne demande **pas de recompiler `game_.exe`**.
On édite/copie des fichiers, on relance.

### Arborescence `client/`

| Dossier | Contenu | Modifiable ? |
|---|---|---|
| `data/scene/` | Logique des scènes (`sXXX.ini` + `sXXX.csm`) | ✅ oui (téléporteurs, bornes) |
| `map/model/SXXX/` | Visuel 3D d'une map (`.nif`, `.fsm`, `.area`) | ⚠️ cloner un existant (3D) |
| `map/sknodemap/` | Navigation (`SXXX.smp/.pgp/.fsmp`) | ⚠️ régénérer si nouvelle map |
| `map/texture/` | Textures `.dds` | ✅ oui |
| `monster/` | Modèles/animations de monstres | ⚠️ assets 3D |
| `char/` | Personnages joueurs (`.kfm`, animations) | ⚠️ assets 3D |
| `item/`, `effect/`, `decorate/`, `house/` | Icônes/effets/décos | ✅ selon type |
| `bgm/`, `Movies/`, `CutScene/` | Musiques (`.wav/.ogg`), vidéos | ✅ remplaçable |
| `*.ini` racine | Config (`client.ini`, `connect.ini`) | ✅ oui |

> **Légende** : ✅ = simple (texte/data) · ⚠️ = nécessite des assets 3D existants à cloner.

---

## 2. Le duo CLIENT ↔ SERVEUR (essentiel)

Certains fichiers existent **des deux côtés** et doivent rester cohérents :

```
server_us/Data/scene/S004.ini   ← logique de la scène (serveur : spawn, téléporteurs)
client/data/scene/s004.ini      ← même logique (client : rendu, zones)
```

**Exemple concret** (extrait réel de `S004.ini`) :
```ini
[globe_data]
S004.fsm,576,576,V7,          # nom du fichier map + taille du monde + version
[born_area]                    # zones d'apparition
[revive_area]
1,11,27,                       # revive_area : id,x,y
[gateway_area]                 # téléporteurs : id, nodeX, mapId, mapNode, targetId, "nom"
1,1,102,90112,102,"NomDeZone",
...
```

> ⚠️ **Encodage** : les noms de zones sont en **Big5/GBK** (chinois traditionnel). C'est pourquoi
> vous voyez des caractères illisibles dans un terminal UTF-8. **Ne réenregistrez PAS** ces
> lignes en UTF-8 : l'affichage en jeu casserait. Pour du nouveau texte, préférez l'anglais/ASCII
> et testez le rendu.

---
## 3. Ajouter du contenu — procédures par type

### 3.1 Ajouter un MONSTRE (le plus simple)
1. **Stats/nom** : éditer `server_us/Data/db/T_Monster.ini`.
   Format : 1re ligne = nombre de champs, 2e ligne = colonnes (ex. `1|3`). Puis chaque
   monstre = `id|nom`. Pour ajouter :
   ```
   29999|Mon Super Monstre
   ```
2. **Apparence** : réutiliser le modèle d'un monstre existant (`client/monster/`) → pas d'asset
   3D à créer. Le lien apparence↔monstre se fait via les champs d'apparence (souvent en base ou
   `T_*.ini`) → à cartographier via le reverse (skill `eden-reverse-serveur`).

### 3.2 Ajouter un ITEM
`server_us/Data/db/T_Item.ini`. Format :
- Ligne 1 : nombre de lignes. Ligne 2 : **schéma** (`1|8|35` = id, nom, description).
- Chaque item : `id|nom|description`. La description utilise des **codes couleur** `$7$`, `$12$`
  et des sauts de ligne `\n`.
  ```
  500001|Épée de Test|$7$"Une épée créée pour tester."\n\n$12$Note: Item de test.|
  ```
- **Icône** : composer l'icône dans `client/item/` et la référencer si le champ existe.

### 3.3 Ajouter/modifier une MAP
- **Logique seule** (téléporteurs, spawn) : éditer `sXXX.ini` (§2). Rapide, sans 3D.
- **Nouvelle map visuelle** (lourd) :
  1. Cloner `client/map/model/S009/` → `S999/`, renommer les fichiers en `S999.*`.
  2. Remplacer le `.nif` (mesh Gamebryo) et les `.dds` (textures).
  3. Régénérer la navigation (`S999.smp/.pgp/.fsmp`) — difficile sans outils d'origine ; en
     pratique on **clone une map existante** et on modifie ses zones.
  4. Éditer `client/data/scene/s999.ini` **et** `server_us/Data/scene/S999.ini` (les deux !).
  5. Déclarer la map dans la table `worlds` (PostgreSQL) si c'est un monde jouable.

> 🎯 **Réaliste** : cloner/modifier une map existante est faisable. Créer une map 3D from scratch
> demande les outils Gamebryo d'origine (probablement perdus) → à considérer comme avancé.

### 3.4 Modifier la CONFIG RÉSEAU (pointer vers votre serveur)
- `client/connect.ini` : `Server=<IP>,6543`
- `client/connects.ini` : canaux `y01..y04` → `<IP>,6544…6551`
Mettez `127.0.0.1` (ou l'IP LAN) pour viser votre serveur local.

---

## 4. Faire aider l'IA (workflow)

Grâce à la **skill `eden-content-editing`** (voir `docs/SKILLS-IA.md`), l'agent connaît déjà les
règles. Exemples de demandes dans Cline :

> « Ajoute un monstre d'ID 29999 nommé "Dragon de Test" dans `T_Monster.ini`, en copiant le
> format d'un monstre existant. Sauvegarde le fichier avant. »

> « Crée une zone de téléportation dans `s004.ini` qui mène à la map 101. Respecte le format
> `gateway_area` et l'encodage. »

L'IA doit : sauvegarder (`.bak`), respecter l'encodage Big5/GBK, garder client/serveur en phase,
et vous indiquer comment tester.

---

## 5. Tester une modification

1. Sauvegarder l'original (`.bak`).
2. Appliquer la modification.
3. Lancer le client via **Lutris/Proton**.
4. Observer les logs du dossier `client/` : `CEGUI.log`, `error.log`, `main.log`, `GameTemp.log`.
   - Un `.ini` mal formé → erreur de parsing dans ces logs.
5. Selon le cas, redémarrer aussi le serveur (données partagées).

---

## 6. Limites du client (à connaître)

- **Recompilation** : impossible sans les sources d'origine (C++ Gamebryo/CEGUI, perdues). On
  modifie la **donnée**, pas le binaire.
- **Assets 3D** : créer du contenu 3D neuf demande des outils Gamebryo (.nif/.kfm) d'époque.
  Le contournement = **réutiliser/cloner** des assets existants.
- **Protocole réseau** : tout ce qui est échangé avec le serveur passe par le protocole (§4 du
  rapport). Ajouter du contenu *réseau* (nouveaux messages) exige le reverse du protocole.

---

## 7. Récap des fichiers de données clés

| Fichier | Rôle | Éditable |
|---|---|---|
| `server_us/Data/db/T_Monster.ini` | Liste monstres (id\|nom) | ✅ |
| `server_us/Data/db/T_Item.ini` | Items (id\|nom\|desc, codes couleur) | ✅ |
| `server_us/Data/db/T_NPC.ini` | PNJ (id\|nom) | ✅ |
| `server_us/Data/db/T_Dialogue.ini` | Dialogues (volumineux) | ⚠️ |
| `server_us/Data/scene/SXXX.ini` | Logique scène (spawn/tp) | ✅ (client aussi) |
| `client/connect.ini`, `connects.ini` | IP/ports serveur | ✅ |
| `client/map/model/SXXX/` | Visuel 3D map | ⚠️ cloner |

---

## 8. Schéma d'un fichier scène `.csm` (client)

`client/data/scene/sXXX.csm` contient la config de rendu (extrait réel) :
```ini
[Version]
Version=20111222
[CSM]
// Max Shadow Distance
...
```
Associé à `sXXX.ini` (logique) et `map/model/SXXX/` (3D) + `map/sknodemap/SXXX.smp` (navigation).
Une scène complète = ces 4 éléments synchronisés.

