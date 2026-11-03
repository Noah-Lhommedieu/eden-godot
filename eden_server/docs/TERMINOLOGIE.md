# Terminologie — enchant / buff / statut (référence projet)

> **But** : fixer le sens exact des mots utilisés dans le code, les
> plans et les rapports — le natif mélange tout sous `enchant`
> (`CEnchant`, `SEffectData`, `EffectToLife`), ce qui a déjà produit
> des bugs d'interprétation (talents affichés comme status, buffs
> jamais appliqués…). Date : 2026-10-24.

## Définitions retenues (vocabulaire projet)

| Terme | Sens |
|---|---|
| **enchant** | Une **statistique ou un proc posé sur un équipement** (ou licence/titre) : ligne S_Enchant portée par un item (`CItemData+0x60`), un slot de licence, un titre actif. Effet *passif et durable* — tant que l'objet est porté/posé. |
| **buff** | Un **gain de stats temporaire** appliqué à une entité : sort de buff (skill casté, c39 résultat) ou proc déclenché. Possède une durée, une icône 299 sauf exceptions, vit dans `EntryState.Statuses` / `player_enchants`. |
| **debuff** | Un buff négatif posé par un adversaire. |
| **status** | L'**instance live** d'un buff/debuff : `ActiveStatus` dans `st.Statuses`, icône `CNC_CZ_ClientEnchantAdd` (299), retrait 300, tick 3001/3002. |
| **talent** (advanced ability) | Enchant **caché** du natif (`flags=0x1000` → `IsHideIcon`) appliqué 1×/point investi (`InvokeAAEnchants`) — stats via le stage « talents » du pipeline (args × points), jamais d'icône ni de ligne `player_enchants`. |
| **commande** (enchant command) | Un bloc `cmdId\|var1..6` de S_Enchant (col 9+7k) — exécuté par les `CEC_*` natifs à l'apply (Execute) / retrait (RollBack × counter). |
| **proc** | Un enchant/buff déclenché par un événement (OnAttack*, OnAttacked…) — les buckets `SEffectData` du natif. |
| **spell attr mod** | Modificateur **par sort** (`ILifeEntity::AddSpellAttrModify`, arbres char+0x6xx/0x704/0x720) — portée/radius/cooldown/crits/dégâts d'un spellId précis. Côté C# : `EntryState.SpellAttrMods` kinds 0-8. |

## Correspondance native

Le natif appelle tout ça « enchant » (`CEnchantMgr`, `CEnchant`,
`SEffectData`, `EffectToLife`, `ILifeEntity::AddEnchant`). Règles de
lecture prouvées par désassemblage :

- **Durées sentinelles** (`param_4` de `EffectToLife` @0x08060ec0,
  rest_time wire) :
  - `-1` → exécution **instantanée** des buckets +0x20/+0x2c, jamais
    de `CEnchant` créé (pas de status).
  - `-9` → `AddEnchant` **inconditionnel** mais **icône supprimée**
    (gate `param_3 != -9` dans `ILifeEntity::AddEnchant` @0x847ea90) :
    l'effet vit par sa durée propre, stats actives, **pas de 299**.
  - `-2` → permanent (buffs système, talents cachés flags 0x1000).
  - `>0` → `AddEnchant` si l'effet a des commandes gérées
    (`SEffectData+0x12c != 0`), sinon instantané.
- **Icône 299** émise seulement si `param_3 != -9` ET
  `(enchant+0x28 & 0x7c000000) == 0` ET `!IsHideIcon()`
  (`SEffectData+0x118 & 2` OU flags 0x1000).
- **Résultat de sort** : `SSpellData+0x7c` = pointeur `SEffectData*`
  (S_Spell c39), chance c40 (`+0x80`), durée c41 (`+0x84`, vide → -9).
  Appliqué **par victime** du HitTest via `EffectToLife` après le roll.
- **Stats** : les commandes `Attr*` écrivent des accumulateurs
  `CKindData` (`char+0x8c`) — flats (+0x124, +0x134…) et % flottants
  (`arg × 0.01` → +0x104/+0x110/+0x12c/+0x13c…), consommés par les
  `CCharFormula::*Rate/*Level` comme facteur `(1 + accum)`.

## Côté port C#

- `EnchantCommands.Names` : table cmdId → nom natif — **les noms
  doivent être ceux émis par `DecodeEffectCommand`** (dispatch
  @0x8380ad0), pas des inventions (ex. 2043 = `AttrCriticalDamage`
  direct, PAS `AttrSpellMagicCriDamages` qui est 2099).
- `SpellAttrKindOf` : kinds 0-6 = `AddSpellAttrModify` (548 émis pour
  0-2), kinds 7/8 = arbres `SpellDamage` flat (+0x704, cmd 2063) /
  rate (+0x720, cmd 2064).
- `player_enchants` : lignes persistées = buffs/debuffs live à
  restaurer — **jamais** les talents (hidden, stats via
  `advanced_ability`) ni les enchants d'équipement (re-dérivés de
  l'inventaire).

## Pièges déjà corrigés (référence)

1. Talents restaurés depuis `player_enchants` → icônes 299 au login
   → `RestorePlayerEnchantsAsync` skip+purge les effect_ids AA.
2. Buffs à durée vide (c41 → -9) jamais appliqués → gate natif
   `dur != -1` + `hidden` pour -9 (icône supprimée native).
3. Self-buff casté via NCID 17 n'appliquait rien → enchant résultat
   appliqué au lanceur dans `NowhereCastReleaseAsync`.
4. AoE ToLife (shape 0 + rayon ≥ 1, ex. Tornado 50027) limitée à la
   cible → le HitTest natif collecte un cercle autour de la cible.
