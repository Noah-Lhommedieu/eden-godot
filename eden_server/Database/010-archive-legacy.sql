-- 010-archive-legacy.sql — nettoyage des doublons réels.
-- Réversible : les tables sont déplacées dans un schéma `archive`,
-- jamais supprimées. Détails : docs/humain/BASE-DE-DONNEES.md.
--
-- Vrai doublon identifié : edenfr_account.fortune_bag = copie à encodage
-- corrompu (noms mojibake Big5) de edenfr_db.fortune_bag qui est
-- authoritative — le serveur C# la lit via _gameDb (World/
-- ZoneLinkServer.cs, SELECT natif PGAccountDBO).

\connect edenfr_account

CREATE SCHEMA IF NOT EXISTS archive;
COMMENT ON SCHEMA archive IS
  'Tables retirées du schéma public — doublons/données obsolètes gardées pour réversibilité';

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM information_schema.tables
               WHERE table_schema='public' AND table_name='fortune_bag') THEN
        ALTER TABLE fortune_bag SET SCHEMA archive;
    END IF;
END $$;
