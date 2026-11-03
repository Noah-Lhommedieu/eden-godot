# Lot : TeamOperation natif (loot/roll/raid) — 2026-10-20

## Contexte & preuve (`CNE_CZ_ZoneServerTeamOperation::Execute` zone/decompiled.c:532135)
Wire : `[s16 op][s16 param1][s16 param2]` (this+0x12/0x14/0x16).

Ops **leader-gated** (après `IsTeamLeader`) :
- `0` → `CForceTeam::ChageLootRule(param1)` : règles natives 1=Free,
  2=Random, 3=Taunt-Exclusive (textes client 208/207/2156) ; valeurs
  hors 1-3 ignorées (le textcmd part quand même chez nous : no-op).
- `1` → `ChangeToRaid` : refuse si un membre est en node-type 5
  (texte 1579) ; succès → cap=10, texte 1578 aux membres, update team.
- `3` → `ChageRollRule(param1)` : seuil qualité 0=White/1=Green/2=Blue
  (textes 2887-2889).
- `6` → `CreateRaid25Team` : cap=25.
- `7` → R25 instance (team+0x64==3 requis) — **exclu** (sous-système
  tower/instance inexistant).
- `4` → TeamCheck tower (DBA log) — **exclu**.

Ops **membre** (avant le check leader) : 5=TeamCheckAnswer, 8=EnterR25Instance — **exclus** (instances).

Notification native : `WorldTeamUpdateTeamInfo` (textcmd mission) →
`RecvWorldTeamInfo` zone → broadcast **381 `ClientTeamInfo`
`[s16 type][s16 value]`** aux membres (type 9=loot rule, 10=roll rule,
champs team locaux +0x98/+0x9a).

## Plan
- [x] `TeamEntity` : `LootRuleValue` (natif 1-3, défaut 1), `RollRule`
      (0-2), `RaidCap` + `MaxSize` raid, enum `TauntExclusive`
- [x] `TeamService` : `ChangeLootRule/ChangeRollRule/ConvertToRaid(cap)`
      leader-gated, plage native 1-3
- [x] Handler : switch sur op → service + 381 `[type][valeur]` aux
      membres (9=loot, 10=roll) + texte 1578 + broadcast complet raid
- [x] `BroadcastTeamInfo` : `TeamMode` ← `team.LootRuleValue` (valeur
      native 1-3, plus l'enum interne 0-4 qui affichait les mauvais
      textes côté client)
- [x] Ops 4/5/7/8 : log documenté (TeamCheck/R25 → instances, exclu)
- [x] Tests service (10/10 TeamServiceTests verts) + build 0/0 +
      suite 307/316 (9 échecs baseline inchangés) + commit
