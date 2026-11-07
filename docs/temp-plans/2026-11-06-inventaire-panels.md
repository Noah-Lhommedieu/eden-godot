# 2026-11-06 — Inventaire fenêtres/panneaux EDENFR

Trois points de montage : `HudRoot.BuildAll` (HUD permanent),
`HudExtensions.cs:200-287` (fenêtres zone), `game.tscn` nœud `ZoneWindows`
(craft/guilde). Ouverture de test : `--hud-open=<clé>` / `HudRoot.ForceOpen`
(clés : inventory, skills, quests, shop, craft, strengthen, combine,
lifeskill, magiccube, magicbox, trade, vending, auction, itemmall, death,
guild, familystorage, townlist, familybuilding, crusade, territory…).

## A — Fenêtres implémentées

### A1. Natif CEGUI + wiring réseau réel

| Fenêtre | Layout | Ouverture | État |
|---|---|---|---|
| ChatPanel | ChannelWndV2 | permanent | natif ; v1 non chargé |
| HotbarPanel | FunctionWnd+FucntionBarWnd | permanent | natif |
| MenuIconsPanel | FunctionMenu5pPet→FunctionMenu | permanent | natif + boutons services |
| MinimapPanel | RadarUI_Ver2 | permanent | natif + services câblés |
| TopLeftVitals | MiniInfoWnd+NewLootRuleWnd | permanent | natif |
| TargetFrame | TargetInfoWnd | cible | natif + MobInfo/trade/stall |
| QuestTracker | QuestTraceWnd | permanent | natif |
| BuffBar/NerfBar | BuffWnd | permanent | natif |
| PetFunctionBar | PetFunctionWnd5p | elf | natif |
| PartyHud | TeamWndFrame×membre | équipe | natif cadres ; fenêtre équipe = liste seule |
| SocialWindow | CommunityWnd | O | natif + repli |
| GuildWindow | 15 layouts family* | G/CityBtn/347/501 | natif complet ; pagination find ; pas de colonne « Guild Town » (rôle = FindingTown→TownList) |
| QuestWindow | MissionLog | J | natif ; onglet Legend non câblé |
| QuestSheetWindow | QuestWnd | 388 | natif |
| NpcDialogueWindow | NpcTalkWnd | 285 | natif |
| WorldMapWindow | ZoneMapUI | carte | natif |
| TransportWindow | TransportWnd | 393 | natif |
| ShopWindow | ShopWnd+shopitem | 304 | natif achat/vente/buyback |
| TradeWindow | TradeWnd | 331/target | natif wire complet |
| VendingWindow | StallBuy+StallUI | 378/WorkBtn | natif |
| AuctionWindow | AuctionWnd | 430 | natif shell + onglets custom ; buyout only |
| ItemMallWindow | ItemMall+ItemMaillItem | ItemMallBtn | natif ; gift = LineEdit inline |
| MailWindow | MailWnd | 399 | natif shell + panes custom |
| BankWindow | StorageUI | 357 | natif + grille custom |
| NeobankWindow | StorageUI2 | banque | coquille + grille Reforged |
| FamilyStorageWindow | FamilyStorageWnd | 507/508 | natif ; pages/journal reproduits |
| DeathWindow | ReviveMenuUI | 305/306/531 | natif 3 choix → NCID 20 |
| DuelDialog | DuelRequestWnd | 451 | natif |
| RollTreasureDialog | RollUI | 520 | natif → C→S 193 |
| BattlefieldWindow | BattlefieldListUI | 462-466 | natif (144/145) |
| MessageBoardWindow | MessageBoardUI | 455 | natif (138/139/141) |
| AchievementWindow | Achievement_Ver2 | achi | natif + contenu custom |
| DestinyWindow | FateRoadWnd | DestinyBtn | natif plateau + custom |
| ProfileWindow | custom+EquipmentWnd_new2 | C | custom+layout ; onglet Fame = stub vide |
| TerritoryWindow | familybattlejoinwnd+territoryschedulewnd | 189→515 | natif ListPane ; territorymain/list/rank/time **absents** |
| ColosseumWindow | ArenaMain | 527+ | natif + join/leave/team |
| RecruitWindow | Recruit | 553/564 | shell natif, corps texte → partiel |
| CrusadeWindow | battlecrusadewnd | 617 | natif + join 259 ; **non testé live** |
| FastDressWindow | DressRoomUI | 594 | shell natif, corps texte → quasi-stub |
| RankWindow | RankMainWnd | 385/521/562 | shell natif, corps texte → quasi-stub |
| TowerWindow | towerwnd | 579[0xe] | natif ; **PROUVÉ live** ; progression figée serveur |
| EvaluateWindow | evaluatewnd | 579[0x12] | natif + consent custom ; **PROUVÉ live** |
| StrengthenWindow | strengthenwnd_na+strengthenfactory | 432(9) | natif complet |
| CombineWindow | combinewnd | 432(10) | natif ; **PROUVÉ live** |
| CraftWindow | worklistwnd+workresultwnd+collectwnd/combinewnd | 496-500+433 | natif ; gabarit ligne absent corpus → lignes code |
| LifeSkillWindow | engineeringstudy/jewelrystudy+lifeskillitem | 432(0x68) | natif |
| MagicCubeWindow | newmagiccubewnd+recipe | 432(0x1c) | natif ; C→S 232 |
| MagicBoxWindow | magicboxwnd | 612 | natif ; C→S 257 |
| SpecialCoinWindow | specialcoinui2_na→cn+item | icône sac | natif **affichage seul** |
| PaySystemWindow | paysystemwnd | 602 | natif ; C→S 253 |
| TownListWindow | familytownlistwnd | 514 | natif ; 174/175/176 |
| FamilyBuildingWindow | familybuilding_forest | 483/487 | natif (forêt seul) ; mountain absent |
| HotelWindow | HotelWnd+hotelstoragewnd | 539 | natif ; storage chargé mais **vide serveur** |
| ElfWindow | PetWndV3→**fallback custom** | P | PetWndV3 absent corpus → custom garanti |

