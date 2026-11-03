# TABLES-DIVERGENCE — audit S_* serveur vs tables client Reforged

> Généré le 2026-10-25 par comparaison ligne-à-ligne (id → champs
> `|`) de `server_us/Data/db_decoded/S_*.ini` contre
> `reforged/EdenReforged/data/db/{c_,C_}*.ini` (extraction en clair du
> client joué — `[pkgfs] mode=loose`).

## Politique adoptée (lot 12)

Le client joué est **Reforged** : quand une table Reforged diverge de
la S_* native, **la version Reforged est la référence pour toute valeur
que le client affiche ou dont il dépend pour déclencher une action**
(tooltips d'enchant générés depuis les blocs de commandes, gates de
talents, noms). Règle appliquée :

| Table | Décision | Implémentation |
|---|---|---|
| Talents (`S_Talent` ↔ `C_Talent`) | **Reforged canonique**, S_ en variante | `TalentService.LoadFromDataDir` charge `reforged/…/C_Talent.ini` en index [0] ; S_Talent reste une variante d'évaluation (compat client projet) |
| Enchants (`S_Enchant` ↔ `c_enchant`, `S_ItemMallEnchant` ↔ `c_itemmallenchant`) | **Overlay Reforged complet** : remplacement par id + ajout des ids nouveaux | `EnchantService.ApplyClientOverlay` dans `EnchantService.LoadFromDataDir` (zone **et** CombatService) |
| Toutes les autres tables | **S_* conservées** pour l'instant | Voir « limites » ci-dessous |

Pourquoi les enchants en entier et pas seulement les ids de talents :
les blocs `cmd|var1..6` (cols 9+7k) alimentent **à la fois** le
tooltip que le client génère (placeholders `$N$`) et l'effet appliqué
par le serveur — il n'existe pas de « valeur affichée » séparée de la
« valeur serveur ». Toute ligne divergente crée le même bug que
Sacred Duty (affiché ≠ appliqué) ; l'overlay global garantit
`affiché == appliqué` partout.

Désactivation : `EDEN_REFORGED_DB=off` (ou `none`/`-`/`0`) ; forçage
du chemin : `EDEN_REFORGED_DB=/chemin/vers/data/db`.

## Résumé des divergences

- `S_Talent` ↔ `C_Talent` : **496/915** arbres différents (rééquilibrage
  complet des gates/maxPoints — ex. clerc arbre 41 slot 54813 :
  S_=`10 0 0 1 25` vs Reforged=`20 0 0 1 0`).
- `S_Enchant` ↔ `c_enchant` : **2437** lignes différentes dont
  **2056 dans les colonnes de commandes** (9-44) ; +1094 ids Reforged
  seuls, −2 serveur seuls. 278 enchants de talents diffèrent.
- `S_Item` ↔ `c_item` : 1844 lignes, +1065 items Reforged (stuff
  ajouté par le mod) — **non overlay** : les stats d'items servent à
  l'équipement ET aux tooltips mais la table porte aussi des champs
  serveur (prix, drops, usages) ; audit fin requis avant bascule.
- `S_Spell` ↔ `C_Spell` : 420 lignes, +132 sorts — non overlay
  (puissances/cooldowns affichés mais aussi consommés par le combat
  IA monstre ; à auditer avec le feature-matching des sorts).
- `S_Level` ↔ `C_Level` : **90/101 lignes diffèrent** — Reforged a
  une courbe d'XP et/ou des stats par niveau différentes ; le
  serveur applique S_Level (les clients ne la « voient » qu'à travers
  les paquets). À trancher : les stats de base affichées (296)
  viennent de S_Level — si Reforged affiche d'autres stats au même
  niveau, basculer sur C_Level devient cohérent.
- `S_Class` ↔ `C_Class` : 165/305 — % de stats par classe
  potentiellement rééquilibrés.
- `S_Parameter` ↔ `C_Parameter` : la table Reforged n'a que **134
  lignes vs 590** — le client ne charge qu'un sous-ensemble ; **ne
  jamais overlay** (S_Parameter est la table canonique serveur).
- `S_ItemChest` ↔ `c_itemchest` : Reforged a **supprimé ~2900
  coffres** (2727 vs 4671) — schéma de loot intentionnellement
  différent ; S_ conservée.
