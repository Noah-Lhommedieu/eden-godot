# K3 — Métier Récolte & Outils (plans VDG mine/ferme/autel + spots, outils, accélérateurs, gains)

Reconnaissance métier croisée guides/wiki FR-EN + `corpus/fr-full/data/db/`.
Complète **D2** (schémas tables guilde, `:26-33,59-77`) et **K5** (obtention/
monnaies, `K5:90-108`) — ici : mécanique fine, ids items, tables de loot.
Vocabulaire : guilde = Family, VDG = Family Town, plans/commissions = **Work**
(métiers 12 ferme? → cf. §2), craft racial = LifeSkill.

Marqueurs : [VERIFIED] preuve directe (table/ligne/script/désasm cité) ·
[HYPOTHESIS] déduction · [GAP] donnée manquante.

---

## 1. Deux systèmes de récolte distincts — ne pas confondre

[VERIFIED] `c_familycollect.ini` (478 entrées, `|V.2|47|`, Big5) couvre **deux
mécaniques** dans la même table `CCollectData` :

| Système | Catalyseur | Exécution | Produits | Visé |
|---|---|---|---|---|
| **Plans VDG** (ferme/mine/autel) | graine/parchemin item (30136-30296, 22038-22117, 23393-23402, 26681-26720, 29011-29050) | commission à un PNJ d'assignation → attente 20-30 min (ferme/mine) ou invocation instantanée d'un mob hostile (autel) | items 30001-30060 (comps classiques) ou **ids de monstres** (autel) | `c_familycollect.ini:2-244,277-356,411,434-473` |
| **Récolte par outil** (spots gratuits potager/mine/source d'énergie, raciale) | l'outil lui-même (36819-37123 blanc/vert, 40970-40978/42104-42106/43850-43852 bleu) | clic droit de l'outil au node VDG → enchant de travail canalisé, ticks jusqu'à durabilité 0 | comps raciales 36791-36817 (lv30-60), 37103-37114 (lv70), 37124-37135 (lv85) + améthyste 36791 | `c_familycollect.ini:245-271,357-365,412-420` |
| Works spéciaux (quêtes/fame/événement) | outils nommés (21/26/31/206/21335/23378/313/384/440/25458/26598/29111-29193) | idem outil | items de quête/fame (22,27,32,74,77,207,21336,23390,314,385,445,25459,26599,29112,29194-96) | `c_familycollect.ini:272-276,366-371,474-477` |

- Le guide FR confirme la dichotomie : « l'artisanat racial est basé sur des
  compos qui s'obtiennent en récoltant avec des outils spéciaux dans les
  villes de guilde. Il n'utilise aucune compo "classique" provenant d'un
  minier, d'un fermier ou d'un invocateur » (`guides-fr/pages/artisanat.md:12`).
- Les compos raciales par niveau d'outil = table EN `wiki-tables/M/
  Gathering_Tools.md:5-12` ↔ FR `artisanat.md:42-64` (lv30→95 ; pêche
  100-110 hors périmètre).
- Les compos classiques par niveau de travail = `guides-fr/pages/
  compos-de-craft.md:39-119` (travail 1 lv20 → travail 12 lv90).

## 2. Schéma `c_familycollect.ini` et sémantique des colonnes

[VERIFIED] Ordre des colonnes = `CCollectQuery::Deserialize` natif désasm
(`eden_server/.../Parsers/WorkOrderParser.cs:21-30`) :

```
workId|type|catalyseurId|nom|qtéCat|nivBâtiment|familyExp|bâtimentExp|expJoueur
     |instant|durée(min)|tauxUsure|usure|coûtOr| {produitId|nom|qté|taux%}×N
```

- **col0 type** (`:31` métiers 12 mining/13 planting/14 summon côté
  S_FamilyCollect ; dans c_*, col1 = 0 ferme « 公會農田培養土 », 1 mine
  « 礦區礦點/outils », 2 autel « 祭壇召喚卷軸 » — D2:30).
- **col5 nivBâtiment** = work level requis 1-12 (§4).
- **col9 instant** : 1 pour TOUTES les lignes autel (`:164,229-244,…`) → pas
  de claim différé, spawn direct ; 0 pour ferme/mine.
- **col10 durée** : 20 min (lv20) → 25 (lv30) → 30 (lv40+) pour ferme/mine ;
  25 ou 50 pour les lignes autel (rôle exact du timer autel = [HYPOTHESIS] :
  durée de vie/fenêtre du mob spawné) ; les lignes outil n'ont ni bâtiment ni
  durée (toutes colonnes 5-13 vides, `:245-276`).
- **col13 coût or** : 122 (lv20) → 361→692→800→875→950→1025→1355→1764 (lv70)
  → 1355/1764 (75-80) → 3492 (85-90).
- **EndCollect** natif @0x8166f60 : chaque produit tiré indépendamment
  (taux ajusté par decline/ext_success), empilé jusqu'à 4 SWorkItem
  (`WorkOrderParser.cs:46-48`).

## 3. Mécanique d'un plan (ferme/mine) — boucle complète

1. **Achat du parchemin** (« Culture Nv. X Travail Nv. Y » / « Gisements Nv.
   X » / « Incantation Nv. X », `t_item.ini:40503,40921,41339`) chez un
   marchand de plans (§6). Non-échangeable (« Can't be Traded »,
   `compos-de-craft.md:11`, ref-shot tooltip `ref-shots-catalog.md:313`).
   Prérequis : **niveau perso ≥ niveau du plan** — col15 de c_item = 20→90
   (ex. 30136 `c_item.ini` col15=20, 22038 col15=75) ; aucun prérequis à
   l'achat selon le guide, mais le level-gate est appliqué au lancement
   (`compos-de-craft.md:9`).
2. **Commission** auprès d'un PNJ d'assignation (`Service <text> <métier>
   <bonus>` dans `c_npc.ini`) : ferme métier **13**, mine **12**, autel
   **14** [VERIFIED — `c_npc.ini` iconv: 50350 `Service 4514 13`,
   50351 `4517 12`, 50352 `4520 14`, `c_npc.ini` (Big5) lignes 635-645 /
   3213-3233 ; métiers 15/16/17 = combine sur 50353-50355 `Service 4524`].
   - Palier du PNJ : apprenti (sans bonus) / **fermier-mineur « +2 »**
     (Service `4515 13 2`, `4518 12 2` sur 53009/53019) / **maître « −10 %
     échec »** (`4516 13 10`, `4519 12 10` sur 53010/53020) — le 3e argument
     Service = bonus chiffré [VERIFIED `c_npc.ini` Big5 lignes 3214-3215,
     3224-3225] ; le guide dit « production +2 » et « taux d'échec −10 % »
     (`compos-de-craft.md:12`). Invocateur unique (53028 `4520 14`, pas de
     palier) — cohérent « en dehors de l'invocateur qui est unique »
     (`compos-de-craft.md:12`).
   - PNJ Aven : **50350 Fermier « Félix »** (Service 4514 13), **50351
     Mineur « Heghurn »** (4517 12), **50352 Invocatrice « Slach »**
     (4520 14) — `t_npc.ini:592-596` + `c_npc.ini` Big5 lignes 635-639.
     PNJ VDG : 53008 Fermier Novice / 53009 Fermier / 53010 Maître fermier ;
     53018 Apprentis Mineurs / 53019 Mineurs / 53020 Maître mineur ; 53028
     Invocateur — `t_npc.ini:3370-3390` ; niveau du PNJ = niveau du bâtiment
     (`compos-de-craft.md:12` ; bâtiments H01001/H01101/H01201, D2:12-13,
     K2:62-64).
3. **Attente** : durée col10 en minutes × 600 unités natif (30 min max
   constaté) — serveur C# `ZoneEntryFlow.Work.cs:40-45,188-200` (DEV plafonné
   100 unités = 10 s). Icône d'attente clignotante à échéance, clic pour
   recevoir (`compos-de-craft.md:13`).
   - **Enchant de travail** 200+work_num armé pendant l'ordre (299/300
     add/remove), retiré à échéance (`ZoneEntryFlow.Work.cs:219-231,616-620,
     688-721`).
   - **~10-11 missions simultanées** : guide « dix missions à la fois »
     (`compos-de-craft.md:12`) vs slots work_num 0-10 persistés
     (`WorkPackets.cs:48`, `ZoneEntryFlow.Work.cs:455`) — [HYPOTHESIS] un
     slot réservé ou borne d'UI.
4. **Loot** : `WorkResultOK` (NCID 171 → réponses 498 WorkResult / 500
   ResultEnd / 497 WorkUpdate) remet les produits tirés + EXP joueur col8
   (2723→14500) + EXP famille/bâtiment col6/7 (`ZoneEntryFlow.Work.cs:
   254-357`, `WorkOrderParser.cs:21-30`).
   - Échec total → work_id négatif persisté (`ZoneEntryFlow.Work.cs:607-609`).

## 4. Work levels, catalyseurs et produits par palier — table de référence

[VERIFIED] Relevé exhaustif de `c_familycollect.ini` (nivBât = col5,
« plans » = catalyseurs, produits = ids col15+ ; niveaux lus dans les noms
Big5 « NN級 ») :

| Niv. plan (perso) | Work lv (bât) | Ferme (type0) — catalyseurs | Mine (type1) — catalyseurs | Autel (type2) — catalyseurs | Exp joueur | Coût |
|---|---|---|---|---|---|---|
| base « 基礎 » | 1 | 34670 | 34671 | 34672 | — | 300 |
| 20 | 1 | 30136-30138 | 30191-30193 | 30246-30248 | 2723 | 122 |
| 30 | 2 | 30139-30142 | 30194-30197 | 30249-30252 | 4500 | 361 |
| 40 | 3 | 30143-30146 | 30198-30201 | 30253-30256 | 6550 | 692 |
| 45 | 3 | 30147-30152 | 30202-30207 | 30257-30262 | 7688 | 800 |
| 50 | 4 | 30153-30162 | 30208-30217 | 30263-30272 | 8900 | 875 |
| 55 | 5 | 30163-30176 | 30218-30231 | 30273-30284 | 10188 | 950 |
| 60 | 6 | 30177-30190 | 30232-30245 | 30285-30296 | 11550 | 1025 |
| 65 | 7 | 23397-23400, 36198-36211 | 23393-23396, 36226-36239 | 23401-23402, 36254-36265 | 12988 | 1355 |
| 70 | 8 | 36212-36225 | 36240-36253 | 36266-36277 | 14500 | 1764 |
| 75 | 9 | 22038-22051 | 22066-22079 | 22094-22105 | 12988 | 1355 |
| 80 | 10 | 22052-22065 | 22080-22093 | 22106-22117 | 14500 | 1764 |
| 85 | 11 | 26681-26694 | 26695-26708 | 26709-26720 | 12988 | 3492 |
| 90 | 12 | 29011-29024 | 29025-29038 | 29039-29050 | 14500 | 3492 |
| « 異界 » (Monde Céleste) | 1 | 28580 | 28581 | 28582 | — | 300 |

(`c_familycollect.ini:2-473` ; work level = nivBâtiment, confirme EN « Work
Level N » des tooltips : lv20→1, lv45→3, lv50→4, lv90→12 —
`pages-index.tsv:8118,8158,8181,8498`.)

- Le **nombre de variantes de plans** croît avec le niveau : 3 (lv20) → 4
  (30-40) → 6 (45) → 10 (50) → 14 (55-60) → 18 (65) → 14 (70-90) — chaque
  variante = un mix de loot différent (§5).
- Doublons de niveau (40 et 45 = work 3) cohérent avec la table FR
  (`compos-de-craft.md:48-56`).
- Les plans « Élémentaire » (34670-72, FR « Culture Secrète Élémentaire »
  `t_item.ini:56511-56523`) et « Monde Céleste » (28580-82,
  `t_item.ini:36996-37008`) = paliers hors progression (tutoriel /
  événement) [HYPOTHESIS usage].
- **[VERIFIED anomalie FR]** : les descriptions t_item des plans lv75+ sont
  décalées — 22038 « Culture Niv. 75 Travail Niv. 7 » liste « Séquoia à
  Pois ×10, Coton de Soie Rouge ×3 » (comps lv65) alors que la table donne
  bât 9 et produits 21971+ (lv75) et que sa desc Big5 dit « 工作等級 9 »
  (`t_item.ini:11686-11690`, `c_item.ini` ligne 49096) ; 29011 « Culture
  Niv. 90 Travail Niv. 1 » est tronqué (work 12 réel). Texte FR à ne pas
  utiliser comme source de vérité pour lv75+ — même classe de bug que les
  pages EN « unofficially-translated » (`pages-index.tsv:8338-8523`).

## 5. Tables de loot des plans — gains par item et par palier

### 5.1 Ferme & mine (type 0/1) — produits = items, chacun tiré à son taux

[VERIFIED] Pattern de loot par variante de plan : chaque produit
`{id|qté|taux%}` est roulé indépendamment (EndCollect). Quantités fixes par
qualité : **×10** (commun), **×3** (vert/orange), **×1** (rare) — ex.
`c_familycollect.ini:2-4` :

- 30136 (*) : `30001 Branche Sèche ×10 @100`
- 30137 (**) : `30001 ×10 @90` + `30008 Coton de Jardin ×3 @60`
- 30138 (***) : `30008 ×3 @80`

Pool de produits par palier (ferme : ids, mine : ids ; noms FR
`t_item.ini:40090-40358`) :

| Work | Ferme | Mine |
|---|---|---|
| 1 (20) | 30001 Branche Sèche, 30008 Coton de Jardin | 30031 Cuivre Brut, 30038 Cristal Pur |
| 2 (30) | 30002 Branche Dure, 30009 Coton de Neige, 30015 Herbe d'Eau | 30032, 30039, 30045 Diamant Œil-de-Chat |
| 3 (40-45) | 30003 Pin, 30004 Pin Montagnard, 30010 Coton Cuivr, 30011 Coton Solaire, 30016 Herbe Céleste, 30017 Herbe-miracle, 30021 Fruit Volta | 30033-30034, 30040-30041, 30046-30047, 30051 |
| 4-6 (50-60) | 30005-30007 Séquoias, 30012-30014 Cotons, 30018-30020 Fleurs, 30022-30024 Fruits, 30025-30027 Vignes, 30028-30030 Graines/Racine | 30035-30037, 30042-30044, 30048-30050, 30052-30054, 30055-30057, 30058-30060 |
| 7-8 (65-70) | 36278/36280/36282/36284/36286/36288 (65) ; 36279…36289 (70) — Séquoia à Pois, Coton de Soie Rouge, Fruit Polaire, Vigne Pourpre… | 36290-36301 — Platine Tempête de Glace, Diamant Étincelant… |
| 9-10 (75-80) | 21971-21982 (Pin Rouge, Coton Givré, Fleur Apaisante, Fruit Fantastique, Vigne de Cerisier Noueuse, Racine Rousse) | 21983-21994 (Minerai Doré, Alliage d'Écailles Fantastiques…) |
| 11 (85) | 26659-26664 (Séquoia, Coton Chimérique, Fleur fanée, Fruit de vision suprême, Épine ensanglantée, Racine minérale) | 26665-26670 (Minerai malléable…) |
| 12 (90) | 28881-28886 (Sapin rougeoyant, Soie d'Or, Pivoine, Mûre Violette, Épine Verte, Racine azuria) | 28887-28892 (Feldspath Carmin…) |

Correspondance exacte avec la table FR `compos-de-craft.md:41-119` et EN
(tooltips « Possible Ore: … ×N » `pages-index.tsv:8118-8512`) — ex. Lv85
Ore Map Type11 ***** : Ocean Diamond ×1 + Flame Crystal + Sunset Amber +
Leaf Crystal (`:8460`).

- **Rareté du plan → qualité obtenable** : */** = blanc+vert+orange ;
  *** = orange seul ; ***/****/***** = orange+doré à partir du work 4
  (lv50) (`compos-de-craft.md:11`, EN decorations-guilde.md:5-99).
- **[VERIFIED doublons FR]** : « Fruit d'éther » = « fruit d'âme pure » ;
  ambre crépusculaire en double skin 80/85 (`compos-de-craft.md:38`) —
  les ids doublons existent (Ambre crépusculaire à work 10 ET 11 :
  `compos-de-craft.md:104,111`).

### 5.2 Autel (type 2) — produits = ids de MONSTRES, loot via drop table

- [VERIFIED] Les « produits » des lignes autel sont des **ids c_monster** :
  58901-58929 (lv20-60), 58957-58966 (65-70), 56711-56715 (75), 56821-56825
  + 56903-56907 (80-85), 56893-56897 (90) — ex. 58901 « Champignon Errant »
  lv20 (`c_monster.ini:7562`, `t_monster.ini:7864`), 58957 « Harpie
  Mystérieuse » lv65, 56711 « Crabe de la Destruction Sinistre » lv75.
- [VERIFIED] Mécanique native (EndSummon @0x8163110) : dé pondéré sur les
  taux cumulés → **UNE** entrée tirée → spawn de `qté` monstre(s) hostile(s)
  ciblant l'invocateur (`ZoneEntryFlow.Work.cs:583-598,628-686`) — « invoque
  un mob donnant des ressources » confirmé ; l'ordre se clôture sans claim
  (435).
- [VERIFIED] Les comps viennent de la **drop table du mob**
  (`c_dropitem.ini`) — ex. 58901 → `30061 Énergie Perdue ×5 @70` +
  `30068 Fourrure d'Animal Sacré ×2 @30` (`c_dropitem.ini:1526`) ; 58959
  Dragon Cornu Mystérieux → `36304 Peau d'âme Enflammée ×4 @40`,
  `36306 Sort d'Emprisonnement ×2 @30`, `36308 Âme Belliqueuse ×2 @30`.
  EN tooltips : « Lost Energy ×5-10, Sacred Animal Fur ×2-4 »
  (`pages-index.tsv:8122-8124`).
- Séries de comps d'autel : 30061-30087 (lv20-60 : Énergies, Fourrures,
  Runes, Sorts, Esprits, Éléments — `t_item.ini:40277-40358`),
  36302-36310+ (65-70), 21995-21998+ (75-80), 26671-72 (85), 28893-94+ (90).
