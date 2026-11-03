# NCIDs implémentés — ce que c'est EN JEU

> Chaque NCID = un message client→serveur (CNE) ou la réponse associée.
> Ce doc traduit chaque NCID retranscrit dans le C# en **action que tu
> connais en jeu**. Noms natifs = `CNE_CZ_ZoneServerXxx` (zone),
> `CNE_CW_ServerXxx` (monde), `CNC_*` (réponses client).
>
> ✅ = implémenté dans le serveur C#.

## Connexion / session (Login + World + Zone)

| NCID | Nom natif | En jeu |
|---|---|---|
| 35-41 | `CNC_CL_ServerHello/LoginAccount/…` | Login : handshake, compte, liste des mondes, ticket |
| 0 | `ZoneServerReceiveTicket` | Le client présente son ticket en entrant sur une zone |
| 153 | `ZoneServerHello` | Entrée effective sur la map (après le chargement) |
| 24-26 | `EnterNodeEquipAndSpell/Inventory/PlayerData` | Le client envoie ses données à l'arrivée (équipement, inventaire, perso) |
| 27/32/33 | `SafeQuit*` | La déco « sécurisée » — le compte à rebours de 20 s quand tu quittes |
| 58 | `LogoutReq` | Retour à l'écran perso / déconnexion |
| 57 | `Echo` | Ping/pong test de latence |
| 4 | `MigrationDone` | Fin de téléport/changement de map |
| 88 | `Transport` | Utiliser un téléporteur/cristal de zone |
| 89 | `BagDueDate` | Expiration des sacs loués |
| 68 | `ReportProcessInfos` | Télémétrie client (processus qui tournent — anti-cheat léger) |
| 218 | `NewPlayerGuide` | Flags du guide débutant (les flèches tuto) |

## Déplacement & combat

