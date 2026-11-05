# Exécution — retours propriétaire (suite de plan-retours-2026-11-03.md)

Date : 2026-11-04. Statuts : `task-contract.md`.

## État final

- **Build** : client + Core + serveur 0 erreur (1 warning préexistant
  SlowFree.cs). **Tests** : NetTests 904/904, Core 967/967, serveur
  1645/1645. Test perf `MountEmitter_ReskinCost_PerFrame` flaky sous
  charge seulement — vert isolément.
- **Graphe** régénéré (50 278 nœuds).

## Réalisé (4 chantiers + correctifs directs)

- **A statuts/profil** (agent) : refresh 296/492/493 au level-up perso
  (jamais émis — trou), gate instantané manquant sur `AddMobEnchantAsync`,
  299 factice 9101 → vrai statut session + RollBack, purge des enchants
  persistés devenus instantanés. Tests StatusSweep ajoutés.
- **B FX** (agent) : corps du joueur jamais construit en `--live`
  (`EnsureSelfVisual`) → chant `magic01`/gestes/pré-cast absents. Live
  prouvé : cercle doré `s11611` tenu pendant le cast, retiré au
  blast/interruption ; Cyclone `s10521`+`s10541` centré lanceur ;
  Tornade `s11722` ×1 sur cible. Flag diag retiré.
- **C+D mobs/montures/input** (agent, vérification) : swap
  `MountAnimFor` prouvé par sonde osseuse en espace rendu ; yeux r253
  skinnés à `Bip01 Head` (pas de défaut d'attache — reste matière/
  visibilité à confirmer live) ; overheads dynamiques vérifiés ;
  taille worldboss col 2 table-driven ; input-test live 5/5 PASS
  (LMB déplacement=caméra, RMB déplacement=déplacement).
- **E HUD/UI** (agent) : inspect 3 onglets prouvé (captures), faiblesses
  resserrées + survol élément seul, cases skillbar +5 px, graphe talents
  −13 %, StatDetailWindow schéma de repli + fix null-deref, NPC talk
  taille/gauche/sans ID vérifiés, archive armes complètes (18 feuilles),
  costumes par onglet collection (pas EquipLoc).
- **Toile d'araignée** (moi) : cause réelle = **blob `Decal`** projeté
  sur le propre modèle de l'entité (volume 3 m, `CullMask` par défaut =
  toutes couches) → ellipse noire sur le quad alpha-test. Fix : visuels
  d'entités couche 2 (`BlobShadow.ExemptVisuals` via `RelayoutOverhead`),
  `CullMask` du blob = toutes couches sauf 2. Idem capot champignoïde
  assombri. Capture `--blob` : toile translucide, ombre au sol OK.
- **Divers** : `UiTextEn` clés canonisées (5 entrées EN mortes ex.
  « Archive points: ») ; `GetPath()` hors-arbre dans `OverheadLayout`
  (spam `Cannot get path of node` au spawn) ; G29 gap noms t_monster
  consigné ; using manquants serveur + `#if GODOT` CollectionTable.

## Reste à confirmer EN JEU par le propriétaire

