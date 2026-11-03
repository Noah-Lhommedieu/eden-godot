# Plan d'action v2 — refonte fidélité totale (2026-10-27)

> Duplicata du plan `2026-10-26-refonte-fidelite-totale.md` réorganisé
> selon les **nouvelles tâches** remontées le 26/10 (fin du doc v1) +
> les corrections utilisateur de `docs/hud-interfaces.md` (noms, rôles,
> ref-shots renommées par panel).
>
> Légende : `[ ]` à faire · `[v]` marqué fait en v1 → **à re-vérifier**
> en runtime · `[x]` prouvé cette vague (build 0 erreur + preuve live
> ou capture `--shot` comparée à la ref-shot du même état).
>
> **Méthode obligatoire par HUD/panel** : reproduire la logique ET le
> visuel — layout natif CEGUI du même nom quand il existe, ref-shot
> `ref-shots/<Panel>*` comme cible visuelle, fonction réelle contre le
> serveur (jamais « juste affiché »). Fermer toute instance Godot après
> chaque tâche (deux clients ouverts = freeze du PC — **jamais deux
> instances Godot simultanées** ; le second acteur des preuves
> multi-joueurs = `tools/HeadlessBot` : pile EdenSession réelle,
> modes `duel-accept` / `mail-check`). Petit commit par sujet.

## Ordre de développement (dépendances)

1. **Bloc A** — renommage « Eden Limitless » (trivial, première pierre).
2. **Bloc B** — perf : lag <60 fps en jeu, fond login ~2 s, chargement
   modèle select/create — ces lenteurs plombent CHAQUE test ensuite.
3. **Bloc C** — gameplay feel : échelle joueur/mobs, vitesse ×5 dev,
   mobs qui téléportent.
4. **Bloc D** — **système de thème + classe de base de fenêtre
   (héritage)** : prérequis pour TOUS les panels ensuite.
5. **Bloc E** — purge des HUD parasites vs `hud-interfaces.md`.
6. **Bloc F** — écrans connexion (login/channel/select/create/loading).
7. **Bloc G** — couleurs corps manquantes (épaules ezelin/humain…).
8. **Bloc H** — HUD permanent conforme `hud-interfaces.md`.
9. **Bloc I** — fenêtres/panels conformes ref-shots.
10. **Bloc J** — analyses `re/` restantes (chargement select→jeu…).
11. **Bloc K** — matrice captures + docs + ménage final.

## Bloc A — Nom du jeu : « Eden Limitless »

- [x] `config/name="Eden Limitless"` dans project.godot (titre
  fenêtre dérivé) + libellés visibles (« Welcome to Eden
  Limitless! », boot « EDEN LIMITLESS ») — **re-vérifié 27/10 :
  capture login montre « EDEN LIMITLESS » + Ver:002.003.05.67**.
  Nom d'assembly `EdenClientReborn` conservé (cosmétique interne).
- [x] Cohérence serveur : le système nomme déjà « Limitless »
  (2032/2033 ZoneLimitless*).

## Bloc B — Performance (nouvelles tâches 1, 2, 4, 6)

- [ ] **Lag in-game <60 fps** — **bisect mesuré (S001 steady)** :
  21 ms → ~15 ms (~47 → ~65 fps). Cause dominante = MASSE DE NŒUDS
  (~0,7 µs/nœud/frame moteur, pas le rendu : --disable-render-loop
  identique, 800×450 identique → CPU main-thread). Appliqué :
  - colliders 9,7 k nœuds → 2 bodies (wall/floor, trimesh fusionnée,
    `ColliderConsolidator`) : −6 ms ;
  - sous-arbres Entities (~6,6 k nœuds / 263 NPC) ≈ −8 ms au strip —
    EntityAnimator LOD 80 u + ticks interp/anim mutualisés dans
    `EntityRegistry._Process` (−2 ms anim) — reste ~4-5 ms de masse
    passive (skeletons/BoneAttachment/labels) ;
  - alpha-blend → alpha-hash (props batchables), labels 3D à
    VisibilityRangeEnd=80, diag --strip*/proc-dump/node-dump ajoutés.
  Reste : ~17,6 k nœuds (Node3D×7,1 k holders FSM, MI×2969, MM×1137) —
  ~60 fps atteint en steady mais chargement toujours ~10 fps
  (streaming corps ~11 s) → affiner si la barre remonte (merge
  holders / Node3D→mesh pools).
  **Re-mesure 2026-11 : ~90-100 fps live steady (9,8 ms), ~60-70
  solo** — objectif dépassé ; seules restent les spikes ~140 ms de
  compile shaders au premier affichage (warmup à faire, Bloc K).
