# Plan 2026-10-19 — cast-break, anims dual, portails, quêtes collect

## Symptômes
- Re-cast pendant un timed-cast → perso figé (cleric + toutes classes
  à cast-time). Après spam : mouvement OK mais NPC/skills bloqués.
- test2 (thief lv11) affiché lv1 à la connexion.
- Stats profil figées/hardcodées.
- Quête "collect item from enemy" : le drop n'arrive jamais.
- Voleur à 2 épées : auto-attaque = anim de tir à l'arc.
- Portails classiques inter-zone/instance : aucun ne fonctionne.
- Exigence transverse : tout dynamique depuis les données natives —
  zéro cas par cas NPC/quête.

## Preuves natives déjà établies
- NCID 287 `ClientSpellBreak` = `[i32 caster][s16 spell][u8 reason=0]`
  (CCharKfm/ISpell::NotifyBreak + CNE_CZ_ZoneServerSpellBreak).
- Refus de cast pendant cast/GCD = NCID 279 `ClientSpellCastOK`
  flag u8=0 (LAB_084ef550). Notre silence = barre figée côté client.
- NCID 408 = interruption d'ITEM canalisé (UseItemTimeBreak), pas
  de spell.
- CCharKfm::Integration @0x80e09e0 : pools single (map kind→pool,
  kind 28 arc → {1421}=bow1) + pools DUAL `this+0x38` (3×3 classes
  blade/axe/autre) : C[0]={2021,2022} épée×épée, C[4]={2023,2024},
  C[8]={1921,1922}, mixtes={2025,2026}. Désarmé={1121,1122,2121}.
- S_Mission `MonsterTreasureGot mobId rate itemId count` → mission
  `ScriptEntry(type=3)` via `MobDieReport` ; bug résiduel : gate
  `progress>=count` basé sur state stale.

## Points d'édition
- [x] 287 format 7B + reason 0, cancel CTS, clear CastingSpell,
      finally sur CastSequenceAsync.
- [x] 279 flag=0 sur refus cast-en-cours/GCD.
- [x] MotionParser : pools single + matrice dual + PlayerAttackKfIds
      (mainKind, offKind).
- [x] ResolveWeaponAsync : main (loc7/19), off (loc8), 2H (9/35) ;
      bouclier exclu (kind 34 non-weapon).
- [x] Invalidation cache anim sur NCID 8, 277, clic droit équipe
      (callback ZoneServices.InvalidateWeaponAnim).
- [x] Portails : trigger natif tracé (15 → CheckTrigger → ScriptEntry
      type 8 → Event_Transport mission→world→zone) + TestIn 6.0f +
      anti-spam 1637 + exécution script complet.
- [x] Quête collect : pipeline MonsterTreasureGot vérifié (roll
      mission-side natif, gate cap inventaire OK) + log du catch.
- [x] Profil stats : formules natives S_Level (cols 5-9 =
      Str/Dex/Int/Spr/Luk) × S_Class (cols 15-21 = hp/mp/str/dex/int/
      spr/luk %) — placeholders 296 remplacés.
- [x] SceneParser : liste complète des commandes [event] groupées
      `#:conds:#:actions:` par paire indépendante.
- [x] Moteur d'événements scène zone : RebornMonster, MonsterAction
      (495+268), BornNpc, KillNpc, GiveBuff(299)/DeleteBuff(300),
      TriggerEvent, EventState, Variable, Transport, ScreenMessage,
      conditions If_*.
- [x] LiveMob.SpawnId + spawn/despawn dynamiques par groupe
      (reborn_monsters col0) + trigger mort de groupe.
- [x] QuestEngine : RewardItem/ChooseItem(chooseIdx op=2)/ClassPoint/
      Reputation(prestige)/SpecialCoin/Buff/TriggerGroup + scaling
      lv_offset natif (col3 = niveau mission, params 332-340).
- [x] Cmds zone : eden-give-item (existant), eden-class-point
      (→ GrantClassPointAsync), eden-buff (→ 299).
- [x] due_date : formule native unit×count + arming ItemUse + 461.
- [ ] Build + redeploy + commit (build OK 0/0 — reste test live).
- [ ] Vérif live client : quêtes anim (NPC qui bougent/parlent),
      drops quête, portails, montures, anims dual, stats profil.

## Preuves natives — portails & événements de scène (session 2)
- `CNE_CZ_ZoneServerCheckNodeExit::Execute` : TestIn radius **6.0f fixe**,
  anti-spam `char+0x7a4` = `(nodeId<<16)|eventKey` → rejet texte 0x665
  (1637). Zone finit par `CSceneEventCmds::ExecuteCommand` → log
  `CheckTrigger ServerID NodeEventID SelectID` → mission
  `TC_CheckTrigger` → `ScriptEntry(type 8)` → `Event_Transport` →
  world `TransportNode ChID=%d NodeEventID=%d`. Notre zone exécute le
  script directement (équivalent fonctionnel) — il faut le script
  COMPLET, pas juste le transport.
