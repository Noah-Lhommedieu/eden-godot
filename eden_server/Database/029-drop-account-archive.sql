-- 029-drop-account-archive.sql — suppression de account_archive.fortune_bag (décision du propriétaire, 2026-11-07).
-- Vérifié : mêmes 86 833 lignes que game.fortune_bag sur toutes les colonnes sauf `note`, dont le texte
-- était un mojibake (octets Big5 lus en Latin-1) ; game.fortune_bag porte le texte correct.
-- SAUVEGARDE : backups/account-archive-fortune-bag-2026-11-07.dump (pg_restore -U postgres -d edenfr …).
BEGIN;
DROP TABLE account_archive.fortune_bag;
DROP SCHEMA account_archive;
COMMIT;
