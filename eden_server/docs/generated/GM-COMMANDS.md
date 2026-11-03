# Audit commandes GM — généré par tools/gm_audit.py

Généré : 2026-10-24 — 354 commandes connues (S_RootCmds+S_GMToolCmds+maison), 58 implémentées.

| Commande | Signature | Accès | Source | Implémentée | Testée |
|---|---|---|---|---|---|
| accept_mission | accept_mission <number> | 4 | ini seul | non | - |
| achievement_item | achievement_item <number> | 4 | ini seul | non | - |
| add_appellation | add_appellation <number> | 4 | ini seul | non | - |
| add_class | add_class <number> | 4 | switch local | oui | - |
| add_diamond_lottery_plus | add_diamond_lottery_plus <number> | 4 | ini seul | non | - |
| add_elf | add_elf <number> | 4 | ini seul | non | - |
| add_ingame_lottery_plus | add_ingame_lottery_plus <number> | 4 | ini seul | non | - |
| add_item | add_item <word> <word> <word> <number> <word> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> <number> | 4 | ini seul | non | - |
| add_lottery_plus | add_lottery_plus <number> | 4 | ini seul | non | - |
| adjust_spell_anitime | adjust_spell_anitime <number> <number> | 4 | ini seul | non | - |
| allusers | allusers | 2 | GmHandlers | oui | - |
| allworld_cmd | allworld_cmd <rest_input> | 4 | ini seul | non | - |
| announce | announce <rest_input> | 4 | ini seul | non | - |
| around_kill | around_kill <number> <number> | 4 | ini seul | non | - |
| around_kill_all | around_kill_all <number> | 4 | ini seul | non | - |
| around_kill_all_player | around_kill_all_player <number> | 4 | ini seul | non | - |
| auction_sell | auction_sell <number> <number> | 4 | ini seul | non | - |
| aw_put_treasure | aw_put_treasure <number> <number> | 4 | ini seul | non | - |
| ban | ban <number> <number> <word> | 4 | ini seul | non | oui |
| banchar | banchar <number> <number> | 4 | ini seul | non | - |
| banchar_name | banchar_name <word> <number> | 4 | ini seul | non | - |
| battlefield | battlefield <number> | 4 | ini seul | non | - |
| bf_ch_num | bf_ch_num <number> <number> <number> | 4 | ini seul | non | - |
| bf_open | bf_open <number> <number> | 4 | ini seul | non | - |
| bgw_account_clear | bgw_account_clear <word> <number> | 4 | ini seul | non | - |
| broadcast_system_message | broadcast_system_message <number> <number> <number> <rest_input> | 4 | ini seul | non | - |
| call_elf | call_elf <number> | 4 | ini seul | non | - |
| caplv | caplv <number> | 4 | ini seul | non | - |
| captcha_id | captcha_id <number> <number> | 4 | ini seul | non | - |
| captcha_name | captcha_name <word> <number> | 4 | ini seul | non | - |
| cast_spell | cast_spell <number> <number> | 4 | ini seul | non | - |
| change_class | change_class <number> | 4 | switch local | oui | - |
| change_family_leader | change_family_leader <word> <number> <number> | 4 | ini seul | non | - |
| change_grow_type | change_grow_type <number> | 4 | ini seul | non | - |
| change_hair | change_hair <number> | 4 | ini seul | non | - |
| change_hair_color | change_hair_color <number> | 4 | ini seul | non | - |
| change_raid25 | change_raid25 | 4 | ini seul | non | - |
| change_weather | change_weather <number> | 4 | ini seul | non | oui |
| channel_limit | channel_limit <number> <number> | 4 | ini seul | non | - |
| channel_limit_name | channel_limit_name <word> <number> | 4 | ini seul | non | - |
| chat | chat <rest_input> | 4 | ini seul | non | oui |
| clear_bag | clear_bag | - | GmHandlers | oui | - |
| clear_bag_item | clear_bag_item | 4 | ini seul | non | - |
| clear_cool_down_time | clear_cool_down_time | 4 | ini seul | non | - |
| clear_lover_disband | clear_lover_disband | 4 | ini seul | non | - |
| clear_near_items | clear_near_items | 4 | ini seul | non | - |
| clevel | clevel | - | switch local | oui | - |
| clone_item | clone_item <number> | 4 | GmHandlers | oui | - |
| clone_item_to | clone_item_to <number> <number> | 4 | GmHandlers | oui | - |
| clone_items | clone_items <number> <number> | 4 | GmHandlers | oui | - |
| clone_monster | clone_monster <number> <number> <number> | 4 | switch local | oui | oui |
| clone_monster_around | clone_monster_around <number> <number> <number> <number> | 4 | ini seul | non | - |
| clone_monster_locate | clone_monster_locate <number> <number> <number> <number> | 4 | switch local | oui | - |
| clone_monster_remote | clone_monster_remote <number> <number> <number> <number> <number> <number> |  | ini seul | non | - |
| clone_npc | clone_npc <number> | 4 | ini seul | non | - |
| clone_quest_item | clone_quest_item <number> <number> <number> <number> | 4 | ini seul | non | - |
| clone_quest_item_silently | clone_quest_item_silently <number> <number> <number> | 4 | ini seul | non | - |
| clone_quest_treasure | clone_quest_treasure <number> <number> <number> <number> <number> <number> | 4 | ini seul | non | - |
| clone_reward_item | clone_reward_item <number> <number> | 4 | ini seul | non | - |
| close_node | close_node <number> <number> | 4 | ini seul | non | - |
| create_family | create_family | 4 | ini seul | non | - |
| create_town | create_town <number> | 4 | ini seul | non | - |
| cross_world | cross_world <number> | 4 | ini seul | non | - |
| crusade | crusade | - | GmHandlers | oui | - |
| date | date | 4 | GmHandlers | oui | oui |
| debug | debug <number> | 4 | ini seul | non | - |
| del_item | del_item | - | GmHandlers | oui | - |
| delete_item | delete_item <number> <word> <word> <number> | 4 | ini seul | non | - |
| disband_family | disband_family | 4 | ini seul | non | - |
| display_sum_node_users | display_sum_node_users <number> | 4 | ini seul | non | - |
| display_sum_world_users | display_sum_world_users <number> | 4 | ini seul | non | - |
| drill_item | drill_item <number> <number> | 4 | ini seul | non | - |
| drop_item | drop_item <number> <number> | 4 | ini seul | non | - |
| drop_items | drop_items <number> <number> | 4 | ini seul | non | - |
| drop_quest_item | drop_quest_item <number> <number> <number> | 4 | ini seul | non | - |
| echo | echo <rest_input> | 4 | ini seul | non | - |
| echobyid | echobyid <number> | 4 | ini seul | non | - |
| effect_life | effect_life <number> <number> <number> <number> <number> | 4 | ini seul | non | - |
| effect_loc | effect_loc <number> <number> <number> <number> <number> | 4 | ini seul | non | - |
| effect_map | effect_map <number> <number> <number> | 4 | ini seul | non | - |
| effect_map_time | effect_map_time <number> <number> <number> <number> |  | ini seul | non | - |
| elf_skill | elf_skill <number> <number> <number> | 4 | ini seul | non | - |
| elf_work | elf_work <number> <number> <number> <number> <number> | 4 | ini seul | non | - |
| enter_my_farm | enter_my_farm | 4 | ini seul | non | - |
| escape | escape | 4 | ini seul | non | - |
| exchange | exchange item service | 4 | ini seul | non | - |
| family_battle_end | family_battle_end | 4 | ini seul | non | - |
| family_battle_honor_switch | family_battle_honor_switch <number> | 4 | ini seul | non | - |
| family_battle_reset_week_update | family_battle_reset_week_update | 4 | ini seul | non | - |
| family_battle_restart | family_battle_restart | 4 | ini seul | non | - |
| family_battle_setup_judge | family_battle_setup_judge <number> <number> <word> | 4 | ini seul | non | - |
| family_level_up | family_level_up | 4 | ini seul | non | - |
| farm_interact_open | farm_interact_open <number> | 4 | ini seul | non | - |
| fatality_damage | fatality_damage <number> | 4 | ini seul | non | - |
| find_item | find_item | - | GmHandlers | oui | - |
| find_spell | find_spell | - | GmHandlers | oui | - |
| flush_dba_data | flush_dba_data | 4 | ini seul | non | - |
| force_close_twenty | force_close_twenty <number> | 4 | ini seul | non | - |
| friend_together | friend_together | 4 | ini seul | non | - |
| fubag | fubag <number> | 4 | ini seul | non | - |
| gain_aa_exp | gain_aa_exp <number> | 4 | ini seul | non | - |
| gain_aa_point | gain_aa_point <number> | 4 | GmHandlers | oui | - |
| gain_alpaca_coin | gain_alpaca_coin <number> | 4 | ini seul | non | - |
| gain_ap_point | gain_ap_point | - | GmHandlers | oui | - |
| gain_bind_gold | gain_bind_gold <number> | 4 | ini seul | non | - |
| gain_building_durability | gain_building_durability <number> <number> | 4 | ini seul | non | - |
| gain_building_exp | gain_building_exp <number> <number> | 4 | ini seul | non | - |
| gain_class_point | gain_class_point <number> | 4 | switch local | oui | - |
| gain_crystal | gain_crystal <number> | 4 | GmHandlers | oui | - |
| gain_cs_gold | gain_cs_gold <number> | 4 | ini seul | non | - |
| gain_destiny_event | gain_destiny_event <number> <number> | 4 | ini seul | non | - |
| gain_destiny_exp | gain_destiny_exp <number> | 4 | ini seul | non | - |
| gain_destiny_vit | gain_destiny_vit <number> | 4 | ini seul | non | - |
| gain_diamond | gain_diamond | - | GmHandlers | oui | - |
| gain_elf_exp | gain_elf_exp <number> <number> | 4 | ini seul | non | - |
| gain_elf_familiar | gain_elf_familiar <number> <number> | 4 | ini seul | non | - |
| gain_ep_point | gain_ep_point | - | GmHandlers | oui | - |
| gain_exp | gain_exp <number> | 4 | switch local/GmHandlers | oui | - |
| gain_family_cp | gain_family_cp <number> | 4 | ini seul | non | - |
| gain_family_exp | gain_family_exp <number> | 4 | ini seul | non | - |
| gain_family_honor | gain_family_honor <number> | 4 | ini seul | non | - |
| gain_family_leader_point | gain_family_leader_point <number> | 4 | ini seul | non | - |
| gain_family_treasury | gain_family_treasury <number> | 4 | ini seul | non | - |
| gain_farm_interaction_point | gain_farm_interaction_point <number> <number> | 4 | ini seul | non | - |
| gain_forever_gold | gain_forever_gold <number> | 4 | GmHandlers | oui | - |
| gain_gem | gain_gem | - | GmHandlers | oui | - |
| gain_gold | gain_gold <number> | 4 | GmHandlers | oui | - |
| gain_life_skill_exp | gain_life_skill_exp <number> <number> | 4 | ini seul | non | - |
| gain_lifelong_gold | gain_lifelong_gold <number> | 4 | switch local | oui | - |
| gain_love_coin | gain_love_coin <number> | 4 | ini seul | non | - |
| gain_prestige_exp | gain_prestige_exp <number> <number> | 4 | switch local | oui | - |
| gain_skill_point | gain_skill_point <number> | 4 | GmHandlers | oui | - |
| gain_soul_ability | gain_soul_ability <number> <number> | 4 | ini seul | non | - |
| gain_special_gold | gain_special_gold <number> <number> | 4 | GmHandlers | oui | - |
| gain_st_point | gain_st_point <number> | 4 | ini seul | non | - |
| gain_war_coin | gain_war_coin <number> | 4 | ini seul | non | - |
| get_all_template_monsters | get_all_template_monsters | 4 | ini seul | non | - |
| get_effect_data | get_effect_data <number> | 4 | ini seul | non | - |
| get_farm | get_farm | 4 | ini seul | non | - |
| get_farm_event | get_farm_event <number> | 4 | ini seul | non | - |
| get_license | get_license <number> | 4 | switch local | oui | - |
| get_player_info | get_player_info <word> | 4 | ini seul | non | - |
| get_player_locate | get_player_locate <number> | 4 | switch local | oui | - |
| get_server_id | get_server_id | 4 | ini seul | non | - |
| get_shortcuts | get_shortcuts | 4 | ini seul | non | - |
| get_spell | get_spell <number> | 4 | ini seul | non | - |
| get_spell_data | get_spell_data <number> | 4 | ini seul | non | - |
| get_spellmaster | get_spellmaster <number> | 4 | ini seul | non | - |
| get_user_info | get_user_info <word> | 4 | ini seul | non | - |
| give | give | - | GmHandlers | oui | - |
| give_exploit | give_exploit <number> | 4 | ini seul | non | - |
| gmannounce | gmannounce <word> | 4 | ini seul | non | - |
| go | go | - | switch local | oui | - |
| god | god | - | switch local | oui | - |
| goto | goto <number> <number> | 4 | switch local | oui | - |
| guild | guild <rest_input> | 4 | ini seul | non | - |
| heal | heal | - | switch local | oui | oui |
| help | help | - | switch local | oui | - |
| identify_shop | identify_shop | 4 | ini seul | non | - |
| inlay_shop | inlay_shop <number> | 4 | ini seul | non | - |
| inside | inside <number> | 4 | ini seul | non | - |
| invincible | invincible <number> | 4 | switch local | oui | oui |
| kick | kick <word> | 4 | ini seul | non | - |
| kill | kill <number> | 4 | switch local | oui | oui |
| learn_spell | learn_spell | - | GmHandlers | oui | - |
| level | level | - | switch local | oui | oui |
| limitless | limitless | - | GmHandlers | oui | - |
| list_class | list_class | - | GmHandlers | oui | - |
| list_durability | list_durability <number> | 4 | ini seul | non | - |
| list_state | list_state | 4 | ini seul | non | - |
| listarea | listarea | 4 | ini seul | non | - |
| listenchant | listenchant <word> | 4 | ini seul | non | - |
| listfms | listfms <number> <number> | 4 | ini seul | non | - |
| listpms | listpms <number> | 4 | ini seul | non | - |
| ll_exp | ll_exp | - | GmHandlers | oui | - |
| look | look <number> | 4 | ini seul | non | - |
| ls_rb | ls_rb | 4 | ini seul | non | - |
| modify_mail | modify_mail <number> <number> <number> | 4 | ini seul | non | - |
| monster_goto | monster_goto <number> <number> <number> | 4 | ini seul | non | - |
| no_speak | no_speak <number> <number> <word> | 4 | ini seul | non | - |
| node | node <rest_input> | 4 | ini seul | non | oui |
| npc_use_channel | npc_use_channel <number> <number> <rest_input> | 4 | ini seul | non | - |
| npc_use_spell | npc_use_spell <number> <number> | 4 | ini seul | non | - |
| offline_ctrl | offline_ctrl <word> <word> <number> <word> | 4 | ini seul | non | - |
| open_exploit_rank | open_exploit_rank | 4 | ini seul | non | - |
| open_staravenue | open_staravenue <number> | 4 | ini seul | non | - |
| party | party <rest_input> | 4 | ini seul | non | oui |
| pet_set_level | pet_set_level <number> <number> | 4 | ini seul | non | - |
| ping | ping | 4 | GmHandlers | oui | - |
| pos | pos | - | switch local | oui | oui |
| put_treasure | put_treasure <number> <number> | 4 | ini seul | non | - |
| query_equipment | query_equipment | 4 | ini seul | non | - |
| query_level_grow | query_level_grow | 4 | ini seul | non | - |
| query_npc | query_npc <number> | 4 | ini seul | non | - |
| query_npc_involve | query_npc_involve <number> | 4 | ini seul | non | - |
| query_test_attack_monster | query_test_attack_monster | 4 | ini seul | non | - |
| querychar | querychar <word> | 4 | ini seul | non | - |
| quest | quest <rest_input> | 4 | ini seul | non | - |
| refresh_recommend_activities | refresh_recommend_activities <number> | 4 | ini seul | non | - |
| reload_diamond_lottery_db | reload_diamond_lottery_db | 4 | ini seul | non | - |
| reload_effect | reload_effect | 4 | ini seul | non | - |
| reload_elf_lottery_db | reload_elf_lottery_db | 4 | ini seul | non | - |
| reload_ingame_lottery_db | reload_ingame_lottery_db | 4 | ini seul | non | - |
| reload_itemmall_db | reload_itemmall_db | 4 | ini seul | non | - |
| reload_template_monster | reload_template_monster | 4 | ini seul | non | - |
| remove_all_license | remove_all_license | 4 | ini seul | non | - |
| remove_class | remove_class <number> | 4 | ini seul | non | - |
| remove_elf | remove_elf <number> | 4 | ini seul | non | - |
| remove_enchant | remove_enchant <number> <number> | 4 | switch local | oui | - |
| remove_family_emblem | remove_family_emblem <number> | 4 | ini seul | non | - |
| repairallequipment | repairallequipment | 4 | ini seul | non | - |
| repairshop | repairshop | 4 | ini seul | non | - |
| reset_aa | reset_aa | 4 | ini seul | non | - |
| reset_attribute | reset_attribute | 4 | ini seul | non | - |
| reset_attribute_gold | reset_attribute_gold <number> | 4 | ini seul | non | - |
| reset_node_times | reset_node_times <number> <number> | 4 | ini seul | non | - |
| reset_skill | reset_skill | 4 | ini seul | non | - |
| reset_skill_gold | reset_skill_gold <number> | 4 | ini seul | non | - |
| reset_st | reset_st | 4 | ini seul | non | - |
| restore | restore | 4 | switch local | oui | oui |
| restore_all | restore_all | 4 | ini seul | non | - |
| return_gold | return_gold <number> <number> | 4 | ini seul | non | - |
| return_item | return_item <number> <word> | 4 | ini seul | non | - |
| rm_rb | rm_rb <number> | 4 | ini seul | non | - |
| run | run <number> | 4 | ini seul | non | - |
| save_item | save_item <number> | 4 | ini seul | non | - |
| save_monster | save_monster <number> | 4 | ini seul | non | - |
| save_shortcut | save_shortcut | 4 | ini seul | non | - |
| save_skytower | save_skytower <number> | 4 | ini seul | non | - |
| say | say <rest_input> | 4 | ini seul | non | - |
| screenmsg | screenmsg <number> <rest_input> | 4 | ini seul | non | - |
| select_family_emblem | select_family_emblem | 4 | ini seul | non | - |
| select_family_leader | select_family_leader <word> | 4 | ini seul | non | - |
| self_use_effect | self_use_effect <number> <number> | 4 | switch local | oui | - |
| send_reward_item | send_reward_item <number> <number> <number> <number> <number> | 4 | ini seul | non | - |
| send_sys_mail_queue | send_sys_mail_queue <number> | 4 | ini seul | non | - |
| server_data_test | server_data_test <word> <number> <number> <number> | 4 | ini seul | non | - |
| set_aa_rate | set_aa_rate <number> | 4 | ini seul | non | - |
| set_achievement | set_achievement <number> <number> <number> | 4 | ini seul | non | - |
| set_actiondelay_unit | set_actiondelay_unit <number> | 4 | ini seul | non | - |
| set_all_spell_level | set_all_spell_level <number> | 4 | ini seul | non | - |
| set_assert | set_assert <word> <number> | 4 | ini seul | non | - |
| set_bag_time | set_bag_time <number> <number> | 4 | ini seul | non | - |
| set_battleroyale_open | set_battleroyale_open <number> <number> | 4 | ini seul | non | - |
| set_blocklogin | set_blocklogin <number> <number> | 4 | ini seul | non | - |
| set_class_level | set_class_level <number> | 4 | switch local | oui | - |
| set_cloak_color | set_cloak_color <number> | 4 | ini seul | non | - |
| set_clothes_color | set_clothes_color <number> | 4 | ini seul | non | - |
| set_colosseum_open | set_colosseum_open <number> <number> <number> <number> | 4 | ini seul | non | - |
| set_cs_is_force_open | set_cs_is_force_open <number> | 4 | ini seul | non | - |
| set_cs_mode | set_cs_mode <number> | 4 | ini seul | non | - |
| set_diamond_lottery_open | set_diamond_lottery_open <number> | 4 | ini seul | non | - |
| set_effect_command | set_effect_command <number> <word> <rest_input> | 4 | ini seul | non | - |
| set_effect_data | set_effect_data <number> <word> <word> <number> <number> <number> <number> <word> <word> <number> <number> <number> <number> | 4 | ini seul | non | - |
| set_elf_action | set_elf_action <number> <number> | 4 | ini seul | non | - |
| set_elf_level | set_elf_level <number> | 4 | ini seul | non | - |
| set_elf_mood | set_elf_mood <number> <number> | 4 | ini seul | non | - |
| set_eq_train | set_eq_train | 4 | ini seul | non | - |
| set_equip_train_level | set_equip_train_level <number> | 4 | ini seul | non | - |
| set_eyes_color | set_eyes_color <number> | 4 | ini seul | non | - |
| set_face_id | set_face_id <number> | 4 | ini seul | non | - |
| set_family_emblem | set_family_emblem <number> <number> | 4 | ini seul | non | - |
| set_family_level | set_family_level <number> | 4 | ini seul | non | - |
| set_farm_interact_time | set_farm_interact_time <number> | 4 | ini seul | non | - |
| set_farm_level | set_farm_level <number> | 4 | ini seul | non | - |
| set_farm_visitable | set_farm_visitable <number> | 4 | ini seul | non | - |
| set_greedyisland_open | set_greedyisland_open <number> | 4 | ini seul | non | - |
| set_hair_color | set_hair_color <number> | 4 | ini seul | non | - |
| set_hair_id | set_hair_id <number> | 4 | ini seul | non | - |
| set_helmet_color | set_helmet_color <number> | 4 | ini seul | non | - |
| set_ingame_lottery_open | set_ingame_lottery_open <number> | 4 | ini seul | non | - |
| set_item | set_item <number> <word> <number> | 4 | ini seul | non | - |
| set_level | set_level <number> | 4 | GmHandlers | oui | - |
| set_level_grow | set_level_grow <number> <number> <number> <number> <number> <number> <number> <number> <number> | 4 | ini seul | non | - |
| set_log_level | set_log_level <word> <number> | 4 | ini seul | non | - |
| set_memory_recycle | set_memory_recycle <number> <number> | 4 | ini seul | non | - |
| set_money | set_money <number> <number> | 4 | switch local | oui | - |
| set_monster_damage | set_monster_damage <number> <number> <number> <number> <number> <number> <number> | 4 | ini seul | non | - |
| set_monster_movement | set_monster_movement <number> <number> <number> <number> | 4 | ini seul | non | - |
| set_monster_sight | set_monster_sight <number> <number> | 4 | ini seul | non | - |
| set_moveself_switch | set_moveself_switch <number> | 4 | ini seul | non | - |
| set_node_drop | set_node_drop <number> <number> | 4 | ini seul | non | - |
| set_node_exp | set_node_exp <number> <number> | 4 | ini seul | non | - |
| set_node_gold | set_node_gold <number> <number> | 4 | ini seul | non | - |
| set_node_np | set_node_np <number> <number> | 4 | ini seul | non | - |
| set_pet_star | set_pet_star <number> <number> | 4 | ini seul | non | - |
| set_present_appellation | set_present_appellation <number> | 4 | ini seul | non | - |
| set_prestige_level | set_prestige_level <number> <number> | 4 | switch local | oui | - |
| set_rb | set_rb <number> <number> <number> | 4 | ini seul | non | - |
| set_reborn_monster | set_reborn_monster <number> <word> <word> <number> <number> <number> <word> <word> <number> <number> <number> | 4 | ini seul | non | - |
| set_skin_color | set_skin_color <number> | 4 | ini seul | non | - |
| set_spell_card | set_spell_card <number> <number> | 4 | ini seul | non | - |
| set_spell_card_attr | set_spell_card_attr <number> <number> <number> <number> | 4 | ini seul | non | - |
| set_spell_level | set_spell_level <number> <number> | 4 | ini seul | non | - |
| set_statue | set_statue <number> <number> <number> <number> | 4 | ini seul | non | - |
| set_sys_var | set_sys_var <word> <number> | 4 | ini seul | non | - |
| set_territory_max_num | set_territory_max_num <number> | 4 | ini seul | non | - |
| set_territory_open | set_territory_open <number> <number> | 4 | ini seul | non | - |
| set_territory_status | set_territory_status <number> <number> | 4 | ini seul | non | - |
| set_textlimit | set_textlimit <number> <word> | 4 | ini seul | non | - |
| set_useblocklogin | set_useblocklogin <number> | 4 | ini seul | non | - |
| set_versus_instance_open | set_versus_instance_open <number> <number> <number> | 4 | ini seul | non | - |
| setextbornmonster | setextbornmonster <number> <number> | 4 | ini seul | non | - |
| setfms | setfms <number> <number> <number> | 4 | ini seul | non | - |
| setpms | setPMS <number> <number> <number> <number> | 4 | ini seul | non | - |
| setra | setra <number> <number> | 4 | ini seul | non | - |
| setstallsignboard | setstallsignboard <number> | 4 | ini seul | non | - |
| setstallspace | setstallspace <number> | 4 | ini seul | non | - |
| shop | shop <word> <number> <number> | 4 | ini seul | non | oui |
| show_debug_message | show_debug_message <number> | 4 | ini seul | non | - |
| show_hate | show_hate <number> | 4 | ini seul | non | - |
| show_monster | show_monster <number> | 4 | ini seul | non | - |
| shut_down | shut_down <number> | 4 | ini seul | non | - |
| slayer | slayer <number> | 4 | ini seul | non | - |
| status | status | 1 | ini seul | non | oui |
| storage | storage <number> <number> | 4 | ini seul | non | oui |
| summon_pet | summon_pet <number> | 4 | ini seul | non | - |
| super_clear_bag_item | super_clear_bag_item | 4 | ini seul | non | - |
| surprise_box | surprise_box <number> | 4 | ini seul | non | - |
| switch_mutuallyhelp_notify_new | switch_mutuallyhelp_notify_new <number> | 4 | ini seul | non | - |
| switch_player_room | switch_player_room <number> | 4 | ini seul | non | - |
| switch_room_decorating | switch_room_decorating <number> <number> | 4 | ini seul | non | - |
| system | system <rest_input> | 4 | ini seul | non | - |
| system_area | system_area <word> <rest_input> | 4 | ini seul | non | - |
| take_expbonus | take_expbonus | 4 | ini seul | non | - |
| tele | tele | - | switch local | oui | - |
| test_character_attack | test_character_attack <number> <number> | 4 | ini seul | non | - |
| test_drop_treasure | test_drop_treasure <number> <number> | 4 | ini seul | non | - |
| test_durability | test_durability <number> <number> <number> | 4 | ini seul | non | - |
| test_monster_attack | test_monster_attack <number> <number> | 4 | ini seul | non | - |
| test_pk | test_pk <number> <number> | 4 | ini seul | non | - |
| test_spell_attack | test_spell_attack <number> <number> <number> | 4 | ini seul | non | - |
| town | town | 4 | ini seul | non | oui |
| tpa_save | tpa_save <number> | 4 | ini seul | non | - |
| trace | trace <number> <word> | 4 | ini seul | non | oui |
| trade | trade <rest_input> | 4 | ini seul | non | oui |
| transfer | transfer <number> | 4 | ini seul | non | - |
| transport_and_deduct | transport_and_deduct <number> <number> | 4 | ini seul | non | - |
| transport_area | transport_area <number> <number> | 4 | ini seul | non | - |
| transport_node | transport_node <number> | 4 | switch local | oui | - |
| transport_to_character | transport_to_character <word> | 4 | ini seul | non | - |
| transport_to_character_id | transport_to_character_id | 4 | ini seul | non | - |
| update_closet | update_closet <number> | 4 | switch local | oui | - |
| update_rank | update_rank | 4 | ini seul | non | - |
| update_shortcut | update_shortcut <number> <number> <number> | 4 | ini seul | non | - |
| use_item_to | use_item_to <word> <number> <number> <number> <rest_input> | 4 | ini seul | non | - |
| users | users | 4 | GmHandlers | oui | - |
| vanish | vanish <number> | 4 | ini seul | non | - |
| version | version | 4 | GmHandlers | oui | oui |
| visit_family_instance | visit_family_instance <word> | 4 | ini seul | non | - |
| visit_player_room | visit_player_room <word> | 4 | ini seul | non | - |
| visit_player_room_id | visit_player_room_id <number> | 4 | ini seul | non | - |
| weak | weak <number> | 4 | ini seul | non | - |
| whisper | whisper <word> <rest_input> | 4 | ini seul | non | - |
