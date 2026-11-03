# Audit technique — état actuel du projet Eden Server C#/.NET 8

> **Date** : 22 Octobre 2026 — **Périmètre** : `server/` (313 fichiers
> .cs, ~103k lignes), `re/` (outillage reverse), `tools/`, `docs/`,
> `captures/`. Chaque constat est vérifié dans le code, pas supposé.

## Synthèse exécutive

Le projet est **fonctionnel et jouable** (login → world → zone, combat,
inventaire, quêtes, familiers, EquipTrain…), avec une discipline de
preuve rare : chaque paquet documente sa source (désasm/capture). Les
points forts sont réels : outillage d'indexation maison (`reindex`),
MITM déchiffrant, 425 tests verts, conventions strictes.

Les faiblesses sont **concentrées sur la concurrence et la vérification
automatique** — et ce sont elles qui ont causé les incidents bloquants
récents (désync RC4 → freeze client, paquet 271 à 10B au lieu de 9B).
Ce ne sont pas des problèmes de performance : ce sont des problèmes de
*correctness sous charge*.

---

## 1. Architecture C# et stack technologique

### 1.1 Packages NuGet

| Projet | Packages | Verdict |
|---|---|---|
| `Eden.Core` | Dapper 2.1.89, Npgsql 10.0.3, System.Text.Encoding.CodePages 10.0.13 | Minimaliste et sain |
| `Eden.Tests` | xunit 2.5.3, coverlet.collector 6.0.0, Microsoft.NET.Test.Sdk 17.8.0 | Standard |
| Tous les services | zéro autre dépendance | Aucun framework superflu |

**Adapté** : pour un serveur de jeu custom-protocol, la stack minimaliste
est un choix défendable — pas de framework web parasite, pas d'ORM lourd
pour une DB au schéma fixé par le natif. Il ne manque rien de bloquant.

### 1.2 Couche réseau

| Composant | État | Constat |
|---|---|---|
| `OutBinStream` | `ArrayBufferWriter<byte>` + `GetSpan`/`Advance`, `BitConverter.TryWriteBytes` | ✅ Moderne, zéro alloc intermédiaire |
| `PacketReceiveBuffer` | fenêtre glissante + compaction amortie | ✅ Bien conçu (ex-`List<byte>` O(n²) corrigé) |
| `NetSession` | `TcpClient`/`NetworkStream`, `SemaphoreSlim` send-lock, RC4 dans la section critique | ✅ Correct (fix documenté : keystream entrelacé → freeze) |
| `InBinStream` | `byte[]` + position | ⚠️ Allocs par `ReadString`/`ReadBytes` — OK à l'échelle actuelle |
| Envoi | `OutBinStream` + `byte[]` frame neufs par paquet | ⚠️ Pas de `ArrayPool<byte>` — ~2 allocs/paquet sortant |
| Réception | `new byte[8192]` + `List<byte[]>` par appel, `payload.AsSpan(2).ToArray()` par paquet | ⚠️ ~3 allocs/paquet entrant |
| `EDEN_NET_DEBUG` | `Environment.GetEnvironmentVariable` lu **à chaque paquet émis** | ⚠️ Lookup env-var dans le hot path — à cacher en static |
| `System.IO.Pipelines`, `MemoryMarshal`, `unsafe`, `StructLayout` | **0 occurrence dans tout le code** | ℹ️ Parsing manuel octet-par-octet — choix lisible, pas un manque |

**Verdict réseau** : correct et largement suffisant pour la charge réelle
(quelques clients). Le gap n'est pas Pipelines — c'est l'absence de
**file d'envoi bornée** : tout envoi passe par `WriteAsync` direct sous
un seul lock. Un client lent ou un burst de broadcasts peut bloquer tous
les senders de la session (head-of-line) et, pire, il n'y a **aucun
contrôle de backpressure** : un client qui ne lit pas remplit le kernel
buffer puis fait attendre le lock indéfiniment.

### 1.3 Persistance et entités

- **Dapper + `new NpgsqlConnection` par appel** — pooling Npgsql natif :
  idiomatique, correct. `MatchNamesWithUnderscores` via
  `DapperBootstrap` (module initializer — propre).
