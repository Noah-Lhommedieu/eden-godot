# Carte d'implémentation des layouts CEGUI (`uidata/layouts` → panneaux Godot)

Pour chacun des **415 `.layout`** de `corpus/fr-full/uidata/layouts/` : la fenêtre
CEGUI (nom racine + titre), l'imageset principal cité, le panneau/script Godot qui
l'implémente — ou le constat d'absence. C'est la carte du travail UI restant.

Complète `docs/reference/corpus/ui-atlas-map.md` (imageset ↔ layout ↔ ref-shot)
et `ui-et-cegui.md` (inventaire brut).

## Méthode & statuts

- **Natif** : le script charge le `.layout` tel quel via `HudRoot.Build("X.layout")`
  (pipeline `scripts/ui/Cegui*.cs` : parse XML → Controls Godot). 53 layouts sont
  cités littéralement dans `eden_godot/godot_project/scripts/`.
- **Custom** : le rôle de la fenêtre est reproduit en code (`EeSkin`, Panels Godot)
  sans charger le layout — documenté dans le docstring du script (ex. `ClassWindow`
  pour `jobwnd`, `InventoryWindow` pour `inventorywnd`, `CharSelectScreen` pour
  `selectcharacterwnd`).
- **Statuts** : `implémenté` = la fenêtre existe et joue le rôle du layout (natif ou
  custom) ; `partiel` = le rôle n'est couvert qu'en partie (variante régionale non
  chargée, sous-fenêtre/template remplacé par du custom, données affichées en texte
  brut sans la mise en page native, ou seulement un bouton/notice) ; `absent` =
  aucun panneau/script ne couvre le rôle. Les fenêtres dont le **paquet réseau est
  parsé mais sans UI** (`→ PlayerProfileState`, `MiscPackets`) sont comptées
  `absent` — la donnée existe, pas la fenêtre.
- `titre_fenetre` = propriété `Text` de la fenêtre racine (sinon nom `Root/…`).
  Les titres Big5 restent en chinois dans les layouts EDENFR — la traduction se
  fait au runtime (`UiTextEn`).
- `imageset_principal` = imagesets cités par `set:` dans le layout ; « skin » =
  aucun `set:` (cadre/boutons viennent de `dejavu.looknfeel`).

## Synthèse par famille

| Famille | Layouts | Implémentés | Partiels | Absents | % couvert (impl.+partiel) |
|---|---|---|---|---|---|
| Connexion & pré-jeu | 18 | 10 | 1 | 7 | 61 % |
| HUD permanent (vitals, hotbar, chat, minimap, cible, buffs) | 37 | 15 | 14 | 8 | 78 % |
| Groupe / équipe / raid | 29 | 4 | 4 | 21 | 28 % |
| Quêtes & dialogues PNJ | 12 | 4 | 5 | 3 | 75 % |
| Classes (jobwnd) & expertise | 32 | 20 | 8 | 4 | 88 % |
| Inventaire & équipement | 30 | 11 | 13 | 6 | 80 % |
| Stockage & hôtel | 11 | 7 | 0 | 4 | 64 % |
| Commerce (shop/trade/étal/HDV/courrier/item mall) | 30 | 14 | 7 | 9 | 70 % |
| Social & guilde (family) | 40 | 7 | 4 | 29 | 28 % |
| Succès, activités, destin & collection | 37 | 7 | 12 | 18 | 51 % |
| Familier (pet/elf) | 8 | 5 | 2 | 1 | 88 % |
| Craft, renforcement & Magic Cube | 22 | 2 | 8 | 12 | 45 % |
| Cartes & téléportation | 7 | 3 | 4 | 0 | 100 % |
| PvP, territoire & instances | 43 | 5 | 8 | 30 | 30 % |
| Loteries & autels | 8 | 2 | 6 | 0 | 100 % |
| Système, légal & outils moteur | 51 | 9 | 8 | 34 | 33 % |
| **Total** | **415** | **125** | **104** | **186** | **55 %** |

## Détail par famille

### Connexion & pré-jeu — 18 layouts (10 implémentés / 1 partiels / 7 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `charpassworddlg.layout` | CharPasswordDlg | skin (dejavu) | `scripts/screens/CharSelectScreen.cs` (dialogues custom) | implémenté | Selection_personnage.png |
| `cncreatecharacterwnd.layout` | CreateCharacterWnd | DejaVu | `scripts/screens/CharCreateScreen.cs` (custom) | implémenté | — |
| `cnworldserverlistwnd.layout` | CWorldServerListWnd | DejaVu | `scripts/screens/ChannelSelectScreen.cs` (custom) | implémenté | Selection_channel.png |
| `createcharacterconfirm.layout` | CommonDlg | skin (dejavu) | `scripts/screens/CharCreateScreen.cs` (flux custom — CommonDlg non chargé) | partiel | — |
| `createcharactermovieui.layout` | CreateCharacterMovieUI | skin (dejavu) | — | absent | — |
| `createcharacterwnd.layout` | CreateCharacterWnd | DejaVu | `scripts/screens/CharCreateScreen.cs` (custom) | implémenté | — |
| `createcharacterwndver2.layout` | CreateCharacterWnd | DejaVu | `scripts/screens/CharCreateScreen.cs` (custom) | implémenté | — |
| `createcharacterwndver2_2.layout` | CreateCharacterWnd | DejaVu | `scripts/screens/CharCreateScreen.cs` (custom) | implémenté | — |
| `cworldserverlistwnd.layout` | CWorldServerListWnd | DejaVu | `scripts/screens/ChannelSelectScreen.cs` (custom) | implémenté | Selection_channel.png |
| `deletecharacterdlg.layout` | DeletecharacterDlg | skin (dejavu) | `scripts/screens/CharSelectScreen.cs` (dialogues custom) | implémenté | Selection_personnage.png |
| `facechange.layout` | ChangeCharacterPropertyWnd | DejaVu | — | absent | — |
| `facechangever2.layout` | ChangeCharacterPropertyWnd | DejaVu | — | absent | — |
| `liveupdate.layout` | LiveUpdateWnd | Roundicon | — | absent | — |
| `login.layout` | Login | skin (dejavu) | `scripts/screens/LoginScreen.cs` (custom — ne charge pas le .layout) | implémenté | — |
| `newotheravatarcn.layout` | NewOtherAvatarCN | DejaVu,DejaVuImage | — | absent | — |
| `openingmoviewnd.layout` | OpeningMovieWnd | skin (dejavu) | — | absent | — |
| `selectcharacterwnd.layout` | SelectCharacterWnd | DejaVu,DejaVuImage,main | `scripts/screens/CharSelectScreen.cs` (custom) | implémenté | Selection_personnage.png |
| `vkeyboard.layout` | VKeyBoard | skin (dejavu) | — | absent | — |

