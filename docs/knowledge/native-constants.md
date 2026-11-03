# Constantes et formules gameplay du code natif

Statut : **verified** pour tout ce qui a un extrait de code cité (lu dans le
décompilé `re/ghidra/zone/decompiled.c` via `reindex`), **hypothesis** pour les
interprétations de champs non nommés (offsets de structures). 2026-11-03.
Binaire : `eden_server/server_us/ZoneServer` (zone, symboles `lapis::*`).
Les descriptions textuelles entre « … » proviennent des chaînes de log
embarquées dans le binaire (`CharFormula.cc`, `Character.cc`).

Complète `docs/reference/harvest/class-expertise.md` et
`expertise-stats.md` : leurs affirmations sur `RefreshJPAward` sont
**toutes confirmées** dans le décompilé (formule, 6 champs, cumul, appels).

## 1. Class Ability Bonus — `lapis::CCharacter::RefreshJPAward` (zone @0x08417c10)

Mécanique vérifiée dans le décompilé :

- `rang = (short)(min(niveau_de_classe, S_Parameter[578]) * 0.2f)` → palier
  tous les 5 niveaux de classe (lv5→1, lv90→18, lv100→20). Le cap provient de
  `CFormula::AllFormulaParas + 0x908` = `S_Parameter[578] = 100` ; le facteur
  `_DAT_0888e90c = 0.2f` (lu dans `.rodata`).
- Itère la table de classes possédées (`this+0xad8` : noeud `{classId@+0, classLv@+0x1c}`).
- Pour chaque classe : `m_pkInstance+0x1c4` (GetClassRankBuffData) →
  `std::map<uchar rang, 6 longs>` ; sélection exige `clé == rang`
  (rang 0 = classe lv<5 → aucun enchant).
- Applique `CEffectFactory::EffectToLife(Ins, char, char, enchantId, durée=-2,
  flags=0x1000, false, 0, 1, 0, 0)` — enchant permanent.
- Les ids d'enchant appliqués sont empilés dans `vector this+0xba4` et
  tous retirés en début d'appel → **rebuild complet à chaque appel** ;
  le bonus se **cumule sur toutes les classes possédées** (verified).

```c
/* zone @0x08417cbc — boucle par classe possédée */
if (*(float *)(CFormula::AllFormulaParas + 0x908) <= (float)(byte)puVar11[0x1c]) {
  iVar10 = (int)(*(float *)(CFormula::AllFormulaParas + 0x908) * _DAT_0888e90c);
} else {
  iVar10 = (int)((float)(byte)puVar11[0x1c] * _DAT_0888e90c);
}
/* ... map<rang,6 champs> : iVar10 = *(int *)(iVar4 + 0x28); */
CEffectFactory::EffectToLife(CEffectFactory::Ins,_param_1,_param_1,
                             (short)iVar10,-2,0x1000,false,0,1,0,0);
```

Appelants vérifiés (`reindex callers`) : `CPlayerFactory::AddClass`
@0x080fd070, `CCharacter::GainClassPoint` @0x0841c140, `CCharacter::Revive`
@0x0841dbd0 (après `InvokeSpellStones/Passivities/AAEnchants` +
`RefreshEQsAttr`), `CCharacter::HookCharacter` @0x0841e6a0 (login, avant
`RefreshEQsAttr`).

