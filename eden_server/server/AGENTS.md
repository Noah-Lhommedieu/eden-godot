# Règles projet — portage Eden C#/.NET 10

## État du code (post-phases 0-4 de l'audit)

Ce qui est déjà en place — ne pas réinventer ni contourner :

- **Réseau** : `NetSession` = `Channel` borné (256 trames) + drain
  unique → **le RC4 sortant suit l'ordre réel du fil** ; backlog
  >5 s → déconnexion propre. Handshake 10 s, idle
  `EDEN_IDLE_TIMEOUT_SEC`, rejet trames `len<2`, `PacketTrace`
  (ring 32 paquets in/out dumpé à la déco anormale).
- **Modèle acteur** : `ZoneLoop` (file de commandes + ticks 100 ms +
  `Schedule`) — **toute mutation d'`EntryState` doit passer par la
  boucle** (`PostAsync`/`Schedule`/`IsOnLoopThread` via AsyncLocal).
  Jamais de `Task.Run`/`Task.Delay` inline dans un chemin de boucle.
- **Persistance** : `PersistenceQueue` (ordonnée par charId) +
  `CharacterWriteBehind` — écritures DB jamais en fire-and-forget
  nu ; `.GetAwaiter().GetResult()` interdit (zéro occurrence).
- **Codecs** : NCIDs → classes `NetCommand` dans
  `Messages/Generated/*.g.cs` quand le format est prouvé ; sinon
  codecs dédiés (`NativeItemCodec`, `NativeElfCodec`,
  `VendorPackets`, `MagicBoxPackets`, `WeddingPackets`) ; les NCIDs
  non prouvés sont listés dans `tests/.../Data/known_incomplete.json`.
- **Observabilité** : `EdenLog.Write` (jamais `Console.WriteLine`),
  `EdenMetrics` (meter `Eden.Server`), `EdenConfig` pour TOUTES les
  env vars (`EDEN_BAL_*`/`EDEN_BAL_PARAM_*` = exception assumée
  dynamique). Rate-limit entrant par session + login par IP.
- **Tests** : 721+ dont 254 cas golden byte-à-byte depuis captures
  natives ; `tools/wire_diff.py` (diff natif-vs-C#), client headless
  RSA+RC4, dissector `tools/lapis.lua`, matrice
  `docs/generated/FEATURE-MATRIX.md` (67 GOLDEN / 641 NON PROUVE).

## Fichiers générés `*.g.cs` — règle spéciale

`ZoneMessages.g.cs` (31k lignes), `MissionMessages.g.cs`,
`WorldMessages.g.cs` sont des **sorties de
`tools/extract_all_protocols.py`** : ~1 300 classes `NetCommand` à
raison de ~20 lignes chacune. Ce n'est **pas** du code à lire —
c'est une base de données de layouts. Les règles « god class /
lisibilité » ne s'y appliquent PAS. Conventions :

- Champs `FieldN` = positionnels, avec commentaire quand le rôle est
  prouvé ; **renommer sémantiquement seulement quand le désasm le
  justifie** (la regen écraserait — le projet tolère l'édition mais
  ne pas en abuser).
- Ne pas « nettoyer » ces fichiers à la main : toute correction de
  layout passe par preuve (capture/désasm) + test golden.
- Les classes dont le format est variable/masqué que le générateur
  a aplati sont dans `known_incomplete.json` — ne pas les utiliser
  comme référence.

## Documentation totale (obligatoire)
- **En-tête de classe** : `<summary>` décrivant la responsabilité et, pour
  les classes de protocole, les NCIDs concernés et la source de preuve.
- **XML doc sur chaque méthode** publique ou non triviale : ce qu'elle
  fait, d'où viennent les formats, ce qu'elle émet.
- **Commentaire sur chaque ligne de charge utile** : tout octet/champ
  écrit dans un paquet ou lu depuis un payload doit être commenté
  (type, rôle, valeur attendue) — cf. `ZoneMissionLink` pour le style.
- Mettre à jour les commentaires quand le format change — un commentaire
  obsolète est pire qu'absent.

## Qualité de code (obligatoire)
- **Pas de god class / god method.** Une classe = une responsabilité. Une méthode qui dépasse ~80 lignes ou qui gère plusieurs domaines doit être découpée.
- **Pas de Train Wreck** (`a.B.C.D()` en chaîne) — extraire un
  helper nommé qui exprime l'intention (cf. `LocalTeamMembers`,
  `ResolveLootReceiver` dans `ZoneEntryFlow.Combat.cs`).
