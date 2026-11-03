# Schémas des tables .ini — Limitless Studio

> Source : `limitless/studio/Limitless.Studio/Services/IniSchemas.cs`,
> construite depuis les commentaires de colonnes des parsers C#
> (`server/src/Eden.Core/Data/Parsers/*Parser.cs`, eux-mêmes prouvés
> par désasm/captures). Utilisé par l'éditeur de formulaires du
> Studio (page INI files + fiches Items/Monsters). Index 0-basé.
> Un champ vide en jeu = 0 côté natif.

## Lecture d'une table `S_*`

- Séparateurs : `|` (et `,` toléré par les parsers).
- En-tête : `|V.<version>|<nbColonnes>|`.
- Les **T_\*.ini** client (`reforged/EdenReforged/data/db`, Latin-1)
  portent les libellés EN : records multi-lignes terminés par une
  ligne finissant en `|` ; l'en-tête donne le nombre de champs texte
  puis les index de colonnes.

## S_Item.ini (78 col) — ItemParser.cs

| Col | Champ | Utilité |
|---|---|---|
| 0 | Item ID | référencé par drops/shops/quêtes |
| 1 | Item code | clé asset → `itemicon/<code>.dds` |
| 3 | Model code | famille visuelle (G00005…) |
| 4 | UI icon code | icône UI alt (UI012) |
| 7 | Name (Big5) | label EN → `t_item.ini` |
| 8 | Item kind | 0x11-0x1e armes, 22/25 livres, 43 sacs, 45 gemmes |
| 9 | Equip slot | 2 armure, 4 gants, 5 bottes, 7 arme |
| 10 | Train mask | stats Mana Infusion (bit k = attr k) |
| 11 | Flags i64 | bit7 bind-on-equip ; hi 8 arme/0x20 armure/0x10 accessoire |
| 12 | Use type | 1/2 consommable, 3/5 item-sur-item, 6 elf |
| 14/15 | Required/Max level | |
| 17 | Mall price | monnaie alternative (PAS l'or — voir 31) |
| 19 | Quality | couleur de base |
| 21/22 | Use cooldown ds / group | dixièmes de s |
| 23 | Use enchant | id S_Enchant au clic droit |
| 24/25 | Buff duration unit/amount | 2=min 3=h 4=jours |
| 26/27 | Rent duration unit/amount | 1/4 min, 2/5 h, 3/6 jours |
| 28 | Bag capacity | slots (kind 43) |
| 29 | Max stack | potions → 100 |
| 31 | System price | LE prix or (NPC buy ×remise, resale ×5%) |
| 34+ | Description | multi-lignes physique suivantes ($7$) |
| 35 | Learn spell / gem level | kind 22/25 livres, 45 gemmes |
| 37-43 | HP MP STR AGI INT WIS LCK | bonus équipement |
| 44 | Attack element | 0 = neutre |
| 45-47 | P-ATK M-ATK variance% | |
| 48/49 | Attack range/speed | raw ×0.5 / ×1.5 |
| 50-57 | DEF HIT DODGE CRIT CRITDMG MCRIT MCRITDMG pet | |
| 63-71 | Resistances ×9 | 69/70 aussi lus comme restrict flags A/B |
| 72/73/74 | MaxSocket / SocketRate% / MaxDurability | |
| 75 | Cube group ids | liste `;` |
| 76 | Repair price | |

## S_Monster.ini (62 col) — MonsterParser.cs

| Col | Champ | Utilité |
|---|---|---|
| 0 | Monster ID | spawn lists, quêtes, drops |
| 1 | Model code | M312040 → modèle + `uiicon` (strip chiffres) |
| 2 | Name (Big5) | EN → `t_monster.ini` |
| 3 | Kill category | buckets DSL KillMonster {2}mob {3,4}elite {5,6}boss |
| 4/5 | Level / Monster type | type : 1 normal 2 élite 3 boss |
| 7 | Active flag | 1 = spawnable |
| 8/9 | HP / MP | |
| 13 | Attack element | indexe les résistances |
| 14-17 | Power / Variance% / Range / Speed | formule `100−var+rand(2var)` |
| 18-24 | DEF HIT DODGE CRIT CRITDMG M-CRIT M-CRITDMG | |
| 25-33 | Resistances ×9 | |
| 34-36 | Aux combat | non documenté |
| 37 | Aggro radius (cells) | ×0.5 u ; pas d'attaque+portée = objet statique |
| 38 | AI profile | id S_Ai |
| 40-49 | Reference ids | S_Spell / S_Enchant / S_ClassDropItem |
| 50 | DoD kill score | 0 = kill non compté |
| 53/54 | EXP / Class EXP (NP) | preuve AttribLog natif |

## S_Spell.ini (45 col) — SpellParser.cs

0 id · 1 code (J001) · 3 blast/motion group · 7 projectile code
(non-vide = missile) · 8 fly speed ×0.5 · 15 catégorie · 16 name
(`T_Spell.ini`) · 17 family (6/7 classe) · 18 type (4/5 heal)
· 19 op-flags i64 · 20/21 cast shape/param · 22 req class level
· 23 class mask i64 (bit(classId−1)) · 26 cast ds · 27 cooldown ds
· 28 range · 29 AoE radius ×2 · 30 shape sub · 32 MP cost base
· 33 element · 34 elf group · 35 damage power · 36 heal power
· 37 variance% · 39 result enchant (S_Enchant) · 40 chance%
· 41 value (−9 def) · 42 pay type (1 or, 9 NP) · 43 upgrade base

## S_Enchant.ini (61 col) — EnchantParser.cs

0 id · 1 group code (E0302) · 3 spell ref · 4 level · 5 name
(`T_Enchant.ini`) · 7 active · 8 trigger type (8208 générique)
· 9-12 trigger params · 16-20 bloc secondaire · 23/24 spell ids
· 37 proc chance% · 38 applied spell · 39 applied chance%
· 47 flag · 56 description

## S_Mission.ini (28 col) — MissionParser.cs

0 id · 1 script name · 2 name (`t_mission.ini`) · 4 complete limit
· 5 min level · 6 flags (bit4 daily, bit2 keep-row) · 10 map name
· 13 conditions `#:If_*` · 14 next/npc · 15 intermediate ·
16 actions `#:NPCTalk/MonsterKilled` · 17 npc/spell · 19 next
· 20 rewards `#:Reward*` · 21 next · 23 catégorie ·
25 trigger group (chaînes) · 27 description.
Syntaxe : `#:Cmd arg…:Cmd2…:::` — 27 commandes connues.

## S_Achievement.ini (15 col)

0 id · 1 need num · 2 sort · 3 code (AC501) · 4 name
(`T_Achievement.ini`) · 5 type (4 = instant) · 6+7 group key
(×100) · 8 points · 9 reward threshold · 10 title id
(`T_Title.ini`) · 11 item reward (`t_item.ini`) · 12 prestige id
· 13 prestige exp / membres `;` · 14 description

## S_Level.ini (27 col)

0 level · 1 EXP · 2/3 base HP/MP · 4 natural DEF · 5-9 STR/AGI/INT/
WIS/LCK · 10 class EXP · 19-22 atk/def min/max · 24 pet EXP · 25 tier

## Tables à structure répétée (formulaire = colonnes génériques)

- **S_DropItem** : id = mobId ou itemId ouvrable ; cols 3-5 = or
  (chance%, centre, spread) ; dès col 10 quadruplets
  `rate|itemId|name|qty` (mobs) ou `itemId|name|qty|rate` (items).
- **S_ClassDropItem** : groupes `chance|itemId|name|qty`.
- **S_Store** : triplets `itemId|name|qty` dès col 3.
- **S_Npc** : DEUX lignes par NPC — head `id|model|title` + body
  `name|flags|…|script|…|role|` (ou record fusionné 21 champs).
- **S_ItemChest** : 1 ingrédient, 5 qualité, 6 qté, 7-18 sous-
  ingrédients, 19 or, 20 taux%, 21 produit (<100000 item, ≥ groupe),
  22 level req, 24 qté produite.
- **S_Talent** : slots `spellId v1..v5` cols 2-9.
- **S_Node** : colonnes après le code map Sxxx anchor-relative —
  voir `GameDataParsers.ParseNodes`.

## Règle d'édition

Le serveur lit `server_us/Data/db_decoded/*.ini` au boot →
redémarrer le service zone. Le client peut lire sa propre copie
(`data/db/C_*.ini` données, `T_*.ini` libellés) — le panneau
« copies » du Studio liste les doublons dont le contenu diffère.
