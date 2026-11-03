# Lot : 10 derniers entrants `CNE_CW_` non gérés

Date : 2026-10-28. Audit des 18 entrants monde — les 10 non couverts.

## Preuves natives (world)

| NCID | Classe | Format | Execute |
|---|---|---|---|
| 8 | ServerLookingForMember | `[s16×5]` | `LoadLFGCharacters` @0x8050ea0 → 45 `ClientLookingForMemberResult` (Serialize @0x80cc5f0 : `[s16 n][n×{i32,str,s16 lvl,s16 node,s16 race,s16 cls}]`) |
| 11 | ServerLookingForGroup | `[i32][str][s16×4][u8]` | `SetLFG(char, u8!=0)` @0x805bb00 — autres champs lus-ignorés |
| 19 | ServerQueryExploitRank | `[u8]` | recopie AllExploitRank → 59 `[u8][n×{s16,str,s16,str,str}]` (Serialize @0x80ccd50) |
| 20 | ServerChangeWorld | `[i32]` | lookup `worlds` + `SaveLastWorld` (id%100≠0) + `CNC_T_ServerGetTicket` → 65 `ClientReceiveTicketToWorld` = 22 octets CTicket (Serialize @0x80cc570) |
| 21 | ServerChangeWorldAck | vide | `AccountCrossLogoutAck` (billing gateway — log seul ici) |
| 26 | ServerCaptchaReturn | `[str][u8]` | Execute natif = return (no-op) |
| 28 | ServerIllPluginInfo | `[str×8][i32][str×2][s16]` | purge map interne, rien émis → log |
| 29 | ServerUpdateWorldServers | vide | `GetWorldServerStates` (SELECT worlds) → 66 `[s16][n×{s16,str,str,s16,i32,s16×5}][s16][u8]` (Serialize @0x80cce00) |
| 30 | ServerCheckHasCharPassword | `[i32]` | → 75 `[i32]` (char_password non vide) |
| 31 | ServerCharPassword | `[i32][s16][str]` | `DoCharPassword` @0x8158c30 → 76 `[s16]` (0=verify→0/-1, 3=set, sinon echo op) |

## SCharacter (fill `IsAccountHaveCharacter` @0x809fb50)

+0xc level, +0xe node_id, +0x10 race_id, +0x12 class_id, +0x14 class_lv,
+0x16 privilege — utilisé pour le record LFG.

## Implémentation

- [x] `CharacterRepository` : `GetCharPasswordAsync`, `SetCharPasswordAsync`,
      `GetExploitRankAsync` (top 50 `total_exploit`), `GetLfgSummaryAsync`.
- [x] `AccountRepository` : `GetWorldServersAsync` (fnaccount.worlds).
- [x] `MiscHandlers.cs` : registre `LfgCharacters` (map charId→LfgEntry,
      in-memory comme le natif) + `FilterLfg` (sémantique native) +
      handlers 8/11/19/20/21/26/28/29/30/31.
- [x] `Program.cs` : register + `Tickets=ticketClient` + unregister LFG à la déco.
- [x] Tests : 9 cas `LfgFilterTests` — 833/833 verts.

## Limites assumées

- ChangeWorldAck : `AccountCrossLogoutAck` notifie la billing gateway —
  pas de gateway billing dans notre stack → log seul (le client coupe
  lui-même en migrant).
- Entrée LFG : snapshot `player_characters` à l'inscription (le natif
  lit le SCharacter live mis à jour par pushes zone→monde).
- ExploitRank : lecture directe top 50 `total_exploit` au lieu de la
  map cachée périodique native — même observable.