- `[event]` verbes observés (21603 RebornMonster!) : If_Level,
  If_EventState, If_Monster, If_RebornMonster, If_Variable,
  Transport, ScreenMessage, RebornMonster, MonsterAction, BornNpc,
  KillNpc, KillMonster, GiveBuff, DeleteBuff, TriggerEvent, Variable,
  EventState, SetAchievement(NPC).
- `Event_MonsterAction` → `CAI::SetEventAction` broadcast
  **495 ClientPlayMonsterAction [i32 handle][s16 actionId]** +
  `MonsterSay` → **268 ClientSay [u32 handle][str][u32 len][bin]**.
- `reborn_monsters` col0 = spawnId = "group" des Event_* ; le groupe
  vit en `monster+0x360`.
- S_Mission col25 = **TriggerGroup** (426 → missions 1295-1298) ;
  `TriggerGroupMission` auto-accepte les missions du groupe
  (FMSCheckApproval==6).
- GainReward natif : gold/exp/class_point/family_exp +
  `clone_reward_item id n` + buffs + prestige + special coin, envoyés
  zone en `CNC_ZMis_ZoneServerTransitionCommands`. Claim op=2 porte
  le **chooseIdx** (u16 arg du 87, 0xffff = aucun).
- Durée items : `due_date = now + col26(unit)×col27(count)` —
  unit {1,4}=min {2,5}=h {3,6}=jour ; 461 ClientItemRent
  `[s16 contType][s16 contIdx][s16 loc][u32 due]`.

## Points d'édition (suite)
- [x] LevelRow mapping S_Level (Str col5, Dex col6, Int col7,
      Spr col8, Luk col9) + ClassPct [2]=hp [3]=mp [4]=str [5]=dex
      [6]=int [7]=spr [8]=luk + ComputeVitals partagé + params.
- [x] due_date : formule native unit×count + arming ItemUse + 461.
- [x] Portail : TestIn 6.0f fixe + anti-spam 1637.
- [ ] SceneParser : liste complète des commandes [event].
- [ ] Moteur d'événements scène zone (tous les verbes + state vars).
- [ ] HandleNodeExitAsync → exécute le script complet.
- [ ] LiveMob.SpawnId + spawn/despawn dynamiques par groupe.
- [ ] QuestEngine : RewardItem/ChooseItem/ClassPoint/Reputation/
      SpecialCoin/Buff/TriggerGroup + chooseIdx du 87.
- [ ] Cmds zone : eden-reward-item/class-point/buff/special-coin.
- [ ] Build + redeploy + commit.

## Lot 2 — event-areas au mouvement + dialogues NPC dynamiques (fait)

- [x] `ParseDialogOption` partagé (GameDataParsers) : grammaire
      `#:cmd#:conds:#:actions:#:failBody` (InsertNPCTalkDB natif) —
      failBody = texte de refus (preuve T_Dialogue 5012).
- [x] `NpcService` record (conds+failBody) ; options `#:Service`
      incluses dans le 56 mission (kind 3) — perdues avant.
- [x] `TalkCondsPassAsync` : If_CharLevel/InventoryItem/InventorySlot
      (64−used sac0, désasm 0x80dbc70)/FMS/Gender/Class/Gold/Reputation,
      inconnues → pass + log.
- [x] `RunDialogActionsAsync` : table TC_* native → zone (setra, tpa,
      eden-give/take-item, gain_gold, eden-buff/debuff, service,
      eden-scene-event) + Reputation→prestige interne.
- [x] `CheckAreaEntryAsync` (grille spatiale de MoveSelf natif) :
      entrée REVIVE → revive_area_id=node<<16|clé + 531 + persist ;
      NORMAL → last_normal_area_id + persist. Tir à l'entrée
      (st.InsideAreas, reset migration).
- [x] `ReviveLinks`/`NormalLinks` corrigés : map[aireTraversée]=clé
      (map[col2]=col1 désasm — sens inversé avant).
- [x] Born-area spawn au login (event-area BORN avant [position]).
- [x] Verbes zone : setra (SetReviveAreaID+531+persist), tpa
      (pos-group + last_normal), gain_gold, eden-scene-event,
      eden-debuff, service numérique, ClearTriggerAreaID, table
      `shop <verbe>` complète type→UI (TC_Service @0x80ef980).
- [x] Build 0/0.
