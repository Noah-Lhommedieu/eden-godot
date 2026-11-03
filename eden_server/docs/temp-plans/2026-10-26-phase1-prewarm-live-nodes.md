# Phase 1 — Préchauffage boot des maps « chaudes » (EnsureNodePopulated)

Date : 2026-10-26. Lot : refonte fiabilité/perf phase 1.

## Constat d'investigation

- `ZoneLiveWorld.EnsureNodePopulated(nodeId, inst, scene)`
  (`server/src/Eden.Zone/ZoneLiveWorld.cs:345`) est **lazy** :
  `_nodes.GetOrAdd((node,inst), _ => PopulateNode(...))` sous
  `_nodesLock` — le premier appel pour une (map, instance) paie tout
  le populate.
- `PopulateNode` (même fichier :357) ne fait **aucune IO** : les
  scènes sont déjà parsées au boot (`sceneSvc.LoadScenes`,
  Program.cs:474 — 411 scènes). Mais il alloue `count` `LiveMob` par
  ligne reborn + les handles NPC : S001 = 431 mobs + 256 NPC,
  moyenne ~185 mobs/scène (76 350 lignes×count au total sur 411
  scènes) — centaines d'objets sous `_nodesLock`.
- Tous les call-sites tournent **sur la boucle acteur** :
  `SpawnSceneEntitiesAsync` (MigrationDone, Lifecycle.cs:199),
  `UpdateSightAsync` (MoveSelf, Movement.cs:98),
  `OnTeamMembershipChanged` (Instancing.cs:92 via ScheduleOnLoop).
  → premier joueur d'une map = hitch de populate sur le fil unique
  de la boucle + `_nodesLock` tenu pendant les allocations.
- Les defs (S_Monster, kfIds Motion.ini, masques Locate S_Npc) sont
  injectées **lazy** dans `SpawnSceneEntitiesAsync`
  (Sight.cs:280-293) — un préchauffage boot doit les binder avant.

## Décision

Lazy + coût non trivial sur maps chaudes → **préchauffage au boot**,
avant `listener.Start()`. Pas de changement d'architecture.

Cibles préchauffées (inst=0 uniquement — les instances CNodeGroup se
peuplent par groupe à l'entrée) :
- auto : scènes à spawn joueur (`EventAreas` Type=="BORN" ou
  `BornPositionIndex > 0`) — 9 maps détectées : S002, S016, S019,
  S028, S093, S094, S752, S784, S904 ;
- + `EDEN_ZONE_PREWARM_MAPS` (codes « S001 » ou ids, CSV) pour les
  maps chaudes sans spawn (ex. S001 ville principale) ;
- exclusion des types instance (`InstanceNodeTypes` — la copie
  inst=0 serait morte).

## Points d'édition

- [x] `server/src/Eden.Core/EdenConfig.cs` : `ZonePrewarmMaps`.
- [x] `server/src/Eden.Zone/ZoneLiveWorld.cs` : `BindGameData(gd)`
      idempotent (MonsterDefs/AttackKfIds/NpcLocateMasks si vides).
- [x] `server/src/Eden.Zone/ZoneEntryFlow.Sight.cs` : remplacer le
      bloc d'injection lazy par `BindGameData` (filet de sécurité).
- [x] `server/src/Eden.Zone/ZoneEntryFlow.Recruit.cs` :
      `InstanceNodeTypes` private→internal.
- [x] `server/src/Eden.Zone/Program.cs` : bloc préchauffage dans
      `if (Directory.Exists(sceneDir))` + helpers statics en bas
      (`PrewarmLiveNodes`, `TrySceneNodeId`).
- [x] Build 0 erreur / 0 warning (Eden.Zone + Eden.Tests).

## Vérification live (boot réel, zone de dev en parallèle)

- Sans env : `[Zone] Préchauffage monde vivant : 7 nœud(s) —
  2,16,19,28,93,94,904` (S752/S784 exclus — type 5 = instance,
  population par groupe à l'entrée).
- `EDEN_ZONE_PREWARM_MAPS="S001,bad,3"` → warning sur « bad »,
  `9 nœud(s) — 1,2,3,16,19,28,93,94,904`.
- Le préchauffage précède `listener.Start()` (log avant
  « Serveur prêt »).

Ne pas commit.
