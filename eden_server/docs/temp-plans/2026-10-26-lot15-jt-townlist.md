# Lot 15 — JT1/JT2/JT3 : Town List, Go to Base, NPCs du hall

## Diagnostic (preuves)

**Fil client (désasm client_game)**
- `FamilyTownListWnd` s'ouvre via le service NPC `family_town` (→ 514)
  ou le bouton « Go to Base » de `FamilyInfoWnd` (`EnterTerritoryBtn`,
  texte 0x2f = "Go to Base" — `FUN_00a95cb0` poste le msg UI 0x3e9 →
  montre la fenêtre, **aucun paquet envoyé**).
- `TownSerachBtn` → **NCID 174** `[s16 min][s16 max][i32 mask][str]`
  (`FUN_008bbaa0` → `FUN_006d21a0` ; défauts min=0 max=100 mask=0).
- `EnterTownBtn` → **NCID 176** `[i32 familyId]` (`FUN_006cd280`).
- `MemberSerachBtn` → NCID 175 (`FUN_006cd220`).

**Preuve live** (`/tmp/eden-zone.log`, joueur réel 02:20)
- `IN 174 00006400000000000000` (min=0 max=100 mask=0 nom="")
  → `OUT 502` **vide** (17 vecteurs à 0) — handler OK mais 0 candidat.
- `IN 176 A1BB0D00` (familyId=900001) → **silence total** — seul chemin
  sans émission : `guild.HouseOpen == 0` dans le cache zone.

**Causes**
1. Cache guilde zone périmé : `GuildService.LoadAsync` = boot only ;
   `house_open` posé par SQL direct (lot13) jamais poussé (ZW 35 n'existe
   que via `family_instance_create`). → 502 vide + 176 silencieux.
2. Nodes 501/502 absents de `GameData.Nodes` (S_Node.ini n'a que 1-37)
   → `IsInFamilyInstanceNode` toujours faux → `SpawnFamilyHallNpcsAsync`
   jamais appelé (JT3), 132 muet, sortie 176 -1 tombe sur
   « Can't find family », gate statue 0x38e cassé.
   Natif : `CFamily::InstanceCreate` mission @0x81bf660 (type1→0x1f5,
   type0→0x1f6) ; `IsInFamilyInstance` zone @0x84098d0 (type nœud == 4).

## Fixes
- [x] F1 `GameDataService.EnsureFamilyInstanceNodes` : NodeDefs
      synthétiques 501/502 type 4 (S501 forêt / S502 montagne) après le
      load S_Node.ini + test `FamilyInstanceNodes_AreRegistered_Type4`.
- [x] F2 `ResolveInstanceGroup` : type 4 → clé = famille visitée
      (`st.VisitedFamilyInstanceId`, repli -charId) — le hall est
      instancié par famille, pas par équipe.
- [x] F3 `GuildService.RefreshHouseOpensAsync()` appelé dans les
      handlers 174/176 — resync house_open/level/name/leader/tax depuis
      `family` (robuste aux UPDATE directs du lot13/tests).
- [x] Fix annexe (fichier autre agent) : `SendMob{Aggro,AttackPhase}Async`
      dé-statiques (CS0120) + test `LifeFlags` attendu 0x0200 pas 0x02.
- [x] Vérifs : build 0 err/0 warn, 1106 tests verts, rapport.

## Journal
- 2026-10-26 : diagnostic complet (désasm + log live) → plan.
- 2026-10-26 : F1-F3 implémentés + 1 test ajouté ; build/tests verts.
  Reste : validation in-game (liste peuplée, entrée hall, NPCs).
