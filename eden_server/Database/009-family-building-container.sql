\connect edenfr_db
-- Migration 009 : conteneur bâtiments des halls de famille
-- Table native family_building_container (schéma prouvé par
-- CFamilyDBO::LoadFamilyBuilding @0x817c2b0 et
-- CFamilyDBO::InsertItemIntoBuilding @0x8178ec0, mission).
-- La table existe déjà dans le dump natif edenfr_db — IF NOT EXISTS pour
-- les bases créées sans dump.

CREATE TABLE IF NOT EXISTS "public"."family_building_container" (
  "family_id"       INTEGER          DEFAULT 0,
  "id"              INTEGER          DEFAULT 0,   -- uid item-bâtiment (+8)
  "item_tid"        INTEGER          DEFAULT 0,   -- template item (+0xc)
  "tid"             INTEGER          DEFAULT 0,   -- template bâtiment S_Building (+0x84)
  "item_type"       INTEGER          DEFAULT 0,   -- type d'item (+0x88)
  "durability"      INTEGER          DEFAULT 0,   -- durabilité (+0xe)
  "container_index" SMALLINT         DEFAULT 0,   -- conteneur (0 = bâtiments)
  "loc"             SMALLINT         NOT NULL,    -- emplacement (clé de map)
  "x"               DOUBLE PRECISION DEFAULT 0,   -- position X (+0x8c)
  "y"               DOUBLE PRECISION DEFAULT 0,   -- position Y (+0x90)
  "face_dir"        DOUBLE PRECISION DEFAULT 0,   -- orientation (+0x94)
  "level"           INTEGER          NOT NULL,    -- niveau bâtiment (+0x98)
  "exp"             INTEGER          NOT NULL,    -- exp bâtiment (+0x9c)
  "due_date"        INTEGER          DEFAULT 0    -- échéance epoch (+0x30)
);

CREATE INDEX IF NOT EXISTS family_building_container_index
  ON family_building_container (family_id, container_index, loc);

COMMENT ON TABLE family_building_container IS
  'Objets placés dans les halls de famille (native family_building_container)';
