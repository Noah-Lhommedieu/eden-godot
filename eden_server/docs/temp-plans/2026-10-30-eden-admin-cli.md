# Lot : Eden.Admin — CLI création comptes/personnages

> Date : 2026-10-30 — demande utilisateur : le client Reforged a retiré
> l'UI de création de personnage ; il faut un outil serveur pour créer
> comptes et personnages sans passer par le client.

## Symptôme / besoin

- Reforged n'expose plus le panneau de création de perso → impossible de
  créer des personnages en jeu.
- Écrire des INSERT à la main est risqué : le flux natif insère dans 5
  tables (player_characters, player_classlist ×6, player_spells ×23,
  player_card, items de départ, character_number) en une transaction.

## Preuves natives

- `Eden.World` implémente déjà le flux natif complet : NCID 4/32
  (name-check : codes 3/8/9/10), NCID 5 (`CNE_CW_ServerCreateCharacterData`
  `[u8 race][u8 class][u8 flag][32B apparence]`) →
  `CharacterRepository.CreateCharacterNativeAsync`.
- Bitfields apparence : désasm `CCharacterDBO::CreateCharacterIntoDB` —
  b24..b31 (hair/face/colors/gender/eyes), déjà documenté dans
  World/Program.cs.
- `accounts.id` n'a pas de séquence : le login natif alloue via
  `CUIDAllocator` (AIDAllocator, `SELECT id FROM accounts` pré-alloué,
  plage MinAID/MaxAID — login @0x804ffc0). Fix : INSERT avec
  `COALESCE(MAX(id),0)+1` (même stratégie que le charId 200000000+).
- Limite native : 3 personnages par compte (World/Program.cs gate).

## Points d'édition

- [x] `server/src/Eden.Admin/` — nouveau projet CLI (net10.0, réf. Eden.Core)
  - `account create|list`, `char create|list|name-check`
  - réutilise `AccountRepository.CreateAccountAsync` +
    `CharacterRepository.CreateCharacterNativeAsync` — INSERT identiques
    au flux in-game, aucun SQL ad hoc.
- [x] `Eden.Core/Game/AppearanceBlob.cs` — codec partagé encode+decode
      des bitfields natifs (une seule source pour World et Admin).
- [x] `Eden.World/Program.cs` — refactor : le décodage inline passe par
      `AppearanceBlob.Decode` (comportement identique).
- [x] `AccountRepository.CreateAccountAsync` — allocation explicite de
      l'id (max+1) comme le natif ; corrige le NOT NULL violation.
- [x] Tests `AppearanceBlobTests` : roundtrip, bornes de bits, formules
      natives sur octets construits à la main, octets hauts à zéro.
- [x] `Eden.sln` — projet ajouté (Debug+Release, dossier src).

## Vérification live

- `account create tooltest` → id=3 ; doublon refusé.
- `char create ToolChar (race=2 class=3)` → id=200000005 ; `ToolChar2`
  (valeurs bornes hair=37 face=45 eyes=33 eyes2=44) → id=200000006.
- `char name-check`/`char list` OK ; nom dupliqué → code natif 3.

## Reste

- Le perso créé n'a pas été connecté en jeu — à valider à la sélection
  de personnage Reforged (le perso doit apparaître dans la liste).
