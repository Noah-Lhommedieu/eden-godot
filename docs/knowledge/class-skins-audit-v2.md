# Audit v2 chaîne « skins de classe » — re-vérification post-edits

Statut : **verified** (relecture du code courant × données corpus) —
2026-11-03 — lecture seule, aucune modif, aucun build/test.

Diff depuis `class-skins-audit.md` (v1, même jour, matin) :
`CharVisual.cs` (05:35) et `CharModelSpec.cs` (04:42) ont été modifiés
**entre** l'audit v1 et cette relecture — tous les numéros de ligne ci-
dessous sont ceux de l'état courant. Vérité terrain identique :
`artifacts/corpus-inventory/class-outfits.tsv` (20 720 lignes).

## Ce qui a changé dans le code depuis v1

- `BuildBody`/`OutfitSuffixes`/`OutfitRows`/`ApplyEquipment` acceptent
  désormais `int classId = 0, int style = 0`
  (CharVisual.cs:34, 1090, 1113, 1150).
- Nouveau chemin « tenue de classe » : quand aucune armure `#` n'est
  résolue, `CharModelSpec.ClassOutfitRow(letter, classId, style)`
  (CharModelSpec.cs:265-274) fournit suffixe **et** ligne 4pequip —
  remplace partiellement le repli « 202 » (commentaire explicite
  CharVisual.cs:1095-1096).
- Nouveau fournisseur `LocalClass()` (CharVisual.cs:1126-1134) :
  `cls = PlayerVitals.ActiveClass (280 attr 0x2c) ?? SessionCarry
  .Character?.ClassId` ; `style = e.Style` de l'entrée
  `ClassChangeDialog.Classes` dont `e.ClassId == cls` (dernier 489).
- Appelants mis à jour : `GameScene.cs:512-513` (BuildBody du joueur),
  `EquipSlotsPanel.cs:242-245` (ApplyBodyEquipment + re-application
  sur `PlayerVitals.ClassChanged` et `ClassChangeDialog.ClassesChanged`
  — EquipSlotsPanel.cs:158-159), `ClassWindow.cs:900` (preview
  garde-robe, `classId: id` **sélectionnée** + `_outfitSel`),
  `VisTest.cs:325` (`--skin-classes`, `--skin-style`).
- Teintes : `floor = -2f` étendu aux peaux `S*` et au corps nu
  `#X000`, pas seulement aux cheveux (CharVisual.cs:391-417).

## H1 — `RowForSuffix` = 1ʳᵉ variante d'un mesh partagé

**Toujours ouvert** — code inchangé (CharModelSpec.cs:219-227) : retourne
la première ligne `DBn` dont `Mesh == "#X<suf>"`. Données : **133 des
147** couples (corps, mesh `#`) des tenues de classe portent plusieurs
textures (TSV) ; ex. `#G202` couvre A01201..A01209 → skins
`G202AS01`..`G202AS09`, `#G203` idem pour le Magicien. La première ligne
est le palier 1 (`AS01`).

**Mais : nouveau contour principal sain.** `ClassOutfitRow` n'utilise
PAS `RowForSuffix` : il lit `map.TryGetValue(code)` (CharModelSpec.cs:
271-272) → la ligne du **code exact** (A01209 → `B202AS09`, la bonne
variante). `OutfitRows` (CharVisual.cs:1119-1121) et `ApplyEquipment`
(L1156-1161) insèrent cette ligne dans `rows` clé par `r.Mesh[2..]` ;
`TintMeshes` consulte `rows[suffix]` **avant** le repli `RowForSuffix`
(CharVisual.cs:432-435). Un tenue de classe affichée via ces chemins
reçoit donc sa vraie texture, pas AS01.

Surface résiduelle du repli `RowForSuffix` (CharVisual.cs:434) :
- le repli `BaseEquipIds = {"202"}` (CharVisual.cs:113) — mais là
  mesh `#X202` **et** skin `AS01` sont cohérents (la 1ʳᵉ ligne `#X202`
  EST la tenue débutant A01201) ;
- un `RetintVisible(body, rows: null)` — aucun appelant nul aujourd'hui
  (`CharDressing.cs:49` passe `OutfitRows`) ;
- un appelant qui afficherait un suffixe de classe sans peupler `rows`
  avec classId — aucun dans l'état courant (voir H2 : ceux qui n'ont
  pas classId affichent « 202 », pas la tenue de classe).

