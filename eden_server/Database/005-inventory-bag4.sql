\connect edenfr_db
-- 005 — 4e slot de sac équipable.
-- Le porte-sacs natif (CBagContainer index 0, map char+0x52c) a 5 slots
-- dont le slot 0 est refusé par SwitchBagFromContainer (le sac de base) :
-- les slots 1-4 ouvrent les sacs d'inventaire 1-4. inventory_slots ne
-- couvrait que bag_0..bag_3 — ajout de bag_4 pour couvrir le slot natif.
ALTER TABLE "public"."inventory_slots"
    ADD COLUMN IF NOT EXISTS bag_4_slots integer DEFAULT 0,
    ADD COLUMN IF NOT EXISTS bag_4_rental_expiry timestamp without time zone;

COMMENT ON COLUMN inventory_slots.bag_4_slots IS
    'Nombre de slots du sac 4 (holder slot 4, CBagContainer natif)';
COMMENT ON COLUMN inventory_slots.bag_4_rental_expiry IS
    'Expiration du rental du sac 4 (NULL = permanent/pas de sac)';
