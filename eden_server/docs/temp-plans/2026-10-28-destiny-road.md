# Plan — Destiny Road / Destiny Dice (chemin d'éveil)

## Sources natives (preuves)

| Pièce | Preuve |
|---|---|
| `CNE_CZ_ZoneServerDestinyDice::Execute` (203) | zone @ 0x08246480 — [s16 op][s16 v1][s16 v2] ; op ∉{2,3} exige scene type ∈{1,3,4} sinon msg 0x184e + 590 [code=op] |
| `CNE_CZ_ZoneServerQueryDestinyRoad::Execute` (204) | n'émet 534 QUE si road_id≠0 |
| `SDestinyRoad::Serialize` (534) | zone @ 0x0863b750 — [s16 road_id][s16 road_type][s16 step][s16 rolls][i32 dp][s16 move][s16 func_state][u8 flag][u16 n][n×s16 slots] |
| `CNC_CZ_ClientUpdateDestinyRoad` (589) | [s16 type][s16 v1][s16 v2] — `NotifyUpdateDestinyRoad` @ 0x08104710 envoie aussi DBA `"Update DestinyRoad type= %hd value1= %hd value2= %hd"` |
| `CNC_CZ_ClientPlayerDestinyRoadEvent` (535) | [u16 event_id] avant résolution |
| `CNC_CZ_ClientDestinyDiceFailed` (590) | [s16 code] — 1 (op1), 4 (op4 versus), op value (scene) |
| `CPlayerFactory::DestinyRoadAction` | zone @ 0x0810d610 — ops 0-4 ci-dessous |
| `CPlayerFactory::DestinyDiceEvent` | zone @ 0x0810d190 — types 1-7 |
| `CCharacter::CreateDestinyRoad[Ev]` | zone @ 0x084218b0 / 0x084222e0 — road par tier `level/10` via `DestinyRoadLevelMap`, pick random, 39 slots groupes→events aléatoires, rolls=10 |
| `CCharacter::UseDestinyFunction` | zone @ 0x08418430 — func 0±1, 1×2, 2+3, 3−4 (clamp 1-6), 4=5, 5=rand1-6 |
| `CCharacter::UseDestinyRoadItem` | zone @ 0x08422520 — itemtype 24, champs def +0x138/+0x13a/+0x13c, cas 0-7 |
| `CEventDestinyVitAdd` | zone @ 0x08198050 — +1 vit / 72 s, cap = 100×param569, stop au cap, notify type 7 |
| `CMonster::GiveDp` → `CCharacter::GainDp` | zone @ 0x08491850 / 0x0840b0c0 — base param375×decline(576/577)×buff ; cap = param376×param570 ; exige road actif ; msg 0xf51 |
| Mission `TC_UpdateDestinyRoad` | mission @ 0x081ee970 → exec+0x694 : type0=step 1=rolls(dice_time) 2=dp(exp_buff i32) 3=move(dice_point) 4=func_state 5=slot[v2]=v1 6=flag 7=vit(exec+0x6b6) |
| `player_destiny_road` | SELECT/UPDATE/INSERT/DELETE natifs — 46 colonnes + 39 slots |
| `player_characters.destiny_vit` | colonne native |
| `S_DestinyRoad.ini` | `id\|tier\|backdrop\|39×group\|` — 12 routes, tiers 0-10 |
| `S_DestinyEvent.ini` | `id\|group\|nom\|desc\|str\|type\|value\|param2\|` — 221 events |
| Params | 375=25 base DP, 376=1250 coût dé, 569=10 vit (cap 1000), 570=10 DP (cap 12500), 576=17, 577=20 decline |

## Machine d'état (DestinyRoadAction)