Table `S_ClassRankBuff.ini` (serveur ; client = `C_ClassRankBuff.ini`,
chaîne `client_game @0x00f4de18`) : `GameData::CClassRankBuffQuery::
Deserialize` zone @0x085c4430 — skip 1 champ, id classe, puis **6 longs par
palier**, paliers 1..22 (`|V.3|134|` = 2 + 22×6). Nœud map : clé rang `@+0x10`,
champs `@+0x14,0x18,0x1c,0x20,0x24,0x28` = HP, MP, ATQ, ATQ MAG, Soin,
**enchant = 6ᵉ champ** (verified : c'est `+0x28` lu par RefreshJPAward ; le
serveur ignore les 5 valeurs de stats — l'enchant les porte). Ex. ligne
Guerrier : `41||33|||45492` = HP+41/ATQ+33 → enchant 45492.

```c
/* zone @0x085c45e0 — 6 longs lus par palier uVar16 (1..22) */
*(long *)(p_Var12 + 0x28) = lVar11;  /* enchant id (6e)   */
*(long *)(p_Var12 + 0x24) = lVar10;  /* soin              */
*(long *)(p_Var12 + 0x20) = lVar9;   /* ATQ MAG           */
*(long *)(p_Var12 + 0x1c) = lVar8;   /* ATQ               */
*(long *)(p_Var12 + 0x18) = lVar7;   /* MP                */
*(long *)(p_Var12 + 0x14) = lVar6;   /* HP                */
```

Annexe dans `GainClassPoint` (zone @0x0841c140) : le niveau de classe ne peut
pas dépasser le niveau personnage (`classLv < charLv` sinon JP borné à
`seuil-1`), et tous les **10 niveaux de classe** une « License » est
distribuée (index `classLv/10-1` dans `classData+0x44`, limitée par
`S_Parameter[578]`).

## 2. Formules de combat (`lapis::CCharFormula`, `CharFormula.cc`)

### 2.1 Dégâts physiques — verified

`CalculatePhysicalDamage(CCharacter*, ILifeEntity*, bool)` zone @0x083fd8c0
(joueur) et `CalculatePhysicalDamage(CMonster*, ILifeEntity*)` @0x083fdec0
(mob). Chaîne de log auto-documentée :

```
"CalculatePhysicalDamage = Physical<%.2f> * (100 - Para<%.2f> * Defender<%d> /
Adjust<%.2f> + 2 * Offset<%.2f>) * ( 100 - Resist<%.2f> )/ 100000 = %d"
```

```
dégâts = ATK·0.01·(100 − var% + U[0,2·var%])   × (100 − 3·DEF/Adjust + 2·Offset)
         × (100 − Resist)/100000
```

- `Adjust = attaquant_lv^1.6 + 30` (`pow(lv, DAT_0888b6d8=1.6) + _DAT_0888b6e0=30`).
- `Para` = `S_Parameter[326] = 3` (coef. défense, `AllFormulaParas+0x518`) ;
  terme `3·DEF/Adjust` borné `[0, 70]`.
- `Offset` = écart de niveau : `clamp(lvA − lvD, −10, +10)` joueurs/boss ;
  `clamp((lvA − lvD)·5, 0, 500)` mobs normaux.
- `Resist` = résist. physique du défenseur (vtable +0xe8), bornée `[-70, +70]`
  (70 → facteur 0.0003, −70 → 0.0017, sinon `(100−r)·1e-5`).
- Variance ±`var%` : `+0x44` de l'arme ; deux armes → `offHand·0.3 + mainHand`
  (`_DAT_0888b688 = 0.3f`, aussi le facteur de debuff de stats §3).
- Min. `< 5` → `random()%4 + 1` ∈ [1,4] ; cap affiché 9 999 999
  (`DAT_0888b6bc = 99999.98` avant ×0.01).

### 2.2 Dégâts magiques — verified

Sort auto-attaque/base, `CalculateMagicalDamage(ILifeEntity*,ILifeEntity*)`
zone @0x083fd060 :

```
damage = matk * (100 − 0.5·mdef/(lv_atk + 9)) / 100      → min <11 → rand%8+2 ∈ [2,9]
"eMagicSkill38" = S_Parameter[38] = 0.5
```

Sort de magie, `CalculateMagicSkillDamage(ILifeEntity*,ILifeEntity*,SSpellData*,
SSpellReference*)` zone @0x083fd190 — **pas de terme de défense** :
résistance élémentaire seule, bornée `[-70,+70]` :

```
"CalculateMagicSkillDamage = skill_damage<%.2f> * (100 + Rand<%.2f>)/100 *
 (100 + 2 * level_paras<%.2f>) * (100 − resist<%.2f>) / 100000 = %d"
```

`skill_damage = (matk + power·(ref.hi·0.01 + ref.lo)·0.01)·0.01`, variance
±`spell+0xc4` % (même schéma `100 − var + U[0,2·var]`), `level_paras` = mêmes
clamps ±10/500. Min `<5` → `rand%4+1`.

### 2.3 Précision / esquive — verified

`CalculatePhysicalHitRate` zone @0x083f84d0 (id. `CalculateMagicHitRate`
@0x083f8630, alias) :

```
hit% = S_Parameter[325](=100) + (attaqueur.Hit − défenseur.Dodge)/100   [min 10]
```

Jet : `rand(100) < hit%` dans `CCharacter::Attack` @0x08416890. Raté →
`HitRateExt += 10` (`this+0xa7c`, remis à 0 au prochain toucher) — **+10%
de précision cumulé par miss contre les joueurs** (type 7).

`PhysicalHitRate(CCharacter*,bool)` zone @0x083fc900 — stat précision en 1/100 % :

```
"PhysicalHitRate(Melee) = (Luk * Para + Equip + Buff) * (1 + Buff) /
 (1 + Level / 2.5) + Para = %.2f"
hit(1/100 %) = ((EquipHit + buff + Luk·P315) · (1+buff%) / (lv·0.4+1) + P316) ·100
```

cap 30000 (300 %). `P315 = S_Parameter[315] = 1` (LUK→hit), `P316 = 1` (base).
EquipHit = `+0x3bc` + arme `+0x54` (deux armes : `off·0.3 + main`).

`PhysicalDodgeRate(CCharacter*)` zone @0x083fccd0 (id. `MagicDodgeRate`
@0x083fcf60, alias) :

```
dodge(1/100 %) = ((Dex·P317 + Equip + buff) · (1+buff%) / (lv·0.4+1) + P318) ·100
```

cap 10000. `P317 = S_Parameter[317] = 1` (AGI→dodge), `P318 = 5` (base).
`Dex` = `round((agi_base + agi_add)·(agiPctEquip + agiEquip)·0.01) + equip`
capé 9999, ×0.3 sous debuff (`this[0x3a]&2`).

### 2.4 Critique — verified

`PhysicalCriticalRate(CCharacter*)` zone @0x083fb370 /
`MagicCriticalRate(CCharacter*)` @0x083fab80 :

```
crit(1/100 %) = ((EquipCrit + buff + maîtriseArme + Luk·P) · (1+buff%)
                 / (lv·0.4+1) + base) · 100
phys : P309=0.8 (LUK), P310=3 (base)   |   mag : P312=1, P313=3
```

Puis jet (`CCharacter::Attack`) : `rand(10000) < crit%`, où
`CalculatePhysicalCriticalRate` @0x083f5f40 ajoute `attaquant.crit +
défenseur.BeCrit(+0x94)·100 + spellMod·100`, borné `[0,10000]` — le
`BeCritRate` du défenseur **augmente** la chance de crit subi.

`CalculatePhysicalCriticalDamage` zone @0x083f7780 :

```
"PhysicalCriticalDamage = damage * (sum_attacker_critical_dmg +
 defender_be_critical_dmg − defender_defence_dmg + spell_modify) / 100"
critDmg% = atkCritDmg + beCritDmg(+0x98) − round(defDEF/Adjust) + spellMod
           borné [100, 300]   →  dégâts × critDmg%/100
```

`PhysicalCriticalDamage(char)` @0x083f5520 : `P311 + equip(+0xfc) +
buff(+0x3c2)`, cap 300 ; `BaseCriticalDamage` @0x083f48a0 =
`round(S_Parameter[311]) = 150` ; `BaseMagicCriticalDamage` = `P314 = 150`.
Le terme `defDEF/Adjust` est borné `[9.425, 70]`.

### 2.5 Block / Parry — verified

`CalculateBlockDamage` @0x083fc780 : `rand()%10000 < blockRate` →
**dégâts = 0** (blocage total, flag résultat 0x10).
`CalculateParryDamage` @0x083fc840 : `rand()%10000 < parryRate` →
**dégâts /= 2** (flag 0x100). `CanBlock` @0x08409090 exige un équipement
(bouclier), `CanParry` @0x08409160 une arme ; l'un et l'autre sont neutralisés
pour la classe 0x14 dans certains états (voir §5).

## 3. Stats dérivées (agrégats personnage) — verified

Patron commun : `stat = round((base + add)·(modPct + mod)·0.01) + equipFlat`,
cap 9999, **×0.3 sous debuff** (`this[0x3a] & 2` ou `>>1 & 1`).

- **P-ATK** `MeleeDamage` @0x083fa430 : `flat(+0xdc) + (100+buff+0xe0)/100 ·
  (Str·P305 + equipAtk(+0x3a8) + armTerm)`, cap 999 999. `P305 = 5` (STR→ATK).
  `armTerm = wpnAtk(+0x3c)·(100+maîtrise+0x304)/100` ; deux armes : `off·P306
  + main`, `P306 = 0.3`. Maîtrise par type d'arme : `charStats+0x304`,
  indices type `0x11..0x21` (17 types).
- **M-ATK** `MagicDamage(char)` @0x083f97a0 : identique avec Int
  (`+0x398/+0x358`), `P307 = 5`, wpnMatk `+0x40`, maîtrise `+0x348`,
  `P308 = 0.3` off-hand, equipMatk `+0x3ac`, cap 999 999.
- **MaxHP** `MaxHP(char)` @0x083f7140 : `(Level(+0x368) + Equip(+0x38c)) ·
  (100 + classHP%(+0x34c)−100 + buff%(+0xd0)) · 0.01 + flat(+0xcc) +
  DEF/P562`, `P562 = 20` — **la défense ajoute des PV**. Min 1 (10 si
  flag 0x3a&2).
- **MaxMP** @0x083f48c0 : `(baseMP(+0x390) + addMP(+0x36c)) · (100 +
  buff%(+0xd8) + classMP%(+0x350)−100) · 0.01 + flat(+0xd4)`, cap 9 999 999.
- **RecoverHP** @0x083f5b40 : `((Lv+Equip)·(100+class+buff)/100 + buff) ·
  P301 · (100+buff)/100 + buff + P302) · rate`, `P301 = 0.05`, `P302 = 20`.
- **RecoverMP** @0x083f59e0 : idem sur MP, `P303 = 0.05`, `P304 = 40`.
- **AttackSpeedLevelRate** @0x083f3c90 : `vitAttaque / (lv·0.4 + 1)` —
  même dénominateur que hit/dodge/crit.
- **MovementRate** @0x083f3cf0 : `(min(vitesse, 3.0) − 1)·100` %
  (`DAT_087b273c = 3.0`).

## 4. Expérience et niveaux — verified + hypothesis

Tables chargées dans `CZoneServer::InitWorld` zone @0x08188450 —
`CLevelDBO::Load*` remplissent des vecteurs indexés `niveau−1`, tous tronqués
à `S_Parameter[362] = 100` entrées (**cap niveau personnage = 100**).

| Table | Source | Champ lu | Contenu |
|---|---|---|---|
| `LoadLevelExp` @0x08362320 | `S_Level.ini` | `CLevelData+4` (long long) | seuil EXP personnage, col 2 |
| `LoadClassLevelExp` @0x08362580 | `S_Level.ini` | `+0x2c` = **col 11** | seuil JP classe de base |
| `LoadClassWakenLevelExp` @0x08362460 | `S_Level.ini` | `+0x6c` = **col 27** | seuil JP classe éveillée |
| `LoadPartyExp/Jp` | `S_Level.ini` | `+0x50/+0x54` = cols 20/21 | seuils groupe |
| `LoadPVPExp/Jp` | `S_Level.ini` | `+0x58/+0x5c` = cols 22/23 | seuils PvP |

`S_Level.ini` = `|V.8|27|` (27 colonnes) ; `CLevelQuery::Deserialize`
zone @0x085fe950 lit `level(byte), exp(long long), 25 longs`. Mapping
offset→colonne : `+0xc + (col−3)·4`. Colonnes 3-10, 12-19, 24-26 non
attribuées ici (hypothesis : autres courbes — pet, equip-train, etc.).

`GainExp` @0x0841cb80 : level `this+0x470` (short), exp `+0x474` (long long) ;
au level-up les gains de stats viennent de `classData` (`+0x38/0x3a/0x3c/
0x3e/0x40` → `this+0x338..0x348`, HP%/MP% → `+0x34c/+0x350`).

`GainClassPoint` @0x0841c140 : classLv `this+0x47e`, JP `+0x484` ; table JP
normale `Ins+0x36c`, éveillée `Ins+0x378` (classe du groupe `GetClassGroup`) ;
cap éveillé `S_Parameter[580] = 100`.

Décroissance par écart de niveau, `ExpDeclineRate` @0x083f5150 /
`JpDeclineRate` @0x083f4ef0 / `DesDeclineRate` @0x083f4db0 :

Signature vérifiée par l'appelant (`CMonster::GiveExp` @0x084994a0 :
`ExpDeclineRate(niveauPerso, niveauMob)` — les étiquettes du log interne sont
inversées dans une branche, piège du décompilé) :

