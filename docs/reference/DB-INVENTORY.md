# Inventaire des tables (généré par tools/db/inventory.py)

Base `edenfr` — 108 tables.

- à supprimer : **0** ; à examiner : **2** ; à garder : **106**
- tables commentées : 43/108 ; colonnes commentées : 29/1773

| schéma | table | lignes | octets | réf. code | commentée | col. comm. | décision |
|---|---|---|---|---|---|---|---|
| account | accounts | 5 | 81920 | 24 | oui | 4/13 | GARDER (renommer/commenter) |
| account | character_number | 4 | 40960 | 11 | non | 0/3 | GARDER (renommer/commenter) |
| account | configuration | 1 | 24576 | 2 | non | 0/2 | GARDER (renommer/commenter) |
| account | ingame_lottery | 294 | 81920 | 4 | non | 0/9 | GARDER (renommer/commenter) |
| account | item_receipt | 3 | 73728 | 9 | non | 0/14 | GARDER (renommer/commenter) |
| account | item_receivable | 1 | 73728 | 16 | non | 0/13 | GARDER (renommer/commenter) |
| account | itemmall | 515 | 122880 | 6 | oui | 0/16 | GARDER (renommer/commenter) |
| account | itemmall_limit_amount | 2 | 40960 | 6 | non | 0/7 | GARDER (renommer/commenter) |
| account | lottery | 294 | 81920 | 30 | non | 0/9 | GARDER (renommer/commenter) |
| account | new_lottery | 320 | 81920 | 12 | non | 0/9 | GARDER (renommer/commenter) |
| account | starway | 11 | 24576 | 4 | non | 0/14 | GARDER (renommer/commenter) |
| account | worlds | 2 | 24576 | 18 | oui | 0/9 | GARDER (renommer/commenter) |
| game | active_auctions | 4 | 32768 | 12 | oui | 1/14 | GARDER (renommer/commenter) |
| game | active_trades | 0 | 40960 | 13 | oui | 2/8 | GARDER (renommer/commenter) |
| game | advanced_ability | 4 | 24576 | 16 | non | 0/3 | GARDER (renommer/commenter) |
| game | auction | 0 | 73728 | 23 | non | 0/60 | GARDER (renommer/commenter) |
| game | bags | 5 | 81920 | 49 | non | 0/53 | GARDER (renommer/commenter) |
| game | battle_royale_rank | 0 | 8192 | 4 | non | 0/3 | GARDER (renommer/commenter) |
| game | battlefield_career | 7 | 32768 | 6 | non | 0/16 | GARDER (renommer/commenter) |
| game | battlefield_month | 7 | 32768 | 8 | non | 0/18 | GARDER (renommer/commenter) |
| game | class_lifelong | 12 | 57344 | 8 | non | 0/6 | GARDER (renommer/commenter) |
| game | classpk_rank | 0 | 8192 | 4 | non | 0/4 | GARDER (renommer/commenter) |
| game | collection | 0 | 24576 | 31 | oui | 0/51 | GARDER (renommer/commenter) |
| game | colosseum_ranking | 0 | 8192 | 2 | oui | 0/4 | A EXAMINER (vide, peu référencée) : fusionner ou supprimer |
| game | configuration | 1 | 24576 | 2 | oui | 0/3 | GARDER (renommer/commenter) |
| game | custom_class_data | 21 | 24576 | 9 | non | 0/12 | GARDER (renommer/commenter) |
| game | daily_awards | 8 | 57344 | 13 | oui | 0/7 | GARDER (renommer/commenter) |
| game | daily_awards_set | 1 | 8192 | 6 | non | 0/4 | GARDER (renommer/commenter) |
| game | elf | 0 | 24576 | 427 | oui | 0/24 | GARDER (renommer/commenter) |
| game | elf_container | 0 | 32768 | 18 | non | 0/54 | GARDER (renommer/commenter) |
| game | equipment1 | 20 | 57344 | 28 | oui | 0/53 | GARDER (renommer/commenter) |
| game | equipment2 | 82 | 98304 | 18 | oui | 0/53 | GARDER (renommer/commenter) |
| game | family | 9 | 65536 | 132 | oui | 0/50 | GARDER (renommer/commenter) |
| game | family_achievement | 3 | 73728 | 12 | oui | 0/3 | GARDER (renommer/commenter) |
| game | family_achievement_log | 0 | 8192 | 3 | non | 0/6 | GARDER (renommer/commenter) |
| game | family_achievement_rank | 1 | 65536 | 11 | non | 0/22 | GARDER (renommer/commenter) |
| game | family_building_container | 44 | 131072 | 19 | oui | 14/14 | GARDER (renommer/commenter) |
| game | family_cadre_members | 0 | 16384 | 20 | non | 0/4 | GARDER (renommer/commenter) |
| game | family_emblem | 0 | 16384 | 9 | non | 0/3 | GARDER (renommer/commenter) |
| game | family_log | 9 | 65536 | 13 | oui | 0/8 | GARDER (renommer/commenter) |
| game | family_msgboard | 0 | 32768 | 11 | non | 0/6 | GARDER (renommer/commenter) |
| game | family_schedule | 0 | 16384 | 3 | non | 0/6 | GARDER (renommer/commenter) |
| game | family_storage | 0 | 49152 | 27 | oui | 0/51 | GARDER (renommer/commenter) |
| game | family_storage_log | 4 | 65536 | 7 | oui | 0/4 | GARDER (renommer/commenter) |
| game | familybattle_ranks | 0 | 8192 | 6 | non | 0/6 | GARDER (renommer/commenter) |
| game | fast_dress | 504 | 98304 | 21 | oui | 0/8 | GARDER (renommer/commenter) |
| game | final_mission_state1 | 117 | 57344 | 3 | oui | 0/3 | GARDER (renommer/commenter) |
| game | final_mission_state2 | 0 | 8192 | 1 | non | 0/3 | A EXAMINER (vide, peu référencée) : fusionner ou supprimer |
| game | fortune_bag | 86833 | 8691712 | 14 | oui | 0/11 | GARDER (renommer/commenter) |
| game | friends | 0 | 24576 | 19 | oui | 0/3 | GARDER (renommer/commenter) |
| game | gladiator | 0 | 16384 | 11 | non | 0/13 | GARDER (renommer/commenter) |
| game | greedy_island_rank | 0 | 8192 | 3 | non | 0/3 | GARDER (renommer/commenter) |
| game | greedy_island_yesterday | 0 | 8192 | 3 | non | 0/3 | GARDER (renommer/commenter) |
| game | in_game_lottery | 1 | 8192 | 7 | non | 0/16 | GARDER (renommer/commenter) |
| game | inventory1 | 150 | 131072 | 100 | oui | 0/53 | GARDER (renommer/commenter) |
| game | inventory2 | 27 | 90112 | 40 | oui | 0/53 | GARDER (renommer/commenter) |
| game | inventory_slots | 2 | 24576 | 19 | oui | 4/11 | GARDER (renommer/commenter) |
| game | limitless_perks | 0 | 24576 | 6 | non | 0/9 | GARDER (renommer/commenter) |
| game | ll_level_cost | 50 | 24576 | 4 | non | 0/2 | GARDER (renommer/commenter) |
| game | login_tickets | 2 | 24576 | 8 | oui | 0/8 | GARDER (renommer/commenter) |
| game | lottery | 1 | 40960 | 30 | non | 0/16 | GARDER (renommer/commenter) |
| game | lover | 0 | 16384 | 16 | non | 0/7 | GARDER (renommer/commenter) |
| game | mailitem | 4 | 65536 | 41 | oui | 0/53 | GARDER (renommer/commenter) |
| game | message_board | 0 | 8192 | 13 | oui | 0/5 | GARDER (renommer/commenter) |
| game | mission_trace | 7 | 57344 | 27 | oui | 0/11 | GARDER (renommer/commenter) |
| game | new_lottery | 1 | 24576 | 12 | non | 0/15 | GARDER (renommer/commenter) |
| game | new_player_guide | 2 | 24576 | 12 | non | 0/41 | GARDER (renommer/commenter) |
| game | personal_mission_states | 45 | 57344 | 27 | oui | 0/3 | GARDER (renommer/commenter) |
| game | player_achievement_rank | 1 | 32768 | 10 | non | 0/25 | GARDER (renommer/commenter) |
| game | player_appellation | 6 | 24576 | 8 | oui | 0/2 | GARDER (renommer/commenter) |
| game | player_card | 9 | 49152 | 18 | oui | 0/15 | GARDER (renommer/commenter) |
| game | player_characters | 39 | 253952 | 242 | oui | 0/110 | GARDER (renommer/commenter) |
| game | player_characters_achievement | 17 | 24576 | 7 | non | 0/4 | GARDER (renommer/commenter) |
| game | player_classlist | 69 | 57344 | 51 | oui | 0/14 | GARDER (renommer/commenter) |
| game | player_closet | 0 | 8192 | 12 | non | 0/6 | GARDER (renommer/commenter) |
| game | player_couples | 0 | 24576 | 8 | oui | 0/6 | GARDER (renommer/commenter) |
| game | player_destiny_road | 0 | 8192 | 10 | non | 0/47 | GARDER (renommer/commenter) |
| game | player_dungeon_entries | 0 | 24576 | 4 | oui | 4/4 | GARDER (renommer/commenter) |
| game | player_enchants | 17 | 204800 | 39 | oui | 0/10 | GARDER (renommer/commenter) |
| game | player_evaluate | 1 | 32768 | 8 | non | 0/5 | GARDER (renommer/commenter) |
| game | player_fortune_bag | 0 | 8192 | 14 | non | 0/3 | GARDER (renommer/commenter) |
| game | player_license | 20 | 24576 | 29 | non | 0/3 | GARDER (renommer/commenter) |
| game | player_life_skill | 56 | 57344 | 12 | non | 0/4 | GARDER (renommer/commenter) |
| game | player_limitless | 0 | 8192 | 5 | non | 0/3 | GARDER (renommer/commenter) |
| game | player_limitless_perks | 0 | 8192 | 3 | non | 0/3 | GARDER (renommer/commenter) |
| game | player_lottery_point | 1 | 24576 | 14 | non | 0/5 | GARDER (renommer/commenter) |
| game | player_mail | 4 | 65536 | 33 | oui | 0/15 | GARDER (renommer/commenter) |
| game | player_mission_complete_num | 69 | 24576 | 16 | non | 0/3 | GARDER (renommer/commenter) |
| game | player_node_times | 4 | 24576 | 10 | non | 0/3 | GARDER (renommer/commenter) |
| game | player_room | 0 | 16384 | 14 | non | 0/14 | GARDER (renommer/commenter) |
| game | player_room_container | 0 | 32768 | 7 | non | 0/53 | GARDER (renommer/commenter) |
| game | player_spells | 1089 | 147456 | 30 | oui | 0/4 | GARDER (renommer/commenter) |
| game | player_works | 77 | 106496 | 18 | non | 0/69 | GARDER (renommer/commenter) |
| game | prestige | 5 | 57344 | 66 | non | 0/4 | GARDER (renommer/commenter) |
| game | recommend_activities | 244 | 90112 | 14 | non | 0/5 | GARDER (renommer/commenter) |
| game | serverstatus | 3 | 32768 | 1 | non | 0/7 | GARDER (renommer/commenter) |
| game | shortcut_menu | 8 | 24576 | 11 | oui | 0/13 | GARDER (renommer/commenter) |
| game | special_coins | 0 | 16384 | 20 | non | 0/13 | GARDER (renommer/commenter) |
| game | statues_scenes | 2 | 32768 | 9 | non | 0/28 | GARDER (renommer/commenter) |
| game | storage1 | 0 | 24576 | 26 | oui | 0/53 | GARDER (renommer/commenter) |
| game | storage2 | 0 | 24576 | 13 | oui | 0/53 | GARDER (renommer/commenter) |
| game | team_evaluate | 1 | 49152 | 10 | non | 0/16 | GARDER (renommer/commenter) |
| game | territory_war | 15 | 40960 | 11 | non | 0/3 | GARDER (renommer/commenter) |
| game | trade_items | 0 | 24576 | 12 | non | 0/5 | GARDER (renommer/commenter) |
| game | transport | 2 | 24576 | 161 | non | 0/3 | GARDER (renommer/commenter) |
| game | worlds | 2 | 24576 | 18 | non | 0/12 | GARDER (renommer/commenter) |
| game | zone_tickets | 0 | 8192 | 5 | oui | 0/8 | GARDER (renommer/commenter) |
| member | tb_user | 5 | 40960 | 15 | non | 0/19 | GARDER (renommer/commenter) |
