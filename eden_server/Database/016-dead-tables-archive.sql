-- 016-dead-tables-archive.sql — archivage des tables maison mortes.
-- Réversible : déplacement vers le schéma `archive`, jamais de DROP.
-- Audit : grep exhaustif de server/src (aucune requête Dapper/Npgsql
-- ne les touche) + comptes live ~0. Détails :
-- docs/humain/BASE-DE-DONNEES.md.
--
-- Remplacées par les tables natives du dump Aeria :
--   player_items        → inventory1/2
--   player_bank         → storage1/2
--   family_bank         → family_storage
--   player_friends      → friends
--   player_elfs         → elf / elf_container
--   player_classes      → player_classlist
--   class_*             → custom_class_data / player_spells / advanced_ability
--   characters (003)    → player_characters
--   guilds/guild_members→ family / family_cadre_members
--   player_mail maison  → player_mail native (corrigé dans 001)
-- Jamais créées / plus utilisées : le reste.

\connect edenfr_db

CREATE SCHEMA IF NOT EXISTS archive;
COMMENT ON SCHEMA archive IS
  'Tables retirées du schéma public — doublons/données obsolètes gardées pour réversibilité';

DO $$
DECLARE t text;
BEGIN
    FOREACH t IN ARRAY ARRAY[
        'guilds', 'guild_members', 'player_friends', 'player_elfs',
        'player_items', 'player_bank', 'family_bank', 'player_classes',
        'class_progression', 'class_spells', 'class_talents',
        'character_stats', 'characters', 'character_stats_cache',
        'character_playtime_log', 'auction_history', 'player_coins',
        'item_mall_items', 'item_mall_purchases', 'character_shortcuts']
    LOOP
        IF EXISTS (SELECT 1 FROM information_schema.tables
                   WHERE table_schema='public' AND table_name=t) THEN
            EXECUTE format('ALTER TABLE %I SET SCHEMA archive', t);
        END IF;
    END LOOP;
    -- Vue morte de la migration 003 (dépend de « characters »).
    IF EXISTS (SELECT 1 FROM information_schema.views
               WHERE table_schema='public'
                 AND table_name='active_characters') THEN
        EXECUTE 'ALTER VIEW active_characters SET SCHEMA archive';
    END IF;
END $$;

-- Documentation en base des tables natives clés (schéma item
-- partagé = mêmes colonnes d'instance, serial global item_serial).
COMMENT ON TABLE inventory1 IS 'Sac items (native) — moitié 1 du couple inventory1/2 ; container_index = bag, loc = slot';
COMMENT ON TABLE inventory2 IS 'Sac items (native) — moitié 2 ; mêmes colonnes qu inventory1';
COMMENT ON TABLE equipment1 IS 'Items équipés (native) — loc = slot équip., container_index = groupe de tenue';
COMMENT ON TABLE equipment2 IS 'Items équipés (native) — moitié 2';
COMMENT ON TABLE storage1 IS 'Banque perso (native) — moitié 1 du couple storage1/2';
COMMENT ON TABLE storage2 IS 'Banque perso (native) — moitié 2';
COMMENT ON TABLE bags IS 'Sacs additionnels équipés (native) — un sac = une ligne item portée en bag';
COMMENT ON TABLE mailitem IS 'Pièces jointes courrier (native) — items sérialisés attachés à player_mail';
COMMENT ON TABLE family_storage IS 'Banque de famille (native) — schéma item partagé, serials globaux';
COMMENT ON TABLE player_characters IS 'Personnages (native) — une ligne par perso ; colonnes maison : visit_family_id, cross_zone_*';
COMMENT ON TABLE player_classlist IS 'Classes débloquées par perso (native) — progression par classe';
COMMENT ON TABLE player_spells IS 'Sorts appris par perso (native)';
COMMENT ON TABLE player_mail IS 'Courrier (native) — receiver_id, send_time, due_date, opened, returned, authoritative';
COMMENT ON TABLE player_card IS 'Cartes joueur / infos publiques (native)';
COMMENT ON TABLE family IS 'Guildes/familles (native) — membres via family_cadre_members';
COMMENT ON TABLE friends IS 'Listes d amis (native)';
COMMENT ON TABLE elf IS 'Familiers invoqués (native) — contenu via elf_container, serials elf_serial';
COMMENT ON TABLE collection IS 'Archive/collection (native) — cartes déposées par perso + points';
