# Audit chaîne « skins de classe » — c_class.ini → 4pequip.ini → mesh+texture

Statut : **verified** (code × données corpus croisés) — 2026-11-03 — lecture
seule, aucune modif, aucun build/test (code en mouvement parallèle).

Vérité terrain : `artifacts/corpus-inventory/class-outfits.tsv` (2 960
(classe,niveau,code) × 7 corps = 20 720 lignes), régénéré par
`tools/corpus-inventory/class_outfits.py`. Chaîne native attendue :
`c_class.ini` (paliers `lvl code1 code2 0`, code1 = série H → item groupe 2
tête, code2 = tenue → item groupe 0 mesh `#X2nn` + skin `XnnnASnn`) →
`4pequip.ini [TableCorps]` (code → mesh,skin,spec dye) →
`char/model/<mesh>.nif` ou mesh `#X2nn` du méga-GLB corps +
`char/texture/<skin>{A,B,C}.dds`.

Vérifié sur les fichiers réels `corpus/fr-full/data/db/` :

- `c_class.ini` : `|V.6|65|`, **686 lignes physiques pour 305 records** —
  les records contiennent des `\n` internes (champ condition, ex.
  `◎角色等級 1 級`). Tous les records reconstruits = 66 cellules, paliers
  en cellules ~55-63 (`'1 A01101 A01201 0'` … `'80 A01109 A01209 0'`),
  niveaux croissants partout, 1 480 paliers au total.
- `4pequip.ini` : `[INFO]` + 7 sections `[TableGirl|Boy|Mouse|Frog|Kuma|
  Loli|Shota]`, lignes `DBn=code,grp,mesh,skin,3,dyeHex,0`. Ex.
  `DB0=A01201,0,#G202,G202AS01,3,5E6F312629B1,0` ;
  `DB108=A01101,2,G1101,G1101H04,3,…,0`. **0 code dupliqué par table**,
  tous les meshes `#` à suffixe numérique, 357 entrées `NULL`.
- Couverture code2 → `#` : **0 manque sur les 1 480 paliers × 7 tables**.
  code1 → groupe 2 (tête), mesh non-`#` externe, pour tous les paliers
  sondés (TableGirl : 100 % grp 2).
- Le même code `A01209` peut partager le mesh `#B202` avec `A01201` mais
  porter un skin différent (`B202AS09` vs `B202AS01`) — « code ≠ skin ».

## Maillon 1 — `ClassCloset()` (CharModelSpec.cs:234-261)

| | Attendu | Constaté |
|---|---|---|
| Source | `data/db/c_class.ini` | `IniTable.ParseFile` via `TextResolver.ResolveFile ?? StoreTable.ResolveDefault` (L240) |
| Palier | `lvl code1 code2 0` | motif `w.Length==4 && int w0 && w1 = lettre+chiffre (≥5) && w3=="0"` (L250-255) — les 2 codes capturés |
| Niveaux | croissants | ordre du fichier conservé (vérifié croissant pour les 305 classes) |
| Codes | code1=tête, code2=tenue | tuple `(Level, Male=w[1], Female=w[2])` — **noms trompeurs** : w[1]=code coiffe, w[2]=code tenue, pas homme/femme (L254) |

Simulation exacte de `BuildRows` sur le fichier réel : 305 rows, 0 <66
cellules, 1 480 paliers parsés sous la sémantique C# `Split(' ')`, 0
cellule palier contaminée par `\r\n`, 0 palier à `w[3] != "0"`. Le bug
multi-lignes trouvé cette nuit est couvert : `IsRecordBoundary`
(IniTable.cs:177-181) ne déclenche un flush prématuré que si la ligne de
continuation commence par `chiffres|` — **aucune** dans le fichier
(toutes par `◎`).

Verdict : **conforme**.

## Maillon 2 — `ClassOutfitRow()` (CharModelSpec.cs:265-274)

- `list[Math.Clamp(style, 0, count-1)]` — `style` = index de garde-robe
  (cohérent avec `_outfitSel` ClassWindow.cs:799/890 et `Entry.Style` u8
  du 489 — ZoneClassMessages.cs:30/66). Ce n'est **pas** une sélection
  par niveau : un `style` non borné écrête au dernier palier.
