# Tables de données — `corpus/fr-full/data/db/`

141 fichiers `.ini` natifs. Conventions :

- **`c_*` / `C_*` = tables client**, `S_*` = serveur, **`t_*` = textes FR**
- Encodage : **Big5** partout sauf `t_*` en **CP1252** — un `cat` brut
  affiche du mojibake ; parser via `IniTable`/`EdenEncoding` (code :
  `scripts/data/`, `src/Eden.Core/Data/`).
- En-tête `|V.x|…|` (format natif, ignoré par les parsers).
- Les `t_*.ini` décrivent la table `c_*`/`C_*` homonyme (id → texte FR).
- Détail item : `docs/reference/ITEMS.md` + `items.tsv` (26 736 lignes)
  déjà joint c_item+t_item. Noms de maps : `maps.tsv`.

| Fichier | Lignes | Rôle |
|---|---|---|
| `4pequip` | 12789 | tenue/équipement → mesh/suffixe par code item (DB<code>=…) |
| `4pequipmap` | 145 | tenue → fichiers texture (B…/G… .dds) |
| `4PHouse` | 18 | maison de guilde/joueur (config) |
| `Assassin` | 101 | données spécifiques classe Assassin |
| `Berserker` | 101 | données spécifiques classe Berserker |
| `boundinfo` | 4906 | boîtes englobantes par code modèle (m010010 → m010.nif) |
| `c_achievement` | 25754 | succès/hauts faits (définitions) |
| `c_activity` | 2679 | activités journalières |
| `c_ai` | 661 | comportements IA des monstres |
| `c_battlefield` | 4 | champs de bataille PvP |
| `c_buliding` | 256 | bâtiments de guilde (orthographe d'origine) |
| `c_charcolor` | 151 | couleurs/teintes personnage |
| `c_class` | 685 | classes jouables : stats, skills, certificats, tenues A0xxxx/level |
| `c_classdropitem` | 290 | objets obtenus par classe |
| `C_Classes` | 8 | classes — noms/codes (table compacte) |
| `c_classrankbuff` | 49 | bonus « Class Ability » par palier de 5 niv. (6e champ = enchant) |
| `c_collection` | 7970 | collections d'objets |
| `c_destinyevent` | 241 | événements de destinée |
| `c_destinyroad` | 12 | Destiny Road (progression) |
| `c_dialogue` | 79446 | arbres de dialogue PNJ |
| `c_dropitem` | 5606 | tables de butin |
| `C_Elf` | 184 | familiers (elf) — base |
| `C_ElfCollect` | 25 | familiers — collecte |
| `C_ElfCombine` | 860 | familiers — combinaison |
| `C_ElfCombineElf` | 458 | familiers — combinaison (variante) |
| `c_enchant` | 24521 | enchants (bonus appliqués — ex. bonus de classe) |
| `c_equipset` | 381 | sets d'équipement (bonus de set) |
| `c_equiptrain` | 100 | entraînement d'équipement |
| `c_familycollect` | 478 | famille — collecte |
| `c_familycombine` | 3178 | famille — combinaison |
| `c_familyevent` | 1 | événements de famille |
| `c_festival` | 647 | festivals/événements saisonniers |
| `c_growequip` | 1664 | équipements évolutifs |
| `c_item` | 81331 | items (référentiel principal, code Q…, icône UI…) |
| `c_itemchest` | 1538 | coffres et contenus |
| `c_itemcombo` | 5413 | combinaisons d'objets |
| `c_itemmall` | 45888 | boutique (item mall) |
| `c_itemmallenchant` | 1729 | enchants de la boutique |
| `c_level` | 101 | courbes de niveau/XP |
| `c_lifeskill` | 595 | compétences de vie (métiers) |
| `C_Lottery` | 281 | loterie |
| `C_LuckyBag` | 2 | sacs chanceux |
| `C_Mask` | 38 | masques (apparences) |
| `c_mission` | 2402 | quêtes/missions (référentiel) |
| `c_monster` | 13174 | monstres (référentiel) |
| `c_monsterstep` | 375 | paliers/évolution monstres |
| `c_node` | 3491 | nodes de carte (codes S### ↔ scènes) |
| `c_npc` | 4082 | PNJ (référentiel) |
| `c_parameter` | 129 | paramètres globaux du jeu (S_Parameter) |
| `c_partner` | 446 | partenaires |
| `c_partnergrow` | 100 | croissance des partenaires |
| `c_party` | 23 | groupes/escouades |
| `c_paysystem` | 950 | système de paiement |
| `c_pvp` | 1 | PvP général |
| `C_Races` | 2 | races jouables |
| `c_rankaward` | 39 | récompenses de rang (class rank — lié au bonus de classe) |
| `c_recommendactivity` | 1212 | activités recommandées |
| `c_rewardscore` | 15 | scores/récompenses |
| `c_ridestep` | 137 | paliers de monture |
| `c_sceneevent` | 259 | événements déclenchés par scène |
| `c_schedule` | 125 | planification d'événements |
| `c_script` | 1568 | scripts de jeu |
| `c_spell` | 19501 | sorts (référentiel principal) |
| `c_spellreference` | 100 | références croisées de sorts |
| `c_statedependence` | 28 | dépendances entre états/buffs |
| `c_store` | 659 | magasins PNJ |
| `c_syssetup` | 2 | réglages système |
| `c_talent` | 736 | talents (arbres de connaissance KP) |
| `c_territory` | 1273 | territoires (guerre de territoire) |
| `c_textindex` | 12392 | index des textes |
| `c_textlimit` | 1000 | limites de texte |
| `c_title` | 997 | titres de joueur |
| `c_transport` | 39 | transports/téléportations |
| `character` | 5 | paramètres de base du personnage |
| `Collection` | 729 | collections (ancienne casse) |
| `color` | 55 | couleurs UI |
| `decorate` | 235 | décorations (guilde/villa) |
| `dyneffect` | 1121 | effets dynamiques |
| `effect` | 1871 | effets de sorts/compétences (FX) |
| `ElfEquip` | 3 | équipement de familier |
| `ElfEvent` | 284 | événements de familier |
| `ELFList` | 3 | liste des familiers |
| `ELFSuit` | 3 | tenues de familier |
| `ElfTrain` | 520 | entraînement de familier |
| `Equip` | 4 | équipements (référentiel) |
| `EquipType` | 22 | types d'équipement |
| `FarmEvent` | 31 | événements de ferme |
| `GuildFunction` | 11 | fonctions de guilde |
| `lic` | 247 | licences (craft/de classe ?) |
| `monsterlist` | 4046 | liens code monstre → kfm + skin (m010010 → m010.kfm, m01001.dds) |
| `motion` | 81144 | motions/animations nommées |
| `NewEquip` | 6 | équipements (nouvelle table) |
| `NormalFemale` | 101 | corps de base — femelles (race humaine?) |
| `NormalMale` | 101 | corps de base — mâles |
| `NpcAvatar` | 3 | avatars de PNJ |
| `npclist` | 684 | liste des PNJ → modèles/skins |
| `PVP` | 8 | PvP (ancienne casse) |
| `ridelist` | 182 | liste des montures |
| `rootcmds` | 315 | commandes console (racine) |
| `rootcmds_st` | 7 | commandes console (variante) |
| `smallFemale` | 101 | corps de base — petites femelles (Zumi?) |
| `smallMale` | 101 | corps de base — petits mâles (Zumi?) |
| `startsetting` | 16 | réglages de démarrage du client |
| `SuitEquip` | 4 | tenues/suits d'équipement |
| `t_achievement` | 26073 | textes FR — succès/hauts faits (définitions) |
| `t_activity` | 2172 | textes FR — activités journalières |
| `t_battlefield` | 4 | textes FR — champs de bataille PvP |
| `t_buliding` | 339 | textes FR — bâtiments de guilde (orthographe d'origine) |
| `t_class` | 698 | textes FR — classes jouables : stats, skills, certificats, tenues A0xxxx/level |
| `t_collection` | 7342 | textes FR — collections d'objets |
| `t_destinyevent` | 243 | textes FR — événements de destinée |
| `t_dialogue` | 73187 | textes FR — arbres de dialogue PNJ |
| `t_enchant` | 22940 | textes FR — enchants (bonus appliqués — ex. bonus de classe) |
| `t_equipset` | 387 | textes FR — sets d'équipement (bonus de set) |
| `t_festival` | 595 | textes FR — festivals/événements saisonniers |
| `t_item` | 70789 | textes FR — items (référentiel principal, code Q…, icône UI…) |
| `t_itemchest` | 3499 | textes FR — coffres et contenus |
| `t_itemcombo` | 5416 | textes FR — combinaisons d'objets |
| `t_itemmall` | 34344 | textes FR — boutique (item mall) |
| `t_itemmallenchant` | 1407 | textes FR — enchants de la boutique |
| `t_mission` | 2482 | textes FR — quêtes/missions (référentiel) |
| `t_monster` | 13438 | textes FR — monstres (référentiel) |
| `t_node` | 2818 | textes FR — nodes de carte (codes S### ↔ scènes) |
| `t_npc` | 4246 | textes FR — PNJ (référentiel) |
| `t_partner` | 448 | textes FR — partenaires |
| `t_party` | 26 | textes FR — groupes/escouades |
| `t_paysystem` | 953 | textes FR — système de paiement |
| `t_rankaward` | 42 | textes FR — récompenses de rang (class rank — lié au bonus de classe) |
| `t_recommendactivity` | 1215 | textes FR — activités recommandées |
| `t_script` | 1758 | textes FR — scripts de jeu |
| `t_sns` | 204 | textes FR (traductions id→chaîne, CP1252) |
| `t_spell` | 18175 | textes FR — sorts (référentiel principal) |
| `t_statedependence` | 30 | textes FR — dépendances entre états/buffs |
| `t_territory` | 881 | textes FR — territoires (guerre de territoire) |
| `t_textindex` | 11492 | textes FR — index des textes |
| `t_textlimit` | 1002 | textes FR — limites de texte |
| `t_title` | 936 | textes FR — titres de joueur |
| `t_transport` | 42 | textes FR — transports/téléportations |
| `textindex` | 1919 | index des textes (ancienne casse) |
| `UIConfig` | 55 | configuration UI |
| `weapon` | 1672 | armes — modèles/skins (codes W#####) |

Voir aussi `data/scene/*.ini` (réglages par map) dans
`scenes-et-cartes.md`.