Fix résiduel : aucun nécessaire pour l'exactitude aujourd'hui ; le repli
reste un piège si un futur appelant montre `#X2nn` sans `rows` —
supprimer/`obsolete` `RowForSuffix` ou l'assortir d'un avertissement
log serait le durcissement minimal. NB : la copie
`src/Eden.Core/Game/CharModelSpec.cs:180-188` contient la même fonction
(drift inchangé, voir § duplications).

## H2 — Repli « 202 » quand classId jeté

**Modifié — partiellement corrigé.** Le nouveau chemin exact :

| Étape | Fichier:ligne | État |
|---|---|---|
| `res.Count==0 && classId>0 → ClassOutfitRow → res.Add(r.Mesh[2..])` | CharVisual.cs:1097-1099 | conforme |
| idem pour `rows` (texture+dye) | CharVisual.cs:1119-1121 | conforme |
| idem dans `ApplyEquipment` (jeu) | CharVisual.cs:1156-1161 | conforme |
| `cls` = `PlayerVitals.ActiveClass` (280 attr 0x2c, ZoneVitalsMessages.cs:202,274-276) sinon `SessionCarry.Character?.ClassId` (ScreenManager.cs:16, record EdenSession.cs:63-66) | CharVisual.cs:1128-1129 | conforme — en live cls est quasi toujours >0 (la liste persos du World fournit le repli) |
| `style` = `e.Style` de `ClassChangeDialog.Classes` (489, ClassChangeDialog.cs:35-38,77-89) filtré `e.ClassId == cls` | CharVisual.cs:1130-1133 | conforme quand le 489 est reçu ; 0 sinon |

**Qui fournit classId maintenant :** joueur local (GameScene.cs:512-513,
EquipSlotsPanel.cs:242-245), fenêtre Classe (ClassWindow.cs:900),
VisTest.

**Qui le jette encore :**
- **Joueurs distants** — `EntityRegistry.cs` ne contient **aucune**
  occurrence de `ClassId`/`classId`/`ActiveClass` : `m.ClassId` du 263
  est toujours parsé (CombatMessages.cs:171,212) mais jamais lu.
  `HandleDisplayChar` (EntityRegistry.cs:1382-1412) → `BuildFromAppearance`
  (CharVisual.cs:89-90 → `BuildBody(app)` sans classId) ;
  `ApplyWornEquipment` (L171-184) → `ApplyEquipment(visual, ModelStem,
  shown, LookupEquipItem)` sans classId/style → distant sans armure `#`
  résolue = « 202 ». De plus le 263 ne porte **aucun** champ de style
  garde-robe (vérifié : zéro « style/closet » dans
  `eden_server/.../ClientDisplayCharCodec.cs`) → même corrigé, un
  distant serait bloqué au palier index 0 sauf si le serveur place la
  tenue de classe dans son set0 équipé (non vérifié côté serveur).
- **Sélecteur perso** — `CharSelectScreen.cs:575-587` :
  `c.ClassId` est disponible (CharacterSummary, EdenSession.cs:64 ;
  utilisé pour l'icône L469/478) mais `CharDressing.Apply`
  (CharDressing.cs:36-38) n'a pas de paramètre classId →
  `OutfitSuffixes(letter, worn, lookup)` avec classId=0 → perso sans
  armure `#` = « 202 » au lieu de sa tenue de classe. Le proto caché
  (`CharBodyCache.cs:88`, clé = blob apparence seul L29-30) devrait
  aussi entrer dans la clé si classId/style y entrait.
- **Création** — `CharCreateScreen.cs:364-371` : sélecteur de classe
  toujours présent (`_class` L105-109, `StartClasses={1,2,3,4,5}` L49,
  envoyé au serveur L467/499) mais non passé → « 202 » quelle que soit
  la classe choisie.
- **Previews** — `ItemPreviewWindow.cs:321,391`, `CharacterPreview.cs:
  109,117` (via CharDressing) : pas de classId → « 202 ».
- **Diags** — `SkinTest.cs:55,164`, `AnimMatrix.cs:115`, `VisTest.cs:
  491` : `BuildBody(app)` nu — acceptable (harnais).

Fix résiduel : (a) stocker `m.ClassId` sur `RemoteEntity` et le passer
aux deux appels (style resterait 0 — limite protocole ci-dessus) ;
(b) ajouter `classId`/`style` à `CharDressing.Apply` et les passer
depuis `c.ClassId` (sélecteur — avec `c.ClassId` dans la clé
`CharBodyCache`) et depuis `StartClasses[_class.Selected]` (création) ;
(c) previews : passer `LocalClass()` si on veut la tenue de classe
sous l'item testé.

## H3 — Branche code1 (`e.Male`, coiffe) morte

**Toujours ouvert.** `ClassOutfitRow` (CharModelSpec.cs:271-272)
inchangé : `e.Female` (w[2] = code2 tenue) exige `StartsWith('#')` puis
`e.Male` (w[1] = code1 coiffe) idem. Vérifié exhaustif sur le TSV :
**100 %** des codes `A0x1xx` (code1) mappent un mesh **non-`#`** externe
(`G1101`, `G1102`, `G1130`…) dans les 7 tables × 1 480 paliers — la
branche est prouvée inatteignable, pas seulement échantillonnée.
Conséquence identique : la coiffe de classe (`char/model/<X11nn>` +
texture `<X11nn>Hnn`) n'est jamais résolue ni attachée ; le pipeline
d'attache existe (`AttachmentBone` groupe 2 → « Bip01 Head »,
CharVisual.cs:1272-1277 + `AttachGlbToBone` L1324-1367) mais ne sert que
les items **portés** (ResolveEquippedMeshes L1047-1050). Noms de tuple
`(Male, Female)` toujours trompeurs (CharModelSpec.cs:230,234,254) :
w[1]=coiffe, w[2]=tenue.

