# Protocol map — opcodes client ↔ serveur vs paquets natifs

Inventaire des NCIDs échangés entre le client Godot
(`eden_godot/godot_project/scripts/protocol/`, `scripts/net/`,
`scripts/session/`) et le serveur C# (`eden_server/server/src/`),
rapprochés des identifiants natifs extraits des binaires
`server_us` (`eden_server/docs/generated/ncid_map.json` — produit par
`tools/extract_ncids.py` depuis `InitPCCommandTable`).

Généré par lecture statique (pas de build). Date : 2025-11 (état du
dépôt). Colonnes :

- **opcode** : numéro sur le fil (u16 NCID) — côté **zone et ticket,
  wire = NCID natif** ; côté **login et monde, un bloc d'entrée a été
  renuméroté** par le portage (le natif est alors rappelé entre
  parenthèses dans la colonne `natif_si_connu`).
- **direction** : `C→S` client→serveur, `S→C` serveur→client.
- **nom_client** : classe `NetMessage` du client Godot
  (`scripts/protocol/*.cs`) ; `—` = aucune classe cliente.
- **handler_serveur** : point de traitement côté serveur
  (`fichier:ligne`, chemins relatifs à `eden_server/`). Pour C→S :
  `case` du switch `ZoneEntryFlow.TryHandleOnLoopAsync`
  (`Eden.Zone/ZoneEntryFlow.cs`), site `d.Register[Char]` des
  `Eden.*/Handlers/*.cs` ou `Program.cs`, `EntryReplayData.cs`
  (réponses rejouées depuis captures). Pour S→C : premier site
  d'émission `new CNC_*`/`SendPacketAsync` (`×N` = N sites).
- **natif_si_connu** : nom de classe natif (`CNE_*` = événement
  client→serveur, `CNC_*` = commande traitée côté client). Vide =
  extension non native ou id inconnu — non inventé.
- **statut** : `implémenté` (handler/émetteur réel) · `partiel`
  (replay de capture, acquittement sans traitement, squelette) ·
  `absent` (paquet connu mais non géré côté serveur).
  Marques jointes : `client émet` = le client Godot envoie ce paquet ;
  `⟂ orphelin` = handler serveur que le client n'émet jamais ;
  `client✓/client∅/client —` = handler client enregistré /
  classe cliente seulement parsable / aucune classe cliente.

## Canaux et numérotation

| Canal | Port | C→S | S→C | Numérotation |
|---|---|---|---|---|
| Login | 6543 | `CNC_CL_Server*` (natif 35–37) | `CNC_CL_Client*` (38–41) | wire 0–6 = natif − 35 |
| Ticket | 2067 | `CNC_T_Server*` | `CNC_T_Client*` | wire = natif (0–3) |
| Monde | 5200 | `CNE_CW_Server*` (35) | `CNC_CW_Client*` (49) | 20 opcodes d'entrée renumérotés (natif entre parenthèses) |
| Zone | 5201 | `CNE_CZ_*` (262 natifs) | `CNC_CZ_*` (359 natifs) | wire = NCID natif |
| Gateway | (démo) | customs 0–3 | — | non natif (stub hors chemin client) |

Dispatch côté serveur : le canal zone passe d'abord par
`ZoneEntryFlow.TryHandleOnLoopAsync` (switch ~125 `case`,
`Eden.Zone/ZoneEntryFlow.cs:474`), puis les opcodes non traités par
`PacketDispatcher.DispatchAsync` (`d.Register[Char]`). Le monde et le
login n'ont que le `PacketDispatcher`. Le ticket a un switch brut
(`TicketSession.Dispatch`).

## Login :6543 — client ↔ Eden.Login

| opcode | direction | nom_client | handler_serveur | natif_si_connu | statut |
|---:|---|---|---|---|---|
| 0 | C→S | LoginServerHello | Eden.Login/Program.cs:80 | CNC_CL_ServerHello (35) | implémenté · client émet |
| 1 | C→S | LoginAccount | Eden.Login/Program.cs:83 | CNC_CL_ServerLoginAccount (36) | implémenté · client émet |
| 2 | C→S | LoginWorldSelect | Eden.Login/Program.cs:86 | CNC_CL_ServerLoginWorld (37) | implémenté · client émet |
| 2 | S→C | LoginWorldReply | Eden.Login/Handlers/LoginPacketHandlers.cs (écho ServerLoginWorld) | CNC_CL_ServerLoginWorld (37) | implémenté |
| 3 | S→C | LoginClientHello | Eden.Login/Handlers/LoginPacketHandlers.cs (push post-crypto) | CNC_CL_ClientHello (38) | implémenté |
| 4 | S→C | LoginFail | Eden.Login/Handlers/LoginPacketHandlers.cs | CNC_CL_ClientLoginFail (39) | implémenté |
| 5 | S→C | WorldList | Eden.Login/Handlers/LoginPacketHandlers.cs | CNC_CL_ClientGetWorldServers (40) | implémenté |
| 6 | S→C | WorldTicket | Eden.Login/Handlers/LoginPacketHandlers.cs | CNC_CL_ClientReceiveTicketToWorldServer (41) | implémenté |

Non utilisés : `CNC_CL_ServerSCardVerify` (254) /
`CNC_CL_ClientSCardInfo` (255) / `CNC_G_ServerSCardVerify` (253) —
classes SCard générées, jamais câblées.

## Ticket :2067 — client ↔ Eden.Ticket

