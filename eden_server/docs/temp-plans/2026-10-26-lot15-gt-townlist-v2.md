# Lot 15 — GT1/GT2/GT3 : "Can't find family", town list vide, bouton Enter

## Diagnostic (preuves live + désasm + repro)

**Preuve live** (`/tmp/eden-zone.log`, run lot14) :
- `IN 176 A1BB0D00` (familyId=900001) → `OUT 266 9B "Can't find family"`
  à +5 ms — `GetGuild(900001)` = null → **`_guilds` vide**.
- `IN 174` → `OUT 502` 17 vecteurs à zéro — aucun candidat
  (cohérent : cache vide, pas un problème de format).
- Monde OK : `[Family] 2 familles chargées, 4 membres`.

**Cause racine (reproduite par harness `/tmp/guildtest` sur la vraie
base)** : `GuildService.LoadAsync` jette
`InvalidOperationException` dans `SocialRepository.GetGuildMembersAsync`
— Dapper exige un constructeur
`(int,int,string,int,int,int,int,int)` : toutes les colonnes
remontent en `System.Int32` (int4 PG), mais `GuildMemberRow` déclare
`short Rank, long Contribution, short Level, short NodeId, short Quit`
→ aucune signature ne matche → exception sur le PREMIER guild →
`_guilds` reste vide à vie (le `_ =` de Program.cs avale l'erreur).
`RefreshHouseOpensAsync` (lot14) ne fait que PATCHER les entrées
existantes — il ne réinsère jamais → aucun effet sur un cache vide.

**Désasm natif** :
- `CNE_CZ_ZoneServerFamilyTownEnter::Execute` @0x8259d30 : gates
  0x207f (newer town) / rate-limit 500 ms char+0x650 / sortie -1 →
  `TransportCharacterToNode(char+0xa98)` / raid ≥0xb → 0x1430 /
  versus char+0xbb4 → 0xd1f → `VisitFamilyInstance`.
- `CZoneServer::VisitFamilyInstance` @0x8171440 : mort → 0xb80 ;
  type node ∉{1,3,4} → 0xb89 ; **famille absente de la map
  `Ins+0x460` → `CNC_CZ_ClientTextOutput 0x9b "Can't find family"`**
  ; sinon `house_open!=0` → DBA `VisitFamilyInstance FamilyID=%d`
  (la mission transporte). Nos gates = fidèles (`Ins+0x460` =
  TOUTES les SFamily, insertion @l.147172).
- Client : `FUN_00a95cb0` (« Go to Base ») = msg UI 0x3e9 → ouvre
  `FamilyTownListWnd`, aucun paquet ; `EnterTownBtn` =
  `FUN_006cd280` → construit la commande depuis la ligne
  sélectionnée → NCID 176 `[i32 familyId]`. Le 176 live reçu avec
  900001 = bouton Enter (ou Go to Base via fenêtre) — le mécanisme
  client existe déjà, seul le lookup serveur manquait.

## Fixes

- [x] F1 `SocialRepository.GuildMemberRow` : types numériques → `int`
      (signature Int32 exigée par Dapper) ; `LoadAsync` caste en
      `short` pour `GuildMember.Level/NodeId/Quit`.
- [x] F2 `GuildService` : extraction `BuildGuildAsync(GuildRow)` —
      `LoadAsync` l'utilise + **try/catch par guilde** (une guilde
      en échec ne vide plus tout le cache) + log du total.
      `RefreshHouseOpensAsync` → **upsert auto-réparant** : guilde
      inconnue du cache → insertion complète (membres inclus) +
      log — guérit un boot load raté ou une guilde créée par SQL
      direct pendant que la zone tourne.
- [x] F3 `Program.cs` : `_ = services.Guilds.LoadAsync()` → wrapper
      loggé (`EdenLog`) — fini les échecs silencieux.
- [x] GT3 : déjà couvert — 176 accepte tout familyId instancié
      (autre guilde incluse) ; sortie -1 → CrossZoneNode (sauvegardé
      par `MigrateToNodeAsync` à l'entrée). Rien à coder, à vérifier.
- [x] Fix annexe (fichier agent GP, non commité) : `SessionEnchants.
      TryRemove(charId, out _)` ne compilait pas — `out _` se liait
      au paramètre lambda `async _ =>` (CancellationToken) au lieu
      d'un discard → `out var _sessionMap`.

## Vérifs
- `dotnet build` 0 err / 0 warn ; `dotnet test` vert.
- Re-test du harness `/tmp/guildtest` : `LoadAsync` doit charger
  900001/900002, `GetGuild(900001)` non null.
- À valider en jeu : Towns liste TestGuildHL/TestGuild2, Enter →
  transport hall, Go to Base → hall de sa guilde.

## Journal
- 2026-10-26 : diagnostic bouclé — cause racine = ctor Dapper
  `GuildMemberRow` (short/long vs colonnes int4) → LoadAsync KO →
  cache guilde zone vide à vie. Plan rédigé.
- 2026-10-26 : F1-F3 livrés + fix compile agent GP. Build Release
  0 err/0 warn, 1106 tests verts. Harness `/tmp/guildtest` : LoadAsync
  charge 900001 (TestGuildHL, 3 membres, house 501) + 900002
  (TestGuild2, 1 membre, house 502), GetGuild(900001) résout.
  **Nécessite le restart zone** pour prendre effet. À valider en
  jeu : Towns list, Enter, Go to Base.
