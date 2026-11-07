-- eden_content / 002 — recettes de fabrication, drops de monstres et boutiques de PNJ (relations lisibles :
-- chaque identifiant porte à côté son libellé FR redondant, maintenu par le seed).
BEGIN;
CREATE SCHEMA IF NOT EXISTS craft; COMMENT ON SCHEMA craft IS 'Recettes de fabrication de guilde (familycombine) et leurs matériaux.';

CREATE TABLE craft.recipe (
    recipe_id        integer PRIMARY KEY,
    category         integer NOT NULL,
    building_level   integer NOT NULL,
    duration_units   integer NOT NULL,
    gold_silver      integer NOT NULL,
    product_item_id  integer NOT NULL,
    product_name_fr  text    NOT NULL,
    product_quantity integer NOT NULL,
    success_percent  integer NOT NULL,
    curse_percent    integer NOT NULL,
    plan_item_id     integer,
    plan_name_fr     text
);
COMMENT ON TABLE  craft.recipe IS 'Une recette de fabrication de guilde (c_familycombine, 3 178 recettes).';
COMMENT ON COLUMN craft.recipe.recipe_id IS 'Identifiant natif de la recette (clé métier).';
COMMENT ON COLUMN craft.recipe.category IS 'Catégorie native : 3-5 recettes à plan, 6 fragments de carte, 7 divers.';
COMMENT ON COLUMN craft.recipe.building_level IS 'Niveau minimal du bâtiment de fabrication.';
COMMENT ON COLUMN craft.recipe.duration_units IS 'Durée brute native (non instantanée : × 600 unités de 100 ms ; avant VIP/config).';
COMMENT ON COLUMN craft.recipe.gold_silver IS 'Coût en pièces d''argent.';
COMMENT ON COLUMN craft.recipe.product_item_id IS 'Objet produit (item.item.item_id).';
COMMENT ON COLUMN craft.recipe.product_name_fr IS 'Nom FR de l''objet produit (redondant, lisible seul).';
COMMENT ON COLUMN craft.recipe.product_quantity IS 'Quantité produite.';
COMMENT ON COLUMN craft.recipe.success_percent IS 'Chance de réussite native, en pourcents.';
COMMENT ON COLUMN craft.recipe.curse_percent IS 'Chance d''échec maudit native, en pourcents.';
COMMENT ON COLUMN craft.recipe.plan_item_id IS 'Objet plan (formule) de la recette : premier matériau de genre 53-56 (règle CraftPlanRules) ; NULL sans plan.';
COMMENT ON COLUMN craft.recipe.plan_name_fr IS 'Nom FR du plan (redondant) ; NULL sans plan.';

CREATE TABLE craft.recipe_material (
    recipe_id    integer NOT NULL,
    position     integer NOT NULL,
    item_id      integer NOT NULL,
    item_name_fr text    NOT NULL,
    quantity     integer NOT NULL,
    PRIMARY KEY (recipe_id, position)
);
COMMENT ON TABLE  craft.recipe_material IS 'Matériaux d''une recette, dans l''ordre natif (le plan éventuel est en position 0).';
COMMENT ON COLUMN craft.recipe_material.recipe_id IS 'Recette concernée (craft.recipe.recipe_id).';
COMMENT ON COLUMN craft.recipe_material.position IS 'Rang du matériau dans la recette (0 = premier).';
COMMENT ON COLUMN craft.recipe_material.item_id IS 'Objet requis (item.item.item_id).';
COMMENT ON COLUMN craft.recipe_material.item_name_fr IS 'Nom FR de l''objet requis (redondant).';
COMMENT ON COLUMN craft.recipe_material.quantity IS 'Quantité requise.';

CREATE TABLE creature.monster_drop (
    monster_id      integer NOT NULL,
    monster_name_fr text    NOT NULL,
    pool            text    NOT NULL,
    position        integer NOT NULL,
    item_id         integer NOT NULL,
    item_name_fr    text    NOT NULL,
    quantity        integer NOT NULL,
    chance_percent  real    NOT NULL,
    PRIMARY KEY (monster_id, pool, position)
);
COMMENT ON TABLE  creature.monster_drop IS 'Objets que lâche un monstre (S_DropItem).';
COMMENT ON COLUMN creature.monster_drop.monster_id IS 'Monstre ou groupe de drop (creature.monster.monster_id).';
COMMENT ON COLUMN creature.monster_drop.monster_name_fr IS 'Nom FR du monstre (redondant) ; vide si le groupe n''est pas un monstre connu.';
COMMENT ON COLUMN creature.monster_drop.pool IS 'normal (tirage à chaque mort) ou break (tiré en plus quand la patience du mob est brisée).';
COMMENT ON COLUMN creature.monster_drop.position IS 'Rang de la ligne dans le pool natif.';
COMMENT ON COLUMN creature.monster_drop.item_id IS 'Objet lâché (item.item.item_id).';
COMMENT ON COLUMN creature.monster_drop.item_name_fr IS 'Nom FR de l''objet (redondant).';
COMMENT ON COLUMN creature.monster_drop.quantity IS 'Quantité lâchée.';
COMMENT ON COLUMN creature.monster_drop.chance_percent IS 'Chance native pondérée, en pourcents (avant DropRate et événements).';

CREATE TABLE npc.shop_item (
    store_id     integer NOT NULL,
    position     integer NOT NULL,
    item_id      integer NOT NULL,
    item_name_fr text    NOT NULL,
    stock_count  integer NOT NULL,
    PRIMARY KEY (store_id, position)
);
COMMENT ON TABLE  npc.shop_item IS 'Contenu des boutiques de PNJ (S_Store) ; un PNJ vend via son store_id (service de type 3).';
COMMENT ON COLUMN npc.shop_item.store_id IS 'Identifiant natif de la boutique.';
COMMENT ON COLUMN npc.shop_item.position IS 'Rang de l''objet dans la boutique.';
COMMENT ON COLUMN npc.shop_item.item_id IS 'Objet vendu (item.item.item_id).';
COMMENT ON COLUMN npc.shop_item.item_name_fr IS 'Nom FR de l''objet (redondant).';
COMMENT ON COLUMN npc.shop_item.stock_count IS 'Stock natif (0 = illimité).';
COMMIT;
