# Crusade (siège guild-vs-guild) — design d'implémentation

> Battlefield arena V1 = fait (BATTLEFIELD_DESIGN.md). Crusade =
> siège guild-vs-guild — chantier mission+guild séparé.

## État actuel
- Zone : `CNE_CZ_ZoneServerBattleCrusadeJoin` (259) → relayé à la
  mission via `CDBAgent::Command "CrusadeJoin crusade_id= %d"`
  (MiscHandlers:46) — zone-side déjà câblé.
- `S_Crusade.ini` : définition des crusades (id, map, schedule,
  récompenses) — à parser comme BattlefieldParser.
- 616/617 crusade dialog/info (zone) : affichage UI crusade.

## Différence vs battlefield
- Arena battlefield = file joueur → match PvP équilibré.
- Crusade = GUILDES s'affrontent pour le contrôle d'un territoire/
  château — registration guild, schedule horaire, siège
  (structures défensives), victoire → contrôle map + récompenses
  guild. Nécessite l'infra guild (existe partiellement — family).
- `IsInBattleCrusade` (Colosseum.cs:112, nt 0x13) : la zone sait
  déjà vérifier si un node est en crusade.

## Plan V1
1. `CrusadeService` mission : registration guild → file des guilds
   inscrites au crusade du schedule actif.
2. Schedule : S_Crusade porte les créneaux — un crusade actif à
   intervalles (comme TerritoryWar existant).
3. Siège : les guilds inscrites se téléportent sur la map crusade ;
   score par kills/structures ; vainqueur → contrôle du node.
4. Récompense guild + stats crusade persistées.

## Références
- `ZoneEntryFlow.Colosseum.cs` — pattern PvP instancié le plus
  proche (inscription, équipes, match).
- `TerritoryWarService` — siège territoire existant, le crusade
  est son analogue guild-focused.

## V1 implémenté (commit)
- `CrusadeService` mission : file des guildes inscrites par
  crusade (Join/Leave/EntriesFor/OpenList).
- `MissionDb.GetFamilyIdAsync` : résout la guilde du char.
- `case "CrusadeJoin"` dans le switch DBA : parse crusade_id +
  family → `_crusade.Join`. Le join client 259 est déjà relayé
  zone→mission — le flux est complet jusqu'à la file.
- Reste : schedule (S_Crusade créneaux), siège (structures/
  contrôle map via TerritoryWarService pattern), reward guild.

## Note — S_Crusade.ini est descriptif
S_Crusade.ini = texte descriptif de la map crusade (nom + mobs
boss), PAS une table de schedule. Le créneau horaire du crusade
vit ailleurs (S_Activity / horaires serveur natif — à chercher
via reindex "crusade" schedule/time dans le binaire mission).
Le join V1 (file guildes) est câblé et indépendant du schedule.

## Natif — crusade = CBattleCrusadeFactory + CCrusadeEventCmds
- `CBattleCrusadeFactory::GetBattleCrusade(short)` : resolve un
  crusade par id (mission @0x08048c60).
- `CCrusadeEventCmds` : les events du siège (structures, points
  de contrôle, timer) — le siège natif est EVENT-DRIVEN, pas un
  simple match comme battlefield. Le join V1 (file) est bon ; le
  siège suit les event-cmds natifs.

## Siège natif — event-driven via CRebornMonster
`CCrusadeEventCmds::ExecuteCommand(CRebornMonster*, int)` — le
siège se pilote par les events de respawn monstres (les
"structures" crusade = mobs reborn à capturer/détruire).
Condition/Action command tables (InitConditionCommandTable /
InitActionCommandTable) = le moteur de script du siège.
Le join V1 (file) reste valide ; le siège suit le système
reborn-monster existant (reborn_monsters + MonsterAction déjà
portés côté zone).

## Pont siège — réutilise le système CEventCmd zone existant
`ZoneEntryFlow.Events.cs` porte déjà If_RebornMonster/
Event_RebornMonster/Event_ScreenMessage (le moteur d'events
natif des scripts S_[event]). Le crusade natif se pilote via les
mêmes cmd — pour un crusade V1 jouable, il suffit :
1. l'activation du crusade au créneau (schedule),
2. les objectifs crusade en events reborn (structures/mobs),
3. la fin = capture du point / timer → contrôle du node.

## Schedule — à confirmer natif
Aucun champ horaire évident dans S_Crusade (descriptif). Le
créneau crusade est probablement en config serveur ou dans
S_Activity — à confirmer via capture/désasm quand le chantier
siège reprendra. Le join V1 (file) est indépendant du schedule.

## V1 — join ack câblé
- Mission case CrusadeJoin → `_crusade.Join` + ack 201
  EnterBattleCrusadeAnswer `[char][crusade][res][×2]` → zone.
- Zone case 201 → client 616 UpdateBattleCrusadeInfo.
- Le flux join crusade est complet : client259→zone DBA→mission
  file→ack→client. Zone-side natif = CBattleCrusadeFactory
  (SyncAll/Check/GmForceChangeStatus) — le siège suit ce pattern.

## Preuve native — StartBattleCrusade zone = STUB VIDE
`CNE_ZMis_ZoneServerStartBattleCrusade::Execute` @0x083353xx =
`return;` (no-op). Le démarrage crusade ne passe PAS par ce
paquet côté zone — le siège vit dans `CBattleCrusadeFactory`
(zone+mission) piloté par `CCrusadeEventCmds` (condition/action
tables sur reborn-monster). Le join V1 (file+ack) reste le bon
point d'entrée ; le siège nécessite le port du factory + event-
cmds, chantier séparé aligné sur le moteur CEventCmd existant.
