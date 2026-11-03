# Audit chaîne « visuels équipements » — c_item.ini → 4pequip.ini → mesh+texture (K#####)

Statut : **verified** (code × données corpus croisés) pour le parsing et
le comportement aux codes manquants ; **hypothesis** pour la fidélité au
client natif (non désassemblé ici) — 2026-11-03 — lecture seule, aucune
modif, aucun build/test. Miroir de `class-skins-audit.md` (codes A de
classe) pour les codes K d'équipement.

Vérité terrain : `artifacts/corpus-inventory/equip-visuals.tsv` (11 099
lignes `code_K × corps → groupe/mesh/texture/dds/items`, notes compagnes
`equip-visuals-notes.md`). Chaîne attendue : `c_item.ini` (f1
`code_item`, f2 `ModelCode`) → `4pequip.ini [TableCorps]`
(`DBn=code,grp,mesh,skin,n,dyeHex,0`) → mesh `#Xnnn` interne au
méga-GLB corps (+ peau `SXnnn`), mesh externe `char/model/<mesh>.nif`
attaché à un os, ou `W#####` → `item/model/<stem>` sur bone arme.

## Vérifié sur les fichiers réels `corpus/fr-full/data/db/`

- `4pequip.ini` (Big5, CRLF, `[INFO] Version=103`) : 12 780 lignes
  `DBn=` = **1 680 A + 11 099 K**, soit **2 491 codes distincts (240 A +
  2 251 K)**. **0 doublon de code par table** (comparé insensible à la
  casse). **Un seul code en minuscule** : `DB1508=k10616,2,C1249,…`
  (TableShota, l. 12465) — `c_item` référence `K10616` en majuscules.
- `c_item.ini` : 18 794 records (`|V.11|78|`, multi-lignes). Codes
  `K#####` en f1 (`code_item`, col1) et/ou f2 (`ModelCode`, col2) :
  ~364-366 codes distincts référencés par 446 items ; **0 k minuscule**.
  - f1==f2 : 163 items ; f1≠f2 : 160 items ; f2 vide : 28 items
    (ex. 21254 « Oreilles du Lapin de Pâques » — f1 `K10006` seul).
  - f1≠f2 : **134 items ont les deux codes dans la même table corps**
    (la priorité de recherche compte) ; **26 items ont des codes à
    couverture disjointe** — paires mâle/femelle (ex. 24264
    « Robe Papillon » : f1 `K20267` → `#B251` dans Boy/Mouse/Frog/Kuma/
    Shota, f2 `K20263` → `#G254` dans Girl/Loli seulement).
- `c_itemmall.ini` (7 460 records, même parseur `ItemTable.Load`) :
  **2 242 codes K distincts référencés** — contrairement aux notes
  d'inventaire (« aucun K##### »), la boutique est la **principale**
  source de costumes. 177 de ses codes manquent dans 4pequip.
