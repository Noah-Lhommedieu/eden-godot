# Lot : formules de combat natives (CCharFormula)

## Objectif
Remplacer les 6 sites de dégâts hardcodés de `ZoneEntryFlow.Combat.cs`
par la transcription fidèle de `lapis::CCharFormula` (désasm
`re/ghidra/zone/decompiled.c`), alimentée par S_Level/S_Class/S_Item/
S_Monster/S_Spell/S_SpellReference/S_Parameter.

## Preuves natives (déjà décodées, désasm zone)

### Pipeline mêlée joueur → cible (`CalculatePhysicalDamage` @0x83fd8c0)
- `adjust = atkLvl^1.6 + 30`, `offset = clamp(atkLvl − defLvl, −10, 10)`
- variance arme : col47 (`addattr+0x44`), roll = `rand mod (var×10000)
  × 1e-4 ×2` → U[0,2var) ; dual wield : `offVar×0.3 + mainVar`
- `phys = MeleeDamage × 0.01 × (roll + 100 − var)`
- `defTerm = clamp(def.pdef × para326(3) / adjust, 0, 70)`
- élément = arme col44 (+0xa8) ou buff 0x15c ; résist =
  `def.resists[elem-1]` clamp [−70,70] ; `local_38 = (100−res)×1e-5`
- `dmg = ROUND((2×offset + 100 − defTerm) × local_38 × phys)` ; <5 → rand%4+1

### Pipeline mêlée mob → cible (@0x83fdec0)
- offset : type normal `clamp(Δlvl×5, 0, 500)` ; elf 0x50b `clamp(Δlvl,±10)`
- atk = `PhysicalAttack` = `ROUND((100+buff)×col14×0.01)+flat`
- variance = col15, même roll ; résist via template+0x70 (col13)
- même combinaison finale, <5 → rand%4+1

### MeleeDamage (joueur) @0x83fa430
- `strTerm = clamp(ROUND(strTot×strPct/100)+flat,0,9999)` (strTot =
  eq+base S_Level col5, strPct = S_Class col17)
- `melee = flat + (100+buff)×(strTerm×para305(5) + equipPAtk(+0x3a8 Σ
  col45) + weaponTerm)/100`, clamp 999999
- weaponTerm : seule arme = `patk×(100+mastery)/100` ; paire même kind =
  `(off×para306(0.3)+main)×(100+mastMain)/100` ; kinds différents =
  `comb×(100+mastOff+mastMain)/100` (mastery = table enchant = 0)

### MagicDamage (joueur) @0x83f97a0 — miroir avec intTerm×para307(5),
`equipMAtk(+0x3ac Σ col46)`, weapon matk col46 × mastery2 (=0)

### Ratings
- hit% : `((equipHit + wpnHits + flat + lckTerm×para315(1))×(1+buff))/
  (lvl×0.4+1) + para316(1))×100`, clamp 30000
- dodge% : idem agiTerm×para317(1) + para318(5), clamp 10000
- crit% : `(equipCrit + wpnCritMast + flat + lckTerm×para309(0.8))/(…)
  + para310(3))×100`, clamp 10000 ; wpnCritMast = crit×mastTbl = 0
- critDmg% : `para311(150) + flat + equipCritDmg`, clamp [0,300]
- pdef : `(equipDef + levelDef(col4) + armorEnh)×(100+buff)/100 + flat`
- mob hit% = `((col19+flat)(1+pct))/(lvl×0.4+1)+para316)×100` ≤30000
- mob dodge% → col20, para318, ≤10000 ; crit% → col21 para310 ≤10000
- mob critDmg = `para311 + flat + col22` ≤300 ; def = col18

### Rolls d'attaque (caller @0x83c6xxx)
- hit : `rate = max(10, para325(100) + (atkHit−defDodge)/100)` ;
  `rand%100+1 ≤ rate` → hit, sinon **miss = type 8**
- crit : `rate = clamp(atkCrit + beCrit×100, 0,10000)` ;
  `rand%10000+1 ≤ rate` → **crit = type 2**, dmg × critDmg%/100

### Dégâts de sorts
- phys skill @0x83fd470 : `ROUND((param5 + power×refPhys×0.01)×1e-7
  ×(roll+100−var)×(100+2offset−defTerm327)×(100−res))` ; <6 → rand%4+1
- magic skill @0x83fd190 : `ROUND((2offset+100)×1e-5×(matk+power×
  refMag×0.01)×0.01×(roll−var+100)×(100−res))` ; <5 → rand%4+1
- auto magique @0x83fd060 : `matk×ROUND(100−mdef×para38(0.5)/(lvl+9))/100`
  ; <11 → rand%8+2
- power = S_Spell col32, variance = col34, élément = col33 ;
  refPhys/refMag = S_SpellReference col2/col3 du niveau du sort
- split phys/mag : élément ≥2 → magique (flag natif 0x68), sinon physique
- résists : `SResistAttribute::GetResist(elem)` = `resists[elem−1]`,
  elem∈[1,9] (désasm @0x83fe6c0)

## Points d'édition
- [x] MonsterParser : mapping combat verrouillé (atk14/var15/range16/
      spd17/def18/hit19/dodge20/crit21-24/resists25-33/elem13)
- [x] ItemParser : patk45/matk46/var47/range48/spd49/def50/hit51/
      dodge52/crit53/critdmg54/resists63-71/socket72/cube75/elem44
