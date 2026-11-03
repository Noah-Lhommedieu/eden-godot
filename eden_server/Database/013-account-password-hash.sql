-- ============================================================================
--  013 — accounts.password_hash : migration Argon2id (rehash-at-login)
--
--  Ajoute la colonne PHC argon2id sur edenfr_account.accounts. La colonne
--  legacy `password` est CONSERVÉE (transition) : auth = Verify(PHC) si
--  password_hash rempli, sinon legacy (plaintext/md5) puis réécriture
--  du PHC post-login. Idempotent.
--
--  Le PHC couvre le credential wire canonique = md5(mdp) — la forme
--  envoyée par le client (CNC_CL_ServerLoginAccount.Field2).
-- ============================================================================

\connect edenfr_account

ALTER TABLE accounts
    ADD COLUMN IF NOT EXISTS password_hash varchar(255);

COMMENT ON COLUMN accounts.password_hash IS
    'PHC Argon2id ($argon2id$v=19$m=65536,t=3,p=1$sel$hash) du credential '
    'wire md5(mdp) — rempli par rehash-at-login / création de compte.';
