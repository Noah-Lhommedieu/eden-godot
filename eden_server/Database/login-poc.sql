-- ============================================================================
--  POC Login — compte admin/adminmdp + channel local joignable
--  Idempotent : rejouable sur un volume PostgreSQL existant.
--
--  edenfr_account.accounts  : authentification POC directe (le natif délègue au
--                        Gateway → account_login() sur edenfr_member.tb_user)
--  edenfr_member.tb_user    : chemin d'auth officiel — seedé pour la parité
--                        (byauthority=1 → droits GM natifs)
-- ============================================================================

\connect edenfr_account

-- Compte POC — mot de passe stocké en clair (le client envoie md5 ;
-- Eden.Login accepte les deux formes).
INSERT INTO accounts (id, username, password, realname, worldserver, state)
SELECT 2, 'admin', 'adminmdp', 'POC Admin', 1010, 0
WHERE NOT EXISTS (SELECT 1 FROM accounts WHERE username = 'admin');

UPDATE accounts SET password = 'adminmdp', state = 0 WHERE username = 'admin';

-- Channel 1010 joignable : on annonce l'IP LAN du host (marche aussi en
-- local — une machine peut joindre sa propre IP LAN). Le client se
-- connecte ensuite au WorldServer recodé (port 5200).
UPDATE worlds SET ip = '172.29.131.228', port = 5200, state = 1, online_user = 0 WHERE id = 1010;
INSERT INTO worlds (id, name, ip, port, online_user, maxnum_user, state, version, show_order)
SELECT 1010, 'Ch01', '172.29.131.228', 5200, 0, 150, 1, '002.003.05.67', 1
WHERE NOT EXISTS (SELECT 1 FROM worlds WHERE id = 1010);

-- Droits outil GM (gm_tool_accounts, schéma edenfr_account) — commandes GM.
INSERT INTO gm_tool_accounts (id, account_name, password, privilege)
SELECT 900001, 'admin', '17c725af38777cf3ac4bf34e39f44caa', 255
WHERE NOT EXISTS (SELECT 1 FROM gm_tool_accounts WHERE account_name = 'admin');

\connect edenfr_member

-- Entrée membre pour le chemin d'auth officiel (account_login).
-- pwd = md5('adminmdp') ; byauthority=1 → GM ; status=1 → actif.
INSERT INTO tb_user (mid, password, pwd, idnum, byauthority, pvalues, billingrule, status, regdate, admin, bonus, email)
SELECT 'admin', 'adminmdp', '17c725af38777cf3ac4bf34e39f44caa', 900001, 1, 0, 0, 1, NOW(), 1, 0, 'admin@eden.local'
WHERE NOT EXISTS (SELECT 1 FROM tb_user WHERE mid = 'admin');
