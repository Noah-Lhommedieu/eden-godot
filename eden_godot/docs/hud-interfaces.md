# HUD & Interfaces — Eden Client Reborn (Godot)

Inventaire de tous les éléments d'interface du client, leur rôle dans
le jeu d'origine (ref `ref-shots/`), le layout CEGUI natif utilisé, la
clé de test `--hud-open=<clé>` quand elle existe, et l'état de
finition vérifié en runtime.

Légende état :
- ✅ natif + prouvé visuellement (capture)
- 🔶 monté/natif, pas encore prouvé sur cette session
- 🔨 fallback codé main (pas de layout natif chargé)
- ⚫ pont invisible (pas de rendu — logique/réseau seulement)

## 1. HUD permanent (toujours visible en jeu)

| Nom interne | Clé/zone écran | Layout natif | Rôle dans le jeu | État |
|---|---|---|---|---|
| `TopLeftVitals` (« Vitals ») | haut-gauche | `MiniInfoWnd.layout` | Cadre joueur : portrait, nom, HP/MP numériques. Le « cadre perso » avec la tete du joueur sur le coin. | ✅ |
- barre de HP en rouge clair
- Barre de mana en bleu clair
- Tête du joueur DANS le cercle.
- Clique droit sur le cercle (de la tête de joueur) pour Reset dungeon (reset les entrées de donjon ainsi que les scenes de quetes (animation quand les npc parlent et se déplace etc uniquement dans les maps dédié aux quetes))
- Si en groupe, moyen de voir le paramétrage du groupe (random -> drop et exp partagé, taunt -> le premier qui touche le mob reçoit tout, items all -> drop roll dice automatique pour tous les drops, items green -> drop roll dice automatique pour les greens et white items mais pas les bleu et au dessus en terme de rareté, items blue -> drop roll dice automatique pour les greens et white et blue mais pas au dessus en terme de rareté)

| `EXPBar` (« Ultimate ») | Bas-gauche | — | Double jauge avec pourcentage XX.XXX (3 décimal de précision), une pour EXP Character (vert pomme), et une pour EXP de la classe actuel du joueur (violet). 
- Clique droit pour définir le niveau de précision (XX ou XX.X ou XX.XX ou XX.XXX) dans un mini panel dédié au choix.

