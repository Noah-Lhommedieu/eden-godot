\connect edenfr_db
-- Migration 003: Characters Schema
-- Base: edenfr_db (Game Database)
-- Date: 15 Octobre 2026
-- Phase 1.2: Character Creation & Deletion

-- =============================================================================
-- TABLE: characters
-- =============================================================================
-- Stocke les personnages des joueurs
DROP TABLE IF EXISTS "public"."characters" CASCADE;
CREATE TABLE "public"."characters" (
  "id" SERIAL PRIMARY KEY,
  "account_id" INTEGER NOT NULL,
  "name" VARCHAR(16) NOT NULL UNIQUE,
  
  -- Base attributes
  "race" SMALLINT NOT NULL CHECK (race BETWEEN 1 AND 5), -- 1:Human, 2:Zumi, 3:Ursun, 4:Halfkin, 5:Anuran
  "gender" SMALLINT NOT NULL CHECK (gender IN (1, 2)), -- 1:Male, 2:Female
  "current_class_id" INTEGER NOT NULL DEFAULT 1,
  "character_level" INTEGER NOT NULL DEFAULT 1 CHECK (character_level BETWEEN 1 AND 120),
  "experience" BIGINT NOT NULL DEFAULT 0 CHECK (experience >= 0),
  
  -- Appearance
  "hair_style" SMALLINT NOT NULL DEFAULT 1,
  "hair_color" SMALLINT NOT NULL DEFAULT 1,
  "face_style" SMALLINT NOT NULL DEFAULT 1,
  "eye_color" SMALLINT NOT NULL DEFAULT 201,
  
  -- Base stats (modifiables par équipement/buffs)
  "str" INTEGER NOT NULL DEFAULT 10 CHECK (str >= 0),
  "dex" INTEGER NOT NULL DEFAULT 10 CHECK (dex >= 0),
  "con" INTEGER NOT NULL DEFAULT 10 CHECK (con >= 0),
  "int" INTEGER NOT NULL DEFAULT 10 CHECK (int >= 0),
  "psy" INTEGER NOT NULL DEFAULT 10 CHECK (psy >= 0),
  "agi" INTEGER NOT NULL DEFAULT 10 CHECK (agi >= 0),
  
  -- Current state
  "current_hp" INTEGER NOT NULL DEFAULT 100,
  "current_mp" INTEGER NOT NULL DEFAULT 50,
  
  -- Position
  "world_id" INTEGER NOT NULL DEFAULT 1,
  "pos_x" FLOAT NOT NULL DEFAULT 0,
  "pos_y" FLOAT NOT NULL DEFAULT 0,
  "pos_z" FLOAT NOT NULL DEFAULT 0,
  "rotation" FLOAT NOT NULL DEFAULT 0,
  
  -- Currency
  "gold" BIGINT NOT NULL DEFAULT 0 CHECK (gold >= 0),
  "ep" INTEGER NOT NULL DEFAULT 0 CHECK (ep >= 0), -- Eden Points (in-game)
  "ap" INTEGER NOT NULL DEFAULT 0 CHECK (ap >= 0), -- Aeria Points (cash shop)
  
  -- PvP/Fame
  "pvp_points" INTEGER NOT NULL DEFAULT 0,
  "fame" INTEGER NOT NULL DEFAULT 0,
  
  -- Metadata
  "is_online" BOOLEAN NOT NULL DEFAULT FALSE,
  "deleted_at" TIMESTAMP NULL, -- Soft delete (grace period 7 days)
  "created_at" TIMESTAMP NOT NULL DEFAULT NOW(),
  "last_login" TIMESTAMP NULL,
  "total_playtime_seconds" BIGINT NOT NULL DEFAULT 0
);