- **op 0 créer** : road==0 && vit≥100 → create, vit−=100, start CEventDestinyVitAdd si absent, notify 7(vit)
- **op 1 lancer** : en zone; road==0→0xf0f; rolls<1||dp<1250→0xf12; monté/mort→0xf10; versus→0xe9a+590(1) ; sinon rolls−−, move=rand6+1, notify3(move)+notify1(rolls)
- **op 2 item** : container bag=0, def.type==24, qty≥1 → `UseDestinyRoadItem(def+0x138,+0x13a,+0x13c)` → consume 1
- **op 3 fonction/reroll** : flag==0 → func bit déjà posé→no-op sinon `UseDestinyFunction(v1)`, func_state|=1<<v1, notify4 ; flag==1 → clear flag, move=v1+1, notify3, notify6
- **op 4 avancer** : mêmes gates qu'op1 + move≠0 ; step+=move → DestinyDiceEvent :
  - continue : move=0, dp−=1250, notify0(step)+notify3(0)+notify2(dp)
  - fini : Reset road, `Clear DestinyRoad`, 534 vide

## Événements (DestinyDiceEvent)

1. or += value ; 2. exp += value ; 3. NP+=value & CP+=value ;
4/6. spawn `param2` monstres `value` à la pos du joueur (aggro) ;
5. effect id=value durée=param2 ticks ; 7. rolls += value (≥0).
Fin : step<0x26 && rolls>0 → continue ; step<0x26 → msg 0xf14 ;
step==0x26 → msg 0xf15 ; step>0x26 → msg 0xf13.

## Implémentation (notre archi : repo zone direct, équivalent DBA)

- [x] `Eden.Core/Data/Parsers/DestinyDataParser.cs` — S_DestinyRoad + S_DestinyEvent + maps (LevelMap, EventGroupMap)
- [x] `GameDataService` — `DestinyRoads`, `DestinyEvents`, `DestinyRoadLevelMap`, `DestinyEventGroupMap`, `GetDestinyRoad`, `GetDestinyEvent`
- [x] `Eden.Core/Database/Repositories/DestinyRoadRepository.cs` — load/upsert(UPDATE→INSERT)/delete + `destiny_vit` (player_characters)
- [x] Fix `CNC_CZ_ClientPlayerDestinyRoad` (534) → layout SDestinyRoad complet
- [x] `EntryState` — `DestinyState` miroir SDestinyRoad + `DestinyVit` + `DestinyVitRegen`
- [x] Chargement à l'entrée (`LoadDestinyAsync` dans Lifecycle après LoadWorks)
- [x] `ZoneEntryFlow.Destiny.cs` — 203/204 interceptés, ops 0-4, events, item, fonction, vit regen (ZoneLoop.Schedule 72 s), GiveDp hook kill (GiveTeamDp = même distribution que GiveTeamExp)
- [x] Item def : `DestinyFunc/DestinyParam2/DestinyParam3` (offsets +0x138/+0x13a/+0x13c — parseur natif les initialise à 0, aucun item kind 0x18 dans S_Item → 0 = fidèle)
- [x] `NativeCombatFormulas` — `DesDeclineRate` (params 576/577) + params destiny 375/376/569/570/363
- [x] `ZoneLiveWorld.SpawnHostileMob` — DestingBornMonster (aggro + owner + expiration param 363)
- [x] Tests : 534 bytes, parser ini, DesDeclineRate — 750/750

## S_Item destiny fields

Itemtype 24 (Kind 0x18) ; `UseDestinyRoadItem(item_def+0x138 s16=func, +0x13a s16=p1, +0x13c s16=p2)` — champs `ItemDef.DestinyFunc/Param2/Param3` ajoutés (non peuplés par le parser, comme le natif).

## Écarts documentés vs natif

- Le relais mission `Update DestinyRoad`/`DestinyRoad RoadID=`/`Clear DestinyRoad` (CNC_DBA_AgentDBACommand) n'est PAS relayé : la mission C# n'a pas de CExecutionerData — la zone persiste directement (`DestinyRoadRepository`), équivalent de `UpdateDestinyRoadToDB` (UPDATE→INSERT) / TC_ClearDestinyRoad (DELETE).
- `char+0x95c` (gate des ops 1/4 — « pas en état bloquant », -1 requis) non porté : champ non identifié dans nos systèmes (probablement cible/duel) → toujours autorisé, commenté dans le code.
- Durée event type 5 : `param2` ticks natifs (0.1 s) → `/10` secondes pour AddPlayerEnchantAsync.
- Despawn mob d'event : param 363 (30 s) via `ZoneLoop.Schedule` + `DespawnPet` (OwnerCharId posé = le déclencheur, CMonster+0xd0 natif).