- `S_LifeSkill` ↔ `C_LifeSkill` : 309 vs 595 — sous-ensemble client.
- `S_SceneEvent` ↔ `C_SceneEvent` : 23 vs 258 — le client n'a que
  les events utiles à l'affichage.
- `S_Monster`, `S_Mission`, `S_DropItem`, `S_Npc` : divergences de
  contenu modérées — les ids Reforged-only (+137 mobs, +157 missions)
  ne sont pas spawnables/validables côté serveur sans leurs S_ ;
  conservées natives.

## Table complète

`S_` = lignes serveur, `C_` = lignes Reforged, `diff` = ids présents
des deux côtés avec contenu différent, `±` = ids exclusifs.

| Table S_ | Table Reforged | S_ | C_ | diff | ids ± |
|---|---|---|---|---|---|
| S_Achievement | c_achievement | 3843 | 3952 | 83 | +110 −1 |
| S_Activity | c_activity | 538 | 538 | 0 | +0 −0 |
| S_Ai | c_ai | 706 | 730 | 1 | +24 −0 |
| S_Battlefield | C_Battlefield | 0 | 0 | 0 | +0 −0 |
| S_BigMale | C_BigMale | 0 | 0 | 0 | +0 −0 |
| S_Buliding | C_Buliding | 136 | 136 | 1 | +0 −0 |
| S_CharColor | C_CharColor | 151 | 241 | 151 | +90 −0 |
| S_Class | C_Class | 305 | 305 | 165 | +0 −0 |
| S_ClassDropItem | C_ClassDropItem | 290 | 338 | 135 | +48 −0 |
| S_ClassRankBuff | C_ClassRankBuff | 60 | 60 | 1 | +0 −0 |
| S_Classes | C_Classes | 0 | 0 | 0 | +0 −0 |
| S_Collection | c_collection | 8516 | 10714 | 2095 | +2295 −97 |
| S_CollectionPoint | c_collectionpoint | 120 | 0 | 0 | +0 −120 |
| S_Crusade | C_Crusade | 4 | 4 | 0 | +0 −0 |
| S_DailyAward | C_DailyAward | 16 | 32 | 2 | +16 −0 |
| S_DestinyEvent | C_DestinyEvent | 221 | 221 | 83 | +0 −0 |
| S_DestinyRoad | C_DestinyRoad | 12 | 12 | 1 | +0 −0 |
| S_Dialogue | c_dialogue | 23226 | 23727 | 35 | +501 −0 |
| S_DropItem | c_dropitem | 5050 | 5131 | 146 | +88 −7 |
| S_Elf | C_Elf | 0 | 0 | 0 | +0 −0 |
| S_ElfCollect | C_ElfCollect | 0 | 0 | 0 | +0 −0 |
| S_ElfCombine | C_ElfCombine | 0 | 0 | 0 | +0 −0 |
| S_ElfCombineElf | C_ElfCombineElf | 0 | 0 | 0 | +0 −0 |
| S_ElfEvent | — | — | — | — | pas de C_ |
| S_Enchant | c_enchant | 19829 | 20921 | 2437 | +1094 −2 |
| S_EquipSet | C_EquipSet | 404 | 432 | 383 | +49 −21 |
| S_EquipTrain | C_EquipTrain | 100 | 100 | 1 | +0 −0 |
| S_EquipType | C_EquipType | 0 | 0 | 0 | +0 −0 |
| S_FamilyCollect | C_FamilyCollect | 500 | 500 | 22 | +0 −0 |
| S_FamilyCombine | c_familycombine | 3273 | 3394 | 336 | +124 −3 |
| S_Festival | c_festival | 488 | 479 | 10 | +17 −26 |
| S_GMToolCmds | C_GMToolCmds | 0 | 0 | 0 | +0 −0 (octet-identique) |
| S_GrowEquip | C_GrowEquip | 53 | 68 | 1 | +15 −0 |
| S_Item | c_item | 20427 | 21485 | 1844 | +1065 −7 |
| S_ItemChest | c_itemchest | 4671 | 2727 | 63 | +953 −2897 |
| S_ItemCombo | C_ItemCombo | 5858 | 5858 | 1 | +0 −0 |
| S_ItemMall | c_itemmall | 8407 | 10400 | 912 | +2010 −17 |
| S_ItemMallEnchant | c_itemmallenchant | 1431 | 1784 | 470 | +353 −0 |
| S_Level | C_Level | 101 | 101 | 90 | +0 −0 |
| S_LifeSkill | C_LifeSkill | 595 | 309 | 257 | +0 −286 |
| S_Lottery | C_Lottery | 0 | 0 | 0 | +0 −0 |
| S_LuckyBag | C_LuckyBag | 0 | 0 | 0 | +0 −0 |
| S_Mask | C_Mask | 0 | 0 | 0 | +0 −0 |
| S_Mission | c_mission | 2831 | 2988 | 351 | +157 −0 |
| S_Monster | c_monster | 12896 | 13033 | 359 | +137 −0 |
| S_MonsterStep | C_MonsterStep | 0 | 0 | 0 | +0 −0 |
| S_Node | c_node | 379 | 407 | 44 | +28 −0 |
| S_NormalFemale | C_NormalFemale | 0 | 0 | 0 | +0 −0 |
| S_NormalMale | C_NormalMale | 0 | 0 | 0 | +0 −0 |
| S_Npc | c_npc | 3083 | 3134 | 16 | +51 −0 |
| S_PVP | C_PVP | 0 | 0 | 0 | +0 −0 |
| S_Parameter | C_Parameter | 590 | 134 | 8 | +0 −456 |
| S_Partner | C_Partner | 572 | 588 | 2 | +16 −0 |
| S_PartnerGrow | C_PartnerGrow | 100 | 100 | 1 | +0 −0 |
| S_Party | C_Party | 23 | 23 | 1 | +0 −0 |
| S_PaySystem | c_paysystem | 385 | 733 | 267 | +348 −0 |
| S_Races | C_Races | 0 | 0 | 0 | +0 −0 |
| S_RankAward | C_RankAward | 39 | 39 | 5 | +0 −0 |
| S_RecommendActivity | C_RecommendActivity | 385 | 387 | 84 | +12 −10 |
| S_RewardScore | C_RewardScore | 21 | 21 | 21 | +0 −0 |
| S_RideStep | c_ridestep | 0 | 0 | 0 | +0 −0 |
| S_RootCmds | C_RootCmds | 0 | 0 | 0 | +0 −0 |
| S_SNS | C_SNS | 201 | 201 | 1 | +0 −0 |
| S_SceneEvent | C_SceneEvent | 258 | 23 | 17 | +0 −235 |
| S_Schedule | C_Schedule | 80 | 75 | 45 | +2 −7 |
| S_Script | C_Script | 244 | 245 | 2 | +1 −0 |
| S_SmallFemale | C_SmallFemale | 0 | 0 | 0 | +0 −0 |
| S_SmallMale | C_SmallMale | 0 | 0 | 0 | +0 −0 |
| S_Spell | C_Spell | 4485 | 4617 | 420 | +132 −0 |
| S_SpellReference | C_SpellReference | 105 | 105 | 1 | +0 −0 |
| S_StateDependence | C_StateDependence | 0 | 0 | 0 | +0 −0 |
| S_Store | C_Store | 670 | 697 | 54 | +27 −0 |
| S_SysSetup | C_SysSetup | 0 | 0 | 0 | +0 −0 |
| S_Talent | C_Talent | 915 | 915 | 496 | +0 −0 |
| S_Territory | C_Territory | 28 | 32 | 1 | +4 −0 |
| S_TextIndex | c_textindex | 9109 | 9136 | 4 | +27 −0 |
| S_TextLimit | C_TextLimit | 442 | 442 | 1 | +0 −0 |
| S_Title | c_title | 337 | 359 | 1 | +22 −0 |
| S_Transport | C_Transport | 40 | 40 | 1 | +0 −0 |

## Limites / à vérifier

- **Client projet** (`client/data/db/C_Talent.ini` == S_Talent) : la
  validation SetAAPoint reste multi-disposition (les deux tables sont
  évaluées, accept si l'une passe) — jouable. En revanche les
  tooltips d'enchants rééquilibrés affichés par le client projet ne
  reflètent plus les valeurs appliquées (Reforged gagne partout :
  le client projet est de référence protocole, pas la cible de jeu).
- `gameData.Enchants` reste la table **native** (référence pour les
  diagnostics) ; seuls les accesseurs `EnchantService` (zone +
  CombatService) portent l'overlay.
- Prochaines bascules candidates si le même symptôme remonte :
  `C_Level` (stats affichées au 296) et `C_Class` (% de classe) —
  les deux alimentent directement ce que le client affiche dans la
  fiche personnage.
