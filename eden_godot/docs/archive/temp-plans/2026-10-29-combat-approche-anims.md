# Plan d'action — combat 2.1/2.2 (approche de cast + gestuelles)

## 2.1 Approche avant cast ✅
- [x] `SkillCast.BeyondReach` : portée `C_Spell` col28 ×1,5, repli 4 u.
- [x] Poursuite `_chaseSpell/_chaseTarget` : `SetMoveTarget` ~0,3 s,
  arrivée → `ClearMoveTarget`+`FaceTowards`+envoi 9 ; annulation
  `Cancelled`/despawn/`NoMove`.
- [x] Live : `poursuite cast #50017` → course → 9 → 279 → 281
  (`--cast-no-engage`, cible la plus lointaine). Commit `dcc2d16`.

## 2.2 Gestuelles de cast ✅
- [x] Mapping natif : `C_Spell` col3 = KF → `motion.ini` → nom anim
  (`MotionTable.Lookup(stem, kf)`, stem KFM du corps via
  `CharModelSpec.BodyStem`). Ex. kf 405 → `shoot05`.
- [x] Déclenchement sur 279 ok (`OnCastOk`) ; timed → `_castAnim`
  rejouée jusqu'à résolution (281/287/410 l'effacent).
- [x] Cause racine de l'anim absente : `AnimRetarget.CharWanted` ne
  listait pas `shootNN`/`magicNN`/`battleNN` non montés → absents des
  `.slim.glb` (préférés par `EntityAnimator`). Élargi côté C# **et**
  côté `tools/anim/trim_anim_glb.py`, slims régénérés (296 anims).
- [x] Live : `--cast-spell=59280` → 279 castDs=10 →
  `cur='shoot05' playing=True` (animdiag, tracks=449).