-- Indexes pour performance
CREATE INDEX idx_characters_account ON characters(account_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_characters_name_lower ON characters(LOWER(name)) WHERE deleted_at IS NULL;
CREATE INDEX idx_characters_online ON characters(is_online) WHERE is_online = TRUE;
CREATE INDEX idx_characters_deleted ON characters(deleted_at) WHERE deleted_at IS NOT NULL;
CREATE INDEX idx_characters_world ON characters(world_id) WHERE deleted_at IS NULL AND is_online = TRUE;

-- Commentaires
COMMENT ON TABLE characters IS 'Table principale des personnages joueurs';
COMMENT ON COLUMN characters.account_id IS 'ID du compte propriétaire (référence au système d''authentification)';
COMMENT ON COLUMN characters.name IS 'Nom unique du personnage (3-16 caractères)';
COMMENT ON COLUMN characters.race IS '1:Human, 2:Zumi, 3:Ursun, 4:Halfkin, 5:Anuran';
COMMENT ON COLUMN characters.gender IS '1:Male, 2:Female';
COMMENT ON COLUMN characters.current_class_id IS 'Classe actuellement équipée (1-60)';
COMMENT ON COLUMN characters.character_level IS 'Niveau total du personnage (1-120)';
COMMENT ON COLUMN characters.experience IS 'XP totale accumulée';
COMMENT ON COLUMN characters.hair_color IS 'ID couleur cheveux (T_CharColor.ini section 1: 1-45)';
COMMENT ON COLUMN characters.eye_color IS 'ID couleur yeux (T_CharColor.ini section 3: 201-210)';
COMMENT ON COLUMN characters.deleted_at IS 'Timestamp de suppression (NULL = actif, NOT NULL = supprimé)';
COMMENT ON COLUMN characters.total_playtime_seconds IS 'Temps de jeu total en secondes';

-- Trigger pour empêcher la modification du nom après création
CREATE OR REPLACE FUNCTION prevent_character_rename()
RETURNS TRIGGER AS $$
BEGIN
  IF OLD.name IS DISTINCT FROM NEW.name THEN
    RAISE EXCEPTION 'Character name cannot be changed after creation';
  END IF;
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_prevent_rename
BEFORE UPDATE ON characters
FOR EACH ROW
EXECUTE FUNCTION prevent_character_rename();

-- =============================================================================
-- FUNCTION: Validate character limit per account
-- =============================================================================
CREATE OR REPLACE FUNCTION check_character_limit()
RETURNS TRIGGER AS $$
DECLARE
  character_count INTEGER;
  max_characters INTEGER := 6; -- Configurable
BEGIN
  -- Count active characters for this account
  SELECT COUNT(*) INTO character_count
  FROM characters
  WHERE account_id = NEW.account_id
    AND deleted_at IS NULL;
  
  IF character_count >= max_characters THEN
    RAISE EXCEPTION 'Character limit reached for account % (max: %)', NEW.account_id, max_characters;
  END IF;
  
  RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_check_character_limit
BEFORE INSERT ON characters
FOR EACH ROW
EXECUTE FUNCTION check_character_limit();

-- =============================================================================
-- TABLE: character_stats_cache
-- =============================================================================
-- Cache des stats finales (base + équipement + buffs) pour performance
DROP TABLE IF EXISTS "public"."character_stats_cache" CASCADE;
CREATE TABLE "public"."character_stats_cache" (
  "character_id" INTEGER PRIMARY KEY REFERENCES characters(id) ON DELETE CASCADE,
  
  -- Stats finales (base + modifiers)
  "final_hp" INTEGER NOT NULL DEFAULT 100,
  "final_mp" INTEGER NOT NULL DEFAULT 50,
  "final_str" INTEGER NOT NULL DEFAULT 10,
  "final_dex" INTEGER NOT NULL DEFAULT 10,
  "final_con" INTEGER NOT NULL DEFAULT 10,
  "final_int" INTEGER NOT NULL DEFAULT 10,
  "final_psy" INTEGER NOT NULL DEFAULT 10,
  "final_agi" INTEGER NOT NULL DEFAULT 10,
  
  -- Combat stats (calculées)
  "physical_attack" INTEGER NOT NULL DEFAULT 0,
  "magic_attack" INTEGER NOT NULL DEFAULT 0,
  "physical_defense" INTEGER NOT NULL DEFAULT 0,
  "magic_defense" INTEGER NOT NULL DEFAULT 0,
  "accuracy" INTEGER NOT NULL DEFAULT 0,
  "evasion" INTEGER NOT NULL DEFAULT 0,
  "critical_rate" INTEGER NOT NULL DEFAULT 0,
  "critical_damage" INTEGER NOT NULL DEFAULT 150,
  "move_speed" INTEGER NOT NULL DEFAULT 100,
  "attack_speed" INTEGER NOT NULL DEFAULT 100,
  "cast_speed" INTEGER NOT NULL DEFAULT 100,
  
  -- Metadata
  "last_updated" TIMESTAMP NOT NULL DEFAULT NOW()
);

COMMENT ON TABLE character_stats_cache IS 'Cache des stats finales pour éviter recalcul constant';
COMMENT ON COLUMN character_stats_cache.final_hp IS 'HP maximum (base + équipement + buffs)';
COMMENT ON COLUMN character_stats_cache.critical_damage IS 'Dégâts critiques en % (150 = +50%)';
COMMENT ON COLUMN character_stats_cache.move_speed IS 'Vitesse de déplacement en % (100 = normal)';

-- =============================================================================
-- TABLE: character_playtime_log
-- =============================================================================
-- Log des sessions de jeu pour analytics
DROP TABLE IF EXISTS "public"."character_playtime_log" CASCADE;
CREATE TABLE "public"."character_playtime_log" (
  "id" SERIAL PRIMARY KEY,
  "character_id" INTEGER NOT NULL REFERENCES characters(id) ON DELETE CASCADE,
  "session_start" TIMESTAMP NOT NULL,
  "session_end" TIMESTAMP NULL,
  "duration_seconds" INTEGER NULL,
  "world_id" INTEGER NOT NULL,
  "logout_reason" VARCHAR(50) NULL -- 'normal', 'disconnect', 'timeout', 'kick'
);

CREATE INDEX idx_playtime_character ON character_playtime_log(character_id);
CREATE INDEX idx_playtime_session ON character_playtime_log(session_start);

COMMENT ON TABLE character_playtime_log IS 'Historique des sessions de jeu par personnage';

-- =============================================================================
-- VIEW: active_characters
-- =============================================================================
-- Vue pour faciliter les requêtes sur les personnages actifs
CREATE OR REPLACE VIEW active_characters AS
SELECT 
  c.*,
  s.final_hp,
  s.final_mp,
  s.physical_attack,
  s.magic_attack
FROM characters c
LEFT JOIN character_stats_cache s ON c.id = s.character_id
WHERE c.deleted_at IS NULL;

COMMENT ON VIEW active_characters IS 'Personnages actifs avec leurs stats (non supprimés)';

-- =============================================================================
-- FUNCTION: Soft delete character
-- =============================================================================
CREATE OR REPLACE FUNCTION soft_delete_character(
  p_character_id INTEGER,
  p_account_id INTEGER
)
RETURNS BOOLEAN AS $$
DECLARE
  v_deleted BOOLEAN := FALSE;
BEGIN
  UPDATE characters
  SET deleted_at = NOW(),
      is_online = FALSE
  WHERE id = p_character_id
    AND account_id = p_account_id
    AND deleted_at IS NULL
    AND is_online = FALSE; -- Cannot delete while online
  
  GET DIAGNOSTICS v_deleted = ROW_COUNT;
  RETURN v_deleted > 0;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION soft_delete_character IS 'Marque un personnage comme supprimé (soft delete, 7 jours grace period)';

-- =============================================================================
-- FUNCTION: Cancel character deletion
-- =============================================================================
CREATE OR REPLACE FUNCTION cancel_character_deletion(
  p_character_id INTEGER,
  p_account_id INTEGER
)
RETURNS BOOLEAN AS $$
DECLARE
  v_restored BOOLEAN := FALSE;
BEGIN
  UPDATE characters
  SET deleted_at = NULL
  WHERE id = p_character_id
    AND account_id = p_account_id
    AND deleted_at IS NOT NULL
    AND deleted_at > NOW() - INTERVAL '7 days'; -- Within grace period
  
  GET DIAGNOSTICS v_restored = ROW_COUNT;
  RETURN v_restored > 0;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION cancel_character_deletion IS 'Annule la suppression d''un personnage (dans les 7 jours)';

-- =============================================================================
-- FUNCTION: Permanent delete expired characters
-- =============================================================================
CREATE OR REPLACE FUNCTION purge_expired_characters()
RETURNS INTEGER AS $$
DECLARE
  v_deleted_count INTEGER;
BEGIN
  -- Delete characters marked for deletion more than 7 days ago
  WITH deleted AS (
    DELETE FROM characters
    WHERE deleted_at IS NOT NULL
      AND deleted_at < NOW() - INTERVAL '7 days'
    RETURNING id
  )
  SELECT COUNT(*) INTO v_deleted_count FROM deleted;
  
  RETURN v_deleted_count;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION purge_expired_characters IS 'Supprime définitivement les personnages expirés (>7 jours)';

-- =============================================================================
-- FUNCTION: Check name availability
-- =============================================================================
CREATE OR REPLACE FUNCTION is_character_name_available(p_name VARCHAR)
RETURNS BOOLEAN AS $$
DECLARE
  v_exists BOOLEAN;
BEGIN
  SELECT EXISTS(
    SELECT 1 FROM characters
    WHERE LOWER(name) = LOWER(p_name)
      AND deleted_at IS NULL
  ) INTO v_exists;
  
  RETURN NOT v_exists;
END;
$$ LANGUAGE plpgsql;

COMMENT ON FUNCTION is_character_name_available IS 'Vérifie si un nom de personnage est disponible (case-insensitive)';

-- =============================================================================
-- Sample data for testing
-- =============================================================================
-- Uncomment for development/testing

-- INSERT INTO characters (account_id, name, race, gender, current_class_id, hair_style, hair_color, face_style, eye_color)
-- VALUES 
--   (1, 'TestWarrior', 1, 1, 1, 1, 15, 1, 201),
--   (1, 'TestMage', 1, 2, 2, 5, 32, 3, 203),
--   (2, 'TestRanger', 2, 1, 3, 2, 38, 2, 205);

-- =============================================================================
-- Grants (à adapter selon votre système de rôles)
-- =============================================================================
-- GRANT SELECT, INSERT, UPDATE ON characters TO eden_server_role;
-- GRANT SELECT ON active_characters TO eden_server_role;
-- GRANT USAGE, SELECT ON SEQUENCE characters_id_seq TO eden_server_role;

-- =============================================================================
-- END OF MIGRATION 003
-- =============================================================================
