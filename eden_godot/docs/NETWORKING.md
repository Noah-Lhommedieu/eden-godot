# Couche réseau — Eden Client Reborn

État : **fonctionnel end-to-end** contre le serveur C# local
(`$EDEN/tools/start-local.sh`) — trace complète :
`docs/netsmoke_trace.log` (login → monde → entrée zone, serveur
streaming des NCIDs d'entrée).

## Transport (`godot_project/scripts/net/`)

- `Rc4.cs` — RC4 flux continu par direction, clé session 5 octets
  (miroir `Eden.Core/Networking/Rc4.cs`, vecteur connu testé).
- `BinReader.cs` / `BinWriter.cs` — little-endian ; string =
  `u16 len` + octets Big5 (CP950, `CodePagesEncodingProvider`).
  Miroirs de `InBinStream`/`OutBinStream`.
- `NetMessage.cs` — base abstraite `Opcode`/`Name`/`Serialize`/
  `Deserialize`, `ToPayload()` = `[u16 NCID][corps]`.
- `EdenConnection.cs` — TCP async ; handshake
  `[u32 n_len LE][u32 e_len LE][n BE][e BE]` → 256 octets RSA
  PKCS#1 v1.5 type 2 de la clé RC4 ; trames `[u16 len LE][RC4(payload)]` ;
  boucle de réception → `Channel<Frame>`.

## Messages (`godot_project/scripts/protocol/`)

Sources de preuve notées en commentaire dans chaque classe
(fichier `.g.cs` + ligne de capture).

### Login :6543 (`LoginMessages.cs`)
| NCID | Dir | Classe | Wire |
|---|---|---|---|
| 0 | S→C | LoginServerHello | `[str]` |
| 1 | C→S | LoginAccount | `[str account][str md5(pwd)][str version]` |
| 2 | C→S | LoginWorldSelect | `[i16 worldId][str account][str md5("")]` |
| 2 | S→C | LoginWorldReply | `[i16 worldId][str ip]` |
| 3 | S→C | LoginClientHello | `[f32]` |
| 4 | S→C | LoginFail | `[i16 code]` |
| 5 | S→C | WorldList | `[i16 count][entries×10 champs][i16 lastWorld]` |
| 6 | S→C | WorldTicket | `[22B CTicket]` |

### Ticket :2067 (`TicketMessages.cs`)
Layouts wire réels (les `.g.cs` ticket sont incomplets — référence
`eden_session.py::get_ticket` + mitm) :
NCID 0 GetTicket `[u32×4][u16]` ; NCID 1 UseTicket / NCID 2 GetTicketReply
`[u32×4][u16][u32 t1][u32 t2]` ; NCID 3 UseTicketReply `[u32][u8]`.
Non utilisés dans le flux standard : le ticket monde arrive via
login NCID 6 et le ticket zone via world NCID 37.

### World :5200 (`WorldMessages.cs`)
C→S : 1 WorldLoginTicket `[22B][str machineId]`, 3 SelectCharacter
`[u32][str][u8]`, 10 QueryFamilyInfo `[u8]`, 13 QueryFriendInfo (vide),
17 ItemMallReceivableQuery `[u16][str][str]`, 25 ItemMallPointInfo
(vide), 26 SelectZone `[str][u8]` ("3966").
S→C : 34 ClientHello `[f32]`, 37 ZoneTicket `[22B]`, 38 CharacterListChunk
`[u8][i16 header][entries]` (vérifié octet par octet, test round-trip),
40 ResetTime, 60 UpdatePrestigeData, 64 ItemMallPointInfo, 69
CharacterDataChunk `[i32×5][str blob]`, 72 ApexBuffer.

### Zone :5201 (`ZoneMessages.cs`)
C→S : 0 UseTicket `[22B]`, 1 MoveSelf, 4 MigrationDone, 10 TalkToNpc,
11 ReplyDialog, 14 PlayerAction, 62 GetStorageData, 87 MissionListOp,
104 SearchOpt, 123 TurnSelf, 124 TargetUpdate, 164 UpdateBotData,
186 SetMissionTrace `[u16][i32×n]`, 218 NewPlayerGuide, 258 DailyAwardOp,
558 QueryRecommendActivities, + `ZoneEmptyRequest` ×23 requêtes vides
(57, 26, 24, 53, 110, 113, 195, 243, 256, 133, 25, 50, 156, 152, 162,
166, 165, 181, 204, 213, 223, 172, 63).
S→C : 473 ClientHello, 302 FlagModify, 313 EnterNodeNodeData,
404 MailReceive — le reste est logué brut (dispatch raw par défaut
via les évènements `EdenSession.Frame`).

## Session (`scripts/session/EdenSession.cs`)

`RunFullAsync()` enchaîne : ConnectGateway → Login → SelectWorld →
WorldLogin → ReceiveCharacterList → SelectCharacter → GetZoneTicket →
ZoneConnect → ZoneEntry (burst dans l'ordre exact du zsniff) →
ListenZone (compteurs NCIDs).

## Divergences vs capture native (toutes = côté serveur C#)

- `LoginWorldSelect` envoie 3 champs sur le wire alors que le `.g.cs`
  n'en documente que 2 (le serveur ignore le reliquat) — reproduit
  fidèlement.
- Le World C# envoie le ticket zone (NCID 37) **dès la sélection de
  perso**, avant le NCID 26 du client (le natif attend le 26) — géré
  par cache du ticket.
- Pas de chunks NCID 69 ni NCID 5 auxiliaire observés contre le
  serveur C# ; NCID 2032 reçu en zone (inconnu, logué brut).

## Non implémenté

- Canal auxiliaire zone (2e connexion, NCID 1→3 façon ticket) —
  inutile : l'entrée fonctionne sans (comme `eden_session.py`).
- Heartbeat/keepalive (pas observé comme requis dans la fenêtre testée).
- ~1200 NCIDs zone hors entrée — dispatch brut les logge.
- Mot de passe compte local : `admin`/`adminmdp` (et non `admin/admin` —
  le seed `Database/login-poc.sql` stocke `adminmdp` ; md5 envoyé =
  `17c725af38777cf3ac4bf34e39f44caa`, identique à la capture native).

## Vérifications

- `dotnet build godot_project` : 0 erreur / 0 warning.
- `godot --headless --editor --import` : OK (assets sous `.gdignore`).
- `dotnet test tools/NetTests` : 9/9 (RC4 vecteur connu, continuité
  stream, roundtrips BinStream, blob RSA 256o + padding type 2 vérifié
  par déchiffrement brut, goldens byte-à-byte : LoginAccount,
  LoginWorldSelect, WorldLoginTicket, CharacterListChunk).
- `tools/NetSmoke` : `dotnet run -- --account admin --password adminmdp`
  → entrée zone complète, NCIDs reçus conformes au zsniff
  (473, 302, 310, 311, 313, 351, 593, 609, 280, 298, 348, 472, 482,
  489, 592, 615…).

## Mode live du client Godot (24 oct. — lot « entités réseau »)

`game.tscn` branché sur la session réelle : `godot --path godot_project --
--live [--host= --account=admin --password=adminmdp --char-id=N
--world-id=N]`. `EdenSession.EnterZoneAsync` enchaîne login→world→zone
(connexion zone laissée ouverte), `ZoneListenLoopAsync` consomme le flux,
`OnZone(ncid, handler)` dispatche par NCID (handlers hors thread Godot —
GameScene reposte via file + `_Process`).

### Entités (`scripts/protocol/ZoneEntityMessages.cs` + `world/EntityRegistry.cs`)

| NCID | Classe | Rôle | Statut |
|---|---|---|---|
| 298 | `ZoneSeeMob` | spawn mob (kind 12) + NPC (kind 8) — masque u16 | GOLDEN, round-trip capture |
| 265 | `ZoneSeeChar` | spawn joueur (handle=charId, layout codec serveur) | PASSTHRU |
| 326 | `ZoneMovePrediction` | move entité, pos ×16, dir half-float | GOLDEN |
| 264 | `ZoneMoveChar` | écho position self [f32 x][f32 y] | GOLDEN |
| 267 | `ZoneClientGoto` | déplacement forcé (rép. TargetUpdate) | capture |
| 269 | `ZoneLifeEradicate` | despawn sight [i32] | DESASM |
| 294 | `ZoneLeaveNode` | despawn [i32] | NON PROUVE |
| 293 | `ZoneNodeExit` | pré-migration → purge registre | GOLDEN |
| 329/330 | `ZoneSee(Treasure|Disappear)` | trésors au sol | DESASM |
| 280 | `ZoneUpdateAttr` | attrs entité (attr 0 = HP) | GOLDEN |
| 296 | `ZoneAttrRefresh` | stats self à l'entrée (corps 96B) | GOLDEN |
| 283 | `ZoneModelModify` | changement d'apparence | GOLDEN |
| 271 | `ZoneDisplayMonster` | changement d'owner mob (variable) | GOLDEN |
| 380 | `ZoneMobTransform` | re-template mob | NON PROUVE |
| 536 | `ZoneUpdateZoneMonster` | paramètres mob | NON PROUVE |
| 310/311/312 | `ZoneEnterNode*` | données self (préfixe 310 = [u32 charId][str name], reste brut) | INCOMPLET |
| 313 | `ZoneEnterNodeNodeData` | `[s16 nodeId][u8][f32 x,y,z]` → bascule map S<Field1> + pos self | GOLDEN |
| C→S 1 | `ZoneMoveSelf` | `[s16 0][f32 x][y][dstX][dstY][dir][s16 7]` throttlé 10 Hz | bytes natifs |

### Sémantiques prouvées par les captures/désasm

- SeeMob floats = `[x][y][dir ent+0x58][moveSpeed vcall+0xd8]` (ctor
  natif @0x849df90) — le serveur C# y écrit (e.Z, facing) : le client
  prend le premier non-nul comme yaw (HYPOTHESIS tolérante).
- MovePrediction positions = unités monde ×16 (u16), facing =
  demi-flottant IEEE 754 (`FacingFloat` décode).
- Handles : NPC `0x88C8xxxx`, mobs `0xFFFFxxxx`, joueurs = charId.
- Modèles : scene `[reborn_monsters]` col 12 / `[npc]` col 5 portent le
  stem (`M025`, `M014`) ; c_monster.ini col 1 (`M025010`) → stem en
  tronquant les 2 derniers chiffres (convention monsterlist/npclist
  `mNNN.kfm`) → `monster/model/<stem>.glb` via AssetCatalog ; capsule
  colorée sinon (joueur rose / NPC vert / mob rouge).
- `ZoneEmptyRequest`s et le burst d'entrée restent identiques.

### Vécu live (`docs/livesmoke_trace.log`, `NetSmoke --zone-entry --move-test`)

- Entrée node 2 (S002), pos self (179.33, 284.27, 0) — `test1` #200000002.
- Reçus : 298×62 (21 NPC + 41 mobs), 326×89-139, 280×63-65, 269×15,
  264×3 (après 3 MoveSelf test), + les NCIDs d'état attendus.
- NetSmoke stats : 243-286 trames entité parsées, **0 erreur**.
- Headless Godot `--live` : bascule S009→S002, 50+ entités spawnées,
  despawns sight (269) appliqués, aucune exception.
- MoveSelf accepté par le serveur (écho 264 par envoi, pas d'erreur
  dans /tmp/eden-zone.log).