### HUD permanent (vitals, hotbar, chat, minimap, cible, buffs) — 37 layouts (15 implémentés / 14 partiels / 8 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `buffwnd.layout` | BuffWnd | skin (dejavu) | `scripts/hud/BuffBar.cs` + `scripts/hud/NerfBar.cs` → BuffWnd.layout | implémenté | shot_0045_000016.png |
| `buffwnd1.layout` | BuffWnd | DejaVuImage | `scripts/hud/BuffBar.cs` (variante / gabarit slot — slots de BuffWnd utilisés) | partiel | shot_0045_000016.png |
| `castingbarwnd.layout` | castingbarWnd | skin (dejavu) | `scripts/combat/CastBar.cs` (custom — régions castingBar* du skin) | implémenté | TimedCastSkill.png |
| `channelconfig.layout` | ChannelConfig | skin (dejavu) | `scripts/hud/ChatPanel.cs` (bouton Settings natif = toggle timestamps seul) | partiel | shot_0078_000316.png |
| `channelconfigv2.layout` | ChannelConfig | skin (dejavu) | `scripts/hud/ChatPanel.cs` (bouton Settings natif = toggle timestamps seul) | partiel | shot_0078_000316.png |
| `channelwnd.layout` | ChannelWnd | DejaVu,DejaVuImage,main | `scripts/hud/ChatPanel.cs` (variante v1 — v2 chargée, même Root/ChannelWnd) | partiel | ChatPanel.png |
| `channelwndv2.layout` | ChannelWnd | DejaVu,DejaVuImage,main | `scripts/hud/ChatPanel.cs` → ChannelWndV2.layout | implémenté | ChatPanel.png, ChatPanel-Peer.png |
| `cnarrowmessage.layout` | CNArrowMessage | CNDejaVu,CNDejaVu2 | — | absent | — |
| `cnradarui.layout` | Radar | CNDejaVu2,Radar | `scripts/hud/MinimapPanel.cs` (variantes — même Root/Radar) | partiel | juste le jeu.png |
| `dpsrecord.layout` | DPSRecord | CDAni,main | — | absent | — |
| `dragmsgdlgui.layout` | 彈出對話框 | skin (dejavu) | — | absent | — |
| `flybirditem.layout` | FlyBirdItem | skin (dejavu) | `scripts/hud/ClassSwitchFx.cs` (custom — anim « Switched Class ») | implémenté | shot_0056_000116.png |
| `fucntionbarwnd.layout` | FunctionBarWnd | main | `scripts/hud/HotbarPanel.cs` → FunctionWnd + FucntionBarWnd.layout | implémenté | HotbarPanel.png, DoubleEXPbar.png |
| `functionmenu.layout` | FunctionMenu | CNDejaVu,DejaVu,main | `scripts/hud/MenuIconsPanel.cs` → FunctionMenu5pPet.layout (repli FunctionMenu) | implémenté | juste le jeu.png |
| `functionmenu5ppet.layout` | FunctionMenu | CNDejaVu,DejaVu,main | `scripts/hud/MenuIconsPanel.cs` → FunctionMenu5pPet.layout (repli FunctionMenu) | implémenté | juste le jeu.png |
| `functionmenu_na.layout` | FunctionMenu | CNDejaVu,DejaVu,main | `scripts/hud/MenuIconsPanel.cs` (variantes régionales — même Root/FunctionMenu) | partiel | juste le jeu.png |
| `functionmenucn.layout` | FunctionMenu | CNDejaVu,CNDejaVu2,DejaVu | `scripts/hud/MenuIconsPanel.cs` (variantes régionales — même Root/FunctionMenu) | partiel | juste le jeu.png |
| `functionmenucnpet.layout` | FunctionMenu | CNDejaVu,CNDejaVu2,DejaVu | `scripts/hud/MenuIconsPanel.cs` (variantes régionales — même Root/FunctionMenu) | partiel | juste le jeu.png |
| `functionwnd.layout` | FunctionWnd | DejaVu,main | `scripts/hud/HotbarPanel.cs` → FunctionWnd + FucntionBarWnd.layout | implémenté | HotbarPanel.png, DoubleEXPbar.png |
| `localinfo.layout` | LocalInfo | skin (dejavu) | — | absent | — |
| `messagewnd.layout` | MessageWnd | skin (dejavu) | — (dialogue de saisie ; rôle partiellement couvert par FeatureDialogWindow) | partiel | — |
| `miniinfownd.layout` | MiniInfoWnd | DejaVuImage,Radar,main | `scripts/hud/TopLeftVitals.cs` → MiniInfoWnd.layout | implémenté | TopLeftVitals.png |
| `miniinfownd_equipmentswitch.layout` | MiniInfoWnd | DejaVuImage,Radar,main | — (variante equipment-switch) | absent | TopLeftVitals.png |
| `nitificationtipwnd.layout` | NotificationTipWnd | DejaVu,main | — | absent | NouveauSkillApresLevelUp.png |
| `notificationtipitem.layout` | NitificationTipItem | DejaVu,DejaVuImage | — | absent | NouveauSkillApresLevelUp.png |
| `notifywnd.layout` | NotifyWnd | skin (dejavu) | — | absent | NouveauSkillApresLevelUp.png |
| `petfunctionwnd.layout` | PetFunctionWnd | DejaVu,main | `scripts/hud/PetFunctionBar.cs` → PetFunctionWnd5p.layout (repli PetFunctionWnd) | implémenté | pet skillbar.png |
| `petfunctionwnd5p.layout` | PetFunctionWnd | DejaVu,main | `scripts/hud/PetFunctionBar.cs` → PetFunctionWnd5p.layout (repli PetFunctionWnd) | implémenté | pet skillbar.png |
| `popupmenu.layout` | PopupMenu | skin (dejavu) | `scripts/hud/TopLeftVitals.cs` (PopupMenu portrait : loot rules/reset) + popups custom | partiel | — |
| `radarui.layout` | Radar | Radar | `scripts/hud/MinimapPanel.cs` (variantes — même Root/Radar) | partiel | juste le jeu.png |
| `radarui_ver2.layout` | Radar | Radar | `scripts/hud/MinimapPanel.cs` → RadarUI_Ver2.layout | implémenté | juste le jeu.png |
| `screenmessagewnd.layout` | DWindow | DejaVu | `scripts/hud/ZoneBanner.cs` (custom — ShowSystemText/ShowZone) | implémenté | shot_0084_000349.png |
| `screenmessagewnd_na.layout` | DWindow | DejaVu,Injured | `scripts/hud/ZoneBanner.cs` (custom — ShowSystemText/ShowZone) | implémenté | shot_0084_000349.png |
| `slotbuff.layout` | SlotBuff | skin (dejavu) | `scripts/hud/BuffBar.cs` (variante / gabarit slot — slots de BuffWnd utilisés) | partiel | shot_0045_000016.png |
| `slotskill.layout` | SlotSkill | DejaVuImage,main | `scripts/hud/HotbarPanel.cs` (SlotImage de FunctionWnd + contenu custom) | partiel | HotbarPanel.png |
| `targetinfownd.layout` | TargetInfoWnd | DejaVu,main | `scripts/hud/TargetFrame.cs` → TargetInfoWnd.layout | implémenté | targetframe (Elite mob).png |
| `targetinfownd1.layout` | TargetInfoWnd | DejaVuImage,main | `scripts/hud/TargetFrame.cs` (variante — même Root/TargetInfoWnd) | partiel | targetframe (Elite mob).png |

### Groupe / équipe / raid — 29 layouts (4 implémentés / 4 partiels / 21 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `cnsearchteamcontrol.layout` | CNSearchTeamControl | DejaVu | — | absent | — |
| `cnsearchteaminstance.layout` | CNSearchTeamInstance | DejaVu,main | — | absent | — |
| `cnsearchteamnormal.layout` | CNSearchTeamNormal | DejaVu | — | absent | — |
| `cnsearchteamwnd.layout` | CNSearchTeamWnd | skin (dejavu) | — | absent | — |
| `cnteamlistwnd.layout` | CNTeamListWnd | DejaVu | — | absent | — |
| `contractwnd.layout` | Agreement | skin (dejavu) | — | absent | — |
| `expeditionframe.layout` | ExpeditionFrame | main | — | absent | — |
| `expeditionlist.layout` | ExpeditionList | DejaVu | — | absent | — |
| `expeditionwnd.layout` | ExpeditionWnd | skin (dejavu) | — | absent | — |
| `instancedungeoncontrol.layout` | InstancedungeonControl | DejaVu | — | absent | — |
| `partytimewnd.layout` | PartyTimeWnd | DejaVu | — | absent | — |
| `partywnd.layout` | party | DejaVu,DejaVuImage | `scripts/hud/PartyHud.cs` (custom — cadres TeamWndFrame) | implémenté | party UI.png |
| `raidwnd.layout` | RaidWnd | skin (dejavu) | — | absent | — |
| `raidwndframe.layout` | RaidWndFrame | main | — | absent | — |
| `recruit.layout` | Recruit | skin (dejavu) | `scripts/hud/SystemServiceWindows.cs::RecruitWindow` → Recruit.layout | implémenté | — |
| `searchslot.layout` | SearchSlot | DejaVu | — | absent | — |
| `searchteamwnd.layout` | SearchTeamWnd | DejaVu | — | absent | — |
| `searchteamwndv2.layout` | NewTeamWnd | DejaVu,main | — | absent | — |
| `setconditioninstance.layout` | SetConditionInstance | DejaVu,main | — | absent | — |
| `setconditionnormal.layout` | SetConditionNormal | skin (dejavu) | — | absent | — |
| `setconditionwnd.layout` | SetConditionWnd | skin (dejavu) | — | absent | — |
| `setconditionwnd2.layout` | SetConditionWnd | DejaVu | — | absent | — |
| `teamagreementdlg.layout` | 組隊申請 | skin (dejavu) | `scripts/hud/PartyHud.cs` (popup invitation custom) | implémenté | — |
| `teammemberlistdlg.layout` | 隊伍清單 | DejaVu | `scripts/hud/PartyHud.cs` (liste membres couvre le rôle) | partiel | — |
| `teamrecruitsetting.layout` | 隊伍招募設定 | DejaVu,main | — | absent | — |
| `teamwnd.layout` | TeamWnd | skin (dejavu) | `scripts/hud/PartyHud.cs` (liste membres ; fenêtre équipe complète absente) | partiel | party UI.png |
| `teamwnd1.layout` | TeamWnd | skin (dejavu) | `scripts/hud/PartyHud.cs` (liste membres ; fenêtre équipe complète absente) | partiel | party UI.png |
| `teamwndframe.layout` | TeamWndFrame | main | `scripts/hud/PartyHud.cs` → TeamWndFrame.layout | implémenté | party UI.png |
| `teamwndframe1.layout` | TeamWndFrame | main | `scripts/hud/PartyHud.cs` (variante — même Root/TeamWndFrame) | partiel | party UI.png |

