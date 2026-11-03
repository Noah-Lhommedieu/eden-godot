# Lot — Formules de combat natives (CCharFormula → C#)

## Symptômes
- 4 sites de dégâts hardcodés dans `ZoneEntryFlow.Combat.cs` :
  `60 + rand(40)` (mêlée joueur→mob ×2 sites), `mob.AttackPower/3` (mob→joueur),
  `sdef.Power + mob.AttackPower/4` (sort mob).
- `CombatService`/`StatsCalculationService` : formules inventées
  (`atk − def×0.5`, `agi/20+5%`, `dmg×0.9-1.1`) — rien de natif.
- `MonsterParser` : mapping de colonnes faux (attack=col21 au lieu de col14,
  resists=25-33 au lieu de… etc).
- `ProfileStatEmitter` : hit/dodge/crit émis en dur (`736`, `1200`, `tStr*8`).

## Preuves natives (decompiled.c — lignes)
- `SCharAttribute::SetData` @808946 : coefs classe `+0x338..0x348` ←
  classdata+0x38/0x3a/0x3c/0x3e/0x40 = S_Class **cols17-21 =
  str%,agi%,int%,wis%,lck%** ; +0x34/0x36 = cols15/16 = hp%/mp%.
  Base stats char+0x354..0x364 ← leveldata+0x18/0x1c/0x20/0x24/0x28
  (str,agi,int,wis,lck), +0x370 ← leveldata+0x14 (def).
- `RefreshEQBonus` @80903x : char+0x38c/390 ← attr+04/08 (hp/mp) ;
  +0x394..0x3a4 ← attr+0x0c..0x1c (str,int→wait: 0x0c str, 0x10 int,
  0x14 agi, 0x18 wis, 0x1c lck) ; +0x3b8 ← attr+0x34 (def col50 ×fortify) ;
  +0x3bc ← attr+0x38 (hit col51) ; +0x3be ← attr+0x3a (dodge col52) ;
  +0x3c0 ← attr+0x3c (crit col53) ; +0x3c2 ← attr+0x3e (critdmg col54) ;
  +0x3d4 ← attr+0x50 (block) ; +0x3d6..0x3e2 ← attr+0x52..0x5e
  (resists cols63-71) ; +0x3a8 ← addattr+0x3c sur items arme
  (col45 — RefreshEQAttr).
- `SetItemAttrData` @714548 : attr+0x20←col45 patk, +0x24←col46 matk,
  +0x28←col47 var, +0x34←col50 def, +0x38←col51 hit, +0x3a←col52 dodge,
  +0x3c←col53 crit, +0x3e←col54 critdmg, +0x50..0x5e←itemdata+0xe4..0xf4
  = **cols 63-71 résistances** (vérif : +0xf6=col72 MaxSocket ✓).
  Addattr (armes) = même attr décalée +0x1c : +0x3c=col45, +0x40=col46,
  +0x44=col47, +0x50=col50, +0x54=col51, +0x56=col52, +0x58=col53,
  +0x5a=col54. Arme element = kinddata+0x128 = itemdata+0xa8 = **col44**.
- `CMonsterQuery::Deserialize` + conversion template @726529 :
  col13→element(+0x70), **col14→attack(+0x4c)** (53641 boss/186 lv1),
  **col15→variance(+0x44)**, col16→portée max(+0x40), col17→vit.att(+0x38),
  **col18→physdef(+0x5c)**, col19→hit(+0x52), col20→dodge(+0x50),
  col21→pcrit(+0x54), col22→pcritdmg(+0x58), col23→mcrit(+0x56),
  col24→mcritdmg(+0x5a), cols25-33→resists(+0xa8+2i).
- `SSpellData` : +0xbe élément, +0xc0 = col32 puissance, +0xc4 = col34
  variance ; `SSpellReference+0x08/+0x0c` = col2/col3 (coeffs u32
  hi16×0.01+lo16).

