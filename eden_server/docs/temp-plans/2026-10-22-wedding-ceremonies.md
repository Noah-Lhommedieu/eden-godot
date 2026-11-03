# 2026-10-22 — Cérémonies de mariage natives (relais Mission↔Zone)

## Symptômes
- `SocialHandlers.WeddingInfoQuery` (client op 251) envoie un
  `CNC_CZ_ClientWeddingsInfo` **vide** en local — le natif relaie à la
  mission, jamais de réponse locale.
- Aucune cérémonie planifiée (`CWeddingFactory`) côté mission : les
  commandes DBA `WeddingInfoQuery`, `ReserverChuch`, `CloseWedding` ne
  sont pas routées.

## Preuves natives (déjà relues byte-par-byte)
- `CNE_CZ_ZoneServerWeddingInfoQuery::Execute` (zone @0x82xxxxx) :
  `CDBAgent::Command(charUid, "WeddingInfoQuery 1")` → NCID 21
  `AgentDBACommand` — aucune autre action locale.
- `TC_WeddingInfoQuery` → `CWeddingFactory::WeddinginfoQuery(char,flag)`
  (mission @0x80dac00) : flag→set d'abonnés (rb-tree +0x10), refresh
  `+0x14=now`, `+0x18=stepEnd-now`, émet `CNC_ZMis_ZoneServerWeddingsInfo`.
- `CNC_ZMis_ZoneServerWeddingsInfo::Serialize` (mission @0x80db220) :
  `[i32 requester][u8 n]` puis n×`[i32 loveId][str name1][str name2]
  [u8 status][i32 startTime][i32 remainingTime]` — wire ncid **193**
  (`docs/generated/ncid_map.json`).
- `CNE_ZMis_ZoneServerWeddingsInfo::Execute` (zone @0x833ffxx) :
  pour chaque rec 6-champs → `leftTime = +0x18 - (now - +0x14)` ;
  émet `CNC_CZ_ClientWeddingsInfo` (599) `[u8 n][n×{i32,str,str,u8,i32}]`.
- `CWeddingFactory::CreateWedding` + `CWedding` ctor (@0x81dbb30) :
  `+4`=love_id (id du couple), `+8`=status u8, `+0xc`=applicant,
  `+0x10`=date du dernier changement de status, `+0x1c/+0x20`=noms.
- `TC_ReserverChurch` (verbe .rodata **`ReserverChuch`**, @0x81ec6c0) :
  <5 mariages, pas de mariage pour le couple (exec+0x600 = love_id),
  item **0x667f** (carte d'invitation) en inventaire → `CreateWedding`.
  Textes : 0x1c61 plein, 0x1c68 déjà réservé, 0x1c62 carte manquante,
  0x1c63 succès — via `CGlobalInfo::TextOutputIndex(char, node, id, 0x9b)`
  → ZMis ncid 66 `[i32 char][s16 node][s16 textId][str ""][u8 0][u8 flag]`.
- `TC_CloseWedding` (@0x81ec900) : mariage du couple status==1 →
  `OnEventTimeUp(id,3)` (fermeture forcée) ; sinon 0x1c64 / 0x1c69.
- `CEventWedding`/`OnEventTimeUp` (@0x80da830) : status 0→1 immédiat,
  1→2 à +600s (exige ≥2 amants « in node » sinon → 3), 2→3 à +7200s,
  3→CloseWedding à +600s ; `Weddinginfo()` rediffuse la liste à tous
  les abonnés après chaque transition.
- `CWedding::SetStatus` : reset `+0x10=now` + broadcast
  `MW_WorldTextCommand "WeddingOper Id=%d oper=%d applicant_id=%d"`.

## Hors scope (lot suivant)
`VisitWedding` (téléport salle via MW_WorldTextCommand → monde),
`PrintedInvitations`, `GetMarry`, `Divorce`, `MarryOper`,
`DisbandMarriage`, `CWedding::Join` (entrée des invités dans le node —
le check « ≥2 amants » est contourné : voir WeddingService).

## Éditions
- [x] `Eden.Mission/Services/WeddingService.cs` — fabrique mémoire
      (≤5), abonnés, machine à status (PeriodicTimer), build ZMis 193.
- [x] `Eden.Mission/Services/MissionDb.cs` — `GetCoupleAsync`,
      `GetCharacterNameAsync` (player_couples/player_characters, fndb1).
- [x] `Eden.Mission/Services/MissionServer.cs` — service + cas DBA
      `WeddingInfoQuery`/`ReserverChuch`/`CloseWedding` + TextOutputIndex
      (ZMis 66).
- [x] `Eden.Zone/Codecs/WeddingPackets.cs` — parse ZMis 193 + build 599.
- [x] `Eden.Zone/ZoneEntryFlow.Mission.cs` — case 193 → 599 au requester.
- [x] `Eden.Zone/Handlers/SocialHandlers.cs` — 251 → AgentDBA
      `"WeddingInfoQuery 1"` ; fallback `599 [u8 0]` sans mission.
- [x] `server/tests/Eden.Tests/WeddingServiceTests.cs` — 11 tests :
      codec byte-par-byte + machine à status.
- [x] MAJ `docs/MASTER-IMPLEMENTATION-PLAN.md` + commit.

## Résultat
- 11/11 tests verts ; suite complète 319/328 — les 9 échecs sont la
  baseline pré-existante (zéro régression).
- Écart restant vs natif : `VisitWedding`/`CWedding::Join` (le node
  mariage n'existe pas — proxy présence = amants connectés) ;
  `MW_WorldTextCommand "WeddingOper …"` non émis (lien monde absent
  côté mission) — traçable dans le master plan.