- Codes K référencés mais **absents de 4pequip** (recomptage exact,
  sémantique `IsRecordBoundary` d'`IniTable`) :
  - `c_item` : **39** (31 `K3####` + 8 `K90###` : K90001/02/04/05/08/18/
    21/30 — les notes disent « 37 K3 + K90001/02/05/18 », même total 39,
    composition corrigée ici) ;
  - `c_itemmall` : **177** (`K10065-69`, `K10694-705`, `K20761`, ~130
    `K3####`, ~30 `K90###`) ;
  - **union : 193 codes** (15 `K1`, 1 `K2`, 146 `K3`, 31 `K9`) — la
    lacune G22 (`docs/workflows/EDENFR-GAPS.md`) est donc ~5× plus
    large que documenté.
  - Orphelins réels (codes K jamais cités par un item, c_item ∪ mall) :
    **92** (et non 1 924 — l'inventaire n'a pas joint c_itemmall).
- Items à codes manquants = **costumes « arme camouflée » et accessoires
  boutique** : kind 61/65 (Costume), locs 7/9/19 (armes) ou 1-5
  (ex. 21253 `K30012` « Lance du Dragon de Jade Personnalisée », loc 9 ;
  20410 `K90004` « Brochette de Viande Fumante » ; 39328 `K90018`
  kind 44 non équipable).
- Familles de groupes (toutes tables) : grp 0 → 4 931 lignes `#Xnnn`
  (tenue corps) + **56 lignes non-`#`** (16 codes `K2097x-85/2106x-8x`,
  meshes `?2124/2126/2135/2137` — orphelins, `.nif` présents) ; grp 1 →
  1 940 lignes `X####` externes = **accessoires de dos, loc 6** (ailes,
  sacs — items `K6####` : kind 68, loc 6 ; le libellé « armes » des
  notes d'inventaire est erroné) ; grp 2 → 5 853 lignes externes
  (casques ET pièces d'armure, jamais de `#`) ; **aucun grp 4**.
- Meshes `#` à suffixe 4 chiffres existent (`#B2100`…`#B2143` par corps,
  codes `K20745+`) : `Mesh[2..]` → « 2100 » reste un suffixe entier.
- `4pequipmap.ini` (145 lignes de noms de textures attendues) :
  **jamais lu** par le client — la convention `char/texture/<skin>A|B.dds`
  suffit (vérifié présents : `g2100s01a/b`, `b230as01a/b`, `x652ah01a/b`…).
- `mesh=NULL` littéral : `K10707` (TableFrog) ; textures `NULL` :
  357 lignes — porté mais sans visuel/masque.
- `.nif` externes manquants (G14) : `M/F/K1213` (K10253-57, K10470),
  `M/F/K1208` (K10435) ; peaux `X628A81A/B` (K60234/35).

## Maillon 1 — l'item porté : `worn` loc→itemId + lookup (Code, ModelCode)

| | Attendu | Constaté |
|---|---|---|
| Items portés | conteneur 0 (tenu) + pages costume 2-4 fusionnées | `PlayerEquipment` : `_worn` (311 `ApplyEnterNode` conteneur 0, ZoneEquipMessages.cs:276-287) + `_costumes` (conteneurs 2-4, L283-284) → `VisualWorn` = `CostumeVisual.Merge` (L249-251) ; 276/282/277 tenus à jour (L290-319) |
| Distants | set0 du 263 + deltas 276 | `RemoteEntity.WornItems`/`CostumeItems` (EntityRegistry.cs:155-161, 1401-1407, 1481-1499) |
| itemId→codes | `c_item` col1 `Code`, col2 `ModelCode` | `ItemTable.cs:169-170` ; lookup = `InventoryWindow.LookupDef` (EquipSlotsPanel.cs:269-273, EntityRegistry.cs:189-194) — **c_item + c_itemmall fusionnés** (`_items.TryAdd`, InventoryWindow.cs:562-574) |
| Item absent de la table | ignoré | `lookup(itemId)==null → continue` (CharVisual.cs:1028-1029) — muet |

⚠️ `CharSelectScreen.ItemLookup` (L706-734) charge **c_item seul** (sa
propre table `_items`, sans c_itemmall) — les ids 40xxx boutique ne
résolvent pas au sélecteur (cosmétique : `PreviewWorn` = conteneur 0).

Verdict : **conforme** (la fusion mall est nécessaire — les costumes
sont des items boutique).

## Maillon 2 — `LoadEquipMap()` / 4pequip (CharModelSpec.cs:154-213)

- Parse manuel `[TableX]`, `EquipTableName` couvre les 7 corps
  (L81-89, défaut TableGirl), lecture Big5 `EdenVfs.ReadLines` (L175).
- `DBn=code,grp,mesh,skin,…,dyeHex,0` → `EquipMesh(mesh,skin,grp,dye)`
  (L190-201) : parts[0]=code, [1]=grp, [2]=mesh, [3]=skin, **[4] (`n`)
  ignoré**, [5]=dye hex, [6] ignoré. Champ `n` (3/1/2/0 — hypothèse
  inventaire : nombre de canaux dye) non vérifié, non utilisé.
- Clé = code, `OrdinalIgnoreCase` (L161-162) → `k10616` résolu par
  `K10616`. **A et K dans le même dict, même chemin** : aucun
  traitement différencié — le split A/K n'existe que dans les données
  (A0xxxx = tenues de classe, K##### = équipement/costume). 0 doublon
  → `map[code]=` jamais en conflit.
- Cache par lettre + verrou (L71-76, thread-safe preview async).

Verdict : **conforme** — mêmes remarques que l'audit A (repli
`RowForSuffix` L219-227 = 1ʳᵉ variante du mesh partagé).

## Maillon 3 — `ResolveEquippedMeshes()` (CharVisual.cs:1014-1080)

Cœur de la chaîne. Pour chaque `(loc, itemId)` de `worn` :

1. `map.TryGetValue(info.Code)` **puis** `map.TryGetValue(info.ModelCode)`
   (L1032-1033) — **Code (col1) prioritaire, ModelCode (col2) en repli**.
   Pour les 26 items « paire genre » disjoints, un seul code existe dans
   la table → ordre indifférent. Pour les **134 items à recouvrement**,
   f1 gagne toujours (ex. 26449 robe violette : `K20032`→`#G224` choisi
   au lieu de `K20017`→`#G217` ; chapeaux 34791+ : `K10010`→`G1203` au
   lieu de `K10018`→`G1126`). Ordre natif non prouvé → voir H2.
2. `row.Mesh.StartsWith('#')` → `outfits.Add(row.Mesh[2..])` +
   `rows[suffix]=row` (L1035-1045) : tenue interne du méga-GLB.
   **Deux items sur le même mesh partagé** (K20006-10 → `#G218`) → le
   dernier de l'itération écrit `rows[suffix]` (ordre d'insertion de
   `worn` = ordre des records loc croissants en pratique) → dye de la
   dernière pièce. Déterministe en pratique, non spécifié.
3. mesh non-`#` non-`NULL` → `attach.Add((AttachmentBone(row.Group),
   "char/model/<mesh>", row))` (L1047-1050) ; `NULL` → rien (L1051).
4. Arme : `WeaponModelStem(ModelCode) ?? WeaponModelStem(Code)`
   (L1054-1055 — **ordre inverse** du lookup 4pequip) →
   `item/model/<W+3ch>` (+`#<W+5ch>` variante de skin) sur bone
   `7|9→"Bip01 Prop1"`, `8|10→"Bip01 Prop2"` (L1061-1075, HYPOTHESIS
   bouclier/arc main gauche commentée). `K#####` n'est pas `W` → un
   code K manquant ne produit **ni tenue ni arme**.
5. Item mixte possible : 23126 (f1 `K10230` grp 2 → `B1255` + f2
   `W27001` → arme) → **les deux** chemins s'appliquent (accessoire +
   arme) — vérifié cohérent.

Verdict : **conforme** au format ; priorité f1 non prouvée (H2).

## Maillon 4 — visibilité des tenues + teinture (CharVisual.cs)

- `ApplyOutfitVisibility` (L1285-1309) : suffixe `int ≥ 200` seulement,
  mesh `#X<suf>` + peau `SX<suf>` ; `wanted` vide →
  `BaseEquipIds={"202"}` (L1288-1291) — tenue Guerrier **du corps
  courant** (`#G202` fille, `#B202` garçon…). Suffixes 4 chiffres
  (`#?2100`) couverts par `int.TryParse`. `ApplyLook` (L130-181) =
  même passe pour le sélecteur.
- `ApplyEquipment` (L1147-1183) : `outfits` vide + `classId>0` →
  repli `ClassOutfitRow` (L1156-1161) ; attachements teints par
  `DyeMeshSurfaces(mi, row)` (L1163-1172 — fix masque brut B des
  casques/capes, FIX-B) ; puis `TintMeshes` sur les visibles avec
  `rows` (L1178-1182).
- `DyeMeshSurfaces` (L562-618) : masque `char/texture/<skin>B.dds` +
  artwork `<skin>A.dds` par `row.Skin` ; `skin` vide/`NULL` → mask/art
  null → `Dyed(tex, spec, null)` — composite dye appliqué sur l'albedo
  du nif (couleurs fantaisistes si la texture n'est pas un masque de
  canaux — cf. `X628A81` absent, G14). `spec==0` → artwork tel quel
  (L663-668).

Verdict : **conforme** ; sensible aux textures absentes (pas de repli
« masque manquant » — la spéc dye est appliquée à la texture de base).

## Maillon 5 — os d'attache + résolution GLB (CharVisual.cs:1270-1367)

- `AttachmentBone` (L1272-1277) : `1|4 → "Bip01 Spine1"`, sinon
  `"Bip01 Head"`. Vérifié vs données : grp 1 = dos (ailes/sacs loc 6) →
  Spine1 **correct** ; grp 4 inexistant → branche morte. **grp 2 → Head
  pour TOUTES les pièces externes** : casques OK (`G1203`, `G1109`,
  `B1255`…) mais **plastrons suspects** — `A20101` (item 32193
  « fencer armor », loc 2 torse) → `G1706`/`B1706` au **crâne** ; les
  `.nif` série 1xxx sont catalogués « casque/coiffure attachée »
  (MODELS-CATALOG) mais G1706 est une pièce de torse → placement
  probablement faux (H3). De même les 16 codes grp-0 non-`#`
  (`?2124/2126/2135/2137` corps entier) → Head — faux si atteint
  (orphelins : aucun item ne les cite → latent seulement, H6).
- `AttachGlbToBone` (L1324-1367) : `rel` = `char/model/<mesh>` (tenues)
  ou `item/model/<stem>[#skin]` (armes) ; `TryGetGlb` → GLB converti
  `assets/models/…glb` ou, en mode natif, `.nif` raw
  (AssetCatalog.cs:101-127). GLB absent → **null, muet** → l'attach est
  simplement sauté (les `.nif` manquants de G14 : `M/F/K1213`,
  `M/F/K1208` → l'item est porté sans accessoire, sans log).
- `DyeMeshSurfaces` rejouée sur chaque attach (L1169-1172).

Verdict : conforme pour casques/dos/armes ; **douteux** pour pièces de
torse et corps-entier externes (H3/H6).

## Maillon 6 — qui alimente `worn`/`classId` ?

| Appelant | worn / lookup | classId/style ? | Conséquence si non résolu |
|---|---|---|---|
| Joueur local — `GameScene.cs:512-517` | corps seul (`worn=null`) puis `EquipSlotsPanel.ApplyBodyEquipment` (L233-246) via `PlayerEquipment.VisualWorn` + `LookupModel` | `LocalClass()` oui | tenue portée réelle ; vide → tenue de classe |
| Panel équipement — hooks `PlayerEquipment.Changed`, `ClassChanged`, `ClassesChanged` (EquipSlotsPanel.cs:156-162) | idem | oui | idem |
| **Distant — `RemoteEntity.ApplyWornEquipment`** (EntityRegistry.cs:171-184) | `WornItems`+`CostumeItems` mergés, `LookupEquipItem` | **non** (`classId=0`) | rien de résolu → repli « 202 » Guerrier (cf. audit A) |
| **Sélecteur — `CharSelectScreen.cs:575-587`** | `PreviewWorn` = conteneur 0 **seul** (EdenSession.cs:552-565), `ItemLookup` **c_item sans mall** | non | costumes invisibles ; vide → « 202 » |
| **Création — `CharCreateScreen.cs:371`** | `null` | non | « 202 » |
| **Preview item — `ItemPreviewWindow.cs:388-395`** | `VisualWorn` + item testé (`LocFor` equipType→loc L399-408) | non | idem |
| `CharacterPreview.Show` (L101-125) | param `worn`/`itemLookup` | non | « 202 » |
| `VisTest --appearance-test` (L233-357) | worn synthétique (codes→ids fictifs) | `--skin-classes`/`--skin-style` | harnais existant |
| `CharDressing.Apply` (L36-52) | délégué à `OutfitSuffixes`+`RetintVisible` | signature sans classId | « 202 » |

`CharBodyCache` (clé = blob apparence, L28-30) : `CharDressing.Apply`
agit sur le `Duplicate` → le cache ne court-circuite **pas** le porté.

## Maillon 7 — fusion costume (CostumeVisual.cs)

- `Merge` (L32-47) : pages costume conteneurs 2-4 sur locs **1-5
  seulement** ; page choisie par `kind % 3` de la pièce **portée**
  (repli kind du torse) → `result[loc] = costumeItemId`.
- **Les armes/off-hand (loc ≥ 6) ne sont jamais recouvertes** → un
  « costume arme camouflée » (K3####, K90###) en page costume
  n'atteint jamais le rendu — que le K existe ou non (H4).
- Testé : `tests/Eden.Core.Tests/Plan22Tests.cs:17-46` (fusion locs
  1-5, armes intactes, poids du torse en repli) — seul maillon K avec
  test unitaire ; `LoadEquipMap`/`ResolveEquippedMeshes` non testés.
- `ZoneUpdateEquipmentView.Hidden` (282 — « ids d'items équipés non
  visibles, slots IsCanSee=false », ZoneEquipMessages.cs:59-60/96-100) :
  **parsé mais jamais consommé** → un slot masqué rend quand même (H5).

Verdict : fusion conforme à sa spec ; couverture armes et Hidden =
divergences probables vs natif.

## Maillon 8 — duplications / chemins parallèles

- `src/Eden.Core/Game/CharModelSpec.cs` : copie **identique** de
  `EquipMesh`/`EquipTableName`/`WeaponModelStem`/`WeaponSkinCode`/
  `LoadEquipMap`/`RowForSuffix` (L68-188 ; s'arrête avant les tenues de
  classe). `ResolveEquippedMeshes`/`ApplyEquipment` n'y existent pas
  (dépendance Godot) → la résolution K n'est pas testable hors moteur.
  Risque de drift si un côté bouge.
- `DemoRemoteEquip` (PlayerController.cs:1581-1623) : ids de preuve
  datés — **608 absent de `c_item` EDENFR** (et `K20105` orphelin) →
  lookup null → l'armure du factice ne résout rien (seules les armes
  31900/32034 `W10201`/`W10101` marchent). Le log `[demo] remote
  tenue=#B230` attendu n'arrive plus sous EDENFR.
- `4pequipmap.ini` ignoré (cf. §données) ; `weapon.ini` = autre chaîne
  (W→`item/model`), hors scope.

## Codes manquants → comportement constaté (la question G22)

Code absent de la table du corps → `TryGetValue` échoue sur Code **et**
ModelCode → **skip muet** (aucun log, aucun repli visuel dédié) :

| Cas | Résultat visuel |
|---|---|
| Item seul à son loc (armure) | ne contribue rien ; si aucune autre armure `#` ne résout → **tenue de classe** (`classId>0`, L1156-1161) sinon **« 202 » Guerrier du corps** (L1288-1291) — jamais nu, jamais invisible |
| Item à loc arme (K3#### loc 7/9) | `WeaponModelStem` null (pas `W`) → **mains vides** (aucun GLB attaché) |
| Costume en page (loc 1-5) | remplace l'itemId porté au merge → l'armure réelle disparaît **et** rien ne la remplace → retour à la tenue de base |
| Costume arme en page (loc ≥ 6) | **jamais mergé** (CostumeVisual couvre 1-5) → l'arme réelle reste affichée — le costume n'a strictement aucun effet |
| `lookup` null (id inconnu, ex. 608) | `continue` silencieux (L1029) |
| Mesh `#Xnnn` de la ligne absent du GLB | suffixe voulu jamais matché → ni tenue ni peau affichées → FACE+cheveux+`#X000` seuls (**aspect nu** — DumpLook le signale, VisTest.cs:359-363) ; couverture réelle des méga-GLB non inventoriée |

Ni « perso invisible » ni « nu » par le code : le pire cas = tenue de
base/classe (ou mains vides pour une arme), sauf le cas « mesh `#`
absent du GLB » ci-dessus.

## Hypothèses « mauvais visuels équipement » (triées par probabilité)

1. **G22 sous-estimé : 193 codes manquants, pas 39** (c_itemmall omis
   par l'inventaire — 177 codes, majoritairement costumes boutique).
   Effet client identique (skip muet) mais le gap touche la boutique.
   *Action doc : corriger `EDENFR-GAPS.md` G22 + les notes d'inventaire
   (« aucun K dans c_itemmall » faux, « 37 K3 » → 31, orphelins 1 924 →
   92).*
2. **Priorité f1(Code) vs f2(ModelCode) non prouvée** — 134 items où
   les deux codes existent dans la même table : f1 gagne ici ; si le
   natif lit f2 (champ « modèle/variante »), ces costumes s'affichent
   dans la mauvaise variante (coupe/teinte). *Test :* preview de
   l'item 26449 (« 晚禮服•紫羅蘭 », f1→`#G224` vs f2→`#G217`) ou 34791
   (`G1203` vs `G1126`) — comparer mesh+couleur au natif/ref-shot.
3. **grp 2 → Bip01 Head pour les plastrons** : `A20101`→`G1706` (item
   32193, loc 2 torse) attaché au crâne — correct pour les casques,
   suspect pour les pièces de torse (offset haut + rigidité tête).
   *Test :* `ItemPreviewWindow` sur 32193 ou worn `{2:32193}` en
   VisTest → regarder où le plastron se pose.
4. **Costumes d'arme jamais affichés** : `CostumeVisual` borne aux
   locs 1-5 → les « arme camouflée » en page costume sont inertes
   (indépendant de G22). *Test :* page costume avec item K3/K90 et un
   `--look-log` — `worn` résolu ne doit pas contenir le loc arme.
5. **`Hidden` (282 IsCanSee=false) ignoré** → équipement masqué rendu.
   *Test :* tramer un 282 avec `Hidden=[id]` → l'item reste visible.
6. **grp 0 non-`#` → Head** : 16 codes orphelins corps-entier
   (`K2097x+`, `?2124/2126/2135/2137.nif`) attacheraient un corps
   complet au crâne si un item les citait un jour — latent.
7. **Mesh `#` absent du méga-GLB** (corp par corp) → tenue demandée
   silencieusement non affichée → aspect « nu » (seul cas réel de
   nudité partielle). *Test :* `--appearance-test` + `DumpLook`
   (« suffixes voulus absents du GLB ») sur les 7 corps.
8. **`rows[suffix]` dernier-gagne** sur meshes partagés (K20006-10 →
   `#G218`) : deux items mappant le même mesh → dye de la dernière
   pièce itérée (ordre insertion `worn`). Non spécifié.
9. **Asymétrie d'ordre** : équipement = Code→ModelCode (L1032-1033)
   mais arme = ModelCode→Code (L1054-1055) — cohérent en pratique
   (23126 marche), mais non documenté.
10. **`ItemLookup` du sélecteur sans c_itemmall** (CharSelectScreen
    cs:706-734) → costumes boutique 40xxx non habillés en preview ;
    `PreviewWorn` = conteneur 0 seul de toute façon (costumes absents
    du sélecteur — limite connue de l'audit A).
11. **`n` (champ 4) ignoré** : 3/1/2/0 — s'il borne les canaux dye
    natifs, le composite actuel peut sur-teinter des tenues (à valider
    sur `K60262`/champ texture vide).
12. **Textures/meshes G14 absents** : `X628A81B` → mask null → la spéc
    dye composite sur l'albedo de base (couleurs non natives) ; meshes
    `M/F/K1208/1213` → attach sauté silencieusement.

## Limites de l'audit

- Comportement natif non désassemblé ici : priorité f1/f2, os réel des
  pièces grp 2/0 externes, couverture armes des pages costume, usage du
  champ `n`, sémantique `Hidden` — marqués « non vérifié »/« divergence
  probable ».
- Contenu des méga-GLB (suffixes `#` réellement présents par corps)
  non inventorié — hors périmètre lecture code (harnais `DumpLook`
  existant).
- Recomptages Python sur la sémantique `IsRecordBoundary`/`BuildRows`
  d'`IniTable` (miroir ligne à ligne), pas d'exécution du binaire.
- Les notes `equip-visuals-notes.md` contiennent trois erreurs de
  relevé à corriger : « c_itemmall : aucun K##### » (réel : 2 242
  codes, 177 manquants), « 37 K3#### manquants » (réel : 31 K3 + 8 K9 =
  39 dans c_item seul), « 1 924 orphelins » (réel : 92 avec la jointure
  mall).
