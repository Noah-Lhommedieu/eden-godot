# Plan — 2.3.1 Registre d'enchants sur mobs

## Preuves natives

- `ILifeEntity::AddEnchant` @0x847ea90 : tout LifeEntity (mobs
  compris) a un `CEnchantMgr` — 299 émis si `!IsHideIcon &&
  (sets&0x7c000000)==0 && dur != -9`, BroadCastToCanSee.
- Capture mort mob (commentaire `FinishMobDeathAsync`) :
  `300 0xcb35` + `300 0xcb36` émis à la mort → le natif vide le
  registre via des 300 visibles.
- `EffectToLife` @0x8060ec0 : `dur != -1` → AddEnchant ;
  dur = S_Spell c41 (`CCharFormula::SpellDuration` @0x83f9310,
  vide → -9 = durée propre de l'effet, icône cachée).
- Golden `ncid_301.json` : `CNC_CZ_ClientEnchantUpdate` =
  `[i32 handle][u8 n][n×16B entry][i32 flags][u8 serial]` — le codec
  généré matche déjà, il faut remplir `Entries`.
- `CEnchant::GetRestTime` @0x85603d0 : rest en ds depuis
  `CEnchant+0x20` (+durée propre SEffectData+0x110 si ≠ -1).
- Locks 2055 (même décodage que le joueur, Status.cs:365) :
  var1→NoMove, var2→NoAttack ; 3001 args[2]>0 régén,
  3002 args[0] signé = HP/tick (DoT).
- Dispel : `EnchantDispel` (pur, déjà testé) — mêmes candidats
  (IsDebuff col7 b1 / Undispellable col8 0x10 / icône).

## Changements

1. ✅ `ZoneLiveWorld.LiveMob` : `Enchants` (effectId → `MobEnchant`
   {CasterId, RestSeconds, Level, StackCount, HpPerTick, LockFlags,
   Hidden}), `MoveLocked`/`AttackLocked`, `AllMobs` pour le scan.
   Purge dans `ResetCombatForRespawn`.
2. ✅ Nouveau `ZoneEntryFlow.MobEnchants.cs` :
   `AddMobEnchantAsync` (upsert + 299 broadcast node+inst si visible),
   `RemoveMobEnchantAsync` (300 broadcast si visible),
   `PurgeMobEnchantsAsync` (300 × actifs + clear — appelé de
   FinishMobDeathAsync), `BroadcastToMobSeersAsync` (toutes sessions
   node+inst avec le handle dans `ost.Sent`, émetteur inclus),
   `TickMobEnchantsAsync` (SlowTick 1 s : HpPerTick + attr 0 aux
   témoins — DoT pouvant tuer via FinishMobDeathAsync du castreur si
   sa session vit, sinon borne 1 HP — expire → remove).
3. ✅ `ApplySpellResultCommandsAsync` : signature `(session, st,
   spellId, def, mob, token)` aux 7 call sites (6 Resolution + 1 Elf)
   — après la boucle de commandes, si `def.Value != -1 &&
   !IsInstantOnly` → AddMobEnchant (hidden = `def.Value == -9`,
   restSec = Value + GetSpellAttrModify(9), level =
   PlayerSpellLevelAsync).
4. ✅ `MobCombatTickAsync` : `MoveLocked` → pas d'avance,
   `AttackLocked` → pas de frappe ni de sort (`canRiposte` gate).
5. ✅ `MobWanderStepAsync` : skip si `MoveLocked`.
6. ✅ `FinishMobDeathAsync` : `PurgeMobEnchantsAsync` — 300 par
   enchant visible puis Clear (capture native).
7. ✅ `HandleEnchantUpdateAsync` (NCID 18, dispatch ZoneEntryFlow.cs:1132)
   : remplit le 301 — handle mob → `mob.Enchants`, handle joueur →
   ses `Statuses` (FindByCharacter). Ancien commentaire
   « pas d'enchants persistants » retiré.
8. ✅ Dispel mob : `MobEnchantDispelHandler` {1821,1822,1823,1824}
   dans `SpellEffectHandlers` — 1821/1824 via `EnchantDispel.Pick`,
   1822 EnchantCancel par plage de type (col38 → GetTypeId) + grade
   ≤flag (col39 → GetGrade) + non-indissipable, 1823 KillList.
   `ISpellEffectHost.RemoveMobEnchantAsync` ajoutée, pont
   `MobSpellEffectHost` → `flow.RemoveMobEnchantAsync`. Executor
   lazily créé via `CreateDefault(_services.Enchants)`.
9. ✅ Tests `SpellEffectTests` : 3 cas (1821 debuff-filtré, 1822 plage
   de type, 1823 liste) sur la vraie table `db_decoded` — 1229 verts.

## Hors périmètre

- Procs mob / miss / hurt-be-kill (2.4.3).
- FindByType/remplacement par groupe (2.3.4).
- DoT hors combat : le tick continue (natif — le registre mob survit
  tant que le mob vit ; leash/reset = heal full + clear, à relier
  quand le deaggro-reset existera).

## Vérif runtime (2026-10-29)

- `cast 50025` → NCID 9 → `add mob=ffff80ae enchant=52025 rest=60` →
  **299 émis** (rest=60) ; **icône debuff visible** sur le cadre cible
  (flameshot `mob-debuff-icon.png`).
- Lock : le mob émettait des 326 (:42/:47/:51) → après le lock **zéro
  326** jusqu'à sa mort — `MoveLocked` effectif.
- DoT : 51940→58447 `-500/s` → mob 211 HP mort par le seul tick
  (`FinishMobDeathAsync`, 305 tueur=castreur) ; purge 300 émise.
- Hidden : `51000`→52218 `dur=-9` → `add mob rest=-9 hidden=True`,
  **aucun 299** (conforme AddEnchant natif).
- 301 : le client reçoit le 301 rempli pour le handle mob (caster,
  rest, stacks, level) après NCID 18.