- `map.TryGetValue(e.Female)` **d'abord** (L271), exige
  `Mesh.StartsWith('#')` → retient le code tenue (code2) — correct.
- `map.TryGetValue(e.Male)` ensuite (L272) — **branche morte** : code1
  (série H) est toujours groupe 2 mesh non-`#` → jamais retenu →
  **l'item coiffe de classe (ex. `char/model/G1101` + `G1101H04`) n'est
  jamais affiché**. Le pipeline d'attache tête existe
  (`AttachmentBone` → « Bip01 Head », CharVisual.cs:1272-1277 +
  `AttachGlbToBone` L1324) mais n'est jamais atteint par la tenue de
  classe.

Verdict : tenue corps **conforme** ; coiffe **divergence probable**
(le natif habille les deux codes — sinon pourquoi 2 codes par palier).

## Maillon 3 — `LoadEquipMap()` / 4pequip (CharModelSpec.cs:154-213)

- Parse manuel des sections `[TableX]` (L179-186), `EquipTableName`
  couvre les **7 corps** b/g/m/f/k/l/c → TableBoy/Girl/Mouse/Frog/Kuma/
  Loli/Shota (L81-89), défaut TableGirl. Conforme aux 7 sections réelles.
- `DBn=code,grp,mesh,skin,…,dyeHex,0` → `EquipMesh(mesh,skin,grp,dye)`
  (L190-201), dye `parts[5]` en hex (L195-199). Clé = code, comparaison
  OrdinalIgnoreCase ; aucun doublon de code par table (vérifié).
- `RowForSuffix` (L219-227) : **première** ligne dont `Mesh == "#X<suf>"`
  — pour un mesh partagé par plusieurs codes (ex. `#B202` = A01201,
  A01202, A01205, A01209 avec skins AS01/02/05/09), retourne la première
  en ordre d'insertion = la plus petite DBn → **skin variante AS01**.

Verdict : **conforme** pour l'accès par code ; `RowForSuffix` est un
repli à variante fixe (voir H5).

## Maillon 4 — résolution mesh + peau S (CharVisual.cs)

- `OutfitSuffixes` (L1087-1103) : `ResolveEquippedMeshes` (L1014-1080)
  pour le porté ; `res.Count==0 && classId>0` → suffixe `r.Mesh[2..]`
  (L1097-1099). `OutfitRows` (L1109-1123) idem pour les lignes dye.
- `ApplyLook` (L130-181) : visible = FACE/HAIR + cheveux `#X1yy` le plus
  proche + `#X<suf>`/`SX<suf>` voulus + corps nu `#X000` ; repli
  `BaseEquipIds = {"202"}` (L113) si ensemble vide — **« 202 » = tenue
  Guerrier A01201, pas « débutant universel »** : chaque classe a sa
  propre tenue (`#G204` classe 4, `#G211` classe 10, `#G216` classe 115…).
- `ApplyOutfitVisibility` (L1285-1309) : suffixe `int ≥ 200` seulement —
  toutes les tenues classe sont 2xx (vérifié).
- Peau `SX<suf>` exposée : même suffixe que le mesh `#X<suf>` → paire
  couverte par `wanted` (L176, L1303).

Verdict : **conforme**.

## Maillon 5 — texture A/B/C + teinture (CharVisual.cs)

- `DyeMeshSurfaces` (L562-618) : masque `char/texture/<skin>B.dds` +
  artwork `<skin>A.dds` chargés par `row.Skin` (L566-571) ; `C`
  ignoré — cohérent avec le composite additif `Σ Bᵢ·dyeᵢ + A·α`
  (`Dyed` L649-754, régression mesurée 0,0135 sur `G202AS01C`). Spec dye
  48 bits de la ligne (DecodeDye L528-554). Ligne par `rows[suffix]` puis
  repli `RowForSuffix` (L432-435).
- `TintMeshes` (L360-445) : `S*` → teinte peau, `#X000` → teinte peau,
  `#X1xx` → teinte cheveux (floor -2 = gain ×2), `#X2xx` → dye 4pequip ;
  filtre `mi.Visible` (L372-375) puis `IsTintProduct` anti-double-teinte.
