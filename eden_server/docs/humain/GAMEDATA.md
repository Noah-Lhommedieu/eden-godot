# Gamedata — les fichiers .ini : d'où ils viennent, pourquoi pas en DB

## Ce que sont les .ini

Ce sont les **tables de design du jeu**, décodées depuis le serveur
officiel : `server_us/Data/db_decoded/` (53 Mo, 165 fichiers). Le
serveur C++ natif lisait exactement ces fichiers — notre C# fait
pareil. `GameDataService` en charge **~50** au boot :

| Catégorie | Fichiers |
|---|---|
| Objets | `S_Item`, `S_ItemMall`, `S_ItemChest`, `S_ItemCombo`, `EquipType` |
| Combat | `S_Spell`, `S_SpellReference`, `S_Monster`, `S_MonsterStep`, `S_DropItem`, `S_ClassDropItem`, `Motion`, `S_Parameter` (coeffs formules) |
| Progression | `S_Class`, `S_Level`, `S_Talent`, `S_ClassRankBuff`, `S_EquipTrain`, `S_Title`, `S_Achievement` |
| Monde | `S_Node` (maps), `S_Npc`, `S_Store`, `S_Transport`, `S_Dialogue`, `S_Battlefield` |
| Famille | `S_Buliding` (faute native), `4PHouse`, `S_FamilyCollect`, `S_FamilyCombine`, `S_Territory` |
| Events | `S_Schedule` (calendrier guerres/colosseum/BR/Greedy), `S_RecommendActivity`, `S_DestinyRoad`, `S_DestinyEvent` |
| Elfes | `S_Elf`, `S_ElfCombine`, `S_Partner`, `S_PartnerGrow` |
| Craft | `S_LifeSkill`, `S_ElfCombine`, `S_EquipSet` |
| Textes | `T_Item`, `T_Spell`, `T_Enchant`, `T_Mission`, `T_Dialogue`, `T_TextIndex`, `S_TextIndex` |
| Divers | `S_RideStep`, `S_Mission`, `S_ItemMallEnchant`, `S_Enchant`, `S_Ai`, `S_GMToolCmds`, `S_RootCmds`, `S_RankAward`, `balance` |

Un second répertoire est requis : **`server_us/Data/scene/`** (12 Mo) —
les fichiers `S<map>.ini` par map (spawns de monstres/PNJ/statues).

## Pourquoi pas en base de données ?

1. **C'est statique** — lu une fois au boot en dictionnaires mémoire.
   Zéro requête par joueur : aucun gain perf à les mettre en DB.
2. **C'est la source de vérité du reverse** — modifier un `.ini` =
   modifier le jeu comme sur le natif. `docs/TUTO-CLIENT.md` documente
   l'ajout de contenu (maps, mobs, items) — **le client lit les mêmes
   `.ini`** : une ligne éditée côté serveur doit correspondre côté client.
3. **Deux schémas à maintenir** si on migre (sync .ini↔tables) pour une
   donnée qui ne change jamais en prod.

La bonne séparation (déjà en place) : **gamedata = fichiers natifs,
état joueur = PostgreSQL**.

## Le problème qu'il y avait (corrigé)

- `docker-compose.yml` ne montait **aucune** donnée : la zone/mission
  docker démarraient avec **0 items, 0 sorts, 0 maps** — en silence.
- `Eden.Mission` avait un **chemin absolu `/home/...` en dur**
  comme fallback (cassé sur toute autre machine).

**Corrections** : volume `../server_us/Data:/data:ro` + env
`DATA_DIR`/`SCENE_DIR` dans compose ; `GameDataService.RequireLoaded()`
fait échouer le boot si les tables cœur sont vides et log les `.ini`
manquants ; fallback unique `AppContext.BaseDirectory/data` partout.

## Arborescence requise (source unique)

```
server_us/Data/
├── db_decoded/    # ~50 ini lus par le serveur (DATA_DIR)
├── scene/         # S<map>.ini par map (SCENE_DIR)
├── db/            # versions ENCODÉES des ini — non utilisé
├── itemmall/      # configs boutique natives — non utilisé (lotteries = DB)
└── NodeMap/       # .smp/.fsmp (pathfinding client) — non utilisé
```

`db_decoded` et `scene` suffisent : les 3 autres dossiers ne sont lus
par aucun code C#.

## Ajouter du contenu

Éditer le `.ini` correspondant (format pipe-séparé, voir
`docs/TUTO-CLIENT.md`) → redémarrer zone/mission. Le chargement est
tolérant aux fichiers absents (`MissingFiles` loggé) mais les tables
cœur (items/monstres/sorts/nodes) sont obligatoires.
