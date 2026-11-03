# 27 - Système de Monstres et Boss

> **Status:** 🟡 En cours — données + spawn/kill livrés, IA à faire  
> **Priorité:** CRITIQUE  
> **Complexité:** Élevée

---

## 1. Description

12 896 monstres : spawn, stats, résistances, sorts associés, mort, drops.

## 2. Symboles lapis::

- `CMonster`, `CMonsterDBO`, `CMonsterTemplate`
- `CMonsterGroup`, `CRebornMonster` (world bosses)
- IA : `CAIFSM*` (Idle, Hunt, Roam, Combat — machine à états)
- Réseau : `CNC_ZM_MonsterServerSpawn/Dead/Regen`, `CNC_CZ_ClientDisplayMonster`

## 3. Données : S_Monster.ini (`|V.6|62|`, 12 896 monstres)

| Col | Contenu |
|-----|---------|
| 0 | Monster ID (20001...) |
| 1 | Code modèle (M312040) |
| 2 | Nom (Big5) |
| 3 | Famille |
| 4 | Niveau |
| 8-9 | HP / MP (boss ≥ 1M) |
| 13 | Type d'attaque |
| 14 | Grade/type (1/5/9) — aussi `AttackMotionId` |
| 15-24 | Stats de combat (attaque 13410, défense, crit...) |
| **18** | **EXP** — constante par palier de niveau (lv1→93/47 selon tier, lv5→129, lv10→209, lv20→452, lv30→783) |
| 25-33 | **9 résistances élémentaires** |
| 34-38 | Crit/vitesse/portée |
| 40-44 | Spell ids (déclenchés à la mort/drops) |
| 59-61 | Flags |

## 4. Implémentation C#

- `Eden.Core/Data/Parsers/MonsterParser.cs` — `MonsterDef` (IsBoss si HP≥1M)
- `Eden.Core/Game/Monsters/MonsterEntity.cs` — instance vivante
- `Eden.Core/Game/Services/MonsterService.cs`
  - `Spawn(monsterId, mapId, x, y, z)`, `Kill`, `Despawn`, `Get`
  - `GetAliveOnMap(mapId)`, `ApplyDamage` (retourne les spells de mort)

## 5. Tests

`MonsterParserTests` (4 tests) : 12 896 chargés, Rose Knight (20001, lvl 80,
202 589 HP), tourelle 9M HP = boss, 9 résistances à 100.

## 5bis. Mort d'un mob — séquence native livrée (18 Oct)

`FinishMobDeathAsync` (ZoneEntryFlow.Combat.cs) :
1. 305 `ClientLifeDie` `[u32 victime][u32 tueur][u8]` ;
2. attrs/enchants + **exp** (`GrantKillExpAsync`) : `ProgressionService`
   + **600** `ClientUpdateAttrNewExp` `[i32 char][i64 exp]`, et **284**
   `ClientLevelUp` `[u32 char][u8][s16 lvl][i64 maxHp][i32 maxMp]` au
   level-up — modèle `CCharacter::GainExp` (zone/decompiled.c) ;
3. loot (`DistributeLootAsync`) : S_DropItem + S_ClassDropItem + or
   S_Monster → insert DB + **274** `ClientPutItemIntoInventory` ;
4. 302 sortie de combat + 444 si plus d'aggro, cadavre 269 à +5 s,
   respawn programmé.

## 6. TODO / À reverser

- IA `CAIFSM*` : Idle/Hunt/Roam/Combat — décompiler les états
- Aggro/threat (taunt) — `CMonster` threat table
- `S_MonsterStep.ini` (375 entrées : vitesses de déplacement par modèle)
- `S_DropItem.ini` (5 049 tables de loot détaillées)
- `CRebornMonster` : respawn des world bosses
- Spawn points par map (`S_Node.ini` bounds + données de spawn)
