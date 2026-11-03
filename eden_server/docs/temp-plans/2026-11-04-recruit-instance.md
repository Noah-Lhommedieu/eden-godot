# Plan — NCID 163 NodeGroupReset + NCID 220 RecruitInstance (LFG instance)

> **STATUT : implémenté** (2026-11-04) — build 0/0, 16 RecruitTests +
> 950/950 total verts. Fichiers : `RecruitBoard.cs`,
> `RecruitPackets.cs`, `ZoneEntryFlow.Recruit.cs`, `NodeDef.CharLimit/
> RecruitMinLevel`, `EntryState.Recruit*`, dispatch 220/163, hooks
> migration+déco. Reste : validation client live.

> Lot : reset des instances d'équipe (163) + tableau de recrutement
> d'instance avec matchmaking (220). Preuves natives citées inline.

## 1. Modèle natif décodé

### NCID 163 `CNE_CZ_ZoneServerNodeGroupReset` — payload vide

- `Execute` zone @0x0826d9d0 → `CCharacter::NodeGroupReset` @0x08424ff0.
- Deux `CNodeGroup` : celui de l'équipe (`GetTeamNodeGroup`) et celui
  du perso (`char+0x328`).
- `CNodeGroup::CheckResetAllNode` @0x084a8210 : pour chaque nœud
  instancié, si `node+0xd8` (occupants) ≥ 1 →
  - texte **0x6a0** (1696) `%s`=nom du nœud au demandeur,
  - texte **0x6a3** (1699) idem aux autres membres en ligne,
  - refus (retour 0).
