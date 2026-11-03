# Rapport complet — Reverse Engineering du serveur Eden (`server_us`)

> **Objet** : analyser le serveur de jeu *Eden Eternal* (`server_us`), produire un plan
> d'action, une analyse de la stack, un tutoriel de reverse engineering assisté par IA
> (100 % local, sans IA cloud), et un plan d'outillage (décompilation pilotable par IA,
> IDE, conteneurisation Docker). Ce document est le livrable de la discussion.

- **Dossier analysé** : `<dépôt>`
- **Repo git** : `github.com/Noah-Lhommedieu/eden_server`
- **Outillage IA déjà présent dans le repo** : `rea-rea-agents-6.5.0` (projet **REA**)
- **Date d'analyse** : 14/10/2026

---

## Sommaire

1. [Résumé exécutif](#1-résumé-exécutif)
2. [Analyse complète de la stack](#2-analyse-complète-de-la-stack)
3. [Architecture fonctionnelle du serveur](#3-architecture-fonctionnelle-du-serveur)
4. [Le protocole réseau et le chiffrement](#4-le-protocole-réseau-et-le-chiffrement)
5. [Plan d'action (reverse vers serveur propre C#)](#5-plan-daction-reverse-vers-serveur-propre-c)
6. [Tutoriel complet : reverse d'un serveur comme celui-là](#6-tutoriel-complet--reverse-dun-serveur-comme-celui-là)
7. [IA locale (100 % hors cloud) pour le reverse](#7-ia-locale-100--hors-cloud-pour-le-reverse)
8. [Outils de décompilation pilotables par IA (Ghidra & co)](#8-outils-de-décompilation-pilotables-par-ia-ghidra--co)
9. [Le dossier `rea-rea-agents-6.5.0` (REA) — comment l'utiliser](#9-le-dossier-rea-rea-agents-650-rea--comment-lutiliser)
10. [IDE & extensions VS Code / Cursor pour le reverse](#10-ide--extensions-vs-code--cursor-pour-le-reverse)
11. [Conteneurisation Docker](#11-conteneurisation-docker)
12. [Reverse du client de jeu (phase ultérieure)](#12-reverse-du-client-de-jeu-phase-ultérieure)
13. [Risques, légal et limites](#13-risques-légal-et-limites)
14. [Annexes — inventaires extraits du binaire](#14-annexes--inventaires-extraits-du-binaire)

---

## 1. Résumé exécutif

**Bonne nouvelle majeure** : ce serveur est *particulièrement* favorable au reverse
engineering, bien plus que la moyenne des binaires « serveur privé de MMO ».

| Constat | Détail | Impact |
|---|---|---|
| **Non strippé** | Les 7 binaires conservent leur table de symboles `.symtab` | On récupère **tous les noms de fonctions/variables** |
| **Debug info DWARF** | Sections `.debug_info`, `.debug_line`, `.debug_str`, etc. | On récupère les **types, structures, et les noms de fichiers source `.cc`** |
| **C++ avec symboles manglés** | `lapis::CAIFSMFlee`, `CCharacter`, `CNE_G_...` | Les classes du moteur sont lisibles telles quelles |
| **Statiquement lié** | Pas de `.so` dynamiques à résoudre | Analyse en un seul fichier |
| **Logs verbeux** | Les logs révèlent fichiers source, ports, protocole | Compréhension terrain sans rien exécuter |

Autrement dit : **on n'est pas dans un reverse « à l'aveugle »**. On a un binaire avec
symboles + DWARF, ce qui se rapproche d'une reconstruction assistée par IA très efficace.
Le moteur interne s'appelle **`lapis`** (C++, GCC ancien). La persistance est
**PostgreSQL**, le réseau est **TCP + epoll**, le chiffrement est **RSA/OpenSSL**.

**Verdict techno pour la reconstruction** : la techno d'origine (C++ bas niveau, GCC
4.1/4.3, 32 bits, API socket/`epoll` maison) est **trop ancienne et trop bas niveau** pour
être reprise telle quelle. Un portage propre en **C# / .NET 8** (votre préférence) est
justifié et cohérent : réseau asynchrone, accès PostgreSQL, sérialisation, structures de
données — tout se transpose proprement.

> **Recommandation** : ne *recompilez pas* le C++. **Ré-écrivez** un serveur propre en C#
> dont vous extrayez le **comportement** et le **protocole** depuis les binaires (via IA +
> Ghidra + DWARF), en ciblant la compatibilité client (voir §4 et §12).

---
## 2. Analyse complète de la stack

### 2.1 Identification des binaires (preuves `readelf`/`file`)

| Serveur | Taille | Format | Notes |
|---|---:|---|---|
| `TicketServer` | ~4 Mo | ELF 32-bit LSB, **x86 (i386)**, statique, non strippé, DWARF | Compilé pour GNU/Linux 2.4.1 (le plus ancien) |
| `GatewayServer` | ~2,2 Mo | ELF 32-bit x86, statique, DWARF | GNU/Linux 2.6.8 |
| `LoginServer` | ~2,3 Mo | ELF 32-bit x86, statique, DWARF | GNU/Linux 2.6.18 |
| `MissionServer` | ~7,4 Mo | ELF 32-bit x86, statique, DWARF | Logique de jeu (missions, familles, colisée…) |
| `WorldServer` | ~7,1 Mo | ELF 32-bit x86, statique, DWARF | Monde global, personnages |
| `ZoneServer` | ~14,7 Mo | ELF 32-bit x86, statique, DWARF | Le plus gros : logique de zone, IA, combat |
| `HTTPAServer` | ~3,4 Mo | ELF 32-bit x86, statique, DWARF | HTTP + API + `oauth2_new.php` (portail/billing) |

**Langage** : **C++** (confirmé par 58 000+ symboles manglés `_Z…` dans `ZoneServer`).
**Compilateur** : GCC Debian **4.1.2**, **4.3.2**, **4.4.5** (voir section `.comment`).
**Bibliothèques statiques embarquées** : **OpenSSL 0.9.8o** (RSA/chiffrement), **libpq**
(PostgreSQL), **zlib**, **boost** (`boost::function`), **pthread/NPTL**.

### 2.2 Le moteur interne : `lapis`

Tout le code applicatif vit dans le **namespace `lapis`** (moteur/framework maison). Exemples
de classes retrouvées (extraction directe des symboles) :

- Réseau : `lapis::CMSSocketManager::EPoll`, `CCWTCPChannel`, `CCWTCPService`, `MSSocketManager`
- Sérialisation : `lapis::CBinStream`, `CInBinStream`, `COutBinStream`
- Logs : `BasicLogger`, `CLogger`, `LogFactory`, `AdvLog`
- DB : `CAccountDBO`, `PGDatabase`, `PGAccountDBO`, `CCharacterDBO`, `DBAgent`
- Jeu : `CCharacter`, `CFamily`, `CLottery`, `CCaptcha`, `CFortuneBag`, `CFormula`
- Commandes réseau : `CNE_*` (Net Event) et `NC_*` (Net Command), ex. `CNE_G_ServerAccountLogin`
- IA (ZoneServer) : `CAIFSMFlee`, `CAIFSMHunt`, `CAIFSMIdle`, `CAIFSMRoam`, `CAI` (machine à états)

### 2.3 Persistance : PostgreSQL 12

- Fichier `database.out` (≈ 5,8 Mo) = dump SQL complet.
- **4 bases** : `fnaccount` (comptes), `fndb1` (jeu/monde), `fnmember`, `fnmember1`.
- **~100+ tables**. Exemples : `accounts`, `worlds`, `gm_tool_accounts`, `character_number`,
  `bags`, `elf`, `equipment1/2`, `collection`, `itemmall`, `lottery`, `auction`, `family`,
  `battlefield_career`, `colosseum_ranking`, `classpk_rank`.
- Accès via **libpq**, identifiants en clair dans `setup.ini` (à externaliser en variables
  d'environnement dans la future stack Docker).

### 2.4 Données de jeu (fichiers `.ini`)

Dossier `server_us/Data` :

- `Data/db/*.ini` : **tables de gameplay** en texte. Volumineuses : `T_Item.ini` (3 Mo),
  `T_Dialogue.ini` (5 Mo), `T_ItemMall.ini` (2 Mo), `T_Enchant.ini`, `T_Monster.ini`,
  `T_Spell.ini`, `T_Class.ini`, `T_Mission.ini`…
- `Data/scene/S*.ini` : **centaines de maps/zones** (`S101.ini`, `S110.ini`…).
- `Data/NodeMap/`, `Data/itemmall/` : navigation et boutique.

> ⚠️ **Point crucial** : les logs montrent que le serveur tente de charger `T_Level.ini`,
> `T_Ai.ini`, `T_DropItem.ini`, `T_SpellReference.ini`… en **`[ERROR] Can't Open File`**.
> Une partie des tables `T_*.ini` attendues **manque** dans ce dump. Un serveur propre devra
> soit les fournir, soit rendre le chargement tolérant aux absents.

### 2.5 Stack logicielle d'exploitation actuelle

- **Base** : PostgreSQL 12 via `docker compose` (`compose.yml`, `network_mode: host`).
- **Démarrage** : script `./start` (enchaîne Ticket→Gateway→Login→Mission→World→Zone avec
  `sleep 5`, en forçant la locale `LC_ALL=C`).
- **Ports** (depuis le README) : `2067` (Ticket), `5560` (Billing/Gateway), `10465` (GMTool),
  `20205` (CGI), `5712` (world channel), `10022`, `8888`, `7777`, `40001`, etc.
- **Client** : attendu côté Windows (`easyfun.bat`, `connect.ini`), IP à patcher — **non
  présent** dans ce dossier (voir §12).
- **Patch d'IP** : `patchIp.sh` remplace des IP ASCII dans `WorldServer`/`ZoneServer`.

### 2.6 Synthèse de la stack

```
┌──────────────────────────────────────────────────────────────┐
│  CLIENT (Windows, non fourni)  ──TCP/RSA──┐                    │
└───────────────────────────────────────────┼──────────────────┘
                                            ▼
 TicketServer ── GatewayServer ── LoginServer ── MissionServer
   (tickets)       (routeur)       (auth/comptes)   (logique MMO)
                                            │
                                            ▼
                        WorldServer ──── ZoneServer (IA, combat)
                                            │
                     HTTPAServer (portail/billing/CGI/oauth2)
                                            │
                    PostgreSQL 12  (fnaccount / fndb1 / fnmember…)
```

**Langage** C++ 32 bits · **Build** GCC 4.1–4.4 statique · **DB** PostgreSQL ·
**Net** TCP+epoll+RSA/OpenSSL · **Config** fichiers `.ini` · **Moteur** `lapis`.

---

## 3. Architecture fonctionnelle du serveur

Chaque binaire est un **processus distinct** lancé séparément (pas un monolithe) :

| Rôle | Responsabilités (déduites des symboles/logs) |
|---|---|
| **TicketServer** (`-p 2067`) | Délivre des tickets de session. Composant d'entrée le plus ancien. |
| **GatewayServer** | Routeur/pivot. Écoute TCP `0.0.0.0:5560`, gère epoll, relaie entre client et serveurs internes. Logs : « Login Server Connected! Version: [1-6-7-VIP] ». |
| **LoginServer** | Authentification des comptes (`NC_G_ClientAccountLogin`). |
| **MissionServer** | Cœur logique MMO : missions, quêtes, familles, colisée, territoire, mail, auctions, loterie. Le plus bavard (`Mission.log`). |
| **WorldServer** | Monde global : personnages, mondes/channels, `WorldServerID=1010`. Table `worlds`. |
| **ZoneServer** | Zone de jeu instanciée : IA (`CAIFSM*`), combat, objets, sorts, scènes. `ZoneServerID=1011`. |
| **HTTPAServer** | HTTP/API/portail : billing, `oauth2_new.php`, commandes CGI (`CGIPort=20205`). |

**Communication inter-serveurs** : messages `NE_GJ_*` (Gateway↔serveurs de jeu) et
`NC_*`/`CNE_*` (commandes et événements réseau) portés par des sockets TCP avec epoll.

**Orchestration** : purement par scripts bash (`start`/`stop`), pas de supervisor. Point
faible à corriger dans la future stack (healthchecks, redémarrage auto — voir §11).

---

## 4. Le protocole réseau et le chiffrement

### 4.1 Transport

- **TCP** avec **epoll** (multiplexage non bloquant) — *pas* UDT par défaut
  (`setup.ini` → `UseUDT=0` ; le code supporte toutefois UDT si le client l'exige).
- Journalisation Gateway typique : `Server is rolling on TCP 0.0.0.0:5560` → `Add New Epoll`
  → `connected from 127.0.0.1` → `connection … encrypted`.
- Handshake chiffré : `RSA size(n) = 256 size(e) = 2` (RSA 1024 bits, exposant public 2).
  OpenSSL 0.9.8o est lié en statique.

### 4.2 Sérialisation — `BinStream`

Le moteur `lapis` sérialise via `CBinStream` / `CInBinStream` / `COutBinStream`. Chaque
message `CNE_*` expose :

- `Serialize(COutBinStream)` → écrit les champs dans l'ordre,
- `Deserialize(CInBinStream)` → relit les champs,
- `Execute()` → traitement métier,
- `GetNCID()` → **identifiant numérique d'opcode**.

> **Le filon pour la reconstruction** : `GetNCID()` de chaque message donne la table des
> **opcodes**. L'ordre des champs dans `Serialize` donne le **format binaire**. C'est
> exactement ce qu'il faut recréer pour un serveur C# **compatible avec le client**.

### 4.3 Conséquence pour un portage C#

Pour qu'un serveur C# parle au client d'origine, il faut reproduire :
1. Le **handshake RSA** (taille de clé, padding, échange de clé de session).
2. La **table des opcodes** (`GetNCID` de chaque `CNE_*`).
3. Le **format de trame** `BinStream` (endianness, tailles, encodages des strings).
4. Le **chiffrement de session** éventuel après le handshake.

C'est le reverse le plus fin, et celui où l'IA + Ghidra aident le plus.

---

## 5. Plan d'action (reverse vers serveur propre C#)

Objectif : passer d'un serveur C++ ancien (binaires seuls) à un **serveur C#/.NET 8 propre,
conteneurisé, maintenable**, compatible avec le client d'origine. Approche en 7 phases.

### Phase 0 — Cadrage & sauvegarde (1 jour)
- Repartir du repo `eden_server`, créer une branche `reverse`.
- **Sauvegarder les binaires d'origine** (lecture seule) ; ne jamais patcher les fichiers de
  référence (conserver `WorldServer.bak`, `ZoneServer.bak`).
- Installer l'outillage (§7–§10) et valider que tout tourne en local (Ghidra + MCP + Ollama).
- **Livrable** : environnement de reverse reproductible + documentation de setup.

### Phase 1 — Cartographie statique assistée par IA (3–5 jours)
- Charger chaque binaire dans **Ghidra** puis dans **REA** (MCP). Objectif : faire nommer
  par l'IA locale les fonctions (`analyze_function`, `batch_decompile`).
- Extraire avec les binaires **non strippés** : `nm`/`readelf`/DWARF donnent déjà les
  symboles. Produire un **inventaire** (faire un export JSON des `CNE_*` + `GetNCID`).
- **Livrable** : table des opcodes, dictionnaire des classes `lapis`, graphe d'appel initial.

### Phase 2 — Protocole (le plus important) (1–2 semaines)
- Reconstruire le **format de trame** `BinStream` et la **table des opcodes** (Phase 1).
- Déchiffrer le **handshake RSA** et le chiffrement de session.
- Écrire un **sniffeur/proxy** en Python (mitmproxy) ou C# entre client et Gateway pour
  capturer des trames réelles (si vous avez le client). Sinon, tout déduire du binaire.
- **Livrable** : spécification Markdown du protocole + bibliothèque C# de (dé)sérialisation.

### Phase 3 — Socle C# du serveur propre (1 semaine)
- Nouvelle solution .NET 8, découpée en services (miroir des processus d'origine) :
  `Ticket`, `Gateway`, `Login`, `World`, `Zone`, `Mission`, `HttpApi`.
- Reprendre **le schéma PostgreSQL existant** tel quel (pas de migration au début) via
  `Npgsql` + Dapper ou EF Core.
- **Livrable** : squelette multi-projets + couche réseau asynchrone (`SocketAsyncEventArgs`
  ou `System.IO.Pipelines`) + accès DB.

### Phase 4 — Portage fonctionnel incrémental (le gros du travail)
- Commencer par le chemin **connexion** : `Ticket → Gateway → Login → World`.
  Valider avec le client (ou avec des tests de trame) à chaque étape.
- Puis `Zone` (le plus complexe : IA, combat) et `Mission` (missions, économie).
- Réutiliser les **fichiers `.ini` de données** (`Data/db`, `Data/scene`) : écrire un chargeur
  C# tolérant aux fichiers manquants (cf. §2.4).
- **Stratégie clé** : garder le serveur C++ d'origine comme **référence comportementale**
  (« serveur oracle ») et comparer trames pour trames.

### Phase 5 — Durcissement & configuration
- Externaliser les secrets (actuellement en clair dans `setup.ini`).
- Remplacer l'orchestration bash par une **stack Docker Compose** avec healthchecks (§11).
- Journalisation structurée (Serilog), corrélation par `traceId`.

### Phase 6 — Conteneurisation & déploiement
- Dockeriser chaque service (§11), valider la parité de comportement avec l'ancien serveur.

> **Principe directeur** : le serveur C++ est votre **spécification exécutable**. On ne le
> « devine » pas, on l'**observe** (trames, logs, DWARF) et on le **rejoue** en C#.

---

## 6. Tutoriel complet : reverse d'un serveur comme celui-là

Méthodologie générique, applicable à ce binaire et à d'autres serveurs MMO privatifs.

### 6.1 Prérequis
- Linux (les outils d'analyse sont plus à l'aise sur Linux ; vous êtes déjà sur Ubuntu).
- Java 21 (Ghidra), Python 3.11+, Node 22+, Docker.
- **Droits légaux** : vous devez être autorisé à analyser ces binaires (voir §13).

### 6.2 Étape 1 — Reconnaissance « sans exécuter » (triage statique)
Toujours commencer par des commandes passives (aucune exécution du binaire) :

```bash
# Type, architecture, linkage, présence de symboles
file WorldServer/WorldServer
readelf -h WorldServer/WorldServer          # ELF32, Intel 80386
readelf -S WorldServer/WorldServer | grep debug   # sections DWARF ?
nm WorldServer/WorldServer | wc -l          # symboles (ici ~26 000)
readelf -p .comment WorldServer/WorldServer # version du compilo (GCC 4.x)

# Noms de fonctions + classes C++ démaglées
nm -C ZoneServer/ZoneServer | grep 'lapis::' | sort -u

# Fichiers source .cc et librairies (or : le binaire vous dit d'où il vient)
strings -n 6 WorldServer/WorldServer | grep -E '\.(cc|h|cpp)$' | sort -u
strings WorldServer/WorldServer | grep -iE 'openssl|libpq|boost'
```

**Ce qu'on obtient ici gratuitement** : la liste des fonctions, des classes, des fichiers
source, des libs. Pour un binaire non strippé+DWARF comme celui-ci, c'est énorme.

### 6.3 Étape 2 — Organisation de la connaissance
- Créer un **dossier de notes** par serveur. Noter : rôle, ports, opcodes, structures.
- Tenir un **glossaire** des messages `CNE_*` ↔ `GetNCID` ↔ champs.
- Produire un **graphe d'appel** (`get_call_graph`) pour les points d'entrée (`main`,
  `OnReady`, handlers réseau).

### 6.4 Étape 3 — Analyse dynamique (exécuter le vieux serveur)
- Lancer la stack d'origine (`docker compose up` + `./start`). Observer les **logs** : ils
  donnient l'ordre d'init, les ports, les erreurs de données manquantes.
- Mettre un **proxy** (mitmproxy) entre client et Gateway pour **capturer des trames**.
- Observer le comportement réseau (`ss -tnp`, `strace -f -e trace=network`).

> L'analyse **statique** (ce que fait le binaire) et **dynamique** (ce qu'il fait réellement)
> se complètent : la statique donne la structure, la dynamique donne les valeurs réelles.

### 6.5 Étape 4 — Reconstruction du protocole
- Pour un message type, identifier son `Serialize()` dans Ghidra : l'ordre des écritures
  dans le `COutBinStream` = l'ordre des champs sur le réseau.
- Identifier les **types** : les tailles écrites (1/2/4 octets) donnent les types (byte,
  short, int) ; les strings ont souvent un préfixe de longueur.
- Établir la **table opcode → handler** (table de dispatch dans `Execute`/`ConvertToEvent`).

### 6.6 Étape 5 — Vérifier en ré-implémentant
- Écrire un **petit client de test** C# (ou Python) qui rejoue le handshake et un message.
- Comparer octet par octet avec une capture réelle. Objectif : **égalité binaire** de trame.

> Règle d'or : *une fonction n'est « comprise » que lorsqu'on peut la ré-écrire et que la
> sortie correspond*. C'est l'approche « reconstruction » que REA formalise (voir §9).

---

## 7. IA locale (100 % hors cloud) pour le reverse

Vous voulez utiliser l'IA **au maximum sans payer d'IA cloud**. Voici la stack 100 % locale.

### 7.1 Le socle : Ollama
- **Ollama** = moteur d'inférence local, gratuit, open source. Il expose une API
  compatible OpenAI sur `http://localhost:11434`.
- Installation Linux :
  ```bash
  curl -fsSL https://ollama.com/install.sh | sh
  ollama --version
  ```
- Télécharger/lancer un modèle : `ollama run qwen2.5-coder:14b`.

### 7.2 Quel modèle selon votre GPU (VRAM)

La contrainte réelle, c'est la **VRAM du GPU**. Poids en quantification Q4 (règle : garder
de la marge pour le contexte) :

| VRAM GPU | Modèle recommandé | Poids ≈ | Usage |
|---:|---|---:|---|
| 8 Go | `qwen2.5-coder:7b` | ~5 Go | Bon équilibre ; fonctions courtes, explications |
| 12 Go | `qwen2.5-coder:14b` | ~9 Go | Gain de qualité net ; recommandé minimum |
| 16 Go | `qwen2.5-coder:14b` (ou `devstral`) | ~9–15 Go | Agentique ; attention à la marge contexte |
| 24 Go | `qwen2.5-coder:32b` | ~20 Go | **Le meilleur choix** ; approche GPT-4o sur le code |
| 48 Go+ | `qwen2.5-coder:32b` + modèle « raisonneur » | — | 32B + un modèle de raisonnement |

**Pour le reverse engineering**, privilégiez :
1. **`qwen2.5-coder:32b`** si vous avez 24 Go+ (le plus performant en code, 40+ langages).
2. **`qwen2.5-coder:14b`** comme compromis solide (12–16 Go).
3. Un **modèle « raisonneur »** (type `deepseek-r1` distillé, ou `qwq`) pour l'analyse
   logique/complexe, en complément.

> Le *tool-calling* (capacité à appeler les outils Ghidra/MCP) est **indispensable** ici.
> Les modèles Qwen2.5-Coder et Mistral/Devstral gèrent bien le tool-calling ; testez-le.

### 7.3 Interface de chat : Open WebUI
- **Open WebUI** : front web auto-hébergeable qui se branche sur Ollama et sait utiliser des
  serveurs **MCP**. C'est l'équivalent local d'un assistant type ChatGPT, connecté à Ghidra.
- Lancement via Docker :
  ```bash
  docker run -d -p 3000:8080 \
    -v open-webui:/app/backend/data \
    -e OLLAMA_BASE_URL=http://host.docker.internal:11434 \
    --name open-webui ghcr.io/open-webui/open-webui:main
  ```
- Rôle : c'est ici que vous activez les **outils MCP** (GhidraMCP, REA) et conversez avec le
  modèle local pour piloter Ghidra (voir §8).

### 7.4 Alternative « sans GPU » (CPU seulement)
Si vous n'avez pas de GPU, tournez sur **CPU** avec des modèles comprimés (Q4) :
- `qwen2.5-coder:7b` en Q4 tourne sur CPU (lent mais fonctionnel pour du nommage/analyse).
- Prévoyez ≥ 32 Go de RAM système. L'inférence sera lente ; réservez-la aux tâches ponctuelles.

### 7.5 Assistants de code locaux (dans l'IDE)
Pour vous assister pendant que vous écrivez le serveur C# :
- **Continue.dev** (VS Code/Cursor) : se branche sur Ollama → autocomplétion + chat locaux.
- **Cline** (l'extension que vous utilisez) : peut pointer sur un endpoint OpenAI-compatible
  (donc Ollama) ou sur LM Studio.
- **Aider** (CLI) : pair-programming en terminal contre Ollama.

> **Important** : la qualité de nommage/reconstruction dépend surtout de la taille du modèle.
> Un 7B « comprend » une fonction simple ; un 32B enchaîne les raisonnements multi-fonctions.
> Investissez dans la VRAM si le reverse devient intensif.

---

## 8. Outils de décompilation pilotables par IA (Ghidra & co)

Cœur de votre demande : **faire piloter Ghidra par une IA**. Deux briques complémentaires.

### 8.1 GhidraMCP (le standard)

**GhidraMCP** (github.com/LaurieWired) = **serveur MCP + plugin Ghidra** qui expose les
fonctions de Ghidra à un LLM, pour qu'il reverse *autonomement* une application.

**Principe** : un plugin Java tourne dans Ghidra (serveur HTTP local, port 8080) ; un serveur
MCP Python fait le pont entre ce plugin et votre agent IA. Le LLM appelle des outils Ghidra.

**Outils exposés par GhidraMCP** :
- Lister méthodes, classes, imports, exports ;
- **Décompiler** une fonction (pseudo-code C) ;
- Renommer automatiquement fonctions et variables ;
- Lire/écrire commentaires et types.

**Installation** :
```bash
# 1) Installer Ghidra 12.x et Java 21, puis uv (Python)
curl -LsSf https://astral.sh/uv/install.sh | sh

# 2) Dans Ghidra : File → Install Extensions → + → ghidra_..._GhidraMCP.zip
#    (télécharger la release), puis File → Configure → Developer → cocher GhidraMCPPlugin.
#    (Options → GhidraMCP HTTP Server pour choisir le port, défaut 8080.)

# 3) Configurer le client MCP (ex. VS Code .vscode/mcp.json) :
```

```json
{
  "servers": {
    "ghidra": {
      "type": "stdio",
      "command": "uvx",
      "args": ["--from", "git+https://github.com/LaurieWired/GhidraMCP",
               "ghidra-mcp", "--ghidra-server", "http://127.0.0.1:8080/"]
    }
  }
}
```

**Mode « headless »** (sans interface Ghidra) — idéal pour automatiser sur nos binaires :
```bash
export GHIDRA_INSTALL_DIR=/opt/ghidra_12.x
uvx --python 3.13 --from "ghidra-mcp[headless] @ git+https://github.com/LaurieWired/GhidraMCP" \
  ghidra-mcp --headless --binary /chemin/vers/ZoneServer --project /tmp/ghidra_proj
```

### 8.2 REA (le projet de votre repo) — surcouche multi-moteurs

**REA** (`rea-rea-agents-6.5.0`) est une **surcouche MCP** plus large : au lieu de seulement
Ghidra, elle pilote **Ghidra, IDA, Hopper, Rizin, objdump, GDB, Frida, JEB…**. C'est votre
couteau suisse local. Détaillé au §9.

### 8.3 Chaîne complète « IA locale → Ghidra »

```
Vous (Open WebUI ou Cursor)
      │  prompt : « décompille et explique CNE_G_ServerAccountLogin »
      ▼
 LLM local (Ollama, qwen2.5-coder:32b)
      │  appelle un outil via MCP (tool-calling)
      ▼
 Serveur MCP (GhidraMCP ou REA)
      │  HTTP/JSON
      ▼
 Plugin Ghidra (dans l'app Ghidra, ou headless)
      │
      ▼
 Résultat (pseudo-code C, symboles, xrefs) → renvoyé au LLM → explication
```

**Choix** : pour démarrer rapidement et avec un excellent support, **GhidraMCP** suffit. Pour
industrialiser (plusieurs binaires, IDA, Frida), ajoutez **REA**. Vous pouvez combiner les
deux MCP dans le même agent.

### 8.4 Bonnes pratiques avec l'IA sur ces binaires
- Donnez le **contexte** à l'IA : nommez d'abord les grosses fonctions, travaillez par
  « feature » (ex. tout le chemin `AccountLogin`).
- Faites **nommer** (`rename`) systématiquement : l'IA est très forte pour renommer à partir
  des strings et xrefs.
- **Vérifiez** toujours : demandez à l'IA de citer ses preuves (REA impose cela par conception).
- Pour le protocole, concentrez l'IA sur les `Serialize`/`Deserialize`/`GetNCID`.

---

## 9. Le dossier `rea-rea-agents-6.5.0` (REA) — comment l'utiliser

### 9.1 Ce que c'est
**REA : Reverse Engineer Anything** (npm `rea-agents`, v6.5.0). C'est un **serveur MCP
local** (+ CLI) qui donne à un agent IA des outils pour analyser des binaires, applications,
sites, captures réseau, etc. **L'analyse tourne en local** et chaque résultat inclut ses
**preuves** et ses **limites** (conçu pour être fiable, pas « magique »).

- Licence MIT. Nécessite **Node.js** `^22.19 || ^24.11 || >=26`.
- Binaire CLI : `rea` / `rea-agents`.
- **Il n'embarque PAS d'IA** : il fournit les *outils* ; le « cerveau » vient de votre agent
  (Cursor, Claude Code, VS Code, ou un LLM local via Open WebUI).

### 9.2 Ce qu'il sait analyser (extrait pertinent ici)
- **Binaires natifs** (votre cas) : via **Ghidra** (deep), **objdump**, **Rizin**, **GDB**,
  **IDA**, **Hopper**, **JEB**. Décompilation, xrefs, call graph, renommage, commentaires.
- **JavaScript/Electron**, **.NET**, **Android**, **firmware**, **pcap/trames**, **SQLite**…
  (utile plus tard pour le **client** — voir §12).

### 9.3 Installation & mise en route
```bash
# Depuis le dossier du projet (le tarball est aussi présent : rea-rea-agents-6.5.0.tar.gz)
cd ~/Bureau/reverseden/eden_server/rea-rea-agents-6.5.0
npm install                 # ou utiliser via npx
npx rea-agents setup        # enregistre le MCP dans votre agent (au choix)
```
`rea-agents setup` détecte et configure votre agent parmi : **Cursor**, **VS Code**,
**Claude Code**, **Codex**, **Gemini CLI**, **Windsurf**, **Qwen Code**, etc. Il affiche les
changements et demande validation (aucune écriture surprise). Redémarrez l'agent ensuite.

### 9.4 Configurer un provider d'analyse (Ghidra)
REA est « bring your own tool » : il **n'installe pas** Ghidra. Il faut :
1. Installer Ghidra (Java 21) — et, sur Linux, un `ps` compatible procps sur le `PATH`
   (contrôle de propriété des processus) ; cf. `docs/installation.md`.
2. Lancer l'analyse d'un binaire en choisissant le provider. Variables utiles :
   - `REA_GHIDRA_STARTUP_TIMEOUT_MS` (binaires volumineux → augmenter, ex. ZoneServer),
   - `REA_OBJDUMP_COMMAND`, `REA_RIZIN_COMMAND`, `REA_GDB_COMMAND` (chemins alternatifs),
   - `REA_MCP_MAX_RESPONSE_BYTES` (si grosses sorties).

### 9.5 Outils MCP natifs les plus utiles pour VOUS
(extraits du catalogue REA, pour l'analyse de `server_us`) :

| Outil | Usage concret |
|---|---|
| `open_binary` / `close_binary` | Ouvrir/fermer une session d'analyse sur un binaire |
| `analyze_function` | Décompiler + expliquer une fonction |
| `batch_decompile` | Décompiler en lot (idéal pour les `CNE_*`) |
| `get_call_graph` | Graphe d'appel autour d'un handler |
| `find_xrefs_to_name` | Trouver qui appelle/ référence un symbole |
| `search_bytes` | Rechercher un motif d'octets (opcodes, en-têtes) |
| `compare_functions` | Comparer deux versions (ex. `WorldServer` vs `.bak`) |
| `inspect_with_objdump` | `disassemble`, `dwarf`, `symbols`… (depuis le CLI aussi) |
| `record_unknown` / `list_unknowns` | Suivre les zones non comprises (rigueur) |
| `export_evidence_bundle` | Exporter les preuves pour la doc |

### 9.6 Workflow recommandé avec REA + LLM local
1. `rea-agents setup` dans Cursor/VS Code ; brancher Ollama (qwen2.5-coder:32b).
2. Dans l'agent : *« Ouvre ZoneServer avec Ghidra, liste les classes `lapis::`, puis décompile
   `CNE_G_ServerAccountLogin` et documente son format de trame. »*
3. Itérer : renommer, explorer les xrefs, alimenter votre §4 (spécification protocole).
4. Exporter les preuves (`export_evidence_bundle`) dans votre dossier de notes.

> **À noter** : REA prône la distinction *observation / déduction / inconnu*. C'est une
> excellente discipline pour ne pas se raconter d'histoires pendant le reverse.

---

## 10. IDE & extensions VS Code / Cursor pour le reverse

Vous utilisez **Cursor** (un VS Code augmenté). Voici l'outillage qui booste l'efficacité.

### 10.1 Les agents IA intégrés (le socle)
- **Cline** (déjà en place) : agent autonome qui édite/lit/exécute. Peut se brancher sur un
  LLM local (Ollama/LM Studio, endpoint OpenAI-compatible).
- **Cursor Composer / Agent** : édition multi-fichiers. Branchez-le sur un modèle local si
  vous voulez zéro cloud, ou gardez le cloud ponctuellement pour les tâches dures.
- **Roo Code / Kilo Code** : alternatives open source d'agent pour VS Code.

### 10.2 Connexion LLM local dans l'IDE
- **Continue.dev** : chat + autocomplétion locaux (Ollama). Idéal pendant que vous codez le
  serveur C#.
- **LM Studio** (GUI) ou **Ollama** : fournissent l'endpoint `http://localhost:11434/v1`
  (Ollama) à renseigner dans Cline/Cursor/Continue.

### 10.3 Extensions d'analyse / reverse (VS Code & Cursor)

| Extension | Rôle |
|---|---|
| **Hex Editor** (`ms-vscode.hexeditor`) | Éditer/inspecter des octets dans l'IDE |
| **Native Debug / C-C++ (CppTools)** | Débogage natif (si vous compilez du C) |
| **C# Dev Kit** (`ms-dotnettools`) | Développer le serveur propre en .NET 8 |
| **Error Lens, GitLens, Even Better TOML** | Confort général |
| **YAML, Docker** | Éditer `compose.yml`, Dockerfiles |
| **REST Client** | Tester l'API de `HTTPAServer` |
| **REA (MCP) + GhidraMCP (MCP)** | Brancher Ghidra dans l'agent (§8–§9) |
| **Python, Pylance** | Écrire les scripts d'analyse (mitmproxy, scapy) |

### 10.4 Astuces productivité IA locale dans Cursor
- Dans **Continue.dev**, mettez `qwen2.5-coder` (14b/32b) en **chat** et un petit modèle
  (1.5b/3b) en **autocomplétion** (rapide).
- Gardez **deux canaux** : un modèle local 32b pour l'analyse lourde, et l'agent cloud (si
  vous l'acceptez) pour les gros refactors. Vous restez maître du curseur « local ↔ cloud ».

### 10.5 Organisation de travail dans l'IDE
- Ouvrez `eden_server` **et** `rea-rea-agents-6.5.0` dans des fenêtres séparées.
- Créez un dossier `notes/` (Markdown) par serveur ; liez les preuves REA.
- Gardez les binaires d'origine en lecture seule ; travaillez sur des copies.

---


## 11. Conteneurisation Docker

### 11.1 Constat sur l'existant
- `compose.yml` actuel : **1 seul service** PostgreSQL 12, en `network_mode: host`. Les
  binaires C++ tournent **en natif** sur l'hôte (script `start`), pas en conteneur.
- Problèmes : secrets en clair, `network_mode: host` (isolement faible), pas de healthchecks,
  pas de redémarrage automatique, dépendance à la locale `LC_ALL=C`.

### 11.2 Deux stratégies

**A. Conteneuriser l'ancien serveur (rapide, pour l'étudier)**
- Les binaires sont **statiques** (pas de `.so`) → une image `i386/debian` ou `i386/ubuntu`
  (ou `--platform linux/386`) suffit. L'ordre de démarrage et les `sleep` s'encodent avec
  `depends_on` + healthchecks.

**B. Stack Docker pour le NOUVEAU serveur C# (recommandée à terme)**
Chaque service .NET dans son conteneur, plus PostgreSQL.

### 11.3 Exemple de `docker-compose.yml` cible (serveur C#)

```yaml
name: eden

services:
  postgres:
    image: postgres:16
    restart: unless-stopped
    environment:
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?set in .env}
      POSTGRES_DB: fnaccount
    volumes:
      - pgdata:/var/lib/postgresql/data
      - ./db/database.out:/docker-entrypoint-initdb.d/10-database.sql:ro
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres -d fnaccount"]
      interval: 5s
      timeout: 3s
      retries: 20
    networks: [eden]

  ticket:
    build: ./src/Eden.Ticket
    restart: unless-stopped
    environment:
      ConnectionStrings__Default: ${DB_CONN}
    depends_on:
      postgres: { condition: service_healthy }
    networks: [eden]

  gateway:
    build: ./src/Eden.Gateway
    restart: unless-stopped
    ports: ["5560:5560"]
    depends_on: [ticket, login]
    networks: [eden]

  login:
    build: ./src/Eden.Login
    restart: unless-stopped
    depends_on:
      postgres: { condition: service_healthy }
    networks: [eden]

  world:
    build: ./src/Eden.World
    restart: unless-stopped
    depends_on:
      postgres: { condition: service_healthy }
    networks: [eden]

  zone:
    build: ./src/Eden.Zone
    restart: unless-stopped
    depends_on: [world]
    networks: [eden]

  mission:
    build: ./src/Eden.Mission
    restart: unless-stopped
    depends_on:
      postgres: { condition: service_healthy }
    networks: [eden]

  httpapi:
    build: ./src/Eden.HttpApi
    restart: unless-stopped
    ports: ["20205:20205"]
    depends_on:
      postgres: { condition: service_healthy }
    networks: [eden]

networks:
  eden:
    driver: bridge

volumes:
  pgdata:
```

### 11.4 Dockerfile type (.NET 8, multi-stage)

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/Eden.Gateway/Eden.Gateway.csproj
RUN dotnet publish src/Eden.Gateway/Eden.Gateway.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
ENV DOTNET_EnableDiagnostics=0
EXPOSE 5560
ENTRYPOINT ["dotnet", "Eden.Gateway.dll"]
```

### 11.5 Points d'attention Docker
- **32 bits** : le nouvel ensemble est en 64 bits (.NET 8), ce qui résout le problème de
  l'ancien binaire. Vous n'avez plus besoin d'image `i386`.
- **Fichiers `.ini`** : montez `server_us/Data` en volume en lecture seule, ou convertissez-
  les au format de config .NET (`appsettings.json`) au fil du portage.
- **Secrets** : via fichier `.env` (git-ignoré) + `${VAR}` ; jamais en clair.
- **Ports** : exposez uniquement ceux nécessaires (Gateway public, DB interne au réseau).

---

## 12. Reverse du client de jeu (phase ultérieure)

Le client n'est **pas** dans ce dossier. Le README suppose un client Windows
(`easyfun.bat`, `connect.ini`, `connects.ini`). Quand vous l'aurez :

### 12.1 Ce que ça change
- Le client est la **référence absolue du protocole** : le reverse devient « capture +
  comparaison » plutôt que « pure déduction ». Beaucoup plus rapide.
- Pour beaucoup de serveurs privés, on reverse d'abord le **client** (souvent plus simple :
  strings, config, assets).

### 12.2 Outils adaptés au client
- Si c'est un exécutable Windows : **Ghidra** (x86/x64), **REA** (`inspect_binary_layout`,
  `.NET` si managé), **Frida** (hook runtime), **x64dbg** (débogueur Windows).
- Si c'est du **.NET** : REA/ilspy décompilent directement (beaucoup plus simple).
- **Capture réseau** : mitmproxy/tcpdump entre client et Gateway (mais le trafic est
  chiffré après le handshake RSA → il faudra aussi reverse le chiffrement, ou hooker le
  client avec Frida pour logguer *avant* chiffrement).
- Les `connect.ini`/`connects.ini` donnent l'IP/port et parfois des indices de protocole.

### 12.3 Ordre conseillé (client + serveur ensemble)
1. Capturer le **handshake** client→Gateway (Wireshark/mitmproxy).
2. Identifier la **clé/le chiffrement de session** (via Frida sur le client ou Ghidra).
3. Avec le serveur C# propre, rejouer le handshake et valider l'égalité de trame.

> Le client facilite la **validation** ; le serveur (déjà analysé) donne la **logique**.
> Le combo des deux est le plus efficace.

---

## 13. Risques, légal et limites

### 13.1 Légal / éthique (important)
- **Autorisation** : n'analysez que des binaires dont vous détenez les droits ou pour
  lesquels vous avez une autorisation explicite. Voir le `DISCLAIMER.md` de REA et les
  licences des outils commerciaux (IDA, Hopper, JEB = payants, à licence valide).
- Le serveur fermé → projet de **préservation** (objectif louable), mais la redistribution de
  binaires/données propriétaires peut poser problème. Gardez le repo privé.
- Respectez le droit d'auteur sur les **assets** (textures, modèles, textes).

### 13.2 Risques techniques
- **Données manquantes** : certains `T_*.ini` absents (cf. §2.4) → le comportement de
  certaines mécaniques sera incomplet. À reconstruire ou à tolérer.
- **Version binaire** : les binaires sont patchés (IP). Vérifiez la cohérence client/serveur
  (version `1-6-7-VIP` vue dans les logs).
- **Chiffrement** : OpenSSL 0.9.8o (ancien). Algorithmes potentiellement obsolètes ; à
  moderniser dans le serveur propre (mais alors incompatibilité client → à gérer).
- **Complexité du ZoneServer** (14,7 Mo) : l'IA/combat est le gros morceau. Prévoir un effort
  concentré sur cette partie.

### 13.3 Limites de l'IA
- L'IA **accélère** (nommage, résumé, détection de structures) mais **ne remplace pas** la
  validation. Exigez des preuves (REA le fait nativement).
- Un modèle local sous-dimensionné produira du « plausible mais faux ». Validez par
  ré-implémentation (§6.6) et par comparaison de trames.

---

## 14. Annexes — inventaires extraits du binaire

> Extraits directement des binaires (`nm`, `readelf`, `strings`). Ils servent de carte de
> départ pour le reverse et valident l'analyse ci-dessus.

### 14.1 Fichiers source du projet (reconstitués depuis les binaires)

**GatewayServer** (extrait) : `Main.cc`, `GatewayServer.cc`, `GatewayServerNetwork.cc`,
`MSSocketManager.cc`, `Packet.cc`, `BinStream.cc`, `PGDatabase.cc`, `PGAccountDBO.cc`,
`JPApClient.cc`, `CGIServer.cc`… et les messages `NC_G_Client*`, `NC_GJ_Server*`, `NE_G_*`.

**ZoneServer** (extrait, très représentatif du gameplay) :
- IA : `AI.cc`, `AICommonFuntions.cc`, `AIFSM{Flee,Hunt,Idle,Roam,Return,Combat,Bewitch,Chaos}.cc`
- Objets/économie : `Armor.cc`, `CloneItem.cc`, `CombineOperation.cc`, `AuctionShop.cc`,
  `DrillItem.cc`, `CubeOperation.cc`
- Combat/scènes : `Battlefield.cc`, `Colosseum.cc`, `BattleRoyale.cc`, `BattleCrusade.cc`
- Personnage : `Character.cc`, `CharColorQuery.cc`, `CharFormula.cc`
- DB : `DBAgent.cc`, `BaseQuery.cc`, `*Query.cc` (nombreuses requêtes)
- Attributs : `EC_Attr*.cc` (des centaines : `EC_AttrDefence.cc`, `EC_AttrCriticalRate.cc`…)

### 14.2 Classes `lapis` — récapitulatif par domaine (WorldServer, extrait)

| Domaine | Exemples de classes |
|---|---|
| Réseau | `CMSSocketManager`, `CCWTCPChannel`, `CCWTCPService`, `CMSChannel` |
| Sérialisation | `CBinStream`, `CInBinStream`, `COutBinStream` |
| Logs | `BasicLogger`, `CLogger`, `CLogFactory`, `CAdvLogger` |
| Base de données | `CAccountDBO`, `CCharacterDBO`, `PGDatabase`, `DBAgent` |
| Comptes/auth | `CBillingCmds`, `CCaptcha`, `Billing_TC_*` |
| Jeu | `CCharacter`, `CFamily`, `CLottery`, `CFortuneBag`, `CFormula` |
| Événements | `CEvent*`, `CEventQueue`, `CCommandEvent` |
| Encodage | `Base64Encode`, `Base85Encode/Decode`, `CalcMD5Sum` |

### 14.3 Messages réseau `CNE_*` — structure type

Exemple de symbole démanglé (WorldServer) :
`CNE_G_ServerAddFriend` expose :
`Serialize(COutBinStream)`, `Deserialize(CInBinStream)`, `Execute()`, `GetNCID()`,
`GetName()`, `HasCommand()`, `ConvertToEvent()`.

→ Chaque message est une **classe** avec une opcode (`GetNCID`) et un format (`Serialize`).
C'est la table à reconstituer pour le serveur C#.

### 14.4 Bases de données (extrait de `database.out`)

- Bases : `fnaccount`, `fndb1`, `fnmember`, `fnmember1`.
- Tables `fnaccount` : `accounts`, `worlds`, `fake_worlds`, `gm_tool_accounts`,
  `exchange_list`, `lottery`, `itemmall`, `starway`…
- Tables `fndb1` : `bags`, `elf`, `equipment1/2`, `family`, `collection`, `auction`,
  `colosseum_ranking`, `battlefield_career`, `class_lifelong`, `crashlog`…

### 14.5 Fichiers de données clés (`server_us/Data/db`)

| Fichier | Taille | Contenu |
|---|---:|---|
| `T_Dialogue.ini` | ~5,3 Mo | Dialogues PNJ |
| `T_Item.ini` | ~3,2 Mo | Objets |
| `T_Enchant.ini` | ~1,6 Mo | Enchantements |
| `T_ItemMall.ini` | ~2,0 Mo | Boutique |
| `T_Monster.ini` | ~272 Ko | Monstres |
| `T_Spell.ini` | ~802 Ko | Sorts |
| `T_Mission.ini` | ~242 Ko | Missions |
| `4pEquip.ini` | ~620 Ko | Équipement |

---

## Conclusion & prochaine action immédiate

Vous avez entre les mains un cas **favorable** : binaires **non strippés + DWARF + logs
verbeux**. Le reverse est donc largement « assisté » et non « à l'aveugle ».

**Les 3 actions à faire dès maintenant** :
1. **Monter la chaîne IA locale** : installer **Ollama** + `qwen2.5-coder` (14b ou 32b selon
   VRAM) + **Open WebUI** (§7). Valider le tool-calling.
2. **Brancher Ghidra à l'IA** : installer **Ghidra 12** + **GhidraMCP** (et/ou lancer
   `npx rea-agents setup` pour **REA**). Tester sur `ZoneServer` en mode headless (§8–§9).
3. **Attaquer le protocole** : faire l'inventaire des `CNE_*`/`GetNCID` et reconstituer le
   format `BinStream` (Phase 1–2 du plan, §5). C'est le chemin critique pour un serveur C#
   compatible client.

Le présent rapport constitue la **spécification de départ** : analyse de stack (§2–§4), plan
d'action (§5), tutoriel (§6), stack IA locale (§7), outils IA↔Ghidra (§8–§9), IDE (§10),
Docker (§11), et feuille de route client/risques (§12–§13).

*Rapport généré le 14/10/2026 — à conserver dans le repo comme documentation de reverse.*
