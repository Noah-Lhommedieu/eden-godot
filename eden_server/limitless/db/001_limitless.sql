-- Limitless Level & Exp Ultime — chantier 3 (voir
-- docs/limitless/LIMITLESS_SYSTEM_DESIGN.md). Schéma fndb1.

CREATE TABLE IF NOT EXISTS player_limitless (
    player_id      INTEGER PRIMARY KEY REFERENCES player_characters(id),
    exp            BIGINT NOT NULL DEFAULT 0,
    level          INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS limitless_perks (
    id             SERIAL PRIMARY KEY,
    code           VARCHAR(64) NOT NULL UNIQUE,
    name           VARCHAR(128) NOT NULL,
    description    TEXT,
    required_level INTEGER NOT NULL,
    value          DOUBLE PRECISION NOT NULL,
    value_kind     VARCHAR(16) NOT NULL DEFAULT 'flat',
    enabled        BOOLEAN NOT NULL DEFAULT TRUE,
    sort_order     INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS player_limitless_perks (
    player_id INTEGER NOT NULL REFERENCES player_characters(id),
    perk_id   INTEGER NOT NULL REFERENCES limitless_perks(id),
    claimed_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (player_id, perk_id)
);

CREATE TABLE IF NOT EXISTS ll_level_cost (
    level   INTEGER PRIMARY KEY,
    exp_req BIGINT NOT NULL
);

INSERT INTO ll_level_cost(level, exp_req)
SELECT lvl, 100 * lvl * lvl FROM generate_series(1, 50) AS lvl
ON CONFLICT (level) DO NOTHING;

-- Perks d'exemple (modifiables/supprimables à chaud — le pass
-- recalcule les effets à chaque accès, le journal garde l'historique).
-- ATTENTION : `name` est affiché par le client via NCID 2033 — la
-- police CEGUI du jeu ne rend QUE l'ASCII 7 bits (un « × » ou un « é »
-- sort en mojibake). Libellés EN ASCII ; SanitizeLabel filet de
-- sécurité côté émission (docs/limitless/LIMITLESS_ENCODING.md).
INSERT INTO limitless_perks
    (code, name, description, required_level, value, value_kind, sort_order)
VALUES
    ('max_hp',        'Max HP +500',       '+500 PV maximum',                    1, 500,  'flat', 1),
    ('drop_rate',     'Drop Rate x2',      'Taux de drop doublé',                5, 2.0,  'mult', 2),
    ('exp_ultime_mult','Ultimate EXP x2',  'Points d''Exp Ultime doublés',       10, 2.0, 'mult', 3),
    ('move_speed_pct','Move Speed +10%',   '+10% vitesse de déplacement',        15, 10,  'percent', 4),
    ('cast_speed_pct','Casting time -10%', '-10% temps d''incantation',          20, 10,  'percent', 5)
ON CONFLICT (code) DO NOTHING;