- Sinon `RemoveAllNode` + `RemoveAllNodeMap` ; **0x6a2** (1698) à tous
  les membres sauf le demandeur ; membres offline →
  `OfflineResetAddMember` (reset à la prochaine connexion — no-op chez
  nous : l'état instance est recalculé au join).
- **0x6a1** (1697) au demandeur en fin.

Chez nous : le `CNodeGroup` d'équipe = le registre `Evaluates` filtré
par `team.EvalId` (+ instances futures). « Occupants » =
`MemberCharIds` non vide.

### NCID 220 `CNE_CZ_ZoneServerRecruitInstance`

Wire (désasm `Deserialize` zone @0x0827c210) — **variable** :
`[s16 count][count×s16 nodeIds][s16 role][u8 flag]`
(flag 0 = add, 1 = leave — `Execute` zone @0x0827c2f0).
La classe générée `CNE_CZ_ZoneServerRecruitInstance` (3×s16+u8) est
fausse → codec dédié.

Champs zone (char) : `+0xc40` état (-1 rien, 0 en file, 2 matched,
3 en instance), `+0xc42` rôle, `+0xc44..` vector<s16> nœuds demandés,
`+0xc50` flag transport cross-world en cours.

Gates add zone (`CPlayerFactory::RecruitInstanceAdd` @0x08100c40) :
1. `IsNodeClosed` (liste runtime, vide chez nous — skip commenté).
2. `IsEncaged` → silent (prison non modélisée, idem FamilyInstance).
3. état ∉ {-1,3} → texte **0x1154** (4436) + 553 `[0][0]`.
4. état==3 | flag statue | **équipe présente** → **0x1156** (4438) + `[0][0]`.
5. liste vide ou >4 → **0x1155** (4437) + `[0][0]`.
6. nœud courant `IsAnyInstance` (types 4-9,10,12,14-19) → **0x1157** (4439) + `[0][0]`.
7. `+0xbb0`/`+0xbb4` (équipe-trial / EvalId) → **0x114d** (4429) + `[0][0]`.
   Succès zone → état=0, rôle, liste → forward « mission ».

Gates add mission (`CTeamManager::RecruitInstanceAdd` @0x080ce5a0) —
par nœud demandé :
- nœud inexistant → texte **0x1169** (4457) + `[charId][0][0]` (verb
  `RecruitInstanceResult` → zone → 553).
- `char.level < node+0x78` → idem. **+0x78 = col 41** de S_Node
  (niveau requis des instances — col 2 « LV x~y » est vide pour les
  types ≥5 ; vérifié : node35→9, node39→105, node50→96).
- `node+0x7a` (col 43, mission requise) ≠0 && `GetFMS(id)!=1` → idem.
  col43 = '1' sur 378/379 lignes → mission 1 (tuto, quasi-universelle)
  → **skip commenté** (pas de plomberie FMS C# ; risque de tout bloquer).
- `node+0x7c`/`+0x80` (col 44 « buffId stack ») → col44 vide ou '1'
  partout (ParsingString exige 2 tokens) → **no-op prouvé, skip**.
- `node+0x8a` (MaxEntryTimes col46) ≠0 && `nodeTimes[node] >= limit`
  → idem.
Insertion : board `map<node, multimap<role, charId>>` + `map<charId,
vector<node>>`, état=0 → `CheckMakeTeam(node)` → 0 ⇒ 553 `[0][1]`
(mis en file) ; 1 ⇒ équipe formée (résultats [3][x] via transport).

`CheckMakeTeam` mission @0x080c8490 :
- `type != 5 && != 7 && charLimit < 2` → invalide (return 0).
- `board[node].size < charLimit` → 0.
- ≥1 membre rôle-0 requis (deux lower_bound sur clés <1/<2).
- `charLimit` indexe `map<size, rule[7]>` — `InitRecruitRule`
  @0x080cda70, clés **3,5,8** :
  - `rule[3] = {1,1,0,0,0,1,0}` → rôle0 + rôle1 + 1 flex
  - `rule[5] = {1,1,0,0,0,1,2}` → rôle0 + rôle1 + 1 flex + 2 any
  - `rule[8] = {1,1,0,0,1,1,4}` → rôle0 + rôle1 + 2 flex + 4 any
  - index 0-2 = rôle exact i ; index 3-6 = flex (RandPos {0,1,2}
    seed i-3) = premier membre rôle ≤2 dans l'ordre multimap.
  - Règle absente → log + return 0 (reste en file indéfiniment).
- Match : `matched == charLimit` → `CUIDAllocator` uid →
  `map<uid, vector<charId>>` + `map<uid, nodeId>` →
  `InstanceMakeTeam` @0x080c62f0.
- `InstanceMakeTeam` : `char+0x740 = uid` ; par membre → verb zone
  `RecruitInstanceMakeTeam` → le membre quitte son équipe puis
  auto-rejoint celle du leader (InviteAnswer forcée). Au dernier join
  → `CheckRecruitInstanceTransport` @0x080c66d0 → verb zone
  `RecruitInstanceTransport` par membre.
- `RecruitInstanceTransport` zone @0x080f1230 : mort → texte 0x115a +
  `[3][0]` + état=3 ; sinon aire `(node<<16)|0x15` (même remap
  `[normal_area]` key 21 que le trial) → `SetValidCoordInArea` →
  `TransportCharacterToArea` (→ `AddNodeTimes` natif pour les nœuds à
  compteur) → état=3 + 553 `[3][1]`.
- Retour CheckMakeTeam : 1 = équipe formée+transportée, 0 = reste en
  file, -1 = retry une fois (membre disparu mid-match — simplifié :
  on filtre les charId offline au match).

Leave zone (`LeaveRecruitInstance` @0x080fff80) : état 2 → texte
**0x1159** (4441) ; état 3 → silent ; sinon reset champs + forward
mission `flag=1`. Mission `RecruitInstanceLeave` @0x080c7290 : retire
des deux maps, reset `+0x728` → 553 `[charId][1][0]` **par nœud
retiré** (la boucle émet un paquet par entrée). `RecruitInstanceLogout`
@0x080c7690 : auto-leave à la déco si enregistré.

Auto-leave à l'entrée de nœud (`ClientEnterNodeNodeData` @0x08414960) :
si enregistré et type destination ∈ {4,6,8,9,10,14-19} →
LeaveRecruitInstance ; types {5,7,12} conservés.

Résultat 553 `ClientRecruitInstanceResult` = `[u8 a][u8 b]` :
[0][0] échec add · [0][1] en file · [1][0] quitté · [3][0] transport
KO · [3][1] transporté. Zone `RecruitInstanceResult` @0x080ee040 :
[0][0] ou [1][0] → reset champs (le [1][b≠0] restaure le rôle —
quirk natif à conserver).

## 2. Modifications

### Core
- `NodeDef` : + `CharLimit` (col 40, int part — template `+0x74`) et
  `RecruitMinLevel` (col 41, int part — template `+0x78`).
- `EntryState` : `RecruitState` (short, -1), `RecruitRole` (-1),
  `RecruitNodes` (`List<short>`).
- `Game/Services/RecruitBoard.cs` (pur/testable) :
  `Add(charId, role, nodes)`, `Remove(charId)` → liste des
  (node, role) retirés, `TryMatch(node, charLimit)` → charIds ou null
  (règles 3/5/8 hardcodées fidèles ; flex = premier rôle ≤2).

### Zone
- `Codecs/RecruitPackets.cs` : parse wire 220 + build 553.
- `ZoneEntryFlow.Recruit.cs` :
  - `HandleRecruitInstanceAsync` — parse + flag 0/1.
  - Add : gates natifs dans l'ordre → textes + `[0][0]` ;
    insert board + match → `[0][1]` ou MakeTeam+transport.
  - `MakeTeamAsync(node, charIds)` : leader=1er ; LeaveTeam chacun ;
    CreateTeam + AddMember ; broadcast `ClientTeamAllMembersInfo` ;
    état=3 ; `AddNodeTimesAsync` ; `MigrateToNodeAsync` vers l'aire 21
    (reuse `ResolveEvaluateEntryArea` → renommer usage générique) ;
    `[3][1]` à chacun.
  - Leave : gates état → reset + board.Remove + `[1][0]`/nœud.
  - `TryHandleNodeGroupResetAsync` (163) — occupancy check →
    1696+nom/1699+nom ou destroy instances + 1698 membres + 1697.
  - Hooks : déconnexion → auto-leave si enregistré ;
    `MigrateToNodeAsync` → auto-leave si type cible ∈ {4,6,8,9,10,
    14-19} et enregistré (state≠3 no-op via LeaveRecruitInstance).
- `ZoneEntryFlow.cs` : `case 220` + `case 163`.

### Skips documentés
`IsNodeClosed` (liste vide), `IsEncaged`, flag statue `+0xec&0x40`,
mission requise col43 (uniforme '1'), buff interdit col44 (no-op
prouvé), cross-world (mono-monde), blocage de migration hors {5,7}
en file (scope natif incertain — l'auto-leave couvre le cas réel).

## 3. Tests
- Parse wire 220 variable.
- Board : add/remove/match rôles exacts + flex + tailles 3/5/8,
  règle absente → null, `size < charLimit` → null.
- Gates : ordre des refus (état/team/liste/instance/eval/niveau/tries).
- 553 : builds [0][0]/[0][1]/[1][0]/[3][x].
- 163 : occupancy refuse (1696/1699) vs destroy+1697/1698.

## 4. Vérif
`dotnet build` 0/0 + suite complète verte.
