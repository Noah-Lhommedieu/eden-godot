# Lot S6+S8 — races non-humaines, changement de classe, craft (works)

## Preuves natives rassemblées

### Corps/race (world/CreateCharacterIntoDB @0x080a2170 + rf_game FUN_0073ddc0)
- Le nibble haut d'app[28] = **code corps** = `gender_id` DB ; le serveur
  n'accepte que les codes {0,1,2,4,6,8,9} (masque 0x317 + bit6 ; 3/5/7 rejetés).
- Lettres KFM : 0→g 1→b 2→m 4→f 6→k 8→l 9→c ; ordre des sections
  `4pequip.ini` [TableGirl,Boy,Mouse,Frog,Kuma,Loli,Shota] →
  g=humaine♀ b=humain♂ m=Zumi f=Anuran k=Ursun l/c=Halfkin ♀/♂.
- Page de classe native = (code>>1)×100 + classId ; le byte `race`
  wire = index de page +1 (1..5) — HYPOTHESIS confirmée par
  l'ordre des races (Human/Zumi/Anuran/Ursun/Halfkin).
- born_area native par code : 2→19, 4→28, 6→16, sinon→2 — **décidé
  serveur** (le C# hardcode node 2 ; écart documenté, rien côté client).
- Bornes cosmétiques natives : codes {0,1,2,4,8,9} → hair<11, face<5,
  hairColor<15, skin<10, eyes<15 ; code 6 → hair<8, face<2 (1 si
  skin==10), hairColor<10, skin<11.
- `data/db/C_Races.ini` ne contient **qu'1 race** (id 1 人/humain,
  clients projet ET Reforged identiques) → l'UI native n'offre que
  l'humain ; notre extension 5 races est une déviation assumée, wire
  côté serveur accepté.

### Classes (zone, capture 17:22 + désasm)
- C→S 166 QueryClassList (vide, déjà dans le burst) → S→C 489 GOLDEN :
  `[u8 ff][u8 0][u16 n]` puis par entrée `[u8 classId][u8 level]
  [u8 style][i32 classExp][i32 classPoint][u8 aaPoint][7×u16 lic]`.
- C→S 168 ChangeClass `[u8 classId]` (désasm Deserialize 1B) → burst
  280/296/492/493/488/489/attr 0x2c/283/509/286/316/510.
- S→C 510 ChangeClassShortcut GOLDEN : `[u8 nBars]{[u16 mask][i32×bits]}
  [u16 nExtras][i32×n]`.

### Craft / ordres de travail (zone désasm + serveur)
- NPC service 12-17 → S→C 496 EnterWorks `[i32 npcUid][s16 workType]
  [s16 bldId][s16 ?][u16 0]` ; le serveur mémorise l'op works.
- C→S 170 PlayerWork `[u32 workId][s16 catBag][s16 catSlot][s16 boostBag]
  [s16 boostSlot]` — catalyseur requis (kind par type) ; boost -1,-1.
- C→S 171 WorkResultOK `[s16 workNum]` ; C→S 172 PlayerWorkCheckData
  (vide, déjà envoyé au burst).
- S→C 497 `[u16 n]{s16 num,s16 type,i32 workId,i32 eventId,str fmName}` ;
  498 `[i32 workId (<0 = échec)][s16 type][s16 num][str fmName]
  [u16 nVec]{u16 13, i32×13 produit}` ; 500 `[s16 workNum]`.
- Recettes client : `C_FamilyCollect.ini` (|V.2|47, même layout que
  S_ : col0 id, col2 catalyseur, col3 nom, col4 qté cat, col8 exp,
  col9 instant, col10 durée(min), col13 or, col14+ produits
  {id,nom,qté,taux}) ; `c_familycombine.ini` (|V.1|44, mêmes offsets
  que S_ : col0 id, col1 catégorie, col9 or, col10 produit, col11 nom,
  col12 qté, col13 succès, col14 malédiction, col15+ mats {id,nom,qté}).
- HYPOTHESIS filtre : collect col1 ∈{0,1,2} ↔ workType-12 ;
  combine col1 ↔ workType-12 (15→3,16→4,17→5 ; catégories 6/7/20 =
  non triées, affichées dans « autre »).
- `c_collectionpoint.ini` : fichier **vide** (header seul) → rien à
  faire (note tâche 4).

## Implémentation

- `scripts/protocol/ZoneClassMessages.cs` — ZonePlayerClassList (489),
  ZoneChangeClass (168 C→S), ZoneChangeClassShortcut (510 parse).
- `scripts/protocol/ZoneCraftMessages.cs` — ZoneEnterWorks (496),
  ZonePlayerWorkUpdate (497), ZonePlayerWorkResult (498),
  ZonePlayerWorkResultEnd (500), ZoneStrengthenCasting (433),
  ZonePlayerWork (170 C→S), ZonePlayerWorkResultOK (171 C→S).
- `scripts/data/tables/RaceTable.cs` — + `LoadRaces` (C_Races.ini) +
  spec statique corps/bornes par race.
- `scripts/data/tables/ClassRankTable.cs` — C_ClassRankBuff.ini
  (id=classId, 20 paliers × 6 champs depuis col2 — miroir serveur).
- `scripts/data/tables/CraftTable.cs` — C_FamilyCollect + c_familycombine.
- `scripts/screens/CharCreateScreen.cs` — map race→bodyCode prouvée,
  genre désactivé pour les corps uniques (Zumi/Anuran/Ursun), bornes
  des cyclers par code, races nommées via RaceTable.
- `scripts/screens/CharSelectScreen.cs` — suffixe race dans le libellé
  du slot (JobLvText) quand race ≠ humaine.
- `scripts/hud/CraftWindow.cs` — Control auto-monté, lazy-bind
  `SessionCarry.Session` : 496→ouvre la fenêtre (liste recettes,
  catalyseur auto depuis le 312 caché, bouton Fabriquer→170) ;
  497/498→ordres en cours/terminés (Réclamer→171) ; 500→clear.
- `scripts/hud/ClassChangeDialog.cs` — Control auto-monté : 489→cache
  la liste ; hotkey P → dialog choix classe → 168.
- `scenes/game.tscn` — + CanvasLayer « ZoneWindows » avec les deux
  Controls (additif ; HudRoot/GameScene.cs intouchés).
- `tools/NetTests/ZoneLotS6S8Tests.cs` — goldens codecs + tables.

## Validation
- `dotnet build godot_project` 0/0 ; `dotnet test tools/NetTests` vert.
- Capture `--screen=create --shot=` avec races.
- Live test123 : créer perso Zumi (body code 2) si le serveur accepte.
