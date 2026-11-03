\connect edenfr_db
-- 014 — player_mail.item_id : smallint → integer.
-- Les item_id natifs dépassent 32767 (ex. 32034 ok, 40000+ échec 22003) —
-- le règlement d'achat d'enchères plantait silencieusement en insérant le
-- mail de livraison (listing perdu en mémoire, resté 'active' en base).
ALTER TABLE player_mail ALTER COLUMN item_id TYPE integer;