- **Schéma** : `Database/*.sql` versionné, tables natives reproduites.
- **`ZoneServices`** = registre de ~60 services/repos en `required init`
  (service locator). `Program.cs` = ~200 lignes de wiring manuel — pas
  de DI container, assumé et lisible.
- **⚠️ Sync-over-async réel** : `GuildService.cs` (×3) et
  `AuctionService.cs` (×2) font `.GetAwaiter().GetResult()` — blocage de
  thread pool, à éliminer.
- **⚠️ `_ = repo.XxxAsync()` fire-and-forget** répandu (24 occurrences
  dans 2 fichiers seulement, ex. `UpdateLevelExpAsync`,
  `TickTreasuresAsync`) : exceptions non observées + **ordre d'écriture
  DB non garanti** (deux updates concurrentes sur la même ligne).
- **Modèle threading** : `Task.Run` par session + `System.Timers.Timer`
  1s (respawn+trésors) + tâches par cast/mob — **pas de game loop
  unifiée**. `EntryState`/live world sont mutés depuis le thread session,
  les timers et les tâches fire-and-forget sans verrou commun. C'est le
  terreau des bugs intermittents difficiles à reproduire.

---

## 2. Traitement du pseudo-code (`decompiled.c`)

### 2.1 Workflow actuel (après refonte `reindex`)

| Étape | Outil | État |
|---|---|---|
| Corps de fonction | `reindex lookup` (leaf OU qualifié) | ✅ |
| Recherche sous-chaîne corpus entier | `reindex search` + snippet FTS5 | ✅ |
| Adresse → fonction | `reindex addr` | ✅ |
| **Qui référence une vtable/globale** | `reindex xref` (640k refs, `PTR__CNC_*` = émetteurs de paquets) | ✅ NOUVEAU — accélère réellement "qui émet ce paquet" |
| Call-graph | `callers`/`callees` | ⚠️ Vfuncs non tracées (limitation documentée — 90% des dispatches natifs sont virtuels) |
| Symboles/chaînes | `symbols`/`strings` | ✅ |
| NCID ↔ classe C# | `reindex opcode` (1 240 commandes) | ✅ |
| Fonction `[FAILED]` | `objdump -d` (18 échecs réels identifiés, dont `InitPCCommandTable`, `DecodeEffectCommand`) | ✅ |
| MCP pour agents | `reindex-mcp.py` — **écrit mais non enregistré** dans une config MCP | ⚠️ à brancher |

### 2.2 Méthodologie C++ → C#

- Offsets `+0xNNN` → colonnes `S_*.ini` via les `Deserialize` natifs
  (méthode solide, preuve par construction).
- Codecs **manuscrits** avec commentaire de preuve par champ — lisible
  mais la cause des bugs de longueur (le 271 : +1 octet = désync).
- `[StructLayout]`/`MemoryMarshal` **non utilisés** — justifié : le wire
  est un flux séquentiel variable, pas des structs fixes. Inutile de les
  introduire.
- `ZoneMessages.g.cs` (31k lignes) = couche message générée — maintenir
  le générateur à jour est critique.
- Limite méthodo : `GetNCID()` natif retourne la constante template —
  le mapping NCID→classe natif exige `Init*CommandTable` (FAILED →
  objdump manuel, non automatisé).

---

## 3. Analyse réseau et RE dynamique

### 3.1 Outils existants (souvent sous-estimés)

| Outil | Rôle | État |
|---|---|---|
| `tools/native_mitm.py` | MITM RSA+RC4 : proxy client↔natif, dump NCID déchiffré | ✅ Fonctionnel |
| `tools/ptrace_sniff.py` | ptrace → plaintext du trafic ZoneServer natif | ✅ Fonctionnel |
| `tools/zsniff_decode.py` + `captures/*.bin` | Captures natives & C# décodables | ✅ |
| `EDEN_NET_DEBUG=1` | PKT-IN/PKT-OUT hex dans les logs serveur | ✅ |
| `docs/WIRE-EVIDENCE.md` | Layouts prouvés byte-par-byte | ✅ |

### 3.2 Ce qui manque (le vrai gap)