- [ ] **Login ~2 s avant le fond** — MESURÉ : le fond n'est PAS une
  image mais le hall 3D `S218_room10b.glb` (~2,2 s de parse en tâche
  de fond ; timeline PerfTrace : login 0,9 s → temple 2,3 s).
  Corrigé : splash vanilla `Loading_0` (client d'origine, pas le
  visuel Reforged) en TextureRect plein écran jusqu'à TempleLoaded
  — fond instantané, swap propre. Preuve /tmp/eden_login_early.png
  (--shot-early ajouté pour capturer la fenêtre).
- [ ] **CharSelect** : chargement modèle skin lent — FAIT (f792028) :
  `CharBodyCache` (proto détaché par empreinte apparence → Duplicate
  ~50 ms) + prewarm dès réception de CharList + portail par clé (plus
  de double build prewarm/clic — mesuré : 1 seule paire
  LoadGlbNode/TintMeshes, avatar attaché t=7,97 s vs 8,18 s, et plus
  de CPU gaspillée). Reste à froid : ~5,5 s premier build (GLB 2,8 s
  + teintes 2,6 s) — levier suivant = cache disque `user://` (PNG
  teintés + GLB pré-parsés) si toujours gênant.
- [ ] **CharCreate** : FAIT (218a59e + cf24650) — même cache corps
  (CharBodyCache) + caméra reculée 2,9→4,4 (plan pieds-tête) +
  panneau droit ~300 px ajusté au contenu + check-name/back prouvés
  live (NCID 32→0, 7→41→retour charsel) + flux création complet
  (zzdevtest9 créé #200000207, rejet compte-plein remonté au lieu
  d'être avalé) + fix surfaces noires (albedo null → composite
  masque ; spec=0 → artwork natif). Épaules foncées = artwork
  légitime B202AS01 (vérifié M/F/halfkin).
- [v] RAM : top 5 postes MESURÉS (`--perf` + nouveau tally
  `tex-postes` par source) — solo tex=620 MB / RSS 2,9 Go ; live
  tex=4,5 Go / RSS 5,0 Go. Dominant : textures exclues de la
  compression S3TC (`char/texture`, `char/model`, atlases UI,
  teintes par avatar) + `monster/texture` 488 MB décodés +
  `glb:S218_room10b` 390 MB. Détail dans `docs/PERF.md` — levier
  restant = compresser les produits de teinte / streamer les skins.

## Bloc C — Gameplay feel (nouvelles tâches mobs/échelle/vitesse)

- [ ] **Mobs se téléportent** : corrigé (commit `7b0722d`). Cause :
  le 326 est une prédiction cur→dst — le client marche lui-même
  (MobWander n'émet qu'1 trame/trajet) ; pousser `dst` à `now`
  plaçait le mob instantanément à destination. Fix :
  `InterpBuffer.PushMovement` (cur@now + dst@now+dist/vitesse,
  flag `Terminal` = clamp sans rubber-band) ; vitesse prise au spawn
  (298 FieldSpeed / 265 MoveSpeed) ; garde anti-stale 0,45 s (pas de
  trame d'arrêt serveur → `InputMoving` coupé, arrêt franc mesuré
  par `EntityAnimator`). Preuve : wire décodé live (~50 trames/mob,
  chase 100 ms ×0,72 u), 8/8 InterpBufferTests verts, run node 2
  (20+ mobs, combat engagé, 0 crash). À revérifier à l'œil lors du
  prochain playtest (glisse fluide attendue).
