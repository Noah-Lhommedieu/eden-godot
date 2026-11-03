# WEB-DATA-AUDIT — audit des données du portail (P3.1)

> Généré le 2026-10-24 — Lot 7 P3. Sources :
> - **Numérique (vérité)** : `server_us/Data/db_decoded/S_*.ini` (Big5,
>   pipe `|` ou virgule `,` — records multi-lignes : une ligne qui
>   commence par un chiffre ouvre un nouveau record).
> - **Libellés EN** : `reforged/EdenReforged/data/db/T_*.ini` (latin-1,
>   2 lignes d'en-tête : nb de colonnes puis indices 1-basés des
>   colonnes traduites de la table c_/S_ correspondante — les layouts
>   S_ et c_ sont IDENTIQUES, vérifié par version header : S_Item
>   `|V.11|78|` = c_item `|V.11|78|`, idem Monster/Mission/Achievement/
>   Class/Spell/Enchant/Npc).
> - **Sémantique des colonnes** : prouvée par les parsers C#
>   `server/src/Eden.Core/Data/Parsers/*.cs` (désasm lapis cité en
>   commentaire — ex. `CItemDBO::SetItemAttrData @0x8358200`,
>   `CSpellQuery::Deserialize @0x862af40`).
> - **Icônes** : `reforged/EdenReforged/UI/{itemicon,skillicon,uiicon}/
>   <code>.dds` — code lu dans la table (item c1, spell c1,
>   achievement c3, class c3). Recherche dans cet ordre : itemicon →
>   skillicon → uiicon.

## État actuel (avant P3)

Le backend `WikiService` parse les `S_*.ini` Big5 en direct :

- **Noms affichés en Big5 brut** (chinois) sur items, mobs, missions,
  succès, crafts, talents, NPC — ~0 % traduit à l'écran. Seules les
  classes avaient une jointure EN partielle (T_Class/T_Spell via
  `client/data/db`, chemin legacy).
- **Colonnes fausses** : items affichaient « Niveau » = col8 = le
  **kind** d'item (44, 16391…) pas le niveau requis (col14) ; succès
  affichaient « pts » = col6 non prouvée ; crafts = `S_ItemCombo` sans
  résolution des matériaux ; `talents` listait des lignes d'arbre
  brutes sans noms EN (S_Enchant Big5 pour les effets 5xxxx).
- **Manquant** : certificats (S_Item kind 40), descriptions EN,
  icônes réelles, récompenses de missions parsées, titres, NPC, maps,
  pages détail par entité, classements par classe, perks Limitless
  exposés en lecture publique.
- `Crafts` = `S_ItemCombo.ini` seul — la recette affichée n'avait ni
  résultat ni nom EN.

## Source de vérité décidée (P3.2)

| Couche | Source | Règle |
|---|---|---|
| Valeurs numériques | `S_*.ini` serveur | Colonnes prouvées par `Eden.Core/Data/Parsers` ; toute autre colonne = non affichée |
| Libellés | `T_*.ini` Reforged (EN, latin-1) | Jointure par id ; champ absent → `name:null` + flag `untranslated:true` (jamais de Big5 brut) |
| Icônes | `UI/<set>/<code>.dds` | converti en WebP 64×64 → `frontend/public/icons/<set>/<CODE>.webp` |
| Données joueurs | PostgreSQL `fndb1`/`fnaccount` | inchangé (rankings, dashboard, admin, limitless state) |

## Couverture EN réelle (mesurée)

| Dataset | Table serveur | Lignes | T_ EN | Couvert |
|---|---|---|---|---|
| Items | S_Item (V.11,78) | 20 427 | t_item (c8 nom, c35 desc) | 19 366 — **95 %** |
| Monstres | S_Monster (V.6,62) | 12 896 | t_monster (c3 nom) | 11 372 — 88 % |
| Missions | S_Mission (V.5,28) | 2 831 | t_mission (c3 nom, c11 map, c28 desc) | 2 459 — 87 % |
| Sorts | S_Spell (V.8,45) | 4 485 | T_Spell (c17 nom, c45 desc) | 4 485 — **100 %** |
| Enchants | S_Enchant (V.3,61) | 19 829 | T_Enchant (c6 nom, c56-58 desc) | 19 829 — **100 %** |
| Succès | S_Achievement (V.4,15) | 3 843 | T_Achievement (c5 nom, c15 desc) | 3 843 — **100 %** |
| Classes | S_Class (V.6,65) | 305 | T_Class (c2 nom, c5-8 desc/req) | 305 — **100 %** |
| Crafts | S_ItemCombo (V.4,42) | 5 858 | T_ItemCombo (c3 nom) | 5 408 — 92 % |
| NPC | S_Npc (V.2,21) | 3 083 | t_npc (c3 nom, c21 info) | 2 824 — 92 % |
| Titres | S_Title (V.3,6) | 337 | T_Title (c2 nom, c3 cat, c5 bonus, c6 obtention) | 337 — **100 %** |
| Maps | S_Node (V.9,78) | — | t_node (c2 nom + c49.. desc) | partiel |
| Certificats | S_Item kind=40 | 156 | via t_item | idem items |
| Talents | S_Talent (915 arbres) | 915 | effets via T_Enchant, classes via T_Class | ~100 % |
| Elfes | S_Elf (V.7,42, `,`) | 226 | — aucune table T_Elf | **0 % → dataset reporté** |
| Perks Limitless | `limitless_perks` (DB) | seed serveur | libellés EN déjà en DB | 100 % |

