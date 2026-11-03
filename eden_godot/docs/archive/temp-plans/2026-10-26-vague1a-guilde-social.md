# Vague 1-A — Guilde & social (client Godot)

Ref : `eden_server/docs/temp-plans/2026-10-27-phase6-completude-ncid-client.md`
(« A — Guilde & social : `GuildWindow` (infos, membres, rangs,
annonces), amis/blacklist/groupe étendu »).

## État audité

Client (`godot_project`) :

- `ZoneSocialMessages.cs` : codecs team zone (28-31 C→S, 317-325/381/392
  S→C), amis monde (12/14/22 C→S, 52/61/53 S→C), 50 `WorldFamilyInfo`
  minimal (nom/niveau seulement). `SocialState` + `PartyState` purs.
- `SocialWindow.cs` : pompe monde propre (`World.ReadFrameAsync`),
  dispatch 52/61/50/53, envois 12/22/14/13, `--mock-social`, touche O.
  **Bug trouvé** : `WorldQueryFamilyInfo { Value = 0 }` — le serveur
  ignore `Value == 0` (`FamilyHandlers.HandleQueryAsync`) → le nom de
  guilde n'arrive jamais. Fix : `Value = 1`.
- Montage : `MinimapPanel.Build` instancie `SocialWindow` (`SocialWnd`).
  `HudRoot.ForceOpen` connaît `social` ; `MenuIconsPanel` émet
  `community` (non branché).

Serveur (oracle `../eden_server/server`) — guilde = « famille » native :

- **Canal monde** : toutes les ops membres passent par
  `CNE_CW_ServerFamilyOp` (NCID 9) `[u8 op][i32 a][i32 b][str s1][str s2]`
  — `Eden.World/Handlers/FamilyHandlers.cs:66-104` (dispatch natif
  `CNE_CW_ServerFamilyOp::Execute @0x80d9ea0`, 22 sous-ops).
- **Canal zone** : création (49 `[str]`), refresh (81 `[u32]` → 346),
  emblème (82), don trésorerie (159 `[i32]`).
- S→C monde : 46 membre, 47 erreur op `[u8]`, 48 invitation
  `[i32 inviter][str][str]`, 49 quit (vide), 50 infos (types 0/2/3/4/5/
  6/7/8), 51 agenda/message-board, 68 journal (6 vecteurs), 74 emblème,
  80 liste recrutement. Formats : `Eden.Core/Game/Family/FamilyPackets.cs`
  (désasm world cité en en-tête).
- S→C zone : 346 `ClientChangeFamily` `[u32][i32 fam][u8][str][i32 rank]
  [s16 emblem][u8 rev]` — émis sur push ZW et en réponse au 81.
- **GAP serveur trouvé** : `FamilyPackets.FamilyInvitation` émet `i32 0`
  alors que le natif envoie le charId de l'inviteur
  (`CNC_CW_ClientFamilyInvitation` ctor @0x08062070 rempli depuis
  `*piVar10` = char_id du SCharacter inviteur, world @0x080523c6).
  L'accept (op 3) exige `inviterId` exact (`TryConsumeInvite`) →
  l'invitation ne peut pas aboutir bout-en-bout tant que le serveur
  émet 0. Documenté en résiduel ; le client renvoie le champ reçu.
- **Rang membre** : absent du record 46/50-8 — seul le leader est
  connu (family.leader_id du bloc type 0) et SON rang (zone 346/i32).
  Affichage : Leader / Member / rang self.

Ops NCID 9 (preuve `FamilyHandlers.cs` + désasm `Execute @0x80d9ea0`) :

| op  | action            | champs                          |
|-----|-------------------|---------------------------------|
| 0x01| inviter           | s1 = nom cible                  |
| 0x02| annuler invites   | —                               |
| 0x03| accepter invite   | a = inviter charId              |
| 0x04| refuser invite    | a = inviter charId              |
| 0x05| quitter           | —                               |
| 0x06| kicker            | a = charId cible                |
| 0x07| passer chef       | a = charId cible                |
| 0x08| dissoudre         | —                               |
| 0x0a| éditer info       | s1 = texte                      |
| 0x0b| titre cadre       | a = index 2-8, s1 = titre       |
| 0x0c| rang cadre        | a = charId, b = rang 0-7        |
| 0x0e| privilège         | a = index 1-14, b = valeur      |
| 0x0f| ajouter message   | s1 = titre, s2 = contenu        |
| 0x11| suppr. message    | a = msgId                        |
| 0x12| marquer lu        | a = état                        |
| 0x13| commentaire mbr   | a = charId, s1 = texte          |
| 0x14| taxe membre       | a = valeur                      |
| 0x15| taxe commerce     | a = valeur                      |
| 0x16| refresh niveau    | —                               |
| 0x46| blob emblème      | s1 = blob latin1                |
| 0x4e| ouvrir recrutement| a = flag                        |
| 0x4f| page recrutement  | a = page                        |
| 0x50| rejoindre recru.  | a = family id                   |

