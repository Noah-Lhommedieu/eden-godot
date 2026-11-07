-- eden_content / 006 — traductions (PLAN-ACTION-3 lot E). Le FR (colonnes name_fr) reste la SEULE vérité ; l'anglais est une couche
-- dérivée, supprimable sans toucher une seule valeur FR (voir tools/content/rollback-fr-only.sh).
BEGIN;
CREATE SCHEMA IF NOT EXISTS text;
COMMENT ON SCHEMA text IS 'Traductions et glossaire. Aucune table FR ni aucun identifiant n''en dépend.';

CREATE TABLE text.translation (
    entity_type   text        NOT NULL,
    entity_id     integer     NOT NULL,
    field         text        NOT NULL,
    lang          text        NOT NULL,
    value         text        NOT NULL,
    status        text        NOT NULL DEFAULT 'auto' CHECK (status IN ('auto','reviewed','validated')),
    fr_hash       text        NOT NULL,
    translated_at timestamptz NOT NULL DEFAULT now(),
    reviewed_by   text,
    PRIMARY KEY (entity_type, entity_id, field, lang)
);
COMMENT ON TABLE  text.translation IS 'Une traduction = (type d''entité, id, champ, langue). Une ligne absente = « missing » (repli FR). Voir text.v_translation_status.';
COMMENT ON COLUMN text.translation.entity_type IS 'Type d''entité traduite : item, monster, npc, quest, map.';
COMMENT ON COLUMN text.translation.entity_id IS 'Identifiant métier de l''entité (item_id, monster_id…).';
COMMENT ON COLUMN text.translation.field IS 'Champ traduit (name pour name_fr).';
COMMENT ON COLUMN text.translation.lang IS 'Code de langue (en).';
COMMENT ON COLUMN text.translation.value IS 'Texte traduit, UTF-8, mêmes codes de mise en forme que le FR.';
COMMENT ON COLUMN text.translation.status IS 'auto = produit par traduction assistée, reviewed = relu, validated = validé par un humain.';
COMMENT ON COLUMN text.translation.fr_hash IS 'Empreinte (16 hex de SHA-256) du texte FR traduit ; si le FR change, la ligne devient « outdated ».';
COMMENT ON COLUMN text.translation.translated_at IS 'Date de la traduction.';
COMMENT ON COLUMN text.translation.reviewed_by IS 'Compte du relecteur, NULL tant que non relu.';

CREATE TABLE text.glossary (
    fr_term     text NOT NULL,
    lang        text NOT NULL,
    term        text NOT NULL,
    note        text,
    PRIMARY KEY (fr_term, lang)
);
COMMENT ON TABLE  text.glossary IS 'Termes de jeu imposés dans toutes les traductions (noms de ressources, bâtiments, statuts…) pour rester cohérents.';
COMMENT ON COLUMN text.glossary.fr_term IS 'Terme français tel qu''il apparaît dans le contenu.';
COMMENT ON COLUMN text.glossary.lang IS 'Code de langue cible.';
COMMENT ON COLUMN text.glossary.term IS 'Terme imposé dans la langue cible.';
COMMENT ON COLUMN text.glossary.note IS 'Précision d''usage (facultatif).';

CREATE OR REPLACE VIEW text.v_translation_status AS
    WITH fr AS (
        SELECT 'item'::text AS entity_type, item_id AS entity_id, name_fr, in_scope FROM item.item
        UNION ALL SELECT 'monster', monster_id, name_fr, in_scope FROM creature.monster
        UNION ALL SELECT 'npc', npc_id, name_fr, in_scope FROM npc.npc
        UNION ALL SELECT 'quest', quest_id, name_fr, in_scope FROM quest.quest
        UNION ALL SELECT 'map', node_id, name_fr, in_scope FROM map.map)
    SELECT fr.entity_type, fr.entity_id, fr.name_fr,
           t.value AS name_en,
           CASE WHEN t.value IS NULL THEN 'missing'
                WHEN t.fr_hash <> left(encode(sha256(convert_to(fr.name_fr, 'UTF8')), 'hex'), 16) THEN 'outdated'
                ELSE t.status END AS status
    FROM fr LEFT JOIN text.translation t
      ON t.entity_type = fr.entity_type AND t.entity_id = fr.entity_id AND t.field = 'name' AND t.lang = 'en'
    WHERE fr.in_scope;
COMMENT ON VIEW text.v_translation_status IS 'État de traduction EN de chaque nom du contenu présent en jeu ≤ Lv90 : missing, outdated (FR modifié depuis), auto, reviewed, validated.';
COMMIT;