- **Fichier = unité lisible** : viser < ~1 000 lignes par fichier
  source (hors `.g.cs` générés, voir règle spéciale). Un fichier qui
  dépasse demande une extraction de composant focalisé ou un
  `partial` par sous-domaine — `ZoneEntryFlow.*.cs` est LE pattern
  (Combat, Sight, Elf, Migration, Work…), `ZoneLoop`/`PersistenceQueue`
  montrent l'extraction de service.
- Préférer petites interfaces + injection aux accès statiques pour
  les nouveaux services (voir `TeamService`, `LootService`,
  `EquipTrainService`) — ça rend le comportement testable sans
  serveur live.
- **Séparation protocole/logique.** Les formats de paquets vivent dans des codecs/builders typés, pas inline dans les flux. Dispatch par NCID via tables/handlers dédiés, pas un switch géant dans la même méthode que la logique métier.
- **Partials par domaine** pour les classes de flux volumineuses (ex. `ZoneEntryFlow.{Combat,Sight,Migration,Dialogue}.cs`).
- Composants focalisés : handler movement, combat, spell, dialogue, portal/migration, sight, mission bridge — chacun dans son fichier.
- **Accès DB — deux pièges déjà rencontrés en live** :
  - `DefaultTypeMap.MatchNamesWithUnderscores = true` est activé par
    `Eden.Core/Database/DapperBootstrap.cs` (module init). Les colonnes
    snake_case natives (`item_id`, `player_id`, `container_index`…)
    mappent vers le PascalCase. Sans lui Dapper **ignore silencieusement**
    les colonnes (items à ItemId=0 → inventaire vide). Ne pas le
    désactiver ; privilégier aussi des alias `AS` explicites dans le SQL.
  - Jamais `using var db = ...; return db.XxxAsync(...)` non-awaité :
    la connexion est disposée pendant la requête en vol
    (« Can't close, connection is in state Connecting », reader
    corrompu). Toujours `await` avant la fin du `using`.
- Les tables items natives n'ont **pas de séquence sur `id`** — passer
  par `ItemSerialAllocator.NextAsync` (séquence partagée
  `item_serial`, espace global sur les ~16 tables items — atomique
  cross-process zone/mission, migration 012) avant tout INSERT dans
  inventory1/2, storage1/2, mailitem, equipment1/2, family_storage…
  Même principe pour `elf.elf_id` (`elf_serial`) et `player_mail.id`
  (`mail_serial`).

## Protocole réseau (dur)
- **Aucun paquet spéculatif vers le client live.** Tout format envoyé doit être vérifié par : capture native byte-par-byte, désassemblage du handler (serveur natif ou client `game_.exe`), ou test unitaire reproduisant les bytes natifs.
- Chaque codec documente sa **source de preuve** en commentaire (`// natif: capture ts…`, `// désasm: 0x80f54xx`). Les hypothèses non vérifiées sont marquées `// HYPOTHESIS:` et désactivées par défaut.
- Vérifier la **longueur exacte** du body vs la capture native avant d'envoyer — un body trop long désynchronise/crashe le client.
- Les handles envoyés au client doivent être des **sight-handles** (`0x88C8xxxx` NPC, `0xFFFFAxxx` mobs), jamais des charId internes.

## Client cible (obligatoire)
Le seul client supporté est le client Godot maison :

| Client | Chemin | Profil |
|---|---|---|
| **Eden Client Reborn** | `../../eden_godot/godot_project` | Godot 4 C#, assets natifs/Reforged convertis en lecture seule |

- Les clients natifs `client/` et `reforged/` sont des **oracles en lecture
  seule** : comportement, animations, layouts, formules et formats peuvent y
  être comparés, mais aucune compatibilité wire ou runtime n'est exigée.
- Toute évolution protocole doit être conçue pour Godot-first : version de
  handshake, payloads modernes si nécessaire, et tests d'oracle quand le
  comportement natif sert de référence.
- Ne jamais modifier les arbres natifs `client/`, `reforged/`, `server_us/`
  ou `re/` ; les réutiliser uniquement comme preuve/documentation.
