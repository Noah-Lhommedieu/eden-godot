# Plan — système de statuts EDENFR (Guérison, portail gratuit <80, audit opcodes)

## Symptômes
- « Guérison » (spell 50028 → enchant 52028, cmd 1824 dispel) listée comme statut.
- État « portail gratuit » natif (enchant 55869, ≤ lvl 80) absent de la liste.
- Commandes 2047/2039/2033/2034 classées « instantanées » → les enchants qui
  ne portent qu'elles (ex. 55869 = `2047|1` pur) ne créent jamais de CEnchant.
- c_itemmallenchant (62 cellules, header |V.2|60|) non normalisé → commandes
  lues décalées (2047 lu en raw[8] alors que GetUseCommands lit [9]).

## Preuves natives (zone/decompiled.c)
- `CEffectFactory::EffectToLife` @0x08060ec0 : persistant si durée != -1 ET
  (effet+0x12c != 0 OU durée == -9). -9 = persistant sans icône.
- `CCharacter::OnMigrationDone` @0x0843fbxx : `level <= 0x50` →
  `EffectToLife(this,this,-0x25c3,-2,0x2000,true,0,1,0,0)` (effectId u16 = 55869).
- `CCharacter::SetLevel` : `param_1 > 0x50` → RemoveEnchantByID(-0x25c3).
- Mission/portal : enchants actifs -0x25c3/-0x25c2 (55869/55870) = TP gratuit.
- 299 corps 23B, 300 corps 15B ; hidden (-9/IsHideIcon) → ni 299 ni 300.

## Éditions
1. `EnchantParser.NormalizeRow` : + `headerCols==60 && len==62`
   (c_itemmallenchant FR — recalage commandes sur les blocs 9+7k).
2. `ZoneEntryFlow.Status.cs` : retirer 2033/2034/2039/2047 de
   `InstantOnlyCommands` ; consts 55869/55870/80 ; `SyncBeginnerPortalStateAsync`.
3. `ZoneEntryFlow.Lifecycle.cs` : appel sync après ApplyDevMoveSpeedBuffAsync
   (point OnMigrationDone natif).
4. `ZoneEntryFlow.Combat.Rewards.cs` : sync après `st.Level = newLevel` si gained>0
   (couvre level-up XP + //level admin).
5. `ZoneEntryFlow.PortalStone.cs` : gratuit si Privilege>0 OU 55869/55870 actif.
6. `ZoneEntryFlow.Mission.cs` : Pms16 mid=0 state=1 aussi si level<=80.
7. HeadlessBot : mode `enchant-test` (binds 299/300, cast 50028, self_use_effect,
   check 55869 à l'entrée).
8. Tests Eden.Tests : NormalizeRow itemmall, GetUseCommands(55869)=2047,
   instant-only 52028 vs persistant 55869.
9. Doc `docs/knowledge/definitions-status.md` + note protocol-map (315).

## Hors scope
SpellFx, cast targeting, certificats, montures, archives. NCID 315/261/443/
467/550 : audit documenté, implémentation seulement si preuve.
