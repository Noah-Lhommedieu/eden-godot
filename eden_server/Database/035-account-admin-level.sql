-- 035 : l'administration est portée par le COMPTE (PLAN-ACTION-3 D0) : niveau d'administrateur 0-5, dérivé en privilège de jeu à chaque entrée.
BEGIN;
ALTER TABLE account.accounts ADD COLUMN IF NOT EXISTS admin_level integer NOT NULL DEFAULT 0 CHECK (admin_level BETWEEN 0 AND 5);
COMMENT ON COLUMN account.accounts.admin_level IS 'Niveau d''administrateur du compte (0 = joueur, 5 = administrateur complet). Le privilège de TOUS les personnages du compte en est dérivé à l''entrée en zone ; player_characters.privilege n''est plus qu''un repli.';
UPDATE account.accounts SET admin_level = 5 WHERE role = 'Admin' AND admin_level = 0;
COMMIT;