- Peau/cheveux/visage : `TintFor` → `CharColorTable` (L348-352),
  `ApplyFaceVariant` (L268-326) pose `<L>FACES<style>` A/B composite
  (skin + iris), `WeldFaceNormals` jointure.
- Résolution casse insensible (`AssetCatalog`/`EdenVfs`) — nécessaire :
  le corpus mélange `G202AS01C.DDS` / `m1101h04a.dds`. Fichiers A/B des
  skins classe vérifiés présents (`g202as09b.dds`, `g1166h01b.dds`…).

Verdict : **conforme** quand la bonne `EquipMesh` arrive ; sensible à
`rows` incomplet (repli `RowForSuffix` → variante AS01).

## Maillon 6 — appelants : qui passe `classId`/`style` ?

| Appelant | classId/style ? | Conséquence si non résolu |
|---|---|---|
| Joueur local — `GameScene.cs:512-517` | `LocalClass()` = `PlayerVitals.ActiveClass ?? Character.ClassId` + `Entry.Style` du 489 (CharVisual.cs:1126-1134) | tenue de classe OK ; `style=0` si 489 absent → palier niveau 1 |
| Équipement local — `EquipSlotsPanel.cs:241-245` | oui (lcls/lsty) | idem |
| Garde-robe HUD — `ClassWindow.cs:872-900` | `ClassCloset(id)` + `_outfitSel` ; `ZoneServerClosetSet` (L812) | conforme |
| **Sélecteur perso — `CharSelectScreen.cs:575-587`** | **non** : `CharBodyCache.GetOrBuild(app)` + `CharDressing.Apply(body,app,PreviewWorn,ItemLookup)` — `c.ClassId` pourtant dispo (L469/478) | `PreviewWorn` ne résout rien de `#` → **repli « 202 » Guerrier pour toute classe** ; `PreviewWorn` = conteneur 0 seul (EdenSession.cs:558-562) → **costumes (conteneurs 2-4) invisibles au sélecteur** |
| **Création — `CharCreateScreen.cs:355-372`** | **non** : `CharDressing.Apply(body, app, null, null)` alors que le sélecteur de classe existe (StartClasses L49, `_class` L105-109) | **toujours « 202 » quelle que soit la classe choisie** ; et `CharBodyCache` clé = blob apparence seul (CharBodyCache.cs:29-30, 88) → même en passant classId, le proto caché ignorerait la classe |
| **Joueurs distants — `EntityRegistry.cs:1386-1407` + `ApplyWornEquipment` L171-184** | **non** : `m.ClassId` du 263 **est parsé** (CombatMessages.cs:171,212) mais jamais stocké/passé | distant sans armure `#` résolue → « 202 » Guerrier |
| Préviews — `ItemPreviewWindow.cs:321/391`, `CharacterPreview.cs:109` | non | repli « 202 » |
| `CharDressing.cs:44-50` (helper sélecteur) | signature sans classId | repli « 202 » |
| `VisTest.cs:318-329` (diag) | oui : `--skin-classes[=base]`, `--skin-style=N` | harnais de test existant |

## Maillon 7 — parseur IniTable multi-lignes

`IniTable.BuildRows` (IniTable.cs:199-232) + `IsRecordBoundary`
(L177-181) + accumulation jusqu'à `cols+1` cellules : gère le format
c_class (vérifié par simulation : 305/305 records complets). Tests
existants : `Parse_PipeFormat_MultiLine_StillJoined`,
`Parse_PipeFormat_ShortRows_NotSwallowed` (tools/NetTests/IniTableTests.cs
:141-172 ; doublons tests/Eden.Core.Tests/). Bord restant : une
continuation démarrant par `chiffres|` casserait le record — absent de
c_class.ini (non vérifié ailleurs).

Verdict : **conforme**.

## Maillon 8 — duplications / chemins parallèles