| `UltimateBar` (« Ultimate ») | Juste au dessus de la ExpBar | — | Jauge « ULTIMATE Lv- / 0% » — Système d'exp ultime (notion a implémenter plus tard, pas obligatoire pour l'instant). | ✅ |
- Ne pas coder (ou cacher pour l'instant) car pas utile avant la fin du développement du projet initial de portage (fonctionnalité custom donc facultatif).

| `MinimapPanel` (« Minimap ») | haut-droite | `RadarUI_Ver2.layout` | Minimap + nom de zone + boutons (mail, itemmall et loyalty shop, eden gem altar, eden crystal altar, star diamond altar, Battlefields, Arena, Rankings, Events). | ✅ |
TOUS les boutons doivent être utile sur le panel
Allongé légèrement horizontalement le panel car il parait serré


| `QuestTracker` (« Quests ») | droite, sous minimap | — | Liste des quêtes suivies (« Track Quests ») avec objectifs 0/N. | ✅ |
- Les objectifs sont clique pour auto-follow vers l'objectif (npc, mobs, coordonnée...)
- Chaque avancement dans les objectifs montre au milieu de l'écran l'avancement via le ZoneBanner


| `ChatPanel` (« Chat ») | bas-gauche | — | Fenêtre de chat : onglets Normal/Battle/…, saisie, canal, journal combat (Battle), choix pour chat channel (normal, combat et ceux créé) du choix d'infos voulu reçu. | ✅ |


| `SkillBarWindow` (« SkillBar ») | bas | `FucntionBarWnd.layout` | Barre de raccourcis principale : slots 1-0, icônes de skills/consommables/item a équiper., Possibilité d'avoir jusqu'a 3 ligne | ✅ |
- Clique gauche pour intéragir et déplacer les sorts, items dans la barre, hotkeys ou clique droit pour les activer


| `MenuIconsPanel` (« MenuIcons ») | bas-droite | `FunctionMenu.layout` | Rangée d'icônes menu (personnage, sac, classes, quêtes, social, carte, système/paramètre…). | ✅ |

| `BuffBar` (« BuffBar ») | Juste a gauche de la minicarte (haut et milieu-droit)| `BuffWnd.layout` | Icônes de buffs et état actifs sur le joueur — timer m:ss, stack ×N, tooltip, clic-droit annule. | ✅ (live prouvé : `--buff-test`, 299 → slot rempli) |
| `NerfBar` (« NerfBar ») | Juste a gauche de la BuffBar (haut et milieu-droit)| `BuffWnd.layout` | Icônes de nerf actifs sur le joueur (nerf = malus pour le joueur)— timer m:ss, stack ×N, tooltip. | |
| `ZoneBanner` (« Banner ») | centre-haut | — | Messages système centraux : « Entering Combat », « Leaving Combat », annonces de zone. | ✅ (Leaving vu live) |
| `TargetFrame` (« TargetFrame ») | haut-centre sous Ultimate | `TargetInfoWnd.layout` | Cadre de la cible : nom, Lv, HP/MP, buffs état et nerf cible (4/10), « Target's Target » (`TargetBg`), bouton inspecter pour voir stats du mobs et drops et skills (normal et countdown skill). | 🔶 (monté, frame non capturée live) |
| `CastBar` (« CastBar ») | bas-centre au-dessus hotbar | — | Barre d'incantation : progression du cast. | Ne pas mettre le nom du sorts, simplement la progression du cast suffit avec les secondes restantes|
| `GoldHud` (« GoldHud ») | bas-droite | — | Affichage or courant (« 44 960 g »). | à retirer, n'existe pas de base et c'est de la merde, les golds c'est QUE dans l'inventaire |
| `MountHud` (« MountHud ») | droite | — | Bouton « Mount » — invoque/renvoie la monture. | à retirer, ne dois pas exister, les montures c'est soit dans la skillbar soit dans l'inventaire. |
| `PartyHud` (« PartyHud ») | gauche | — | Cadres de groupe : membres, HP/MP, buffs de party. | 🔶 |
Clique droit sur un membre pour le kick (si chef de groupe), ou le message privé.


| `PetFunctionBar` (« PetBar ») | bas, au-dessus hotbar | `PetFunctionWnd5p.layout` | Barre d'actions du familier invoqué (attaquer, suivre, ramasser…). | 🔶 |
| `DamageFloaters` (« DamageFloaters ») | monde 3D | — | Chiffres de dégâts/soins (critique ou non) flottants| Il ne faut pas le nom du skill. |
| `TreasurePickup` (« TreasurePickup ») | bas-centre | — | Popup loot : objet ramassé + nombre. | 🔶 |

## 2. Fenêtres principales (menu/raccourci)

| Nom interne | Clé `--hud-open` | Layout natif | Rôle | État |
|---|---|---|---|---|
| `InventoryWindow` (« Inventory ») | `inventory` (toggle B) | `inventorywnd.layout` | Sac à dos : grille 19×12, onglets All/Potions/Gear/Town/Special, or+points, sélecteurs sac Slot0-5, boutons Stack/Break/Fortify/Fuse/Stall/Case, drag&drop, tooltip, Archive, craft racial, backpack manager, special currency... | ✅ live |
| `ProfileWindow` (« Profile ») | `profile` (toggle C) | `CharacterWnd.layout` | Fiche personnage : stats, attributs, titres, résistances, équipements. | ✅ |
| `EquipSlotsPanel` | (via Profile) | — | Panneau d'équipement : slots d'objets portés (arme, armure…). | ✅ |
| `SkillsWindow` (« Skills ») | `skills` (toggle K) | `NewSkillList.layout` | Liste des skills de classe connus + drag vers hotbar. | C'est pas SkillWindow mais ClassWindow, c'est le panel des classes des joueurs. |
| `QuestWindow` (« QuestLog ») | `quest` (toggle L) | `MissionLog.layout` | Journal de quêtes complet : liste, détails, abandon, partage. | ✅ |
| `WorldMapWindow` (« WorldMap ») | `map` (toggle M) | `ZoneMapUI.layout` | Carte du monde/région : continents, marqueurs (marchands, ventes, portails), filtre « City Features », zoom, coords. | ✅ |
| `SocialWindow` (« SocialWnd ») | `social` | `CommunityWnd.layout` | Social : onglets Friend/Search/Team/Lover, liste d'amis, blocage, whisper, invitation couple. | ✅ (capture) |
| `ClassChangeDialog` (« ClassChangePanel ») | `class` (toggle P) | `JobWnd_NA.layout` | Fenêtre de classe : liste des classes, détail, points de classe, « Switch Class » (NCID 168→489). | ✅ (capture ; swap live à prouver) |
| `ElfWindow` (« Elf ») | `elf` (toggle E) | `PetWndV3.layout` | Familier/Elf : stats, skills, nourriture, invocation. | ✅ |
| `ElfBook` | — | — | Grimoire du familier (compétences elf). | ⚫ helper |
| `GuildWindow` (« GuildWnd ») | `guild` (toggle G) | — | Guilde : membres, annonce, niveau, invites. | ✅ (capture) |
| `CraftWindow` (« CraftPanel ») | `craft` | — | Artisanat : recettes, matériaux, fabrication. | 🔨 |
| `TransportWindow` (« Transport ») | `transport` | `TransportWnd.layout` | Portail/transport : choix de destination (cristaux de téléport (Transportation Array)). | ✅ |
| `SettingsWindow` | menu système | — | Options : vidéo, audio, gameplay, raccourcis, thème UI. | ✅ |
| `InspectWindow` (« InspectWnd ») | `inspect` | — | Inspection d'un joueur ciblé : équipement porté (C→S 143 → 460). | 🔶 |
| `ClassChangeDialog` alternatif | — | `JobListWnd_NA.layout` | (variante liste — non utilisée) | ⚫ |

## 3. Commerce, stockage, services

| Nom interne | Clé `--hud-open` | Layout natif | Rôle | État |
|---|---|---|---|---|
| `ShopWindow` (« ShopWnd ») | `shop` | `ShopWnd.layout` | Marchand NPC : onglets achat/revente, grille d'objets, prix. | ✅ |
| `BankWindow` (« BankWnd ») | `bank` | `StorageUI.layout` | Banque perso : grille stockage + or, dépôt/retrait. | ✅ |
| `NeobankWindow` (« NeobankWnd ») | `neobank` | `StorageUI2.layout` | « Neo Bank » Reforged : banque étendue multi-onglets (vue rf_neobank). | ✅ |
| `FamilyStorageWindow` (« FamilyStorageWnd ») | `familystorage` | `FamilyStorageWnd.layout` | Coffre de famille/compte partagé entre persos (service NPC 19). | ✅ |
| `TradeWindow` (« TradeWnd ») | `trade` | `TradeWnd.layout` | Échange joueur↔joueur : 2 colonnes d'objets, or, confirm/lock. | ✅ |
| `VendingWindow` (« VendingWnd ») | `vending`/`stall` | `StallUI.layout`+`StallBuy.layout` | Étale personnel (stall) : mise en vente d'objets à prix fixe + vue acheteur. | ✅ |
| `AuctionWindow` (« AuctionWnd ») | `auction` | `AuctionWnd.layout` | Hôtel des ventes : recherche, enchères/achat immédiat, « My Sales ». | ✅ (binds 114-118/425-428 ; live à prouver) |
| `MailWindow` (« MailWnd ») | `mail` | `MailWnd.layout` | Courrier : boîte de réception, pièces jointes, « Take All », envoi. | ✅ |
| `ItemMallWindow` (« ItemMallWnd ») | `itemmall`/`mall` | `ItemMall.layout`+`ItemMaillItem.layout` | Boutique cash-shop : catégories, panier, points cristaux. | ✅ |
| `MessageBoardWindow` (« MessageBoardWnd ») | `messageboard` | — | Panneau d'affichage : notes/annonces, poster un message (455-459). | 🔶 |
| `FeatureDialogWindow` (« FeatureDialogWnd ») | `featuredlg` | — | Dialogs de service NPC génériques : fortification, fusion, décomposition, réparation… | 🔶 |
| `LotteryWindow` (« LotteryWnd ») | `lottery` | — | Loterie/tombola (tirages, lots). | 🔶 |
| `MagicBoxWindow` (« MagicBoxWnd ») | `magicbox` | — | Boîte magique : transformation/alchimie d'objets. | 🔶 |
| `StorageItemCell` | — | — | Cellule d'objet réutilisable (banque/étal/coffre). | ⚫ composant |
| `ItemTooltip` | — | — | Tooltip d'objet complet (stats, durabilité, prix, dye) utilisé partout. | ⚫ composant |
| `DragHelper` | — | — | Drag&drop + sauvegarde des positions de fenêtres (`ui_layout.cfg`) — clamp plein écran au restore. | ⚫ utilitaire |

## 4. Événements, PvP, services instanciés

| Nom interne | Clé `--hud-open` | Layout natif | Rôle | État |
|---|---|---|---|---|
| `DuelDialog` (« DuelDialog ») | `duel` | — | Invitation/résultat de duel 1v1 (451-454). | ✅ chaîne complète prouvée live (GUI+bot : 135→451→136→453→454) |
| `BattlefieldWindow` (« BattlefieldWnd ») | `battlefield` | — | Champ de bataille : inscription, score, file. | 🔶 |
| `HotelWindow` (« HotelWnd ») | `hotel` | — | Hôtel/auberge : repos, services de l'aubergiste. | 🔶 |
| `TerritoryWindow` (« TerritoryWnd ») | `territory` | — | Guerre de territoire : carte des zones de guilde, état. | 🔶 |
| `ColosseumWindow` (« ColosseumWnd ») | `colosseum` | — | Colisée/arène : inscriptions et matchs PvP. | 🔶 |
| `RecruitWindow` (« RecruitWnd ») | `recruit` | — | Recrutement de groupe/party search. | 🔶 |
| `FastDressWindow` (« FastDressWnd ») | `fastdress` | — | « Fast Dress » : tenues/équipements prédéfinis rapides. | 🔶 |
| `RankWindow` (« RankWnd ») | `rank` | — | Classements : PvP, niveau, guilde. | 🔶 |
| `RollTreasureDialog` (« RollTreasureDlg ») | `roll` | — | Tirage loot de groupe (Need/Greed/pass). | 🔶 |
| `DeathWindow` (« DeathWnd ») | `death` | — | Fenêtre de mort : respawn en ville/sur place, pénalité. | 🔶 |
| `AchievementWindow` (« AchievementWindow ») | `achievements` | — | Succès : catégories, points, titres déblocables. | ✅ |
| `DailyRewardWindow` (« DailyRewardWindow ») | `daily`/`loginreward` | — | Calendrier de connexion : récompense quotidienne. | ✅ claim prouvé live (258 op=1/2 → 615 + 274 item) |
| `DestinyWindow` (« DestinyWindow ») | `destiny`/`fateroad` | `FateRoadWnd.layout` | « Fate Road »/chemin d'éveil : route de nodes de progression + fortune. | ✅ |
| `CoupleWindow` (« CoupleWindow ») | `couple`/`lover`/`wedding` | — | Couple/mariage : fiche partenaire, invite, cérémonie. | ✅ |
| `SystemServiceWindows` | — | — | Regroupe petits dialogs système (confirmations, choix NPC). | ⚫ helper |

## 5. Dialogues et flux NPC

| Nom interne | Déclencheur | Layout natif | Rôle | État |
|---|---|---|---|---|
| `NpcDialogueWindow` (« NpcDialogue ») | clic NPC (285) | `NpcTalkWnd.layout` | Dialogue NPC : texte, réponses, quêtes accept/rendre, boutique/service lié. | ✅ |
| `ItemTooltip` | survol objet | — | (cf. §3) | ⚫ composant |

## 6. Écrans hors-HUD (chaîne de connexion)

| Écran | Rôle | État |
|---|---|---|
| `BootScreen` | Splash/boot : charge uidata + préchauffe temple/proto GLB. | ✅ |
| `LoginScreen` | Compte/mot de passe + serveur (fond temple 3D). | ✅ (look à refaire vs ref) |
| `ChannelSelectScreen` | Liste mondes + channels (« Ch01 (Recommended) »). | ✅ (look à refaire vs ref) |
| `CharSelectScreen` | Grille 5×2 persos, fiche info, avatar 3D sur le temple. | ✅ |
| `CharCreateScreen` | Création : race, classe, apparence, nom, avatar live. | ✅ |
| `LoadingScreen` | Écran de chargement map (fondu, barre). | ✅ |
| `TempleBackdrop` | Fond 3D partagé (temple S218_room10b) + marqueur avatar + caméras shot=Wide/Select. | ✅ |

## 7. Ponts réseau / nœuds logiques (pas de rendu direct)

| Composant | Rôle |
|---|---|
| `HudNetBridge` | Binds zone : 444 cible → TargetFrame, 285 dialogue, 280 attributs, 272 melee → bannière combat + « Target's Target », cercle de sélection rouge. |
| `BuffBridge` | 299/300/301/315/372/445/446 enchants → BuffBar + buffs du cadre cible + annulations (C→S 18/85/127/128). |
| `ElfBridge` | Familier 412/413/414/418/424/513/567/568/574/575/588 → ElfWindow/PetBar. |
| `MissionBridge` | Quêtes 388/87/51, marqueurs NPC 345, transport 393/88, portails 15, monture 283/19. |
| `DestinyBridge` | Fate Road 534/535/589/590/557 + actions 203/204/223. |
| `LoverBridge` | Couple 475-479/597/599 + actions 154-157/249/251. |
| `MissionBridge`/`SkillCast` | Casts : C→S 9/16/17/13 → castbar + cercle magique + résultats 279/281. |
| `SystemServiceWindows` | Petits dialogs de service génériques. |
| `StorageItemCell`, `ItemTooltip`, `DragHelper`, `EquipSlotsPanel` | Composants partagés (cellule, tooltip, drag, slots). |

## 8. Entités monde (rattachées au HUD)

| Élément | Rôle |
|---|---|
| Nameplate `Label3D` | Nom au-dessus des entités (blanc = mob, vert-jaune = NPC, bleu = joueur, doré = trésor). |
| HP bar mob | Barre colorée sous la nameplate (vert→orange→rouge selon fraction) — `RemoteEntity.UpdateHpBar`. |
| Cercle de sélection | Anneau rouge au sol sous la cible courante (`RemoteEntity.SetTargeted`, NCID 444). |
| `EntityAnimator` | Anims retargetées GLB (stand01/walk01/attack01/death01…) par entité. |
| `InterpBuffer`/`NetworkInterpolator` | Interpolation positions des entités distantes. |

## 9. Raccourcis et clés de test

- `--hud-open=<clé>` : voir colonnes ci-dessus (ex.
  `--hud-open=inventory,profile`). Fonctionne en `--solo`.
  Clés fenêtres de service : `duel, battlefield, hotel,
  messageboard/msgboard, roll, death, featuredlg/feature,
  territory, colosseum/arena, recruit, fastdress/dress, rank,
  cast` — ouverture via les wrappers montés (`EnsureDemo` fournit
  un contenu de démo quand aucun paquet n'est arrivé).
- `--hud-dump` : imprime les rects globaux des panneaux.
- `--shot=<chemin>` : capture viewport puis quitte (attend HUD prêt,
  et la fin de `--combat-test`).
- `--mob-test --combat-test` : migre vers une map à mobs, engage le
  hostile le plus proche (NCID 5 auto-attaque), lance le 1er sort.
- Toggles en jeu : B inventaire, C profil, K skills, L quêtes, M map,
  G guilde, E elf, P classe — via `MenuIconsPanel`.
- `--repair-test` : envoie NCID 45 après l'entrée zone et logue la
  réponse 342 (coût réparation) — preuve wire forgeron sans NPC.
- `--perf --perf-seconds=N` : métriques + `tex-postes` (top postes
  de texture décodée par dossier/fichier — voir `docs/PERF.md`).

---
Sources : `HudRoot.BuildAll` (permanent + fenêtres cœur),
`HudExtensions.MountAll` (vagues S1/S4/S7/1C/1D/V2/complet),
`ref-shots/` (visuel cible). Mise à jour : 27/10/2026.

### État vague « layouts natifs » (nov. 2026)

Fenêtres de service/PvP migrées sur les layouts CEGUI natifs —
`HudExtensions.BuildNativeWindow` (chrome + titlebar + close
natifs, repli panneau générique) : DuelRequestWnd,
BattlefieldListUI, HotelWnd, MessageBoardUI (MsgBorardFWin),
RollUI, ReviveMenuUI (revive types 0/1/5), TerritoryMainWnd,
ArenaMain, Recruit, DressRoomUI, RankMainWnd + ShopWnd
(fixAllBtn/fixoneBtn → senders 45-48). Les libellés natifs encodés
noir (`TextColours FF000000`) passent en clair sur chrome sombre
(`CeguiToGodot.ApplyText` — labels seulement). Round-trips live
prouvés : repair 45→342 (`--repair-test`).
