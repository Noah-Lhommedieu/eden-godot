\connect edenfr_db
-- 007 — Panneau d'affichage (message board).
-- Table native (CExecutionerDBO::SaveMessage mission @0x813e5xx) :
--   INSERT INTO message_board (message_id, title, content, player_name,
--   create_date) — id alloué par CUIDAllocator::MessageIDAllocator
--   (max+1), create_date = unix time. Le natif purge le post sitôt
--   écrit (ZoneServerMessageWriteResult → "MessageDelete id = %d") —
--   feature bridée, mais la table sert aux relais MessageRead/
--   ListQuery/Delete. Schéma aligné sur la DB live (text + no PK).
CREATE TABLE IF NOT EXISTS "public"."message_board" (
    message_id  integer NOT NULL,
    title       text    NOT NULL,
    content     text    NOT NULL,
    player_name text    NOT NULL,
    create_date integer NOT NULL
);

COMMENT ON TABLE message_board IS
    'Panneau d''affichage global (table native, cap ~100 posts)';
