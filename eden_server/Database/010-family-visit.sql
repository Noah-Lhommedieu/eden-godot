\connect edenfr_db
-- 010 — hall de famille : persistance de la visite et du point de
-- retour d'instance (exec+0x114 « VisitFamilyID » et exec+0xf0
-- « CrossZoneData » natifs — GenerateOffset @mission:0x834d530).
-- cross_zone_* = dernier node non-instancié quitté + position : les
-- nodes de type 9/10/13/15/16/17/19 (colosseum, versus, battle…)
-- ont leur propre mécanisme de retour et n'écrasent pas ce point.
ALTER TABLE player_characters
    ADD COLUMN IF NOT EXISTS visit_family_id integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS cross_zone_node integer NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS cross_zone_x double precision NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS cross_zone_y double precision NOT NULL DEFAULT 0;