### A2. Custom fidèle (rôle natif reproduit en code)

ClassWindow, InventoryWindow (grille+split+barre fonctions), QuestWindow
coquille, RegionMapWindow, SystemMenuWindow, ExpertiseWindows,
CostumeWindow, ArchiveWindow, DailyRewardWindow, CoupleWindow,
LotteryWindow, AltarWindow×2 (**tirages désactivés — paquet non prouvé**),
InspectWindow, MobInfoWindow, DungeonEntryWindow (protocole custom),
ItemPreviewWindow, AdvancedStats, ZoneBanner, CastBar, ClassSwitchFx,
UltimateBar, SettingsWindow + écrans login/channel/select/create.

## B — Stubs à remplacer (par priorité)

| Actuel | Déclencheur | Layout natif visé | Priorité |
|---|---|---|---|
| FeatureDialog 579 hors 0xe/0x12 | services NPC | `instancedungeoncontrol` (donjons multi 126/225/226/227/232) | **Haute** |
| FeatureDialog 585 | mission shop | `shopwnd` → déléguer ShopWindow | **Haute** |
| 564 instance → RecruitWindow texte | service 35 | `instancedungeoncontrol` | Haute |
| FeatureDialog 598 | église | `wedding` → router CoupleWindow | Moyenne |
| RankWindow corps texte | 521/562 | rankinfo/pvprank/rankevaluate | Moyenne |
| FastDressWindow corps texte | 594 | dressroomui (chargé, non rendu) | Moyenne |
| RecruitWindow corps texte | 553/564 | recruit (chargé) | Moyenne |
| InspectWindow = liste uids | 460/571 | otherequipmentwnd | Moyenne |
| AltarWindow tirages off | IGLty/Fty | crystallottery/diamondlottery | Moyenne (bloqué serveur) |
| DailyReward onglet seul | 615 | activitieswnd/loginreward | Moyenne |
| Profile onglet Fame vide | C | likehatewnd/reputation | Moyenne |
| Inventory « Backpack manager » → notice | sac | bagmanagement2 | Basse |
| ElfWindow custom | P | petwndv2 (essayer — v3 absent) | Basse |
| QuestWindow onglet Legend | J | autoquestlist | Basse |
| FeatureDialog 603 | pay event | paysystemwnd adjacent | Basse |

