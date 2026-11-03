# Plan — refonte mapping S_Spell (proof complète Deserialize + builder)

## Découverte structurale (corrigée)

`GameData::CSpellQuery::Deserialize` (@0x862af40) lit **séquentiellement** les
cellules d'une ligne S_Spell.ini — **une cellule par lecture**, y compris les
deux `operator>>(longlong*)` (c19 → +0x3c flags i64, c23 → +0x50 mask i64 —
la cellule contient déjà l'i64 complet, ex. 50011 → 54095984974762049).
45 lectures = 45 cellules = `|V.8|45|` exactement. La première itération du
plan (i64 = 2 cellules) était FAUSSE : elle décalait tout après c19.

Cellules string (avancent le curseur d'une cellule, certaines réduites en
bool « non-vide ») : c2, c4-7, c9-13, c15, c16 (nom Big5 → +0x30), c44.

Mapping colonne → CSpellData → SSpellData (désasm `LoadAllSpellData`
@0x8380130, bloc builder ~740560-740735) — définitif :

| col ini | CSpellData | SSpellData | rôle natif |
|---|---|---|---|
| 17 | +0x34 | +0x08 | **famille** (6/7 = sorts de classe → gate class_lv) |
| 18 | +0x38 | +0x0c | **type dispatch** (4/5→HitRestoreSkill ; `SSpellData::Init` en dérive les hit-flags +0x94) |
| 19 | +0x3c i64 | +0x10/+0x14 | **OpFlags** (`CheckOpFlag` @0x84efae0 — conditions de cast) |
| 20 | +0x44 | →+0x18/+0x1a | cast-shape source (switch : 0=life,2=direction,5=cross,6=sector) |
| 21 | +0x48 | +0x1c | ? |
| 22 | +0x4c u8 | +0x20 | **niveau requis** (`CheckLevel` @0x84edfe0) |
| 23 | +0x50 i64 | +0x24/+0x28 | **class mask** (`CheckClass` @0x84ee000) — mono-cellule ! |
| 24 | +0x58 | +0x30 | ? |
| 25 | +0x5c | +0x34 | ? |
| 26 | +0x64 | +0x38 | **cast ds** (gate `OnCast` @0x84ef430, plancher 4ds) |
| 27 | +0x68 | +0x3c | **down-time/cooldown ds** (`CoolDownTime` @0x83f6060, plancher 5ds si >0) |
| 28 | +0x6c | +0x40 float | **portée** (melee=4, heal=40, nuke=36 — float ×const) |
| 29 | +0x6e | +0x48 | durée (u16 × const) |
| 30 | +0xcc | +0xa8 | champ ver≥5 |
| 31 | +0xd4 | +0xac | float ver≥5 |
| 32 | +0x74 | +0xbc | **MP base** (`CostMp` @0x83f44d0 : `refCol1 × base × 0.01`) |
| 33 | +0x76 | +0xbe | **élément** |
| 34 | +0xe0 | — | champ ver≥8 |
| 35 | +0x8c | +0xc0 | **power** (gate `HitMagicSkill`/`HitPhysicalSkill`) |
| 36 | +0x8e | +0xc2 | **heal power** (`HitRestoreSkill`→`CalculateMagicSkillHeal` @0x83f9310) |
| 37 | +0x90 | +0xc4 | variance (lo8 → s16) |
| 39 | +0xb0 | gate | sort/enchant résultat (5xxxx) |
| 40 | +0xb4 | +0x80 | u8 |
| 41 | +0xb8 | +0x84 | value (défaut -9) |
| 42 | +0xbc | +0xcc | **pay type** (SetSpellLevel : 1=or, 9=CP) |
| 43 | +0xc0 | +0x88 | **upgrade cost base** |
| 44 | +0xc8 | — | string |

Vérifications données réelles : Slam 50016 cast='' instant/CD=20ds/MP=75/power=140 ;
Cure 50028 cast=20ds/CD=''/MP=112/heal=110 ; 火矢 50126 CD=30/MP=100/power=150/elem=6 ;
Onrush 50038 cast=''/CD=80/mask=1 (Warrior) ; Ambush 50058 mask=16 (Thief bit4) ;
heals type=4, 神癒之光 50109 type=5, portée c28=40.

## Modèle cooldown natif

- `CSpellMaster` porte {dernier spellId +0x14, expiration +0x18/+0x1c} —
  armé par `CCharacter::PutInDownTime(SSpellData)` @0x842f380 après cast
  réussi ; `CheckInDownTime` @0x84f3720 (retour 1 tant qu'actif).
- Durée = `CoolDownTime(entity, +0x3c)` = `(1−BuffCoolDownRate)×base`,
  plancher 5 ds si base>0.
- Refus de cast natif → `ClientCommandFailReturn` 410 `[i32 ncid][i32 spellId]`
  (cf. `CNE_CZ_ZoneServerSpellCastToLife::Execute` @0x828a1xx).
- 368 `ClientUpdateCoolDown` (Serialize @0x8237d30) : UN paquet fusionne
  items (kind=0, map char+0x6a8) ET sorts (kind=1, `[u8 1][s16 spellId]
  [u16 0][i32 restant ds]` — pairs de `CSpellMaster::GetDownTime` @0x84f3920).
- Global cast lockout séparé : `char+0x660` délai `AllFormulaParas+0x5b4` ds.
- Downtime >3000ds persisté via `SavePlayerCoolDown` — non porté (rare).

## Tâches

- [x] `SpellParser` : remappé — Type=c18, Family=c17, cast=c26 (`CastTimeDs`),
      `CooldownDs`=c27, `MpCost`=c32, `AttackElement`=c33, `DamagePower`=c35,
      `HealPower`=c36, `DamageVariance`=c37, `Range`=c28, `Duration`=c29,
      `ResultSpellId`=c39, `Value`=c41, `PayType`=c42, `UpgradeCostBase`=c43,
      `OpFlags`=c19 (i64), `ClassMask`=c23 (i64 mono-cellule)
- [x] `SpellReferenceParser` : `MpCoef` = col1 (ref+4)
- [x] `SpellCooldownService` (volatile par perso, keyed spellId) +
      `ZoneServices.SpellCooldowns` ; gate au NCID 9 → 410 `[9][spellId]` ;
      arm à la résolution réussie + émission 368 fusionné (kind=0+1)
- [x] MP : `SpellMpCost` = `refCol1(level) × c32 × 0.01` (remplace le déduit
      plat) sur les deux chemins (mob + life)
- [ ] Tests régression parser (champs nouveaux) ; build ; commit
