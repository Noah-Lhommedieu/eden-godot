# Plan — Instancing des nœuds de donjon (CNodeGroup natif)

## Problème
`ZoneLiveWorld._nodes` = `nodeId → entités partagées` : tous les
joueurs d'un même nœud voient la même scène. Natif : `CNodeGroup` par
équipe (et un par perso solo) — chaque groupe a sa **copie** des
nœuds de type instance (`IsAnyInstance` = types 4-19, zone
@0x0812b430 / `CreateNodeInstance`). Conséquences : deux équipes dans
le même donjon ne se voient pas, chacune a ses mobs, ses drops, ses
events de scène ; le trial 655 est aussi instancié par équipe.
Directement requis par les lots 163 (reset = vider les instances du
groupe) et 220 (transport → instance du groupe).

## Modèle
- `inst` : int sur `EntryState` — `0` = scène partagée (nœuds
  normaux), `team.Id` = groupe d'équipe, `-charId` = groupe perso.
  Posé à la migration quand `IsAnyInstance(type)` → sinon 0.
- `LiveMob.InstanceKey` ; `_nodes` → `(nodeId, inst)` ; mêmes clés
  pour `GroundTreasureService` (drops au sol) et les watchers de
  broadcast (NodeId+InstanceGroup).
- GC : `RemoveInstances(inst)` — disband d'équipe, déco solo, reset
  163 (occupé → refus 1696/1699 avec nom du nœud).

## Fichiers
- `ZoneLiveWorld` : clés (node,inst), `MobsOf/GroupMobs/SpawnGroup/
  EnsureNodePopulated +inst`, `RemoveInstances`, `LiveMob.InstanceKey`.
- `EntryState.InstanceGroup` ; `MigrateToNodeAsync` le pose.
- Sight (2 sites), Probes (1), MobWander (paires node+inst, watchers
  filtrés), Summon/Items/Combat*/Events/Treasure : passer l'inst.
- `BroadcastToNodeAsync`/`BroadcastSeeMobAsync` : filtrer par inst.
- `GroundTreasureService` : +`InstanceKey` sur les drops.
- `HandleNodeGroupResetAsync` : généraliser au-delà du trial —
  occupancy = sessions avec `InstanceGroup==g` sur les nœuds du
  groupe ; sinon `RemoveInstances(g)` + textes natifs.
- Disband (`TeamHandlers`/leave → équipe vide) + `OnDisconnectAsync`
  solo → `RemoveInstances`.
- `Evaluate` : l'entrée trial migre sur un nœud instancié → clé
  groupe automatique (trial isolé par équipe comme le natif).

## Tests
- `RecruitBoard`-style : instancing = logique de clé — testable via
  `ZoneLiveWorld` (populate 2 inst du même node → mobs distincts,
  kill dans l'un n'affecte pas l'autre, RemoveInstances purge).
- Suite complète + build 0/0.

## Risques / points de vigilance
- `GetMob(handle)` global : les handles restent uniques — vérifier
  `mob.InstanceKey == st.InstanceGroup` à l'attaque (handle vu dans
  une autre instance impossible via sight, mais stale possible).
- Family hall (nœud 501/502) : `AllocNpcHandle` per-char déjà en
  place — compatible (les NPCs famille restent poussés explicitement).
- Pets (`OwnerCharId`) : cibler uniquement les mobs de l'instance du
  maître.

## État — TERMINÉ (build 0/0, 957/957 verts)
- [x] `ZoneLiveWorld` : clés (node,inst) sur tout le registre,
      `RemoveInstances`/`InstanceNodes`, `LiveMob.InstanceKey`.
- [x] `EntryState.InstanceGroup` posé au login + à chaque migration.
- [x] Sight/probes/wander/spawn/despawn/broadcasts filtrés par inst
      (mob.InstanceKey = autorité, st.InstanceGroup = acteur).
- [x] `GroundTreasureService` : `InstanceKey`, `ListForNode`+inst,
      `ClearInstance`.
- [x] `TeamService.MembershipChanged` → refresh de clé on-loop +
      GC du groupe orphelin (join/leave/kick/disband/déco).
- [x] 163 généralisé : occupancy via `InstanceGroup` → refus
      0x6a0/0x6a3 nommé ou `DestroyGroupInstances` + 0x6a2/0x6a1.
- [x] 7 tests `InstancingTests` (isolation mobs/npcs/pets/trésors,
      RemoveInstances ciblé, hook MembershipChanged).
- Note : joueurs non vus entre eux (265 SeeChar non émis — format
  non prouvé) donc pas de fuite joueur↔joueur à traiter.
