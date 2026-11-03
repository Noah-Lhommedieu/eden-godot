# Lot — FamilyTownEnter (176) : entrée/sortie du hall de famille

Suite du lot instance (commit 2975117). Objectif : rendre le hall
familial **réellement visitable** — le 487 du lot précédent reste
inaccessible tant que le perso ne peut pas entrer physiquement dans
les nodes 501/502 (type 4, scènes `client/data/scene/S501.ini` /
`S502.ini` présentes dans les deux clients).

## Preuves natives

### CNE_CZ_ZoneServerFamilyTownEnter (176) — zone @0x8259d30

Wire : `[i32 familyId]` (-1 = quitter).

Gates dans l'ordre natif :

1. `IsInNewerTown` — node courant ∈ {94 (0x5e), 768 (0x300)}
   (@0x8409890) → TextOutputIndex **0x207f**.
2. `IsEncaged` → `DebugLog` seulement (silence client). État
   prison non modélisé chez nous — documenté, ignoré.
3. Rate-limit **char+0x650/0x654** (~500 ms) — MÊME champ que le
   173 (mutualisé, `_familyInstanceCreateAt` → renommé
   `_familyInstanceOpAt`).
4. `IsInFamilyInstance` (node type 4, @0x84098d0) **et**
   `familyId == -1` → `TransportCharacterToNode(char+0xa98)` =
   retour au node sauvegardé (aire NORMALE aléatoire).
5. Sinon : équipe `char+0x284 != 0` et `team+0x54 ≥ 0xb`
   (capacité raid ≥ 11 — `DEF_TEAM_MEMBER_SIZE` : party 5,
   raid10 10, raid25 25) → **0x1430** ; `char+0xbb4 != 0`
   (VersusPending) → **0xd1f**.
6. `VisitFamilyInstance` (@0x8171440) :
   - `char+0x3c & 4` (mort) → **0xb80** ;
   - type du node courant ∉ {1, 3, 4} → **0xb89** ;
   - `Ins+0x460` (map des familles instanciées) sans l'entrée →
     `CNC_CZ_ClientTextOutput` (266) `[u8 0x9b][str
     "Can't find family"]` — texte brut, PAS un index ;
   - `SFamily+0x3c == 0` (house_open) → retour silencieux ;
   - sinon relais « VisitFamilyInstance FamilyID=%d » à la
     mission → `TC_VisitFamilyInstance` pose `exec+0x114` et
     broadcast `VisitFamilyID` → `Mission_TC_VisitFamily`
     (zone @0x83acda0) : `char+0xaac = familyId` +
     `TransportCharacterToNode(SFamily+0x3c)`.

### TransportCharacterToNode (@0x8176530)

Position d'arrivée = **NormalArea aléatoire** du template cible +
`SetValidCoordInArea` → pour nous : `EventArea` de type `NORMAL`
de la scène cible (S501 : `1,115.267,29.4,~0,r=4,NORMAL`), point
aléatoire dans le rayon, sinon `Positions[0]`, sinon première
aire GATEWAY.

### CrossZoneData (exec+0xf0/+0xf4/+0xf8 → char+0xa98/+0xa9c/+0xaa0)

Persisté dans le blob exec (`GenerateOffset` : « CrossZoneData »,
« VisitFamilyID » exec+0x114). Sauvegarde mission @0x8134a60
(`CNE_ZMis_MissionServerCrossZone::Execute`) : si le type du node
**quitté** ∉ {9, 10, 13, 15, 16, 17, 19} (instances à retour
dédié), on mémorise (ancien node, pos courante). Consommateur
famille : `+0xa98` dans le 176 (-1) et
`CEventQuitNodeFamilyInstance` (@0x866a700 →
`TransportCharacterToPoint` au point exact, fallback
`TransportCharacterToArea`).

`char+0xaac` n'est JAMAIS effacé après la visite (relit au login
depuis exec+0x114) — on le persiste pareil.

### Sortie par portail

`S501.ini` `[event]` id 100 : `Event_Transport 1 50 0` (gateway
key 1) → couvert par `RunSceneEventAsync`/`MigrateToNodeAsync`
existants — rien à coder.

## Implémentation

- [x] `Database/010-family-visit.sql` : `visit_family_id int`,
      `cross_zone_node int`, `cross_zone_x double`, `cross_zone_y
      double` (défaut 0) sur `player_characters`.
- [x] `CharacterRepository` : SELECT += les 4 colonnes
      (`PlayerCharacterEntity`) ; `SaveInstanceVisitAsync` —
      UPDATE des 4 champs.
- [x] `EntryState` : `CrossZoneNode/CrossZoneX/CrossZoneY` +
      `IsInFamilyInstanceNode` helper (node type 4).
- [x] `Lifecycle` : chargement des 4 colonnes ; `SaveCharacterAsync`
      écrit visit+cross-zone dans la même passe.
- [x] `MigrateToNodeAsync` : si type de l'ANCIEN node ∉ set
      natif → `st.CrossZone* = (NodeId, PosX, PosZ)` avant màj.
- [x] `ZoneEntryFlow.FamilyInstance.cs` : `HandleFamilyTownEnterAsync`
      — gates natifs ci-dessus ; entrée = position NORMAL de la
      scène cible (helper `ResolveInstanceEntryPos` testable) ;
      sortie = `st.CrossZoneNode` (fallback `ReviveAreaId>>16`
      puis node 1).
- [x] `TryHandleOnLoopAsync` : `case 176` ; retrait du stub
      `FamilyHandlers.FamilyTownEnter`.
- [x] Tests : helper position (NORMAL area / fallbacks), gates
      raid/versus/mort/node-type. Build 0/0, suite verte.
- [x] Commit.

## Hors scope

- `CEventQuitNodeFamilyInstance` (compte à rebours 0xa62 +
  TransportCharacterToPoint) — événement cross-zone, aucun
  déclencheur scène dans S501/S502 (les portails utilisent
  Event_Transport).
- 174/175 (query ville/membres → paquets 502/503 multi-vecteurs),
  130/131 conteneur (op NPC 0x3b), 180 upgrade, 225 battle.