### Quêtes & dialogues PNJ — 12 layouts (4 implémentés / 5 partiels / 3 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `autoquestlist.layout` | MissionList | skin (dejavu) | `scripts/hud/QuestWindow.cs` (journal couvre la liste — Root/MissionList absent) | partiel | quest log 2.png |
| `missionlog.layout` | Mission | DejaVu | `scripts/hud/QuestWindow.cs` → MissionLog.layout | implémenté | QuestLog.png |
| `npctalkwnd.layout` | Title | skin (dejavu) | `scripts/hud/NpcDialogueWindow.cs` → NpcTalkWnd.layout | implémenté | npc talk propositions.png |
| `questautonotifywnd.layout` | QuestAutoNotifyWnd | DejaVu | — | absent | — |
| `questexp.layout` | QuestExp | skin (dejavu) | — | absent | — |
| `questitem.layout` | QuestItem | DejaVuImage | `scripts/hud/QuestSheetWindow.cs` (icônes récompenses custom) | partiel | quest UI.png |
| `questtracewnd.layout` | QuestTraceWnd | DejaVu | `scripts/hud/QuestTracker.cs` → QuestTraceWnd.layout | implémenté | QuestTracker.png |
| `questwnd.layout` | Tatle  | skin (dejavu) | `scripts/hud/QuestSheetWindow.cs` → QuestWnd.layout | implémenté | quest UI.png, proposition nouvelle quete.png |
| `scriptslot.layout` | 1234 | skin (dejavu) | `scripts/hud/NpcDialogueWindow.cs` (options custom — fenêtre script dédiée absente) | partiel | — |
| `scriptwnd.layout` | Script | DejaVu,DejaVuImage | `scripts/hud/NpcDialogueWindow.cs` (options custom — fenêtre script dédiée absente) | partiel | — |
| `slotquest.layout` | AutoQuest | skin (dejavu) | `scripts/hud/QuestTracker.cs` (entrées tracker custom) | partiel | QuestTracker.png |
| `storywnd.layout` | StoryWnd | skin (dejavu) | — | absent | — |

### Classes (jobwnd) & expertise — 32 layouts (20 implémentés / 8 partiels / 4 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `changetypeconfirm.layout` | CommonDlg | skin (dejavu) | `scripts/hud/ClassChangeDialog.cs` (flux custom — CommonDlg non chargé) | partiel | — |
| `changetypewnd.layout` | ChangeTypeWnd | skin (dejavu) | `scripts/hud/ClassChangeDialog.cs` + `scripts/hud/ClassWindow.cs` (« Transform! », C→S 168) | implémenté | ClassPanel-ChangementDeClasse.png |
| `cooperation_ver2.layout` | CooperationNA | DejaVu | `scripts/hud/ExpertiseWindows.cs` (custom — « Class Ability Bonus ») | implémenté | panel primaire d'expertise d'une classe.png, second panel expertise d'une classe.png |
| `cooperationtab.layout` | CooperationTab | DejaVu | `scripts/hud/ExpertiseWindows.cs` (custom — « Class Ability Bonus ») | implémenté | panel primaire d'expertise d'une classe.png, second panel expertise d'une classe.png |
| `jobcurrentwnd.layout` | JobCurrent | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (onglet License knowledge, modèle JobCurrentWnd_NA) | implémenté | ClassePanel-ClassTalent&Certificat.png |
| `jobcurrentwnd2.layout` | JobCurrent | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (onglet License knowledge, modèle JobCurrentWnd_NA) | implémenté | ClassePanel-ClassTalent&Certificat.png |
| `jobcurrentwnd_na.layout` | JobCurrent | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (onglet License knowledge, modèle JobCurrentWnd_NA) | implémenté | ClassePanel-ClassTalent&Certificat.png |
| `jobglyph.layout` | JobGlyph | DejaVu,JobGlyph | — | absent | — |
| `jobglyph_na.layout` | JobGlyph | DejaVu,JobGlyph | — | absent | — |
| `jobinfo.layout` | JobInfo | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (onglet Class News — fiche + stats) | implémenté | ClassePanel-ClassNews.png, HoverToolTipClasse.png |
| `joblistwnd.layout` | JobList | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (colonne classes + onglet Class News, modèle JobListWnd_NA) | implémenté | ClassePanel-ClassNews.png |
| `joblistwnd2.layout` | JobList | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (colonne classes + onglet Class News, modèle JobListWnd_NA) | implémenté | ClassePanel-ClassNews.png |
| `joblistwnd3.layout` | JobList | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (colonne classes + onglet Class News, modèle JobListWnd_NA) | implémenté | ClassePanel-ClassNews.png |
| `joblistwnd_na.layout` | JobList | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (colonne classes + onglet Class News, modèle JobListWnd_NA) | implémenté | ClassePanel-ClassNews.png |
| `jobpointawarditem.layout` | JobPointAwardItem | skin (dejavu) | `scripts/hud/ExpertiseWindows.cs` (lignes custom) | partiel | ClassAbilitybonuslogo.png |
| `jobpointawarditem_na.layout` | JobPointAwardItem | main | `scripts/hud/ExpertiseWindows.cs` (lignes custom) | partiel | ClassAbilitybonuslogo.png |
| `jobpointlist.layout` | JobPointList | DejaVu | `scripts/hud/ExpertiseWindows.cs` (custom — « Class Ability Bonus ») | implémenté | panel primaire d'expertise d'une classe.png, second panel expertise d'une classe.png |
| `jobpointlist2.layout` | JobPointList2 | DejaVu | `scripts/hud/ExpertiseWindows.cs` (custom — « Class Ability Bonus ») | implémenté | panel primaire d'expertise d'une classe.png, second panel expertise d'une classe.png |
| `jobskill_angel.layout` | JobSkillAngel | DejaVu,DejaVuImage | — (classe angel) | absent | — |
| `jobskill_na.layout` | JobSkillNA | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (grilles de sorts custom) | partiel | ClassePanel.png |
| `jobskill_na02.layout` | JobSkillNA | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (grilles de sorts custom) | partiel | ClassePanel.png |
| `jobwnd.layout` | 職業介面 | skin (dejavu) | `scripts/hud/ClassWindow.cs` (custom — coquille + onglets reproduits) | implémenté | ClassePanel.png |
| `jobwnd_na.layout` | 職業介面 | DejaVu | `scripts/hud/ClassWindow.cs` (custom — coquille + onglets reproduits) | implémenté | ClassePanel.png |
| `krjoblistwnd2.layout` | JobList | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (colonne classes + onglet Class News, modèle JobListWnd_NA) | implémenté | ClassePanel-ClassNews.png |
| `newjoblist.layout` | JobList | DejaVu | `scripts/hud/ClassWindow.cs` (colonne classes + onglet Class News, modèle JobListWnd_NA) | implémenté | ClassePanel-ClassNews.png |
| `newknowledge.layout` | NewKnowledge | DejaVu | `scripts/hud/ClassWindow.cs` (arbre de talents, onglet License knowledge) | implémenté | ClassePanel-ClassTalent&Certificat.png |
| `newlicence.layout` | NewLicence | DejaVu,DejaVuImage | `scripts/hud/ClassWindow.cs` (grille certificats, onglet License knowledge) | implémenté | case pour certificat.png |
| `newskilllist.layout` | NewSkillList | DejaVu | `scripts/hud/ClassWindow.cs` (onglet Class Skills) | implémenté | ClassePanel.png |
| `slotjob.layout` | SlotJob | DejaVu,DejaVuImage,main | `scripts/hud/ClassWindow.cs` (slots custom) | partiel | ClassePanel.png |
| `slotjobver2.layout` | SlotJobVer2 | DejaVuImage,main | `scripts/hud/ClassWindow.cs` (slots custom) | partiel | ClassePanel.png |
| `slotlicense.layout` | SlotLicense | main | `scripts/hud/ClassWindow.cs` (grille certificats custom) | partiel | case pour certificat.png |
| `slotlifelong.layout` | SlotLifeLong | main | — | absent | — |

