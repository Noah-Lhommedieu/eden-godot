# 2026-10-22 — Sorts de soin + dégâts des sorts à cast (mapping S_Spell corrigé)

## Symptômes live

- Les sorts de soin ne soignent pas, pas de chiffre vert, pas de crit de soin.
- Les sorts à cast time ne font plus crash mais **0 dégât**.

## Preuves natives (zone/decompiled.c)

### Mapping colonnes S_Spell — `CSpellQuery::Deserialize` @0x862b000

Le désérialiseur lit **45 cellules séquentiellement** (header `|V.8|45|` ✓) :

| ini col | CSpellData | SSpellData | Rôle |
|---|---|---|---|
| 17 | +0x34 | +8 | **famille/catégorie** (6/7 = sorts de classe) |
| 18 | +0x38 | +0xc | **TYPE dispatch** (4/5 → HitRestoreSkill) |
| 33 | +0x76 | +0xbe | élément (sélecteur flags + resist) |
| 35 | +0x8c | +0xc0 | **power** (PAS col32 !) |
| 36 | +0x8e | +0xc2 | **heal power** |
| 37 | +0x90 | +0xc4 | **variance** (PAS col34 !) |
| 39 | +0xb0 | → enchants | ResultSpellId |
| 40 | +0xb4 | +0x80 | Value (byte) |
| 42 | +0xbc | +0xcc | PayType |
| 43 | +0xc0 | +0x88 | UpgradeCostBase |
| 26 | +0x64 | +0x38 | cast time (ds) |
| 22 | +0x4c | +0x20 | RequiredLevel (CheckLevel) |
| 23 | +0x50 | +0x24/28 | ClassMask u64 |
| 28 | +0x6c | +0x40 float | portée |
| 29 | +0x6e | +0x48 | durée (×const) |

### Dispatch `OnHitLife` @0x84f2080 — type = col18

```
flags = InitHurtType(+0x94) : type6/7→{u3:8,u5:0x0c,u4:8}, type4/5→{0x40,0x44,0x48},
        type2/3→{0x20,0x24,0x28}, autre→{0,4,8} ; elem1→u5, elem∈[4,9]→u4, sinon→u3
if ((flags & 0x68) == 0) || (type-4 < 2):
    if ((flags & 4) == 0): type∈{4,5} → HitRestoreSkill
    else → HitPhysicalSkill
else → HitMagicSkill
```

- **Type 4/5 = soins/rez** (col18 : Cure 50028→4, Healing Light 50055→4, Revive…).
- Quirk : type4/5 + elem1 → flags 0x44 → bit4 → **HitPhysicalSkill**.
- Sorts d'attaque col17=4/5 (Earth Shock 50026, col18=1) → NOTRE code les routait en Restore → **0 dégât = le bug "cast sans dégât"**.

### `HitRestoreSkill` @0x84f1f40

- `*flags = 0x200` ; si `+0xc2 != 0` → `CalculateMagicSkillHeal` ; roll
  `rand%10000 < CalculateMagicCriticalRate` → `CalculateMagicCriticalDamage` → `|0x200000`.

### `CalculateMagicSkillHeal` @0x83f9310

- `base = ROUND(healPower(+0xc2) × refCoef(+0x10=S_SpellReference col4)/100)`
- `r = rand % (variance(+0xc4)×10000) × 0.0001` → `heal = ROUND((2r + 100 − var) × base × 0.01)`
- `Heal(caster)` @0x83f9210 / `BeHeal(target)` @0x83f9110 : mods d'enchants G/P-Healing
  (équipement/enchant) — **≈ identité** sans enchant heal actif.
- `SpellDamage` = modificateur par sort (enchant) — identité par défaut.

### Paquets `OnHitLife` @0x84f2080+

1. `ClientUpdateAttr` (280) attr HP + attr 0x13 sur la cible (NotifyClient + broadcast)
2. `ClientSpellHitToLife` (281) `[u16 spell][u32 victim+0x34][u32 caster+0x34][i32 amount][i32 flags]`
   → NotifyClient(cible) + BroadCastToCanSee + au caster si cible joueur damage.
3. Flags : `1`=hit, `8`=miss, `0x10`=block, `0x200`=restore (VERT), `0x200000`=crit,
   `0x800000`=flag +0x12&2.

## Corrections à faire

- [x] `SpellParser` : `Type`=col18, `Family`=col17 (nouveau), `TargetType`=col18,
      `DamagePower`=col35, `DamageVariance`=col37, `HealPower`=col36 (nouveau),
      `Duration`=col29 (col33 = élément !)
- [ ] `SpellReferenceParser` : `HealCoef`=col4 (+0x10)
- [ ] `NativeCombatFormulas.ResolveSpellHeal` : formule native + crit mag
- [ ] `ExecuteSpellHitAsync` : kind Restore → heal mob + 281 flags 0x200|crit ;
      281 flags dynamiques partout (miss=8, crit=|0x200000)
- [ ] `ExecuteSpellOnLifeAsync` : HealPower>0 → heal + 281 0x200|crit + vitals
- [ ] `PlayerHandlers` gate learn : `def.Type 6/7` → `def.Family 6/7`
- [ ] Tests : parser cols, heal formula, flags 281
- [ ] Build 0 err / tests
