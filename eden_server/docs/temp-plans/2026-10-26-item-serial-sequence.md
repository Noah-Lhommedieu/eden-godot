# Plan chantier — séquences d'allocation d'ids (item / elf / mail)

## Symptôme prouvé par audit

- `ItemSerialAllocator` seedait `SELECT max(id)+1` en mémoire +
  `Interlocked` : thread-safe intra-processus MAIS
  `MissionDb.cs:560` (processus mission) l'utilise aussi → zone et
  mission produisent les mêmes serials → collision PK réelle sur
  `inventory1`/`mailitem`/…
- `ElfRepository.NextElfIdAsync` : `MAX(elf_id)+1` sans verrou —
  même bug (race intra-process + multi-process).
- `MailIdAllocator` : même pattern seed+Interlocked sur
  `player_mail.id` (PK) — utilisé aujourd'hui seulement par la zone,
  mais même classe de bug.
- `CharacterRepository.InsertStarterItemsAsync` : son propre
  `GREATEST(max(equipment2), max(inventory2), 220000000)+1` en
  transaction — ne lit que 2 tables sur les ~16 de l'espace serial →
  peut émettre des ids déjà pris ailleurs (collision d'espace) +
  race entre créations concurrentes.

## Fix

Séquences PostgreSQL partagées : `nextval` est atomique et
cross-process. Un aller-retour DB par allocation — coût assumé de la
correction (les appels sont déjà des chemins DB, pas des hot loops).

## Points d'édition

- [x] `Database/012-item-serial-sequence.sql` : `item_serial`
  (seed = max(id) des tables `ItemSerialTables` +
  `family_building_container`, allouée elle aussi par l'allocateur),
  `elf_serial` (max `elf.elf_id`), `mail_serial` (max
  `player_mail.id`). `setval(…, false)` + garde anti-rembobinage →
  idempotent au rejeu.
- [x] `ItemSerialAllocator.cs` → `nextval('item_serial')` par appel ;
  suppression seed/semaphore ; overload `(NpgsqlConnection, tx)`.
- [x] `MailIdAllocator.cs` → `nextval('mail_serial')` (même classe
  de bug, fix groupé).
- [x] `ElfRepository.NextElfIdAsync` → `nextval('elf_serial')`.
- [x] `CharacterRepository.InsertStarterItemsAsync` → `nextval` par
  item dans la transaction existante (au lieu de son max+1 maison).
- [x] `InventoryRepository.ItemSerialTables` += `family_building_container`
  (son `id` EST alloué depuis l'espace serial → doit figurer dans le
  seed ; la liste documente l'espace, la migration la reflète).
- [x] Commentaires d'appelants rafraîchis (MissionDb, BankRepository,
  MailRepository, SocialRepositories) — le commentaire « seed
  max(id)+1 » devient obsolète.
- [x] `AGENTS.md` (racine + server/) : « pas de séquence native »
  → réécrit en « via séquence `item_serial` ».
- [x] Migration appliquée à `fndb1` (conteneur
  `eden_server-postgres-1`).
- [x] `dotnet build` → 0 erreur / 0 warning.

## Hors scope (rapport)

- `player_characters.id`, `accounts.id`, `family.id` : max+1 mais
  espaces distincts, mono-writer process — risque résiduel signalé,
  non corrigé.
