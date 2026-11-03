-- DB-2 — FKs d'intégrité compte ↔ personnage, rendues possibles par la
-- fusion en une base (avant : rustines C# entre edenfr_account et edenfr_db).
-- Idempotent.

-- Un personnage appartient à un compte existant.
-- RESTRICT : supprimer un compte exige de supprimer ses personnages d'abord.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_player_characters_account') THEN
    ALTER TABLE game.player_characters
      ADD CONSTRAINT fk_player_characters_account
      FOREIGN KEY (account_id) REFERENCES account.accounts (id) ON DELETE RESTRICT;
  END IF;
END $$;

CREATE INDEX IF NOT EXISTS ix_player_characters_account_id
  ON game.player_characters (account_id);

-- Le magasin de points (member.tb_user) pointe sur un personnage.
-- NOT VALID : des lignes héritées (compte de test) référencent des
-- personnages absents ; seules les nouvelles écritures sont contrôlées.
DO $$ BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tb_user_character') THEN
    ALTER TABLE member.tb_user
      ADD CONSTRAINT fk_tb_user_character
      FOREIGN KEY (char_id) REFERENCES game.player_characters (id)
      ON DELETE SET NULL NOT VALID;
  END IF;
END $$;