1. **Pas de replayer de captures** : impossible de rejouer
   `zsniff_native_entry.bin` contre le serveur C# headless pour vérifier
   que chaque handler produit la réponse attendue. Les tests actuels
   vérifient des unités, jamais une séquence wire complète.
2. **Pas de diff automatique natif-vs-C#** : le MITM logge les deux
   mondes mais rien ne compare les streams NCID par NCID — la détection
   d'écart reste manuelle (lecture de logs). Le bug du 271 (9B vs 10B)
   aurait été détecté automatiquement.
3. **Pas de dissector Wireshark** du protocole lapis (header u16/RC4) —
   le MITM compense, mais un `.lua` dissector aiderait les captures
   spontanées.
4. **Pas de golden-bytes par NCID** : quelques codecs testés, pas de
   fixture `capture → bytes attendus` systématique.

---

## 4. Documentation et connaissances

### 4.1 Inventaire

| Document | Rôle | État |
|---|---|---|
| `AGENTS.md` racine + `server/AGENTS.md` | Conventions, 2 clients, règles dures | ✅ À jour (reindex intégré) |
| `docs/MASTER-IMPLEMENTATION-PLAN.md` (1 231 l.) | Roadmap 100+ systèmes | ✅ Vivant — mais les statuts vieillissent mal (le doc dit lui-même « les [x] ne valent pas validation live ») |
| `docs/temp-plans/` | 40 plans par batch | ✅ Process respecté — devient un historique fouillis, jamais consolidé |
| `docs/implementation/` | 15+ docs par système | ✅ Bien structuré |
| `docs/generated/` (`ncid_map.json`, `cne/cnc_fields.json`, `protocol_spec.json`) | Dictionnaire opcodes/champs | ⚠️ **Partiel et stale** : `ncid_map` n'a que 6 serveurs, `TicketServer` vide |
| `docs/protocole/opcodes.md` | 581 noms CNE repérés | ⚠️ Inventaire de noms, pas de mapping opcode→champs consolidé |
| `docs/WIRE-EVIDENCE.md` | Layouts prouvés | ✅ |

### 4.2 Ce qui manque pour garder le fil

