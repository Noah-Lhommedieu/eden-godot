-- 021-limitless.sql — tables du pass Limitless (docs/limitless/
-- LIMITLESS_SYSTEM_DESIGN.md). Absentes de la base live : leur
-- prefetch au pipeline d'entrée zone avortait TOUTE l'entrée
-- (relation does not exist → InstallEntry jamais appelé → plus de
-- 298/313/entités côté client).

-- Progression Limitless par personnage (1 ligne/perso).
CREATE TABLE IF NOT EXISTS player_limitless (
    player_id      INTEGER PRIMARY KEY REFERENCES player_characters(id),
    exp            BIGINT NOT NULL DEFAULT 0,   -- total Exp Ultime
    level          INTEGER NOT NULL DEFAULT 0   -- Limitless Level (cache)
);

-- Définitions des perks du pass (modifiables à chaud).
CREATE TABLE IF NOT EXISTS limitless_perks (
    id             SERIAL PRIMARY KEY,
    code           VARCHAR(64) NOT NULL UNIQUE, -- ex 'drop_rate_mult'
    name           VARCHAR(128) NOT NULL,
    description    TEXT,
    required_level INTEGER NOT NULL,            -- palier Limitless requis
    value          DOUBLE PRECISION NOT NULL,   -- magnitude (x2 → 2.0, -20% → -0.2)
    value_kind     VARCHAR(16) NOT NULL,        -- 'mult'|'flat'|'percent'
    enabled        BOOLEAN NOT NULL DEFAULT TRUE,
    sort_order     INTEGER NOT NULL DEFAULT 0
);

-- Perks réellement débloqués par perso (journal — les buffs actifs se
-- recalculent depuis limitless_perks, pas depuis ce journal).
CREATE TABLE IF NOT EXISTS player_limitless_perks (
    player_id INTEGER NOT NULL REFERENCES player_characters(id),
    perk_id   INTEGER NOT NULL REFERENCES limitless_perks(id),
    claimed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (player_id, perk_id)
);

-- Courbe configurable : coût cumulé d'Exp Ultime par Limitless Lv.
CREATE TABLE IF NOT EXISTS ll_level_cost (
    level   INTEGER PRIMARY KEY,
    exp_req BIGINT NOT NULL        -- Exp Ultime cumulée pour ce niveau
);

-- Courbe par défaut (design doc : 100·lvl², niveaux 1-50).
INSERT INTO ll_level_cost(level, exp_req)
SELECT lvl, 100::BIGINT * lvl * lvl FROM generate_series(1, 50) AS lvl
ON CONFLICT (level) DO NOTHING;
