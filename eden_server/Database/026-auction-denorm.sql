-- E6 : colonnes dénormalisées de filtre pour active_auctions, miroir
-- de l'INSERT natif SaveAuction (@0x0815da30, mission) :
--   item_type      ← CItemData+0x84 (c_item col33) — égalité filtre
--   level          ← CItemData+0x3e (c_item col14 = RequiredLevel)
--   class          ← CItemData+0x48 (c_item col18, i64 masque classes)
--   quality_filter ← 1 << quality d'instance — filtre bitmask
--   item_name      ← maker + nom + "+N" — filtre LIKE natif
ALTER TABLE active_auctions
    ADD COLUMN IF NOT EXISTS item_type smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS level smallint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS class bigint NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS quality_filter smallint NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS item_name text NOT NULL DEFAULT '';