- [ ] **Échelle** : résolu par la caméra (commit `8a3a4c4`). Les
  modèles sont déjà aux bonnes proportions natives (mushroid M010
  h=0,93 / joueur ~1,4 = 0,67 ≈ ref ~0,7 — `ref-shots/TailleMob.png`)
  ; le « monde trop grand » venait du `SpringLength=6,5` (~23 % de
  hauteur écran vs 12,5 % natif) → 11 u. Vitesses natives déjà bonnes
  (RunSpeed 10 ≈ BaseMovement serveur 9,4). À revalider à l'œil au
  prochain playtest (cadrage, mobs vs perso en profondeur).
- [ ] **Vitesse dev ×5** : `--devspeed` (commit `200b2fe`) — ×5 sur
  `MoveSpeed`/`RunSpeed` du joueur uniquement ; le serveur renégocie
  la position via le MoveSelf ~10 Hz habituel (live : aucune déco,
  log `[dev] vitesse ×5 (MoveSpeed=30 RunSpeed=50)`).
- [ ] **Anim de cast joueur** : au 279 CastOK, l'anim d'action du
  sort joue — `C_Spell` col3 → id KF → `motion.ini` via
  `CharModelSpec.BodyStem` (commit `5be99b3`). Prouvé live
  `--buff-test` : cast 50014 → `cast anim : shoot12 (kf=412)`.
- [ ] Saut : implémenté côté client (`jump01` + `Airborne` en vol,
  double saut `MaxJumps=2` = « native double jump »). Preuve re/
  (strings rf_d3d9) : `jump_feature` Reforged = chaîne de sauts
  **validée serveur** (SetFloat — « air jumps are counted when
  sent ») et `jump_feature=0` = désactivé dans leur config → rien
  à capturer en comparaison. Limite notée : la validation serveur
  du float n'est pas implémentée (mouvement sol-side only).

## Bloc D — Système de thème + fenêtre de base (tâche « ESSENTIEL »)

Objectif utilisateur : **héritage** — un code initial produit un
panel thémé/déplaçable ; panels « simples » = héritage pur, customs
= override. Thèmes natifs dans les assets (bleu, violet, rouge,
gris/café…) modifiant entêtes + contours de quasi tous les panels.

- [ ] **Données thèmes** : `copy_ui_png.py` copie
  `uidata/imagesets/<T>/` + `themes/<T>/` (11 thèmes) dans leurs
  arbres respectifs sous `ui_textures/` — la « collision » était une
  duplication md5-identique (Blue/Green/Red/Purple), pas un conflit.
  ✔ `2269836`
- [ ] **`UiTextureCache.Theme`** : `SetTheme` résout
  `imagesets/<T>/` puis `themes/<T>/` (noms de dossiers
  casse-normalisés) puis base ; invalide les caches et re-mappe les
  `AtlasTexture` existantes sur le nouvel atlas (géométrie
  identique → rects réutilisables). ✔ `2269836`
- [ ] **`EdenPanel`/`EdenWindow` (base)** : `EdenWindow` créé —
  `BuildWindow(hud, layout, title, key, pin…)` = Build →
  `BuildFallback()` virtuel → `Pin` → `OnBuilt()` virtuel →
  `SkinUnstyled` → `EnsureTitleBar` + `Persist` ; `FinishWindow`
  pour les panneaux codés. `TransportWindow` migrée (gabarit).
  Migration progressive des autres fenêtres = Bloc I. ✔ `6dcebe7`
