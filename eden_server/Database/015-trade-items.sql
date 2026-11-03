\connect edenfr_db
-- 015 — trade_items : contenu des fenêtres d'échange persisté.
-- Avant, InitiatorItems/PartnerItems de TradeEntity n'existaient qu'en
-- mémoire sur l'instance chargée : chaque handler rechargeait une entité
-- fraîche → listes vides au settle → items réservés (débités de
-- l'inventaire par ReserveItemAsync) jamais livrés = PERTE d'item
-- (constaté live : active_trades consommé, inventaire débité, rien
-- livré). Les lignes sont écrites dans la même transaction que la
-- réservation (ReserveItemAsync) et purgées au settle/cancel/delete.
CREATE TABLE IF NOT EXISTS trade_items (
    trade_id   integer NOT NULL,
    player_id  integer NOT NULL,
    item_id    integer NOT NULL,
    quantity   integer NOT NULL,
    slot       integer NOT NULL DEFAULT 0
);
CREATE INDEX IF NOT EXISTS trade_items_trade_idx
    ON trade_items(trade_id);
