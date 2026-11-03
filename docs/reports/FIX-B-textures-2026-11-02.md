# Textures (demandes 3, 4, 1) — 2026-11-02

## 3. Joueur : couleurs aberrantes — CORRIGÉ (VISUALLY_VALIDATED sur 1 perso)
Cause : le corps nu `#<L>000` (ici `#G000`) partage le masque rouge de peau
`gx01as01a` des meshes `S*` mais n'était jamais teinté (« hors-pattern » dans
`CharVisual.TintMeshes`). Il apparaissait en rouge vif sous la tenue `#G202`
(jambes, torse). Fix : `CharVisual.TintMeshes` teinte `#<L>000` avec la teinte peau.
Preuve : `artifacts/shots/live-perso-avant.png` / `live-perso-apres.png`
(live, flux `--login --channel --char`, serveur local, compte test123).
Diagnostic ajouté : `--mesh-dump` (`CharVisual.DumpVisible`).
Limites : un seul personnage (corps G, tenue de base #G202) ; pas de
comparaison à une référence officielle de ce perso ; tenues portées par le
serveur (10 items) ne changent pas le mesh affiché (non investigué).

## 4. Plantes / champignons / toutes textures — NON REPRODUIT, audit chiffré
`RenderAudit` (scène `render_audit.tscn`, `map` + `char`, 3 min) →
`artifacts/coverage/render-audit-1{,.summary}.tsv`.
- 319 683 meshes map OK ; 7 722 char OK.
- MASQUE_TEXTURE 5 144 : 5 084 `shadow_overlay` (plans d'ombre `UD10801`,
  voulu) + 60 `textured_blocker` (`nocamerablock`).
- REF_NON_RESOLUE 127 : toutes dans EDENFR-GAPS G13 (textures jamais livrées).
- TEX_NOIRE 66 : masques de cheveux `b1h*` (intensité, teintés en jeu) et
  silhouettes de foule `s113054`, `ud51607` — authentiques probables.
- Captures S002 (live), S004, S022 : plantes et champignons texturés.
Si le propriétaire voit des plantes sans texture, il faut la map et la position.

## 1. Particules en rectangles — correctif agent E confirmé à l'image
`NifMaterials.For` gère déjà dst=ONE en additif ; `kit-before` / `kit-after`
regardées : halos opaques remplacés par traînées additives (kitsune 253020).
Reste : centaure thunder non capturé ; sens de mouvement interne non analysé.

## Autres correctifs
- `ScreenManager` : `--shot` attend la fin du pilote auto (login/canal/perso).
- `VfsReadGuardTests` : exception `RenderAudit.cs`.
- `PlayerController` : `--input-test` solo termine (point 9 prouvé).

## Session 2 — captures du propriétaire (formes noires, carrés, glitch monture)
Diagnostics ajoutés (flags, `RenderAudit`/`GameScene`) : `--black-scan`, `--pick-px=x,y`, `--lum-scan` (8 captures + BigScan + RideDir), `--mesh-dump`.
1. **Plantes en silhouette noire** — cause : variantes « N » (UE715N, UE716N…) : ambient=(1,1,1) diffuse=(0,0,0). Gamebryo les éclaire par l'ambiante seule ; l'import lisait le diffuse → albedo noir opaque. Fix `NifMaterials.For` : diffuse nul ⇒ ambient. Preuve : pick du pixel d'une tache (`ue71501 col=(0,0,0)` → `(1,1,1)`), capture avant/après (tache noire → plante rouge). Scan 6 maps (S001-S004,S012,S022) : plus aucune couleur d'albedo noire hors plans d'ombre en blend.
2. **Carrés gris sous les entités** — cause : `BlobShadow` texture L8 (alpha=1 partout) ⇒ Decal carré. Fix : RGBA8, disque dans l'alpha. Preuve : `artifacts/shots/live-ombres-apres.png`.
3. **Glitch plein écran sur monture animée (bande rose, queues qui enflent)** — cause : `BillboardDriver` relisait `_node.Scale` (déjà multiplié par `pb.Inverse()`) → échelle exponentielle. Fix : échelle d'origine mémorisée. Preuve : AABB Plane04 36→75 u en 3 s avant, stable après ; `monture-apres-billboard.png`.
4. **Casques/capes en masque brut (rouge/vert/bleu)** — le masque `<skin>B` des meshes attachés n'était jamais teint. Fix : `DyeMeshSurfaces` appliqué aux meshes attachés (ligne 4pequip). Preuve : `--appearance-test` : `B1102`, `G1107` BRUTE → TEINTE.
5. **Sens de marche des montures** — mesuré cos(tête-pelvis, déplacement) = +0,96/+0,87 : correct (kitsune 253020, S002). L'inversion des *animations* n'est pas reproduite.
6. **Luminosité qui varie** — scène solo immobile : écart 0,0 sur 8 captures. Non reproduit ; piste : cascades d'ombres/SSIL en live.

## Session 2 (suite) — HUD et classe
- **Classe (11)** : panneau en live (`--hud-open=class`, `--class-tab=0/1/2`, `--class-mock=`) : compétences générales + de classe issues de fr-full pour Guerrier/Clerc/Barde, onglets connaissances (talents, certificats) et aperçu rendus ; captures `classe-onglet1/2.png`, `classe-mock1/2.png`. **Non vérifié** : arbre de talents par classe/éveil (mêmes cases pour toutes les classes ?) et contenu serveur des talents appris.
- **Montures (12)** : archive = 340 montures dont 91 absentes de `c_collection` (« Hors archive »), dont Renard Blanc du blizzard 48813 (rideKey 268010) ; rendu de 268010 vu (`monture-268010.png`). **Non fait** : séquences KFM de combat de monture, nuage noir sous la monture (FX `R260-262` ShaderMaterial), inversion d'animation (non reproduite).
- **Chat (10)** : racine non épinglée + layout réappliqué → onglets/+/Opt. empilés. Fix : racine plein écran, `RepinBand` (timers + SizeChanged), onglets créés avec texte source EN (AutoTranslate). Preuve : `chat-avant.png` / `chat-apres.png`, dump de rects stables.
- **Profil (7)** : libellés d'armure à taille 11 (mesuré) — `profil-apres.png`.
- **Vitals/EXP (14)** : barres EXP personnage + âme présentes ; trait héroïque rendu (`--hero-trait=3`) ; le serveur envoie 9058 (trait 0 pour test123). Ajout `--hero-trait=N`.

## Première attaque (6) — mesuré (`--combat-test` live, A/B `EDEN_ATTACK_LEGACY=1`)
| | avant (legacy) | après |
|---|---|---|
| nombres affichés par coup | 2 (réponse 272 kf≠-1 puis kf=-1) → ex. 139+139 | 1 (3 flotteurs pour 3 coups) |
| 1ʳᵉ animation d'attaque | +22 ms, décodage `g.kf:attack01` 18,7 ms sur le thread principal | +0/+1 ms (séquence préchauffée) |
| 1ᵉʳ dégât affiché | +79 ms puis 2ᵉ nombre +352 ms | un seul à l'arrivée du 272 |
Limites : mesure sur 3 coups à 1 compte ; la synchro dégât ↔ frame de hit de l'animation officielle n'est pas comparée au décompilé.

## Session 3 (2026-11-03)
- **+0 attaque/soin des skills** : `#29#` (ATQ) ↔ c_spell **col36** (1252/1426 textes), `#30#` (soin) ↔ **col35** (127/135) ; le code lisait l'inverse. Canal du palier : col2 physique, col3 si la ligne dit « MAG ». Non vérifié en capture.
- **Procs** : tooltip d'item déjà par item ; l'icône de buff actif affichait la description du déclencheur (« 5 % de chances… ») → `EnchantTable.ActiveTextOf` (champ court « MAG +8214 »).
- **Trait héroïque** : choisi à la création ; le perso test avait `create_flag` NULL (base `edenfr`, schéma `game`). Fixé à 3 en base → icône visible (capture).
- **Clic droit → 1re attaque** : `_swingTimer` initialisé à 0 (≠ « premier swing immédiat ») + reset du timer à chaque envoi hors portée → attente 1,2 s en plus. Corrigé (timer armé).
- **Quêtes** : traces périmées filtrées à l'entrée de zone (FMS 3/4 seulement). L'état DB du perso ne contient qu'1 quête terminée (1010) + 266 en FMS 5 ; « faites mais reproposées » non reproduit (nécessite une session de jeu).
- **Tracker** : bouton « + » = repli (SizeBtn), icône inversée → « − » déplié ; bande haute répartie sur toute la largeur (marges 10 px).
- **Chat** : labels/boutons centrés (`CenterLabels`).
- **Inventaire sac** : vue unifiée (sac de base + extensions, 3×12 = 36 cases avec Bagage du Voyageur, items conservés) — chaque case porte son sac (move/split/tri).
- **Robe de protection** : clé FR corrigée.
- **Skins** : régression `G202AS01C ≈ A + Σ Bᵢ·dyeᵢ` (err 0,0135) → composition additive (gain ×2) ; détails d'armure visibles (`skin-additif3.png`). Teinte peau/cheveux NON revue (peau orangée, cheveux plats).
- **Lignes 1 px monture** : plans de FX skinnés non billboardés → billboard matériau par défaut (`EDEN_NATIVE_BB=0` pour l'ancien).

## Session 4 (2026-11-03, suite)
- **Skins** : masques R de cheveux (max 123/255) et peau conçus pour un gain ×2 ; `Tinted` normalisait + plancher 0,7 (aplat). Mode gain direct (cheveux ×2, peau ×1,15), armures additives `A + Σ Bᵢ·dyeᵢ ×2`. Les 4931 lignes 4pequip (#) ont toutes leurs A et B (script) : aucune texture de tenue manquante ; seules G13 (non livrées) manquent. Capture `skin-gain.png`, `appearance-test` (7 corps).
- **Quêtes** : règle serveur — seule une quête journalière (col6 bit4) ou répétable (titre répét./quotidien/daily/hebdo) peut être reproposée ; toute autre est unique même avec limite col4>0 (compteur de session perdu au relog). Traces périmées filtrées (entrée de zone + push mission).
- **Auto-attaque après cast offensif** : `SkillCast.EngageAfterCast` (prouvé : `[combat] engage` après `cast #50011 clos`).
- **Freeze** : verrou global de `KfSource.Baked` tenu pendant les cuissons (~105 ms) → thread principal bloqué pendant le préchauffage ; verrou par séquence (Lazy). Hitches > 40 ms : 24 → 13 sur `--combat-test --hitch`. Reste un pic ~100-150 ms au 1er coup reçu par un mob (non élucidé).
- **Profil** : corps dupliqué remis à Position 0 / Scale 1 (décalage de monture/snap). Non prouvé sur votre résolution.
- **Tracker** : cadres dessinés ré-épinglés avec les contrôles ; capture alignée.
- **Montures hors archive** : 58 clés de monture sur 58 se montent (cycle `/tmp/ride_key.txt`, `[mount] visuel` pour chacune). Combat de monture / particules / sens d'animation non traités.
- SpellFx : `HandleBillboards` suit le nouveau défaut (billboard matériau) ; sinon double rotation.

## Session 5 (2026-11-03)
- **Banc skins** : `vis_test --appearance-test --skin-sheet=SEED --skin-body=N` (6 apparences aléatoires : coiffure, visage, couleurs cheveux/peau/yeux). Le banc n'avait pas `TextResolver.ResolveFile` → table de couleurs vide → toutes les figurines identiques ; branché. Les 7 corps (g b m f k l c) se rendent avec coiffures/couleurs variées (`/tmp/.../races.png`). Visage = composite A (RGBA) + B (R peau, G iris) d'après `FUN_00ab8000`.
- **Freeze après kill** : libération étalée des cadavres et FX (`SlowFree`, 10 nœuds/image) ; mesure `--hitch` avec Δobjets : −827 objets d'un coup (66 ms) → paquets ≤ 250 (~50 ms, proche du pas de base 40 ms de ce banc). Pic +150 objets (création FX / floaters) toujours ~80-110 ms.
- **Sorts de zone** : client déduit la forme (c_spell col21/col31) ; shape 1 → NCID 16 au sol sur la cible ; shape 3 → NCID 17 centré lanceur. Non testé en jeu.
- **Profil** : clic sur nom/valeur d'une stat → détail (valeur, naturel+bonus, formule) ; bouton « Statistiques avancées » retiré (`stat-detail.png`). Aperçu perso recentré.
- **Procs** : données = 5 % pour 1376/1838 items à proc (col41 c_enchant, col42 S_Enchant serveur) ; pas de correction possible sans inventer.
- Non fait / non vérifié : FX en carrés (banc FX vide), bordures d'animation, anims montures, stats de monture, clic droit monture, thème violet.

## Session 6 (2026-11-03)
- **Skill niv. 28 à classe niv. 1** : le serveur (`TrySpellLevelUpAsync`) utilisait la classe ACTIVE (Guerrier niv 28, 92 025 PC) pour monter un sort d'une autre classe affichée (Chasseur niv 1, 0 PC). Garde `ClassSkillTable.CanCast(classeActive, sort)` ajoutée ; client : « +1 / Max » grisés si la classe affichée n'est pas l'active.
- **Talents** : l'arbre est déjà dynamique par classe (`c_talent.ini` : 3 branches × 8 colonnes, prérequis à 1, 2 ou 5 colonnes à gauche — 1035/620/5 cas). Corrigé : géométrie (les colonnes 7-8 passaient sous le panneau), flèches de prérequis vers toute case PreSpell de la branche (pas seulement la voisine).
- **Tenue de base de classe** : `c_class.ini` cols 55+ = « niveau itemHomme itemFemme 0 » (≤ 9 tenues/classe) → `4pequip.ini` par code d'item. Le client affichait toujours `#G202` (tenue Guerrier). `CharModelSpec.ClassCloset/ClassOutfitRow`, `CharVisual.BuildBody/ApplyEquipment(classId, style)`, `CharVisual.LocalClass()` ; rafraîchi au changement de classe. Preuve : planche `--skin-classes` (Magicien = robe).
- **Class News (Aperçu de la classe)** : 9 boutons de tenue (verrou selon niveau de classe), aperçu 3D du joueur avec la tenue choisie, « Tenue » → `ClosetSet` (167). Preuve : `classe-news.png`. Limite : les autres joueurs affichent encore la tenue 202 (classe/style non portés par le blob d'apparence).
- **Catalogue des modèles** : `tools/model-catalog.py` → `docs/reference/MODELS-CATALOG.tsv` (7991 fichiers classés) + `.md`.
- **Profil** : fenêtre dédiée par stat (`StatDetailWindow`) : valeur, diagramme (naturel/bonus ou jauge/plafond), rôle, formule, « vos valeurs » serveur 32002.
- **Teinture armure** : ordre des canaux de masque vérifié sur 12 tenues avec texture C officielle : R,G,B → couleurs 1,2,3 gain ×2 (meilleur, erreur 0,103).
- Non fait : FX carrés, bordures d'animation, montures (anim, FX, stats), clic droit monture.