- Divers gameplay : monstre **attaque**, mort du joueur = mob disparu sans
  butin + perte d'XP en VDG (pas à Aven), loot de groupe non-raid possible
  (`compos-de-craft.md:15-17`).

## 6. Marchands de plans — PNJ, magasins, raretés

- **Aven** (parchemins lv20-30 */**/*** only) : **50364 Marchand Agricole**
  → store **70** {30136,30137,30139-41}, **50365 Marchand de Minéraux** →
  store **71** {30191-92,30194-96}, **50366 Marchand d'Invocations** →
  store **72** {30246-47,30249-51} — `c_npc.ini` Big5 lignes 659-661
  (`Service 4523 24 70/71/72`), `c_store.ini:71-73`, `t_npc.ini:617-619`.
- **VDG** (décorations, « Can only be managed in the guild leader's UI »,
  EN `categories/decorations-guilde.md:5,16,31,55-65,78-99`) :
  - ferme : 53011-53015 (Marchand/Érudit/Supérieur/Expert/Spécial) +
    53244-53246 + **53376-53378** (傳說/expert/mystery) → stores
    **501-507** (graines 30136-30187, lv20-60) + **609-611** (lv65-70,
    36198-36211) + **725-727** (lv75-80) — `c_npc.ini` Big5 lignes
    3216-3218, 3472-3474, 3667-3669 ; `c_store.ini` lignes 300-307,409-411.
  - mine : 53021-53026 + 53247-53249 + 53379-53381 → stores **508-514**
    (30191-30242) + **612-614** + **728-730**.
  - autel : 53029-53034 + 53250-53252 + 53382-53384 → stores **515-521**
    (30246-30293) + **615-617** + **731-733**.
  - EN mapping des raretés : Agricultural/Mineral/Summon Merchant */**
    (lv20-40) ; Senior/Supreme */** (41-60) ; Botanist/Mineral Scholars/
    Summon Scholar *** ; Expert/Master ***/**** ; **Mr. Mystery /
    神秘夫人 (53246/53249/53252/53378/53381/53384) ***** orange** —
    `decorations-guilde.md:5-99` + `t_npc.ini:3633-3675`.
- Stores **825-831** (plans lv85-90 : 26681/26695/26709/29011/29025/29039)
  existent dans `c_store.ini` (lignes 624-630) mais **aucun `Service 24`
  c_npc ne les référence** → vente via autre canal (loyauté ? S_Store
  serveur absente — D2:101) [GAP résolu partiellement : stock présent,
  vendeur non mappé].

## 7. Outils de récolte — ids, raretés, durabilité, prix

### 7.1 Ids et durabilités

[VERIFIED] ids complets (c_item.ini / c_itemmall.ini + t_item.ini) :

| Type | Niveau | Blanc (id) | Vert « Alpha/Renforcé » (id) | Bleu « parfait » (id) |
|---|---|---|---|---|
| Pioche (mine, areaType 60) | 30/45/60/70/85 | 36819 / 36825 / 36831 / 37097 / 37118 | 36820 / 36826 / 36832 / 37098 / 37119 | 40970 / 40971 / 40972 / 42104 / 43850 |
| Gants (potager, 61) | idem | 36821 / 36827 / 36833 / 37099 / 37120 | 36822 / 36828 / 36834 / 37100 / 37121 | 40973 / 40974 / 40975 / 42105 / 43851 |
| Boule cristal (source énergie, 62) | idem | 36823 / 36829 / 36835 / 37101 / 37122 | 36824 / 36830 / 36836 / 37102 / 37123 | 40976 / 40977 / 40978 / 42106 / 43852 |

- Noms FR : Pioche, Pioche d'Expert, Pioche de Maître (…/Gourou/Perdue) ;
  verts = « en Titane / Renforcés / Divinatoire » ; bleus = « Parfaite /
  Super Pioche de Gourou / Bonne Pioche Perdue » — `t_item.ini:65339-65406`,
  `t_itemmall.ini:3525-3545,7306-7316,13799-13815`.
- **Durabilité (col74 MaxDurability, `ItemParser.cs:98-101`)** — [VERIFIED
  dans les lignes de continuation de description] : **blanc 180**, **vert
  540**, **bleu 1440** (`c_item.ini` ~lignes 32805-32876 pour 180/540 ;
  `c_itemmall.ini` ligne ~4934 pour 1440) — pile les « 180 ≈ 30 min / 540 ≈
  1 h 30 / 1440 ≈ 4 h » du guide (`artisanat.md:16,26,28`) → [HYPOTHESIS]
  1 point de durabilité par tick de ~10 s.
- Flags : blanc col12=7, bleu col12=131 (0x83 → échangeable mais règles
  HdV spéciales — guide : « échangeables, pas à l'HdV » `artisanat.md:28`),
  kind col8=44.
- **UseEnchantId (col23)** : blancs 57520/57526/57532/56866/41105 (par
  niveau et type — `c_item.ini:32800,32842,32884,33606,33714` ; les
  enchant-rows existent : `c_enchant.ini:4717-4729,9130,17799`, type 812,
  liés à l'item outil + effet 37030) ; bleus 55297-55300/55706-55708/
  55882-55884. C'est l'enchant qui arme la récolte canalisée.
- **Zone d'usage (col32 UseAreaNodes, `ItemParser.cs:518-533`)** :
  « 501 502;60/61/62 » = nodes **S501 森林城鎮 (ville forêt)** et **S502
  山間城鎮 (ville montagne)** (`c_node.ini:1571,1581`) + type d'aire
  60=mine/61=potager/62=source d'énergie → outils utilisables **uniquement
  en VDG**, comme le dit le guide (`artisanat.md:31-34`).
- Comportement : outils cumulables en chaîne (même niveau/qualité),
  immobilisation totale du perso (pas de mail/HdV), groupable en raid —
  `artisanat.md:36-37`. Achat limité : le script PNJ exige
  `If_InventoryItem <outil> <= 0` → pas de doublon du même id sur soi
  (`c_npc.ini` Big5 ligne 3425-3429, §7.2).

### 7.2 PNJ vendeurs d'outils (Aven X638.Y508) — scripts et prix réels

[VERIFIED] Les ventes se font par **dialogue scripté** (`#:Talk … #:If_Gold
>= N :If_InventoryItem <id> <= 0 :If_InventorySlot >= 1 :If_CharLevel >= L
:#:Gold -N :InventoryItem <id> +1`) — pas par un store :

| PNJ | c_npc ligne | Outil | lv30 | lv45 | lv60 | lv70 | lv85 |
|---|---|---|---|---|---|---|---|
| **53199 Phio** « Marchand de Pioches » (n115010, 布魯斯) | `c_npc.ini:3425` | pioches | 36819 −2000 | 36825 −5000 | 36831 −10000 | 37097 −15000 | 37118 −20000 |
| **53200 Ri** « Vendeur de Gants de Jardinage » (n115020, 克勞德) | `c_npc.ini:3427` | gants | 36821 −2000 | 36827 −5000 | 36833 −10000 | 37099 −15000 | 37120 −20000 |
| **53201 Aidey** « Marchand de Boules de Cristal » (n115030, 亞迪拉) | `c_npc.ini:3429` | boules | 36823 −2000 | 36829 −5000 | 36835 −10000 | 37101 −15000 | 37122 −20000 |

(`t_npc.ini:3584-3590` ; dialogues Talk 5874-5876, 6596-6598,
100610-100612.) Correspond au tableau FR « 2/5/10/15/20 PO »
(`artisanat.md:17-24`) → [HYPOTHESIS] l'unité interne `Gold` vaut
1/1000 de l'affichage « PO » (à recouper avec la doc prix de K5).

- **Store 679** (`c_store.ini:479`) contient les 9 outils blancs lv30-60
  mais **aucun NPC `Service 24 679`** dans c_npc → store orphelin/doublon
  legacy [GAP].
- **53198 Hanga « Amateur d'Améthyste »** (卡特, `c_npc.ini` Big5 ligne
  3423, `t_npc.ini:3583`) : échange **1×36791 Améthyste d'Ezur → coffre**
  (37027 « Coffre Ancien de Mine » / 37028 « …de Cueillette » / 37029 « …
  de Magie » — `t_item.ini:65841-65847`) ou **10 améthystes → 10 coffres**
  (Talk 5868-5870, 6599-6601). Contenu : outil vert lv30-95 ou comps
  vertes/oranges (45-60) ou dorées [guide `artisanat.md:26` ; ids coffres
  VERIFIED, contenu à confirmer dans `c_itemchest.ini` — GAP].
- Outils bleus : autel de cristal / promotions / boutique — présents dans
  `c_itemmall.ini` (40970-78 :4927+, 42104-06 :11043+, 43850-52 :20510+).

### 7.3 Spots de récolte par outil — tables de gains

[VERIFIED] Lignes `c_familycollect.ini` :245-271 (lv30-60), :357-365 (lv75),
:412-420 (lv85), type 1, catalyseur = l'outil ; chaque niveau a 3 lignes
(blanc/vert/bleu) avec taux croissants :

| Spot | Produits (blanc → vert/bleu) | Améthyste |
|---|---|---|
| 礦石30 (mine) | 36801 Pépite de Granite Doré @40→50, 36802 Tuile de Pierre de Rêve @10→15, — | 36791 @0.2→0.4 |
| 草藥30 (ferme) | 36792 Champignon du Printemps @40→50, 36793 Racines de Sérénité @10→15 | @0.2→0.4 |
| 能量30 (énergie) | 36810 Éclat Scintillant de Mana @40→50, +2e comp | @0.2→0.4 |
| *45 / *60 | mêmes formes (36803-36814, 36792-36799…) | @0.5→0.8 / @1→1.3 |
| 礦石75 | 37107 Pyrite hyperpolaire, 37108 Feldspath, 37109 Wolframite Lunaire @40-50/10-15/3-4.5 | @1→1.3 |
| 農場75 | 37103 Branche d'Hyphe, 37104 Fleur Fungi, 37105 Jasmin Fungi | @1→1.3 |
| 能量75 | 37111 Énergie Hardonique, 37112 Vague de Quark, 37113 Salve de Flammes | @1→1.3 |
| 礦石85 | 37128 Pierre aux pétales fruités, 37129 Globe de l'aurore, 37130 Diamant de sang | @1→1.3 |
| 農場85 | 37124 Bactérie rouge, 37125 Ginseng jaune, 37126 Emblème du Dragon de ginseng bleu | @1→1.3 |
| 能量85 | 37132 Pensées angoissantes, 37133 Fluide d'Oracle, 37134 Feu tectonique | @1→1.3 |

- Les taux verts = bleus (50/15/0.4-1.3) — l'avantage du bleu = durabilité
  + échangeabilité, pas le rendement.
- La **4e comp de chaque palier (dorée)** — ex. 37131 Cristal du Dragon de
  glace, « Diamant de soleil » lv60 — n'apparaît PAS dans les lignes de
  spots : obtenue via les coffres de Hanga, comme le dit le guide
  (`artisanat.md:40`, `:53,61`).
- **Améthyste d'Ezur 36791** : récoltable avec tout outil/niveau
  (`artisanat.md:40`) — taux 0.2→1.3 % selon niveau de spot [VERIFIED].
- Pas de spot outil lv95 dans la table FR (outil 95 = Vendetta,
  `artisanat.md:24`) — cohérent : les lignes s'arrêtent à 85.
- Spots « monde » hors VDG : [GAP] la table ne contient que des works VDG/
  quête ; aucune ligne de récolte de nodes monde ouverts n'a été trouvée
  (les lignes « spéciales » 272-276, 366-371, 474-477 sont des outils de
  quête/fame : ex. 亞瑟的開山之鎚 item 26 → produits 27+74 = faction Arthur
  Hammer, `c_familycollect.ini:273`).

## 8. Accélérateurs, aides, boosters

| Item | Id(s) | Effet | Source |
|---|---|---|---|
| **Parchemin de Récolte Instantanée** (−95 %) | **40414** (NT, flags 0) / **40415** (échangeable, flags 4) — « 快速採集卷軸 » | « réduit le temps de récolte de 95 % » | `t_itemmall.ini:1700-1701`, `c_itemmall.ini:2027-2029` (useEnchant 55150) ; EN `pages-index.tsv:7392` |
| Parchemin de vitesse −50 % | [GAP id non trouvé en tables] | −50 % du délai | chemin de la destinée (cases marrons) `compos-de-craft.md:13`, `K5:95` |
| **Aide à la récolte** (détecteur) | **40078** (NT) / **40096** (échangeable) — 採集偵測器 | « améliore le taux de réussite de 8 % » lors d'une commission | `c_itemmall.ini:525-527,599-601` (desc Big5 verbatim) ; obtention PoD + pièces alpaga `compos-de-craft.md:13` |
| **Super Fertiliseur** | **22418** (jeu, craft zumi lv70) / **42625** (boutique) | « augmente le rendement de 2 » / EN « increase effectiveness and productivity of Farm and Mine items » | `t_item.ini:12781`, `c_item.ini:50202`, `t_itemmall.ini:9389`, EN `pages-index.tsv:11520` ; guide `compos-de-craft.md:13` |
| Poudres booster (slot NCID 170) | Corona's Essence 37006-37018, Essence Powder 22433-22445 | +30 ext_success_rate — **déviation serveur documentée** : kinds natifs CChanceItem 0x1f43-0x1f48 absents des données | `ZoneEntryFlow.Work.cs:93-118` |

- Mécanique booster : NCID 170 PlayerWork porte catalyst(bag,slot) +
  **booster(bag,slot) optionnel** (`ZoneEntryFlow.Work.cs:60-63`) ; le
  −95 % et le détecteur sont des consommables de commission (le client les
  passe en slot booster — [HYPOTHESIS] même canal).

## 9. Protocole / état serveur (repères pour G2/S1)

- NCID : **170** PlayerWork (démarrage), **171** WorkResultOK (claim),
  **172** WorkCheckData (refresh) ; serveur → **497** WorkUpdate (slots
  work_num 0-10 + enchant 200+num), **498** WorkResult, **500**
  ResultEnd, **435** clôture summon, **299/300** enchant add/remove,
  **274** — `ZoneEntryFlow.Work.cs:13-48`, `WorkPackets.cs:12-70`.
- Persistance : 11 lignes `player_works` (`ZoneEntryFlow.Work.cs:455`).
- Tables serveur S_FamilyCollect/S_FamilyCombine **absentes du corpus**
  (D2:101) — les defs C# sont alimentées par les c_* parsés.
- Client Godot : `ZoneCraftMessages.cs`, `CraftWindow.cs` existent
  (domaine G1/G3).

## 10. Succès & bonus liés

- Succès légendaires de récolte jusqu'au lv65 : Apprenti/Maître
  cultivateur-mineur-invocateur (+3 % stats) puis « La loi des végétaux » /
  « Plus de minéraux s'il vous plaît » / « Cette fois j'invoque » (+4 %),
  englobés dans « La légende d'Ezur » (monture dragon) —
  `compos-de-craft.md:23-35` ; au-delà de 65, succès municipaux individuels
  (`:23`).
- Les récoltes achetables à la boutique de loyauté **ne comptent pas** pour
  les succès (`compos-de-craft.md:20`).
- Succès municipal « Deep Miner » visible sur `Profile-Achievements.png`
  (`ref-shots-catalog.md:461`).
- Tooltip plan de mine lv90 : `ref-shots/item hover (plan mine
  ressources).png` — Work Level 12, Crimson Diamond/Bluefire Crystal/
  Deepfathom Shell/Lavender Crystal ×1, « Can't be Traded », Level Req 90
  (`ref-shots-catalog.md:308-313`) — recoupe `pages-index.tsv:8498-8511`.

## 11. GAPs

1. **Id du parchemin −50 %** : introuvable dans c_item/t_item/t_itemmall
   (guidé par PoD ; peut-être item exclusif event/destiny road non tabulé
   en FR, ou nom différent).
2. **Vendeur des stores 825-831** (plans lv85-90) : aucun `Service 24`
   c_npc → probablement boutique loyauté ou S_Store (absente, D2:101).
3. **Store 679** (9 outils blancs) : aucun NPC ne sert ce magasin dans
   c_npc → orphelin ; la vente réelle passe par les Talk-scripts §7.2.
4. **Contenu des coffres Hanga** 37027-37029 : non vérifié (lire
   `c_itemchest.ini`).
5. **Colle temporelle autel** : col10=25/50 sur lignes instantanées — rôle
   exact (TTL du mob ? fenêtre de loot ?) non confirmé.
6. **Unité `Gold`** des scripts (2000=« 2 PO » guide) : vérifier le ratio
   avec `S_Parameter`/doc économie K5 (hypothèse ×1000).
7. **Cap missions** : 10 (guide) vs work_num 0-10 (11 slots natifs) —
   trancher par désasm ToCollect (borne réelle).
8. **Durée/tick des outils** : durabilité 180/540/1440 ≈ 30 min/1 h30/4 h
   ⇒ ~10 s/tick déduit, pas prouvé en désasm (chercher le décrément de
   cur_maxdurability dans la boucle de collecte natif).
9. **Spots de récolte monde ouvert** hors VDG : aucune table trouvée —
   soit gérés par c_node/autre système (ex. items de quête 29111-29193
   « 十字鎬/手套 »), soit absents du périmètre EDENFR.
10. **Plans « Monde Céleste »** (28580-82) et « Élémentaires » (34670-72) :
    canal d'obtention/PNJ d'assignation non identifiés (produits 28583-84,
    mobs 58929/63402).
