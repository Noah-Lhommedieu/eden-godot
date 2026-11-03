# P16.3 — Timing natif swing/projectile (fin des délais fixes 300 ms)

Date : 2026-11-05. Suite de `2026-10-23-combat-character-equipement-quetes.md` P16.3.

## Preuves natives (déjà collectées)

- `lapis::CSpellToLife::GetBlastTime` zone@0x084f7db0 : délai blast =
  **distance 3D caster→cible / flySpeed** (SSpellData+0xa8 ; 0 →
  `DefaultFlySpeed` = **40 u/s**, .data@0x089abae2).
- `lapis::CKfm::GetMotionTime` zone@0x080e0b00 : temps de la clef `"hit"`
  d'une motion (KFM,kfId) → swing avant impact.
- `lapis::ParseMotionIni` zone@0x080e1cd0 : Motion.ini lignes
  `DBn=kfm,kfId,sec,hit|end,...` — secondes → ds par `round(sec*100)/10`.
- `lapis::CEventSpellBlasting::Execute` zone@0x0866d6e0 : délais armés en
  décisecondes (`param_2 % 10 * 100000` + `param_2 / 10`).
- `SSpellData` : c8 (idx7) non-vide → flag projectile ; c9 (idx8) u32 →
  flySpeed = raw × 0.5 ; c4 (idx3) = blastId → `ClientSpellBlastOK`.
- `lapis::CCharKfm::GetBlastID` zone@0x080e03f0 : KFM lettre par
  nibble genre (char+0x4aa) — table switch {0:g,1:b,2:m,4:f,6:k,8:l,9:c,
  défaut:g}.
- Mêlée `CEventDelayDamage` : `local_2c` = hit-time du kfId d'attaque
  choisi (+ dist/40 pour les armes à distance).

## Plan

- [x] `MotionParser.ParseTimings` → map `kfm → kfId → (HitDs, EndDs)`
  (rows `hit`/`end`, sec→ds natif).
- [x] `SpellParser` : `HasProjectile` (idx7 non-vide) + `FlySpeed`
  (idx8 × 0.5).
- [x] `GameDataService.MotionTimings` (chargé avec AttackKfIds).
- [x] `ZoneEntryFlow` : `KfmForGender`, `SwingDelayMs` (blast hit-time,
  fallback kfm lettres → attaque arme → 400 ms), `FlyDelayMs` (dist/vit,
  vit 0 → 40).
- [x] `ExecuteSpellHitAsync` : Aggro à +swing, Impact à +fly
  (paramétrer `SpellHitAggroAsync`).
- [x] Mêlée : `MeleeHitDelayed`/`MeleeEngageDelayed` au hit-time du kfId
  émis (récupérer le kfId choisi ; +fly pour kinds arc/arbalète/armes
  à feu).
- [x] Tests : parse timings, résolution délai, fallback.
- [x] Build 0 warn + tests verts + commit + plan P16.3 à jour.

## Hors scope (autres cases P16)

Freeze résiduel cast (P16.1 — wire diff), multi-aggro (P16.2),
CPU-spin (P16.4), gear upgrade (P16.5).