## Champs exportés par dataset (colonnes 0-basées)

- **items** : id, icon(c1→itemicon), name EN(c7 via t_item), desc(c34),
  kind(c8), equipLoc(c9), useType(c12), reqLevel(c14), maxLevel(c15),
  quality(c19), useEnchant(c23), maxStack(c29), sellPrice(c31),
  stats c37-43 (HP/MP/STR/AGI/INT/WIS/LCK), element(c44), pAtk(c45),
  mAtk(c46), variance(c47), atkRange(c48), atkSpeed(c49), def(c50),
  hit/crit… c51-56, resist c63-71, sockets(c72), durability(c74),
  repairPrice(c76). *Corrige : « level » affiché avant = kind.*
- **certificates** : sous-ensemble items `kind==40` (156 entrées —
  les certificats de classe, ex. 33272+).
- **monsters** : id, model(c1), name EN(c2), killType(c3), level(c4),
  type(c5 : 1 normal/2 élite/3 boss), enabled(c7), hp(c8), mp(c9),
  atkElement(c13), atkPower(c14), variance(c15), range(c16),
  atkSpeed(c17), pDef(c18), hit(c19), dodge(c20), crits(c21-24),
  resists(c25-33), aux(c34-38), aggroCells(c37), aiId(c38),
  refIds(c40-49), killScore(c50), exp(c53), classExp(c54).
- **missions** : id, name EN(c2), map EN(c10 via t_mission c11),
  minLv(c5), maxLv(c6), flag(c6→daily bit4 via col6? non — flag c7,
  daily = bit4 de c6… cf. MissionParser : CompleteLimit c4, daily =
  bit4 de Field6), conditions script(c13), actions(c16),
  rewards(c20 → parsé : RewardGold/RewardExp/RewardItem id+qté
  résolus EN/RewardReputation), category(c23 tag S00x Big5 →
  mapping EN main/daily/repeat/special/adventure), desc EN(c27).
- **spells** : id, icon(c1→skillicon), name EN(c16 via T_Spell c17),
  desc EN(c44 via T_Spell c45), blastId(c3), projectile(c7),
  flySpeed(c9), category(c15), family(c17), type(c18), castShape(c20),
  reqLevel(c22), castDs(c26), downtimeDs(c27), range(c28),
  duration(c29), mpCost(c32), element(c33), power(c35),
  healPower(c36), variance(c37).
- **classes** : id, name EN(T_Class c2), short(c5), trait(c6),
  desc(c7), requirement EN(c8), icon(S_Class c3→uiicon),
  spells(S_Class c30-42 → noms T_Spell), knowledge(S_Class c55+),
  awakened = requirement non vide / id>20.
- **talents** : par classe — S_Talent rows {classId, slots:
  effectId,maxPts,reqEffectId,reqPts,flag,levelReq} ; noms d'effets
  via T_Enchant (c6), noms de classes via T_Class.
- **achievements** : id, icon(c3), name EN(c4 via T_Achievement c5),
  category(c2), tier(c5), target(c6), count(c7), desc EN(c14),
  points extraits du texte EN « Achievement Points: N » (c6 en repli —
  non prouvé, marqué dans l'audit).
- **crafts** : S_ItemCombo id, name EN(c2), materials c3 `#id#…#`
  résolus en {id, name EN} ; result = premier matériau non-ingrédient
  inconnu → affiché comme « combo set » (pas de colonne résultat
  prouvée — ne pas en inventer).
- **titles** : id, name EN(c1 via T_Title c2), category(c2),
  bonus EN(c4 via T_Title c5), obtain EN(c5 via T_Title c6).
- **npcs** : id, code(c1), name EN(c2 via t_npc c3), info EN(c20).
- **maps** : id, name EN(c1 via t_node c2), desc EN(c48 via t_node
  c49), type(c3), level range best-effort.

## Corrections de données fausses (P3.5)

| # | Avant | Après | Preuve |
|---|---|---|---|
| 1 | Item « Niveau » = col8 (kind, ex. 44/16391) | reqLevel = col14 | ItemParser doc — `CItemData` mapping |
| 2 | Succès « pts » = col6 | points depuis texte EN « Achievement Points: N » | T_Achievement desc |
| 3 | Crafts : matériaux en ids bruts/Big5 | ids → noms EN t_item | T_ItemCombo + t_item |
| 4 | Talents : noms d'effets Big5 | noms EN T_Enchant | T_Enchant c6 |
| 5 | Classes : desc Big5 / partielle | EN complète T_Class (305 classes, éveils inclus) | T_Class |
| 6 | Missions : type affiché tag Big5 « S002主線 » | mapping EN (Main/Daily/…) + récompenses parsées | MissionParser col20/23 |
| 7 | Pas de détail entité | page détail par dataset (stats réelles) | — |

## Hors champ / limites connues

- Elfes : aucune traduction EN disponible (pas de T_Elf) → dataset
  exclu du wiki v2 (noms Big5 = pire qu'absent). À réintroduire si une
  source EN apparaît (uidata lua ?).
- Montures : pas de table dédiée — les montures sont des items
  (kind ride, ex. 20051) déjà dans `items`.
- `c6` succès affiché « points » avant = non prouvé → remplacé par
  l'extraction texte EN (voir #2).
- S_ItemCombo n'a pas de colonne « résultat » prouvée — le site
  présente les combos comme ensembles de matériaux, pas de recette
  « A+B→C » (pas de valeur inventée).
