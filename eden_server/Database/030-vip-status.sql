-- 030-vip-status.sql — statut VIP par compte (PLAN-ACTION-3 F4) : un simple statut, pas de paliers.
-- Les bonus eux-mêmes (−25 % temps, +10 % EXP…) sont des données du moteur de modificateurs (VipModifierSource).
BEGIN;
CREATE TABLE IF NOT EXISTS account.vip_status (
    account_id integer PRIMARY KEY REFERENCES account.accounts(id) ON DELETE CASCADE,
    vip_since  timestamptz NOT NULL DEFAULT now(),
    vip_until  timestamptz,
    granted_by text,
    note       text
);
COMMENT ON TABLE  account.vip_status IS 'Comptes VIP : une ligne = compte VIP tant que vip_until est NULL (permanent) ou dans le futur. Supprimer la ligne retire le VIP.';
COMMENT ON COLUMN account.vip_status.account_id IS 'Compte concerné (account.accounts.id).';
COMMENT ON COLUMN account.vip_status.vip_since  IS 'Début du statut VIP.';
COMMENT ON COLUMN account.vip_status.vip_until  IS 'Fin du statut ; NULL = permanent.';
COMMENT ON COLUMN account.vip_status.granted_by IS 'Compte administrateur ou source (commande, site) qui a accordé le VIP.';
COMMENT ON COLUMN account.vip_status.note       IS 'Motif libre (achat, cadeau, événement).';
COMMIT;
