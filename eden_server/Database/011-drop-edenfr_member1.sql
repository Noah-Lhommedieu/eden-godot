-- 011-drop-edenfr_member1.sql — supprime la base dupliquée edenfr_member1.
--
-- edenfr_member1 était un quasi-duplicata de edenfr_member (dump Aeria legacy :
-- billing + tables admin natives adminuser/admin_pro/ccu_log).
-- AUCUN service C# ne s'y connecte (MEMBER_DB_CONN = edenfr_member).
-- Backup conservé dans Database/backups/edenfr_member1.sql.

SELECT pg_terminate_backend(pid)
  FROM pg_stat_activity
 WHERE datname = 'edenfr_member1' AND pid <> pg_backend_pid();

DROP DATABASE IF EXISTS edenfr_member1;