- [x] SpellParser : power32/elem33/var34 + SpellReference col2/3
- [x] `Eden.Zone/Combat/NativeCombatFormulas.cs` — transcription des
      formules (entrées : snapshots + ParameterSet, Random injecté)
- [x] `Eden.Zone/Combat/CombatantSnapshot.cs` — profil combat joueur
      (`FromPlayer` factory) et monstre (`FromMonster`)
- [x] `EquipmentStatService` : agréger hit/dodge/crit/critdmg/mcrit/
      mcritdmg/resists/atkspeed + ItemDefs par slot d'arme dans
      `ResolveWeaponAsync` → `EntryState.Combat`
- [x] `ZoneLiveWorld.LiveMob` : `Def` retenu aux 3 chemins de spawn
- [x] `ResolveWeaponAsync` : ItemDefs résolues + agrégat + snapshot
- [x] `ZoneEntryFlow.Combat.cs` : les 6 sites remplacés
      (mêlée joueur, PvP, riposte mob, sort mob, sort joueur, AoE)
- [x] Rolls hit/crit (miss → 272 type8 dmg0, crit → type2)
- [x] `ProfileStatEmitter` : 296 = valeurs natives (MeleeDamage/
      MagicDamage/PhysicalDefence/ratings/résists ordre 8,9,6,4,5,7,1,2,3
      /MCriLv/AttackSpeedRate/DefenceReduce/CriDmgReduce/Movement 9.425)
      + `ComputeVitals` natif ((eq+base)×pct + pdef/para562)
- [x] Tests déterministes (Random seedé) : parsers + formules joueur/mob/
      skills/clamps — 23/23 verts (`NativeCombatFormulaTests.cs`)
- [x] Build + suite complète : 0 warning/erreur, 359/368 — les 9 échecs
      restants = baseline pré-existante (Login serialization,
      PacketDispatcher, LoopMessage, SceneParser, ItemParser data-drift
      col14=65 vs test 44 — inchangé par ce lot)

## Verdict tests (session finale)

- Dispatch spell : quirk natif confirmé — type 4 + élément 1 → flag
  0x44 → HitPhysicalSkill (bit4 gagne sur restore) ; restore réel =
  type 4/5 + élément ∈[4,10] (revives : cols33 = 7/10 dans S_Spell).
- Plancher crit natif : critLv=0 → rate = para310(3)×100 = 300 → ~3 %
  toujours possibles (pas de "zéro crit").
- Plancher hit natif : rate ≥ 10% via `max(10, para325 + …)`.
- `PhysicalDefence(joueur)` = EquipDef(col50) + LevelDef(S_Level col4)
  — test : 0+359 = 359, defTerm = 359×3/395.84 = 2.72 → dmg 149.
- [x] Commit `09a1f30`

## Raffinements de la session (décodage complet terminé)

- `SSpellData::Init` @0x84eecf0 : le flag mot `+0x94` est calculé depuis
  `type` (+0xc = col17) et `element` (+0xbe = col33) :
  type{6,7}→0xc, type{4,5}→0x44, type{2,3}→0x24, sinon→4 ; puis
  element==1 → tel quel, element∈[4,9] → uVar4, sinon → uVar3.
  Dispatch (@0x84ef9xx) : `flags&0x68 && type∉{4,5}` → HitMagicSkill ;
  `flags&4` → HitPhysicalSkill ; type{4,5} → HitRestoreSkill.
- `CalculatePhysicalHitRate` @0x83f84d0 : `max(10, para325 +
  (atkHit−defDodge)/100 + charExt)`, `rand%100 < rate` → hit.
- `CalculatePhysicalCriticalDamage` @0x83f7780 :
  `pct = PCritDmg(atk) + wpnMastery(0) + defBeCritDmg(+0x98) −
  round(defPdef/(atkLvl^1.6+30))`, clamp [100,300], `dmg×pct/100`.
- Crit magique : `MagicCriticalRate(char)` = même squelette lck×para312
  (1) + equipMCrit(char+0x3c4 = Σ col55) + wpnMCrit×mast(0), base
  para313(3) ; `MagicCriticalDamage` = para314(150)+kinddata+0x108+
  equipMCritDmg(+0x3c6 = Σ col56) clamp[0,300] ;
  `CalculateMagicCriticalRate` = atkRate + defBeMCrit(+0x96)×100 ;
  `CalculateMagicCriticalDamage` = dmg×(atkRate+defBe+spell)clamp[100,300]/100.
- Item cols : col55→attr+0x40 (crit mag), col56→attr+0x42 (critdmg mag).
- `CWeapon::TwoHand+0x50` = hit désarmé (0 par défaut).
- LevelRow : col4 = NaturalDef (leveldata+0x14) — à ajouter au parser.
- RefreshEQAttr : `+0x3a8/+0x3ac/+0x3bc` = Σ col45/46/51 des items à
  addattr (armes incluses → double comptage natif avec le terme arme
  de MeleeDamage/PhysicalHitRate — reproduire, ne pas corriger).
- Ref coef = S_SpellReference col2/col3 en % direct (le décodage
  `hi×65536+lo` reforme l'u32 brut).
- Mob spell → HitMagicSkill : `matk(col14) + power×refMag/100`, résist
  spell element (col33).
