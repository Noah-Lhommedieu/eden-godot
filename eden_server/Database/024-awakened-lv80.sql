-- Migration 024 : classes éveillées insérées à lv1/aa1 avant KB-04.
-- Natif AddClass @0x080fd070 : classId != base -> level=80, aa_point=80.
-- Éveillées = id local 21-58 et 60/61 (59 = Juge, classe de base).
-- Idempotent ; ne touche que les lignes jamais jouées (exp=0, point=0).
UPDATE player_classlist SET level = 80, aa_point = 80
WHERE level = 1 AND exp = 0 AND point = 0 AND aa_point = 1
  AND (class % 100 BETWEEN 21 AND 58 OR class % 100 IN (60, 61));