- Yeux r253 : géométrie correcte en sonde ; si défaut persistant →
  matière/visibilité (pas d'attache), à reproduire live.
- Skins d'état mobs (BP/HP/dégât) : **aucune donnée native** —
  non implémenté, seule la teinte BP existe (definitions.md).
- Enchants de travail 200+work_num : ids absents de c_enchant FR —
  masqués, pas de bouche-trou.
- `//set_level` GM : état portail resync à la migration suivante.
- StatDetail « Calcul » : attend un 32002 live pour s'afficher.

---

# Batch 2 — retours du 2026-11-04 (second passage)

## Corrigé / implémenté (build vert, tests verts)

- **Anims monture = mapping natif** (A→`move02`, D→`move03`, records
  9/10 → actions 102/103) sur les 2 copies de `DirectionalLocomotion`
  (`src/Eden.Core` + `scripts/game`). Le croisement « penché dans le
  strafe » a été réverté : sondes osseuses (rendu+fichier concordants)
  → `move02` = flanc droit, `move03` = gauche, même convention que les
  .kf perso (penché hors du strafe). Docs `mounts-visual-pipeline.md`
  corrigées. Tests : `Mount_SideAnims_NativeRecords` 37/37 verts.
- **Caméra** (`PlayerController`) : LMB = caméra seule partout (drag
  ou clic, arrêt ou déplacement, cap gelé `_lFreeze`) ; RMB = steer
  (`_model→_yaw`) + rattrapage (`FollowCamera` actif sous `_rDrag`,
  `_rFreeze` gèle `_moveYaw`). Input-test étendu 1b/1c.
- **Gate cast classe** (`SkillCast.Cast`) : refus local si
  `ClassMask(spellId)` exclut la classe courante (miroir
  `SpellAllowedForClass` serveur, C_Spell col 24).
- **NRE `NativeFxHost._Process`** : garde `Root` null/libéré.
- **Halo loot noir** (`EntityRegistry`) : `BlobShadow.ExemptVisuals`
  après `AddChild(halo)` — même famille de bug que la toile d'araignée.
- **Résidu FX vert de soin** : `s11811` boucle + floor 3 s ; durée
  dérivée de `hold` (SpellFx) → cercle tué à la fin du cast.
- **Level-up jaune** : `SpellFx.OnMisc` sur 284 `ZoneClientLevelUp` →
  `Spawn(LevelUpFx="s99211")` self ou entité broadcast (serveur déjà
  émet). **Key = hypothèse documentée** (famille s99*, fan sprays).
- **Inventaire/équipement** (agent) : rangée armure par archétype
  `c_class` (1→Heavy) + titre « Current Gear », durabilité hors
  compteur (`MaxStack>1`), loc 9 fantôme filtrée (3e épée), routage
  pet/sac, déséquipe total, drop = drag hors panel + NCID 6, aperçu
  Class News remplacé, « Chance : X % » Inspect, prefs PV/MP
  (`ValuesMode`/`ShowMax`/`Labels`, `SettingsWindow.PrefsVitals.cs`).
- **Serveur** (agent) : durées statut ÷10, patrouilles WorldBoss
  `PatrolId`+`IsNodeBlocked`, portée cast `col28×0.5+radii+2.5`,
  regen 3 s, coût MP, 284 broadcast aux témoins, lag paramètres
  (tabs paresseux, ~0 ms réouverture, 35-48 ms 1re ouverture).

## Skins d'état mobs — verdict preuve

- Mécanisme BP cassé natif : `FUN_0072ffe0` (client_game) bascule
  AppCulled des nœuds `KNOCK`/`KNOCKNODE` — déjà implémenté
  (`RemoteEntity.SetBroken`/`ApplyBrokenKnock`, attr 0x13 patience <1).
- Variant texture d'état : les `*5x.dds` sont des textures
  d'émetteurs de particules (`NiPSysMeshEmitter`/`SuperSpray*`),
  pilotées par les KF (`NiPSysEmitterCtlr`/`EmitterActive`) — parsées
  par `NifFile`, non jouées côté client (KfAnimation = os seulement,
  `NiParticleRuntime` = FX hosts uniquement). Aucun swap de texture
  corps par état dans le décompilé (reindex exhaustif).
- État : BP cassé = implémenté (KNOCK+hurt05+teinte) ; émetteurs d'état
  = données présentes, runtime non implémentée.

## Reste à confirmer en jeu (bloqué : /tmp/godot.lock, PID 359909)

- Ride-lab A/D monture (sens visuel), yeux r253 (« eye light » =
  mesh-émetteur PArray, skinning vérifié — repro live requis).
- Profil/inventaire/drop/Class News/Inspect/prefs PV-MP : captures.
- Input-test caméra live ; halo loot à angle variable ; cast FX
  famille Direction/Cross/Sector en vue directionnelle ; s99211 level-up.
- Lag post-kill en rendu réel (headless : pas de backlog mesuré).
- Perf flaky : `MountEmitter_ReskinCost_PerFrame` (seuil 500 µs,
  passe isolé, dépend de la charge machine).

## Session 2 (04:30-04:55) — verrou Godot libéré

### Validé par captures
- **Profil guerrier** : « Heavy Armor (Current Gear) » sur la rangée
  lourde (shot-profile.png) — les autres rangées affichent
  Robe/Armure Légère en parallèle. BUG « lourde→robe » = client stale.
- **Overhead** : « PV 13898/18006 » (label PV + actuel/max).
- **Anims monture (ride-lab 253010)** : mapping croisé A→move03 /
  D→move02 → penché dans le strafe des deux côtés (captures cam90/270,
  museau tourné côté strafe vu de face). FaceOffset ±90° correct.
- **Level-up** : s99211 était le feu d'artifice « 2011 » (nouvel an).
  Décompilé FUN_00713430 : flag0→s01651+UI034.wav (niveau perso),
  flag1→s01751+UI035.wav (niveau classe) → SpellFx recâblé.

### Corrigé en code
- **Caméra** : `_rFreeze` retiré (RMB=steer+rattrapage), clic gauche
  relâché = sélection/marche simple (fix « clic mob sans effet »).
- **Mob skins cassées à plein PV** : `PatienceSeen` — SetBroken n'est
  évalué qu'après réception d'une valeur de patience réelle ; à
  l'init patience=max + SetBroken(false).
- **Lag paramètres** : pré-chauffage de la fenêtre après le spawn
  (`SettingsApplier` warm) — l'ouverture à la demande mesurait 77 ms.
- **Lag post-kill** : SpellFxWarmup étendu aux protos GLB (le parse
  10-32 ms par FX se faisait à la 1re utilisation en combat) +
  NifScene parse déjà caché.
- **WorldBoss immobiles** : MobWander saute le wander aléatoire pour
  TOUTE entrée boss (pas seulement PatrolId>0) — vérifié
  `IsWorldBoss` couvre les 3 kinds ; murs `IsNodeBlocked` conservés.
- NetTests 915/918 (3 skips env), Core 967/967 (flaky perf isolé vert).

### Reste ouvert
- Cast anims inversées/90° : audit dédié non fait cette session.
- Statut portal-stone affiché côté client : serveur envoie
  l'enchant 55869 (durée -2, lvl≤80) — affichage liste à vérifier.
- Résistances détaillées par source dans le profil : à faire.
- « Run » kiting + position updates 10 Hz : livré côté agent précédent,
  pas re-validé live.
- Drops fond noir : fix blob-shadow livré précédemment — à recapturer.
