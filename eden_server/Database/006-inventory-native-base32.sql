\connect edenfr_db
-- Native ClientEnterNodeInventory capture (17:22:12.907) sends 32 slots
-- for the base inventory container. Earlier schema revisions defaulted
-- this custom capacity row to 20, which disagreed with the native 312.
BEGIN;

ALTER TABLE inventory_slots
    ALTER COLUMN bag_0_slots SET DEFAULT 32;

UPDATE inventory_slots
SET bag_0_slots = 32
WHERE bag_0_slots = 20;

COMMENT ON COLUMN inventory_slots.bag_0_slots IS
    'Nombre de slots du sac principal (32 natif, capture NCID 312)';

COMMIT;
