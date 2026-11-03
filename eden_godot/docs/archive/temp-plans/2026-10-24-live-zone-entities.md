# Plan — branchement live de game.tscn (entrée zone + entités réseau)

Date : 24/10/2026. Serveur local : 127.0.0.1 (déjà up : 6543/5200/5201/2067).

## Faits wire prouvés (capture native + désasm + netsmoke C#)

- Spawn mob/NPC : **298 ClientSeeMob** (GOLDEN, masque u16 + champs
  optionnels). Handle : NPC=0x88C8xxxx, mob=0xFFFFxxxx.
  Floats wire = [x][y][dir(ent+0x58)][speed(+0x18)] — ctor natif
  @0x849df90 (param_6 = this+0x58 direction, param_7 = vitesse).
  NOTE : le serveur C# y écrit (e.Z, facing) dans les slots 3-4 —
  le client tolère les deux (yaw heuristique).
- Spawn joueur distant : **265 ClientSeeChar** (PASSTHRU — codec
  ClientSeeCharCodec.cs serveur) : charId (=handle), x, y, dir, speed…
- Self spawn/pos : **313 EnterNodeNodeData** [s16 nodeId][u8][f32 x][f32 y][f32 z]
  (GOLDEN — node S<Field1>, pos joueur).
- Self echo : **264 ClientMoveChar** [f32 x][f32 y] (GOLDEN, suit MoveSelf).
- Mouvement entités : **326 ClientMovePrediction** [i32 h][u16 x*16]
  [u16 y*16][u8 flag][u16 dir half-float][opt u16 dstX*16 dstY*16].
- Despawn : **269 LifeEradicate** [i32] (vue serveur, DESASM) +
  **294 LeaveNode** [i32] (NON PROUVE) + **330 TreasureDisappear**.
- Attrs : **280 UpdateAttr** [i32][s16 attr][i32 cur][i64 max] (attr 0=HP).
- 267 ClientGoto [i32][f32][f32][u8] (réponse à TargetUpdate 124 C→S).
- 271 DisplayMonster [u32 mob][u8 n][s16×n][u8 gen][i32 owner].
- 283 ModelModify, 296 AttrRefresh (self), 293 NodeExit [u8][u8].
- 310/311/312 EnterNode* = données perso (INCOMPLET → parse préfixe
  310 = [u32 charId][str name], reste brut).
- C→S MoveSelf NCID 1 : [s16 count=0][f32 x][f32 y][f32 dstX][f32 dstY]
  [f32 dir][s16 7] — bytes natifs reproduits.
- Modèles : code `n117010`/`m025010` → stem `m117`/`m025` (npclist/
  monsterlist.ini → mNNN.kfm) → `monster/model/mNNN.glb`.
  Scene [npc]/[reborn_monsters] portent déjà le stem (col motion M014 /
  col12 M025) → `monster/model/{code}.glb`.

## Étapes

- [x] 1. `scripts/protocol/ZoneEntityMessages.cs` : SeeMob 298, SeeChar
      265, MoveChar 264, MovePrediction 326, LifeEradicate 269,
      LeaveNode 294, NodeExit 293, UpdateAttr 280, AttrRefresh 296,
      ModelModify 283, DisplayMonster 271, ClientGoto 267, SeeTreasure
      329, TreasureDisappear 330, MobTransform 380, UpdateZoneMonster
      536, EnterNodePlayerData 310 (préfixe), EnterNodeEquipAndSpell 311
      + EnterNodeInventory 312 (brut), TextCommand 3 (C→S).
- [x] 2. `EdenSession` : `EnterZoneAsync(host,account,pwd,charId,worldId)`,
      `SendZoneAsync(NetMessage)`, dispatch `OnZone(ncid, handler)`,
      `ZoneListenLoopAsync`.
- [x] 3. `scripts/world/EntityRegistry.cs` : handle→RemoteEntity
      (Node3D+NetworkInterpolator+Label3D), résolveurs NPC/mob via
      Scene + MonsterTable, GLB `monster/model/`.
- [x] 4. `GameScene` : `--live` → EnterZoneAsync en tâche, NodeData→
      recharger la bonne map S<Field1>, dispatch trames →
      EntityRegistry, MoveSelf throttlé ~10 Hz.
- [x] 5. Tests NetTests : goldens SeeMob (mob+NPC), MovePrediction
      (11B/15B), SeeChar, UpdateAttr, MoveSelf, EnterNodePlayerData
      préfixe… depuis hex réels des captures.
- [x] 6. NetSmoke `--zone-entry` : stats NCIDs entité parsés.
- [x] 7. Run live → `docs/livesmoke_trace.log` + rapport NCIDs.
