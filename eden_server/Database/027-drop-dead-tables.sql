-- 027-drop-dead-tables.sql — suppression des tables MORTES (PLAN-ACTION-3, lot DB-A, 2026-11-07).
-- Dépendances vérifiées (pg_depend : vues, clés étrangères) avant suppression.
-- Critère : aucune référence dans le code serveur/outils ET (0 ligne, ou données de journaux obsolètes).
-- Politique du propriétaire : une table morte est supprimée, pas gardée « au cas où ».
-- SAUVEGARDE : backups/dead-tables-2026-11-07.dump (pg_dump -Fc des 77 tables dont game.final_mission_state2, finalement CONSERVÉE : la vue final_mission_state_v2 en dépend, données comprises).
-- RESTAURATION : pg_restore -U postgres -d edenfr backups/dead-tables-2026-11-07.dump
-- Pas de CASCADE : si une clé étrangère existe encore, la migration échoue (rien n'est supprimé).
-- Base : edenfr (conteneur edengodot-postgres).

-- account_archive.fortune_bag (86 833 lignes d'historique) est CONSERVÉE : donnée volumineuse, décision du propriétaire.

BEGIN;
DROP TABLE IF EXISTS account.crashlog;
DROP TABLE IF EXISTS account.crashmonitor;
DROP TABLE IF EXISTS account.exchange_list;
DROP TABLE IF EXISTS account.exchange_pin;
DROP TABLE IF EXISTS account.exchange_rule;
DROP TABLE IF EXISTS account.fake_worlds;
DROP TABLE IF EXISTS account.gm_tool_accounts;
DROP TABLE IF EXISTS account.gm_tool_log;
DROP TABLE IF EXISTS account.gm_tool_log_old;
DROP TABLE IF EXISTS account.itemmall_old;
DROP TABLE IF EXISTS account.time_itemmall_log;
DROP TABLE IF EXISTS game.battle_royale_rank_old;
DROP TABLE IF EXISTS game.bind_gold_log;
DROP TABLE IF EXISTS game.class_lifelong_old;
DROP TABLE IF EXISTS game.classpk_rank_old;
DROP TABLE IF EXISTS game.colosseum_ranking_old;
DROP TABLE IF EXISTS game.elf_work;
DROP TABLE IF EXISTS game.farm;
DROP TABLE IF EXISTS game.farm_container;
DROP TABLE IF EXISTS game.farm_interaction;
DROP TABLE IF EXISTS game.farm_interaction_log;
DROP TABLE IF EXISTS game.farm_interaction_log_detail;
DROP TABLE IF EXISTS game.farm_message_board;
DROP TABLE IF EXISTS game.farm_votes;
DROP TABLE IF EXISTS game.gladiator_rank_old;
DROP TABLE IF EXISTS game.gold_log;
DROP TABLE IF EXISTS game.greedy_island_rank_old;
DROP TABLE IF EXISTS game.greedy_island_today;
DROP TABLE IF EXISTS game.illplugininfo;
DROP TABLE IF EXISTS game.init_errlog;
DROP TABLE IF EXISTS game.midnight_limit;
DROP TABLE IF EXISTS game.monster_kill_achievement_month;
DROP TABLE IF EXISTS game.new_player_guide_v2;
DROP TABLE IF EXISTS game.new_player_guide_v3;
DROP TABLE IF EXISTS game.node_stream;
DROP TABLE IF EXISTS game.pet_lottery;
DROP TABLE IF EXISTS game.pet_new_lottery;
DROP TABLE IF EXISTS game.player_achievement_rank_old;
DROP TABLE IF EXISTS game.player_characters_achievement1;
DROP TABLE IF EXISTS game.player_characters_achievement2;
DROP TABLE IF EXISTS game.player_room_old;
DROP TABLE IF EXISTS game.player_spellcards;
DROP TABLE IF EXISTS game.player_town_achievement_month;
DROP TABLE IF EXISTS game.rank_award_mail_queue;
DROP TABLE IF EXISTS game.statistics_monster;
DROP TABLE IF EXISTS game.statistics_player;
DROP TABLE IF EXISTS game.sys_mail_queue;
DROP TABLE IF EXISTS game.victory_declaration;
DROP TABLE IF EXISTS game.week_update;
DROP TABLE IF EXISTS game_archive.auction_history;
DROP TABLE IF EXISTS game_archive.character_stats;
DROP TABLE IF EXISTS game_archive.class_progression;
DROP TABLE IF EXISTS game_archive.class_spells;
DROP TABLE IF EXISTS game_archive.class_talents;
DROP TABLE IF EXISTS game_archive.family_bank;
DROP TABLE IF EXISTS game_archive.guild_members;
DROP TABLE IF EXISTS game_archive.item_mall_items;
DROP TABLE IF EXISTS game_archive.item_mall_purchases;
DROP TABLE IF EXISTS game_archive.player_bank;
DROP TABLE IF EXISTS game_archive.player_classes;
DROP TABLE IF EXISTS game_archive.player_coins;
DROP TABLE IF EXISTS game_archive.player_elfs;
DROP TABLE IF EXISTS game_archive.player_friends;
DROP TABLE IF EXISTS member.age_iteminsert_txnlog;
DROP TABLE IF EXISTS member.age_user_aggregate;
DROP TABLE IF EXISTS member.currentuser;
DROP TABLE IF EXISTS member.game_log;
DROP TABLE IF EXISTS member.gmip;
DROP TABLE IF EXISTS member.gmtool_log;
DROP TABLE IF EXISTS member.oauth2_error_log;
DROP TABLE IF EXISTS member.oauth2_itemmall_log;
DROP TABLE IF EXISTS member.oauth2_mapping;
DROP TABLE IF EXISTS member.powerupitem;
DROP TABLE IF EXISTS member.pus_avg;
DROP TABLE IF EXISTS member.two_pass;
DROP TABLE IF EXISTS member.web_itemmall_log;
DROP TABLE IF EXISTS game_archive.class_progression;
DROP TABLE IF EXISTS game_archive.class_spells;
DROP TABLE IF EXISTS game_archive.class_talents;
DROP TABLE IF EXISTS game_archive.family_bank;
DROP TABLE IF EXISTS game_archive.item_mall_items;
DROP TABLE IF EXISTS game_archive.player_classes;
DROP TABLE IF EXISTS game_archive.player_items;
DROP TABLE IF EXISTS game_archive.character_stats;
DROP TABLE IF EXISTS game_archive.guild_members;
DROP TABLE IF EXISTS game_archive.guilds;
DROP TABLE IF EXISTS game_archive.item_mall_purchases;
DROP TABLE IF EXISTS game_archive.player_bank;
DROP TABLE IF EXISTS game_archive.player_coins;
DROP TABLE IF EXISTS game_archive.player_elfs;
DROP TABLE IF EXISTS game_archive.player_friends;
DROP TABLE IF EXISTS game_archive.auction_history;
DROP SCHEMA IF EXISTS game_archive;
COMMIT;
