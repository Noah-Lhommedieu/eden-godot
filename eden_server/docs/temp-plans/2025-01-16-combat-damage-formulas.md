# Lot — Dégâts de combat natifs (CCharFormula)

## Symptôme
`ZoneEntryFlow.Combat.cs` utilise des dégâts plats sans stats :
`60+rand(40)` (joueur→mob, PvP), `AttackPower/3+rand(12)` (mob→joueur),
`sdef.Power+AttackPower/4` (sort mob), `80+rand(60)` (sort joueur).
Aucune prise en compte de STR/équipement/DEF/niveau/crit/esquive.

## Preuves natives (désasm `re/ghidra/zone/decompiled.c` + .rodata)

- `PhysicalAttack(CMonster)` @0x83f8910 :
  `ROUND((100+kinddata+0xe0)×template+0x4c×0.01)+kinddata+0xdc`
  → template+0x4c = **S_Monster col21** (attaque), buffs=0.
- `MeleeDamage(CCharacter)` @0x83fa4c0 :
  `flat(+0xdc) + ROUND((100+buff) × (strLv×param305 + equipPAtk(char+0x3a8)
  + weaponAtk) × 0.01)`, `strLv = ROUND(strTotal×(str%+buff)×0.01)+flat`
  clamp [0,9999], `weaponAtk = attr+0x3c×0.01×(100+typeBonus)`.
  → attr+0x3c = itemdata+0xc4 = **S_Item col53** ; dual-wield
  off-hand × param306(0.3).
- `CalculatePhysicalDamage` @0x83fd870 (char) / 0x83fdcc0 (mob) — log :
  `Physical × (100 − Para326×DEF/Adjust + 2×Offset) × (100−Resist)/100000`.
  - `Adjust = atkLvl^1.6 + 30` (doubles .rodata 0x888b6d8/0x888b6e0)
  - Offset joueur = clamp(atkLvl−defLvl, −10, 10) ; mob =
    clamp((mobLvl−defLvl)×5, 0, 500)
  - defFactor = clamp(DEF×para326(3)/Adjust, 0, 70)
  - resist clampé [−70,70] → facteur (100−r)×1e-5 ; appliqué que si
    l'attaque porte un élément (kinddata+0x15c ou template+0x70/sub
    weapon flag) — sinon facteur 0.001 (=resist 0).
  - mult = 100 − var + rand[0,2var) ; var = attr+0x44 = **S_Item col57**
    (vide→0) / template+0x44 = **S_Monster col18**.
  - `dmg < 5 → rand%4+1`.
- `CalculatePhysicalHitRate` @0x83f84d0 :
  `(atkHit−defDodge)/100 + para325(100)`, plancher 10 ;
  test natif `rand%100+1 ≤ rate` (Attack @0x83c63xx) — sinon type 8 = miss.
- Ratings (+0xf0/+0xf4/+0xf8 vtables) :
  `((base + equipStat + lvStat×P) × (1+buff) / (lvl×0.4+1) + para) × 100`
  - joueur : hit (base+0x3bc, weapon col65, hitLv×para315, +para316,
    cap 30000) ; dodge (base+0x3be, dodgeLv=agi×dex%×para317, +para318,
    cap 10000) ; crit (base+0x3c0, weapon crit col67×type-buff,
    criLv=luk×luk%×para309, +para310, cap 10000)
  - `lvStat = ROUND(lukTotal×(luk%+buff)/100)+flat` clamp 9999
    (LUK pour hit/crit, AGI pour dodge — champs char+0x3a4/+0x364,
    +0x39c/+0x35c)
  - mob : hit = template+0x52=**col24** (+para316, cap 30000),
    dodge = +0x50=**col23** (+para318, cap 10000), crit = s16@+0x54
    (chevauche cols26-27, ~0) (+para310, cap 10000).
- `CalculatePhysicalCriticalRate` @0x83f7510 :
  `atkCrit + defender(kinddata+0x94)×100` clamp [0,10000] — beCrit=0.
- `CalculatePhysicalCriticalDamage` @0x83f76c0 :
  `dmg × clamp(beCritRes(+0x98) − ROUND(DEF/Adjust) + critDmg, 100, 300)/100`
  ; critDmg joueur = ROUND(para311=150) + char+0x3c2 (equip, ~0) ;
  mob = 150 + template+0x58 (**col30**) ; type-crit weapon buff = 0.
- `PhysicalDefence(CCharacter)` @0x83f55c0 :
  `kinddata+0xec + ROUND((100+buff)×(baseDef+0x3b8 + eqDef+0x370
  + armorEnh)×0.01)` min 1 — sans buffs = baseDef + eqDef.
  Mob : `ROUND(col34 × (100+buff)/100) + flat`.
- `MagicDamage(CMonster)` @0x83f8860 : `(100+kinddata+0xe8)×0.01×col21
  + kinddata+0xe4`.
- `CalculateMagicalDamage` @0x83fd060 :
  `matk × ROUND(100 − defMDef×para38(0.5)/(atkLvl+9))/100` ;
  `CCharacter::GetMagicDefence` = **0** ; mob mDef = template+0x60 =
  **col37** (×kinddata+0xf8=0 → col37).
- `CCharacter::GetMagicDefence` @0x8452070 : retourne 0.
- Type du 272 : 1 normal, 2 crit, 8 miss (Attack dispatch @0x83c63xx).

## Paramètres S_Parameter (valeurs vérifiées)
305=5, 306=0.3, 309=0.8, 310=3, 311=150, 315=1, 316=1, 317=1,
318=5, 325=100, 326=3, 38=0.5.

## Points d'édition
- `MonsterParser` : `AttackPower` → col21 ; +`AttackVariance` (col18),
  `Dodge` (col23), `Hit` (col24), `PhysicalDef` (col34),
  `PhysicalResist` (Resistances[0]), `MagicDef` (col37),
  `CritDmgBonus` (col30), `ElementAttack` (col40 bas ≠0).
- `ItemParser` : +`WeaponAttack` (col53), `AttackVariance` (col57),
  `WeaponHit` (col65).
- `ParameterParser` : +constantes combat (305/306/309-311/315-318/325/
  326/38).
- NOUVEAU `Eden.Core/Game/Services/CombatFormulaService.cs` :
  snapshots attaquant/défenseur + `MeleeOutcome` (miss/crit/dmg),
  `MagicalDamage`, `SpellDamage` — pur, testable.
- `ZoneEntryFlow.Combat.cs` : `BuildPlayerCombatAsync` (eq items +
  base stats + pcts classe + arme) → remplace les 4 sites de dégâts
  plats ; miss → 272 type 8, crit → type 2.
- `tests/Eden.Tests/CombatFormulaTests.cs` : vecteurs natifs.

## Vérifs
- dotnet build 0 erreur ; tests ciblés ; suite complète (baseline 9).
- Wire inchangé (mêmes NCIDs, type 8/2 natifs sur miss/crit).
