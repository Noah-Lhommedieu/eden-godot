# 2026-11-04 — B1 : élites d'invasion → cristaux de place forte

## Symptôme

Les élites de légion (60047…) ne marchent jamais vers les cristaux de
place forte (據點水晶) : le mécanisme reborn natif (clefs d'event +
timers) n'est pas consommé — parser muet sur col8/col9/col11, aucun
dispatch naissance/mort, patrouille réservée aux world boss.

## Preuves natives

- `CMonsterDBO::GetAllRebornMonsterInfo` zone @0x083688a0 — layout
  `CRebornMonsterInfo` (0x5c) : +0x1c patrol, +0x18 count, +0x8 respawn,
  +0xc/10/24 xyz, **+0x28 int = col8**, +0x2c = col9, +0x30 = col10,
  +0x34 = col11, +0x38 bool born (col10/11/12 selon nb champs),
  +0x48/+0x4c = annonce, +0x50 = shorts extra.
- `GetRebornMonsterFromNodeDB` (mission) + ctor `CRebornMonster` :
  col8 → `reborn+0x34` = **clef event à la mort** (`OneDie` →
  `CSceneEventCmds::ExecuteCommand`, zone @0x083b69d0) ;
  col9 → `reborn+0x38` = **clef event à la naissance** (`Born` →
  `ScriptEntry '\b'`).
- Données S005 (`corpus/fr-full/data/scene/s005.ini`, lignes 24 ch.) :
  - 116 bleu `55068` : col8=**3**, born=1 — mort → event 3.
  - 117 rouge `60019` : col8=**4**, born=0, col11=**1200** — dormant,
    TTL 20 min → event 4 (retour bleu), annonce 3732.
  - 118 timer `56038` : col9=**1** (born → event 1), col11=**30**
    (TTL), born=1, respawn 3600 → cycle ~1 h.
  - 119 timer `56039` : col8=**2** (mort → event 2), col11=**420**
    (« 7分後關閉 » : mort de vieillesse → nettoyage), born=0.
  - 115 élite `60047` : patrol 308 → dernier waypoint = le cristal.
- `[event]` S005 : ev1 (rouge absent → spawn 115+119), ev2 (fermeture
  → despawn 115+119), ev3 (bleu mort → spawn 117), ev4 (rouge mort →
  spawn 116), paires 5-12 pour les autres places fortes.

## Implémentation

- [x] `SceneParser.MonsterSpawn` : `DeathEventId` (col8, int — la
      colonne n'est pas un facing natif), `BornEventId` (col9, n≥19),
      `BornDelaySeconds` (col11, n≥20).
- [x] `LiveMob` : mêmes champs + `EventSpawned` (SpawnGroup),
      `RowRespawnSeconds` (col4 brute), `SiegeMobHandle`/`NextSiegeAt`.
- [x] `SpawnGroup`/`PopulateNode` : propagation des champs.
- [x] `MobWander` : patrouille aussi pour `EventSpawned` ; marche
      unique (pas de boucle) → ancre au dernier waypoint + cible de
      siège = structure vivante ≤ 30 u ; tick de siège 2 s → mort de
      la structure par `KillSceneMobAsync` (305 broadcast + event col8).
- [x] Timers : armement par cellule dans le tick wander (dict
      `_bornTimerArmed`) → event col9 au spawn (~1 s) + mort de
      vieillesse à +col11 s (évalue col8 → event 2/4 ; respawn si
      col4>0 → cycle horaire 56038).
- [x] `FinishMobDeathAsync` : `mob.DeathEventId > 0` →
      `RunNodeSceneEventAsync` avec la session du tueur (OneDie natif).
- [x] `RunSceneEventAsync`/`RunSceneActionAsync` : `NetSession?` —
      actions joueur (Transport/Buff/Achievement) ignorées sans
      session ; ScreenMessage broadcast au node.
- [x] Garde anti-fantôme dans `ScheduleRespawn` : mob retiré du
      registre (DespawnGroup) ne ressuscite pas.

## Vérif

- [x] build `Eden.Zone` + solution : 0 erreur, 0 warning ;
      tests serveur 64/64 verts.
- [x] live (zone :5201, client Godot, perso test migré S005) — cycle
      complet observé sur les 3 places fortes :
  - populate → timers ouverts 118/123/128 nés → events 1/5/9 tirés
    (col9 → born event) ; élites 60047/60048/60049 + timers clos
    119/124/129 spawnés (298 reçus, fil).
  - marche : `fin de route 308 (387,100) → siège=0xffff85e3` (élite
    60047), idem 60048 route 313, 60049 route 318 — les 9 waypoints
    suivis avec paquets 326 émis aux sessions en sight.
  - siège ~2 s/tick : `structure #55068 détruite par 0xffff85e9 →
    event 3` (20:07:29) ; fil : `305 e385ffff e985ffff` (victime
    bleu, tueur élite). Idem 55070→ev7, 55069→ev11.
  - transition : `annonce 3732 mob #60019` + 298 des cristaux rouges
    (ef85/f085/f185) — events 3/7/11 ont spawné les groupes dormants.
  - fermeture : timers clos expirés (~+2 min) → events 2/6/10 →
    despawn groupes (269 des élites e985/eb85/ed85) ; les rouges
    restent (TTL 1200 → ev 4/8/12 restaureront le bleu).
- [x] capture live `/tmp/b1_invasion.png` (shot à +150 s horloge,
      nouveau suffixe `--shot=path:sec`) : le cristal rouge dressé
      à la place du bleu, perso au pied — transition VISUELLE prouvée.
- [x] instrumentation [W-DBG] retirée, zone relancée sur binaire propre.