| NCID | En jeu |
|---|---|
| 1 `MoveSelf` | Chaque pas/déplacement du perso |
| 265 `ClientSeeChar` | **Voir un autre joueur** apparaître (toute sa présentation : niveau, HP, équipement, étal) |
| 269 `ClientLifeEradicate` | Une entité (mob ou joueur) sort de ta vue et disparaît |
| 298 `ClientSeeMob` | Voir un mob/PNJ apparaître |
| 326 `ClientMovePrediction` | Mouvement des autres entités (mobs qui courent, joueurs qui marchent) |
| 123 `TurnSelf` | Pivoter sur place |
| 124 `TargetUpdate` | Changer de cible |
| 125 `GetSelectLifeTarget` | Infos de la cible sélectionnée |
| 5 `MeleeAttack` | Auto-attaque / coup au corps-à-corps |
| 9/16/17/59 `SpellCastTo*` | Lancer un sort (sur cible / zone / self / proche) |
| 13 `SpellBreak` | Annuler un cast en cours |
| 18/127/128 `Enchant*` | Buffs : maj, pause, lancer |
| 85 `EnchantCancel` | Cliquer droit sur un buff pour le retirer |
| 122 `StateCancel` | Annuler un état (ex. assis, transformation) |
| 20/44 `Revive`/`Resurrect` | Rapparition / rez par un joueur |
| 12 `UseSkillPoint` | Dépenser les points de compétence (panel classe) |
| 52 `BuySpell` | Apprendre un sort chez le maître de classe |
| 121 `SetSpellStone` | Poser une **pierre de sort** (glyphe sur un sort) |
| 14 `PlayerAction` | Actions corporelles (s'asseoir, danser…) |
| 21 `PlayerEmotion` | Emotes du chat (/dance, /laugh) |
| 230 `EquipTrainSetItem` | **Mana Infusion** — charger un objet avec de l'exp d'équipement |
| 126 `PVPFlagModify` | Activer/désactiver le flag PvP |
| 231 `PVPRankLookEquip` | Inspecter l'équipement d'un joueur du classement PvP |
| 143 `LookEquip` | Inspecter l'équipement d'un joueur ciblé |

## Inventaire & objets

| NCID | En jeu |
|---|---|
| 6 `DropItem` | Jeter un objet au sol |
| 35 `TreasurePickup` | Ramasser un objet au sol |
| 7 `SwitchItemLocation` | Déplacer un objet dans l'inventaire |
| 8 `UpdateEquipment` | Équiper/déséquiper |
| 84 `DivideItem` | Séparer une stack en deux |
| 260 `ArrangeItem` | Bouton « ranger » l'inventaire |
| 19 `ItemUse` | Utiliser un consommable/objet d'usage |
| 83 `ItemMallUseItem` | Utiliser un objet boutique |
| 64/65 `AskIdentifyGold`/`IdentifyItem` | Identifier un objet non identifié (parchemin) |
| 45-48 `Ask/Repair*` | Réparer l'équipement chez le forgeron (tout/un) |
| 66 `ExchangeItemPin` | Échanger un objet via un **code PIN** |
| 67 `InlayItem` | Sertir une gemme dans un équipement |
| 60-62 `StorageDeposit/Withdraw/GetStorageData` | Entrepôt (banque) — déposer/retirer |
| 69 `StorageExpand` | Acheter des cases d'entrepôt |
| 22/23 `BuyItems`/`SellItems` | Acheter/vendre chez un marchand NPC |
| 63 `LeaveShop` | Fermer la fenêtre du marchand |
| 71 `AskVendor` | Ouvrir la boutique d'un NPC |
| 185 `BuyBackOpen` | Fenêtre rachat (racheter ce qu'on a vendu) |
| 193 `RollTreasure` | Lancer les dés sur le butin (gneed/greed) |
| 221/222 `ChanceLuckyBag*` | **Sac chanceux** — l'event loot enchères flash |
| 223 `QueryFortuneBagCounter` | Compteur de sacs de fortune ouverts |
| 165/167 `QueryClosets`/`ClosetSet` | **Garderobe** — collection de skins/costumes |

## Social — groupe, amis, couple, mariage

| NCID | En jeu |
|---|---|
| 28-31, 79, 90 `Team*` | Groupe : inviter, accepter, quitter, ops, passer chef |
| 86 `TeamRadarAttention` | Ping/attention sur la minimap pour l'équipe |
| 191/192/235-237 `TeamRecruit*` | **Recherche de groupe** (panneau recruit + teleport) |
| 238 `ChangePositionV2` | Échanger les positions dans l'équipe |
| 135/136 `Duel*` | Demande de duel + réponse |
| 12 (world) `ServerAddFriend` | Ajouter un ami |
| 14/22 (world) `Friend*` | Invitation ami + retirer |
| 154-157 `Lover*` | Système **couple** (lover) : inviter, message, infos, rompre |
| 249-251 `Marry*`/`Church*`/`Wedding*` | **Mariage** : opérations, réserver l'église, infos |
| 202 `CoRideInvite` | Inviter quelqu'un sur ta **monture duo** |
| 133/134/169 `PlayerCard*` | **Carte joueur** — le profil social (signe, humeur, likes) |
| 93/94 `Appellation*` | **Titres** — choisir/lister les titres affichés |
| 138-142 `Message*` | Panneau de messages (le « message board » perso) |
| 73 `SloganUpdate` | Modifier le slogan/message perso |
| 103/104 `SearchCondition`/`SearchOpt` | Options « qui peut me trouver » (recherche joueurs) |
| 2 `WhoIs` | Commande `/who` (liste des joueurs en ligne) |
| 3 `TextCommand` | Commandes chat (/…) |
| 34 `ChannelMsg` | Message canal |

## Courrier

| NCID | En jeu |
|---|---|
| 95-102 `Mail*` | Boîte aux lettres : ouvrir, lire, supprimer, envoyer, récupérer pièces jointes/or, retourner |

## Échanges P2P & hôtel des ventes

| NCID | En jeu |
|---|---|
| 36-42, 56, 91, 92 `Trade*` | Échange joueur↔joueur : demande, ajouter/retirer objet, or, confirmer |
| 114-118 `Auction*` | **Hôtel des ventes** : rechercher, acheter, vendre, annuler |

## Quêtes

| NCID | En jeu |
|---|---|
| 50 `QueryMissionStates` | Demander l'état des quêtes |
| 51 `GiveUpMission` | Abandonner une quête |
| 43 `CloseMissionBrief` | Fermer le résumé de quête |
| 87 `MissionListOp` | Opérations sur la liste de quêtes |
| 160 `MissionGoto` | Bouton « aller à » du tracker de quête |
| 186 `SetMissionTrace` | Tracker/détracker une quête à l'écran |
| 10/11 `TalkToNPC`/`ReplyDialog` | Parler à un NPC + choisir une réponse de dialogue |

## Sprite / familier (« Elf » natif = le **sprite compagnon**)

| NCID | En jeu |
|---|---|
| 105/106 `ElfStone*` | Pierres sprite (améliorations du familier) |
| 107/111 `ElfWork*` | **Salon du sprite** — les tâches de craft/récolte du familier |
| 110/113 `EnterNodeElf*` | Données du sprite à l'entrée de zone |
| 112 `ElfCheckData` | Vérifier les données du sprite |
| 137 `ElfTalk` | Le sprite te parle (bulles d'ambiance) |
| 227 `ElfMix` | **Fusion de sprites** (mixer deux familiers) |
| 228 `ElfChangeName` | Renommer le sprite |
| 229 `ElfRevive` | Ressusciter/réveiller le sprite |

## Famille / guilde

| NCID | En jeu |
|---|---|
| 49 `FamilyCreate` | Créer une guilde |
| 81/82 `UpdateFamily`/`SetFamilyEmblem` | Infos guilde + blason |
| 130/131 `FamilyContainer*` | Conteneur du hall de guilde (déposer/prendre) |
| 159 `FamilyDonateMoney` | Donner de l'or à la guilde |
| 161 `FamilyStatue` | Statue de la guilde |
| 173 `FamilyInstanceCreate` | Créer/ouvrir le **hall de guilde** |
| 174/175 `FamilyTownQuery*` | **Recherche de ville de guilde** (panneau des halls) + liste membres |
| 176 `FamilyTownEnter` | Entrer/quitter un hall de guilde |
| 177-179 `FamilyStorage*` | Entrepôt de guilde |
| 180 `FamilyBuildingRepair` | Réparer/upgrader un bâtiment du hall |
| 225 `FamilyBattleAskInfo` | Infos bataille de guildes |
| 239 `FamilyTalkToNpc` | Parler aux NPC placés dans le hall |
| 240 `FixEquipByFamily` | Réparer son équipement via le bâtiment de guilde |
| 9/10/73 (world) `FamilyOp`/`QueryFamilyInfo`/`FamilyEmblem` | Ops guilde côté monde (infos, blason) |

## Maison perso (chambre d'hôtel)

| NCID | En jeu |
|---|---|
| 195 `EnterNodePlayerRoom` | Entrer dans une chambre perso |
| 205-217 `Room*`/`RentRoom`/`PayRent`/`StartParty`… | **Chambre perso** : chercher, louer, entrer, privilèges, meubles/déco, loyer, **fête**, extension, papier peint |

## PvP / arènes / events

| NCID | En jeu |
|---|---|
| 144-147, 162 `Battlefield*` | **Champ de bataille** (10v10 par tranche de level) : rejoindre, quitter, liste |
| 196/197 `Gladiator*` | Équipe **gladiateur** 3v3 (créer, info) |
| 198-201, 219 `Colosseum*` | **Colisée** : demande de join, équipe, quitter, mode |
| 252 `VersusInstanceJoinRequest` | Instance versus (ranked) |
| 241 `BattleRoyaleJoinRequest` | Rejoindre le **battle royale** event |
| 233/234 `GreedyIsland*` | **Île Cupide** (event PvP loot) : join + classement |
| 259 `BattleCrusadeJoin` | Croisade de bataille |
| 158 `VictoryDeclaration` | Déclaration de victoire (panneau guerre) |
| 189/190 `Territory*` | **Guerre de territoire** : infos + rejoindre |
| 194 `RankInfoQuery` | Panneau des **classements** (tous les ranks) |
| 226 `RecommendActivity*` | Activités recommandées (calendrier du jour) |
| 253/254 `PayEvent*` | Événement de recharge (cash event) |
| 255 `EvaluateLotteryOpen` | **Trial du gladiateur** (nœuds 655-674, type 18) : donjon d'équipe noté — consentement (TeamOperation [4][3]+[5], 577), run 3 h chronométré, score kills/morts/temps (`S_RewardScore.ini`), grade C→S+, puis **loterie de récompenses** : ouverture de boîte (valeur 0-4), claim (-1), achat d'ouverture (5), auto-open (7). HUD zone→client = 607 (statut/compteurs/résultat/loterie), best-record = 608, assignation instance = 525, compteur d'entrées = 563 |
| 256 `EnterNodeStarWay` | Entrer la **Voie astrale** (buffs constellations) |
| 224 `UpdateLotteryBonus` | Paliers de bonus loterie |
| 129/184 `GoLottery`/`CheckUpdateLottery` | **Autel Eden Crystal/Gem** (tirages) |
| 258 `DailyAwardOP` | Récompenses de connexion quotidienne |
| 220 `RecruitInstance` | **File de recrutement d'instance** (LFG auto) : le joueur s'inscrit seul pour 1-4 donjons (nœuds type 4-19, `S_Node` col 40 = taille d'équipe, col 41 = niveau min) dans un rôle 0/1/2 — `[s16 count][n×s16 nodeIds][s16 rôle][u8 flag]` (0=inscrire, 1=se retirer). Quand la composition exacte est complète (règles natives tailles 3/5/8 : 1 tank + 1 heal + flex DPS), le serveur **forme l'équipe et téléporte tout le monde** à l'entrée du donjon. Résultats = NCID 553 (`[0][x]` inscription, `[1][0]` retiré, `[3][x]` transporté). Déconnexion et entrée dans un autre type d'instance retirent de la file |
| 163 `NodeGroupReset` | **Reset des instances de l'équipe** (payload vide) : refuse si un nœud instancié du groupe contient encore des membres (texte 1696 au demandeur + nom du donjon, 1699 aux autres) ; sinon détruit les instances (trial 655+ inclus) → 1697 « reset effectué », 1698 aux membres |
| 164 `UpdateBotData` | Données des bots (arène) |

## Personnage — création & compte (World)

| NCID | En jeu |
|---|---|
| 84-92, 96-98 (CW) | Hello, ticket, création de perso (nom, apparence), suppression, vérif nom |
| 20/21 `ChangeWorld` | **Changer de canal** |
| 29 `UpdateWorldServers` | Liste des canaux dans l'UI |
| 26 `CaptchaReturn` | Réponse au **captcha anti-bot** |
| 28 `IllPluginInfo` | Rapport anti-cheat (plugins illégaux détectés) |
| 30/31, 75/76 `CharPassword` | **Mot de passe secondaire** du perso |
| 15/17/18/23-25, 56/58/63/64 `ItemMall*` | **Boutique** : solde, catalogue, achat, réception |
| 8/11 `LookingForMember/Group` | Panneau recherche groupe/membre (LFG) |
| 33 `MutuallyHelpSearch` | **Aide mutuelle** (mentorat) |
| 19 `QueryExploitRank` | Classement des exploits |
| 71/72 `ApexBufferTransfer` | Transfert de buff « apogée » entre persos |
| 242 `PlayerCCDataSave` | Sauvegarde des données de création perso |

## À noter

- **« Elf » = le sprite/familier**, pas un elfe — c'est le pet
  compagnon qui craft dans le salon sprite (`elf_work`, `farm*`).
- **« Fast dress »** (243-248) = les **tenues rapides** : presets
  d'équipement sauvegardés, switch d'un clic (feature tardive).
- **« Room »** (195-217) = la **chambre perso/hôtel** où tu poses des
  meubles et peux lancer une fête.
- **« Lottery »** (129, 184, 224) = les **autels Eden Crystal/Eden
  Gem** en ville — la loterie à objets.
- **« Collection »** (150-152) = la **galerie** où tu exposes des
  objets pour des bonus.
- **« Destiny road »** = la **route du destin**, le plateau de dés
  quotidien avec buffs.
- **« StarWay »** = la **voie astrale**, buffs hebdomadaires par
  constellation.
- **« Exchange pin »** = les **codes PIN d'échange** (codes cadeaux).
- **« Battlefield »** ≠ **Colosseum** ≠ **Territory war** : champ de
  bataille 10v10 / arène gladiateurs 3v3 / guerre de guildes pour les
  territoires de carte.