### Inventaire & équipement — 30 layouts (11 implémentés / 13 partiels / 6 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `bagmanagement2.layout` | 04 | DejaVu | `scripts/hud/InventoryWindow.cs` (bouton « Backpack manager » → notice seulement) | partiel | — |
| `characterwnd.layout` | CharacterUI | skin (dejavu) | `scripts/hud/ProfileWindow.cs` (custom — « Character Info », touche C) | implémenté | profile.png |
| `characterwnd_new2.layout` | CharacterUI | skin (dejavu) | `scripts/hud/ProfileWindow.cs` (custom — « Character Info », touche C) | implémenté | profile.png |
| `cncharacterui.layout` | CNCharacterUI | DejaVu,DejaVuImage | `scripts/hud/ProfileWindow.cs` (custom — « Character Info », touche C) | implémenté | profile.png |
| `dividedlg.layout` | DivideDlg | DejaVuImage | `scripts/hud/InventoryWindow.cs` (popup split custom → NCID 84) | implémenté | — |
| `dressroomui.layout` | DressRoomUI | DejaVu | `scripts/hud/SystemServiceWindows.cs::FastDressWindow` → DressRoomUI.layout | implémenté | — |
| `dyeingwindow.layout` | DyeingWindow | DejaVu | `scripts/hud/ItemPreviewWindow.cs` (teintures en préview — fenêtre dye absente) | partiel | — |
| `equipmentwnd.layout` | EquipmentWnd | DejaVu,DejaVuImage | `scripts/hud/ProfileWindow.cs` + `scripts/hud/EquipSlotsPanel.cs` (custom) | implémenté | profile.png |
| `equipmentwnd2.layout` | EquipmentWnd | DejaVu,DejaVuImage | `scripts/hud/ProfileWindow.cs` + `scripts/hud/EquipSlotsPanel.cs` (custom) | implémenté | profile.png |
| `equipmentwnd_new2.layout` | EquipmentWnd | DejaVu,DejaVuImage | `scripts/hud/ProfileWindow.cs` → EquipmentWnd_new2.layout (intégré) | implémenté | profile.png |
| `equipmentwndswitch.layout` | EquipmentWndSwitch | DejaVu,DejaVuImage | — | absent | — |
| `growthequipment.layout` | GrowthEquipment | DejaVu | — | absent | — |
| `growthequipmentitem.layout` | GrowthEquipmentItem | skin (dejavu) | — | absent | — |
| `inventorywnd.layout` | 背包 | CNDejaVu,DejaVu,DejaVuImage | `scripts/hud/InventoryWindow.cs` (custom — grille EeSkin) | implémenté | shot_0017_235743.png |
| `inventorywnd_cn.layout` | Bag | CNDejaVu,DejaVuImage,Radar | `scripts/hud/InventoryWindow.cs` (variantes régionales — même Root/Bag) | partiel | shot_0017_235743.png |
| `inventorywnd_na.layout` | Bag | CNDejaVu,DejaVuImage,Radar | `scripts/hud/InventoryWindow.cs` (variantes régionales — même Root/Bag) | partiel | shot_0017_235743.png |
| `itemdetail.layout` | ItemDeTail | skin (dejavu) | `scripts/hud/ItemTooltip.cs` (tooltip custom couvre le rôle) | partiel | item hover (weapon).png |
| `iteminheritwnd.layout` | IteminheritWnd | DejaVu,DejaVuImage | — | absent | — |
| `krbagmanagement2.layout` | 04 | DejaVu | `scripts/hud/InventoryWindow.cs` (bouton « Backpack manager » → notice seulement) | partiel | — |
| `krcharacterwnd.layout` | CharacterUI | skin (dejavu) | `scripts/hud/ProfileWindow.cs` (custom — « Character Info », touche C) | implémenté | profile.png |
| `krequipmentwnd.layout` | EquipmentWnd | DejaVu,DejaVuImage | `scripts/hud/ProfileWindow.cs` + `scripts/hud/EquipSlotsPanel.cs` (custom) | implémenté | profile.png |
| `moneyconfirmitem.layout` | MoneyConfirmItem | DejaVuImage | — | absent | — |
| `newequipment.layout` | NewEquipment | DejaVu,DejaVuImage | — | absent | — |
| `otherequipmentwnd.layout` | OtherEquipmentWnd | DejaVu,DejaVuImage | `scripts/hud/InspectWindow.cs` (liste itemUid — panneau visuel absent) | partiel | — |
| `otherequipmentwnd2.layout` | OtherEquipmentWnd | DejaVu,DejaVuImage | `scripts/hud/InspectWindow.cs` (liste itemUid — panneau visuel absent) | partiel | — |
| `otherequipmentwndcn.layout` | OtherEquipmentWndCN | DejaVu,DejaVuImage | `scripts/hud/InspectWindow.cs` (liste itemUid — panneau visuel absent) | partiel | — |
| `pvpequipmentscanner.layout` | PVPLookEquipWnd | DejaVu,DejaVuImage | `scripts/hud/InspectWindow.cs` (571 PVPRankLookEquip) | partiel | — |
| `slotmodel.layout` | SlotModel | skin (dejavu) | `scripts/hud/ItemPreviewWindow.cs` + `scripts/hud/EquipSlotsPanel.cs` (slots custom) | partiel | — |
| `tipdetail.layout` | TipDetail | skin (dejavu) | `scripts/hud/ItemTooltip.cs` (tooltip custom couvre le rôle) | partiel | item hover (weapon).png |
| `usbagmanagement2.layout` | BagManagement | DejaVu | `scripts/hud/InventoryWindow.cs` (bouton « Backpack manager » → notice seulement) | partiel | — |

### Stockage & hôtel — 11 layouts (7 implémentés / 0 partiels / 4 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `familystoragelogwnd.layout` | FBHistory | skin (dejavu) | `scripts/hud/FamilyStorageWindow.cs` (journal 506 custom) | implémenté | — |
| `familystoragetab1.layout` | FBPage1 | DejaVuImage | `scripts/hud/FamilyStorageWindow.cs` (pages 40 slots custom) | implémenté | — |
| `familystoragetab2.layout` | FBPage2 | DejaVuImage | `scripts/hud/FamilyStorageWindow.cs` (pages 40 slots custom) | implémenté | — |
| `familystoragetab3.layout` | FBPage3 | DejaVuImage | `scripts/hud/FamilyStorageWindow.cs` (pages 40 slots custom) | implémenté | — |
| `familystoragewnd.layout` | 背包 | skin (dejavu) | `scripts/hud/FamilyStorageWindow.cs` → FamilyStorageWnd.layout | implémenté | — |
| `hotelprivilegedlg.layout` | HotelPrivilege | DejaVuImage | — | absent | — |
| `hotelstoragewnd.layout` | HotelStorage | DejaVu,DejaVuImage | — | absent | — |
| `hotelwnd.layout` | HotelWnd | DejaVu,DejaVuImage | `scripts/hud/HotelWindow.cs` → HotelWnd.layout | implémenté | — |
| `playerroomwnd.layout` | PlayerRoomWnd | skin (dejavu) | — | absent | — |
| `storagerentdlg.layout` | StorageRentDlg | skin (dejavu) | — | absent | — |
| `storageui.layout` | StorageUI | skin (dejavu) | `scripts/hud/BankWindow.cs` → StorageUI.layout (+ `scripts/hud/NeobankWindow.cs` repli) | implémenté | — |