## Plan d'édition (client uniquement)

- [x] Audit protocole + état client/serveur (ci-dessus).
- [x] `ZoneSocialMessages.cs` : nouveaux codecs
  `WorldFamilyOp` (9), `WorldQueryFamilyEmblem` (73), `WorldFamilyMemberUpdate`
  (46), `WorldFamilyOpException` (47), `WorldFamilyInvitation` (48),
  `WorldFamilyQuit` (49), `WorldFamilyInfo` étendu (50 complet :
  bloc famille 19 champs, membres type 8, titres, privilèges),
  `WorldFamilySchedule` (51), `WorldFamilyLogUpdate` (68),
  `WorldFamilyEmblem` (74), `WorldFamilyRecruitList` (80),
  `ZoneFamilyCreate` (49 zone), `ZoneUpdateFamily` (81),
  `ZoneSetFamilyEmblem` (82), `ZoneFamilyDonateMoney` (159),
  `ZoneClientChangeFamily` (346) + `GuildState`/`GuildMember`/
  `GuildBoardMessage`/`GuildLogEntry`/`GuildRecruitEntry` purs.
- [x] `SocialWindow.cs` : dispatch 46-51/68/74/80 → `GuildState`,
  fix `Value=1`, whisper par ligne ami (zone 34 canal 1, cible=nom),
  invite d'ami entrante 53 → accept/refus 14 (ligne dédiée).
- [x] `GuildWindow.cs` nouveau : `GuildWnd`/`GuildPanel`, 4 onglets
  (Infos, Membres, Annonce, Recruit), touche G, DragHelper « guild »,
  `--mock-guild`. Envois : monde 9/10/73 + zone 49/81/82/159.
  Popup invitation 48 → 0x03/0x04.
- [x] Wiring : `MinimapPanel` (montage), `HudRoot.ForceOpenExt`
  (« guild » → `Open()` re-requête 10+81), `DumpRects` (+GuildPanel),
  `OnMenuAction` « community » → SocialPanel / « city » → GuildPanel
  (`ToggleByName`).
- [x] `tools/NetTests/ZoneSocialTests.cs` : +20 tests (9 invite/kick/
  cadre/board, 73, 50 types 0/5/8, 46, 47, 48, 51, 68, 74, 80, zone
  49/81/82/159/346, `GuildState` séquence/zone-change/invite).
- [x] Build + tests + live : `dotnet build` 0 erreur/warning,
  `dotnet test` 361/361 vert ; live `--login=admin:adminmdp
  --channel --char` → `test1` (chef #900001) : logs
  `[guild] bind zone 346 + requête 81/10`, `[wire] D NCID=10 0a0001`
  (query Value=1), `[wire] D NCID=81 510002c2eb0b`, `[wire] E NCID=346`
  `TestGuildHL` ×2, **`[guild] fiche reçue : <TestGuildHL> Lv10`** +
  **`[guild] membres : 3`** (type-0 + chunk type-8 réels),
  `[hud] --hud-open GuildWnd → /root/GameScene/Hud/GuildWnd`,
  `[hud-dump] GuildPanel (276,74) 600×500`,
  `godot-shots/guild-window.png` capturé (rendu Xephyr :91 llvmpipe —
  le display :0 était saturé par 4 clients concurrents et le
  `--shot` interne tirait avant les timers +1 s à ~300 fps).

## Hors-scope / résiduels documentés

- Accept d'invitation guilde : serveur C# émet `i32 0` dans le 48
  (natif : charId inviteur) → 0x03 échouera tant que serveur non corrigé.
- Rang des membres non-leader : pas sur le wire (records 46/50-8
  n'ont pas de champ rang) — affichage Leader/Member.
- Chat guilde : canal 3 fonctionnel serveur (`ZoneEntryFlow.Chat.cs:60`)
  mais non testé multi-clients en mono-zone.
- Hall/ville de famille (zone 174/175/502/503), storage, bâtiments :
  NCIDs zonés hors périmètre de cette fenêtre.