- **Pas de dictionnaire d'opcodes unique et frais** : la vérité est
  éclatée entre `ZoneMessages.g.cs` (C#), `ncid_map.json` (stale),
  `opcodes.md` (noms) et le désasm. `reindex opcode` est aujourd'hui la
  source la plus fraîche — le régénérer dans `docs/generated/` à chaque
  build fermerait la boucle.
- **Pas de lexique des constantes/états** : elf status (0/1/2), types
  d'attributs 418 (1=exp, 2=hp, 8-11=stats, 12=name), container types
  (0 inv/1 equip), EquipLoc multipliers, text indices… Ces énumérations
  sont re-découvertes à chaque tâche dans les commentaires — une table
  de référence unique (`docs/generated/constants.md`) éviterait les
  erreurs de réutilisation.
- **Statuts de features dérivables** : la roadmap demande une vérité
  « fait = prouvé côté client » qu'aucun fichier ne consolide
  automatiquement. Un `FEATURE-MATRIX.md` régénéré (système × NCID ×
  handler × test × preuve) remplacerait le tracking manuel.

---

## 5. Points aveugles et optimisations

### 5.1 Architecture — non traité aujourd'hui

- **Pas de game loop unifiée** : ticks dispersés (`System.Timers` 1s,
  tâches par cast, boucles de chase par mob ~100 ms) + mutations
  concurrentes d'`EntryState` depuis thread session/timers/fire-and-forget.
  Aucun verrou de domaine. → déterminisme impossible, races latentes.
- **Backpressure** : pas de file d'envoi bornée par session (cf. §1.2) —
  risque head-of-line + pas de déconnexion sur backlog.
- **Fire-and-forget DB** : écritures non ordonnées et non supervisées —
  perte silencieuse possible (cf. §1.3).
- **Observabilité** : `Console.WriteLine` partout, zéro `ILogger`, zéro
  métrique (paquets/s, latence DB, sessions). Debug = relire des logs.
- **Rate-limiting** : aucun sur login/paquets entrants — un flood
  trivial DoS la session (lecture en boucle sans quota).
- **Config** : ~15 env vars dispersées (`ZONE_PORT`, `NODE_ID`,
  `DB_CONN`, `EXP_RATE`, `DATA_DIR`…) — pas de fichier central (choix
  assumé, à consolider avant multi-zone).

### 5.2 Conteneurisation

`docker-compose.yml` complet existe (postgres + 6 services .NET,
healthchecks) **mais le mode opératoire réel demandé est hybride** :
C# sur l'hôte, seul PostgreSQL en Docker. Recommandation : garder
l'hybride en dev (itération plus rapide), conserver le compose pour
reproduction/CI — c'est déjà le cas, rien à réparer, juste à documenter
comme choix officiel.

### 5.3 Librairies/outils à considérer (priorité réelle, pas gadget)

| Besoin | Candidat | Effort | Gain |
|---|---|---|---|
| File d'envoi bornée | `System.Threading.Channels` (BCL, zéro dep) | ½ j | Head-of-line résolu, contrôle du backlog |
| Buffer pooling sortie | `ArrayPool<byte>.Shared` | ½ j | -2 allocs/paquet |
| Journalisation structurée | `Microsoft.Extensions.Logging` console | 1 j | Niveaux/filtres sans env-vars |
| Replayer/golden bytes | tests xunit + `captures/*.bin` existants | 2-3 j | Détection auto des écarts wire |
| Diff natif-vs-C# | extension `native_mitm.py` (déjà là) | 1-2 j | Le bug 271 détecté avant le client |
| `System.IO.Pipelines` | réécriture transport | 3-4 j | **Non prioritaire** — charge actuelle insuffisante pour le justifier |
| ORM plus riche (EF Core) | — | — | **Contre-indiqué** : schéma fixé par le natif, Dapper suffit |

**IA** : `reindex-mcp.py` écrit mais non enregistré — le brancher dans
la config MCP donne aux agents `re_lookup/re_xref/…` nativement (gain
mesurable : xref = « qui émet ce paquet » en une requête au lieu de 3
greps manuels).

---

## Plan d'action priorisé — 3 chantiers majeurs

### Chantier 1 — Fiabiliser concurrence et flux d'envoi *(correctness)*
**Pourquoi** : les incidents bloquants récents (désync RC4 → freeze,
paquet mal formé) naissent de la concurrence, pas du débit.
- File d'envoi bornée par session (`Channel<byte[]>`) drainée par une
  tâche unique → sérialisation RC4 sans lock applicatif + disconnect
  sur backlog.
- Éliminer `.GetAwaiter().GetResult()` (GuildService, AuctionService) et
  router les écritures DB fire-and-forget dans une file ordonnée.
- Game loop unifiée : `PeriodicTimer` central (ex. 100 ms) dépilant
  respawn/chase/casts/trésors → ordering déterministe, suppression des
  races sur `EntryState`.

### Chantier 2 — Harnais de vérification wire automatisée *(preuve)*
**Pourquoi** : « fait = prouvé côté client » est aujourd'hui manuel ; les
bugs les plus coûteux sont des écarts de quelques octets.
- Golden-bytes : fixtures par NCID (capture native → bytes attendus) en
  tests xunit sur les codecs.
- Replayer : rejouer `captures/*.bin` contre les handlers headless.
- Diff-tool : extension de `native_mitm.py` comparant les streams
  NCID/longueur natif-vs-C# en continu.

### Chantier 3 — Consolider la connaissance protocole *(vélocité)*
**Pourquoi** : la même information (opcodes, constantes, statuts) est
re-découverte dans 4 sources partielles.
- Régénérer `docs/generated/` (ncid_map complet, field layouts) depuis
  le code + reindex à chaque build.
- `constants.md` unique : enums états/types/containers/attr/text-indices
  sourcés depuis le désasm + `S_*.ini`.
- Feature-matrix auto (NCID → handler → test → statut preuve).
- Enregistrer `reindex-mcp.py` dans la config MCP.

**Ce que cet audit déconseille** : réécriture réseau en Pipelines,
migration EF Core, refactor du composition root en DI container —
dépense sans gain mesurable à l'échelle actuelle. La qualité existante
(codecs prouvés, discipline de commentaires, outillage reindex) est
l'atout : ne pas la diluer.
