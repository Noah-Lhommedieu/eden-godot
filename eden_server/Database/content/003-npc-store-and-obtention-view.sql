-- eden_content / 003 — lien PNJ → boutique et vue de lecture « comment obtenir cet objet ».
BEGIN;
ALTER TABLE npc.npc ADD COLUMN IF NOT EXISTS store_id integer NOT NULL DEFAULT 0;
COMMENT ON COLUMN npc.npc.store_id IS 'Boutique vendue par ce PNJ (npc.shop_item.store_id) ; 0 si le PNJ ne vend rien.';

CREATE OR REPLACE VIEW item.v_item_obtention AS
    SELECT d.item_id, 'drop'::text AS source_kind, d.monster_id AS source_id, d.monster_name_fr AS source_name_fr,
           d.pool || ' ' || d.chance_percent || ' %' AS detail
    FROM creature.monster_drop d
    UNION ALL
    SELECT s.item_id, 'shop', n.npc_id, n.name_fr, 'boutique ' || s.store_id
    FROM npc.shop_item s JOIN npc.npc n ON n.store_id = s.store_id
    UNION ALL
    SELECT r.product_item_id, 'recipe', r.recipe_id, 'Recette ' || r.recipe_id,
           CASE WHEN r.plan_name_fr IS NULL THEN 'sans plan' ELSE 'plan : ' || r.plan_name_fr END
    FROM craft.recipe r
    UNION ALL
    SELECT m.item_id, 'material', m.recipe_id, 'Matériau de ' || r.product_name_fr, m.quantity || ' requis'
    FROM craft.recipe_material m JOIN craft.recipe r ON r.recipe_id = m.recipe_id;
COMMENT ON VIEW item.v_item_obtention IS 'Pour chaque objet : où il tombe (drop), où il se vend (shop), quelle recette le fabrique (recipe) ou le consomme (material).';
COMMIT;