| opcode | direction | nom_client | handler_serveur | natif_si_connu | statut |
|---:|---|---|---|---|---|
| 0 | C→S | GetTicket | Eden.Ticket/Services/TicketSession.cs:112 | CNC_T_ServerGetTicket | implémenté · ⟂ orphelin (client ne l'appelle pas) |
| 1 | C→S | UseTicket | Eden.Ticket/Services/TicketSession.cs:129 | CNC_T_ServerUseTicket | implémenté · ⟂ orphelin (client ne l'appelle pas) |
| 2 | S→C | GetTicketReply | Eden.Ticket/Services/TicketSession.cs:121 | CNC_T_ClientGetTicket | implémenté |
| 3 | S→C | UseTicketReply | Eden.Ticket/Services/TicketSession.cs:136 | CNC_T_ClientUseTicket | implémenté |

Le service ticket n'est pas appelé dans le flux standard du client
(`eden_godot/docs/NETWORKING.md`) : le ticket monde arrive via login
NCID 6 et le ticket zone via monde NCID 37. Natifs inutilisés :
`CNC_T_ServerCheckPin` (4) / `CNC_T_ClientCheckPin` (5).

## Monde :5200 — client ↔ Eden.World

### C→S (35 opcodes natifs `CNE_CW_*` — 35/35 implémentés)

| opcode | direction | nom_client | handler_serveur | natif_si_connu | statut |
|---:|---|---|---|---|---|
| 0 | C→S | WorldServerHello | Eden.World/Program.cs:107 | CNE_CW_ServerHello (84) | implémenté · client émet |
| 1 | C→S | WorldLoginTicket | Eden.World/Program.cs:121 | CNE_CW_ServerReceiveTicket (85) | implémenté · client émet |
| 2 | C→S | — | Eden.World/Program.cs:344 | CNE_CW_ServerKeepAlive (86) | implémenté · ⟂ orphelin |
| 3 | C→S | WorldSelectCharacter | Eden.World/Program.cs:198 | CNE_CW_ServerLoginCharacter (87) | implémenté · client émet |
| 4 | C→S | WorldCreateCharacterName | Eden.World/Program.cs:382 | CNE_CW_ServerCreateCharacterName (88) | implémenté · client émet |
| 5 | C→S | WorldCreateCharacterData | Eden.World/Program.cs:399 | CNE_CW_ServerCreateCharacterData (89) | implémenté · client émet |
| 6 | C→S | WorldRemoveCharacter | Eden.World/Program.cs:484 | CNE_CW_ServerRemoveCharacter (90) | implémenté · client émet |
| 7 | C→S | WorldCancelCreateCharacter | Eden.World/Program.cs:476 | CNE_CW_ServerCancelCreateCharacterReq (91) | implémenté · client émet |
| 8 | C→S | — | Eden.World/Handlers/MiscHandlers.cs:191 | CNE_CW_ServerLookingForMember | implémenté · ⟂ orphelin |
| 9 | C→S | WorldFamilyOp | Eden.World/Handlers/FamilyHandlers.cs:51 | CNE_CW_ServerFamilyOp | implémenté · ⟂ orphelin |
| 10 | C→S | WorldQueryFamilyInfo | Eden.World/Handlers/FamilyHandlers.cs:53 | CNE_CW_ServerQueryFamilyInfo | implémenté · client émet |
| 11 | C→S | — | Eden.World/Handlers/MiscHandlers.cs:213 | CNE_CW_ServerLookingForGroup | implémenté · ⟂ orphelin |
| 12 | C→S | WorldAddFriend | Eden.World/Handlers/FriendHandlers.cs:58 | CNE_CW_ServerAddFriend | implémenté · client émet |
| 13 | C→S | WorldQueryFriendInfo | Eden.World/Handlers/FriendHandlers.cs:182 | CNE_CW_ServerQueryFriendInfo | implémenté · client émet |
| 14 | C→S | WorldFriendInvitationAnswer | Eden.World/Handlers/FriendHandlers.cs:121 | CNE_CW_ServerFriendInvitation | implémenté · client émet |
| 15 | C→S | — | Eden.World/Handlers/ItemMallHandlers.cs:42 | CNE_CW_ServerItemMallAnythingReceivable | implémenté · ⟂ orphelin |
| 17 | C→S | WorldItemMallReceivableQuery | Eden.World/Handlers/ItemMallHandlers.cs:54 | CNE_CW_ServerItemMallReceivableQuery | implémenté · client émet |
| 18 | C→S | WorldItemMallReceiveAttempt | Eden.World/Handlers/ItemMallHandlers.cs:66 | CNE_CW_ServerItemMallReceieveAttempt | implémenté · client émet |
| 19 | C→S | — | Eden.World/Handlers/MiscHandlers.cs:113 | CNE_CW_ServerQueryExploitRank | implémenté · ⟂ orphelin |
| 20 | C→S | — | Eden.World/Handlers/MiscHandlers.cs:227 | CNE_CW_ServerChangeWorld | implémenté · ⟂ orphelin |
| 21 | C→S | — | Eden.World/Handlers/MiscHandlers.cs:262 | CNE_CW_ServerChangeWorldAck | implémenté · ⟂ orphelin |
| 22 | C→S | WorldRemoveFriend | Eden.World/Handlers/FriendHandlers.cs:163 | CNE_CW_ServerRemoveFriend | implémenté · client émet |
| 23 | C→S | WorldItemMallBuyRequest | Eden.World/Handlers/ItemMallHandlers.cs:106 | CNE_CW_ServerItemMallReqBuyItem | implémenté · client émet |
| 24 | C→S | WorldItemMallListQuery | Eden.World/Handlers/ItemMallHandlers.cs:113 | CNE_CW_ServerItemMallListQuery | implémenté · client émet |
| 25 | C→S | WorldItemMallPointInfoQuery | Eden.World/Handlers/ItemMallHandlers.cs:120 | CNE_CW_ServerItemMallPointInfo | implémenté · client émet |
| 26 | C→S | WorldSelectZone | Eden.World/Handlers/MiscHandlers.cs:132 | CNE_CW_ServerCaptchaReturn | implémenté · client émet |
| 27 | C→S | — | Eden.World/Handlers/MutuallyHelpHandlers.cs:54 | CNE_CW_ServerPlayerState (92) | implémenté · ⟂ orphelin |
| 28 | C→S | — | Eden.World/Handlers/MiscHandlers.cs:136 | CNE_CW_ServerIllPluginInfo | implémenté · ⟂ orphelin |
| 29 | C→S | — | Eden.World/Handlers/MiscHandlers.cs:145 | CNE_CW_ServerUpdateWorldServers | implémenté · ⟂ orphelin |
| 30 | C→S | WorldCheckCharPassword | Eden.World/Handlers/MiscHandlers.cs:170 | CNE_CW_ServerCheckHasCharPassword | implémenté · client émet |
| 31 | C→S | WorldCharPassword | Eden.World/Handlers/MiscHandlers.cs:180 | CNE_CW_ServerCharPassword | implémenté · client émet |
| 32 | C→S | WorldCreateNameCheck | Eden.World/Program.cs:368 | CNE_CW_ServerCreateCharacterNameCheck (103) | implémenté · client émet |
| 33 | C→S | — | Eden.World/Handlers/MutuallyHelpHandlers.cs:67 | CNE_CW_ServerMutuallyHelpSearch | implémenté · ⟂ orphelin |
| 71 | C→S | — | Eden.World/Program.cs:494 | CNE_CW_ServerApexBufferTransfer | implémenté · ⟂ orphelin |
| 73 | C→S | WorldQueryFamilyEmblem | Eden.World/Handlers/FamilyHandlers.cs:55 | CNE_CW_ServerQueryFamilyEmblem | implémenté · ⟂ orphelin |

Note 26 : `WorldSelectZone` (client) réutilise le NCID natif
`CNE_CW_ServerCaptchaReturn` — `Execute` natif vide, no-op fidèle
(`MiscHandlers.cs:132`). La sélection de zone effective passe par
`CNE_CW_ServerChangeWorld`/ticket.

### S→C (50 classes `CNC_CW_*` dont 1 custom — 25/50 émis)

| opcode | direction | nom_client | handler_serveur | natif_si_connu | statut |
|---:|---|---|---|---|---|
| 0 | S→C | — | — | CNC_CW_ClientForceKick | absent · client — |
| 16 | S→C | — | — | CNC_CW_ServerItemMallReceivableList | absent · client — |
| 34 | S→C | WorldClientHello | Eden.World/Program.cs:112 ×2 | CNC_CW_ClientHello (93) | implémenté · client✓ |
| 35 | S→C | — | Eden.World/Program.cs:140 ×5 | CNC_CW_ClientTicketInvalid (94) | implémenté · client — |
| 36 | S→C | — | — | CNC_CW_ClientTextOutput (95) | absent · client — |
| 37 | S→C | ZoneTicket | Eden.World/Program.cs:284 | CNC_CW_ClientReceiveTicketToZoneServer (96) | implémenté · client✓ |
| 38 | S→C | CharacterListChunk | Eden.World/Program.cs:533 ×3 | CNC_CW_ClientGetCharacters (97) | implémenté · client✓ |
| 39 | S→C | WorldVerifyNameResult | Eden.World/Program.cs:363 | CNC_CW_ClientVerifyCharacterName (98) | implémenté · client✓ |
| 40 | S→C | WorldResetTime | Eden.World/Program.cs:248 ×2 | CNC_CW_ClientResetTime | implémenté · client✓ |
| 41 | S→C | WorldCancelCreateCharacterResp, WorldClientCancelCreateCharacterResp | Eden.World/Program.cs:480 | CNC_CW_ClientCancelCreateCharacterResp | implémenté · client✓ |
| 42 | S→C | WorldClientTell | — | CNC_CW_ClientTell | absent · client✓ |
| 43 | S→C | WorldClientTextOutputIndex | Eden.World/ZoneLinkServer.cs:645 | CNC_CW_ClientTextOutputIndex | implémenté · client✓ |
| 44 | S→C | — | — | CNC_CW_ClientChannelMsg (100) | absent · client — |
| 45 | S→C | WorldClientLookingForMemberResult | Eden.World/Handlers/MiscHandlers.cs:207 | CNC_CW_ClientLookingForMemberResult | implémenté · client✓ |
| 46 | S→C | WorldUpdateFamilyMember, WorldFamilyMemberUpdate | — | CNC_CW_ClientUpdateFamilyMember | absent · client✓ |
| 47 | S→C | WorldFamilyOpException | — | CNC_CW_ClientFamilyOpException | absent · client✓ |
| 48 | S→C | WorldFamilyInvitation | — | CNC_CW_ClientFamilyInvitation | absent · client✓ |
| 49 | S→C | WorldFamilyQuit | — | CNC_CW_ClientFamilyQuit | absent · client✓ |
| 50 | S→C | WorldFamilyInfo | Eden.World/ZoneLinkServer.cs:662 ×2 | CNC_CW_ClientQueryFamilyInfo | implémenté · client✓ |
| 51 | S→C | WorldFamilySchedule | — | CNC_CW_ClientFamilySchedule | absent · client✓ |
| 52 | S→C | WorldUpdateFriendMember | — | CNC_CW_ClientUpdateFriendMember | absent · client✓ |
| 53 | S→C | WorldFriendInvitationNotice | — | CNC_CW_ClientFriendInvitation | absent · client✓ |
| 54 | S→C | WorldRepeatedLogin | — | CNC_CW_ClientRepeatedLogin | absent · client✓ |
| 55 | S→C | — | — | CNC_CW_ClientException (101) | absent · client — |
| 56 | S→C | WorldItemMallAnything | Eden.World/Handlers/ItemMallHandlers.cs:49 ×2 | CNC_CW_ClientItemMallAnythingReceivable | implémenté · client✓ |
| 57 | S→C | WorldItemMallReceivableList | Eden.World/Handlers/ItemMallHandlers.cs:61 | CNC_CW_ClientItemMallReceivableQuery | implémenté · client✓ |
| 58 | S→C | WorldItemMallReceiveResult | Eden.World/Handlers/ItemMallHandlers.cs:79 ×3 | CNC_CW_ClientItemMallReceieveAttempt | implémenté · client✓ |
| 59 | S→C | WorldClientQueryExploitRank | Eden.World/Handlers/MiscHandlers.cs:128 | CNC_CW_ClientQueryExploitRank | implémenté · client✓ |
| 60 | S→C | WorldUpdatePrestigeData | Eden.World/Handlers/FamilyHandlers.cs:575 ×2 | CNC_CW_ClientUpdatePrestigeData | implémenté · client✓ |
| 61 | S→C | WorldUpdateBlackList | — | CNC_CW_ClientUpdateBlackList | absent · client✓ |
| 62 | S→C | WorldItemMallList | Eden.World/Handlers/ItemMallHandlers.cs:255 | CNC_CW_ClientItemMallList | implémenté · client✓ |
| 63 | S→C | WorldItemMallBuyResult | Eden.World/Handlers/ItemMallHandlers.cs:146 | CNC_CW_ClientItemMallReqBuyItem | implémenté · client✓ |
| 64 | S→C | WorldItemMallPointInfo | Eden.World/Handlers/ItemMallHandlers.cs:124 | CNC_CW_ClientItemMallPointInfo | implémenté · client✓ |
| 65 | S→C | WorldClientReceiveTicketToWorld | Eden.World/Handlers/MiscHandlers.cs:256 | CNC_CW_ClientReceiveTicketToWorld | implémenté · client✓ |
| 66 | S→C | — | Eden.World/Handlers/MiscHandlers.cs:166 | CNC_CW_ClientGetWorldServers (102) | implémenté · client — |
| 67 | S→C | WorldClientFamilyAchievementUpdate | — | CNC_CW_ClientFamilyAchievementUpdate | absent · client✓ |
| 68 | S→C | WorldFamilyLogUpdate | — | CNC_CW_ClientFamilyLogUpdate | absent · client✓ |
| 69 | S→C | CharacterDataChunk | — | CNC_CW_ClientCaptcha (99) | absent · client✓ |
| 70 | S→C | WorldClientCrossZone | — | CNC_CW_ClientCrossZone | absent · client✓ |
| 72 | S→C | WorldApexBuffer, WorldClientApexBufferTransfer | Eden.World/Program.cs:253 | CNC_CW_ClientApexBufferTransfer | implémenté · client✓ |
| 74 | S→C | WorldFamilyEmblem | — | CNC_CW_ClientFamilyEmblem | absent · client✓ |
| 75 | S→C | WorldCheckCharPasswordResult, WorldClientCheckHasCharPassword | Eden.World/Handlers/MiscHandlers.cs:175 | CNC_CW_ClientCheckHasCharPassword | implémenté · client✓ |
| 76 | S→C | WorldCharPasswordResult, WorldClientCharPassword | Eden.World/Handlers/MiscHandlers.cs:186 | CNC_CW_ClientCharPassword | implémenté · client✓ |
| 77 | S→C | WorldClientSelectCharEnableBtn | — | CNC_CW_ClientSelectCharEnableBtn | absent · client✓ |
| 78 | S→C | WorldUpdateSpecialCoin | Eden.World/Handlers/FriendHandlers.cs:292 ×3 | CNC_CW_ClientUpdateSpecialCoin | implémenté · client✓ |
| 79 | S→C | WorldItemMallLimit | Eden.World/Handlers/ItemMallHandlers.cs:237 ×2 | CNC_CW_ClientItemMallLimit | implémenté · client✓ |
| 80 | S→C | WorldFamilyRecruitList | — | CNC_CW_ClientQueryFamilyRecruitList | absent · client✓ |
| 81 | S→C | WorldClientMutuallyHelpUpdate | — | CNC_CW_ClientMutuallyHelpUpdate | absent · client✓ |
| 82 | S→C | WorldClientNotifyChangeWorld | — | CNC_CW_ClientNotifyChangeWorld | absent · client✓ |
| 83 | S→C | WorldClientUpdateFortuneBagContentList | — | CNC_CW_ClientUpdateFortuneBagContentList | absent · client✓ |

## Zone :5201 — client ↔ Eden.Zone

### C→S (264 opcodes : 262 natifs `CNE_CZ_*` + customs 623/32001 — 255 implémentés, 9 partiels, 0 absents)

| opcode | direction | nom_client | handler_serveur | natif_si_connu | statut |
|---:|---|---|---|---|---|
| 0 | C→S | ZoneUseTicket | ZoneEntryFlow.cs:476 | CNE_CZ_ZoneServerReceiveTicket | implémenté · client émet |
| 1 | C→S | ZoneMoveSelf | ZoneEntryFlow.cs:498 | CNE_CZ_ZoneServerMoveSelf | implémenté · client émet |
| 2 | C→S | — | ZoneEntryFlow.cs:486 | CNE_CZ_ZoneServerWhoIs | implémenté · ⟂ orphelin |
| 3 | C→S | ZoneTextCommand | ZoneEntryFlow.cs:505 | CNE_CZ_ZoneServerTextCommand | implémenté · client émet |
| 4 | C→S | ZoneMigrationDone | ZoneEntryFlow.cs:510 | CNE_CZ_ZoneServerMigrationDone | implémenté · client émet |
| 5 | C→S | — | ZoneEntryFlow.cs:599 | CNE_CZ_ZoneServerMeleeAttack | implémenté · ⟂ orphelin |
| 6 | C→S | ZoneServerDropItem | Eden.Zone/Handlers/InventoryHandlers.cs:31 | CNE_CZ_ZoneServerDropItem | implémenté · client émet |
| 7 | C→S | ZoneSwitchItemLocation | Eden.Zone/Handlers/InventoryHandlers.cs:88 | CNE_CZ_ZoneServerSwitchItemLocation | implémenté · client émet |
| 8 | C→S | ZoneUpdateEquipment | Eden.Zone/Handlers/InventoryHandlers.cs:188 | CNE_CZ_ZoneServerUpdateEquipment | implémenté · client émet |
| 9 | C→S | ZoneSpellCastToLife | ZoneEntryFlow.cs:603 | CNE_CZ_ZoneServerSpellCastToLife | implémenté · client émet |
| 10 | C→S | ZoneTalkToNpc | ZoneEntryFlow.cs:1162 | CNE_CZ_ZoneServerTalkToNPC | implémenté · client émet |
| 11 | C→S | ZoneReplyDialog | ZoneEntryFlow.cs:1166 | CNE_CZ_ZoneServerReplyDialog | implémenté · client émet |
| 12 | C→S | ZoneUseSkillPoint | Eden.Zone/Handlers/PlayerHandlers.cs:219 | CNE_CZ_ZoneServerUseSkillPoint | implémenté · client émet |
| 13 | C→S | ZoneSpellBreak | ZoneEntryFlow.cs:861 | CNE_CZ_ZoneServerSpellBreak | implémenté · client émet |
| 14 | C→S | ZonePlayerAction | ZoneEntryFlow.cs:1038 | CNE_CZ_ZoneServerPlayerAction | implémenté · client émet |
| 15 | C→S | ZoneCheckNodeExit | ZoneEntryFlow.cs:595 | CNE_CZ_ZoneServerCheckNodeExit | implémenté · client émet |
| 16 | C→S | ZoneSpellCastToLocation | ZoneEntryFlow.cs:625 | CNE_CZ_ZoneServerSpellCastToLocation | implémenté · client émet |
| 17 | C→S | ZoneSpellCastToNowhere | ZoneEntryFlow.cs:634 | CNE_CZ_ZoneServerSpellCastToNowhere | implémenté · client émet |
| 18 | C→S | ZoneEnchantUpdateRequest | ZoneEntryFlow.cs:1158 | CNE_CZ_ZoneServerEnchantUpdate | implémenté · client émet |
| 19 | C→S | ZoneItemUse | Eden.Zone/Handlers/ItemActionHandlers.cs:164 | CNE_CZ_ZoneServerItemUse | implémenté · client émet |
| 20 | C→S | ZoneReviveRequest | ZoneEntryFlow.cs:1145 | CNE_CZ_ZoneServerRevive | implémenté · client émet |
| 21 | C→S | ZoneServerPlayerEmotion | Eden.Zone/Handlers/PlayerHandlers.cs:54 | CNE_CZ_ZoneServerPlayerEmotion | implémenté · ⟂ orphelin |
| 22 | C→S | ZoneBuyItems | ZoneEntryFlow.cs:880 | CNE_CZ_ZoneServerBuyItems | implémenté · client émet |
| 23 | C→S | ZoneSellItems | ZoneEntryFlow.cs:881 | CNE_CZ_ZoneServerSellItems | implémenté · client émet |
| 24 | C→S | ZoneEmptyRequest.EnterNodeEquipAndSpell | EntryReplayData.cs · Eden.Zone/Handlers/EntryHandlers.cs:227 | CNE_CZ_ZoneServerEnterNodeEquipAndSpell | partiel (replay post-entrée ; squelette pré-entrée) · client émet |
| 25 | C→S | ZoneEmptyRequest.EnterNodeInventory | Eden.Zone/Handlers/EntryHandlers.cs:103 | CNE_CZ_ZoneServerEnterNodeInventory | implémenté · client émet |
| 26 | C→S | ZoneEmptyRequest.EnterNodePlayerData | EntryReplayData.cs · Eden.Zone/Handlers/EntryHandlers.cs:67 | CNE_CZ_ZoneServerEnterNodePlayerData | partiel (replay post-entrée ; squelette pré-entrée) · client émet |
| 27 | C→S | ZoneServerSafeQuitReq | Eden.Zone/Handlers/PlayerHandlers.cs:19 | CNE_CZ_ZoneServerSafeQuitReq | implémenté · ⟂ orphelin |
| 28 | C→S | ZoneTeamLeave | Eden.Zone/Handlers/TeamHandlers.cs:62 | CNE_CZ_ZoneServerTeamLeave | implémenté · client émet |
| 29 | C→S | ZoneTeamInvite | Eden.Zone/Handlers/TeamHandlers.cs:19 | CNE_CZ_ZoneServerTeamInvite | implémenté · client émet |
| 30 | C→S | ZoneTeamInviteAnswer | Eden.Zone/Handlers/TeamHandlers.cs:39 | CNE_CZ_ZoneServerTeamInviteAnswer | implémenté · client émet |
| 31 | C→S | ZoneTeamInviteCancel | Eden.Zone/Handlers/TeamHandlers.cs:125 | CNE_CZ_ZoneServerTeamInviteCancel | implémenté · ⟂ orphelin |
| 32 | C→S | — | Eden.Zone/Handlers/PlayerHandlers.cs:29 | CNE_CZ_ZoneServerSafeQuitCancel | implémenté · ⟂ orphelin |
| 33 | C→S | ZoneServerSafeQuitCountDownOver | Eden.Zone/Handlers/PlayerHandlers.cs:34 | CNE_CZ_ZoneServerSafeQuitCountDownOver | implémenté · ⟂ orphelin |
| 34 | C→S | ZoneChannelMsg | ZoneEntryFlow.cs:884 | CNE_CZ_ZoneServerChannelMsg | implémenté · client émet |
| 35 | C→S | ZoneTreasurePickup | ZoneEntryFlow.cs:612 | CNE_CZ_ZoneServerTreasurePickup | implémenté · client émet |
| 36 | C→S | ZoneTradeRequest | Eden.Zone/Handlers/TradeHandlers.cs:28 | CNE_CZ_ZoneServerTradeRequest | implémenté · client émet |
| 37 | C→S | ZoneTradeRequestAccept | Eden.Zone/Handlers/TradeHandlers.cs:81 | CNE_CZ_ZoneServerTradeRequestAccept | implémenté · client émet |
| 38 | C→S | ZoneTradeAddItem | Eden.Zone/Handlers/TradeHandlers.cs:90 | CNE_CZ_ZoneServerTradeAddItem | implémenté · client émet |
| 39 | C→S | ZoneTradeRemoveItem | Eden.Zone/Handlers/TradeHandlers.cs:100 | CNE_CZ_ZoneServerTradeRemoveItem | implémenté · client émet |
| 40 | C→S | ZoneTradeModifyGold | Eden.Zone/Handlers/TradeHandlers.cs:107 | CNE_CZ_ZoneServerTradeModifyGold | implémenté · client émet |
| 41 | C→S | ZoneTradeConfirm | Eden.Zone/Handlers/TradeHandlers.cs:116 | CNE_CZ_ZoneServerTradeConfirm | implémenté · client émet |
| 42 | C→S | ZoneTradeCancel | Eden.Zone/Handlers/TradeHandlers.cs:160 | CNE_CZ_ZoneServerTradeCancel | implémenté · client émet |
| 43 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:295 | CNE_CZ_ZoneServerCloseMissionBrief | implémenté · ⟂ orphelin |
| 44 | C→S | ZoneServerResurrect | ZoneEntryFlow.cs:1146 | CNE_CZ_ZoneServerResurrect | implémenté · ⟂ orphelin |
| 45 | C→S | ZoneServerAskRepairEquipGold | Eden.Zone/Handlers/InventoryHandlers.cs:238 | CNE_CZ_ZoneServerAskRepairEquipGold | implémenté · client émet |
| 46 | C→S | ZoneServerRepairEquipment | Eden.Zone/Handlers/ItemActionHandlers.cs:24 | CNE_CZ_ZoneServerRepairEquipment | implémenté · client émet |
| 47 | C→S | ZoneServerAskRepairOneItemGold | Eden.Zone/Handlers/InventoryHandlers.cs:249 | CNE_CZ_ZoneServerAskRepairOneItemGold | implémenté · client émet |
| 48 | C→S | ZoneServerRepairOneItem | Eden.Zone/Handlers/ItemActionHandlers.cs:37 | CNE_CZ_ZoneServerRepairOneItem | implémenté · client émet |
| 49 | C→S | ZoneFamilyCreate | Eden.Zone/Handlers/SocialHandlers.cs:284 | CNE_CZ_ZoneServerFamilyCreate | implémenté · client émet |
| 50 | C→S | ZoneEmptyRequest.QueryMissionStates | ZoneEntryFlow.cs:896 | CNE_CZ_ZoneServerQueryMissionStates | implémenté · client émet |
| 51 | C→S | ZoneGiveUpMission | Eden.Zone/Handlers/TeamHandlers.cs:138 | CNE_CZ_ZoneServerGiveUpMission | implémenté · client émet |
| 52 | C→S | — | Eden.Zone/Handlers/InstanceHandlers.cs:78 | CNE_CZ_ZoneServerBuySpell | implémenté · ⟂ orphelin |
| 53 | C→S | ZoneEmptyRequest.EnterNodeShortcut | Eden.Zone/Handlers/EntryHandlers.cs:179 | CNE_CZ_ZoneServerEnterNodeShortcut | implémenté · client émet |
| 54 | C→S | ZoneServerUpdateShortcut | Eden.Zone/Handlers/PlayerHandlers.cs:125 | CNE_CZ_ZoneServerUpdateShortcut | implémenté · ⟂ orphelin |
| 55 | C→S | ZoneServerGetShortcutOfPage | Eden.Zone/Handlers/PlayerHandlers.cs:154 | CNE_CZ_ZoneServerGetShortcutOfPage | implémenté · ⟂ orphelin |
| 56 | C→S | ZoneTradeReset | Eden.Zone/Handlers/TradeHandlers.cs:167 | CNE_CZ_ZoneServerTradeReset | implémenté · ⟂ orphelin |
| 57 | C→S | ZoneEmptyRequest.Echo | ZoneEntryFlow.cs:490 | CNE_CZ_ZoneServerEcho | implémenté · client émet |
| 58 | C→S | — | Eden.Zone/Handlers/PlayerHandlers.cs:44 | CNE_CZ_ZoneServerLogoutReq | implémenté · ⟂ orphelin |
| 59 | C→S | ZoneSpellCastToNearby | ZoneEntryFlow.cs:635 | CNE_CZ_ZoneServerSpellCastToNearby | implémenté · ⟂ orphelin |
| 60 | C→S | ZoneStorageDeposit | Eden.Zone/Handlers/InventoryHandlers.cs:316 | CNE_CZ_ZoneServerStorageDeposit | implémenté · client émet |
| 61 | C→S | ZoneStorageWithdraw | Eden.Zone/Handlers/InventoryHandlers.cs:366 | CNE_CZ_ZoneServerStorageWithdraw | implémenté · client émet |
| 62 | C→S | ZoneGetStorageData | EntryReplayData.cs · Eden.Zone/Handlers/InventoryHandlers.cs:409 | CNE_CZ_ZoneServerGetStorageData | partiel (replay post-entrée ; squelette pré-entrée) · client émet |
| 63 | C→S | ZoneEmptyRequest.LeaveShop | ZoneEntryFlow.cs:888 | CNE_CZ_ZoneServerLeaveShop | implémenté · client émet |
| 64 | C→S | — | Eden.Zone/Handlers/InventoryHandlers.cs:261 | CNE_CZ_ZoneServerAskIdentifyGold | implémenté · ⟂ orphelin |
| 65 | C→S | — | Eden.Zone/Handlers/ItemActionHandlers.cs:58 | CNE_CZ_ZoneServerIdentifyItem | implémenté · ⟂ orphelin |
| 66 | C→S | ZoneServerExchangeItemPin | Eden.Zone/Handlers/MiscHandlers.cs:127 | CNE_CZ_ZoneServerExchangeItemPin | implémenté · ⟂ orphelin |
| 67 | C→S | — | ZoneEntryFlow.cs:1056 | CNE_CZ_ZoneServerInlayItem | implémenté · ⟂ orphelin |
| 68 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:379 | CNE_CZ_ZoneServerReportProcessInfos | implémenté · ⟂ orphelin |
| 69 | C→S | ZoneStorageExpand | Eden.Zone/Handlers/InventoryHandlers.cs:425 | CNE_CZ_ZoneServerStorageExpand | implémenté · client émet |
| 70 | C→S | ZoneItemMallReceivableEnter | ZoneEntryFlow.cs:937 | CNE_CZ_ZoneServerItemMallReceivableEnter | implémenté · client émet |
| 71 | C→S | ZoneAskVendor | ZoneEntryFlow.cs:949 | CNE_CZ_ZoneServerAskVendor | implémenté · ⟂ orphelin |
| 72 | C→S | ZoneAskVendorContainer | ZoneEntryFlow.cs:953 | CNE_CZ_ZoneServerAskVendorContainer | implémenté · client émet |
| 73 | C→S | — | Eden.Zone/Handlers/HousingHandlers.cs:138 | CNE_CZ_ZoneServerSloganUpdate | implémenté · ⟂ orphelin |
| 74 | C→S | ZoneVendorClose | ZoneEntryFlow.cs:958 | CNE_CZ_ZoneServerVendorClose | implémenté · client émet |
| 75 | C→S | ZoneVendorCloseView | ZoneEntryFlow.cs:962 | CNE_CZ_ZoneServerVendorCloseView | implémenté · client émet |
| 76 | C→S | ZoneVendorBuy | ZoneEntryFlow.cs:966 | CNE_CZ_ZoneServerVendorBuy | implémenté · client émet |
| 77 | C→S | ZoneVendorSellItem | ZoneEntryFlow.cs:970 | CNE_CZ_ZoneServerVendorSellItem | implémenté · client émet |
| 78 | C→S | — | ZoneEntryFlow.cs:975 | CNE_CZ_ZoneServerVendorCloseVending | implémenté · ⟂ orphelin |
| 79 | C→S | ZoneServerTeamOperation | ZoneEntryFlow.cs:1109 | CNE_CZ_ZoneServerTeamOperation | implémenté · ⟂ orphelin |
| 80 | C→S | — | ZoneEntryFlow.cs:976 | CNE_CZ_ZoneServerVendorUseItem | implémenté · ⟂ orphelin |
| 81 | C→S | ZoneUpdateFamily | Eden.Zone/Handlers/FamilyHandlers.cs:252 | CNE_CZ_ZoneServerUpdateFamily | implémenté · client émet |
| 82 | C→S | ZoneSetFamilyEmblem | Eden.Zone/Handlers/SocialHandlers.cs:319 | CNE_CZ_ZoneServerSetFamilyEmblem | implémenté · client émet |
| 83 | C→S | ZoneItemMallUseItem | Eden.Zone/Handlers/ItemActionHandlers.cs:560 | CNE_CZ_ZoneServerItemMallUseItem | implémenté · ⟂ orphelin |
| 84 | C→S | ZoneDivideItem | Eden.Zone/Handlers/InventoryHandlers.cs:207 | CNE_CZ_ZoneServerDivideItem | implémenté · client émet |
| 85 | C→S | ZoneEnchantCancel | ZoneEntryFlow.cs:519 | CNE_CZ_ZoneServerEnchantCancel | implémenté · client émet |
| 86 | C→S | ZoneTeamRadarAttention | Eden.Zone/Handlers/MiscHandlers.cs:682 | CNE_CZ_ZoneServerTeamRadarAttention | implémenté · client émet |
| 87 | C→S | ZoneMissionListOp | Eden.Zone/Handlers/SocialHandlers.cs:450 | CNE_CZ_ZoneServerMissionListOp | implémenté · client émet |
| 88 | C→S | ZoneTransport | ZoneEntryFlow.cs:873 | CNE_CZ_ZoneServerTransport | implémenté · client émet |
| 89 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:390 | CNE_CZ_ZoneServerBagDueDate | implémenté · ⟂ orphelin |
| 90 | C→S | ZoneTeamChangeLeader | Eden.Zone/Handlers/TeamHandlers.cs:97 | CNE_CZ_ZoneServerTeamChangeLeader | implémenté · client émet |
| 91 | C→S | ZoneTradeReady | Eden.Zone/Handlers/TradeHandlers.cs:138 | CNE_CZ_ZoneServerTradeReady | implémenté · client émet |
| 92 | C→S | ZoneTradeUnConfirm | Eden.Zone/Handlers/TradeHandlers.cs:151 | CNE_CZ_ZoneServerTradeUnConfirm | implémenté · ⟂ orphelin |
| 93 | C→S | ZoneSetAppellation | ZoneEntryFlow.cs:1131 | CNE_CZ_ZoneServerUpdateAppellation | implémenté · client émet |
| 94 | C→S | ZoneQueryAppellation | ZoneEntryFlow.cs:1139 | CNE_CZ_ZoneServerQueryAppellation | implémenté · ⟂ orphelin |
| 95 | C→S | ZoneMailOpenBox | ZoneEntryFlow.cs:1077 | CNE_CZ_ZoneServerMailOpenBox | implémenté · client émet |
| 96 | C→S | ZoneMailQuery | Eden.Zone/Handlers/SocialHandlers.cs:196 | CNE_CZ_ZoneServerMailQuery | implémenté · client émet |
| 97 | C→S | ZoneMailRead | Eden.Zone/Handlers/SocialHandlers.cs:208 | CNE_CZ_ZoneServerMailRead | implémenté · client émet |
| 98 | C→S | ZoneMailDelete | Eden.Zone/Handlers/SocialHandlers.cs:258 | CNE_CZ_ZoneServerMailDelete | implémenté · client émet |
| 99 | C→S | ZoneMailSend | Eden.Zone/Handlers/SocialHandlers.cs:35 | CNE_CZ_ZoneServerMailSend | implémenté · client émet |
| 100 | C→S | ZoneMailGetItem | Eden.Zone/Handlers/SocialHandlers.cs:225 | CNE_CZ_ZoneServerMailGetItem | implémenté · client émet |
| 101 | C→S | ZoneMailGetGold | Eden.Zone/Handlers/SocialHandlers.cs:245 | CNE_CZ_ZoneServerMailGetGold | implémenté · client émet |
| 102 | C→S | ZoneMailReturn | Eden.Zone/Handlers/SocialHandlers.cs:271 | CNE_CZ_ZoneServerMailReturn | implémenté · client émet |
| 103 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:580 | CNE_CZ_ZoneServerSearchCondition | implémenté · ⟂ orphelin |
| 104 | C→S | ZoneSearchOpt | ZoneEntryFlow.cs:1190 · Eden.Zone/Handlers/MiscHandlers.cs:566 | CNE_CZ_ZoneServerSearchOpt | partiel (acquitté ; Register court-circuité) · client émet |
| 105 | C→S | — | Eden.Zone/Handlers/ElfHandlers.cs:210 | CNE_CZ_ZoneServerElfStoneUpdate | implémenté · ⟂ orphelin |
| 106 | C→S | — | Eden.Zone/Handlers/ElfHandlers.cs:204 | CNE_CZ_ZoneServerElfStoneUse | implémenté · ⟂ orphelin |
| 107 | C→S | ZoneElfWork | ZoneEntryFlow.cs:544 | CNE_CZ_ZoneServerElfWork | implémenté · client émet |
| 108 | C→S | ZoneElfReturn | ZoneEntryFlow.cs:540 | CNE_CZ_ZoneServerElfReturn | implémenté · client émet |
| 109 | C→S | ZoneElfCallElf | ZoneEntryFlow.cs:536 | CNE_CZ_ZoneServerElfCallElf | implémenté · ⟂ orphelin |
| 110 | C→S | ZoneServerEnterNodeElfInfo | ZoneEntryFlow.cs:532 | CNE_CZ_ZoneServerEnterNodeElfInfo | implémenté · client émet |
| 111 | C→S | — | Eden.Zone/Handlers/ElfHandlers.cs:151 | CNE_CZ_ZoneServerElfWorkResultOK | implémenté · ⟂ orphelin |
| 112 | C→S | — | Eden.Zone/Handlers/ElfHandlers.cs:186 | CNE_CZ_ZoneServerElfCheckData | implémenté · ⟂ orphelin |
| 113 | C→S | ZoneEmptyRequest.EnterNodeElfContainer | EntryReplayData.cs · Eden.Zone/Handlers/ElfHandlers.cs:20 | CNE_CZ_ZoneServerEnterNodeElfContainer | partiel (replay post-entrée ; squelette pré-entrée) · client émet |
| 114 | C→S | ZoneAuctionBuyQuery | Eden.Zone/Handlers/ShopHandlers.cs:420 | CNE_CZ_ZoneServerAuctionBuyQuery | implémenté · client émet |
| 115 | C→S | ZoneAuctionBuy | Eden.Zone/Handlers/ShopHandlers.cs:331 | CNE_CZ_ZoneServerAuctionBuy | implémenté · client émet |
| 116 | C→S | ZoneAuctionSellQuery | Eden.Zone/Handlers/ShopHandlers.cs:402 | CNE_CZ_ZoneServerAuctionSellQuery | implémenté · client émet |
| 117 | C→S | ZoneAuctionSell | Eden.Zone/Handlers/ShopHandlers.cs:287 | CNE_CZ_ZoneServerAuctionSell | implémenté · client émet |
| 118 | C→S | ZoneAuctionCancel | Eden.Zone/Handlers/ShopHandlers.cs:385 | CNE_CZ_ZoneServerAuctionCancel | implémenté · client émet |
| 119 | C→S | ZoneServerStrengthenItem | ZoneEntryFlow.cs:1048 | CNE_CZ_ZoneServerStrengthenItem | implémenté · ⟂ orphelin |
| 120 | C→S | ZoneServerStrengthenClose | ZoneEntryFlow.cs:1052 | CNE_CZ_ZoneServerStrengthenClose | implémenté · ⟂ orphelin |
| 121 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:138 | CNE_CZ_ZoneServerSetSpellStone | implémenté · ⟂ orphelin |
| 122 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:320 | CNE_CZ_ZoneServerStateCancel | implémenté · ⟂ orphelin |
| 123 | C→S | ZoneTurnSelf | ZoneEntryFlow.cs:869 | CNE_CZ_ZoneServerTurnSelf | implémenté · ⟂ orphelin |
| 124 | C→S | ZoneTargetUpdate | ZoneEntryFlow.cs:1154 | CNE_CZ_ZoneServerTargetUpdate | implémenté · client émet |
| 125 | C→S | — | Eden.Zone/Handlers/PlayerHandlers.cs:103 | CNE_CZ_ZoneServerGetSelectLifeTarget | implémenté · ⟂ orphelin |
| 126 | C→S | — | Eden.Zone/Handlers/PlayerHandlers.cs:91 | CNE_CZ_ZoneServerPVPFlagModify | implémenté · ⟂ orphelin |
| 127 | C→S | ZoneEnchantPauseRequest | Eden.Zone/Handlers/CombatHandlers.cs:186 | CNE_CZ_ZoneServerEnchantPause | implémenté · ⟂ orphelin |
| 128 | C→S | ZoneEnchantStartRequest | Eden.Zone/Handlers/CombatHandlers.cs:175 | CNE_CZ_ZoneServerEnchantStart | implémenté · ⟂ orphelin |
| 129 | C→S | ZoneServerGoLottery | ZoneEntryFlow.cs:1084 | CNE_CZ_ZoneServerGoLottery | implémenté · client émet |
| 130 | C→S | ZoneServerFamilyContainerPutItem | ZoneEntryFlow.cs:701 | CNE_CZ_ZoneServerFamilyContainerPutItem | implémenté · ⟂ orphelin |
| 131 | C→S | ZoneServerFamilyContainerGetItem | ZoneEntryFlow.cs:706 | CNE_CZ_ZoneServerFamilyContainerGetItem | implémenté · ⟂ orphelin |
| 132 | C→S | ZoneServerFamilyInstanceGetData | ZoneEntryFlow.cs:683 | CNE_CZ_ZoneServerFamilyInstanceGetData | implémenté · ⟂ orphelin |
| 133 | C→S | ZoneEmptyRequest.EnterNodePlayerCard | EntryReplayData.cs · Eden.Zone/Handlers/EntryHandlers.cs:233 | CNE_CZ_ZoneServerEnterNodePlayerCard | partiel (replay post-entrée ; squelette pré-entrée) · client émet |
| 134 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:441 | CNE_CZ_ZoneServerQueryPlayerCard | implémenté · ⟂ orphelin |
| 135 | C→S | ZoneDuelRequest | ZoneEntryFlow.cs:711 | CNE_CZ_ZoneServerDuelRequest | implémenté · client émet |
| 136 | C→S | ZoneDuelRequestAnswer | ZoneEntryFlow.cs:715 | CNE_CZ_ZoneServerDuelRequestAnswer | implémenté · client émet |
| 137 | C→S | ZoneElfTalk | Eden.Zone/Handlers/ElfHandlers.cs:74 | CNE_CZ_ZoneServerElfTalk | implémenté · client émet |
| 138 | C→S | ZoneMessageListQuery | Eden.Zone/Handlers/MiscHandlers.cs:212 | CNE_CZ_ZoneServerMessageListQuery | implémenté · client émet |
| 139 | C→S | ZoneMessageWrite | Eden.Zone/Handlers/MiscHandlers.cs:180 | CNE_CZ_ZoneServerMessageWrite | implémenté · client émet |
| 140 | C→S | ZoneMessageReadRequest | Eden.Zone/Handlers/MiscHandlers.cs:200 | CNE_CZ_ZoneServerMessageRead | implémenté · client émet |
| 141 | C→S | ZoneMessageDelete | Eden.Zone/Handlers/MiscHandlers.cs:222 | CNE_CZ_ZoneServerMessageDelete | implémenté · client émet |
| 142 | C→S | ZoneMessageExit | Eden.Zone/Handlers/MiscHandlers.cs:233 | CNE_CZ_ZoneServerMessageExit | implémenté · client émet |
| 143 | C→S | ZoneServerLookEquip | Eden.Zone/Handlers/MiscHandlers.cs:489 | CNE_CZ_ZoneServerLookEquip | implémenté · client émet |
| 144 | C→S | ZoneBattlefieldJoin | Eden.Zone/Handlers/InstanceHandlers.cs:20 | CNE_CZ_ZoneServerBattlefieldJoin | implémenté · client émet |
| 145 | C→S | ZoneEnterBattlefieldAnswer | Eden.Zone/Handlers/MiscHandlers.cs:744 | CNE_CZ_ZoneServerEnterBattlefieldAnswer | implémenté · client émet |
| 146 | C→S | ZoneBattlefieldLeave | Eden.Zone/Handlers/InstanceHandlers.cs:54 | CNE_CZ_ZoneServerBattlefieldLeave | implémenté · client émet |
| 147 | C→S | — | Eden.Zone/Handlers/InstanceHandlers.cs:49 | CNE_CZ_ZoneServerBFListQuery | implémenté · ⟂ orphelin |
| 148 | C→S | ZoneServerSetAAPoint | ZoneEntryFlow.cs:987 | CNE_CZ_ZoneServerSetAAPoint | implémenté · ⟂ orphelin |
| 149 | C→S | ZoneServerSetAARate | ZoneEntryFlow.cs:1027 | CNE_CZ_ZoneServerSetAARate | implémenté · ⟂ orphelin |
| 150 | C→S | ZoneServerSaveCollection | ZoneEntryFlow.cs:992 | CNE_CZ_ZoneServerSaveCollection | implémenté · client émet |
| 151 | C→S | ZoneServerGetCollection | ZoneEntryFlow.cs:998 | CNE_CZ_ZoneServerGetCollection | implémenté · client émet |
| 152 | C→S | ZoneServerQueryCollection | ZoneEntryFlow.cs:1003 | CNE_CZ_ZoneServerQueryCollection | implémenté · client émet |
| 153 | C→S | — | Eden.Zone/Handlers/EntryHandlers.cs:61 | CNE_CZ_ZoneServerHello | implémenté · ⟂ orphelin |
| 154 | C→S | ZoneLoverInviteRequest | Eden.Zone/Handlers/SocialHandlers.cs:331 | CNE_CZ_ZoneServerLoverInvite | implémenté · client émet |
| 155 | C→S | ZoneLoverMessage | Eden.Zone/Handlers/SocialHandlers.cs:340 | CNE_CZ_ZoneServerLoverMessage | implémenté · client émet |
| 156 | C→S | ZoneQueryLoverInfo | ZoneEntryFlow.cs:1190 · Eden.Zone/Handlers/SocialHandlers.cs:352 | CNE_CZ_ZoneServerQueryLoverInfo | partiel (acquitté ; Register court-circuité) · client émet |
| 157 | C→S | ZoneLoverBreakRequest | Eden.Zone/Handlers/SocialHandlers.cs:348 | CNE_CZ_ZoneServerLoverBreak | implémenté · client émet |
| 158 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:478 | CNE_CZ_ZoneServerVictoryDeclaration | implémenté · ⟂ orphelin |
| 159 | C→S | ZoneFamilyDonateMoney | Eden.Zone/Handlers/SocialHandlers.cs:297 | CNE_CZ_ZoneServerFamilyDonateMoney | implémenté · client émet |
| 160 | C→S | ZoneServerMissionGoto | ZoneEntryFlow.cs:892 | CNE_CZ_ZoneServerMissionGoto | implémenté · client émet |
| 161 | C→S | ZoneServerFamilyStatue | ZoneEntryFlow.cs:719 | CNE_CZ_ZoneServerFamilyStatue | implémenté · ⟂ orphelin |
| 162 | C→S | ZoneServerQueryBattlefieldData | ZoneEntryFlow.cs:1012 | CNE_CZ_ZoneServerQueryBattlefieldData | implémenté · client émet |
| 163 | C→S | ZoneServerNodeGroupReset | ZoneEntryFlow.cs:1126 | CNE_CZ_ZoneServerNodeGroupReset | implémenté · ⟂ orphelin |
| 164 | C→S | ZoneUpdateBotData | ZoneEntryFlow.cs:1007 | CNE_CZ_ZoneServerUpdateBotData | implémenté · client émet |
| 165 | C→S | ZoneServerQueryClosets | ZoneEntryFlow.cs:907 | CNE_CZ_ZoneServerQueryClosets | implémenté · client émet |
| 166 | C→S | ZoneServerQueryClassList | ZoneEntryFlow.cs:911 | CNE_CZ_ZoneServerQueryClassList | implémenté · client émet |
| 167 | C→S | ZoneServerClosetSet | ZoneEntryFlow.cs:915 | CNE_CZ_ZoneServerClosetSet | implémenté · client émet |
| 168 | C→S | ZoneChangeClass | ZoneEntryFlow.cs:920 | CNE_CZ_ZoneServerChangeClass | implémenté · client émet |
| 169 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:458 | CNE_CZ_ZoneServerPlayerCardUpdate | implémenté · ⟂ orphelin |
| 170 | C→S | ZonePlayerWork | ZoneEntryFlow.cs:944 | CNE_CZ_ZoneServerPlayerWork | implémenté · client émet |
| 171 | C→S | ZonePlayerWorkResultOK | ZoneEntryFlow.cs:979 | CNE_CZ_ZoneServerPlayerWorkResultOK | implémenté · client émet |
| 172 | C→S | ZoneServerPlayerWorkCheckData | ZoneEntryFlow.cs:983 | CNE_CZ_ZoneServerPlayerWorkCheckData | implémenté · client émet |
| 173 | C→S | ZoneServerFamilyInstanceCreate | ZoneEntryFlow.cs:679 | CNE_CZ_ZoneServerFamilyInstanceCreate | implémenté · ⟂ orphelin |
| 174 | C→S | ZoneServerFamilyTownQuery | ZoneEntryFlow.cs:691 | CNE_CZ_ZoneServerFamilyTownQuery | implémenté · ⟂ orphelin |
| 175 | C→S | ZoneServerFamilyTownMemberQuery | ZoneEntryFlow.cs:696 | CNE_CZ_ZoneServerFamilyTownMemberQuery | implémenté · ⟂ orphelin |
| 176 | C→S | ZoneServerFamilyTownEnter | ZoneEntryFlow.cs:687 | CNE_CZ_ZoneServerFamilyTownEnter | implémenté · ⟂ orphelin |
| 177 | C→S | ZoneFamilyStoragePut | Eden.Zone/Handlers/InventoryHandlers.cs:486 | CNE_CZ_ZoneServerFamilyStoragePutItem | implémenté · client émet |
| 178 | C→S | ZoneFamilyStorageGet | Eden.Zone/Handlers/InventoryHandlers.cs:582 | CNE_CZ_ZoneServerFamilyStorageGetItem | implémenté · client émet |
| 179 | C→S | ZoneFamilyStorageQueryLog | Eden.Zone/Handlers/FamilyHandlers.cs:186 | CNE_CZ_ZoneServerFamilyStorageQueryLog | implémenté · client émet |
| 180 | C→S | — | Eden.Zone/Handlers/FamilyHandlers.cs:74 | CNE_CZ_ZoneServerFamilyBuildingRepair | implémenté · ⟂ orphelin |
| 181 | C→S | ZoneServerQueryLicense | ZoneEntryFlow.cs:924 | CNE_CZ_ZoneServerQueryLicense | implémenté · client émet |
| 182 | C→S | ZoneServerPlayerLicense | ZoneEntryFlow.cs:928 | CNE_CZ_ZoneServerPlayerLicense | implémenté · ⟂ orphelin |
| 183 | C→S | ZoneServerQueryLicenseSet | ZoneEntryFlow.cs:932 | CNE_CZ_ZoneServerQueryLicenseSet | implémenté · ⟂ orphelin |
| 184 | C→S | ZoneServerCheckUpdateLottery | ZoneEntryFlow.cs:1091 | CNE_CZ_ZoneServerCheckUpdateLottery | implémenté · ⟂ orphelin |
| 185 | C→S | — | Eden.Zone/Handlers/ShopHandlers.cs:218 | CNE_CZ_ZoneServerBuyBackOpen | implémenté · ⟂ orphelin |
| 186 | C→S | ZoneSetMissionTrace | Eden.Zone/Handlers/TeamHandlers.cs:175 | CNE_CZ_ZoneServerSetMissionTrace | implémenté · client émet |
| 187 | C→S | ZonePetModeChange | ZoneEntryFlow.cs:573 | CNE_CZ_ZoneServerPetModeChange | implémenté · client émet |
| 188 | C→S | ZoneSendPetCmd | ZoneEntryFlow.cs:578 | CNE_CZ_ZoneServerSendPetCmd | implémenté · client émet |
| 189 | C→S | ZoneServerTerritoryQuery | ZoneEntryFlow.cs:724 | CNE_CZ_ZoneServerTerritoryQuery | implémenté · ⟂ orphelin |
| 190 | C→S | ZoneServerTerritoryJoin | ZoneEntryFlow.cs:728 | CNE_CZ_ZoneServerTerritoryJoin | implémenté · ⟂ orphelin |
| 191 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:701 | CNE_CZ_ZoneServerTeamRecruitMsg | implémenté · ⟂ orphelin |
| 192 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:631 | CNE_CZ_ZoneServerSearchTeam | implémenté · ⟂ orphelin |
| 193 | C→S | ZoneServerRollTreasure | ZoneEntryFlow.cs:618 | CNE_CZ_ZoneServerRollTreasure | implémenté · client émet |
| 194 | C→S | ZoneServerRankInfoQuery | ZoneEntryFlow.cs:757 | CNE_CZ_ZoneServerRankInfoQuery | implémenté · ⟂ orphelin |
| 195 | C→S | ZoneEmptyRequest.EnterNodePlayerRoom | EntryReplayData.cs · Eden.Zone/Handlers/EntryHandlers.cs:236 | CNE_CZ_ZoneServerEnterNodePlayerRoom | partiel (replay post-entrée ; squelette pré-entrée) · client émet |
| 196 | C→S | ZoneServerGladiatorTeamCreate | ZoneEntryFlow.cs:738 | CNE_CZ_ZoneServerGladiatorTeamCreate | implémenté · ⟂ orphelin |
| 197 | C→S | ZoneServerGladiatorTeamQuery | ZoneEntryFlow.cs:739 | CNE_CZ_ZoneServerGladiatorTeamQuery | implémenté · ⟂ orphelin |
| 198 | C→S | ZoneServerColosseumJoinAnswer | ZoneEntryFlow.cs:741 | CNE_CZ_ZoneServerColosseumJoinAnswer | implémenté · ⟂ orphelin |
| 199 | C→S | ZoneServerColosseumJoinRequest | ZoneEntryFlow.cs:744 | CNE_CZ_ZoneServerColosseumJoinRequest | implémenté · ⟂ orphelin |
| 200 | C→S | ZoneServerColosseumTeamQuery | ZoneEntryFlow.cs:747 | CNE_CZ_ZoneServerColosseumTeamQuery | implémenté · ⟂ orphelin |
| 201 | C→S | ZoneServerColosseumLeaveJoin | ZoneEntryFlow.cs:750 | CNE_CZ_ZoneServerColosseumLeaveJoin | implémenté · ⟂ orphelin |
| 202 | C→S | ZoneServerCoRideInvite | Eden.Zone/Handlers/MiscHandlers.cs:246 | CNE_CZ_ZoneServerCoRideInvite | implémenté · ⟂ orphelin |
| 203 | C→S | ZoneDestinyDice | ZoneEntryFlow.cs:819 | CNE_CZ_ZoneServerDestinyDice | implémenté · client émet |
| 204 | C→S | ZoneQueryDestinyRoad | ZoneEntryFlow.cs:830 | CNE_CZ_ZoneServerQueryDestinyRoad | implémenté · client émet |
| 205 | C→S | — | Eden.Zone/Handlers/HousingHandlers.cs:80 | CNE_CZ_ZoneServerSearchRoom | implémenté · ⟂ orphelin |
| 206 | C→S | — | Eden.Zone/Handlers/HousingHandlers.cs:18 | CNE_CZ_ZoneServerRentRoom | implémenté · ⟂ orphelin |
| 207 | C→S | ZoneServerEnterRoom | ZoneEntryFlow.cs:670 | CNE_CZ_ZoneServerEnterRoom | implémenté · client émet |
| 208 | C→S | — | Eden.Zone/Handlers/HousingHandlers.cs:95 | CNE_CZ_ZoneServerSetRoomPrivilege | implémenté · ⟂ orphelin |
| 209 | C→S | — | Eden.Zone/Handlers/HousingHandlers.cs:127 | CNE_CZ_ZoneServerSwitchSetRoomMode | implémenté · ⟂ orphelin |
| 210 | C→S | ZoneServerRoomGetData | ZoneEntryFlow.cs:674 | CNE_CZ_ZoneServerRoomGetData | implémenté · ⟂ orphelin |
| 211 | C→S | — | Eden.Zone/Handlers/HousingHandlers.cs:110 | CNE_CZ_ZoneServerUpdateDecoration | implémenté · ⟂ orphelin |
| 212 | C→S | — | Eden.Zone/Handlers/HousingHandlers.cs:28 | CNE_CZ_ZoneServerPayRent | implémenté · ⟂ orphelin |
| 213 | C→S | ZoneServerQueryLifeSkill | ZoneEntryFlow.cs:1069 | CNE_CZ_ZoneServerQueryLifeSkill | implémenté · client émet |
| 214 | C→S | ZoneServerLifeSkill | ZoneEntryFlow.cs:1073 | CNE_CZ_ZoneServerLifeSkill | implémenté · ⟂ orphelin |
| 215 | C→S | — | Eden.Zone/Handlers/HousingHandlers.cs:67 | CNE_CZ_ZoneServerChangeRoomTexture | implémenté · ⟂ orphelin |
| 216 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:309 | CNE_CZ_ZoneServerStartParty | implémenté · ⟂ orphelin |
| 217 | C→S | — | Eden.Zone/Handlers/HousingHandlers.cs:40 | CNE_CZ_ZoneServerExtendRoom | implémenté · ⟂ orphelin |
| 218 | C→S | ZoneNewPlayerGuide | ZoneEntryFlow.cs:1017 | CNE_CZ_ZoneServerNewPlayerGuide | implémenté · ⟂ orphelin |
| 219 | C→S | ZoneServerColosseumModeQuery | ZoneEntryFlow.cs:753 | CNE_CZ_ZoneServerColosseumModeQuery | implémenté · ⟂ orphelin |
| 220 | C→S | ZoneServerRecruitInstance | ZoneEntryFlow.cs:1121 | CNE_CZ_ZoneServerRecruitInstance | implémenté · ⟂ orphelin |
| 221 | C→S | ZoneServerChanceLuckyBagGetReward | ZoneEntryFlow.cs:836 | CNE_CZ_ZoneServerChanceLuckyBagGetReward | implémenté · ⟂ orphelin |
| 222 | C→S | ZoneServerChanceLuckyBagReTry | ZoneEntryFlow.cs:840 | CNE_CZ_ZoneServerChanceLuckyBagReTry | implémenté · ⟂ orphelin |
| 223 | C→S | ZoneQueryFortuneBagCounter | ZoneEntryFlow.cs:844 | CNE_CZ_ZoneServerQueryFortuneBagCounter | implémenté · client émet |
| 224 | C→S | ZoneServerUpdateLotteryBonus | ZoneEntryFlow.cs:1097 | CNE_CZ_ZoneServerUpdateLotteryBonus | implémenté · ⟂ orphelin |
| 225 | C→S | ZoneServerFamilyBattleAskInfo | ZoneEntryFlow.cs:732 | CNE_CZ_ZoneServerFamilyBattleAskInfo | implémenté · ⟂ orphelin |
| 226 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:554 | CNE_CZ_ZoneServerSetRecommendActivityTracing | implémenté · ⟂ orphelin |
| 227 | C→S | ZoneElfMix | ZoneEntryFlow.cs:583 | CNE_CZ_ZoneServerElfMix | implémenté · client émet |
| 228 | C→S | ZoneElfChangeName | ZoneEntryFlow.cs:589 | CNE_CZ_ZoneServerElfChangeName | implémenté · client émet |
| 229 | C→S | ZoneElfRevive | Eden.Zone/Handlers/ElfHandlers.cs:85 | CNE_CZ_ZoneServerElfRevive | implémenté · client émet |
| 230 | C→S | — | Eden.Zone/Handlers/EquipTrainHandlers.cs:30 | CNE_CZ_ZoneServerEquipTrainSetItem | implémenté · ⟂ orphelin |
| 231 | C→S | ZoneServerPVPRankLookEquip | ZoneEntryFlow.cs:763 | CNE_CZ_ZoneServerPVPRankLookEquip | implémenté · ⟂ orphelin |
| 232 | C→S | ZoneServerVerifyCubeCosts | ZoneEntryFlow.cs:1065 | CNE_CZ_ZoneServerVerifyCubeCosts | implémenté · ⟂ orphelin |
| 233 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:72 | CNE_CZ_ZoneServerGreedyIslandJoin | implémenté · ⟂ orphelin |
| 234 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:82 | CNE_CZ_ZoneServerGreedyIslandRankQuery | implémenté · ⟂ orphelin |
| 235 | C→S | — | Eden.Zone/Handlers/TeamHandlers.cs:282 | CNE_CZ_ZoneServerSearchTeamV2 | implémenté · ⟂ orphelin |
| 236 | C→S | — | Eden.Zone/Handlers/TeamHandlers.cs:314 | CNE_CZ_ZoneServerTeamRecruitV2 | implémenté · ⟂ orphelin |
| 237 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:720 | CNE_CZ_ZoneServerTeamRecruitTransport | implémenté · ⟂ orphelin |
| 238 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:269 | CNE_CZ_ZoneServerChangePositionV2 | implémenté · ⟂ orphelin |
| 239 | C→S | — | Eden.Zone/Handlers/FamilyHandlers.cs:204 | CNE_CZ_ZoneServerFamilyTalkToNpc | implémenté · ⟂ orphelin |
| 240 | C→S | — | Eden.Zone/Handlers/FamilyHandlers.cs:218 | CNE_CZ_ZoneServerFixEquipByFamily | implémenté · ⟂ orphelin |
| 241 | C→S | ZoneServerBattleRoyaleJoinRequest | ZoneEntryFlow.cs:644 | CNE_CZ_ZoneServerBattleRoyaleJoinRequest | implémenté · ⟂ orphelin |
| 242 | C→S | ZoneServerPlayerCCDataSave | ZoneEntryFlow.cs:1031 | CNE_CZ_ZoneServerPlayerCCDataSave | implémenté · ⟂ orphelin |
| 243 | C→S | ZoneServerEnterNodeFastDress | ZoneEntryFlow.cs:769 | CNE_CZ_ZoneServerEnterNodeFastDress | implémenté · client émet |
| 244 | C→S | ZoneServerApplyFastDressData | ZoneEntryFlow.cs:772 | CNE_CZ_ZoneServerApplyFastDressData | implémenté · ⟂ orphelin |
| 245 | C→S | ZoneServerSaveFastDressData | ZoneEntryFlow.cs:780 | CNE_CZ_ZoneServerSaveFastDressData | implémenté · ⟂ orphelin |
| 246 | C→S | ZoneServerRecordFastDressInfo | ZoneEntryFlow.cs:786 | CNE_CZ_ZoneServerRecordFastDressInfo | implémenté · ⟂ orphelin |
| 247 | C→S | ZoneServerUpdateFastDressPage | ZoneEntryFlow.cs:801 | CNE_CZ_ZoneServerUpdateFastDressPage | implémenté · ⟂ orphelin |
| 248 | C→S | ZoneServerRemoveFastDressInfo | ZoneEntryFlow.cs:807 | CNE_CZ_ZoneServerRemoveFastDressInfo | implémenté · ⟂ orphelin |
| 249 | C→S | ZoneMarryOpRequest | Eden.Zone/Handlers/SocialHandlers.cs:382 | CNE_CZ_ZoneServerMarryOperation | implémenté · client émet |
| 250 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:280 | CNE_CZ_ZoneServerChurchReserve | implémenté · ⟂ orphelin |
| 251 | C→S | ZoneWeddingInfoQuery | Eden.Zone/Handlers/SocialHandlers.cs:397 | CNE_CZ_ZoneServerWeddingInfoQuery | implémenté · client émet |
| 252 | C→S | ZoneServerVersusInstanceJoinRequest | ZoneEntryFlow.cs:655 | CNE_CZ_ZoneServerVersusInstanceJoinRequest | implémenté · ⟂ orphelin |
| 253 | C→S | ZoneServerPayEventSelect | ZoneEntryFlow.cs:1103 | CNE_CZ_ZoneServerPayEventSelect | implémenté · ⟂ orphelin |
| 254 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:352 | CNE_CZ_ZoneServerPayEventResponse | implémenté · ⟂ orphelin |
| 255 | C→S | ZoneServerEvaluateLotteryOpen | ZoneEntryFlow.cs:1116 | CNE_CZ_ZoneServerEvaluateLotteryOpen | implémenté · ⟂ orphelin |
| 256 | C→S | ZoneEmptyRequest.EnterNodeStarWay | EntryReplayData.cs · Eden.Zone/Handlers/EntryHandlers.cs:239 | CNE_CZ_ZoneServerEnterNodeStarWay | partiel (replay post-entrée ; squelette pré-entrée) · client émet |
| 257 | C→S | ZoneServerExecuteMagicBox | ZoneEntryFlow.cs:1061 | CNE_CZ_ZoneServerExecuteMagicBox | implémenté · ⟂ orphelin |
| 258 | C→S | ZoneDailyAwardOp | ZoneEntryFlow.cs:1022 | CNE_CZ_ZoneServerDailyAwardOP | implémenté · client émet |
| 259 | C→S | — | Eden.Zone/Handlers/MiscHandlers.cs:50 | CNE_CZ_ZoneServerBattleCrusadeJoin | implémenté · ⟂ orphelin |
| 260 | C→S | ZoneArrangeItem | Eden.Zone/Handlers/InventoryHandlers.cs:274 | CNE_CZ_ZoneServerArrangeItem | implémenté · client émet |
| 558 | C→S | ZoneQueryRecommendActivities | ZoneEntryFlow.cs:850 | CNE_CZ_ZoneServerQueryRecommendActivities | implémenté · client émet |
| 623 | C→S | ZoneUseSkillPointMax | Eden.Zone/Handlers/PlayerHandlers.cs:240 | CNE_CZ_ZoneServerUseSkillPointMax | implémenté · client émet |
| 32001 | C→S | ZoneAdvStatsQuery | ZoneEntryFlow.cs:514 |  | implémenté · client émet |

### S→C (364 opcodes : 359 natifs `CNC_CZ_*` + customs 2032/2033/9057/9058/32002 — 284 émis, 80 jamais émis)

| opcode | direction | nom_client | handler_serveur | natif_si_connu | statut |
|---:|---|---|---|---|---|
| 261 | S→C | ZoneClientElfCastEnchant | — | CNC_CZ_ClientElfCastEnchant | absent · client✓ |
| 262 | S→C | ZoneClientTicketInvalid | Eden.Zone/Handlers/EntryHandlers.cs:35 | CNC_CZ_ClientTicketInvalid | implémenté · client✓ |
| 263 | S→C | — | Eden.Zone/ZoneEntryFlow.DisplayChar.cs:52 | CNC_CZ_ClientDisplayChar | implémenté · client — |
| 264 | S→C | ZoneMoveChar | Eden.Zone/Handlers/MovementHandlers.cs:24 ×3 | CNC_CZ_ClientMoveChar | implémenté · client✓ |
| 265 | S→C | ZoneSeeChar | Eden.Zone/ZoneEntryFlow.SeeChar.cs:83 ×2 | CNC_CZ_ClientSeeChar | implémenté · client✓ |
| 266 | S→C | ZoneClientTextOutput | Eden.Zone/Handlers/GmHandlers.cs:345 ×8 | CNC_CZ_ClientTextOutput | implémenté · client✓ |
| 267 | S→C | ZoneClientGoto | Eden.Zone/ZoneEntryFlow.Combat.Mob.cs:320 ×3 | CNC_CZ_ClientGoto | implémenté · client✓ |
| 268 | S→C | ZoneClientSay | Eden.Zone/ZoneEntryFlow.Chat.cs:47 ×3 | CNC_CZ_ClientSay | implémenté · client✓ |
| 269 | S→C | ZoneLifeEradicate | Eden.Zone/ZoneEntryFlow.Combat.Death.cs:229 ×11 | CNC_CZ_ClientLifeEradicate | implémenté · client✓ |
| 270 | S→C | ZoneClientResetCoord | Eden.Zone/ZoneEntryFlow.Movement.cs:51 ×3 | CNC_CZ_ClientResetCoord | implémenté · client✓ |
| 271 | S→C | ZoneDisplayMonster | Eden.Zone/ZoneEntryFlow.Combat.Mob.cs:79 ×2 | CNC_CZ_ClientDisplayMonster | implémenté · client✓ |
| 272 | S→C | — | Eden.Zone/Handlers/CombatHandlers.cs:47 ×3 | CNC_CZ_ClientMeleeAttack | implémenté · client✓ |
| 273 | S→C | ZoneClientRestore | Eden.Zone/ZoneEntryFlow.BattleRoyale.cs:187 ×2 | CNC_CZ_ClientRestore | implémenté · client✓ |
| 274 | S→C | ZonePutItem | Eden.Zone/Codecs/NativeItemCodec.cs:628 ×17 | CNC_CZ_ClientPutItemIntoInventory | implémenté · client✓ |
| 275 | S→C | ZoneDropItem | Eden.Zone/Handlers/GmHandlers.cs:435 ×24 | CNC_CZ_ClientDropItem | implémenté · client✓ |
| 276 | S→C | ZoneChangeEquip | Eden.Zone/Handlers/InventoryHandlers.Helpers.cs:400 ×2 | CNC_CZ_ClientChangeEquip | implémenté · client✓ |
| 277 | S→C | ZoneSwitchItemLocationOk | Eden.Zone/Handlers/InventoryHandlers.Helpers.cs:264 ×4 | CNC_CZ_ClientSwitchItemLocationOK | implémenté · client✓ |
| 278 | S→C | ZoneClientSpellCast | Eden.Zone/Handlers/CombatHandlers.cs:100 ×6 | CNC_CZ_ClientSpellCast | implémenté · client✓ |
| 279 | S→C | ZoneClientSpellCastOK | Eden.Zone/Handlers/CombatHandlers.cs:93 ×15 | CNC_CZ_ClientSpellCastOK | implémenté · client✓ |
| 280 | S→C | ZoneUpdateAttr | Eden.Zone/Handlers/CombatHandlers.cs:274 ×19 | CNC_CZ_ClientUpdateAttr | implémenté · client✓ |
| 281 | S→C | ZoneClientSpellHitToLife | Eden.Zone/ZoneEntryFlow.Combat.Spells.cs:633 ×2 | CNC_CZ_ClientSpellHitToLife | implémenté · client✓ |
| 282 | S→C | ZoneUpdateEquipmentView | Eden.Zone/Handlers/InventoryHandlers.Helpers.cs:513 | CNC_CZ_ClientUpdateEquipment | implémenté · client✓ |
| 283 | S→C | ZoneModelModify | Eden.Zone/Handlers/ItemActionHandlers.cs:1301 ×9 | CNC_CZ_ClientModelModify | implémenté · client✓ |
| 284 | S→C | ZoneClientLevelUp | Eden.Zone/ZoneEntryFlow.Combat.Rewards.cs:349 ×2 | CNC_CZ_ClientLevelUp | implémenté · client✓ |
| 285 | S→C | ZoneDisplayDialogView | Eden.Zone/ZoneEntryFlow.Dialogue.cs:150 ×4 | CNC_CZ_ClientDisplayDialogView | implémenté · client✓ |
| 286 | S→C | ZoneClientGetSpell | Eden.Zone/Handlers/InstanceHandlers.cs:109 ×4 | CNC_CZ_ClientGetSpell | implémenté · client✓ |
| 287 | S→C | ZoneClientSpellBreak | Eden.Zone/ZoneEntryFlow.Breakpoint.cs:121 ×2 | CNC_CZ_ClientSpellBreak | implémenté · client✓ |
| 288 | S→C | ZoneClientSpellBlastOK | Eden.Zone/ZoneEntryFlow.Combat.Spells.Resolution.cs:294 ×4 | CNC_CZ_ClientSpellBlastOK | implémenté · client✓ |
| 289 | S→C | ZoneClientSpellBlastToLife | Eden.Zone/ZoneEntryFlow.Combat.Mob.cs:867 ×4 | CNC_CZ_ClientSpellBlastToLife | implémenté · client✓ |
| 290 | S→C | ZoneClientPlayerAction | Eden.Zone/Handlers/PlayerHandlers.cs:65 ×8 | CNC_CZ_ClientPlayerAction | implémenté · client✓ |
| 291 | S→C | ZoneModifyItemSlot | Eden.Zone/Handlers/InventoryHandlers.cs:218 ×11 | CNC_CZ_ClientModifyItemDurability | implémenté · client✓ |
| 292 | S→C | ZoneClientEffectReport | Eden.Zone/ZoneEntryFlow.Combat.Spells.cs:690 ×4 | CNC_CZ_ClientEffectReport | implémenté · client✓ |
| 293 | S→C | ZoneNodeExit | Eden.Zone/ZoneEntryFlow.Migration.cs:387 | CNC_CZ_ClientNodeExit | implémenté · client✓ |
| 294 | S→C | ZoneLeaveNode | — | CNC_CZ_ClientLeaveNode | absent · client✓ |
| 295 | S→C | ZoneClientDestShutdown | — | CNC_CZ_ClientDestShutdown | absent · client✓ |
| 296 | S→C | ZoneAttrRefresh | Eden.Zone/EntryReplayData.cs | CNC_CZ_ClientAttrRefresh | implémenté · client✓ |
| 297 | S→C | ZoneClientSpellBlastToLocation | Eden.Zone/ZoneEntryFlow.Combat.Spells.Resolution.cs:994 | CNC_CZ_ClientSpellBlastToLocation | implémenté · client✓ |
| 298 | S→C | ZoneSeeMob | Eden.Zone/ZoneEntryFlow.Sight.cs:345 ×5 | CNC_CZ_ClientSeeMob | implémenté · client✓ |
| 299 | S→C | ZoneEnchantAdd | Eden.Zone/ZoneEntryFlow.Items.cs:465 ×5 | CNC_CZ_ClientEnchantAdd | implémenté · client✓ |
| 300 | S→C | ZoneEnchantRemove | Eden.Zone/Handlers/InventoryHandlers.cs:104 ×7 | CNC_CZ_ClientEnchantRemove | implémenté · client✓ |
| 301 | S→C | ZoneEnchantUpdate | Eden.Zone/Handlers/MiscHandlers.cs:158 ×4 | CNC_CZ_ClientEnchantUpdate | implémenté · client✓ |
| 302 | S→C | ZoneFlagModify | Eden.Zone/ZoneEntryFlow.Combat.Mob.cs:666 ×3 | CNC_CZ_ClientFlagModify | implémenté · client✓ |
| 303 | S→C | ZoneClientItemUse | Eden.Zone/Handlers/ItemActionHandlers.cs:1273 | CNC_CZ_ClientItemUse | implémenté · client✓ |
| 304 | S→C | ZoneEnterShop | Eden.Zone/Handlers/ShopHandlers.cs:29 ×4 | CNC_CZ_ClientEnterShop | implémenté · client✓ |
| 305 | S→C | ZoneDie | Eden.Zone/ZoneEntryFlow.Combat.Death.cs:117 ×2 | CNC_CZ_ClientDie | implémenté · client✓ |
| 306 | S→C | ZoneRevive | Eden.Zone/ZoneEntryFlow.Combat.Death.cs:418 ×2 | CNC_CZ_ClientRevive | implémenté · client✓ |
| 307 | S→C | ZoneClientPlayerEmotion | Eden.Zone/Handlers/PlayerHandlers.cs:56 | CNC_CZ_ClientPlayerEmotion | implémenté · client✓ |
| 308 | S→C | ZoneBuyUp | Eden.Zone/Handlers/ShopHandlers.cs:97 | CNC_CZ_ClientBuyUp | implémenté · client✓ |
| 309 | S→C | ZoneClientSellOut | Eden.Zone/Handlers/ShopHandlers.cs:181 | CNC_CZ_ClientSellOut | implémenté · client✓ |
| 310 | S→C | ZoneEnterNodePlayerData | Eden.Zone/Handlers/EntryHandlers.cs:82 ×2 | CNC_CZ_ClientEnterNodePlayerData | implémenté · client✓ |
| 311 | S→C | ZoneEnterNodeEquipAndSpell | Eden.Zone/Handlers/EntryHandlers.cs:229 ×2 | CNC_CZ_ClientEnterNodeEquipAndSpell | implémenté · client✓ |
| 312 | S→C | ZoneEnterNodeInventory | Eden.Zone/Handlers/EntryHandlers.cs:163 | CNC_CZ_ClientEnterNodeInventory | implémenté · client✓ |
| 313 | S→C | ZoneEnterNodeNodeData | Eden.Zone/ZoneEntryFlow.Admin.cs:92 ×6 | CNC_CZ_ClientEnterNodeNodeData | implémenté · client✓ |
| 314 | S→C | ZoneClientCanCloseConnection | — | CNC_CZ_ClientCanCloseConnection | absent · client✓ |
| 315 | S→C | ZoneEnchantReset | — | CNC_CZ_ClientEnchantReset | absent · client✓ |
| 316 | S→C | ZoneClientTextOutputIndex | Eden.Zone/Handlers/FamilyHandlers.cs:84 ×97 | CNC_CZ_ClientTextOutputIndex | implémenté · client✓ |
| 317 | S→C | ZoneTeamLeaved | Eden.Zone/Handlers/TeamHandlers.cs:75 ×3 | CNC_CZ_ClientTeamLeaved | implémenté · client✓ |
| 318 | S→C | ZoneTeamInviteNotice | Eden.Zone/Handlers/TeamHandlers.cs:31 | CNC_CZ_ClientTeamInviteNotice | implémenté · client✓ |
| 319 | S→C | ZoneTeamInviteResult | — | CNC_CZ_ClientTeamInviteResult | absent · client✓ |
| 320 | S→C | ZoneClientSafeQuitCancel | Eden.Zone/Handlers/PlayerHandlers.cs:31 | CNC_CZ_ClientSafeQuitCancel | implémenté · client✓ |
| 321 | S→C | ZoneClientSafeQuitResp | Eden.Zone/Handlers/PlayerHandlers.cs:22 | CNC_CZ_ClientSafeQuitResp | implémenté · client✓ |
| 322 | S→C | ZoneClientNonSafeQuit | — | CNC_CZ_ClientNonSafeQuit | absent · client✓ |
| 323 | S→C | ZoneClientDisplayCharSimple | — | CNC_CZ_ClientDisplayCharSimple | absent · client✓ |
| 324 | S→C | ZoneTeamAllMembersInfo | Eden.Zone/Handlers/TeamHandlers.cs:380 | CNC_CZ_ClientTeamAllMembersInfo | implémenté · client✓ |
| 325 | S→C | ZoneTeamNewMemberInfo | — | CNC_CZ_ClientTeamNewMemberInfo | absent · client✓ |
| 326 | S→C | ZoneMovePrediction | Eden.Zone/ZoneEntryFlow.Combat.cs:419 ×2 | CNC_CZ_ClientMovePrediction | implémenté · client✓ |
| 327 | S→C | ZoneClientChannelMsg | Eden.Zone/Handlers/SocialHandlers.cs:367 ×3 | CNC_CZ_ClientChannelMsg | implémenté · client✓ |
| 328 | S→C | ZoneModifyAttr | Eden.Zone/ZoneEntryFlow.Combat.Spells.Resolution.cs:595 | CNC_CZ_ClientModifyAttr | implémenté · client✓ |
| 329 | S→C | ZoneSeeTreasure | Eden.Zone/ZoneEntryFlow.Sight.cs:250 ×2 | CNC_CZ_ClientSeeTreasure | implémenté · client✓ |
| 330 | S→C | ZoneTreasureDisappear | Eden.Zone/ZoneEntryFlow.Sight.cs:262 | CNC_CZ_ClientTreasureDisappear | implémenté · client✓ |
| 331 | S→C | ZoneClientTradeRequest | Eden.Zone/Handlers/TradeHandlers.cs:73 | CNC_CZ_ClientTradeRequest | implémenté · client✓ |
| 332 | S→C | ZoneClientTradeStart | Eden.Zone/Handlers/TradeHandlers.cs:87 | CNC_CZ_ClientTradeStart | implémenté · client✓ |
| 333 | S→C | ZoneClientTradeAddItem | Eden.Zone/Handlers/TradeHandlers.cs:95 | CNC_CZ_ClientTradeAddItem | implémenté · client✓ |
| 334 | S→C | ZoneClientTradeRemoveItem | Eden.Zone/Handlers/TradeHandlers.cs:104 | CNC_CZ_ClientTradeRemoveItem | implémenté · client✓ |
| 335 | S→C | ZoneClientTradeAddItemFail | Eden.Zone/Handlers/TradeHandlers.cs:97 | CNC_CZ_ClientTradeAddItemFail | implémenté · client✓ |
| 336 | S→C | ZoneClientTradeModifyGold | Eden.Zone/Handlers/TradeHandlers.cs:111 | CNC_CZ_ClientTradeModifyGold | implémenté · client✓ |
| 337 | S→C | ZoneClientTradeModifyGoldFail | Eden.Zone/Handlers/TradeHandlers.cs:113 | CNC_CZ_ClientTradeModifyGoldFail | implémenté · client✓ |
| 338 | S→C | ZoneClientTradeConfirm | Eden.Zone/Handlers/TradeHandlers.cs:123 | CNC_CZ_ClientTradeConfirm | implémenté · client✓ |
| 339 | S→C | ZoneClientTradeCancel | Eden.Zone/Handlers/TradeHandlers.cs:51 ×2 | CNC_CZ_ClientTradeCancel | implémenté · client✓ |
| 340 | S→C | ZoneClientTradeComplete | Eden.Zone/Handlers/TradeHandlers.cs:133 | CNC_CZ_ClientTradeComplete | implémenté · client✓ |
| 341 | S→C | ZoneClientResurrect | Eden.Zone/Handlers/PlayerHandlers.cs:275 | CNC_CZ_ClientResurrect | implémenté · client✓ |
| 342 | S→C | ZoneClientAskRepairEquipGold | Eden.Zone/Handlers/InventoryHandlers.cs:242 | CNC_CZ_ClientAskRepairEquipGold | implémenté · client✓ |
| 343 | S→C | ZoneClientAskRepairOneItemGold | Eden.Zone/Handlers/InventoryHandlers.cs:253 | CNC_CZ_ClientAskRepairOneItemGold | implémenté · client✓ |
| 344 | S→C | ZoneUpdateSystemVar | Eden.Zone/EntryReplayData.cs | CNC_CZ_ClientUpdateSystemVar | implémenté · client✓ |
| 345 | S→C | ZoneUpdateNpcSymbols | Eden.Zone/ZoneEntryFlow.Mission.cs:241 | CNC_CZ_ClientUpdateNPCSymbols | implémenté · client✓ |
| 346 | S→C | ZoneClientChangeFamily | Eden.Zone/Handlers/FamilyHandlers.cs:259 ×4 | CNC_CZ_ClientChangeFamily | implémenté · client✓ |
| 347 | S→C | ZoneClientEnterFamilyCreation | Eden.Zone/ZoneEntryFlow.Dialogue.cs:275 | CNC_CZ_ClientEnterFamilyCreation | implémenté · client✓ |
| 348 | S→C | ZoneMissionStates | Eden.Zone/Handlers/TeamHandlers.cs:165 ×3 | CNC_CZ_ClientMissionStates | implémenté · client✓ |
| 349 | S→C | ZoneClientEnterSpellShop | Eden.Zone/ZoneEntryFlow.Items.cs:374 ×2 | CNC_CZ_ClientEnterSpellShop | implémenté · client✓ |
| 350 | S→C | ZoneClientEnterRepairShop | Eden.Zone/ZoneEntryFlow.Mission.Commands.cs:197 | CNC_CZ_ClientEnterRepairShop | implémenté · client✓ |
| 351 | S→C | ZoneEnterNodeShortcut | Eden.Zone/Handlers/EntryHandlers.cs:182 | CNC_CZ_ClientEnterNodeShortcut | implémenté · client✓ |
| 352 | S→C | ZoneClientReceiveShortcutOfPage | Eden.Zone/Handlers/PlayerHandlers.cs:182 | CNC_CZ_ClientReceiveShortcutOfPage | implémenté · client✓ |
| 353 | S→C | ZoneClientTradeReset | Eden.Zone/Handlers/TradeHandlers.cs:171 | CNC_CZ_ClientTradeReset | implémenté · client✓ |
| 354 | S→C | ZoneClientUseSkillPointOK | — | CNC_CZ_ClientUseSkillPointOK | absent · client✓ |
| 355 | S→C | ZoneClientLogoutResp | Eden.Zone/Handlers/PlayerHandlers.cs:46 | CNC_CZ_ClientLogoutResp | implémenté · client✓ |
| 356 | S→C | ZoneClientCanLogout | Eden.Zone/ZoneEntryFlow.Versus.cs:207 | CNC_CZ_ClientCanLogout | implémenté · client✓ |
| 357 | S→C | ZoneEnterStorage | Eden.Zone/ZoneEntryFlow.Dialogue.cs:279 ×2 | CNC_CZ_ClientEnterStorage | implémenté · client✓ |
| 358 | S→C | ZoneClientGetStorageData | Eden.Zone/Handlers/InventoryHandlers.Helpers.cs:376 ×2 | CNC_CZ_ClientGetStorageData | implémenté · client✓ |
| 359 | S→C | ZoneClientPutItemIntoStorage | — | CNC_CZ_ClientPutItemIntoStorage | absent · client✓ |
| 360 | S→C | ZoneClientPutItemIntoContainer | — | CNC_CZ_ClientPutItemIntoContainer | absent · client✓ |
| 361 | S→C | ZoneClientSpellLoopCastStop | — | CNC_CZ_ClientSpellLoopCastStop | absent · client✓ |
| 362 | S→C | ZoneClientQueryNpc | — | CNC_CZ_ClientQueryNpc | absent · client✓ |
| 363 | S→C | ZoneClientAskIdentifyGold | Eden.Zone/Handlers/InventoryHandlers.cs:267 | CNC_CZ_ClientAskIdentifyGold | implémenté · client✓ |
| 364 | S→C | ZoneClientIdentifyItem | Eden.Zone/Handlers/ItemActionHandlers.cs:121 | CNC_CZ_ClientIdentifyItem | implémenté · client✓ |
| 365 | S→C | ZoneClientEnterIdentifyShop | Eden.Zone/ZoneEntryFlow.Mission.Commands.cs:202 | CNC_CZ_ClientEnterIdentifyShop | implémenté · client✓ |
| 366 | S→C | ZoneClientEnterExchageItemService | Eden.Zone/ZoneEntryFlow.Mission.Commands.cs:210 | CNC_CZ_ClientEnterExchageItemService | implémenté · client✓ |
| 367 | S→C | ZoneClientResetSpell | — | CNC_CZ_ClientResetSpell | absent · client✓ |
| 368 | S→C | ZoneUpdateCoolDown | Eden.Zone/ItemCooldownService.cs:82 ×3 | CNC_CZ_ClientUpdateCoolDown | implémenté · client✓ |
| 369 | S→C | ZoneClientModifyItemInfo | Eden.Zone/Handlers/EquipTrainHandlers.cs:72 ×8 | CNC_CZ_ClientModifyItemInfo | implémenté · client✓ |
| 370 | S→C | ZoneClientEnterInlayShop | Eden.Zone/ZoneEntryFlow.Mission.Commands.cs:229 | CNC_CZ_ClientEnterInlayShop | implémenté · client✓ |
| 371 | S→C | ZoneClientInlayItemResult | Eden.Zone/ZoneEntryFlow.Strengthen.cs:587 | CNC_CZ_ClientInlayItemResult | implémenté · client✓ |
| 372 | S→C | ZoneTeamMemberEnchant | Eden.Zone/ZoneEntryFlow.Status.cs:894 | CNC_CZ_ClientTeamMemberEnchant | implémenté · client✓ |
| 373 | S→C | ZoneStorageExpandResult | Eden.Zone/Handlers/InventoryHandlers.cs:469 | CNC_CZ_ClientStorageExpandResult | implémenté · client✓ |
| 374 | S→C | ZoneClientSloganUpdate | — | CNC_CZ_ClientSloganUpdate | absent · client✓ |
| 375 | S→C | ZoneClientVendorClose | Eden.Zone/Handlers/ShopHandlers.cs:263 ×2 | CNC_CZ_ClientVendorClose | implémenté · client✓ |
| 376 | S→C | ZoneClientVendorCloseView | Eden.Zone/ZoneEntryFlow.Vending.cs:401 | CNC_CZ_ClientVendorCloseView | implémenté · client✓ |
| 377 | S→C | ZoneClientVendorStatus | Eden.Zone/ZoneEntryFlow.Vending.cs:470 | CNC_CZ_ClientVendorStatus | implémenté · client✓ |
| 378 | S→C | ZoneClientVendorUpdateView | Eden.Zone/ZoneEntryFlow.Vending.cs:476 ×3 | CNC_CZ_ClientVendorUpdateView | implémenté · client✓ |
| 379 | S→C | ZoneClientVendorContainer | — | CNC_CZ_ClientVendorContainer | absent · client✓ |
| 380 | S→C | ZoneMobTransform | — | CNC_CZ_ClientMobTransform | absent · client✓ |
| 381 | S→C | ZoneTeamInfo | Eden.Zone/Handlers/TeamHandlers.cs:413 | CNC_CZ_ClientTeamInfo | implémenté · client✓ |
| 382 | S→C | ZoneClientMigrationDone | Eden.Zone/Handlers/EntryHandlers.cs:57 ×2 | CNC_CZ_ClientMigrationDone | implémenté · client✓ |
| 383 | S→C | ZoneClientPlayEffect | Eden.Zone/ZoneEntryFlow.Events.Scene.cs:202 ×4 | CNC_CZ_ClientPlayEffect | implémenté · client✓ |
| 384 | S→C | ZoneClientSelectFamilyEmblem | Eden.Zone/ZoneEntryFlow.Mission.Commands.cs:217 | CNC_CZ_ClientSelectFamilyEmblem | implémenté · client✓ |
| 385 | S→C | ZoneClientOpenExploitRank | Eden.Zone/ZoneEntryFlow.Mission.Commands.cs:180 | CNC_CZ_ClientOpenExploitRank | implémenté · client✓ |
| 386 | S→C | ZoneClientTreasurePickupOK | Eden.Zone/ZoneEntryFlow.Treasure.Roll.cs:238 ×2 | CNC_CZ_ClientTreasurePickupOK | implémenté · client✓ |
| 387 | S→C | ZoneClientTeamRadarAttention | Eden.Zone/Handlers/MiscHandlers.cs:692 | CNC_CZ_ClientTeamRadarAttention | implémenté · client✓ |
| 388 | S→C | ZoneEnterMissionList | Eden.Zone/Handlers/SocialHandlers.cs:471 ×2 | CNC_CZ_ClientEnterMissionList | implémenté · client✓ |
| 389 | S→C | ZoneClientBagDueDate | Eden.Zone/Handlers/MiscHandlers.cs:429 | CNC_CZ_ClientBagDueDate | implémenté · client✓ |
| 390 | S→C | ZoneClientUpdateInteraction | — | CNC_CZ_ClientUpdateInteraction | absent · client✓ |
| 391 | S→C | ZoneUpdateMissionState | Eden.Zone/ZoneEntryFlow.Mission.cs:246 | CNC_CZ_ClientUpdateMissionState | implémenté · client✓ |
| 392 | S→C | ZoneTeamMemberPosition | Eden.Zone/ZoneEntryFlow.Team.cs:38 | CNC_CZ_ClientTeamMemberPosition | implémenté · client✓ |
| 393 | S→C | ZoneEnterTransport | Eden.Zone/Handlers/ShopHandlers.cs:444 ×2 | CNC_CZ_ClientEnterTransport | implémenté · client✓ |
| 394 | S→C | ZoneClientTradeReady | Eden.Zone/Handlers/TradeHandlers.cs:146 | CNC_CZ_ClientTradeReady | implémenté · client✓ |
| 395 | S→C | ZoneClientTradeUnConfirm | Eden.Zone/Handlers/TradeHandlers.cs:157 | CNC_CZ_ClientTradeUnConfirm | implémenté · client✓ |
| 396 | S→C | ZoneChangeAppellation | Eden.Zone/ZoneEntryFlow.Titles.cs:70 ×2 | CNC_CZ_ClientChangeAppellation | implémenté · client✓ |
| 397 | S→C | ZoneUpdateAppellation | Eden.Zone/ZoneEntryFlow.Titles.cs:60 | CNC_CZ_ClientUpdateAppellation | implémenté · client✓ |
| 398 | S→C | ZoneAddAppellation | Eden.Zone/ZoneEntryFlow.Mission.Commands.cs:311 | CNC_CZ_ClientAddAppellation | implémenté · client✓ |
| 399 | S→C | ZoneMailOpenBoxNotify | Eden.Zone/Handlers/SocialHandlers.cs:165 ×3 | CNC_CZ_ClientMailOpenBox | implémenté · client✓ |
| 400 | S→C | ZoneMailList | Eden.Zone/ZoneEntryFlow.Mission.cs:786 | CNC_CZ_ClientMailList | implémenté · client✓ |
| 401 | S→C | ZoneMailReadResult | Eden.Zone/ZoneEntryFlow.Mission.cs:417 | CNC_CZ_ClientMailRead | implémenté · client✓ |
| 402 | S→C | ZoneMailDeleteResult | Eden.Zone/ZoneEntryFlow.Mission.cs:424 | CNC_CZ_ClientMailDeleteResult | implémenté · client✓ |
| 403 | S→C | ZoneMailSendResult | Eden.Zone/Handlers/SocialHandlers.cs:43 ×3 | CNC_CZ_ClientMailSendResult | implémenté · client✓ |
| 404 | S→C | ZoneMailReceive | Eden.Zone/ZoneEntryFlow.Mission.cs:441 ×2 | CNC_CZ_ClientMailReceive | implémenté · client✓ |
| 405 | S→C | ZoneMailGetItemResult | Eden.Zone/ZoneEntryFlow.Mission.cs:450 | CNC_CZ_ClientMailGetItemOK | implémenté · client✓ |
| 406 | S→C | ZoneMailGetGoldResult | Eden.Zone/ZoneEntryFlow.Mission.cs:477 | CNC_CZ_ClientMailGetGoldOK | implémenté · client✓ |
| 407 | S→C | ZoneClientUseItemTime | — | CNC_CZ_ClientUseItemTime | absent · client✓ |
| 408 | S→C | ZoneClientUseItemTimeBreak | — | CNC_CZ_ClientUseItemTimeBreak | absent · client✓ |
| 409 | S→C | ZoneMailReturnResult | Eden.Zone/ZoneEntryFlow.Mission.cs:507 | CNC_CZ_ClientMailReturnResult | implémenté · client✓ |
| 410 | S→C | ZoneClientCommandFailReturn, ZoneCommandFailReturn | Eden.Zone/ZoneEntryFlow.Combat.Mob.cs:173 ×23 | CNC_CZ_ClientCommandFailReturn | implémenté · client✓ |
| 411 | S→C | ZoneClientSearchInfo | — | CNC_CZ_ClientSearchInfo | absent · client✓ |
| 412 | S→C | ZoneEnterNodeElfInfo | Eden.Zone/Handlers/EntryHandlers.cs:211 ×3 | CNC_CZ_ClientEnterNodeElfInfo | implémenté · client✓ |
| 413 | S→C | ZoneElfAdd | Eden.Zone/ZoneEntryFlow.Elf.cs:1091 | CNC_CZ_ClientElfAdd | implémenté · client✓ |
| 414 | S→C | ZoneElfReturnOk | Eden.Zone/ZoneEntryFlow.Elf.Mix.cs:261 ×2 | CNC_CZ_ClientElfReturnOK | implémenté · client✓ |
| 415 | S→C | ZoneClientElfWorkResult | — | CNC_CZ_ClientElfWorkResult | absent · client✓ |
| 416 | S→C | ZoneClientElfWorkResultEnd | Eden.Zone/Handlers/ElfHandlers.cs:127 ×2 | CNC_CZ_ClientElfWorkResultEnd | implémenté · client✓ |
| 417 | S→C | ZoneElfEvent | — | CNC_CZ_ClientElfEvent | absent · client✓ |
| 418 | S→C | ZoneElfUpdateAttr | Eden.Zone/Handlers/InventoryHandlers.cs:1248 ×9 | CNC_CZ_ClientElfUpdateAttr | implémenté · client✓ |
| 419 | S→C | ZoneElfUpdateSkill | — | CNC_CZ_ClientElfUpdateSkill | absent · client✓ |
| 420 | S→C | ZoneElfUpdateSoulAbility | — | CNC_CZ_ClientElfUpdateSoulAbility | absent · client✓ |
| 421 | S→C | ZoneClientElfGoToWork | — | CNC_CZ_ClientElfGoToWork | absent · client✓ |
| 422 | S→C | ZoneClientElfDecompose | — | CNC_CZ_ClientElfDecompose | absent · client✓ |
| 423 | S→C | ZoneClientElfStoneUse | — | CNC_CZ_ClientElfStoneUse | absent · client✓ |
| 424 | S→C | ZoneEnterNodeElfContainer, ZoneClientEnterNodeElfContainer | Eden.Zone/Handlers/EntryHandlers.cs:232 ×3 | CNC_CZ_ClientEnterNodeElfContainer | implémenté · client✓ |
| 425 | S→C | ZoneAuctionBuyList | Eden.Zone/Handlers/ShopHandlers.cs:423 | CNC_CZ_ClientAuctionBuyList | implémenté · client✓ |
| 426 | S→C | ZoneAuctionBuyOk | Eden.Zone/Handlers/ShopHandlers.cs:375 | CNC_CZ_ClientAuctionBuyOK | implémenté · client✓ |
| 427 | S→C | ZoneAuctionCancelOk | Eden.Zone/Handlers/ShopHandlers.cs:395 | CNC_CZ_ClientAuctionCancelOK | implémenté · client✓ |
| 428 | S→C | ZoneAuctionSellList | Eden.Zone/Handlers/ShopHandlers.cs:413 | CNC_CZ_ClientAuctionSellList | implémenté · client✓ |
| 429 | S→C | ZoneAuctionSellResult | Eden.Zone/Handlers/ShopHandlers.cs:325 | CNC_CZ_ClientAuctionSellResult | implémenté · client✓ |
| 430 | S→C | ZoneAuctionHouse | Eden.Zone/ZoneEntryFlow.Dialogue.cs:334 ×2 | CNC_CZ_ClientAuctionHouse | implémenté · client✓ |
| 431 | S→C | ZoneChangeElf | Eden.Zone/Handlers/ElfHandlers.cs:59 ×2 | CNC_CZ_ClientChangeElf | implémenté · client✓ |
| 432 | S→C | ZoneClientEnterStrengthen | Eden.Zone/Handlers/SocialHandlers.cs:172 ×4 | CNC_CZ_ClientEnterStrengthen | implémenté · client✓ |
| 433 | S→C | ZoneStrengthenCasting | Eden.Zone/ZoneEntryFlow.CombineStation.cs:75 ×7 | CNC_CZ_ClientStrengthenCasting | implémenté · client✓ |
| 434 | S→C | ZoneClientStrengthenItemCancel | Eden.Zone/ZoneEntryFlow.CombineStation.cs:95 ×10 | CNC_CZ_ClientStrengthenItemCancel | implémenté · client✓ |
| 435 | S→C | ZoneClientStrengthenItemResult | Eden.Zone/ZoneEntryFlow.CombineStation.cs:177 ×10 | CNC_CZ_ClientStrengthenItemResult | implémenté · client✓ |
| 436 | S→C | ZoneClientModifyItemStrengthen | Eden.Zone/Handlers/ItemActionHandlers.cs:911 ×2 | CNC_CZ_ClientModifyItemStrengthen | implémenté · client✓ |
| 437 | S→C | ZoneClientSetSpellStone | — | CNC_CZ_ClientSetSpellStone | absent · client✓ |
| 438 | S→C | ZoneClientSummonPet | Eden.Zone/ZoneEntryFlow.Summon.cs:74 | CNC_CZ_ClientSummonPet | implémenté · client✓ |
| 439 | S→C | ZoneClientEnterElfReturn | Eden.Zone/ZoneEntryFlow.Items.cs:420 | CNC_CZ_ClientEnterElfReturn | implémenté · client✓ |
| 440 | S→C | ZoneClientMobChangeAI | — | CNC_CZ_ClientMobChangeAI | absent · client✓ |
| 441 | S→C | ZoneClientTurnChar | Eden.Zone/Handlers/PlayerHandlers.cs:81 ×2 | CNC_CZ_ClientTurnChar | implémenté · client✓ |
| 442 | S→C | ZoneElfAction | — | CNC_CZ_ClientElfAction | absent · client✓ |
| 443 | S→C | ZoneClientElfReturnEffect | — | CNC_CZ_ClientElfReturnEffect | absent · client✓ |
| 444 | S→C | — | Eden.Zone/Handlers/PlayerHandlers.cs:107 ×10 | CNC_CZ_ClientGetSelectLifeTarget | implémenté · client✓ |
| 445 | S→C | ZoneEnchantPause | Eden.Zone/Handlers/CombatHandlers.cs:188 | CNC_CZ_ClientEnchantPause | implémenté · client✓ |
| 446 | S→C | ZoneEnchantStart | Eden.Zone/Handlers/CombatHandlers.cs:179 | CNC_CZ_ClientEnchantStart | implémenté · client✓ |
| 447 | S→C | ZoneClientPVPTime | — | CNC_CZ_ClientPVPTime | absent · client✓ |
| 448 | S→C | ZoneClientLotteryResult | Eden.Zone/ZoneEntryFlow.Lottery.cs:665 | CNC_CZ_ClientLotteryResult | implémenté · client✓ |
| 449 | S→C | ZoneClientLotteryEnd | Eden.Zone/ZoneEntryFlow.Lottery.cs:697 | CNC_CZ_ClientLotteryEnd | implémenté · client✓ |
| 450 | S→C | ZoneClientLotteryUpdatePlus | Eden.Zone/ZoneEntryFlow.Lottery.cs:730 | CNC_CZ_ClientLotteryUpdatePlus | implémenté · client✓ |
| 451 | S→C | ZoneClientDuelRequest | Eden.Zone/ZoneEntryFlow.Duel.cs:68 | CNC_CZ_ClientDuelRequest | implémenté · client✓ |
| 452 | S→C | ZoneClientDuelRequestResult | Eden.Zone/ZoneEntryFlow.Duel.cs:98 ×2 | CNC_CZ_ClientDuelRequestResult | implémenté · client✓ |
| 453 | S→C | ZoneClientDuelBegin | Eden.Zone/ZoneEntryFlow.Duel.cs:128 ×3 | CNC_CZ_ClientDuelBegin | implémenté · client✓ |
| 454 | S→C | ZoneClientDuelResult | Eden.Zone/ZoneEntryFlow.Duel.cs:210 ×9 | CNC_CZ_ClientDuelResult | implémenté · client✓ |
| 455 | S→C | ZoneClientEnterMessageBoard | Eden.Zone/ZoneEntryFlow.Dialogue.cs:361 | CNC_CZ_ClientEnterMessageBoard | implémenté · client✓ |
| 456 | S→C | ZoneClientMessageList | Eden.Zone/ZoneEntryFlow.Mission.cs:516 | CNC_CZ_ClientMessageList | implémenté · client✓ |
| 457 | S→C | ZoneClientMessageWriteResult | Eden.Zone/Handlers/MiscHandlers.cs:190 ×3 | CNC_CZ_ClientMessageWriteResult | implémenté · client✓ |
| 458 | S→C | ZoneClientMessageRead | Eden.Zone/ZoneEntryFlow.Mission.cs:554 | CNC_CZ_ClientMessageRead | implémenté · client✓ |
| 459 | S→C | ZoneClientMessageDeleteResult | Eden.Zone/ZoneEntryFlow.Mission.cs:561 | CNC_CZ_ClientMessageDeleteResult | implémenté · client✓ |
| 460 | S→C | ZoneClientLookEquip | Eden.Zone/Handlers/MiscHandlers.cs:494 | CNC_CZ_ClientLookEquip | implémenté · client✓ |
| 461 | S→C | ZoneClientItemRent | Eden.Zone/Handlers/ItemActionHandlers.cs:264 ×2 | CNC_CZ_ClientItemRent | implémenté · client✓ |
| 462 | S→C | ZoneClientBattlefieldList | Eden.Zone/Handlers/InstanceHandlers.cs:52 | CNC_CZ_ClientBattlefieldList | implémenté · client✓ |
| 463 | S→C | ZoneClientEnterBattlefield | — | CNC_CZ_ClientEnterBattlefield | absent · client✓ |
| 464 | S→C | ZoneClientBattlefieldJoinResult | Eden.Zone/Handlers/InstanceHandlers.cs:39 ×2 | CNC_CZ_ClientBattlefieldJoinResult | implémenté · client✓ |
| 465 | S→C | ZoneClientBattlefieldResult | Eden.Zone/ZoneEntryFlow.Mission.cs:353 | CNC_CZ_ClientBattlefieldResult | implémenté · client✓ |
| 466 | S→C | ZoneClientBattlefieldUpdateScore | Eden.Zone/ZoneEntryFlow.Mission.cs:326 | CNC_CZ_ClientBattlefieldUpdateScore | implémenté · client✓ |
| 467 | S→C | ZoneClientAltarState | — | CNC_CZ_ClientAltarState | absent · client✓ |
| 468 | S→C | ZoneClientBattleMemberPosition | — | CNC_CZ_ClientBattleMemberPosition | absent · client✓ |
| 469 | S→C | ZoneClientBattlefieldMemberInfo | — | CNC_CZ_ClientBattlefieldMemberInfo | absent · client✓ |
| 470 | S→C | ZonePlayElfPetAction | Eden.Zone/ZoneEntryFlow.Elf.cs:894 ×2 | CNC_CZ_ClientPlayElfPetAction | implémenté · client✓ |
| 471 | S→C | ZoneUpdateAdvancedAbility | Eden.Zone/ZoneEntryFlow.AdvancedAbility.cs:162 ×3 | CNC_CZ_ClientUpdateAdvancedAbility | implémenté · client✓ |
| 472 | S→C | ZoneUpdateCollection | Eden.Zone/ZoneEntryFlow.Collection.cs:36 ×4 | CNC_CZ_ClientUpdateCollection | implémenté · client✓ |
| 473 | S→C | ZoneClientHello | Eden.Zone/Handlers/EntryHandlers.cs:63 ×2 | CNC_CZ_ClientHello | implémenté · client✓ |
| 474 | S→C | ZoneClientScreenMessage | Eden.Zone/ZoneEntryFlow.Mission.cs:104 | CNC_CZ_ClientScreenMessage | implémenté · client✓ |
| 475 | S→C | ZoneLoverInvite | — | CNC_CZ_ClientLoverInvite | absent · client✓ |
| 476 | S→C | ZoneLoverUpdate | — | CNC_CZ_ClientLoverUpdate | absent · client✓ |
| 477 | S→C | ZoneLoverInfo | — | CNC_CZ_ClientLoverInfo | absent · client✓ |
| 478 | S→C | ZoneLoverBreak | — | CNC_CZ_ClientLoverBreak | absent · client✓ |
| 479 | S→C | ZoneLoverPosition | — | CNC_CZ_ClientLoverPosition | absent · client✓ |
| 480 | S→C | ZoneAchievementUpdate | Eden.Zone/ZoneEntryFlow.Achievement.cs:95 ×2 | CNC_CZ_ClientAchievementUpdate | implémenté · client✓ |
| 481 | S→C | ZoneClientTextOutputItem | — | CNC_CZ_ClientTextOutputItem | absent · client✓ |
| 482 | S→C | ZoneUpdateBFData | Eden.Zone/ZoneEntryFlow.PlayerGuide.cs:123 ×2 | CNC_CZ_ClientUpdateBFData | implémenté · client✓ |
| 483 | S→C | ZoneClientEnterFamilyBuilding | Eden.Zone/ZoneEntryFlow.Dialogue.cs:308 | CNC_CZ_ClientEnterFamilyBuilding | implémenté · client✓ |
| 484 | S→C | ZoneClientFamilyContainerData | Eden.Zone/ZoneEntryFlow.Dialogue.cs:229 ×4 | CNC_CZ_ClientFamilyContainerData | implémenté · client✓ |
| 485 | S→C | ZoneClientFamilyContainerDeleteItem | — | CNC_CZ_ClientFamilyContainerDeleteItem | absent · client✓ |
| 486 | S→C | ZoneClientFamilyContainerPutItem | — | CNC_CZ_ClientFamilyContainerPutItem | absent · client✓ |
| 487 | S→C | ZoneClientFamilyInstanceData | Eden.Zone/Handlers/FamilyHandlers.cs:168 ×2 | CNC_CZ_ClientFamilyInstanceData | implémenté · client✓ |
| 488 | S→C | ZonePlayerClosets | Eden.Zone/ZoneEntryFlow.Services.cs:516 ×3 | CNC_CZ_ClientPlayerClosets | implémenté · client✓ |
| 489 | S→C | ZonePlayerClassList | Eden.Zone/ZoneEntryFlow.Services.cs:193 ×2 | CNC_CZ_ClientPlayerClassList | implémenté · client✓ |
| 490 | S→C | ZoneEnterNodePlayerCard | Eden.Zone/Handlers/EntryHandlers.cs:235 ×2 | CNC_CZ_ClientEnterNodePlayerCard | implémenté · client✓ |
| 491 | S→C | ZoneClientQueryPlayerCard | Eden.Zone/ZoneEntryFlow.Mission.cs:590 | CNC_CZ_ClientQueryPlayerCard | implémenté · client✓ |
| 492 | S→C | ZoneBaseAttrRefresh | Eden.Zone/EntryReplayData.cs | CNC_CZ_ClientBaseAttrRefresh | implémenté · client✓ |
| 493 | S→C | ZoneAttrLevelRefresh | Eden.Zone/EntryReplayData.cs | CNC_CZ_ClientAttrLevelRefresh | implémenté · client✓ |
| 494 | S→C | ZoneClientPlaySound | Eden.Zone/ZoneEntryFlow.Events.Scene.cs:168 | CNC_CZ_ClientPlaySound | implémenté · client✓ |
| 495 | S→C | ZoneClientPlayMonsterAction | Eden.Zone/ZoneEntryFlow.Combat.Mob.cs:51 | CNC_CZ_ClientPlayMonsterAction | implémenté · client✓ |
| 496 | S→C | ZoneEnterWorks | Eden.Zone/ZoneEntryFlow.Dialogue.cs:328 | CNC_CZ_ClientEnterWorks | implémenté · client✓ |
| 497 | S→C | ZonePlayerWorkUpdate | Eden.Zone/ZoneEntryFlow.Work.cs:894 | CNC_CZ_ClientPlayerWorkUpdate | implémenté · client✓ |
| 498 | S→C | ZonePlayerWorkResult | Eden.Zone/ZoneEntryFlow.Work.cs:443 ×2 | CNC_CZ_ClientPlayerWorkResult | implémenté · client✓ |
| 499 | S→C | ZoneClientPlayerEvent | — | CNC_CZ_ClientPlayerEvent | absent · client✓ |
| 500 | S→C | ZonePlayerWorkResultEnd | Eden.Zone/ZoneEntryFlow.Work.cs:357 | CNC_CZ_ClientPlayerWorkResultEnd | implémenté · client✓ |
| 501 | S→C | ZoneClientEnterFamilyInstanceCreation | Eden.Zone/ZoneEntryFlow.Dialogue.cs:368 | CNC_CZ_ClientEnterFamilyInstanceCreation | implémenté · client✓ |
| 502 | S→C | ZoneClientFamilyTownQuery | Eden.Zone/ZoneEntryFlow.FamilyInstance.cs:295 ×2 | CNC_CZ_ClientFamilyTownQuery | implémenté · client✓ |
| 503 | S→C | ZoneClientFamilyTownMemberQuery | Eden.Zone/ZoneEntryFlow.FamilyInstance.cs:413 | CNC_CZ_ClientFamilyTownMemberQuery | implémenté · client✓ |
| 504 | S→C | ZoneFamilyStorageDelete | Eden.Zone/Handlers/InventoryHandlers.cs:618 | CNC_CZ_ClientFamilyStorageDeleteItem | implémenté · client✓ |
| 505 | S→C | ZoneFamilyStoragePutNotify | — | CNC_CZ_ClientFamilyStoragePutItem | absent · client✓ |
| 506 | S→C | ZoneFamilyStorageLog | Eden.Zone/Handlers/FamilyHandlers.cs:190 ×3 | CNC_CZ_ClientFamilyStorageLog | implémenté · client✓ |
| 507 | S→C | ZoneEnterFamilyStorage | Eden.Zone/ZoneEntryFlow.Dialogue.cs:342 | CNC_CZ_ClientEnterFamilyStorage | implémenté · client✓ |
| 508 | S→C | ZoneFamilyStorageData | Eden.Zone/ZoneEntryFlow.Dialogue.cs:353 | CNC_CZ_ClientFamilyStorageData | implémenté · client✓ |
| 509 | S→C | ZonePlayerLicense | Eden.Zone/Handlers/GmHandlers.cs:518 ×7 | CNC_CZ_ClientPlayerLicense | implémenté · client✓ |
| 510 | S→C | ZoneChangeClassShortcut | Eden.Zone/ZoneEntryFlow.Services.cs:695 | CNC_CZ_ClientChangeClassShortcut | implémenté · client✓ |
| 511 | S→C | ZoneClientUpdateLottery | Eden.Zone/ZoneEntryFlow.Lottery.cs:578 | CNC_CZ_ClientUpdateLottery | implémenté · client✓ |
| 512 | S→C | ZoneClientBuyBackList | Eden.Zone/Handlers/ShopHandlers.cs:69 ×3 | CNC_CZ_ClientBuyBackList | implémenté · client✓ |
| 513 | S→C | ZonePetShortCut | Eden.Zone/ZoneEntryFlow.Elf.cs:319 ×3 | CNC_CZ_ClientPetShortCut | implémenté · client✓ |
| 514 | S→C | ZoneClientEnterFamilyTownList | Eden.Zone/ZoneEntryFlow.Dialogue.cs:375 | CNC_CZ_ClientEnterFamilyTownList | implémenté · client✓ |
| 515 | S→C | ZoneClientTerritoryQuery | — | CNC_CZ_ClientTerritoryQuery | absent · client✓ |
| 516 | S→C | ZoneClientTerritoryInfo | Eden.Zone/ZoneEntryFlow.Territory.War.cs:47 ×2 | CNC_CZ_ClientTerritoryInfo | implémenté · client✓ |
| 517 | S→C | ZoneClientCanJoinTeam | — | CNC_CZ_ClientCanJoinTeam | absent · client✓ |
| 518 | S→C | ZoneClientSearchTeamInfo | Eden.Zone/Handlers/MiscHandlers.cs:634 | CNC_CZ_ClientSearchTeamInfo | implémenté · client✓ |
| 519 | S→C | ZoneTeamRecruitMsg | Eden.Zone/Handlers/MiscHandlers.cs:703 ×2 | CNC_CZ_ClientTeamRecruitMsg | implémenté · client✓ |
| 520 | S→C | ZoneClientRollTreasure | Eden.Zone/ZoneEntryFlow.Treasure.Roll.cs:109 | CNC_CZ_ClientRollTreasure | implémenté · client✓ |
| 521 | S→C | ZoneClientRankInfo | Eden.Zone/ZoneEntryFlow.Room.cs:333 | CNC_CZ_ClientRankInfo | implémenté · client✓ |
| 522 | S→C | ZoneClientHateReport | — | CNC_CZ_ClientHateReport | absent · client✓ |
| 523 | S→C | ZoneEnterNodePlayerRoom | Eden.Zone/Handlers/EntryHandlers.cs:238 ×2 | CNC_CZ_ClientEnterNodePlayerRoom | implémenté · client✓ |
| 524 | S→C | ZoneClientEnterGladiatorTeamCreation | Eden.Zone/ZoneEntryFlow.Dialogue.cs:382 | CNC_CZ_ClientEnterGladiatorTeamCreation | implémenté · client✓ |
| 525 | S→C | ZoneClientGladiatorTeamInfo | Eden.Zone/ZoneEntryFlow.Evaluate.cs:390 ×3 | CNC_CZ_ClientGladiatorTeamInfo | implémenté · client✓ |
| 526 | S→C | ZoneClientDisbandGladiatorTeam | — | CNC_CZ_ClientDisbandGladiatorTeam | absent · client✓ |
| 527 | S→C | ZoneClientEnterColosseum | — | CNC_CZ_ClientEnterColosseum | absent · client✓ |
| 528 | S→C | ZoneClientColosseumJoinResult | — | CNC_CZ_ClientColosseumJoinResult | absent · client✓ |
| 529 | S→C | ZoneClientColosseumTeamInfo | — | CNC_CZ_ClientColosseumTeamInfo | absent · client✓ |
| 530 | S→C | ZoneClientColosseumLeaveJoinResult | — | CNC_CZ_ClientColosseumLeaveJoinResult | absent · client✓ |
| 531 | S→C | ZoneClientReviveInfo, ZoneReviveInfo | Eden.Zone/ZoneEntryFlow.Combat.Death.cs:299 ×4 | CNC_CZ_ClientReviveInfo | implémenté · client✓ |
| 532 | S→C | ZoneClientTerritoryKilledInfo | Eden.Zone/ZoneEntryFlow.Territory.War.cs:272 | CNC_CZ_ClientTerritoryKilledInfo | implémenté · client✓ |
| 533 | S→C | ZoneClientCoRideInvite | — | CNC_CZ_ClientCoRideInvite | absent · client✓ |
| 534 | S→C | ZonePlayerDestinyRoad | Eden.Zone/ZoneEntryFlow.Destiny.cs:91 | CNC_CZ_ClientPlayerDestinyRoad | implémenté · client✓ |
| 535 | S→C | ZonePlayerDestinyRoadEvent | Eden.Zone/ZoneEntryFlow.Destiny.cs:459 | CNC_CZ_ClientPlayerDestinyRoadEvent | implémenté · client✓ |
| 536 | S→C | ZoneUpdateZoneMonster | — | CNC_CZ_ClientUpdateZoneMonster | absent · client✓ |
| 537 | S→C | ZoneClientSearchRoomResult | Eden.Zone/ZoneEntryFlow.Room.cs:157 | CNC_CZ_ClientSearchRoomResult | implémenté · client✓ |
| 538 | S→C | ZoneClientUpdateRoom | Eden.Zone/ZoneEntryFlow.Room.cs:107 | CNC_CZ_ClientUpdateRoom | implémenté · client✓ |
| 539 | S→C | ZoneClientOpenHotel | Eden.Zone/ZoneEntryFlow.Dialogue.cs:415 | CNC_CZ_ClientOpenHotel | implémenté · client✓ |
| 540 | S→C | ZoneClientSetRoomResult | — | CNC_CZ_ClientSetRoomResult | absent · client✓ |
| 541 | S→C | ZoneClientRoomData | Eden.Zone/ZoneEntryFlow.Room.cs:271 | CNC_CZ_ClientRoomData | implémenté · client✓ |
| 542 | S→C | ZoneClientUpdateRoomItem | Eden.Zone/ZoneEntryFlow.Room.cs:173 | CNC_CZ_ClientUpdateRoomItem | implémenté · client✓ |
| 543 | S→C | ZonePlayerLifeSkill | Eden.Zone/ZoneEntryFlow.LifeSkill.cs:55 ×2 | CNC_CZ_ClientPlayerLifeSkill | implémenté · client✓ |
| 544 | S→C | ZoneClientLifeSkillUpdate | Eden.Zone/ZoneEntryFlow.LifeSkill.cs:432 ×8 | CNC_CZ_ClientLifeSkillUpdate | implémenté · client✓ |
| 545 | S→C | ZoneClientRideSpell | Eden.Zone/ZoneEntryFlow.Combat.Ride.cs:139 ×3 | CNC_CZ_ClientRideSpell | implémenté · client✓ |
| 546 | S→C | ZoneClientSpellFlyToLocation | — | CNC_CZ_ClientSpellFlyToLocation | absent · client✓ |
| 547 | S→C | ZoneClientTerritoryEnd | Eden.Zone/ZoneEntryFlow.Territory.War.cs:211 | CNC_CZ_ClientTerritoryEnd | implémenté · client✓ |
| 548 | S→C | ZoneClientUpdateSpellAttr | Eden.Zone/ZoneEntryFlow.SpellAttr.cs:183 | CNC_CZ_ClientUpdateSpellAttr | implémenté · client✓ |
| 549 | S→C | ZoneClientUpdateHealthOnlineTime | — | CNC_CZ_ClientUpdateHealthOnlineTime | absent · client✓ |
| 550 | S→C | ZoneClientResetDailyMission | — | CNC_CZ_ClientResetDailyMission | absent · client✓ |
| 551 | S→C | ZoneClientNewPlayerGuide | Eden.Zone/EntryReplayData.cs | CNC_CZ_ClientNewPlayerGuide | implémenté · client✓ |
| 552 | S→C | ZoneClientColosseumModeQueryResult | — | CNC_CZ_ClientColosseumModeQueryResult | absent · client✓ |
| 553 | S→C | ZoneClientRecruitInstanceResult | Eden.Zone/ZoneEntryFlow.Recruit.cs:168 ×5 | CNC_CZ_ClientRecruitInstanceResult | implémenté · client✓ |
| 554 | S→C | ZoneClientUpdateLifelongPoint | Eden.Zone/ZoneEntryFlow.EntryPipeline.Install.cs:280 ×4 | CNC_CZ_ClientUpdateLifelongPoint | implémenté · client✓ |
| 555 | S→C | ZoneClientUpdateLifelongGold | Eden.Zone/ZoneEntryFlow.Admin.cs:543 ×4 | CNC_CZ_ClientUpdateLifelongGold | implémenté · client✓ |
| 556 | S→C | ZoneClientChanceLuckyBagResult | Eden.Zone/ZoneEntryFlow.LuckyBag.cs:234 | CNC_CZ_ClientChanceLuckyBagResult | implémenté · client✓ |
| 557 | S→C | ZoneUpdateFortuneBagCounter | Eden.Zone/ZoneEntryFlow.FortuneBag.cs:146 ×2 | CNC_CZ_ClientUpdateFortuneBagCounter | implémenté · client✓ |
| 559 | S→C | ZoneRecommendActivities | Eden.Zone/ZoneEntryFlow.Recommend.cs:138 ×2 | CNC_CZ_ClientRecommendActivities | implémenté · client✓ |
| 560 | S→C | ZoneClientUpdateRecommendActivities | Eden.Zone/ZoneEntryFlow.Lifelong.cs:282 ×4 | CNC_CZ_ClientUpdateRecommendActivities | implémenté · client✓ |
| 561 | S→C | ZoneClientFamilyBattleInfo | — | CNC_CZ_ClientFamilyBattleInfo | absent · client✓ |
| 562 | S→C | ZoneClientFamilyBattleKilledRank | Eden.Zone/ZoneEntryFlow.Territory.War.cs:199 | CNC_CZ_ClientFamilyBattleKilledRank | implémenté · client✓ |
| 563 | S→C | ZoneUpdateNodeTimes | Eden.Zone/ZoneEntryFlow.Evaluate.cs:823 ×2 | CNC_CZ_ClientUpdateNodeTimes | implémenté · client✓ |
| 564 | S→C | ZoneClientOpenInstanceDialog | Eden.Zone/ZoneEntryFlow.Dialogue.cs:400 | CNC_CZ_ClientOpenInstanceDialog | implémenté · client✓ |
| 565 | S→C | ZoneClientR25UpdateFamily, ZoneR25UpdateFamily | Eden.Zone/ZoneEntryFlow.Family.cs:159 | CNC_CZ_ClientR25UpdateFamily | implémenté · client✓ |
| 566 | S→C | ZoneClientTerritoryFamilyScore | Eden.Zone/ZoneEntryFlow.Territory.War.cs:180 | CNC_CZ_ClientTerritoryFamilyScore | implémenté · client✓ |
| 567 | S→C | ZoneUpdateMixElf | Eden.Zone/ZoneEntryFlow.Elf.Work.cs:255 | CNC_CZ_ClientUpdateMixElf | implémenté · client✓ |
| 568 | S→C | ZoneUpdateElfMixElf | Eden.Zone/ZoneEntryFlow.Elf.Mix.cs:270 | CNC_CZ_ClientUpdateElfMixElf | implémenté · client✓ |
| 569 | S→C | ZoneClientLookElfPetEquip | — | CNC_CZ_ClientLookElfPetEquip | absent · client✓ |
| 570 | S→C | ZoneClientEquipTrainItemChanged | Eden.Zone/Handlers/EquipTrainHandlers.cs:52 | CNC_CZ_ClientEquipTrainItemChanged | implémenté · client✓ |
| 571 | S→C | ZoneClientPVPRankLookEquip | Eden.Zone/ZoneEntryFlow.PvpRankEquip.cs:79 | CNC_CZ_ClientPVPRankLookEquip | implémenté · client✓ |
| 572 | S→C | ZoneClientLoadCubeItems | Eden.Zone/ZoneEntryFlow.Cube.cs:410 | CNC_CZ_ClientLoadCubeItems | implémenté · client✓ |
| 573 | S→C | ZoneClientGreedyIslandInfo | — | CNC_CZ_ClientGreedyIslandInfo | absent · client✓ |
| 574 | S→C | ZonePetUpgradeCasting | Eden.Zone/ZoneEntryFlow.Elf.Mix.cs:94 | CNC_CZ_ClientPetUpgradeCasting | implémenté · client✓ |
| 575 | S→C | ZonePetUpgradeCancel | Eden.Zone/ZoneEntryFlow.Elf.Mix.cs:81 | CNC_CZ_ClientPetUpgradeCancel | implémenté · client✓ |
| 576 | S→C | ZoneClientUpdateWarCoin | Eden.Zone/ZoneEntryFlow.Mission.Commands.cs:126 | CNC_CZ_ClientUpdateWarCoin | implémenté · client✓ |
| 577 | S→C | ZoneClientTeamCheckNotify | Eden.Zone/ZoneEntryFlow.Evaluate.cs:219 ×3 | CNC_CZ_ClientTeamCheckNotify | implémenté · client✓ |
| 578 | S→C | ZoneClientUpdateFloorInfo | — | CNC_CZ_ClientUpdateFloorInfo | absent · client✓ |
| 579 | S→C | ZoneClientOpenAreaDialog | Eden.Zone/ZoneEntryFlow.Dialogue.cs:421 ×2 | CNC_CZ_ClientOpenAreaDialog | implémenté · client✓ |
| 580 | S→C | ZoneClientUpdateAlpacaCoin | Eden.Zone/ZoneEntryFlow.Mission.Commands.cs:134 | CNC_CZ_ClientUpdateAlpacaCoin | implémenté · client✓ |
| 581 | S→C | ZoneClientLifeFloat | — | CNC_CZ_ClientLifeFloat | absent · client✓ |
| 582 | S→C | ZoneClientSearchTeamInfoV2 | Eden.Zone/Handlers/TeamHandlers.cs:285 | CNC_CZ_ClientSearchTeamInfoV2 | implémenté · client✓ |
| 583 | S→C | ZoneTeamRecruitMsgV2 | Eden.Zone/Handlers/TeamHandlers.cs:322 ×2 | CNC_CZ_ClientTeamRecruitMsgV2 | implémenté · client✓ |
| 584 | S→C | ZoneClientCheckTeamTransport | — | CNC_CZ_ClientCheckTeamTransport | absent · client✓ |
| 585 | S→C | ZoneClientOpenMissionShop | Eden.Zone/ZoneEntryFlow.Dialogue.cs:427 | CNC_CZ_ClientOpenMissionShop | implémenté · client✓ |
| 586 | S→C | ZoneClientTerritoryPlayerInfo | — | CNC_CZ_ClientTerritoryPlayerInfo | absent · client✓ |
| 587 | S→C | ZoneClientColosseumQuery | — | CNC_CZ_ClientColosseumQuery | absent · client✓ |
| 588 | S→C | ZoneUpdateElfSpell | Eden.Zone/ZoneEntryFlow.Elf.Spell.cs:186 | CNC_CZ_ClientUpdateElfSpell | implémenté · client✓ |
| 589 | S→C | ZoneUpdateDestinyRoad | Eden.Zone/ZoneEntryFlow.Destiny.cs:79 | CNC_CZ_ClientUpdateDestinyRoad | implémenté · client✓ |
| 590 | S→C | ZoneDestinyDiceFailed | Eden.Zone/ZoneEntryFlow.Destiny.cs:110 | CNC_CZ_ClientDestinyDiceFailed | implémenté · client✓ |
| 591 | S→C | ZoneClientBattleRoyaleInfo | Eden.Zone/ZoneEntryFlow.BattleRoyale.cs:141 ×2 | CNC_CZ_ClientBattleRoyaleInfo | implémenté · client✓ |
| 592 | S→C | ZonePlayerClassCustomData | Eden.Zone/ZoneEntryFlow.CustomClass.cs:122 ×3 | CNC_CZ_ClientPlayerClassCustomData | implémenté · client✓ |
| 593 | S→C | ZoneEnterNodeFastDress | Eden.Zone/ZoneEntryFlow.FastDress.cs:124 ×2 | CNC_CZ_ClientEnterNodeFastDress | implémenté · client✓ |
| 594 | S→C | ZoneClientUpdateFastDressData | Eden.Zone/ZoneEntryFlow.FastDress.cs:155 ×7 | CNC_CZ_ClientUpdateFastDressData | implémenté · client✓ |
| 595 | S→C | ZoneClientInGameLotteryNotify | — | CNC_CZ_ClientInGameLotteryNotify | absent · client✓ |
| 596 | S→C | ZoneClientDiamondLotteryNotify | — | CNC_CZ_ClientDiamondLotteryNotify | absent · client✓ |
| 597 | S→C | ZoneMarryOperation | Eden.Zone/Handlers/SocialHandlers.cs:386 | CNC_CZ_ClientMarryOperation | implémenté · client✓ |
| 598 | S→C | ZoneClientOpenChurchDialog | — | CNC_CZ_ClientOpenChurchDialog | absent · client✓ |
| 599 | S→C | ZoneWeddingsInfo | Eden.Zone/Handlers/SocialHandlers.cs:407 ×2 | CNC_CZ_ClientWeddingsInfo | implémenté · client✓ |
| 600 | S→C | ZoneUpdateAttrNewExp | Eden.Zone/Handlers/CombatHandlers.cs:253 ×3 | CNC_CZ_ClientUpdateAttrNewExp | implémenté · client✓ |
| 601 | S→C | ZoneClientVersusInstanceInfo | Eden.Zone/ZoneEntryFlow.Versus.cs:157 ×2 | CNC_CZ_ClientVersusInstanceInfo | implémenté · client✓ |
| 602 | S→C | ZoneClientOpenPaySystem | Eden.Zone/ZoneEntryFlow.Dialogue.cs:433 | CNC_CZ_ClientOpenPaySystem | implémenté · client✓ |
| 603 | S→C | ZoneClientPayEvent | — | CNC_CZ_ClientPayEvent | absent · client✓ |
| 604 | S→C | ZoneClientPayEventOK | Eden.Zone/ZoneEntryFlow.PayEvent.cs:52 ×3 | CNC_CZ_ClientPayEventOK | implémenté · client✓ |
| 605 | S→C | ZoneClientSlotFlicker | — | CNC_CZ_ClientSlotFlicker | absent · client✓ |
| 606 | S→C | ZoneClientUpdateAttrNewHp | Eden.Zone/Handlers/CombatHandlers.cs:55 | CNC_CZ_ClientUpdateAttrNewHp | implémenté · client✓ |
| 607 | S→C | ZoneClientEvaluateInfo | Eden.Zone/ZoneEntryFlow.Evaluate.cs:135 ×7 | CNC_CZ_ClientEvaluateInfo | implémenté · client✓ |
| 608 | S→C | ZoneClientUpdateEvaluateInfo | Eden.Zone/ZoneEntryFlow.Evaluate.cs:505 | CNC_CZ_ClientUpdateEvaluateInfo | implémenté · client✓ |
| 609 | S→C | ZoneEnterStarWayData | Eden.Zone/Handlers/EntryHandlers.cs:241 ×2 | CNC_CZ_ClientEnterStarWayData | implémenté · client✓ |
| 610 | S→C | ZoneClientUIControlNotify | — | CNC_CZ_ClientUIControlNotify | absent · client✓ |
| 611 | S→C | ZoneClientChangeSpell | Eden.Zone/Handlers/PlayerHandlers.cs:446 | CNC_CZ_ClientChangeSpell | implémenté · client✓ |
| 612 | S→C | ZoneClientOpenMagicBoxDialog | Eden.Zone/ZoneEntryFlow.Dialogue.cs:392 | CNC_CZ_ClientOpenMagicBoxDialog | implémenté · client✓ |
| 613 | S→C | ZoneClientMagicBoxResult | Eden.Zone/ZoneEntryFlow.MagicBox.cs:364 | CNC_CZ_ClientMagicBoxResult | implémenté · client✓ |
| 614 | S→C | ZoneClientBackItem | — | CNC_CZ_ClientBackItem | absent · client✓ |
| 615 | S→C | ZoneUpdateDailyAwards | Eden.Zone/ZoneEntryFlow.DailyAward.cs:69 ×3 | CNC_CZ_ClientUpdateDailyAwards | implémenté · client✓ |
| 616 | S→C | ZoneClientUpdateBattleCrusadeInfo | Eden.Zone/ZoneEntryFlow.Mission.cs:379 | CNC_CZ_ClientUpdateBattleCrusadeInfo | implémenté · client✓ |
| 617 | S→C | ZoneClientOpenBattleCrusadeDialog | Eden.Zone/ZoneEntryFlow.Dialogue.cs:464 | CNC_CZ_ClientOpenBattleCrusadeDialog | implémenté · client✓ |
| 618 | S→C | ZoneClientPetLotteryResult | — | CNC_CZ_ClientPetLotteryResult | absent · client✓ |
| 619 | S→C | ZoneClientPetLotteryEnd | — | CNC_CZ_ClientPetLotteryEnd | absent · client✓ |
| 620 | S→C | ZoneClientArrangeItemOK | Eden.Zone/Handlers/InventoryHandlers.cs:300 | CNC_CZ_ClientArrangeItemOK | implémenté · client✓ |
| 2032 | S→C | ZoneLimitlessState | Eden.Zone/ZoneEntryFlow.Limitless.cs:102 |  | implémenté · client✓ |
| 2033 | S→C | ZoneLimitlessPass | Eden.Zone/ZoneEntryFlow.Limitless.cs:178 |  | implémenté · client✓ |
| 9057 | S→C | — | Eden.Zone/ZoneEntryFlow.cs:495 | CNC_CZ_ClientEchoPong | implémenté · client✓ |
| 9058 | S→C | — | Eden.Zone/ZoneEntryFlow.Services.cs:558 | CNC_CZ_ClientHeroTrait | implémenté · client✓ |
| 32002 | S→C | ZoneAdvStatsInfo | Eden.Zone/ZoneEntryFlow.AdvancedStats.cs:49 |  | implémenté · client✓ |

## Extensions hors protocole natif

| opcode | direction | nom | site |
|---:|---|---|---|
| 0xFFFD/0xFFFE/0xFFFF | gate | `VersionGate` handshake (modern, pré-hello) | `Eden.Core/Networking/Session/VersionGate.cs` |
| 623 | C→S | `CNE_CZ_ZoneServerUseSkillPointMax` (Reforged `btn_max`) | `Eden.Zone/Handlers/PlayerHandlers.cs:240` |
| 2032 | S→C | `ZoneLimitlessState` (Limitless Reborn) | `Eden.Zone/ZoneEntryFlow.Limitless.cs:102` |
| 2033 | S→C | `ZoneLimitlessPass` | `Eden.Zone/ZoneEntryFlow.Limitless.cs:178` |
| 9057 | S→C | `CNC_CZ_ClientEchoPong` (RTT) | `Eden.Zone/ZoneEntryFlow.cs:495` — client `EdenSession.OnZone(EchoPongNcid)` |
| 9058 | S→C | `CNC_CZ_ClientHeroTrait` | `Eden.Zone/ZoneEntryFlow.Services.cs:558` |
| 32001 | C→S | `ZoneAdvStatsQuery` (diagnostic) | `ZoneEntryFlow.cs:514` (`case OpAdvStatsQuery`) |
| 32002 | S→C | `ZoneAdvStatsInfo` | `Eden.Zone/ZoneEntryFlow.AdvancedStats.cs:49` |
| 20 (zone) | C→S | `ChatMessage` (`Eden.Core/Networking/Messages/World/ChatMessage.cs`, `Name`=`CNC_CW_ClientChannelMsg`) | enregistré `Eden.Zone/Program.cs:794` — **mort** : le `case 20` Revive du flow le court-circuite |
| 0–3 (gateway) | C→S | `GatewayHeartbeatMessage`, `GatewayAccountLoginRequest/Response`, `GatewayKickUserMessage` | `Eden.Gateway/Program.cs:29,38` — service de démo, le client ne s'y connecte pas |
| 0 (monde S→C) | S→C | `CNC_CW_ClientForceKick` | classe générée sans NCID natif, jamais émise |
| 254/255/253 | — | `CNC_CL_ServerSCardVerify`/`ClientSCardInfo`/`CNC_G_ServerSCardVerify` | SCard non implémenté |

## Synthèse des écarts

### Paquets émis par le client sans handler serveur (0)

Aucun — tout paquet émis par le client a un traitement côté serveur.

### Handlers serveur orphelins — C→S que le client Godot n'émet jamais (135)

Liste complète = les lignes `⟂ orphelin` des tables ci-dessus. Comptes :
zone 121 / 264,
monde 14 / 35.
Ce sont surtout des NCIDs d'entrée de node, de boutiques PNJ, de
famille/territoire/colosseum — implémentés côté serveur mais jamais
déclenchés par le client Godot actuel (features UI absentes ou requêtes
émises par le client natif uniquement).

### S→C définis mais jamais émis par le serveur (80 zone, 25 monde)

`absent` dans les tables = la classe `NetCommand` existe (générée
depuis le désasm) mais aucun site n'envoie l'opcode — fonctionnalités
non portées (mariage, enchères avancées, événements saisonniers, GM
tool, etc.). Le client journalise ces NCIDs comme « non gérés »
(`EdenSession._unhandledLogged`).

### Conflits de dispatch repérés (lecture statique)

- **Zone 104 `SearchOpt`** : `RegisterChar` présent
  (`Eden.Zone/Handlers/MiscHandlers.cs:566`) mais le flow retourne
  `true` pour 104 via la « liste silencieuse »
  (`ZoneEntryFlow.cs:1190`) avant le dispatcher → handler jamais
  atteint en pratique.
- **Zone 156 `QueryLoverInfo`** : idem — `RegisterChar`
  (`Eden.Zone/Handlers/SocialHandlers.cs:352`) court-circuité par
  `ZoneEntryFlow.cs:1190`.
- **Zone 20** : `ChatMessage` enregistré sur le dispatcher zone
  (`Program.cs:794`) alors que `case 20` (Revive) du flow le précède —
  code mort + nommage trompeur (`CNC_CW_ClientChannelMsg`).
- **Zone 172 / 204** : présents dans la « liste silencieuse »
  (`:1190`) mais déjà traités par leurs `case` du switch — entrées
  redondantes, sans effet.

## Hors périmètre client↔serveur (comptage des classes générées)

Familles inter-serveurs présentes dans `Eden.Core/Networking/
Messages/Generated/*.g.cs` — non routées vers le client Godot :
`CNC/CNE_ZW` zone↔monde (30/30), `CNC/CNE_ZM` zone↔mission (16/17),
`CNC/CNE_ZMis` zone↔mission (136/137), `CNC/CNE_MW` monde↔mission
(19/19), `CNC/CNE_DBA` AgentDBA (11/32), `CNC/CNE_G` gateway natif
(26/10), `CNC/CNE_GJ` (5), `CNC/CNE_CG` canal communauté (9/2),
`CNC/CNE_CGIZ` (1/1), `CNC_T` duplicatas (6/3). Dispatchés sur les
liens `Eden.Zone/Links/ZoneWorldLink.cs`, `Eden.World/ZoneLinkServer.cs`,
`Eden.Zone/ZoneEntryFlow.Mission.cs` — hors de cette carte.

## Sources

- Client : `eden_godot/godot_project/scripts/protocol/*.cs`
  (`public override int Opcode`, `Name`), `scripts/net/`,
  `scripts/session/EdenSession.cs` (`OnZone`/`OnWorld`/burst d'entrée),
  binds `Bind(new …)`/`OnZone(ncid)`.
- Serveur : `eden_server/server/src/Eden.Core/Networking/Messages/
  Generated/*.g.cs` (classes `NetCommand`), `Eden.Zone/ZoneEntryFlow.cs`
  (switch), `Eden.*/Handlers/*.cs` (`d.Register[Char]`), `Program.cs`
  de chaque service, `Eden.Zone/EntryReplayData.cs`, `TicketSession.cs`.
- Natifs : `eden_server/docs/generated/ncid_map.json`
  (`tools/extract_ncids.py`, ordre `InitPCCommandTable` des binaires
  `server_us`), `eden_server/docs/generated/FEATURE-MATRIX.md`,
  `eden_godot/docs/NETWORKING.md` (ports, layouts vérifiés).
