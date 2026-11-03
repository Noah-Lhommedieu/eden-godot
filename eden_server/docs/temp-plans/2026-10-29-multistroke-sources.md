# Plan — 2.4.1/2.4.2 MultiStroke : args + sources indépendantes

## Preuves (zone/decompiled.c + données + tooltips)

- `CEC_MultiStroke::Init` @0x8545f20 : 5 champs lus
  `[arg0→+0x10 mask, arg1→+0x12 c1, arg2→+8 f1, arg3→+0x14 c2,
  arg4→+0xc f2]`.
- `Execute` @0x8546210 : rolls **séquentiels** (échec → break), 2 coups
  max, dégât = `f × seed` (seed=0 depuis AttackTrigger → vfunc calcule
  un dégât frais), stack `CEnchant+0x30` multiplie chance ET facteur,
  wire = 292 ClientEffectReport (coups négatifs) NotifyClient +
  BroadCastToCanSee.
- **Données/tooltips** (~150 lignes corrélées) : `2031|15|50` =
  « +15 % Double Hit », `2031|4|50` = « 4 % », `2031|3|50|100|30` =
  « 3 % triple hit » → **arg0 = chance1, arg1 = facteur1** (le champ
  +0x10 « mask » du binaire est renseigné en données comme un % ;
  valeurs arg0 ∈ 1..100). Le port adopte la sémantique données :
  `[c1, f1, c2, f2]` à arg0..arg3.
- 78 % des enchants 2031 ont col40/41 **vides** → passifs, hors
  `GetProc` (exige trigger) → jamais roulés.
- `InstantOnlyCommands` contient 2031/2032 → un buff « 2031 seul » ne
  crée aucun status → source perdue (certificats « Double Attaque
  +1 % » = 2031|1|50).

## Changements

1. `ExecuteProcCommandAsync` : MultiStroke `Arg(0..3)` (arg0 = chance1
   — ne plus le sauter), + stacks du statut multiplicateur.
2. `EnchantService.GetAttackCommands(id)` : blocs non référencés par
   col40, filtrés {1031,2031,2032} → commandes MultiStroke passives.
3. `RunItemProcsAsync` : sources = `ProcEnchantIds` (équipement) ∪
   `st.Statuses` (AddedAt), dédup par effectId, stack porté ;
   MultiStroke passifs roulés sur evt Attack, chaque commande = roll
   indépendant ; les GetProc déclenchés s'évaluent aussi pour les
   statuts (même vote natif).
4. Filtre équipement `Combat.cs` : `GetProc != null ||
   GetAttackCommands().Count > 0`.
5. Retirer 2031/2032 de `InstantOnlyCommands` (commande gérée → le
   natif crée un CEnchant : `SEffectData+0x12c != 0`).
6. Test Eden.Tests : GetAttackCommands sur vraies lignes S_Enchant +
   cas déclenché/passif.

## Preuve live (29/10)

- Client `--zone-cmd="self_use_effect 48149 120"` + `--combat-test`
  (compte admin, priv=5) : enchant `2031|100|100|100|100` (2 coups à
  100 % garantis).
- Log zone : `Status add char=200000002 enchant=48149 rest=120
  stack=1 persist=True` (le status persiste — plus consommé en
  instantané), puis `MultiStroke char=200000002 mob=… +143` ×2 —
  dégât = facteur 100 % × coup déclencheur 143 ✓ stack=1 ✓.
- Fil : 2 × `NCID=292 len=24` …`71ffffff` (-143) après le hit — le
  même layout variable (u32 atk + u32 tgt + … + i32 dmg négatif) que
  le rapport natif.
- Aucune récursion (les coups extra ne redéclenchent pas de proc),
  le coup d'origine unique avant les 292.
- `Eden.Tests` 1226 verts dont `GetAttackCommands_*` sur les vraies
  lignes S_Enchant.

## Hors périmètre

- Vfunc +0x94 dégât frais natif (seed=0) : on garde
  `hitDamage × factor%` (approximation documentée).
- Procs mobs (registre mob 2.3.1) — inchangé.