Fix résiduel : si le natif affiche la coiffe de palier (probable — 2
codes par palier sinon inutile), ajouter à `ClassOutfitRow` (ou un
`ClassHeadRow` parallèle) la résolution du mesh externe code1 +
l'attacher à « Bip01 Head » dans les 3 chemins (OutfitSuffixes n'a pas
de canal attache — il faudrait le passer par `ApplyEquipment`/
`BuildBody`, pas seulement par suffixe).

## H4 — `floor = -2f`

**Modifié (élargi).** État courant CharVisual.cs:391-417 :

| Catégorie | Lignes | tint | floor |
|---|---|---|---|
| `S<Lettre>*` (peaux) | 392-396 | peau | **-2f** (nouveau vs v1) |
| `#<L>000` (corps nu) | 399-404 | peau | **-2f** (nouveau) |
| `#<L>1xx` (cheveux) | 405-417 | cheveux | -2f (inchangé) |

Sémantique `Tinted` (CharVisual.cs:805-807) : `floor < 0` = mode
« gain direct » `k = |floor|·(r/255)` — **sans normalisation par le
max** ni plancher positif. V1 ne mentionnait le -2 que pour les
cheveux ; l'extension aux peaux/corps nu retire la normalisation
`r/rMax` qu'elles avaient (masque R plafonnant ~0,5 → gain ×2 =
équivalent approximatif, à réponse en bas de gamme différente).
Réglage utilisateur — à considérer comme état voulu, pas bug ; à
revalider visuellement sur les peaux `S*` (une carnation
`sur-normalisée` avant, ×2 direct maintenant, peut saturer sur les
zones de masque fortes).

## H5 — `style=0` si le 489 absent

**Toujours ouvert, mitigé.** Le style arrive uniquement via
`e.Style` des entrées du 489 (ZoneClassMessages.cs:30,66) stockées dans
`ClassChangeDialog._classes` (statique, remplacé à chaque 489 —
ClassChangeDialog.cs:35,77-89) ; `LocalClass` filtre par
`e.ClassId == cls` (CharVisual.cs:1131-1132). Cas :

- **Live normal** : le corps est construit à l'entrée de scène
  (GameScene.cs:512) — si le 489 n'est pas encore là, style=0 au
  build ; **auto-réparation** par `ClassesChanged → ApplyBodyEquipment`
  (EquipSlotsPanel.cs:159) dès réception, à condition que le panneau
  existe (HUD monté).
- **`--solo` / `--nohud` / pas de serveur** : `ActiveClass=-1`,
  `Character=null` → `cls=0` → « 202 » ; aucune réparation (inchangé,
  cohérent pour du hors-ligne).
- **Changement de classe en jeu** : 280 attr 0x2c → `ClassChanged` →
  `ApplyBodyEquipment` (EquipSlotsPanel.cs:158) → nouvelle tenue —
  couvert.
- **Nouveau bord (introduit par LocalClass)** : `_classes` est
  **statique** et survit au changement de personnage — au login d'un
  second perso, `LocalClass` lit le 489 du perso **précédent** tant que
  le nouveau n'est pas arrivé (cls vient alors de
  `SessionCarry.Character`) → style d'un autre perso appliqué quelques
  frames ; réparé par `ClassesChanged` si HUD présent.