```
rate = (100 − decline%) / 100
exp  : mob > perso → decline% = (mobLv − persoLv − 5)·P329          ← 5 littéral !
       perso > mob → decline% = (persoLv − P328 − mobLv)·P329       ← P328 = 9 (eExpLevelDiff)
jp   : même structure (grace inverse = eJpLevelDiff P364 = 9), même P329 = 9
des  : decline% = clamp(((Δlv)·100 − 100)/P, 0, 100)   P576 = 17 / P577 = 20 selon le sens de l'écart
```

⚠️ **Asymétrie codée en dur** : le palier de grâce est **5 (littéral)** quand
le mob domine et **9 (`eExpLevelDiff`)** quand le perso domine — le seuil
anti-leech (mob supérieur) n'est donc pas réglable via `S_Parameter.ini`
(verified : les deux formes coexistent dans le code).

`ExpForTeam` @0x083f4470 : `exp·0.01·count·(membreLv·P330 + 100)/totalLv`,
cap = exp ; `P330 = 25`. `ExpForTeamNoShare`/`JpForTeamNoShare`
@0x083f5340/@0x083f50e0 : `(param₂·P331 + 100)·0.01·(gain·declineRate)`,
`P331 = 5`, borne `param_1` si `> 2×param_1`.

## 5. Caps et constantes diverses — verified

