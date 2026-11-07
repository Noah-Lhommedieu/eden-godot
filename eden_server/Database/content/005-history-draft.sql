-- eden_content / 005 — brouillons d'édition : on prépare des modifications, on voit le diff, puis on les publie en une release (annulable en bloc).
BEGIN;
CREATE TABLE history.draft (
    draft_id    bigserial PRIMARY KEY,
    entity_type text        NOT NULL,
    entity_id   integer     NOT NULL,
    field       text        NOT NULL,
    new_value   text        NOT NULL,
    created_by  text        NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    reason      text,
    release_id  text
);
COMMENT ON TABLE  history.draft IS 'Modifications préparées mais pas encore publiées. Publier = les appliquer en une release (history.change.release_id).';
COMMENT ON COLUMN history.draft.draft_id IS 'Numéro du brouillon.';
COMMENT ON COLUMN history.draft.entity_type IS 'Type de l''entité visée (item, monster, npc, quest, map, recipe).';
COMMENT ON COLUMN history.draft.entity_id IS 'Identifiant métier de l''entité visée.';
COMMENT ON COLUMN history.draft.field IS 'Colonne à modifier (liste blanche par type).';
COMMENT ON COLUMN history.draft.new_value IS 'Valeur proposée (texte).';
COMMENT ON COLUMN history.draft.created_by IS 'Compte administrateur auteur du brouillon.';
COMMENT ON COLUMN history.draft.created_at IS 'Date de création du brouillon.';
COMMENT ON COLUMN history.draft.reason IS 'Motif saisi par l''administrateur.';
COMMENT ON COLUMN history.draft.release_id IS 'Release qui a publié ce brouillon ; NULL tant qu''il est en attente.';
COMMIT;