- [ ] **Drag universel** : dialogs codés (Battlefield, Hotel,
  Lottery, MagicBox, MessageBoard, RollTreasure, Duel,
  SystemService) → titre statique remplacé par titlebar native
  (× + drag fond + chrome thémé) + `Persist` ; Inspect/FeatureDialog
  (titre dynamique) et DeathWindow → drag + `Persist`. Toutes les
  fenêtres flottantes ont drag + persistance `ui_layout.cfg`.
  ✔ `6dcebe7`
- [ ] **Thème live** : `ApplyTheme` → `hud.Tex.SetTheme(theme)` —
  les AtlasTexture déjà posées sont re-mappées sur l'atlas thémé
  (TextureButton/TextureRect/NinePatchRect à jour live) ; les
  nine-patch composés se régénèrent à la reconstruction des
  fenêtres. `ThemeTint` = repli si le thème n'a pas d'assets.
  Prouvé : HUD live `theme=Purple` → chrome violet sur
  l'inventaire. Swap via sélecteur prouvé au code (chemin unique),
  capture visuelle du toggle à faire. ✔ `2269836`
- [ ] Sélecteur Settings branché : `_optTheme` → `S.Theme` →
  `ApplyInterface` → `ApplyTheme` → `SetTheme`. ✔ `2269836`

## Bloc E — Purge HUD parasites (vs `hud-interfaces.md` corrigé)

