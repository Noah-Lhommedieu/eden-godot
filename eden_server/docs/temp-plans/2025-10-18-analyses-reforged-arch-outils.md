# Temp-plan — 18 Oct : analyses Reforged, architecture, modding client, outils d'édition

> Plan d'action temporaire (règle AGENTS.md). Livrables = analyses/documentations,
> pas de refactor massif sans validation utilisateur.

## 1. Client Reforged — connexion ch01 + bypass launcher + compat ✅

- [x] Inventaire du dossier client Reforged → `game_.exe` + `d3d9.dll`
      (hook, 43 features), `connect.ini` → `login.edenreforged.com:6543`,
      `eden_patch.ini` ~20 toggles.
- [x] Flux launcher : `app.asar` extrait → `spawn(game_.exe,['LogLog'])`
      (main.js:728), aucun jeton — login en jeu.
- [x] Diag : **connect.ini pointait le serveur officiel** → corrigé
      `Server=127.0.0.1,6543` (backup `.bak`). Check `strstr(cmdline,
      "LogLog")` dans rf_game/decompiled.c ~514100 → MessageBox+exit sinon.
- [x] Bypass : `launch-reforged.sh` (repo) — wine-ge + prefix
      `reforgeddecompiled` + arg `LogLog`.
- [x] Compat : handshake/login/ticket identiques (même moteur) ; NCID
      inconnus tolérés ; politique = serveur piloté par `client/`,
      features Reforged désactivables via `eden_patch.ini` si conflit.
- → **docs/ANALYSE-REFORGED-CLIENT.md**

## 2. Audit architecture — extensibilité gameplay ✅

- [x] God object : `ZoneEntryFlow` 5043 l / switch ~40 NCID / EntryState
      ~50 champs ; `Combat.cs` 1604 l mélange paquets+logique+IA.
- [x] Gameplay hardcodé : dmg `60+rand`/`80+rand`, mob `Atk/3` — aucune
      stat/def/resist/target-rule ; formule HP dupliquée 3×.
- [x] Doublons : `CharacterRepository` ×2, `CharacterCreationService` ×2,
      deux chemins de dispatch.
- [x] Cible : 4 couches (Eden.Core.Game domaine pur → orchestrateurs
      Gameplay → Handlers minces → Codecs) ; `StatSheet`, `DamagePipeline`
      à stages ordonnés, `ISkillEffect`, `ILootSource` (drops composables),
      dispatch table unique, `PlayerSession` éclaté, migration en 5 étapes.
- → **docs/ARCHITECTURE.md**

## 3. Modding clients — faisabilité ✅

- [x] Client projet : `UIData/layouts` = **434 .layout XML CEGUI** +
      imagesets/schemes/fonts/lua → UI modifiable en clair.
- [x] Reforged : tout en `pkg/*.pkg` (format Aeria) → `quickbms/` +
      `aeriagames.bms` déjà dans le repo.
- [x] Logique native : modèle **d3d9.dll proxy** (exactement ce que fait
      Reforged, `rf_d3d9` décompilé dispo comme référence) — réversible.
- [x] Source complet compilable : irréaliste (12,7 Mo pseudo-C) ; client
      réécrit Unity/Godot = projet séparé.
- → **docs/ANALYSE-MODDING-ET-OUTILS.md** §A

## 4. Outils d'édition de contenu ✅

- [x] Tout est texte : `SXXX.ini` `[reborn_monsters]/[npc]/[gateway]/
      [event]`, `S_DropItem/S_ClassDropItem`, `S_/T_*` — nos parsers
      `Eden.Core.Data` existent déjà.
- [x] Proposition : `Eden.Tools` web local (ASP.NET + `Eden.Core.Data`)
      — modules : map/spawn editor, drop editor (normal/class/BP),
      monster/item/NPC, shop, quest/dialog (phase avancée).
- [x] Points d'attention : écriture Big5, sync `SXXX.ini`+`sXXX.ini`,
      backups.
- → **docs/ANALYSE-MODDING-ET-OUTILS.md** §B
