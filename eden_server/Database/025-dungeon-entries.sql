-- Migration 025 : entrées journalières de donjon (par node, Solo=7xx/8xx,
-- Multi=1xx/2xx). `day` = jours locaux depuis 1970-01-01 ; un `day` ancien
-- vaut compteur remis à zéro. Idempotent.
CREATE TABLE IF NOT EXISTS game.player_dungeon_entries (
    player_id integer NOT NULL,
    node_id   integer NOT NULL,
    day       integer NOT NULL,
    times     integer NOT NULL DEFAULT 0,
    PRIMARY KEY (player_id, node_id)
);
