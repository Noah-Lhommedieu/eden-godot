-- 036 : audit d'administration chaîné (append-only) et secret TOTP par compte (PLAN-ACTION-3 D5/D8).
BEGIN;
CREATE SCHEMA IF NOT EXISTS admin;
COMMENT ON SCHEMA admin IS 'Administration : journal d''audit chaîné des commandes d''administration.';
CREATE TABLE admin.audit (
    audit_id     bigserial PRIMARY KEY,
    at           timestamptz NOT NULL,
    account_id   integer     NOT NULL,
    account_name text        NOT NULL,
    char_id      integer     NOT NULL,
    command      text        NOT NULL,
    args_json    text        NOT NULL,
    dry_run      boolean     NOT NULL,
    ok           boolean     NOT NULL,
    message      text        NOT NULL,
    request_id   text        NOT NULL,
    prev_hash    text        NOT NULL,
    hash         text        NOT NULL
);
CREATE UNIQUE INDEX audit_request_id ON admin.audit(request_id) WHERE request_id <> '';
COMMENT ON TABLE  admin.audit IS 'Journal append-only des commandes d''administration (acceptées ET refusées). Chaque hash couvre le précédent : modifier une ligne casse la chaîne.';
COMMENT ON COLUMN admin.audit.audit_id IS 'Numéro d''ordre (la chaîne suit cet ordre).';
COMMENT ON COLUMN admin.audit.at IS 'Date et heure de la commande.';
COMMENT ON COLUMN admin.audit.account_id IS 'Compte administrateur qui a lancé la commande.';
COMMENT ON COLUMN admin.audit.account_name IS 'Nom du compte au moment de la commande.';
COMMENT ON COLUMN admin.audit.char_id IS 'Personnage actif au moment de la commande (0 depuis le site).';
COMMENT ON COLUMN admin.audit.command IS 'Nom de la commande (give, remove, vip, approve…).';
COMMENT ON COLUMN admin.audit.args_json IS 'Arguments en JSON (sans le code TOTP).';
COMMENT ON COLUMN admin.audit.dry_run IS 'Vrai pour une simulation sans effet.';
COMMENT ON COLUMN admin.audit.ok IS 'Vrai si la commande a réussi.';
COMMENT ON COLUMN admin.audit.message IS 'Résultat ou motif du refus.';
COMMENT ON COLUMN admin.audit.request_id IS 'Identifiant d''idempotence fourni par l''appelant (vide = non rejouable) ; unique.';
COMMENT ON COLUMN admin.audit.prev_hash IS 'Empreinte de la ligne précédente (64 zéros pour la première).';
COMMENT ON COLUMN admin.audit.hash IS 'SHA-256 de prev_hash et du contenu de la ligne.';
ALTER TABLE account.accounts ADD COLUMN IF NOT EXISTS totp_secret text;
COMMENT ON COLUMN account.accounts.totp_secret IS 'Secret TOTP (base32) de la double authentification administrateur ; NULL = non configurée.';
COMMIT;