## Formules natives (à implémenter tel quel)
```
tStat = clamp(round((eq+base)×classPct/100),0,9999)          // kfm=0
MeleeDamage  = round((100+0)×(tStr×p305 + eqPAtk(0x3a8) + weaponTerm)×0.01)
  weaponTerm = patk×(1+mastery/100)  (mastery kfm+0x304[kind]=0)
  dual: term = (offPatk×0.3 + mainPatk)×(1+masterySum/100)
BaseMagicDamage ≈ tInt×p307 ; MagicDamage = même squelette arme (matk)
PhysDef(char) = (leveldef(0x370)+eqdef(0x3b8)) clamp [0,99999]
PhysDef(mon)  = col18 clamp [0,99999] ; MagicDef = 0 partout
HitLv(char)   = ((eqHit+wHit + tLck×p315)/(lvl×0.4+1) + p316)×100  [0,30000]
DodgeLv(char) = ((eqDodge + tAgi×p317)/(lvl×0.4+1) + p318)×100     [0,10000]
CritRate(char)= ((eqCrit+wCrit + tLck×p309)/(lvl×0.4+1) + p310)×100 [0,10000]
CritDmg(char) = p311 + eqCritDmg                                    [0,300]
  monster: mêmes formes avec template fields (hit col19, dodge col20,
  pcrit col21, pcritdmg col22+param311).
HitTest  = max(10, p325 + (atkHit−defDodge)/100) ; touché si rand%100 < rate
CritTest = rand%10000 < (atkCrit + defBeCrit×100 + spellMod×100)
CritDmg  = dmg × clamp(atkCritDmg + defBeCritDmg − def/(lvl^1.6+30), 100,300)/100
Damage char→t = round(melee×0.01×(100−var+2r(var))×(100+2Δlvl−defTerm)×(100−R)×1e-5)
  var = arme col47 (dual: off×0.3+main) ; defTerm = min(70, def×p326/(lvl^1.6+30))
  Δlvl = clamp(atkLv−defLv, −10,10) ; R = resist(element) clampée [-70,70]
  (élément 0 → R=0). <5 → rand 1-4.
Damage mon→t  = idem avec attack col14, var col15.
MagicDamage normale = matk × round(100 − mdef×p38/(atkLv+9)) /100 ; <11 → 2-9
MagicSkillDamage = round((matk + power×refMagCoef×0.01)×0.01×(100−v+2r(v))
                   ×(100+2L)×(100−R)×1e-5) → SpellDamage → <5 : rand1-4
PhysSkillDamage  = round((meleeBase + power×refPhysCoef×0.01)×1e-7
                   ×(100−v+2r(v))×(100+2L−defTerm327)×(100−R)) → Spell → <6 : rand1-4
  L : char → clamp(Δlvl,-10,10) ; mon → clamp(Δlvl×5,0,500)
SpellDamage : ×(100+enhanceRate)/100 puis +enhanceFlat (0 sans enchant).
```

## Points d'édition
- [ ] `MonsterParser` : corriger le mapping (col13 element, 14 atk, 15 var,
      17 atkspeed, 18 def, 19 hit, 20 dodge, 21-24 crits, 25-33 resists,
      16 range ; MagicDef=0).
- [ ] `ItemParser` : ajouter Element(44), Variance(47), Hit(51), Dodge(52),
      Critical(53), CritDamage(54), Resists[](63-71).
- [ ] `SpellParser` : ajouter DamagePower(32), DamageVariance(34), Element(33)
      — vérifier cols 32-34 sur données.
- [ ] `SpellReferenceParser` : ajouter PhysCoef(col2)/MagicCoef(col3) (u32
      natif → double : hi16×0.01 + lo16 → pour nous 2 floats).
- [ ] `EquipmentStatService` : étendre `EquipmentStats` (Hit/Dodge/Crit/
      CritDmg/PAtk/MAtk/element/resists + armes par slot).
- [ ] `GameDataService` : exposer S_Parameter values aux formules.
- [ ] Nouveau `NativeCombatFormulas` (Eden.Core) : toutes les formules,
      RNG injectable, buffs/kfm = 0 documentés.
- [ ] `ZoneEntryFlow.Combat.cs` : remplacer les 4 sites + dégâts sort joueur.
- [ ] `ProfileStatEmitter` : hit/dodge/crit/def/p-atk/m-atk réels dans le 296.
- [ ] `CombatService`/`StatsCalculationService` : router via
      NativeCombatFormulas (supprimer les formules inventées).
- [ ] Tests : parser mapping, formules déterministes (RNG seedé), clamps,
      dual-wield ×0.3, resist, level offset.
- [ ] Build + suite complète ; commit ; MAJ master plan.

## Non-objectifs (documentés HYPOTHESIS)
- Buffs/enchants kfm (+0x8c) — tous les termes kfm = 0 tant que le système
  d'enchants n'alimente pas ces champs ; le code les expose en paramètres.
- Weapon mastery tables (kinddata+0x304/0x38c/+0x2c0) — table enchant,
  0 sans enchant ; exposée en paramètre.
- Classe spéciale 0x14 (awakened) — même code path, slots identiques.
