# Phase 2 — Chargement d'entrée hors-boucle (ZoneLoop non bloquée)

Date : 2026-10-26 (implémentation reprise 2026-11-xx)
Plan d'action pour `server/` — déplace le chargement d'entrée zone
(ticket + ~30-50 opérations SQL séquentielles) hors de la boucle
acteur `ZoneLoop`, avec install atomique en une commande.

## Symptôme

`HandleTicketAsync` (NCID 0) s'exécute comme UNE commande de la
boucle : RPC ticket + ~25 à 50 lectures/écritures PostgreSQL
séquentielles (jusqu'à ~200 opérations avec les seeds). Pendant ce
temps aucune autre commande ne passe → ticks 100 ms retardés en
rafale d'entrées (constaté : files de commandes pleines à l'ouverture).

## Invariants à préserver

- NCID 473 ClientHello émis EN PREMIER, avant la validation ticket.
- Ordre des paquets par session inchangé (le read loop est
  séquentiel ; la passerelle attend `EntryLoad` avant de poster).
- NCID 57 (premier echo → burst), NCID 4 (MigrationDone → dump),
  patches 310/311/313/302 identiques à l'actuel.
- Aucune mutation d'`EntryState` hors de la boucle : le pipeline
  peuple un `EntryState` scratch ; l'install copie les champs en une
  commande.
- Écritures d'entrée (purges, seeds, normalisation équipement,
  sorts) exécutées hors-boucle AVANT le calcul des corps patchés.
- Les 4 repos en cours d'édition par un autre agent restent
  intouchés : FastDressRepository, CustomClassRepository,
  DailyAwardRepository, LifeSkillRepository.

## Points d'édition

### `ZoneEntryFlow.State.cs`
- [x] `EntryState.EntryLoad` — `volatile Task?` documenté (posé par
      la commande NCID 0, attendu par la passerelle).
- [x] `EntryState.Entry` — `EntryBundle?` installé (patches 310/311,
      MigrationDone différés).
- [x] `EntryState.EntryDirty` — invalidation mi-session (équipement/
      sorts/classe changés → SendPatched retombe en lecture live).

### `ZoneEntryFlow.cs`
- [x] `TryHandleAsync` — passerelle : `await st.EntryLoad` avant de
      poster la commande suivante (tous NCIDs, ticket y compris).
- [x] `case 0` — commande légère : pose `st.EntryLoad =
      StartEntryLoad(...)` et rend la main immédiatement (plus de
      `HandleTicketAsync` sur la boucle).

### `ZoneEntryFlow.EntryPipeline.cs` (nouveau)
- [x] `EntryBundle` — données chargées figées : scratch `EntryState`,
      `Character`, `ClassRow`, `EquipSpellBody`, listes d'expiration,
      enchants à restaurer, stats précalculées, summons/works à armer.
- [x] `StartEntryLoad` — `Task.Run` du pipeline (créé ici, pas dans
      la commande boucle) ; renvoie la Task stockée dans `EntryLoad`.
- [x] `RunEntryPipelineAsync` — 473 → ticket RPC →
      `LoadEntryDataAsync` → install posté sur la boucle et attendu
      (fin de `EntryLoad` = install exécutée).
- [x] `LoadEntryDataAsync` — lectures groupées `Task.WhenAll`
      (connexions pool distinctes) puis écritures déterministes :
      normalisation slots, purge expirés, seeds life-skill/sorts,
      daily/lifelong rollover, works (consommation différée, tirages,
      saves), lottery, enchant-purge ; peuple le scratch + bundle.
- [x] `InstallEntryAsync` — commande boucle : copie champs scratch→st,
      `BindCharacter`, timers works, summons différées (spawn mobs),
      émissions dépendantes — seule mutation d'`EntryState`.

### `ZoneEntryFlow.Lifecycle.cs`
- [x] `HandleTicketAsync` → remplacé par la commande légère ; corps
      migré dans le pipeline.
- [x] `SendPatchedAsync` — 310 lit `st.Entry.ClassRow`, 311 prend
      `st.Entry.EquipSpellBody` (fallback live si `EntryDirty`).
- [x] `HandleMigrationDoneAsync` — consomme le bundle : 2032/2033
      (données), notices d'expiration, purge enchants + restauration,
      overlay (ClassRow), recommend/lifelong déjà chargés (émissions
      conservées au même point), stat blocks depuis contribution
      préchargée.

### Autres fichiers
- [x] `ItemsExpiry.cs` — split purge : préparation off-boucle
      (SELECT/DELETE) + émissions 316/275 au même point MigrationDone.
- [x] `Work.cs` — variante pipeline de `LoadWorksAsync` : consommations
      + tirages + saves hors-boucle, armement timers et spawns de
      summons différés à l'install.
- [x] `Services.cs` — `EnsureActiveClassSpellsAsync` sur données
      préchargées + upsert batch ; overlay AA sans requête ;
      `SendStatBlocksAsync` via contribution préchargée.
- [x] `Status.cs` — restauration enchants : lecture/purge off-boucle,
      `AddPlayerEnchantAsync` conservé à MigrationDone (même ordre).
- [x] `Equipment.cs` — normalisation sur liste préchargée.
- [x] `StatPipeline.cs` + `Program.cs` — `StatFetch` (cache de lignes
      préchargées) pour dédupliquer classlist/char/AA/enchants/perks.
- [x] `ProfileStatEmitter.cs` — param contribution préchargée.
- [x] `ClassRepository.cs`/`IClassRepository` — `LearnSpellsAsync`
      batch (unnest + spell_order calculé), ancienne méthode conservée.
- [x] `ZoneEntryFlow.Limitless.cs` — émission 2032 depuis données.

## Mesures

| Opération | Avant (boucle) | Après |
|---|---|---|
| Opérations SQL séquentielles sur boucle | ~50+ | 0 (install = copies) |
| Lectures équipement | ~9 | 1 (`GetAllEquipmentEntitiesAsync`) |
| Lectures player_classlist | ~4 | 1 |
| Lectures player_spells | ~2+ | 1 |
| INSERT sorts manquants | N×`LearnSpellAsync` | 1 batch |
| INSERT life-skill | N×`InsertAsync` | 1 `InsertManyAsync` (existant) |

## Validation

- [x] `dotnet build` 0 erreur / viser 0 warning.
- [x] Tests : passerelle (paquet pendant load attend), install atomique
      (état complet ou absent), batch sorts.
- [ ] Non fait faute d'env : bench rafale, headless entry, régressions
      move/combat — à documenter.

## Hors scope

- Repos protégés (autre agent).
- Requêtes par-paquet post-install (elfs 110, mission states 50…) —
  elles restent sur la boucle comme aujourd'hui.