- `src/Eden.Core/Game/CharModelSpec.cs` : copie **sans** `ClassCloset`/
  `ClassOutfitRow` (s'arrête L189). Si Eden.Core sert aux tests purs, la
  garde-robe n'y est pas testable — **divergence probable** (drift).
- `ClassTable.cs:126-139` : second parse des mêmes cellules paliers,
  positionnel (`spellBase+25..34`) et nomme les codes `CertCode1/2` —
  même donnée, deux interprétations (certificat vs tenue ; le code A est
  matériellement un item portable ET le « certificat » du palier).
  Cohérent sur ce fichier ; risque de drift si le layout bouge.
- `c_class.ini.inib`/`4pequip.ini.inib` : absents du corpus → le chemin
  `.ini` est celui lu (pas de risque de baked obsolète aujourd'hui).

## Hypothèses « mauvais skins » (triées par probabilité)

1. **Appelants sans `classId` → repli « 202 » Guerrier.** Sélecteur
   (perso sans armure `#` résolue → tenue guerrier au lieu de sa classe),
   création (**toujours** guerrier quel que soit le choix de classe +
   cache proto ignorant la classe), distants (ClassId du 263 parsé mais
   jeté). *Test qui tranche :* `--look-log` au lancement — la ligne
   `[outfit] letter=X class=C style=S → … (closet=N)` montre si classId
   arrive ; comparer une capture sélecteur d'un mage non équipé à son
   `closet[0]` attendu (`#G205`).
2. **`style=0` faute de 489** (`ClassChangeDialog.Classes` vide avant le
   489, ou `--solo` sans serveur) → palier index 0 (niveau 1) affiché au
   lieu du style choisi → perso haut niveau en tenue débutant. *Test :*
   même log `[look]`/`[outfit]` sur un perso dont `Entry.Style>0` ;
   vérifier la tenue vs `closet[style]`.
3. **Coiffe de classe jamais affichée** : `ClassOutfitRow` filtre
   `StartsWith('#')` → code1 (grp 2) ignoré → tenue de classe incomplète
   vs natif. *Test :* dans `VisTest` attacher `char/model/<X11nn>` à
   « Bip01 Head » et comparer à une capture native `ref-shots/`.
4. **`Entry.Style` ≠ index garde-robe** : le u8 « style/carte » du 489
   (ZoneClassMessages.cs:30) — si le serveur y met autre chose qu'un
   index 0-8, `list[Clamp(style)]` choisit un mauvais palier. *Test :*
   dump du 489 pour un style serveur connu vs `closet[style]` affiché
   (log `[outfit]`).
5. **`RowForSuffix` = variante AS01** : un mesh `#X2nn` montré sans sa
   ligne dans `rows` (ex. distant `style>0` non transmis, ou re-teinte
   `RetintVisible` sans `rows`) → teinté avec la 1re variante
   (`AS01`) au lieu de la sienne (`AS09`…). *Test :* `VisTest
   --skin-classes --skin-style=8` + log `[dye] <mesh> row=… skin=…` —
   `skin=AS01` au lieu de `AS09` confirme.
6. **Costumes invisibles au sélecteur** : `PreviewWorn` = conteneur 0
   seulement (EdenSession.cs:558-562) → joueur en costume montré en
   armure. *Test :* perso avec page costume active — tenue sélecteur vs
   en jeu (`VisualWorn` fusionne 2-4, ZoneEquipMessages.cs:249-251).
7. **Meshes `#X2nn` absents du méga-GLB** pour certaines races (M/F/K
   moins pourvues) → `wanted` ne matche rien → corps nu plutôt que
   mauvaise tenue. *Test :* `DumpLook` liste déjà les « suffixes voulus
   absents du GLB » (VisTest.cs:359-363) ou `--mesh-dump`.
8. **Tenue de classe ajoutée malgré des attachs** : si le porté ne
   résout que des non-`#` (cape/casque), `outfits` reste vide → tenue de
   classe ajoutée *sous* les attachs — conforme ou non selon le natif
   (non vérifié). *Test :* VisTest avec un item cape seul + log
   `[outfit]`.

## Limites de l'audit

- Comportement natif non désassemblé ici : port de la coiffe code1,
  sémantique exacte de `Entry.Style`, tenue distante sous costume —
  marqués « non vérifié »/« divergence probable ».
- Contenu des méga-GLB (noms de meshes réellement présents par corps)
  non inventorié — hors périmètre lecture code.
- Simulation du parseur faite en Python (miroir ligne à ligne de
  `BuildRows`), pas d'exécution du binaire (contrainte : pas de build).