| Constante | Valeur | Où |
|---|---|---|
| Or max | 999 999 999 | `CCharacter::GainGold` zone @0x0842a7f0 (`999999999 − gold < gain` → refus) |
| Dégâts max | 9 999 999 | `CalculatePhysicalDamage` (`DAT_0888b6bc`, mob) |
| P-ATK/M-ATK max | 999 999 | `MeleeDamage`/`MagicDamage` |
| MaxMP max | 9 999 999 | `MaxMP` @0x083f48c0 |
| Stats composites (Luk/Dex/Str/Int) | 9 999 | toutes les formules §3 |
| Taux (hit/dodge/crit/block/parry) | /10 000 | `rand%10000`, caps 10000 |
| Précision % | cap 300 % (30000) | `PhysicalHitRate` |
| Crit dmg | [100 %, 300 %] | `PhysicalCriticalDamage`, `PhysicalCriticalDamage(char)` |
| Résistance dégâts | [−70, +70] | `DAT_0888b694/-c`, phys + sorts |
| Ajustement défense | `lv^1.6 + 30` | `pow(lv,1.6)+30`, phys + crit dmg |
| Offset dégâts par Δlv | ±10 (chars/boss) / 0-500 (mobs) | formules §2 |
| Debuff de stats | ×0.3 (`this[0x3a]&2`, `>>1&1`) | `_DAT_0888b688` |
| Niveau personnage | 100 | `S_Parameter[362]`, tailles tables |
| Niveau classe éveillée | 100 | `S_Parameter[580]` (`GainClassPoint`) |
| Cap palier expertise | 100 lv classe | `S_Parameter[578]` (`RefreshJPAward`) |
| Enchant permanent | durée −2, flag 0x1000 | `EffectToLife`, `RefreshJPAward` |
| HitRateExt | +10 %/miss vs joueurs | `this+0xa7c`, `CCharacter::Attack` |
| Types d'entité | 7 = CCharacter, 0xb = CMonster, 0x50b = boss/kfelf, 0x8b = non-esquivable (hit forcé 100 %) | vérifié par comparaisons `(**+8)` |
| Slots arme | `GetWeaponItem(7/8/10)` ; classe `0x14` gère slot 10 (classe à mécanique d'armes spéciale — hypothesis : dual-gun/mimic) | `MeleeDamage`, `CanBlock/Parry` |
| Types d'arme | 17 types, ids `0x11..0x21` | index maîtrises +0x304/+0x348/+0x38c |

## 6. Paramètres `S_Parameter.ini` utilisés par ces formules

`AllFormulaParas` = `vector<float>` de 0x2a6 (678) entrées, indexé par **l'id
de ligne de `S_Parameter.ini`** (`CServerDBO::LoadAllFormulaParas`
@0x0836dfd0 — les logs nomment « eMagicSkill38 », « eExpLevelDiff »…,
confirmant index = id). Valeurs lues dans
`eden_server/server_us/Data/db_decoded/S_Parameter.ini` (oracle US) :

| Id | Valeur | Usage | Statut |
|---|---|---|---|
| 38 | 0.5 | coef. déf. magique, `CalculateMagicalDamage` | verified |
| 301 / 302 | 0.05 / 20 | RecoverHP taux/base | verified |
| 303 / 304 | 0.05 / 40 | RecoverMP taux/base | verified |
| 305 | 5 | FOR→P-ATK | verified |
| 306 | 0.3 | ratio arme secondaire P-ATK | verified |
| 307 / 308 | 5 / 0.3 | INT→M-ATK / off-hand M-ATK | verified (index lu ; nom inféré par symétrie) |
| 309 / 310 | 0.8 / 3 | LUK→crit phys / base | verified |
| 311 / 314 | 150 / 150 | crit dmg phys / mag de base | verified |
| 312 / 313 | 1 / 3 | LUK→crit mag / base | verified |
| 315 / 316 | 1 / 1 | LUK→précision / base | verified |
| 317 / 318 | 1 / 5 | AGI→esquive / base | verified |
| 325 | 100 | précision de base (jet %) | verified |
| 326 | 3 | coef. DEF dans dégâts phys | verified |
| 327 | 3 | (non rencontré — probablement DEF sorts, hypothesis) | hypothesis |
| 328 / 329 | 9 / 9 | eExpLevelDiff / eExpLevelDec | verified (nommé dans le log) |
| 330 / 331 | 25 / 5 | poids groupe Exp/JP | verified |
| 362 | 100 | cap niveau personnage | verified |
| 364 | 9 | eJpLevelDiff | verified (nommé) |
| 562 | 20 | DEF→MaxHP | verified |
| 576 / 577 | 17 / 20 | DesDeclineRate par sens d'écart | verified (rôle exact par direction, hypothesis) |
| 578 | 100 | cap lv classe → palier expertise | verified |
| 580 | 100 | cap niveau classe éveillée | verified |

## Vérifications des docs harvest (résultat)

- « rang = floor(min(lv, S_Parameter 578 = 100) × 0.2) » → **exact**
  (`×0.2f`, troncature flottant→int ; paliers 5/10/15…).
- « lecture `c_classrankbuff.ini`, 6 champs/palier, enchant = 6ᵉ champ » →
  **exact** ; fichier serveur = `S_ClassRankBuff.ini` (le `c_` est la copie
  client), 22 paliers max/ligne dans le désérialiseur.
- « cumul inter-classes » → **exact** (boucle + rebuild du vecteur d'enchants).
- « appel depuis AddClass/GainClassPoint/Revive » → **exact** + `HookCharacter`
  (login), soit 4 appelants.
- « durée −2, flag 0x1000 » → **exact**.
- Nouveau : sélection exige `clé == rang` (rang 0 ⇒ pas d'enchant, lv<5) ;
  license de classe tous les 10 niveaux ; JP borné à `seuil−1` quand
  `classLv == charLv`.