## C — Layouts gameplay non implémentés (priorité haute→basse)

**Haute** : `instancedungeoncontrol`, `messagewnd` (saisie),
`searchteamwnd*` + `setcondition*` + `teamrecruitsetting` (recherche
d'équipe), `playerroomwnd`/`hotelprivilegedlg`/`storagerentdlg` (housing),
`buymsgdlg` (popup étal), `itemmallmailwnd`, `itemmallserial`.

**Moyenne** : `familybattleinfownd`/`judgewnd`/`killedrankwnd*`/`rankwnd`/
`timewnd` (bataille guilde — join fait, reste non), `familyachievement`,
`familycenter`, `familybuilding(_mountain)`, `familymemberfindingwnd`,
`familymessagedlg`, `familyprivilege`, `likehatewnd`, `reputation`,
`prestigewnd*`, `personalcard*`, `adventurelogwnd`, `activitieswnd*`,
`evaluatetimewnd`/`evaluaterankwnd`/`evaluatememberwnd`/`rankevaluatewnd`,
`territoryinfownd`/`territorylist`/`territoryrankwnd`/`territorytimewnd*`/
`territorymainwnd`/`statusdefendwnd`×3, `arenaduel`/`arenainformation`,
`duelwnd`, `questautonotifywnd`, `storywnd`, `facechange*`, `monsterinfo*`,
`equipmentwndswitch`/`newequipment`/`growthequipment`/`iteminheritwnd`,
`dyeingwindow`, `jobglyph*`, `gembreaker` (sans trigger EDENFR),
`newmagiccuberecipedetailwnd`, `specialcoinui/ui2/ps`, `raidwnd`/
`expedition*`/`contractwnd`, `greedyisland*`, `battleroyale*`,
`versusinstance*`, `starway_v2`, `dpsrecord`, `notifywnd`/`nitification*`,
`localinfo`, `logoutwnd`, `optionreadmeui`.

**Hors-scope** : cs_*×15, debugrenderer, shadereditor, webbrowserwnd,
againstindulges, captchawnd, cnsecuritycard, kr*, variantes _cn/_kr/_us
des fenêtres déjà implémentées, gabarits slot*, écrans secondaires dont le
rôle est couvert.

## Corrections à `layout-impl-map.md` (périmé)

~28 layouts implémentés depuis (combinewnd, strengthenwnd_na,
magicboxwnd, newmagiccubewnd, engineeringstudy, paysystemwnd,
specialcoinui2_na, battlecrusadewnd, towerwnd, evaluatewnd, family*×14,
familytownlistwnd, familybuilding_forest, familybattlejoinwnd,
territoryschedulewnd, commondlg). **Faux positif** : `territorymainwnd`
marqué implémenté mais non chargé. Révision ≈ 150+/415 implémentés.

## Priorités joueur

1. Live-vérif des fenêtres « À VÉRIFIER » (shop, craft, strengthen,
   lifeskill, cube, magicbox, itemmall, trade, HDV, étals, mort/revive).
2. `instancedungeoncontrol` + routage 564/579 (donjons multi).
3. Mission shop 585 → ShopWindow.
4. Croisade 617 : test live CrusadeWindow.
5. Family battle : 6 fenêtres familybattle* + rankinfo.
6. Église 598 → CoupleWindow ; Inspect → otherequipmentwnd.
7. Corps des shells : Rank, FastDress, Recruit.