- [ ] `GoldHud` : retiré — classe + montage (classe + montage ;
  DumpRects/ForceOpen n'en parlaient pas). ✔ `d027607`
- [ ] `MountHud` : retiré — classe + montage MissionBridge +
  ToggleRide/FindMountItem/EnchantTable local morts (ItemUse
  couvre le ride via double-clic inventaire, UseType≠0). ✔ `d027607`
- [ ] `UltimateBar` : `Visible=false` après Build (code conservé).
  ✔ `d027607`
- [ ] Audit montages vs `hud-interfaces.md` : fixtures visibles =
  vitals, minimap, quest tracker, chat, hotbar, menu icons, banner,
  buff/nerf bars, target, castbar, floaters, party, pet bar — tous
  §1 ; le reste = fenêtres §2 cachées par défaut. ✔ `d027607`
- [ ] `SkillsWindow` → `ClassWindow` (clé `skills` + raccourci K
  conservés ; node name `Skills` inchangé). ✔ `d027607`
- [ ] `CastBar` : nom du sort retiré → progression + secondes
  restantes (`BeginCast(seconds)`). ✔ `d027607`
- [ ] `DamageFloaters` : pas de nom de sort — vérifié, n'affichait
  déjà que les montants. ✔
- [ ] `NerfBar` : `NerfBar : BuffBar` (debuffs seuls, slots
  Debuff1-10 natifs reparentés, timer m:ss, stack ×N, tooltip,
  liseré rouge) épinglée juste à gauche de BuffBar ; handle self
  propagé aux deux barres. Prouvé live : debuff démo visible dans
  la NerfBar séparée. ✔ `d027607`

## Bloc F — Écrans de connexion (nouvelles tâches 3, 5, 8)

Cible visuelle : ref-shots (`Selection_channel.png`,
`Selection_personnage.png`) + hud-interfaces §6. Thème = chrome
CEGUI orné sur fond temple 3D (pas de panneaux plats maison).

- [v] **Login** (d22989f) : chrome `DejaVu/FrameWindow` + fond sombre,
  champs compte/mdp, Confirm/Cancel, bannière, footer disclaimer +
  Official Website (x-legend.com) / Quit — capture `/tmp/login_v3.png`.
- [v] **Channel** (d22989f) : 2 colonnes Server/Ch. + puce gemme
  (`Emoji:gem` vert/rouge) + scrollbar + « Welcome to Eden
  Limitless! » + Confirm/Cancel — capture `/tmp/channel_v4.png`.
- [v] **CharSelect** (15217e6) : **panneau « Preview » retiré** —
  remplacé par une strip roster native-style (cellules nom + paging,
  équivalent `face1-5`/`pageBtn` du layout natif ; les atlases FACES
  sont des feuilles UV, inutilisables en portrait → cellules nom).
  Fiche perso haut-droite, rotate ◀▶, Start Game, Set/Remove password
  (NCID 30/31 → 75/76, codec prouvé NetTests), Select Server, Logout —
  capture `/tmp/charsel_faces.png`.
  ⏳ reste : round-trip live set/remove mot de passe à rejouer.
- [v] **CharCreate** (dffe78c + cf24650/218a59e) : panneau droit
  refait (chrome natif + fond sombre + skin `SkinUnstyled` +
  séparateurs + boutons `ScreenStyle`) — capture `/tmp/create_v4.png`.
  « Check Name » prouvé live (NCID 32→39 = 0), « Back » prouvé live
  (opcode 7 → retour CharSelect), création bout-en-bout prouvée
  (NCID 4→39 = 0 + liste rafraîchie → 2 persos), caméra reculée
  (corps entier cadré).
- [v] **Loading** (f2ee8f0) : artwork plein écran (bandes dorées
  cuites visibles, `StretchMode.Scale`), titre/sous-titre map via
  `t_node.ini`, tip haut-droite, lore bas-gauche, **barre native
  ornée** — fill rose `loadingframe01` rognée + cadre doré
  `loadingframe00` + alpaga animé (`animation.png`, 5 frames) au bout
  du remplissage ; splash « EDEN REFORGED » évité (fallback neutre) —
  captures `/tmp/loading_v3.png`, `/tmp/loading_v4.png`.

## Bloc G — Corps perso : couleurs manquantes

- [ ] Épaules/parties corps noires/vides (ezelin + humain M/F) :
  audit `CharVisual`/`CharDressing` — meshes sans texture résolue
  (KFACES*/`CharColorTable`), matériau par défaut quand texture
  absente, espace couleur DDS/TGA. Comparer à la liste de tenues de
  départ par race.
  - Audit 5 races × 2 genres par captures (`--create-race/gender`) :
    corps/tenues/peau texturés partout (dye+nullTex déjà fixés).
  - **Ursun coupé** : caméra [Create] fixe 4,4u → `ApplyShot` par race
    + `FitCreateCamera` sur l'AABB avatar (423a396) — preuve
    `/tmp/create_r4g0.png`.
  - **Visages masques rouges** : variants impaires (B/D/F) de
    `LFACES*` = masques R à teinter par la couleur peau/fourrure —
    appliqués bruts → visages rouges pour skin 1/3/5 (04185c3,
    `TintedFaceMask` préserve yeux/dents). Preuves
    `/tmp/zumi_skin1.png` `/tmp/human_skin3.png`. Zumi skin=0 =
    fourrure sombre native (variant A), pas un bug.

## Bloc H — HUD permanent (hud-interfaces §1)

Reproduire logique + visuel ref-shot ; chaque item = build + capture.

- [v] `TopLeftVitals` — HP rouge clair / mana bleu clair / tête dans
  le cercle ; **clic droit portrait → Reset dungeon** ; **si groupe →
  paramétrage loot** (random/taunt/items all/green/blue).
- [v] `EXPBar` — double jauge EXP perso (vert pomme) + EXP classe
  (violet), % XX.XXX ; **clic droit → mini panel précision** (XX /
  XX.X / XX.XX / XX.XXX). (v1 : UltimateBar seule → refaire)
- [v] `MinimapPanel` (dc6a828) — tous les boutons câblés : mail→
  MailWnd, InGame→ItemMall, Fty→ItemMall (loyalty), AL→LotteryWnd
  (3 autels : normal/en-jeu/diamant), Battle→BattlefieldWnd,
  Rank→RankWnd, Territory→TerritoryWnd, Team→SocialWnd,
  Cooperation→RecruitWnd, Hide→replie la rangée ; **Arena**
  (ticket Col) + **Events** (gift BONA) ajoutés via régions atlas
  Radar (absents du layout Ver2) ; tooltips partout ; panneau élargi
  210→240 px — capture `/tmp/minimap_v3.png`.
- [v] `QuestTracker` — objectifs **cliquables → auto-follow** ;
  avancement → `ZoneBanner` centre écran.
- [v] `ChatPanel` — onglets Normal/Battle + créés, choix canal,
  journal combat.
- [v] `SkillBarWindow` — jusqu'à **3 lignes**, clic gauche déplace,
  hotkeys/clic droit activent.
- [v] `MenuIconsPanel`.
- [v] `BuffBar` + **`NerfBar` (Bloc E)** — prouvée live par
  `--buff-test` : `//learn_spell 50014` → cast auto-ciblé → 279 ack →
  299 `EnchantAdd` (effet 0xcb2e, 300 s) → `EntityBuffs` self = 1 →
  slot BuffBar rempli.
- [v] `ZoneBanner`.
- [v] `TargetFrame` — orné + « Target's Target » (TargetBg) + bouton
  inspect (stats/drops/skills du mob).
- [v] `CastBar` — progression + secondes restantes, pas de nom de
  sort (Bloc E) — capture live `/tmp/castbar.png`
  (`--hud-open=cast`, barre à « 0.8s » en vol).
- [v] `PartyHud` — clic droit membre → kick (si chef) / whisper.
- [v] `PetFunctionBar`.
- [v] `DamageFloaters` (sans nom de sort — Bloc E).
- [v] `TreasurePickup`.

## Bloc I — Fenêtres & panels (hud-interfaces §2-§5)

Chaque fenêtre : layout natif + ref-shot + fonction live. Migrer sur
la base `EdenPanel` (Bloc D) au passage.

### Principales
- [v] `InventoryWindow` (live prouvé) — re-vérifier drags/split/use.
- [v] `ProfileWindow` (+`EquipSlotsPanel`).
- [v] `ClassWindow` (ex-`SkillsWindow`, K) — icônes réelles, Learn,
  drag → hotbar.
- [v] `QuestWindow` (MissionLog) — accepter/rendre live (388→87 pas
  encore déclenché en v1).
- [v] `WorldMapWindow` — icônes mapBtn noires (imageset manquant) +
  filtres sans marqueurs : à finir.
- [v] `SocialWindow` — onglets TabControl non rendus visuellement.
- [v] `ClassChangeDialog` (P) — swap live à prouver.
- [v] `ElfWindow` + `ElfBook`.
- [v] `GuildWindow` — recruitment (Apply to Join) + fenêtre guilde.
- [v] `CraftWindow` 🔨.
- [v] `TransportWindow`.
- [v] `SettingsWindow` — 5 onglets réellement appliqués + persistés.
- [v] `InspectWindow`.

### Commerce / stockage / services
- [v] `ShopWindow` (achat prouvé) · [v] `BankWindow` (prouvé) ·
  [v] `NeobankWindow` · [v] `FamilyStorageWindow` ·
  [v] `TradeWindow` (prouvé 2 clients) · [v] `VendingWindow` (**étal
  live prouvé** : vendeur 77→377[1][2] + vente 275/280 ; acheteur
  72→378→76→274) · [v] `AuctionWindow` (live à prouver) ·
  [v] `MailWindow` (**PJ↔PJ prouvé** : envoi test123→test1 + or joint,
  vérifié en boîte destinataire) ·
  [v] `ItemMallWindow` (achat prouvé) · [v] `MessageBoardWindow`
  (MsgBorardFWin natif — contenu re-parenté) ·
  [v] `FeatureDialogWindow` (chrome natif, passe-plat) ·
  [v] `LotteryWindow` (chrome natif — layouts loterie = roues
  animées, pas de chooser) · [v] `MagicBoxWindow` (idem, contenu
  formulaire conservé) · [v] Repair (fixAllBtn/fixoneBtn natifs
  déplacés en bord bas + senders 45-48 — **round-trips live prouvés** :
  `--repair-test` émet 45 → 342 et 47 → 343 (coût=0, équipement non
  endommagé) ; confirme 46/48 via popup existante — sans réponse
  serveur, seul l'envoi est prouvable).

### Événements / PvP / divers
- [v] `DuelDialog` (DuelRequestWnd natif + Msg visible + **défi live
  prouvé** : 135→451→136→453 des deux côtés) ·
  [v] `BattlefieldWindow` (BattlefieldListUI natif) ·
  [v] `HotelWindow` (HotelWnd natif + EnterBtn câblé) ·
  [v] `TerritoryWindow` (TerritoryMainWnd) ·
  [v] `ColosseumWindow` (ArenaMain) ·
  [v] `RecruitWindow` (Recruit) ·
  [v] `FastDressWindow` (DressRoomUI) ·
  [v] `RankWindow` (RankMainWnd) ·
  [v] `RollTreasureDialog` (RollUI + Need/Greed/Pass) ·
  [v] `DeathWindow` (ReviveMenuUI + types 0/1/5) ·
  [v] `AchievementWindow` ·
  [v] `DailyRewardWindow` (collecte live à prouver) ·
  [v] `DestinyWindow` · [v] `CoupleWindow` ·
  [v] `SystemServiceWindows` (5 fenêtres → layouts natifs via
  `BuildNativeWindow`). Round-trips live à prouver pour la plupart —
  captures de montage + EnsureDemo faites, pas le trafic réel.

### Dialogues
- [v] `NpcDialogueWindow` (prouvé talk/quest rituel).

## Bloc J — Analyses `re/` (tâche « analyser le client Reforged »)

- [v] Chargement charselect → jeu : flux natif documenté —
  `FUN_00cf53b0` charge toutes les DB `.ini` en une passe,
  `FUN_00b07450` charge la CZone séquentielle, options
  `PrecacheKFM`/`UseMappedFile`/`BelowNormalLoading` ; notre
  `MapLoader` (Task.Run + protos préchargés) déjà conforme →
  `docs/NATIVE-LOADING-ANALYSIS.md`.
- [v] Autres « choses mal refaites » : jump, interp mobs, échelle
  → Bloc C ; skins → Bloc G (masques FACES teintés).
- [v] Sortie : notes dans `docs/NATIVE-LOADING-ANALYSIS.md` ;
  corrections restantes = Bloc B (RAM textures) + warm-up tables
  au loading (optionnel).

## Bloc K — Validation & livraison

- [x] **Spec exhaustive des 82 ref-shots** → `docs/REF-SHOTS-SPEC.md`
  (chaque image décrite : style, layout, contenus, états — cahier des
  charges de la refonte totale). Corrigés à l'analyse : **barre
  ULTIMATE rétablie** (présente sur toutes les refs — supprimée par
  erreur en Bloc E, ré-alimentée par 2032 → `PlayerProfileState`) ;
  **spawn hors-terrain** (X/Y serveur hors grille → `SnapToGround`,
  preuve live test3 node2 372,293 : vide cyan → sur le terrain) ;
  **ColliderConsolidator off-tree** (GlobalTransform interdit hors
  arbre → chaîne de parents : 2178 erreurs → 0, 2177 shapes à la
  bonne place).
- [v] Matrice capture-par-capture → `docs/CAPTURE-MATRIX.md` : 5/5
  écrans vérifiés (`mx_*.png`), ~25 fenêtres montées sur layouts
  natifs, états live marqués honnêtement (non falsifiables listés).
- [v] Checklist fonctionnelle : drivers live existants — talk/rituel,
  shop, trade 2-clients, bank/neobank/familystorage, item mall,
  `--repair-test` (45→342 + 47→343), `--buff-test` (9→279→302→299),
  cast anim (C_Spell col3 → motion.ini),
  `--mail-test[=dest]` PJ↔PJ prouvé (GUI test123 → «test1» admin :
  95/7→399, 99→403, vérifié dans la boîte destinataire via
  `HeadlessBot mail-check` — #15 sujet limitless-150539, or=1),
  `--vending-test[=buy]` prouvé (vendeur 77→377[1][2] selling=True
  puis 275/280 à la vente ; acheteur 72→377+378 goods «QA Stall» →
  76→274, sac 50→51 item 38572, 75 close),
  `--duel-test` prouvé (GUI challenger test1 135→«[GM]…» handle
  0bebc201 → HeadlessBot `duel-accept` reçoit 451 «test1», émet
  136 accept → 453 DuelBegin des DEUX côtés + 454 résultat reçu
  côté GUI quand le bot part ; note : 452 non émis par le serveur
  sur accept). `--daily-test` prouvé de bout en bout : poll
  258 op=1 → 615 type=1 toutes les ~60 s (online 0→547 s), bit
  jour-1 levé à 600 s, claim 258 op=2 → **274 insert item** +
  615 type=2 confirmé. `--levelup-test` prouvé : `//set_level 31`
  → 600 exp=0 + **284 ClientLevelUp level=31**, restauration 30
  (pas de 286 — aucun sort de classe au niveau 31). Téléport/
  migration prouvée au passage (`//transport_node` + `//tele` +
  acks 264/266 dans chaque driver).
  Reste : roll loot (groupe+drop réel requis).
- ⚠️ Piège infra noté : `reverseden/eden_server/` = ANCIEN arbre
  serveur (u16, sans TLS/varint) — `start-native.sh` à lancer depuis
  `portage/eden_server/server/` (`bash ../start-native.sh`) sinon
  handshake TLS impossible (« Cannot determine the frame size »).
- [v] Mesures perf avant/après → `docs/PERF.md` (tex-postes par
  dossier, A/B compression).
- [v] Fuites RID/Objects à la sortie : mesurées (`--perf-seconds`
  sortie propre, solo S001) — ObjectDB 12 698 → **5 623** (−56 %)
  après libération des caches statiques à `_ExitTree`
  (`AssetCatalog`/`CharBodyCache`/`ToonMaterializer`/`WaterAnimator` —
  protos GLB `Free()`s, RefCounted déréférencés). Résidu : 2 090
  `GodotBody3D` (colliders de map — bruit d'ordre de teardown,
  transition de map QueueFree bien l'ancien root) + instances
  restantes à attribuer (`--verbose`).
- [v] `docs/plan-refonte_architecture-complet.md` + `hud-interfaces.md`
  états mis à jour au fur et à mesure (entrées v2 : buffs, repair,
  cast anim, codec 78, fuites).

## Historique v1 (repris du plan précédent)

- Perf/states au 2026-10-27 00:50 & 01:35 : ClampIntoView au restore,
  `CeguiToGodot.SkinUnstyled`, WorldMap `ZoneMapUI`+OptionUI traduite,
  Social sur `CommunityWnd`, Class sur `JobWnd_NA`, harness
  `game.tscn --solo --map=S001 --hud-open=… --shot=…` (~45 s).
- Prouvés live v1 : Talk NPC + rituel 285→11→531→285 ; achat shop
  (Pure Dew 245 g) ; trade 2 clients complet ; bank/neobank dépôt-
  retrait+coffre famille ; item mall (achat 40362, points 136→135) ;
  inventaire live (items/gold concordants).
- Reste v1 : onglets TabControl non rendus (Social), skin ItemList
  blanc résiduel, NCID 78 tronqué, fuites RID à la sortie.
- Stack serveur : `eden_server/start-native.sh` en chemin absolu ;
  PostgreSQL :5432 ; gateway 5560 / login 6543 / ticket 2067 /
  mission 7654 / world 5200+5301 / zone 5201.
- Captures rapides : `godot --path godot_project res://scenes/game.tscn
  -- --solo --map=S001 --hud-open=<clé> --shot=/tmp/x.png` ; live via
  flux écrans `--login=… --channel --char` (`game.tscn --live` direct
  stalle après l'herbe).