### Commerce (shop/trade/étal/HDV/courrier/item mall) — 30 layouts (14 implémentés / 7 partiels / 9 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `auctionbuywnd.layout` | AuctionBuyWnd | DejaVu,DejaVuImage | `scripts/hud/AuctionWindow.cs` (onglets buy/sell custom) | implémenté | Auction (hotel de vente) multiple line item on sell time and colonne name.png |
| `auctionsellwnd.layout` | AuctionSellWnd | DejaVu,DejaVuImage | `scripts/hud/AuctionWindow.cs` (onglets buy/sell custom) | implémenté | Auction (hotel de vente) multiple line item on sell time and colonne name.png |
| `auctionwnd.layout` | 拍賣場 | skin (dejavu) | `scripts/hud/AuctionWindow.cs` → AuctionWnd.layout | implémenté | Auction (hotel de vente) line item on sell time.png |
| `buymsgdlg.layout` | 彈出對話框 | skin (dejavu) | — (popup message étal — VendingWindow couvre l'étal) | partiel | — |
| `cnauctionbuywnd.layout` | AuctionBuyWnd | DejaVu,DejaVuImage | `scripts/hud/AuctionWindow.cs` (variante CN) | partiel | — |
| `cnitemmall.layout` | ItemMal | CNDejaVu2,DejaVu,DejaVuImage | `scripts/hud/ItemMallWindow.cs` (variante CN — même Root/ItemMal) | partiel | ItemMallPanel.png |
| `cnspecialcoinitem.layout` | SpecialCoinItem | skin (dejavu) | — | absent | — |
| `cnspecialcoinui.layout` | SpecialCoin | DejaVu,DejaVuImage | — | absent | — |
| `itemmaillitem.layout` | ItemMallItem | DejaVu,DejaVuImage | `scripts/hud/ItemMallWindow.cs` → ItemMaillItem.layout (lignes) | implémenté | ItemMallPanel.png |
| `itemmailltopitem.layout` | ItemMallTopItem | DejaVuImage | `scripts/hud/ItemMallWindow.cs` (lignes custom) | partiel | — |
| `itemmall.layout` | ItemMal | DejaVu,DejaVuImage | `scripts/hud/ItemMallWindow.cs` → ItemMall.layout | implémenté | ItemMallPanel.png |
| `itemmallmailwnd.layout` | ItemMaillMailWnd | DejaVu | `scripts/hud/ItemMallWindow.cs` (envoi cadeau — champ destinataire) | partiel | — |
| `itemmallquestitem.layout` | QuestItem | DejaVuImage | — | absent | — |
| `itemmallserial.layout` | ItemMallSerial | skin (dejavu) | — | absent | — |
| `mailitem.layout` | mailitem | skin (dejavu) | `scripts/hud/MailWindow.cs` (panes liste/lecture/envoi custom) | implémenté | Mail2.png |
| `maillistwnd.layout` | MaillistWnd | DejaVu | `scripts/hud/MailWindow.cs` (panes liste/lecture/envoi custom) | implémenté | Mail2.png |
| `mailreadwnd.layout` | MailReadWnd | DejaVuImage | `scripts/hud/MailWindow.cs` (panes liste/lecture/envoi custom) | implémenté | Mail2.png |
| `mailsendwnd.layout` | MailSendWnd | DejaVu,DejaVuImage | `scripts/hud/MailWindow.cs` (panes liste/lecture/envoi custom) | implémenté | Mail2.png |
| `mailwnd.layout` | MailWnd | skin (dejavu) | `scripts/hud/MailWindow.cs` → MailWnd.layout | implémenté | Mail.png |
| `paysystemwnd.layout` | PaySystem | skin (dejavu) | `scripts/hud/FeatureDialogWindow.cs` (OpenPaySystem → texte) | partiel | — |
| `shopitem.layout` | ShopItem | DejaVuImage | `scripts/hud/ShopWindow.cs` (lignes custom) | partiel | tradeNPCSystem.png |
| `shopwnd.layout` | shop | DejaVu,DejaVuImage | `scripts/hud/ShopWindow.cs` → ShopWnd.layout | implémenté | tradeNPCSystem.png |
| `specialcoinitem.layout` | SpecialCoinItem | skin (dejavu) | — | absent | — |
| `specialcoinps.layout` | SpecialCoinPS | skin (dejavu) | — | absent | — |
| `specialcoinui.layout` | SpecialCoin | DejaVu | — | absent | — |
| `specialcoinui2.layout` | SpecialCoin | DejaVu,DejaVuImage | — | absent | — |
| `specialcoinui2_na.layout` | SpecialCoin | DejaVu | — | absent | — |
| `stallbuy.layout` | XXX的個人商店 | DejaVu,DejaVuImage | `scripts/hud/VendingWindow.cs` → StallUI + StallBuy.layout | implémenté | — |
| `stallui.layout` | StallUI | DejaVu,DejaVuImage | `scripts/hud/VendingWindow.cs` → StallUI + StallBuy.layout | implémenté | — |
| `tradewnd.layout` | TradeWnd | DejaVuImage | `scripts/hud/TradeWindow.cs` → TradeWnd.layout | implémenté | — |

### Social & guilde (family) — 40 layouts (7 implémentés / 4 partiels / 29 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `addressbookwnd.layout` | AddressBookWnd | DejaVu | `scripts/hud/SocialWindow.cs` (amis + blacklist custom) | implémenté | CommunityPanel-SearchPlayers.png |
| `cnfamilyemblem.layout` | FamilyEmblem | skin (dejavu) | — | absent | — |
| `cnfamilyinfownd.layout` | FamilyInfo | DejaVu,DejaVuImage | `scripts/hud/GuildWindow.cs` (variantes — même Root/FamilyInfo) | partiel | — |
| `cnfamilyorderwnd.layout` | FamilyOrder | DejaVu | — | absent | — |
| `communitywnd.layout` | Community | DejaVu,DejaVuImage | `scripts/hud/SocialWindow.cs` → CommunityWnd.layout | implémenté | CommunityPanel.png |
| `familyachievement.layout` | Achievement | skin (dejavu) | — | absent | — |
| `familyadministration.layout` | FamilyAdministrationWnd | DejaVu,DejaVuImage | — | absent | — |
| `familyappwnd.layout` | FamilyAppWnd | DejaVu | — | absent | — |
| `familybuilding.layout` | FamilyBuildingSetWnd | DejaVu,main | — | absent | — |
| `familybuilding_forest.layout` | FamilyBuildingSetWnd | DejaVu | — | absent | — |
| `familybuilding_mountain.layout` | FamilyBuildingSetWnd | DejaVu | — | absent | — |
| `familybulletindlg.layout` | CommonDlg | skin (dejavu) | `scripts/hud/GuildWindow.cs` (onglet Annonce custom) | partiel | — |
| `familycenter.layout` | FamilyBuildingCenter | skin (dejavu) | — | absent | — |
| `familycommentdlg.layout` | CommonDlg | skin (dejavu) | — | absent | — |
| `familydonatedlg.layout` | CommonDlg | DejaVuImage | `scripts/hud/GuildWindow.cs` (champ Donate → zone 159) | implémenté | — |
| `familyfindinginfo.layout` | FamilyFindingInfo | DejaVu,DejaVuImage | — | absent | FindGuild.png |
| `familyfindingwind.layout` | FamilyFindingWind | skin (dejavu) | — | absent | FindGuild.png |
| `familyinfownd.layout` | FamilyInfo | DejaVu,DejaVuImage | `scripts/hud/GuildWindow.cs` (variantes — même Root/FamilyInfo) | partiel | — |
| `familyinfownd_na.layout` | FamilyInfo | DejaVu,DejaVuImage | `scripts/hud/GuildWindow.cs` → FamilyInfoWnd_NA.layout | implémenté | — |
| `familyjobnamedlg.layout` | CommonDlg | skin (dejavu) | — | absent | — |
| `familymemberfindingwnd.layout` | TownFinding | DejaVu | — | absent | FindGuild.png |
| `familymessagedlg.layout` | CommonDlg | skin (dejavu) | — | absent | — |
| `familymessagewnd.layout` | FamilyMessage | DejaVu | — | absent | — |
| `familymessagewnd_na.layout` | FamilyMessage | DejaVu | — | absent | — |
| `familyorderwnd.layout` | FamilyOrder | DejaVu | — | absent | — |
| `familyorderwnd_na.layout` | FamilyOrder | DejaVu | — | absent | — |
| `familyprivilege.layout` | FamilyPrivilege | skin (dejavu) | — | absent | — |
| `familysetprivilegedlg.layout` | CommonDlg | skin (dejavu) | — | absent | — |
| `familytownlistwnd.layout` | TownList | DejaVu | — | absent | shot_0062_000149.png |
| `familywnd.layout` | FamilyWnd | skin (dejavu) | `scripts/hud/GuildWindow.cs` (coquille onglets reproduite) | implémenté | — |
| `familywnd_na.layout` | FamilyWnd | skin (dejavu) | `scripts/hud/GuildWindow.cs` (coquille onglets reproduite) | implémenté | — |
| `krcommunitywnd.layout` | Community | DejaVu,DejaVuImage | `scripts/hud/SocialWindow.cs` (variante KR — même Root/Community) | partiel | CommunityPanel.png |
| `likehatewnd.layout` | LikeHateWnd | skin (dejavu) | — (onglet Fame de ProfileWindow = stub sans données) | absent | profile-fame onglet.png |
| `personalcardviewwnd.layout` | PersonalCardViewWnd | DejaVu,DejaVuImage | — (490 playercard → PlayerProfileState, pas d'UI) | absent | — |
| `personalcardwnd.layout` | PersonalCardWnd | DejaVu,DejaVuImage | — (490 playercard → PlayerProfileState, pas d'UI) | absent | — |
| `prestigeitem.layout` | PrestigeItem | DejaVu | — (prestige → PlayerProfileState, pas d'UI) | absent | — |
| `prestigewnd.layout` | Appellation | DejaVu | — (prestige → PlayerProfileState, pas d'UI) | absent | — |
| `prestigewnd_new.layout` | PrestigeWnd | DejaVu | — (prestige → PlayerProfileState, pas d'UI) | absent | — |
| `reputation.layout` | Reputation | skin (dejavu) | — (onglet Fame de ProfileWindow = stub sans données) | absent | profile-fame onglet.png |
| `wedding.layout` | Wedding | DejaVu | `scripts/hud/CoupleWindow.cs` (liste cérémonies, modèle Wedding.layout) | implémenté | — |

### Succès, activités, destin & collection — 37 layouts (7 implémentés / 12 partiels / 18 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `achievement.layout` | Achievement | DejaVu,DejaVuImage | `scripts/hud/AchievementWindow.cs` (variante v1 — _ver2 chargée) | partiel | Profile-Achievements.png |
| `achievement_ver2.layout` | Achievement | DejaVu,DejaVuImage | `scripts/hud/AchievementWindow.cs` → Achievement_Ver2.layout | implémenté | Profile-Achievements.png |
| `achievementgroup.layout` | AchievementGroup | DejaVu,DejaVuImage | `scripts/hud/AchievementWindow.cs` (arbre catégories + lignes custom) | partiel | Profile-Achievements2.png |
| `achievementinputwnd.layout` | AchievementInputWnd | skin (dejavu) | — | absent | — |
| `achievementreport.layout` | AchievementReport | skin (dejavu) | — | absent | — |
| `achievementtipwnd.layout` | AchievementTipWnd | DejaVuImage | `scripts/hud/AchievementWindow.cs` + `scripts/hud/ItemTooltip.cs` (tooltips custom) | partiel | — |
| `achievementtipwnd_ver2.layout` | AchievementTipWnd | DejaVuImage | `scripts/hud/AchievementWindow.cs` + `scripts/hud/ItemTooltip.cs` (tooltips custom) | partiel | — |
| `achievementviewwnd.layout` | AchievementViewWnd | DejaVu | `scripts/hud/AchievementWindow.cs` (arbre catégories + lignes custom) | partiel | Profile-Achievements2.png |
| `activitiesitem.layout` | ActitviesLable | DejaVu,main | — (559 activités → PlayerProfileState, pas d'UI) | absent | — |
| `activitieswnd.layout` | 活動 | DejaVu | `scripts/hud/DailyRewardWindow.cs` (seul le sous-onglet Everyday couvert — coquille Activities absente) | partiel | daily reward.png |
| `adventurelogwnd.layout` | 冒險日誌 | skin (dejavu) | — | absent | — |
| `appellationwnd.layout` | Appellation | DejaVu | `scripts/hud/ProfileWindow.cs` (onglet Title — C→S 93) | implémenté | profile-Title.png |
| `appellationwnd_ver2.layout` | AppellationWnd | DejaVu | `scripts/hud/ProfileWindow.cs` (onglet Title — C→S 93) | implémenté | profile-Title.png |
| `cnactivitieswnd.layout` | 活動 | DejaVu | `scripts/hud/DailyRewardWindow.cs` (seul le sous-onglet Everyday couvert — coquille Activities absente) | partiel | daily reward.png |
| `collectionset.layout` | CollectionSet | skin (dejavu) | `scripts/hud/ArchiveWindow.cs` (dépôt drag item — fenêtres dédiées absentes) | partiel | — |
| `collectionwnd.layout` | collectionWnd | DejaVu | `scripts/hud/ArchiveWindow.cs` (custom — « Archive », C→S 152/150/151) | implémenté | — |
| `collectwnd.layout` | CollectWnd | DejaVu,DejaVuImage | `scripts/hud/ArchiveWindow.cs` (dépôt drag item — fenêtres dédiées absentes) | partiel | — |
| `evaluatememberwnd.layout` | EvaluateMemberWnd | skin (dejavu) | — | absent | — |
| `evaluaterankwnd.layout` | EvaluateRankWnd | DejaVu | — | absent | — |
| `evaluatetimewnd.layout` | EvaluateTimeWnd | DejaVu | — | absent | — |
| `evaluatewnd.layout` | EvaluateWnd | DejaVu,DejaVuImage | — | absent | — |
| `fateroadinfo.layout` | Root\FateRoadInfo | DejaVu,Radar,main | `scripts/hud/DestinyWindow.cs` (bandeau custom — panneaux info/tip absents) | partiel | — |
| `fateroadtipwnd.layout` | FateRoadTipWnd | DejaVuImage,Radar | `scripts/hud/DestinyWindow.cs` (bandeau custom — panneaux info/tip absents) | partiel | — |
| `fateroadwnd.layout` | FateRoadWnd | CDAni,Radar | `scripts/hud/DestinyWindow.cs` → FateRoadWnd.layout (plateau) | implémenté | — |
| `fateroadwndna2.layout` | 推薦任務 | CDAni,DejaVu,Radar | `scripts/hud/DestinyWindow.cs` (variante NA — même Root/FateRoadWnd) | partiel | — |
| `guidewnd.layout` | GuideWnd | newhand | — (551 guide → PlayerProfileState, pas d'UI) | absent | — |
| `krcollectionwnd.layout` | collectionWnd | DejaVu | `scripts/hud/ArchiveWindow.cs` (custom — « Archive », C→S 152/150/151) | implémenté | — |
| `lifeskillitem.layout` | LifeSkillItem | skin (dejavu) | — (543 lifeskill → PlayerProfileState, pas d'UI) | absent | — |
| `loginreward.layout` | x推薦任務 | skin (dejavu) | `scripts/hud/DailyRewardWindow.cs` (custom, modèle LoginReward.layout) | implémenté | daily reward.png |
| `monsterinfo.layout` | MonsterInfo | DejaVu | — | absent | — |
| `monsterinfoclassitem.layout` | PrestigeItem | DejaVu | — | absent | — |
| `monsterinfoitem.layout` | PrestigeItem | DejaVu | — | absent | — |
| `rankevaluatewnd.layout` | RankEvaluateWnd | DejaVu | — | absent | — |
| `recommendactivitieswnd.layout` | 推薦任務 | DejaVu | — (559 activités → PlayerProfileState, pas d'UI) | absent | — |
| `reportwnd.layout` | ReportWnd | DejaVu,main | — | absent | — |
| `starway_v2.layout` | 星光大道 | DejaVu | — (609 starway → PlayerProfileState, pas d'UI) | absent | — |
| `tutorailwnd.layout` | TutorailWnd | skin (dejavu) | — (551 guide → PlayerProfileState, pas d'UI) | absent | — |

### Familier (pet/elf) — 8 layouts (5 implémentés / 2 partiels / 1 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `5ppetitem.layout` | PetItem | DejaVuImage | `scripts/hud/ElfWindow.cs` (lignes liste custom) | partiel | — |
| `changepetnamewnd.layout` | ChangePetNameWnd | skin (dejavu) | `scripts/hud/ElfWindow.cs` (liste + popups Advanced/Rename custom) | implémenté | PetsPanel.png |
| `coridewnd.layout` | CoRideWnd | skin (dejavu) | — | absent | — |
| `petitem.layout` | PetItem | skin (dejavu) | `scripts/hud/ElfWindow.cs` (lignes liste custom) | partiel | — |
| `petlistwnd.layout` | PetList | skin (dejavu) | `scripts/hud/ElfWindow.cs` (liste + popups Advanced/Rename custom) | implémenté | PetsPanel.png |
| `petupgradewnd.layout` | PetUparadeWnd | DejaVu,DejaVuImage | `scripts/hud/ElfWindow.cs` (liste + popups Advanced/Rename custom) | implémenté | PetsPanel.png |
| `petwnd.layout` | Pet | DejaVu,DejaVuImage | `scripts/hud/ElfWindow.cs` (PetWndV3 Reforged absent → chrome custom) | implémenté | PetsPanel.png |
| `petwndv2.layout` | Pet | DejaVu,DejaVuImage | `scripts/hud/ElfWindow.cs` (PetWndV3 Reforged absent → chrome custom) | implémenté | PetsPanel.png |

### Craft, renforcement & Magic Cube — 22 layouts (2 implémentés / 8 partiels / 12 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `combinewnd.layout` | CombineWnd | DejaVu,DejaVuImage | — (bouton « Fusion » du sac → notice seulement) | absent | FusionPanel Craft (et guild npc crafter magic pool, forger...).png |
| `engineeringstudy.layout` | EngineeringStudy | DejaVu,DejaVuImage | — | absent | Blueprint.png |
| `gembreaker.layout` | Root\GemBreaker | skin (dejavu) | — | absent | — |
| `jewelrystudy.layout` | JewlryStudy | DejaVu,DejaVuImage | — | absent | Blueprint.png |
| `krstrengthenwnd.layout` | Strengthen | DejaVu,DejaVuImage | `scripts/hud/FeatureDialogWindow.cs` (« Enclume ouverte » texte) + `scripts/hud/CraftWindow.cs` (cast bar) | partiel | FusionPanel Craft (et guild npc crafter magic pool, forger...).png |
| `magicboxwnd.layout` | MagicBox | DejaVu,main | `scripts/hud/FeatureDialogWindow.cs` (612/613 → dialogue générique) | partiel | — |
| `magiccube00.layout` | x布諾拉克方塊x | DejaVu,DejaVuImage,MagicCube,main | — | absent | — |
| `magiccube01.layout` | MagicCube01Wnd | DejaVu,DejaVuImage | — | absent | — |
| `magiccube02.layout` | xAuxilliaryWndx | DejaVu,DejaVuImage | — | absent | — |
| `magiccube03.layout` | CommonDlg | skin (dejavu) | — | absent | — |
| `magiccubeitem.layout` | CubeItem | skin (dejavu) | — | absent | — |
| `newmagiccuberecipedetailwnd.layout` | NewMagicCubeRecipeDetailWnd | skin (dejavu) | — | absent | — |
| `newmagiccuberecipewnd.layout` | x新布蘭德方塊詳細資料 | skin (dejavu) | — | absent | — |
| `newmagiccubewnd.layout` | x新布蘭德方塊 | CNDejaVu2,DejaVu,DejaVuImage,MagicCube | — | absent | — |
| `strengthenfactory.layout` | StrengthenFactory | DejaVu | `scripts/hud/FeatureDialogWindow.cs` (« Enclume ouverte » texte) + `scripts/hud/CraftWindow.cs` (cast bar) | partiel | FusionPanel Craft (et guild npc crafter magic pool, forger...).png |
| `strengthenslot.layout` | StrengthenSlot | DejaVu | `scripts/hud/FeatureDialogWindow.cs` (« Enclume ouverte » texte) + `scripts/hud/CraftWindow.cs` (cast bar) | partiel | FusionPanel Craft (et guild npc crafter magic pool, forger...).png |
| `strengthenwnd.layout` | Strengthen | DejaVu,DejaVuImage | `scripts/hud/FeatureDialogWindow.cs` (« Enclume ouverte » texte) + `scripts/hud/CraftWindow.cs` (cast bar) | partiel | FusionPanel Craft (et guild npc crafter magic pool, forger...).png |
| `strengthenwnd14.layout` | Strengthen | DejaVu,DejaVuImage | `scripts/hud/FeatureDialogWindow.cs` (« Enclume ouverte » texte) + `scripts/hud/CraftWindow.cs` (cast bar) | partiel | FusionPanel Craft (et guild npc crafter magic pool, forger...).png |
| `strengthenwnd_na.layout` | Strengthen | DejaVu,DejaVuImage | `scripts/hud/FeatureDialogWindow.cs` (« Enclume ouverte » texte) + `scripts/hud/CraftWindow.cs` (cast bar) | partiel | FusionPanel Craft (et guild npc crafter magic pool, forger...).png |
| `strengthenwnd_na_20.layout` | Strengthen | DejaVu,DejaVuImage | `scripts/hud/FeatureDialogWindow.cs` (« Enclume ouverte » texte) + `scripts/hud/CraftWindow.cs` (cast bar) | partiel | FusionPanel Craft (et guild npc crafter magic pool, forger...).png |
| `worklistwnd.layout` | Work | skin (dejavu) | `scripts/hud/CraftWindow.cs` (custom — ordres de travail NPC 12-17) | implémenté | — |
| `workresultwnd.layout` | WorkResult | skin (dejavu) | `scripts/hud/CraftWindow.cs` (custom — ordres de travail NPC 12-17) | implémenté | — |

### Cartes & téléportation — 7 layouts (3 implémentés / 4 partiels / 0 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `transporttip.layout` | TransportTip | skin (dejavu) | `scripts/hud/TransportWindow.cs` (infobulle nom+coût custom) | implémenté | panel tp map (transportation array).png |
| `transportwnd.layout` | 傳送點 | BigWorld,BigWorldSet,DejaVu,WorldMap,worldmapcity | `scripts/hud/TransportWindow.cs` → TransportWnd.layout | implémenté | TransportationArray (TP changement carte).png |
| `zonemapoptioncn.layout` | MapOptionUI | skin (dejavu) | `scripts/hud/RegionMapWindow.cs` (« Icon Setting »/filtres custom) | partiel | carte.png |
| `zonemapoptionver2.layout` | MapOptionUI | skin (dejavu) | `scripts/hud/RegionMapWindow.cs` (« Icon Setting »/filtres custom) | partiel | carte.png |
| `zonemapui.layout` | ZoneMapUI | BigWorld,BigWorldSet,DejaVu,WorldMap11,worldmapcity | `scripts/hud/WorldMapWindow.cs` → ZoneMapUI.layout (carte monde) | implémenté | carte.png |
| `zonemapuicn.layout` | ZoneMapUI | BigWorld,BigWorldSet,DejaVu,WorldMap,worldmapcity | `scripts/hud/WorldMapWindow.cs` + `scripts/hud/RegionMapWindow.cs` (custom « Region Map ») | partiel | carte.png |
| `zonemapuiver2.layout` | ZoneMapUI | BigWorld,BigWorldSet,DejaVu,WorldMap,worldmapcity | `scripts/hud/WorldMapWindow.cs` + `scripts/hud/RegionMapWindow.cs` (custom « Region Map ») | partiel | carte.png |

### PvP, territoire & instances — 43 layouts (5 implémentés / 8 partiels / 30 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `arenaduel.layout` | ArenaDuel | CDAni,DejaVu,main | — | absent | — |
| `arenaduel2.layout` | ArenaDuel | CDAni,DejaVu,main | — | absent | — |
| `arenaduel3.layout` | ArenaDuel3 | CDAni,DejaVu | — | absent | — |
| `arenainformation.layout` | ArenaInformation | DejaVu,main | — | absent | — |
| `arenainformation2.layout` | ArenaInformation | DejaVu,DejaVuImage | — | absent | — |
| `arenamain.layout` | ArenaMain | DejaVuImage | `scripts/hud/SystemServiceWindows.cs::ColosseumWindow` → ArenaMain.layout | implémenté | — |
| `battlecrusadewnd.layout` | x時空裂縫 | BigWorld,BigWorldSet,DejaVu,WorldMap,worldmapcity2 | — | absent | — |
| `battlefieldlistui.layout` | BaListUIFWindow | skin (dejavu) | `scripts/hud/BattlefieldWindow.cs` → BattlefieldListUI.layout | implémenté | — |
| `battleroyaleinfownd.layout` | BattleRoyaleInfoWnd | skin (dejavu) | — | absent | — |
| `battleroyalerankwnd.layout` | x戰場排名 | skin (dejavu) | — | absent | — |
| `bulletterrainwnd.layout` | BulletWnd | skin (dejavu) | — | absent | — |
| `duelrequestwnd.layout` | DuelRequestWnd | skin (dejavu) | `scripts/hud/DuelDialog.cs` → DuelRequestWnd.layout | implémenté | — |
| `duelwnd.layout` | DuelWnd | skin (dejavu) | — | absent | — |
| `familybattleinfownd.layout` | FamilyBattleInfo | skin (dejavu) | — | absent | — |
| `familybattlejoinwnd.layout` | FamilyBattleJoinWnd | skin (dejavu) | — | absent | — |
| `familybattlejudgewnd.layout` | x賽程表 | CompetitionSchedule,DejaVu,promotion | — | absent | — |
| `familybattlekilledrankwnd.layout` | x戰場排名 | skin (dejavu) | — | absent | — |
| `familybattlekilledrankwnd2.layout` | FamilyBattleKilledRankWnd2 | DejaVu | — | absent | — |
| `familybattlerankwnd.layout` | 公會排名 | DejaVu | — | absent | — |
| `familybattletimewnd.layout` | FamilyBattleTimeWnd | DejaVu | — | absent | — |
| `greedyislandinfownd.layout` | GreedyIslandInfoWnd | skin (dejavu) | — | absent | — |
| `greedyislandjoinwnd.layout` | GreedyIslandJoinWnd | DejaVu | — | absent | — |
| `greedyislandrank2wnd.layout` | 排行榜 | DejaVu | — | absent | — |
| `greedyislandrankwnd.layout` | x公會排名 | DejaVu | — | absent | — |
| `greedyislandtimewnd.layout` | GreedyIslandTimeWnd | DejaVu | — | absent | — |
| `mosterarena.layout` | ArenaDuel | CDAni,DejaVu,main | — | absent | — |
| `rankinfownd.layout` | RankPersonalWnd | DejaVu,DejaVuImage | `scripts/hud/SystemServiceWindows.cs::RankWindow` (521/562/monde 59 → texte ; fenêtres dédiées absentes) | partiel | — |
| `rankmainwnd.layout` | RankMainWnd | skin (dejavu) | `scripts/hud/SystemServiceWindows.cs::RankWindow` → RankMainWnd.layout | implémenté | — |
| `rankpvpinfownd.layout` | RankPVPWnd | DejaVu,DejaVuImage | `scripts/hud/SystemServiceWindows.cs::RankWindow` (521/562/monde 59 → texte ; fenêtres dédiées absentes) | partiel | — |
| `slotterritory.layout` | SlotTerritory | skin (dejavu) | — | absent | — |
| `statusdefendwnd.layout` | StatusSupporter | DejaVu | — | absent | — |
| `statusdefendwnd2.layout` | StatusSupporter | DejaVu | — | absent | — |
| `statusdefendwnd3.layout` | StatusSupporter | DejaVu | — | absent | — |
| `territoryinfownd.layout` | TerritoryInfoWnd | skin (dejavu) | `scripts/hud/SystemServiceWindows.cs::TerritoryWindow` (515/516 → texte ; fenêtres dédiées absentes) | partiel | — |
| `territorylist.layout` | TerritoryList | DejaVu | `scripts/hud/SystemServiceWindows.cs::TerritoryWindow` (515/516 → texte ; fenêtres dédiées absentes) | partiel | — |
| `territorymainwnd.layout` | TerritoryMain | skin (dejavu) | `scripts/hud/SystemServiceWindows.cs::TerritoryWindow` → TerritoryMainWnd.layout | implémenté | — |
| `territoryrankwnd.layout` | TerritoryRankWnd | WorldMap,worldmapcity | `scripts/hud/SystemServiceWindows.cs::TerritoryWindow` (515/516 → texte ; fenêtres dédiées absentes) | partiel | — |
| `territoryschedulewnd.layout` | x領地爭奪戰時程表 | DejaVu | `scripts/hud/SystemServiceWindows.cs::TerritoryWindow` (515/516 → texte ; fenêtres dédiées absentes) | partiel | — |
| `territorytimewnd.layout` | TerritoryTimeWnd | DejaVu | `scripts/hud/SystemServiceWindows.cs::TerritoryWindow` (515/516 → texte ; fenêtres dédiées absentes) | partiel | — |
| `territorytimewnd2.layout` | TerritoryTimeWnd2 | DejaVu | `scripts/hud/SystemServiceWindows.cs::TerritoryWindow` (515/516 → texte ; fenêtres dédiées absentes) | partiel | — |
| `towerwnd.layout` | TowerWnd | DejaVu | — | absent | — |
| `versusinstanceinfownd.layout` | VersusInstanceInfoWnd | skin (dejavu) | — | absent | — |
| `versusinstancerankwnd.layout` | 積分結算 | CDAni,DejaVu | — | absent | — |

### Loteries & autels — 8 layouts (2 implémentés / 6 partiels / 0 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `baselotterywnd.layout` | LotteryWnd | skin (dejavu) | `scripts/hud/LotteryWindow.cs` (custom générique — 511/436-438 + C→S 129) | partiel | — |
| `chanceluckybag.layout` | 寶盒結果 | DejaVu,DejaVuImage | `scripts/hud/DestinyWindow.cs` (compteur fortune bag 557 — listes absentes) | partiel | — |
| `crystallotterywnd.layout` | CrystalLotteryWnd | DejaVu | `scripts/hud/AltarWindow.cs` « Star Diamond / Eden Crystal » (custom) | implémenté | ItemMallPanel-LoyaltyShop.png |
| `diamondlotterywnd.layout` | DiamondLotteryWnd | DejaVu | `scripts/hud/AltarWindow.cs` « Eden Gem » (custom) | implémenté | ItemMallPanel-LoyaltyShop.png |
| `ingamelotterywndver2.layout` | InGameLotteryWnd | DejaVu | `scripts/hud/LotteryWindow.cs` (générique) | partiel | — |
| `lotterywnd.layout` | LotteryWnd | DejaVu | `scripts/hud/LotteryWindow.cs` (custom générique — 511/436-438 + C→S 129) | partiel | — |
| `lotterywndver2.layout` | LotteryWnd | DejaVu | `scripts/hud/LotteryWindow.cs` (custom générique — 511/436-438 + C→S 129) | partiel | — |
| `luckybagdetailwnd.layout` | FortuneBagList | DejaVu | `scripts/hud/DestinyWindow.cs` (compteur fortune bag 557 — listes absentes) | partiel | — |

### Système, légal & outils moteur — 51 layouts (9 implémentés / 8 partiels / 34 absents)

| fichier_layout | titre_fenetre | imageset_principal | panneau_godot | statut | ref-shot |
|---|---|---|---|---|---|
| `againstindulgesimagewnd.layout` | AgainstIndulgesImageWnd | skin (dejavu) | — (régional KR/CN — hors scope EDENFR) | absent | — |
| `againstindulgesnotifywnd.layout` | AgainstIndulgesNotifyWnd | skin (dejavu) | — (régional KR/CN — hors scope EDENFR) | absent | — |
| `againstindulgeswnd.layout` | AgainstIndulgesWnd | DejaVu | — (régional KR/CN — hors scope EDENFR) | absent | — |
| `captchawnd.layout` | Captcha | skin (dejavu) | — (régional KR/CN — hors scope EDENFR) | absent | — |
| `cnlootrulewnd.layout` | CNLootRuleWnd | skin (dejavu) | `scripts/hud/TopLeftVitals.cs` (variantes — NewLootRuleWnd chargé) | partiel | — |
| `cnsecuritycard.layout` | SecurityCard | skin (dejavu) | — (régional KR/CN — hors scope EDENFR) | absent | — |
| `colorcorrection.layout` | 顏色校正參數 | skin (dejavu) | — (outils moteur/éditeur) | absent | — |
| `colorcorrectionitem.layout` | ColorCorrection | skin (dejavu) | — (outils moteur/éditeur) | absent | — |
| `commondlg.layout` | CommonDlg | skin (dejavu) | dialogues custom disséminés (delete char, invite couple…) | partiel | — |
| `commondlg_a.layout` | CommonDlg | DejaVuImage | dialogues custom disséminés (delete char, invite couple…) | partiel | — |
| `cs_actionbarwnd.layout` | CS_ActionBarWnd | cutScene | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_animationwnd.layout` | CS_AnimationWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_cameraviewwnd.layout` | CS_CameraViewWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_dummywnd.layout` | CS_DummyWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_effectwnd.layout` | CS_EffectWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_functionalwnd.layout` | CS_FunctionalWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_letterboxcontrolwnd.layout` | CS_LetterBoxControlWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_letterboxwnd.layout` | CS_LetterBoxWnd | cutScene | `scripts/scene/CinemaMode.cs` (letterbox custom — texture cutScene inutilisée) | partiel | — |
| `cs_model_scrollpanewnd.layout` | CS_Model_ScrollPaneWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_modelwnd.layout` | CS_ModelWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_modelwnd2.layout` | CS_ModelWnd2 | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_normal_basewnd.layout` | CS_Normal_BaseWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_normal_controlwnd.layout` | CS_Normal_ControlWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_normalwnd.layout` | CS_NormalWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_option_musicwnd.layout` | CS_Option_MusicWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_option_showwnd.layout` | CS_Option_ShowWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_optionbarwnd.layout` | CS_OptionBarWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `cs_optionwnd.layout` | CS_OptionWnd | skin (dejavu) | — (éditeur/lecteur de cinématique) | absent | — |
| `debugrenderer.layout` | DebugRenderer | skin (dejavu) | — (outils moteur/éditeur) | absent | — |
| `debugrendereritem.layout` | DebugRenderer | skin (dejavu) | — (outils moteur/éditeur) | absent | — |
| `depthoffieldwnd.layout` | DepthOfField | skin (dejavu) | — (outils moteur/éditeur) | absent | — |
| `krconsiderationwnd.layout` | KRConsiderationWnd | KRConsideration | — (régional KR/CN — hors scope EDENFR) | absent | — |
| `kroptionmenuui.layout` | System | skin (dejavu) | `scripts/hud/SystemMenuWindow.cs` (custom « Main Menu ») | implémenté | SystemPanel.png |
| `krpaywarningwnd.layout` | KRPayWarningWnd | KRPayWarning | — (régional KR/CN — hors scope EDENFR) | absent | — |
| `logoutwnd.layout` | 結束遊戲 | skin (dejavu) | `scripts/hud/SystemMenuWindow.cs` (bouton Logout — fenêtre dédiée absente) | partiel | SystemPanel.png |
| `lootrulewnd.layout` | LootRuleWnd | skin (dejavu) | `scripts/hud/TopLeftVitals.cs` (variantes — NewLootRuleWnd chargé) | partiel | — |
| `messageboardui.layout` | MsgBoard | DejaVu | `scripts/hud/MessageBoardWindow.cs` → MessageBoardUI.layout | implémenté | — |
| `newlootrulewnd.layout` | LootRuleWnd | skin (dejavu) | `scripts/hud/TopLeftVitals.cs` → NewLootRuleWnd.layout (menu portrait) | implémenté | — |
| `optioncontrolui.layout` | OptionControlUI | DejaVu | `scripts/settings/SettingsWindow.cs` + `scenes/settings.tscn` (custom) | implémenté | ParameterPanel-UISettings.png |
| `optioncontroluicn.layout` | OptionControlUI | DejaVu | `scripts/settings/SettingsWindow.cs` + `scenes/settings.tscn` (custom) | implémenté | ParameterPanel-UISettings.png |
| `optioncontroluiver2.layout` | OptionControlUI | DejaVu | `scripts/settings/SettingsWindow.cs` + `scenes/settings.tscn` (custom) | implémenté | ParameterPanel-UISettings.png |
| `optionmenuui.layout` | System | skin (dejavu) | `scripts/hud/SystemMenuWindow.cs` (custom « Main Menu ») | implémenté | SystemPanel.png |
| `optionreadmeui.layout` | 遊戲說明 | skin (dejavu) | — | absent | — |
| `revivecount.layout` | ReviveCount | skin (dejavu) | `scripts/hud/DeathWindow.cs` (aire 531 affichée — compteur dédié absent) | partiel | — |
| `revivemenuui.layout` | 一二三四五六七八對你施展復活技能 | DejaVu | `scripts/hud/DeathWindow.cs` → ReviveMenuUI.layout | implémenté | — |
| `rollui.layout` | RollWnd | skin (dejavu) | `scripts/hud/RollTreasureDialog.cs` → RollUI.layout | implémenté | — |
| `shadereditor.layout` | ShaderEditor | skin (dejavu) | — (outils moteur/éditeur) | absent | — |
| `shadowconfig.layout` | PCF | skin (dejavu) | — (outils moteur/éditeur) | absent | — |
| `toonshaderwnd.layout` | ToonShaderWnd | skin (dejavu) | — (outils moteur/éditeur) | absent | — |
| `treasureinfoui.layout` | TreasureInfoUI | skin (dejavu) | `scripts/combat/TreasurePickup.cs` (ramassage → message chat ; panneau info absent) | partiel | drop au sol d'item de quete important.png |
| `webbrowserwnd.layout` | defaultWindow | skin (dejavu) | — | absent | — |

## Limites

- **Variantes régionales** (`_cn`, `_na`, `_kr`, `ver2`, `cn*`, `kr*`, `us*`) : le
  client charge une seule variante par fenêtre (souvent `_na`/Reforged ou la plus
  récente). Les variantes non chargées d'une fenêtre implémentée sont comptées
  `partiel` même si leur rôle est identique — EDENFR n'en utilise vraisemblablement
  qu'une par écran, et le choix exact n'est pas prouvé pour chacune.
- **Layouts « template »** (racine sans `Root/` ou items : `slot*`, `*item`,
  `teamwndframe`, `jobpointawarditem`…) servent de gabarit instancié en code par
  leur fenêtre mère — comptés `partiel` quand la mère est custom, `implémenté`
  quand le script les construit réellement (`ItemMaillItem`, `TeamWndFrame`).
- **Layouts absents du corpus mais demandés par le code** : `PetWndV3.layout`
  (`ElfWindow`), `StorageUI2.layout` (`NeobankWindow`), `limitless/LimitlessHud.layout`
  (`UltimateBar`) sont des layouts **Reforged** — `hud.Build()` renvoie null et
  le chrome de repli prend le relais ; `UltimateBar` est de plus cachée.
- **`partiel` ne garantit pas la parité visuelle** : il marque une couverture
  fonctionnelle du rôle (données affichées, action câblée), pas la fidélité au
  layout natif. Seuls les panneaux `→ X.layout` sont pixel-proches du CEGUI.
- **Fenêtres hors périmètre** : outils moteur (`shadereditor`, `debugrenderer`,
  `colorcorrection`, `depthoffieldwnd`, `shadowconfig`, `toonshaderwnd`), légal
  régional (`captchawnd`, `againstindulges*`, `cnsecuritycard`, `kr*warning`,
  `krconsiderationwnd`), patcher (`liveupdate`) — `absent` par choix de scope,
  pas par oubli.
- Montage réel : `HudRoot.BuildAll` (panneaux permanents), `HudExtensions.MountAll`
  (fenêtres de service, autoload), `MinimapPanel` (PartyHud/Social/Guild),
  `scenes/game.tscn` (`ZoneWindows` : CraftWindow, ClassChangeDialog), écrans
  `scripts/screens/` pour le pré-jeu.
