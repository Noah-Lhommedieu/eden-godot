\connect edenfr_db
-- ============================================================================
-- 004-player-works.sql — ordres de travail (métiers 12-17)
--
-- Table native `player_works` (world : INSERT init de 11 lignes work_num
-- 0-10 par joueur ; UPDATE par CWork::SaveWork/ResetWork). Chaque ligne =
-- un lapis::CWork : métadonnées + 4 produits SWorkItem
-- (item/amount/socket_amount/curse/combo/extra_combo1-9).
--
-- + finish_at : colonne ajoutée par le port C# — le natif persistait
--   seulement l'enchant id de deadline (volatil, perdu au restart) ;
--   finish_at (epoch ms UTC) rend l'échéance durable pour les reconnects.
-- ============================================================================

CREATE TABLE IF NOT EXISTS "public"."player_works" (
    "player_id"                INTEGER      NOT NULL,
    "work_num"                 SMALLINT     NOT NULL,
    "work_type"                INTEGER      NOT NULL DEFAULT 0,
    "work_id"                  INTEGER      NOT NULL DEFAULT 0,
    "work_event"               INTEGER      NOT NULL DEFAULT 0,
    "failure_rate_of_decline"  INTEGER      NOT NULL DEFAULT 0,
    "bouns_item_amount"        INTEGER      NOT NULL DEFAULT 0,
    "building_item_id"         INTEGER      NOT NULL DEFAULT 0,
    "fm_name"                  VARCHAR(128) NOT NULL DEFAULT '',
    "fm_id"                    INTEGER      NOT NULL DEFAULT 0,
    "ext_success_rate"         SMALLINT     NOT NULL DEFAULT 0,
    "ext_building_rate"        SMALLINT     NOT NULL DEFAULT 0,
    "item1"                    INTEGER      NOT NULL DEFAULT 0,
    "amount1"                  INTEGER      NOT NULL DEFAULT 0,
    "socket_amount1"           INTEGER      NOT NULL DEFAULT 0,
    "combo1"                   INTEGER      NOT NULL DEFAULT 0,
    "curse1"                   INTEGER      NOT NULL DEFAULT 0,
    "extra_combo1_1"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo1_2"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo1_3"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo1_4"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo1_5"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo1_6"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo1_7"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo1_8"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo1_9"           INTEGER      NOT NULL DEFAULT 0,
    "item2"                    INTEGER      NOT NULL DEFAULT 0,
    "amount2"                  INTEGER      NOT NULL DEFAULT 0,
    "socket_amount2"           INTEGER      NOT NULL DEFAULT 0,
    "combo2"                   INTEGER      NOT NULL DEFAULT 0,
    "curse2"                   INTEGER      NOT NULL DEFAULT 0,
    "extra_combo2_1"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo2_2"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo2_3"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo2_4"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo2_5"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo2_6"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo2_7"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo2_8"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo2_9"           INTEGER      NOT NULL DEFAULT 0,
    "item3"                    INTEGER      NOT NULL DEFAULT 0,
    "amount3"                  INTEGER      NOT NULL DEFAULT 0,
    "socket_amount3"           INTEGER      NOT NULL DEFAULT 0,
    "combo3"                   INTEGER      NOT NULL DEFAULT 0,
    "curse3"                   INTEGER      NOT NULL DEFAULT 0,
    "extra_combo3_1"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo3_2"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo3_3"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo3_4"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo3_5"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo3_6"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo3_7"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo3_8"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo3_9"           INTEGER      NOT NULL DEFAULT 0,
    "item4"                    INTEGER      NOT NULL DEFAULT 0,
    "amount4"                  INTEGER      NOT NULL DEFAULT 0,
    "socket_amount4"           INTEGER      NOT NULL DEFAULT 0,
    "combo4"                   INTEGER      NOT NULL DEFAULT 0,
    "curse4"                   INTEGER      NOT NULL DEFAULT 0,
    "extra_combo4_1"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo4_2"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo4_3"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo4_4"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo4_5"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo4_6"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo4_7"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo4_8"           INTEGER      NOT NULL DEFAULT 0,
    "extra_combo4_9"           INTEGER      NOT NULL DEFAULT 0,
    -- Extension C# (absente du schéma natif) : échéance epoch ms UTC.
    "finish_at"                BIGINT       NOT NULL DEFAULT 0,
    CONSTRAINT "player_works_pkey" PRIMARY KEY ("player_id", "work_num")
);

CREATE INDEX IF NOT EXISTS "player_works_index"
    ON "public"."player_works" ("player_id", "work_num");

-- Quand `player_works` existe déjà (dump natif), le CREATE TABLE ci-dessus
-- est sauté : l'ALTER garantit `finish_at` dans les deux cas.
ALTER TABLE "public"."player_works"
    ADD COLUMN IF NOT EXISTS "finish_at" BIGINT NOT NULL DEFAULT 0;
