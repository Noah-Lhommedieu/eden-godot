# Plan chantier — croisade siège (CBattleCrusade natif)

## Preuves natives collectées (mission/decompiled.c)

- `CBattleCrusadeFactory` : map<crusadeId, CBattleCrusade*> this+4,
  flag global `this[0x1c]` = système fermé (texte 0x652 au join).
- `CreateNewBattleCrusade(crusadeId, flag)` @0x8048640 :
  TemplateData via `IGameData+0x1dc` → `new CBattleCrusade(id, 600,
  tpl+0x3c * 60)` (600s checkin, durée = tpl+0x3c minutes × 0x3c) →
  `Start` → enregistre → `BattleCrusadeWorldID++` →
  `PreBattleCrusadeCheckInStart(id)` (cascade sur templates dont
  `tpl+0xc == id` — les crusades partagent des ids de schedule).
- `PreBattleCrusadeCheckInStart/End(scheduleId)` : itère la map
  gamedata (+0xdc), crée les crusades dont `tpl+0xc` (start) /
  `tpl+0xe` (end) == scheduleId. Close enchaîne pareil (tpl+0xe →
  relance le crusade suivant).
- `CBattleCrusade::Join(exec)` @0x81ac540 : set charIds (+0x10) ;
  retours : 2=déjà inscrit, 1=plein (`+0x24 >= s16(+0xc)`),
  3=mauvais état (`+0x28 != 1`), 4=OK.
- `TC_BattleCrusadeJoin` @0x81ebc40 (commande script NPC) :
  factory[0x1c]→0x652 ; crusade+0x28!=1 → 0x67a ; Join==4 →
  `exec+0x8c6 = crusadeId` + `NotifyEnterBattleCrusade(charId)`.
- `NotifyEnterBattleCrusade` @0x8048eb0 : `exec+0x8c8 =
  crusade->node(+0x2c)->node_id(+0xc)` ; sauve pos courante dans
  exec+0xf0/+0xf4/+0xf8 + hp(+0xfc = hp*10) ; envoie
  `CNC_ZMis_ZoneServerEnterBattleCrusadeAnswer` [i32 char][s16
  crusade][s16 node][s16 0] à la zone de la map crusade
  (ZoneNodeTemplate lookup).
- `CloseBattleCrusade` : broadcast `CNC_ZMis_ZoneServerBattleCrusadeInfo`
  [s16 id][s16 0][i32 status(+0x28)][i32 +4][u32 +8][u8 0] à toutes
  les zones, puis re-scan tpl+0xe → relance le suivant.
- `CCrusadeEventCmds::ExecuteCommand` @0x805d060 : dispatch des
  events script (kill/object) vers les structures de la map crusade
  (map node-event → liste d'exécuteurs, vfunc+4 sur chacun).
- `Init` : crée les crusades dont `tpl+0x10 != 0` (activés).
- `S_Crusade.ini` : 4 territoires 時空裂縫 (灰岩山/榮譽高地/寧靜之丘/
  腐朽沼澤) — id|nom|nom. Le TemplateData (schedule/durée) vient
  d'une autre table gamedata (à identifier : vfunc+0x1dc).
- `TC_BattleCrusadeClose` / `TC_BattleCrusadeLeave` : commandes
  script (fin anticipée / sortie).

## État actuel C#

- Zone NCID 259 → relay « CrusadeJoin crusade_id= %d » → mission
  (MissionServer.cs:534) → `_crusade.Join(fam, cid, charUid)` +
  ack 201 EnterBattleCrusadeAnswer minimal (char/crusade/res).
- CrusadeService.cs = file de guildes par crusade (V1).

## Incréments proposés

1. **TemplateData** : identifier la table gamedata vfunc+0x1dc
   (schedule tpl+0xc/+0xe/+0x10, durée +0x3c, node +0x2c) —
   probablement S_Node.ini crusade rows ou table dédiée ; croiser
   `CGlobalInfo::ZoneNodeTemplate`.
2. ~~**Cycle de vie** dans MissionServer : création programmée~~
   ✅ **FAIT (V2)** : `CrusadeService` = state machine Phase 1
   (checkin 600 s, constante du ctor natif) → Phase 2 (bataille,
   `EDEN_CRUSADE_BATTLE_MIN`, défaut 90 min) → clôture. Tick 30 s
   dans `MissionServer` + annonces écran (canal 20 → 474).
   Déclenchement : auto au boot (crusade 1, Init natif tpl+0x10≠0)
   + chaîne cyclique 1→2→3→4→1 à la clôture (tpl+0xe natif) +
   `//crusade start|close <id>` admin. Vérifié en live : « start
   crusade=1 node=310 checkin 600s → battle 90min » au boot.
   **E2E prouvé** (17:18) : handshake mission + DBA
   « CrusadeJoin crusade_id= 1 » char 200000002 (guilde 900001) →
   log « join crusade=1 char family=900001 n=1 » + 201
   `[char][res=1][crusade=1][node=310]` ; join sans guilde → res=3.
3. ✅ **FAIT (V2)** — **Entrée** : `CrusadeJoin` mission → codes
   natifs (1=plein, 2=déjà, 3=mauvais état, 4=OK) → 201
   EnterBattleCrusadeAnswer [i32 char][s16 res][s16 crusade]
   [s16 node] ; la zone pose `EntryState.CrusadeId` (char+0xdfc
   natif) + `MigrateToNodeAsync(node, entry)` — nodes 310-313.
   ✅ Sauvegarde/restauration pos (exec+0xf0..f8) :
   CrusadeReturnNode/X/Y posés au join, rapportés au close (530)
   et `CrusadeLeave` relayé à la sortie du node type 19.
4. **Siège — partiel** : ✅ kills PvP comptés (lethalCrusade zone →
   `CrusadeKill` DBA → score par famille, kills cross-famille
   seulement). **E2E prouvé** (17:29, script lien mission) : join 2
   guildes (900001+900002) → `CrusadeBattle` (forçage admin, tick
   sinon) → kills cross-famille `score=1` ×2, kill même-famille
   ignoré silencieusement. Reste : structures cristaux/portes
   (CGameObjFactory + CCrusadeEventCmds).
5. **Clôture — partiel** : ✅ vainqueur = famille au plus haut
   score, annonce + broadcast 530 → zones remettent CrusadeId à 0.
   ✅ Chaîne unifiée : `CrusadeClose`/`CrusadeBattle` passent par
   `AnnounceCrusadeEventAsync` → chaînage 1→2 prouvé (Crusade 2
   checkin + 202 status=1 après close). Commande admin ajoutée :
   `//crusade battle <id>` + DBA `CrusadeBattle`.
   Reste : ownership DB + rewards.
6. ✅ **FAIT (V2)** — **Wire** : 202 ZoneServerBattleCrusadeInfo
   broadcast à chaque transition + push à la connexion zone
   (`PushCrusadeStatusToAsync`) → cache `_crusadeStatus` ; dialog
   « battle_crusade » (service 60) émet le 617 natif
   `[i32 npcUid][u16 n1][checkin][u16 n2][bataille]` (Serialize
   @0x81ec980, sets = `BattleCrusadeStatus` @0x80492e0).
   ⚠️ Aucun NPC de S_Npc.ini n'expose le service battle_crusade —
   chemin d'entrée joueur : `//crusade join <id>` (équivalent NCID
   259). Reste : 200 StartBattleCrusade (début de siège côté zone),
   kill-info 532, rewards + ownership du territoire.
