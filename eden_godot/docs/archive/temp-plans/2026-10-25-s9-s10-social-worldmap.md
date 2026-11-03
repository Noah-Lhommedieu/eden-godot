# Lot plan-de-secours S9+S10 — social (party/guild/amis) + carte monde/minimap

Date : 2026-10-25. Références : `docs/MASTER-PLAN-SECOURS.md` §S9/S10,
`docs/generated/NCIDS.md`, `docs/NETWORKING.md`. Référence serveur :
`$EDEN/server/` (lecture seule).

## Preuves wire retenues

### Zone — team/party (C→S)
| NCID | Classe | Wire | Preuve |
|---|---|---|---|
| 28 | TeamLeave | `[u32 charId]` | désasm Deserialize zone @0x8290fe0 |
| 29 | TeamInvite | `[u32 charId][str name][u8 lv][u8 class][s16 flags]` | désasm @0x8290780 ; Execute @0x8290400 : charId=0 → résolution par nom (natif) — le serveur C# exige le charId (TeamHandlers.cs:22) |
| 30 | TeamInviteAnswer | `[s16 type][i32 inviterId][u8 bool][s16]` | désasm @0x82922d0 ; type=1 → accept (TeamHandlers.cs:42-49) |
| 31 | TeamInviteCancel | vide | .g.cs |

### Zone — team (S→C)
| NCID | Classe | Usage |
|---|---|---|
| 317 | ClientTeamLeaved `[s16][u8]` | broadcast à tous quand un membre part (serveur : champs 0) → on vide le roster |
| 318 | ClientTeamInviteNotice `[i32 inviter][str name][s16×4]` | popup accepter/refuser |
| 319 | ClientTeamInviteResult `[str][str][s16][u8]` | résultat d'invite (chat sys) |
| 324 | ClientTeamAllMembersInfo `[s16 n][n×rec][s16 teamMode]` | roster complet — rec = `[s16 slot][s16 flags][u32 charId][str name][i64 exp][i64 expMax][i32 lv][i32 hp][s16 m1..m3][u16][s16 m4][str guild]` (.g.cs + BroadcastTeamInfo serveur : flags bit0=leader, m2=classId, m3=mapId, m4=online) |
| 325 | ClientTeamNewMemberInfo | même shape par champ ; ajout membre |
| 381 | ClientTeamInfo `[s16 type][s16 val]` | loot/roll rule (chat sys) |
| 392 | ClientTeamMemberPosition `[i32 charId][s16 node][f32 x][f32 z]` | marqueurs minimap (ZoneEntryFlow.Team.cs) |

### Monde — friends/family (pompé sur la connexion monde)
| NCID | Dir | Wire | Preuve |
|---|---|---|---|
| 12 | C→S | AddFriend `[str name][s16 type]` (1=ami 0=blacklist) | FriendHandlers.cs:52 |
| 14 | C→S | FriendInvitation `[i32 fromId][s16 accept]` | FriendHandlers.cs:115 |
| 22 | C→S | RemoveFriend `[i32 charId]` | FriendHandlers.cs |
| 13 | C→S | QueryFriendInfo vide | mitm (déjà codé) |
| 52 | S→C | `[u16 worldId][s16 n][n×{i32 id,str nom,s16 type,s16 lv,s16 node,s16 race,s16 class,s16 classLv,str guild,u8 online,u16 server,u8 state,u8 public}]` | SocialPackets.cs — désasm world @0x80cc120 |
| 61 | S→C | blacklist `[u8 1][s16 n][n×(i32,str)]` | SocialPackets.cs |
| 50 | S→C | QueryFamilyInfo : `[u8 type]` ; type<2 → `[u16 world][i32 famId][i32 time][str name]…` | FamilyPackets.cs — désasm Serialize @0x80ceb10 |
| 53 | S→C | FriendInvitation `[i32][str]` | .g.cs (jamais émis par le serveur C# — stub tolérant) |

## Conception

- `scripts/protocol/ZoneSocialMessages.cs` (NOUVEAU, pur — testable) :
  codecs ci-dessus + `PartyState` (roster courant, HP max observé) +
  `SocialState` (amis, blacklist, nom de guilde) + `WorldMapPins`
  (mapBtn<i>→nodeId i, zones transport depuis C_Transport.ini).
- `scripts/hud/PartyHud.cs` (NOUVEAU) : panneau haut-gauche sous les
  vitals (x8 y150) — membres nom+Lv+barre HP, boutons Invite-target /
  Invite-name / Leave, popup d'invitation (318 → 30). Lazy-bind
  `SessionCarry.Session` (pattern HudNetBridge) : binds 317/318/319/
  324/325/381.
- `scripts/hud/SocialWindow.cs` (NOUVEAU) : fenêtre « Social » (touche
  O) — nom de guilde en tête (52 entrées + 50 type0 + 324 self),
  liste amis (nom/lv/classe/zone/online), ajout (12), retrait (22),
  blacklist (61). Pompe les trames monde en tâche de fond sur
  `session.World.ReadFrameAsync` (rien d'autre ne lit la connexion
  monde en jeu — canal partagé, arrêté à _ExitTree).
- `MinimapPanel.cs` : marqueurs « ! »/« ? » jaune/cyan sur NPC avec
  symbole quête (bind 345 local, mêmes données que MissionBridge) ;
  portails GATEWAY de la scène courante (SceneTable, carré cyan) ;
  positions coéquipiers 392 (point blanc, même node) ; flèche joueur
  → triangle orienté caméra (GetCamera3D yaw, repli yaw modèle).
  Expose `NodeId` (courant) — monte aussi PartyHud/SocialWindow sur
  le HudRoot dans Build (fichier déjà monté par HudRoot).
- `WorldMapWindow.cs` : tooltips mapBtnN = nom de zone réel
  (mapBtn<i>↔nodeId i — désasm client FUN_00a50b60/FUN_00a55410 :
  page 0 = nodes 1-16,22-27,32-34,36-38 = ids C_Transport continent
  1) + badge « transport » (T_Transport) ; PlayerImg posé au centre
  du bouton du node courant ; ExitBtn referme ; clic mapBtn →
  aperçu de la zone (S###s.png) + nom dans le titre.

## Limites assumées
- Pas de création de guilde (hors scope) ; nom de guilde read-only.
- TeamLeaved 317 sans id → on vide le roster (le serveur le broadcast
  à tous sans préciser qui — re-rempli par le 324 suivant).
- HP bar des membres : le 324 n'a qu'un i32 HP (pas de max prouvé) →
  barre relative au max observé par membre (documenté).
- `/invite` chat : ChatPanel hors ownership → boutons Invite dans
  PartyHud (cible courante via `HudRoot.Target.TargetHandle` ou nom
  résolu via le registre d'entités joueurs handle=charId).

## Validation
- `dotnet build godot_project` 0/0 ; `dotnet test tools/NetTests`
  (nouveaux tests wire : encodages 28/29/30, décodages 318/324/52/50).
- Captures headless : worldmap ouverte, party HUD mock, minimap.