- Sémantique native de `Entry.Style` (u8 « style/carte ») toujours non
  désassemblée — `list[Clamp(style)]` suppose un index de garde-robe
  (cohérent avec `_outfitSel`/`ZoneServerClosetSet` 167,
  ClassWindow.cs:812) ; hypothèse v1 n°4 non tranchée.

## Divergences nouvelles / confirmations

1. `ClassOutfitRow` = accès par **code** → variante correcte (pas de
   biais RowForSuffix). Confirmé données : A01209→`#B202`+`B202AS09`,
   A02209→`#G203`+`G203AS09`.
2. `rows[r.Mesh[2..]] = r` (CharVisual.cs:1121,1160) n'est écrit que
   quand `rows` est vide → pas de collision porté-vs-classe.
3. `OutfitSuffixes`+`OutfitRows` (BuildBody L49,69) et le log
   `[outfit]` (L1100-1101) exécutent `ResolveEquippedMeshes`/
   `ClassCloset` jusqu'à 3× par corps — coût seulement.
4. `ClassWindow.cs:900` passe `classId: id` = classe **sélectionnée**
   dans la liste (pas forcément la classe active ni possédée) +
   `_outfitSel` borné par `closet.Count` (L873) — preview hors
   condition de niveau (`closet[i].Level <= lvl` ne grise que les
   boutons) : conforme pour un aperçu, à recouper avec le natif.
5. Distants : la fusion costumes `CostumeVisual.Merge` s'applique à
   `ApplyWornEquipment` (EntityRegistry.cs:176-181) — un distant en
   costume affiche sa **costume tenue** correctement ; le trou reste
   = tenue de classe quand rien d'équipé.
6. `src/Eden.Core/Game/CharModelSpec.cs` : toujours sans
   `ClassCloset`/`ClassOutfitRow` (s'arrête à `RowForSuffix` L180) —
   garde-robe non testable hors moteur, drift inchangé.
7. `PreviewWorn` = conteneur 0 seul (EdenSession.cs:557-564) →
   costumes invisibles au sélecteur — inchangé (v1 H6).

## Hypothèses « mauvais skins » — priorités révisées

1. **Appelants sans classId → « 202 »** — encore ouvert pour :
   distants (classId parsé mais jeté, style absent du 263 → palier 0
   au mieux), sélecteur (`c.ClassId` dispo, signature CharDressing
   sans classId, clé de cache à élargir), création (classe choisie
   ignorée par RefreshModel), previews item/perso. *Test :*
   `--look-log` → ligne `[outfit] letter=X class=C style=S → …` ;
   un mage nu doit afficher `class=2 … → 203` et non `→ 202`.
2. **Coiffe de classe jamais affichée** — prouvée inatteignable sur
   tout le corpus (0 code1 `#`). *Test :* attacher
   `char/model/G1101` à « Bip01 Head » en VisTest vs ref-shots.
3. **`style=0` faute de 489** — solo/nohud uniquement en pratique
   (réparation live via ClassesChanged) + fenêtre inter-persos du
   `_classes` statique.
4. **`Entry.Style` ≠ index garde-robe** — non tranché (désasm natif
   requis).
5. **`RowForSuffix` = AS01** — latent, non déclenché par les chemins
   de classe actuels (rows peuplés par code) ; ne pèse que sur le
   repli « 202 » où AS01 est la bonne texture.
6. **Costumes invisibles au sélecteur** — inchangé.
7. **Meshes `#X2nn` absents du méga-GLB** (races M/F/K) — non
   inventorié ; `--mesh-dump`/`DumpLook` couvre.
8. **Tenue de classe sous attachements non-`#`** — si le porté ne
   résout que cape/casque, `outfits` reste vide → tenue de classe
   affichée *sous* les attachements (CharVisual.cs:1156-1158) —
   comportement natif non vérifié.

## Limites de l'audit v2

- Comportement natif : port de la coiffe code1, sémantique `Entry.Style`,
  tenue de classe distante (le 263 ne porte pas le style — le serveur
  met-il la tenue de classe dans le set0 ? non vérifié).
- Ordre réel du burst zone vs `GameScene._Ready` (le style au build
  dépend du timing 489) — non exécuté (pas de build).
- Contenu des méga-GLB par corps — hors périmètre.
</content>
