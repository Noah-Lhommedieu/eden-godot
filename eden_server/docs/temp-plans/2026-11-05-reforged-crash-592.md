# 2026-11-05 — Crash client Reforged à l'entrée : NCID 592 malformé

## Symptôme

Reforged : sélection perso → chargement rapide → écran figé, pas
d'entrée en jeu. `error.log` : `System Crash!!`, minidump
`EXCEPTION_BREAKPOINT @0x6a8f30`, thread 36, déterministe (21/10,
23/10 ×4).

## Diagnostic (preuves binaires `rf_game`)

- `0x6a8f30` = stub `int3` enregistré à `0x6a95a6` comme
  **handler CRT invalid-parameter** (`FUN_004ea3c4` = setter,
  globale `DAT_010568e8` via `DecodePointer`).
- Stack du dump (vérifiée octet-par-octet, retours après `call`) :
  `FUN_00d75da0` (dispatch NCID) → `FUN_00ca1380` (Execute) →
  `FUN_00cad9a0` (Deserialize) → `FUN_00c90120` → `FUN_00b4eee0`
  (**lecture u16 du InBinStream**) → `FUN_004ea40a` (invoque le
  handler) → `int3`. = **lecture au-delà de la fin du paquet**.
- `FUN_00ca1380` = Execute du command-object de vtable `0xc786b8`.
  `FUN_00d75da0` lit un `u16` en tête de paquet = index direct dans
  le vecteur `commands` (`commands[i]` créés séquentiellement dans
  `FUN_00c93a30` : `new`+ctor+`push_back`+`mov ax,0x10c6fdX`).
- Comptage des `call 0xc35250` (push_back) avant le ctor `0xc786a0`
  (le nôtre, à `0xc9bd66`) : **592** → index = NCID **592** =
  `CNC_CZ_ClientPlayerClassCustomData`, émis par nous en réponse au
  166 (QueryClassList) quand `CustomClassSlots > 0`.

## Bug

`CNC_CZ_ClientPlayerClassCustomData::Serialize` zone @`0x081fed00` :
le compteur externe `vector<vector<u32>>` est écrit par
`operator<<(uint)` → **u32**. `CustomClassPackets.Build` écrivait
un **u16** → client lit u32, consomme 2 octets de trop → underflow
sur l'entrée suivante → invalid-param → int3. Le codec généré
(`Field7 uint`) et le golden `ncid_592.json` (`03000000`)
confirment u32. Pourquoi test3/test2 et pas les autres : le 592
n'est envoyé que si le perso a des cartes de classe personnalisée.

## Fix

- [x] `CustomClassPackets.Build` : `WriteShort` → `WriteInt` pour
      nslots + commentaire preuve.
- [x] `CustomClassTests` : 3 layouts mis à jour (u32) — 8/8 verts.
- [x] `dotnet build` : 0 err / 0 warn.
- [ ] Validation client : re-tester entrée Reforged test3 + client
      référence.

## Rappel méthodo

Un paquet qui annonce des tailles plus grandes que le contenu tue
le client proprement (CRT `_invalid_parameter` → int3), pas
seulement « trame ignorée » — d'où l'importance des goldens
byte-à-byte pour tout paquet émis.