- « Compatible » signifie désormais : même serveur, même DB, comportement
  fidèle à l'original côté Godot, mesuré et prouvé par tests/captures.

## Reverse engineering (obligatoire)
- **Source de vérité : `re/ghidra/<binaire>/decompiled.c`** — exports
  pseudo-C complets de tous les binaires, clients compris. Inventaire :
  - **Serveur natif** : `zone/`, `world/`, `mission/`, `login/`,
    `gateway/`, `ticket/`, `httpa/` (symboles `lapis::*` — navigation
    directe par nom de fonction).
  - **Client projet** : `client_game/` (game_.exe), `client_launcher/`,
    `cl_alaudio/`, `cl_steams/`, `cl_webbrowser/` (strippé, `FUN_*`).
  - **Client Reforged** : `rf_game/` (game_.exe), `rf_d3d9/` (le hook —
    modèle pour nos propres mods DLL), `rf_gamefix/`, `rf_itemmall/`,
    `rf_neobank/` (dlls de features dans `EdenReforged/mods/_testing/`).
- Chercher via **`reindex`** (index SQLite `re/index.sqlite`, règles
  dans `re/AGENTS.md`) avant tout autre outil :
  `reindex lookup lapis::CItemFactory::BuyItems -b zone` (corps complet),
  `reindex search|addr|callers|callees|strings|symbols|opcode`, et
  `reindex xref PTR__CNC_Xxx` (tous les émetteurs d'un paquet) /
  `reindex xref 0x<addr>` (qui référence une globale/vtable).
  ⚠️ `callers/callees` ne trace pas les appels via vtables — incomplet
  par construction. Fallback : `grep -n "Nom" re/ghidra/<b>/decompiled.c`.
  **JAMAIS REA** (serveur MCP `rea`) — il réanalyse les mêmes binaires
  que les exports déjà présents : doublon pur. Ne pas re-décompiler
  non plus ce qui existe déjà.
- **Fonction `FAILED/TIMEOUT` dans `decompiled.c` ?** Pas de REA :
  `objdump -d --start-address=0x<addr> --stop-address=0x<addr+taille>`
  sur le binaire correspondant (adresses dans `functions.txt`), ou
  `nm -C`/`strings` pour le contexte. L'assembleur + les xrefs
  suffisent pour les switchs de dispatch (tables de sauts visibles).
- **Binaire non décompilé ?** Utiliser la même chaîne que `re/` :
  `re/decompile.sh /chemin/binaire re/ghidra/<nom>` — Ghidra
  headless (`GHIDRA_HOME`, défaut `ghidra_12.1.4_PUBLIC/` du dépôt,
  `support/analyzeHeadless`) + le GhidraScript `re/ExportAll.java`
  (produit `decompiled.c`, `functions.txt`, `strings.txt`, `STATUS`).
  Statut dans `re/ghidra/<nom>/STATUS` (RUNNING/DONE/FAILED).

## Validation headless vs client réel (obligatoire)

Le client headless (`tools/headless_client`) prouve **uniquement le
comportement serveur** (paquets émis, ordre, persistance) — ce n'est
PAS une preuve du comportement du client Reforged. Les retours du
joueur sont des constats VISUELS : un souci noté peut venir du
serveur, du client (patchs/d3d9), ou des deux — ne jamais conclure
« serveur innocent » ou « réglé » sur la seule base d'un test
headless. Dans chaque compte-rendu, distinguer explicitement :
(1) ce qui a été testé headless (serveur seul), (2) ce qui reste à
vérifier en jeu par le joueur, (3) l'attribution actuelle
(serveur / client / indéterminé).

## Process
- Avant toute implémentation : lire ce fichier + `docs/MASTER-IMPLEMENTATION-PLAN.md`.
- **Chaque lot de tâches ⇒ un plan d'action dans `docs/temp-plans/`**
  (`YYYY-MM-DD-<sujet>.md`) AVANT de coder : symptômes, preuves natives
  attendues, points d'édition. Le plan suit le master plan ; le cocher
  au fil de l'eau.
- Build zéro erreur, viser zéro warning (`dotnet build`).
- Fonctionnalité "faite" = confirmée côté client, pas juste émise dans les logs.
- Client crash = bloquant : désactiver le paquet suspect avant toute autre chose.
