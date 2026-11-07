-- eden_content / 004 — récompenses d'objets des quêtes + source « quête » dans la vue d'obtention (SourceGraph, lot D2).
BEGIN;
CREATE TABLE quest.quest_reward (
    quest_id      integer NOT NULL,
    position      integer NOT NULL,
    item_id       integer NOT NULL,
    item_name_fr  text    NOT NULL,
    quantity      integer NOT NULL,
    is_choice     boolean NOT NULL,
    PRIMARY KEY (quest_id, position)
);
COMMENT ON TABLE  quest.quest_reward IS 'Objets donnés à la fin d''une quête (RewardItem) ou à choisir parmi plusieurs (RewardChooseItem).';
COMMENT ON COLUMN quest.quest_reward.quest_id IS 'Quête concernée (quest.quest.quest_id).';
COMMENT ON COLUMN quest.quest_reward.position IS 'Rang de la récompense dans le script de la quête.';
COMMENT ON COLUMN quest.quest_reward.item_id IS 'Objet donné (item.item.item_id).';
COMMENT ON COLUMN quest.quest_reward.item_name_fr IS 'Nom FR de l''objet (redondant).';
COMMENT ON COLUMN quest.quest_reward.quantity IS 'Quantité donnée.';
COMMENT ON COLUMN quest.quest_reward.is_choice IS 'Vrai si le joueur choisit UNE récompense parmi celles marquées ainsi.';

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
    FROM craft.recipe_material m JOIN craft.recipe r ON r.recipe_id = m.recipe_id
    UNION ALL
    SELECT w.item_id, 'quest', w.quest_id, q.name_fr, CASE WHEN w.is_choice THEN 'au choix, ' ELSE '' END || w.quantity || ' donné(s)'
    FROM quest.quest_reward w JOIN quest.quest q ON q.quest_id = w.quest_id;
COMMENT ON VIEW item.v_item_obtention IS 'Pour chaque objet : où il tombe (drop), où il se vend (shop), quelle recette le fabrique (recipe) ou le consomme (material), quelle quête le donne (quest).';
COMMIT;
