# Plan d'action global — Eden Portage

Synthèse de 7 investigations (2026-10-29). Chaque bloc = état réel du
code, gaps, actions ordonnées, validation. Sources : `knowledge/` pour
les données de jeu, `re/` pour les preuves natives (adresses citées).

**⚠️ Périmètre : Eden de base, contenu lvl 90 max. Rien au-dessus.**

**Ordre de priorité (demande 2026-10-29) :**
1. **Panneau classes** — PRIORITAIRE (section 1)
2. **Combat** — approche/cast, animations, sorts/buffs, stats/procs,
   types d'entités (section 2)
3. **Reste** — correctifs rapides + retours utilisateur divers
   (section 3)

**Règles transverses (must have) :**
- **FR + EN obligatoire** pour CHAQUE tâche/feature — détail dans
  `AGENTS.md` § Localisation (littéraux via `UiTextEn.Translate` +
  `ui_text_en_fr.tsv`, data via `lang/fr/` + repli EN).
- **Pour CHAQUE NCID et CHAQUE tâche** : moissonner d'abord
  `knowledge/` — c'est-à-dire les **« sites web Eden »** (wikis EN/FR
  convertis, `knowledge/wiki-en`, `knowledge/wiki-fr`, `guides-fr`) ET
  les **« xlsx/csv »** (classeur Vendetta, `knowledge/wiki-tables`,
  CSV dans `knowledge/raw/`) — plus `ref-shots/` et le corpus `re/`
  pour les preuves natives. Ne jamais coder un système « de tête ».
- **Tout ce qui touche les classes — et pas seulement — DOIT être
  revu et recalé selon les connaissances de `knowledge/` et
  `ref-shots/`** (priorité absolue : panneau classes, éveil, talents,
  certificats, expertise/stats par tranche de niveau, buffs de race,
  skills hover). La connaissance extraite fait foi face à tout ce qui
  existe déjà dans le code — ne pas hésiter à reprendre l'existant
  quand il s'en écarte.

---

## État réel au 2026-10-29 (fin de session d'exécution) — rien n'est coché

Aucune case n'est cochée : validation = l'utilisateur (`.claude/COMMON_MISTAKES.md`).
Légende : PROUVÉ = capture Flameshot / DB / log lu ; CODÉ = compile + tests, non vu en jeu ; OUVERT = non traité ou non reproduit.
Détails + scénarios : `.claude/completions/2026-10-29-*.md`. Captures : `eden_godot/godot-shots/`.

| Sujet | État | Commits | Reste / limite |
|---|---|---|---|
| 1 Panneau Classes (contraste, éveils, certificats, header, News, expertise) | PROUVÉ | 709ace3, d40eb2b | « skill général par race » absent des données ; sorts raciaux 45/60 non rebranchés ; aperçu perso/tenues/compteurs hebdo inertes ; icônes des éveils à valider par l'utilisateur |
| 3.2.9 / 3.3.0 / 3.3.3 / 2.5 / 2.6 HUD (barres PV, Préférences, floaters, pseudo, couleurs mobs) | PROUVÉ (jaune clair des mobs non vu) | 6f438fd, e66f437 | TargetFrame sur PNJ non prouvé |
| 3.2.5 statuts serveur | CODÉ | 235e9a0 | Zone redémarrée depuis, non rejoué en jeu |
| 3.2.2 minimap/carte | PROUVÉ (position) ; 270 ResetCoord CODÉ | b7c8388, 86242c7 | fix 270 non rejoué |
| 3.2.8 T-pose profil | PROUVÉ | 321db11 | « coupure verticale » non reproduite, aucun correctif : besoin d'un angle/capture utilisateur |
| 3.2.7 angle de port des armes | PROUVÉ (planche VisTest + jeu) | 321db11 | pas de ref-shot avec épée ; arc (Prop2) = hypothèse |
| 3.2.7 murs invisibles (.smp) | PROUVÉ (wall-1.png, log [nodemap]) | 2e504d5, f4e58c9 | serveur ne valide pas contre .smp ; eau non marquée |
| Skins d'armes (variantes W40106…) | PROUVÉ (weapon-variants.png) | b69f8b9 | — |
| 3.3.2 décor noir/rouge, orientation/hauteur PNJ | CODÉ | b740d8e | aucune capture avant/après probante ; prop fautif de 298,68 non identifié |
| Entités fixes (œufs, coffres, machines) mauvais modèle/position | OUVERT | — | non reproduit ; besoin d'une quête précise pour comparer à l'ini |
| 2.2 verrou de cast après combat/TP | PROUVÉ (fx-locktest.png) | f16738d | serveur n'a pas renvoyé de 410 pendant le test |
| 2.2 particules démesurées | CODÉ | f16738d | glb d'effets à régénérer (convert.py) ; non vu |
| Écran blanc à la mort d'un mob | OUVERT | — | non reproduit |
| 3.2.1 joueurs invisibles entre eux | PROUVÉ (multi-7.png, bot) | 4ef7775 | modèle distant lu en réduit |
| `//go` / `//transport_node` (Positions[0]) | CODÉ | 4ef7775 | falaise S002 non reproduite |
| 3.3.4 sauvegarde à la sortie + EXP au relog | PROUVÉ (DB + captures) | 8130d83 | — |
| 3.2.3 équipement (swap, dual→2H, sac plein) | PROUVÉ | eb6a111 | texte 0x4f9 EN non vu |
| 3.2.4 boîtes (kind 48) | PROUVÉ | cf95b0f | coffres kind 44 et lignes « monnaie » non traités ; NCID natif d'ouverture non retrouvé |
| 3.3.1 config serveur à chaud (`eden_server/server.config.txt`) | CODÉ (tests + log) | 54b76e8 | pas vu un kill donner plus d'exp/drop ; MoveSpeedBasePct sans effet côté client (×3 local) ; événements = bonus seulement |
| Login / Ch01 / sélection persos | PROUVÉ (after-*.png) | 775a6c9, f7a70a3, b7c4bfb | pas de comparaison pixel à pixel avec ref-shots ; avatars attachés ~7,6–8 s (≈2 s d'habillage thread principal non optimisé) ; coût à froid 5,7 s à la 1re session |
| Tests | client NetTests 435/435, serveur 1246/1246 | — | — |

### Lot 2 (même jour) — voir `.claude/completions/2026-10-29-correctifs-lot2.md`
Vitesse validée serveur, « appris » limité aux vrais gains, certificats lv/10+2, sorts raciaux 45/60 (race déduite des ids), dernier perso auto, jeter un item, noms multi-lignes (tracker/TargetFrame), fenêtres d'expertise séparées, talents (verrous, X/max, clic droit), système BP (jauge + gating des drops BP ; faiblesses dérivées, pas de barre client, drop par classe non mappé), cuisson du décal de texture (opt-in, non relancée). Tout CODÉ, rien PROUVÉ en jeu.

### Scénarios de test utilisateur (à jouer, puis cocher soi-même)
1. Classes (K) : ouvrir, lire le contraste, éveils hors liste, certificats, tooltip d'expertise.
2. Combat : tuer un mob avec un sort, relancer un sort aussitôt ; `//tele` en plein cast puis relancer ; observer taille des effets (sort 59280) et l'écran à la mort du mob.
3. Armes : équiper plusieurs variantes d'une même arme (ex. W401 01/03/05/06) ; ouvrir le profil (pas de T-pose) ; regarder l'angle en main.
4. Murs : S002, `//tele 330 250`, marcher vers l'est : glissement contre les obstacles, pas de traversée.
5. Multi : deux clients sur la même map, l'un reste immobile après `//tele` : l'autre doit le voir.
6. Sauvegarde : se déplacer + gagner de l'EXP, arrêter la Zone (SIGTERM), relancer : position/niveau/EXP conservés.
7. Équipement : swap d'armure, 2 armes 1-main puis une 2-mains, sac plein ; ouvrir une boîte (clic droit).
8. Config : éditer `eden_server/server.config.txt` (DropRate, ExpRate, Event.*) sans redémarrer, tuer des mobs.
9. Pré-jeu : login (fond fixe), Ch01, sélection (tous les persos visibles) ; chronométrer l'arrivée des avatars à la 2e session.
10. Décor : S010/S011/S013/S052 (UB228, UB229, UD607) sans meshes noirs ; PNJ bien orientés, non flottants.

### Bloqueurs d'infra rencontrés
- `/tmp/eden-godot.lock` pouvait être tenu par un démon Zone (fd hérité) : lancer les serveurs avec `3>&-`.
- Plusieurs agents sur un seul client Godot → captures parasites possibles.

---

## 1. Panneau Classes (touche K) — PRIORITÉ 1

> **Rappel fort : tout ce qui touche les classes doit être revu et
> recalé selon `knowledge/` + `ref-shots/`** (et au-delà : toute tâche
> doit croiser ces sources — règle transverse ci-dessus). L'existant
> n'est pas un oracle : quand il s'écarte des captures/tables, on le
> reprend.

### 1.1 Fidélité UI (ref-shots-catalog.md §Classes)
-  Tooltip sort enrichi : nom, Niv., « Cost: N MP » (base col32 ×
  coef SpellRef du palier), cast/cooldown, description T_Enchant/T_Spell
  multi-lignes (gabarits `#N#` résolus, `#B$n$v#` mis à l'échelle par
  palier), « Next level » rouge + stats nv. suivant, coût montée rouge
  — prouvé en jeu (Coupe Sonique Lv4). `#18#` armes = masqué
  (`shortcut:` encodage non décodé).
-  Tooltip talent custom : nom, Max Level, effet + saveur, prérequis
  non remplis en rouge, coût 1 KP rouge — prouvé (Art Of Throwing).
-  « KPs Remain/Entered » : boîte grise + texte sombre (conforme
  ref-shot — pas rouge/vert comme supposé avant).
-  Certificats : grille fleur-de-lis native (`EeSkin.EmptySlot`)
  en 2+3+2 + 2 gemmes `LicenseCombo/NoCombo` aux jonctions — layout
  extrait de `JobCurrentWnd.layout` (7 ComboIcon + 2 SecretIcon,
  pas 3×4 : le 489 ne porte que lic_1..lic_7). Prouvé en jeu.
-  Placement : drag depuis la liste possédée → slot actif (NCID
  182 `licId,slot,classId`), clic droit slot occupé → retrait ;
  écho 509 **partiel** mergé par licId+loc (remplaçait toute la
  liste avant — « Certificats : 1 »). Prouvé en jeu.
-  Arbre talents : connecteur « → » affiché entre un slot et son
  prérequis (colonne précédente) — prouvé en jeu.
- Header : 3 grandes icônes de classe ; bandeau switch avec dragon animé.
- « Class News » = flux d'annonces (clarifier vs fiche info actuelle via
  `re/ghidra/rf_game`).
  
Manquement a corriger :
- les classes éveils sont encore dans la liste des classes normal qu'on peut scroll via le curseur.
- le panel est pas exactement comme l'officiel y'a encore des soucis de choix de couleur provoquant un contraste ILLISIBLE (blanc sur blanc, noir sur noir..).

### 1.2 Classes verrouillées —  fait
-  Tooltip classe sur toute la ligne (`ItemTooltip.BindCustom(row)`)
  : nom, description T_Class multi-lignes, « Condition requise : »
  rouge (ex. Chevalier → « *Chevalier du Chaos niveau 20 ») — prouvé
  en jeu (scroll liste + survol Chevalier verrouillée).
-  Clic sur classe verrouillée ignoré (`_selected` inchangé) —
  prouvé (clic sur ligne verrouillée, panneau reste sur Voleur).
-  Lignes verrouillées affichées même si `CurrentClass` inconnu :
  race déduite du 489 (préfixe ×100) au lieu de `CurrentClass/100`.
- Prérequis 21+ : fallback « {Parent} Level 80 » déjà présent ; quête
  d'éveil via `ClassDataParser.ParseClassPrerequisite` reste à
  confirmer côté rendu.

  

### 1.3 Classes éveil (21–61) — noms anglais trouvés (ClassDataParser + overrides)
Berserker, Shielder, Arch-Elementalist, Gravity Manipulator, Trainer,
Devil Hunter, Adjudicator, Equilibrian, Assassin, Kage, Battlefield
Poet, Life Worshipper, Executioner, Glacier Knight, Mecha Ares,
Annihilator, Asura, Rakshasa, Demon Summoner, Chronologist, Blade
Acrobat, Elegant Dancer, Druid, Archimage, Blood Knight, Paladin,
Lethal Arrow, Celestial Arrow, Demon Tamer, Conjurer, Blade Master,
Master Fencer, Holy Lamentor, Litigator, Dragon Soul, Dragon Emperor,
Darkness Blade, Holy Blade, Inquisitor, Reaver (+id 61 à nommer via
c_class Reforged).
-  Client : borne `<race*100+62` (inclut 21–61) — prouvé en jeu
  (Samouraï, Sage, Chevalier Dragon, Le Messie listés verrouillés).
-  Noms FR résolus : `t_class.ini` FR couvre 21–61 (Barbare,
  Muraille, Archélémentaliste, Astromagicien…) — aucune table à
  ajouter côté client.
- Serveur : `AccessibleClassIds={1..5,19}` → élargir aux possédées ;
  octroi via quêtes d'éveil (TC_AddClass) ou GM (`Admin.cs:326`).
- Talents éveil déjà dans S_Talent (branches 211-613).


### 1.4 Lag à l'ouverture (cause racine identifiée)
Premier open = parses INI synchrones thread principal : c_class,
c_talent, **c_enchant ~25k**, **c_spell ~60k**, **c_item ~9 Mo** +
t_item, +61 textures d'icônes. Chaque `Refresh()` reconstruit ~300 nœuds
et est appelé sur 6 événements.
1.  Warm-up des tables pendant l'écran de chargement — `DataWarmup.Run`
   en `Task.Run` depuis `FinishWorldLoad` (T_Class, T_Spell, t_item,
   c_talent via `ClassWindow.WarmTalents`, c_spell via `ClassSpells`,
   SpellRefTable, EnchantTable, `ClassIcon` → c_class + uiicon).
   `SpellCatalog.EnsureLoaded` verrouillé + publication atomique
   (`_icons` en dernier). Prouvé : `[skill] C_Spell : 4612 icônes`
   logué pendant le chargement, panneau ouvert sans erreur ;
2.  cache icônes classe — `CharSelectScreen.ClassIcon` cachait déjà
   toutes les icônes au premier appel, désormais pré-chauffé ;
3.  `Refresh()` déjà ciblé par événement (GoldChanged → bannières,
   Skills.Changed → sorts, ClassChanged → actif ; le 489 et
   l'ouverture gardent un refresh complet, nécessaire) ;
4.  pages license/news construites à la 1re visite via `SelectTab`
   — prouvé : `--class-tab=1` affiche la grille certificats sans
   l'avoir construite au montage du panneau.

### 1.5 Classes inactives en lecture seule —  fait
- Déjà : Lv réel, arbre talents, points KP (489). Interdit d'investir ✓.
-  Vrais spell-levels des classes non actives : `player_spells`
  n'est pas filtré par classe (requête serveur sans class_id) — le
  client possédait déjà tous les niveaux ; `learned` forcé à `false`
  masquait le niveau réel. `SkillCell` distingue désormais « appris »
  (icône + « Lv.N » + tooltip) et « investissable » (boutons +1/Max,
  drag) — classe inactive = lecture seule, aucun paquet requis.
  Prouvé en jeu : Voleur inactive → « Torgnole Niv.8 », « Coupe
  Sonique Niv.4 », « Confinement Mortel Niv.30 », non-appris →
  « Non apprise » grisé, zéro bouton.
-  Les 7 licences (`U1..U7` du 489) affichées : grille fleur-de-lis
  2+3+2 + gemmes par classe sélectionnée ; classe active via le 509
  (locs 1..7). Prouvé en jeu (1.1).
  Ne doit pas pouvoir cliquer et afficher une classes non possédé, par contre on doit pouvoir voir que la classe existe dans la liste des classes (comme actuellement) avec en hover les conditions d'obtentions.  (fait — 1.2, prouvé en jeu)

### 1.6 Classes éveil : emplacement UI (2 cases dédiées) —  fait
-  2 cases dédiées entre le portrait et « Activate » : paire éveil
  de la classe affichée (base locale b→2b+19/2b+20, vérifié vs
  prérequis T_Class EN ; 59 solo, 60/61 paire Warrior tardive).
  Verrouillé → icône atténuée + 🔒 + tooltip classe (desc FR +
  conditions) ; possédé → clic = sélection de la classe.
- Prouvé en jeu : Voleur → Assassin + Kageryu verrouillées, tooltip
  « Assassin — Tapis dans l'ombre… ».
- Les classes éveil restent aussi listées dans la liste normale
  (liste native = toutes les classes).
  
  Manquement a corriger :
- Il manque dans les cases de classes éveil pas mal d'image/logo de représentant la classe

### 1.7 Refonte complète UI Class Talent + Certificates
Reprendre 1.1 jusqu'au pixel-faithful ref-shots (grille certificats
3×4 fleur-de-lis, arbre talents avec connecteurs, tooltips sorts
enrichis MP/CD/description), système de combo de certificat (carré de vert = 16% stats p-atk et m-atk arme a 2 mains etc), voir les dossier knowledge et ref-shots. À consolider avec 1.1, pas en doublon.

Combo certificats —  **fait, prouvé en jeu + wire** :
- Client `EquipSetTable` (port de `EquipSetPool`, C_EquipSet.ini,
  sets 79-93 = 5 branches × tailles 2/3/4, priorité = ordre fichier).
- `ClassWindow` : après chaque 182, `BestCombo` sur chaque moitié
  (slots 1-4 / 4-7) → carrés verts sur les membres + gemme allumée
  + tooltip bonus (enchant de l'item récompense) ; envoi 183
  `count+ids+flag moitié` (dédup par clé combo+classe).
- Prouvé runtime (test1, 4 certs Mêlée/Défense) : 2-cert → 183
  `b7000300028203820000`, 3-cert → `b7000400018202820382000` ;
  réponses serveur 280 (enchant swap) + 296/492/493 (stat refresh)
  après chaque 183 = combo appliqué via `InvokeSpellStonesSet`
  (déjà couvert par `LicenseTests` serveur).
- Tooltips certificats : nom T_Item + bonus de l'enchant lié en vert
  (T_Enchant FR) + saveur, sur les slots posés, la liste possédée et
  la gemme allumée — prouvé en jeu (Double Attaque, +10% Torgnole).
- Reste : fidélité pixel résiduelle.

Manquement a corriger :
- le visuel pue la merde et ressemble a 1% au positionnement réellement pour chaque cases, il manque quasiement tous les éléments visuel pour clarifier tout et pour que ce soit beau.

### 1.8 Système classes+stats complet (moisson knowledge obligatoire)
`knowledge/` contient des infos **primordiales** pour recoder tout le
système : buffs de race selon les classes, 1 skill général unique par
race, bonus de stats « expertise » par tranche de niveau pour chaque
classe (éveil et non-éveil), images détaillées. → lancer **plusieurs
subagents** pour extraire l'intégralité (classes, skills, hover-info
éveil dans `ref-shots/` aussi) avant toute implémentation.

---

## 2. Combat — PRIORITÉ 2

### 2.1 Approche + cast à portée de l'attaque ou skill —  fait
- Natif : **le client connaît la portée et poursuit la cible avant
  d'envoyer le NCID 9** — aucun gate distance serveur (preuve dans
  `Combat.Spells.cs:142-152`). Borne défensive serveur >60 u.
-  `SkillCast` : `BeyondReach` (col28 ×1,5, repli 4 u) → poursuite
  `_chaseSpell/_chaseTarget` (`_PhysicsProcess`, re-ciblage 0,3 s) ;
  à portée → `ClearMoveTarget` + `FaceTowards` + envoi 9. Annulé par
  `Cancelled`, despawn, `NoMove`/`NoAnySkill` ; `CastingSpell` posé
  uniquement à l'émission. Auto-attack et poursuite coexistent sans
  exclusion (même point cible).
- Prouvé live (`--cast-no-engage`, cible la plus lointaine) :
  `poursuite cast #50017` → course → 9 `090061c38b80ffff` → 279 OK
  → 281 impact ; les casts suivants partent direct (déjà à portée).
- Étapes :
  1. `SpellCatalog.Range` = `C_Spell col28` (`IniTable.GetInt(row,28)`),
     distance d'approche = `Range×1,5` (même facteur que `AttackRange`
     serveur) ; `Range==0` → repli portée arme (4 u).
  2. `SkillCast` : état pending `_castChase/_chaseSpell/_chaseRange` ;
     dans `_PhysicsProcess` (pattern `TreasurePickup`) :
     dist > portée → `SetMoveTarget(cible)` toutes les ~0,3 s ;
     sinon `ClearMoveTarget` + `FaceTowards` + envoi du 9.
     Annulation : `PlayerController.Cancelled` (WASD/clic sol), despawn,
     `NoMove`. **Ne pas poser `CastingSpell` avant l'envoi.**
  3. Clic droit ennemi : `AutoAttack.Engage` fait déjà l'approche mêlée
     (OK) — vérifier que cast-skill pending et auto-attack s'excluent.
- Vérif : caster hors de portée → le perso court puis lance ; buff +
  ennemi → self-cast (3.1). (R6)

### 2.2 Animations de cast de skill —  fait
- Mapping natif : `C_Spell` col3 = KF → `motion.ini` (`MotionTable.
  Lookup(stem, kf)`, stem KFM du corps `CharModelSpec.BodyStem`) —
  ex. kf 405 → `shoot05`, 3001 → `shoot23`.
- `SkillCast.OnCastOk` (279) : `PlayOnce(anim)` ; cast temporisé →
  `_castAnim` rejouée jusqu'à résolution (effacée par 281/287/410).
- Cause racine de l'absence : `AnimRetarget.CharWanted` ne listait
  pas `shootNN`/`magic02+`/`battle02+` non montés → filtrés des
  `.slim.glb` (préférés par `EntityAnimator`). Liste élargie +
  `tools/anim/trim_anim_glb.py` aligné, slims régénérés (296 anims).
- Prouvé live (`--cast-spell=59280`, castDs=10) :
  `cur='shoot05' playing=True` pos 0,07→0,44/0,70 s, tracks=449 —
  la gestuelle tient pendant la barre de cast.

Manquement a corriger :
- Quand on tue un mob on voit plus rien a part du blanc comme si on était tp, on est glitché quelque part.
- Les animations mob/joueur marche mais c'est ultra bug, visuellement sur l'écran on voit des trucs qui sont pas animé "simplement" (des grand particules d'épée etc pour un sort basique)
- Parfois après un combat ou un tp etc, on peut juste pas lancer de sorts

### 2.3 Système de sorts complet (serveur-majoritaire)

#### 2.3.0 Reverse complémentaire (prérequis — corps absents de decompiled.c)
`objdump -d` sur `server_us/zone` : `CEnchantMgr::Add/Reset`
(0x8569a50/8c10 — sémantique re-add/stack), `ILifeEntity::AddEnchant`
(0x847ea90), `FindByType`/`RemoveReplaceableSpell` (0x847b220/f2a0 —
stacking par groupe), `ISpell::OnCast/CastTest`, `CEnchantMgr::OnAttack-
Enchant` (0x856a320), `CEC_MultiStroke::Init/Execute/CanExecute`
(0x8545f20/6210/5e90 — pour 2.4), `CEC_EnchantCancel` (0x852c470).

#### 2.3.1 Registre d'enchants sur mobs —  fait
-  `LiveMob.Enchants` (effectId → caster/rest/level/stacks/HpPerTick/
  LockFlags/Hidden) + `MoveLocked`/`AttackLocked` + `AllMobs` ; purge à
  mort/respawn ; jamais persisté.
-  `ApplySpellResultCommandsAsync(session, st, spellId, def, mob, …)`
  aux 7 call sites : `def.Value != -1 && !IsInstantOnly` →
  `AddMobEnchantAsync` (hidden si Value==-9, rest = Value+attr9).
-  Wire : 299/300 broadcast à tous les voyants (CanSee — émetteur
  inclus) ; NCID 18 → 301 rempli pour mobs (`mob.Enchants`) et joueurs
  (`st.Statuses`), hidden exclus.
-  Locks 2055 : `MoveLocked` bloque approche+wander, `AttackLocked`
  bloque frappes/sorts (canRiposte).
-  DoT 3002 : tick SlowTick 1 s, attr 0 broadcast aux voyants, peut
  tuer via `FinishMobDeathAsync` (attribution castreur).
-  Purge mort : 300 × visibles + Clear (capture native).
-  Dispel mob : `MobEnchantDispelHandler` {1821,1822,1823,1824} via
  `EnchantDispel.Pick` + `ISpellEffectHost.RemoveMobEnchantAsync` ; 1822
  par plage de type (col38) + grade (col39).
-  Tests : 1229 verts (dispel mob 1821/1822/1823 inclus).
-  **Prouvé live** : 50025→52025 `2055|1` sur mob → 299 rest=60 →
  **icône debuff visible** sur le cadre cible (flameshot) ; **zéro 326**
  pendant le root (mob en mouvement avant) ; DoT 58447 −500/s → mort
  DoT seule (305, tueur=castreur) ; purge 300 ; 52218 dur=−9 enregistré
  **sans** 299 (icône cachée natif). `--cast-near` ajouté au pilote.

#### 2.3.2 Cast temporisé complet —  fait et prouvé live
- NCID 17/59 : `CastTimeMs` honoré — 279 castDs + 302 + résolution
  schedulée (live : `279 castDs=20` → `288` ~2 s ; refus si cast en
  cours, 410/287 plutôt que lock silencieux).
- Mobs : `sdef.CastTimeDs` honoré — `CastingUntil` gèle le mob pendant
  le wind-up, blast schedulé gardé par `IsCombatLoopCurrent` +
  `!AttackLocked` (mort/deaggro/stun annulent). Live clone 58592 :
  `495` geste → `289 51021` **+2,1 s** (×2). Le 279 « mob » spéculatif
  au joueur victime est supprimé (un mob n'a pas de session natif) —
  bug connexe corrigé : `OnCastOk` exige `isSelfCast`.
- `ResolveCastAsync`/location/nowhere : garde `PlayerHp > 0 && !Dead
  && Migrated` ; `PlayerDeathAsync` → `BreakCastAsync` (287 + cancel +
  release flag) ; migration → `CastCts.Cancel` existant.
- Client : `CastBar` reste visible jusqu'au 281/287/410 (garde-fou
  +3 s) — capture flameshot « Guérison 0,7 s » ; mort → `CancelCast`.
- Bug pré-existant corrigé en passant : le tick cast lisait les defs
  brutes `Live.MonsterDefs` (`SpellIds` vide — aucun mob ne castait) ;
  il lit désormais `Monsters.GetDefinition` (enrichi ResolveRefs).
- Pilote : `--cast-nowhere`, `--cast-break=<ms>`, sélection du clone
  par `EntityId`. Cast-mobile assumé (mouvement ≠ interruption).

#### 2.3.3 Commandes résultat manquantes (par priorité — AUDIT-EFFETS-SORTS)
Dispels dans `ApplySpellResultCommandsAsync` (incl. `Value==-1` via
`ExecuteInstantEnchantAsync` sur la cible) ; `1822 EnchantCancel` ;
OnAttackBuff*/procs (→ 2.4) ; `3101-3103` checks ; `2119/2121` hate ;
`2137` level-check ; `7003 Effect2Target` ; `1997/1999` ;
`1913 PlayEffect` ; `2052/2037` pets ; `2945 SpellToSpell` ; `1076`.
-  Batch 1 : `7003 Effect2Target` (modes 1-5, cercle rayon×0,5,
  multi-cibles live prouvé : 2 mobs enchantés + 299 fil), `3101-3103`
  checks no-op natifs, `1822` voie joueur.
-  Batch 2 : `2119/2121 AttrHateHot` (hate[lanceur] ± arg0),
  `2137` check no-op.
-  Batch 3 : `2047 HealPlus` (types 4/5 flat sur le lanceur),
  `1913 PlayEffect` (383 broadcast), `1999 SpellAttack` (**live** :
  cast 51546 → clone recaste 51547, 495+289 fil), `1807
  DistanceSummon` (mob téléporté au contact), no-op registre/
  formule `1997/2033/2034/2055/3001/3002`. **Différés documentés**
  (AUDIT) : `2037/2052` pets, `2945`, `2152`, `1076` (Mission RPC).
  Temp-plan `eden_server/docs/temp-plans/2026-10-29-commandes-resultat-7003.md`.

#### 2.3.4 Stacking avancé —  fait (sauf RemoveReplaceableSpell différé)
-  `EnchantService.TypeAddAllowed` (FindByType de `CEnchantMgr::Add`
  @0x08569a50) câblé sur les deux voies : refus si un enchant même-type
  (col38) de grade supérieur (col39) — ou égal à niveau supérieur — est
  actif ; sinon les plus faibles sont remplacés (300). Joueur → index
  0x5b9 au rejet.
- ⏸ `RemoveReplaceableSpell` différé : registre `replaceable[0..4]`
  (Reg2Caster bits 0xf80000 de CEnchant+0x28) + flags de sort 0x3e00000
  — flag-bits apply non portés dans le modèle, colonne donnée floue.
- Vérif live : `self_use_effect` 59319(g1)→40631(g2)→59319 = 299 →
  300+299 → 316 index 0x5b9. Suite 1239/1239.

### 2.4 Stats, bonus/malus, multi-hit (dépend de 2.3.1)

#### 2.4.1 Fix mapping MultiStroke —  fait
-  `Procs.cs` lit `Arg(0..3)` = `[chance1, facteur1, chance2,
  facteur2]` (corrélé tooltips ~150 lignes : `2031|15|50` = « 15 %
  double ») ; break-on-fail séquentiel, 2 coups max, dégât =
  `hitDamage × facteur%`, chance/facteur × stack (CEnchant+0x30).
-  Rolls séquentiels confirmés par `CEC_MultiStroke::Execute`
  @0x8546210 (objdump — échec → break, émission 292 par coup).

#### 2.4.2 Sources de procs indépendantes —  fait
-  `EnchantService.GetAttackCommands` : blocs non référencés par
  col40 filtrés {1031,2031,2032} → MultiStroke passifs résolus (78 %
  des enchants 2031 n'ont pas de trigger — avant : jamais roulés).
-  Sources = équipement (useEnchant) ∪ `st.Statuses`, dédup par
  effectId, ordre équipement puis AddedAt — chaque commande = roll
  indépendant, un 292 par source taggé de son enchantId.
-  `InstantOnlyCommands` sans 2031/2032 → status persistant (le
  natif crée un CEnchant pour toute commande gérée).
-  **Prouvé live** : `//self_use_effect 48149` (2031|100|100|100|100)
  → status actif → 2 `MultiStroke +143` (facteur 100 % × coup 143) →
  2 × NCID 292 sur le fil ; anti-récursion OK. `--zone-cmd=` ajouté
  au client (pilote GM générique).
-  Ratios modifiables : facteurs lus depuis S_Enchant args (données).

#### 2.4.3 Chemins de hit + registre mob —  fait (miss + JcJ différés)
-  Sorts par victime : procs « attaque » roulés par victime dans les
  4 chemins de dégât (primaire, splash, rayon, dash-AoE — miss exclu).
-  ToOpponent appliqué au registre mob (`ApplyProcBuffAsync` →
  `AddMobEnchantAsync`). **Live** : `self_use_effect 10061 120` →
  mêlée → proc evt2 → `add mob enchant=61280` + 299 `60EF` sur le fil.
-  BeKill (events 8/9) sur mort joueur. **Live** : `10042` → mort →
  `add mob=0xffff8448 enchant=10045`.
-  Procs mob attaquant (`RunMobAttackProcsAsync` — MultiStroke
  joueur→292 `[mob][player]`, 1921/attrs self=mob/opponent=joueur).
- ⏸ Miss : `commands_miss` natif découpé par `DecodeEffectCommand`
  (FAILED — objdump requis) ; colonne non exposée par le parseur.
- ⏸ JcJ : `Versus.cs` n'a pas de pipeline de hit (invitations
  seulement) — rien à brancher pour l'instant.

#### 2.4.4 Commandes stats manquantes utiles —  fait (cast-rates différés)
Crits par sort `2096-2099` : les kinds 3-6 étaient stockés mais jamais
consommés — `ResolveSpell` prend désormais `GetSpellAttrModify` par
canal (phys→3/4, magic→5/6 ; preuve `CalculateMagicCriticalRate`
@0x83f5d00 → `GetSpellAttrModify(atk,5,id)`). Bug live trouvé à la
preuve : le guard `> short.MaxValue` excluait TOUS les sorts >32767 —
corrigé en `ushort.MaxValue`. Preuve live `49445` (+100 crit sur
50827) : avant fix hits 140-174 flags=1 ; après fix `281` flags
`0x200001` dmg 297 = crit garanti. `2059/2061/2067` cast success/fail
→ champs entité natifs +0xa8-0xae/+0xc6 non modélisés → différés.
`2141` CD, `2143` portée, `2150` rayon, `3101-3103` : déjà couverts
(kinds 0-2 + no-ops). Test : `SpellHitFlagsOf` miss/hit/crit/restore
+ constantes natives 0x200/0x200000.

#### 2.4.5 Dispel directionnel —  fait
Joueur (1821/1823/1824, type1=buffs type2=debuffs) **et** registre mob
(`MobEnchantDispelHandler` : 1821 tirage type/debuff, 1822 EnchantCancel
type c38∈[a0,a1] + grade≤a2, 1823 liste d'ids — `RemoveMobEnchantAsync`
sur `mob.Enchants`). Tests `MobDispel_1821/1822`.

Vérif : tests RNG seedé (indépendance des sources, mapping args) +
live : buff double-hit visible en coups répétés (Flameshot). (R2)

### 2.5 Types d'entité —  gate ennemi prouvé (flag mob « pacifique » différé)
Rafiner le fait que certaines entités ont des « types » : mobs, elites,
boss, NPC, entités spéciales « neutres » (scènes animées lore/histoire),
ennemis (mode duel, sessions PvP — ile sakura…). Certains sorts doivent
s'appliquer ou non selon le type (un joueur peut être allié, ennemi ou
neutre selon le moment — un ennemi à l'instant T ne reçoit jamais de
buff sur ce même instant).

-  `IsPlayerEnemy` (port char→char de `CCharacter::IsMyEnemy`
  @0x840e490) : duel mutuel (`DuelActive`+`DuelOpponentId` croisés),
  camps opposés territoire (`TerritoryId` commun + `TerritoryCamp`
  différent), contexte commun versus/battlefield/crusade/BR. Gate dans
  `TryMeleePlayerAsync` : non-ennemi → 410 `Field1=5`, cible morte →
  refus aussi (natif `param+0x3a&1`).
-  Côté ennemi de `7003 Effect2Target` touche aussi les **joueurs
  hostiles** (`PlayersNear(foes:true)` — cercle centré lanceur/cible).
-  **Preuve live 2 clients** (`--duel-test` + `--pvp-attack`, accepteur
  `--headless` — 2 fenêtres GPU se gèlent mutuellement) : NCID5 pré-duel
  → **410 code 5** ; duel 135/136/453 serial=1 ; même NCID5 après →
  **272** dmg 65 + 280. Tests `EntityTypingTests` (duel croisé, camps,
  contextes, dead) + `Modes1And4` foes.
- ⏸ Flag S_Monster « pacifique » (`def+0x10&4` natif) : colonne du
  parseur non identifiée — mobs non-hostiles déjà couverts par
  `IsElfPet`/`IsAlliedCrystal`/`MonsterType`. ⏸ Buffs sur ennemi (R6) :
  les voies de sort joueur→joueur n'existent pas encore (pas de
  pipeline de cast sur joueur) — le gate mêlée couvre le seul chemin
  d'attaque joueur→joueur actuel.
Manquement a corriger :
- Les mobs dangereux qui peuvent aggro ont pas leur nom en rouge, les mobs "ok" qui aggro pas mais reste dangereux n'ont pas leur nom en jaune léger, et les mobs qui sont trop bas level comparé au joueur n'ont pas leur nom en gris).

### 2.6 Animation damage coup critique —  fait
-  Bloc kanji `critical01/02` remplacé par une **aura rouge**
  radiale procédurale (`CritAuraTex`, Image 128² rgba — aucune
  texture asset requise) derrière les chiffres crit.
-  **Empilement vertical** : chaque floater vivant sur la même
  cible pousse le suivant de +0,4 u (cap ×6) — les hits simultanés
  s'accumulent vers le haut au lieu de se chevaucher.
-  Latence procs double/triple hit : stagger 0,18 s par valeur du
  292 EffectReport (déjà en place, conservé).
- **Live** (`EDEN_FLOATER_TEST` + `EDEN_FORCE_CRIT`, capture
  `26-crit-aura-stack.png`) : « 210 » au-dessus de « 234 », deux
  grosses auras rouges, aucun glyphe — aura + empilement visibles.
- Conservé : digits atlas natifs (algorism05 jaune crit / 04 rouge
  subi / 02 vert soin), pop 0,35→1,9→1,5, rise ease-out.
  
Manquement a corriger :
Il faut aussi corriger le fait que les animations de dégat RECU et les dégats INFLIGE doivent être différentes (reçu = rouge, infligé = blanc).
---

## 3. Reste — PRIORITÉ 3

### 3.1 Correctifs rapides (diagnostic posé, changements minimes)

#### 3.1.1 Tooltips instantanés —  déjà en place
- `ItemTooltip.cs` : `_wait = 0` à `Enter` (délai 200 ms déjà supprimé).
- `project.godot` : `timers/tooltip_delay_sec=0.0` déjà posé.

#### 3.1.2 Curseur ennemi/npc à travers les panels —  fait
- Cause : `GuiGetHoveredControl()` renvoie null pendant 1-2 frames quand
  un panel se reconstruit → `PickEntity` voit le mob derrière.
- Fait : `_uiGrace = 0,12 s` réarmée tant que `PointerOverUi() ||
  GuiIsDragging()` ; `OverHud()` = `_uiGrace > 0` (clics + curseur
  bloqués pendant la grâce — bloc curseur idem).

#### 3.1.3 Mort des mobs —  fait et prouvé live
- Serveur `Death.cs` : timers `ScheduleCorpseDespawn`+`ScheduleRespawn`
  armés EN TÊTE de `FinishMobDeathAsync` (plus tués par une exception
  de session) ; le 269 est émis à toutes les sessions du node SANS le
  gate `Sent.TryRemove` (UpdateSight pouvait l'avoir purgé → cadavre
  éternel).
- Client `EntityRegistry.cs` : `Revive()` (Dying=false + AutoLocomotion
  + stand01) appelé dans `HandleSeeMob` ; `HandleMovePrediction`/
  `HandleGoto` → `if (e.Dying) return`.
- **Fil prouvé** : 305 mort 14:59:44 → 269 despawn +5 s → 298 respawn
  +30 s réutilisant le handle → mob debout vivant (avant : cadavre
  éternel + respawn « invisible »).

#### 3.1.4 Skillbars par classe —  fait
- `PlayerProfileState.Apply(ZoneChangeClassShortcut)` = remplace
  `ShortcutBars` + `RaiseChanged("shortcuts")`, appelé depuis le bind
  510 de `ClassChangeDialog` (sur le thread principal via `_pending`).
- `HotbarPanel.OnProfileChanged` : `_page` clampée à `bars.Count-1`
  au changement de classe + `PageText` maj.
- **Live** : `//cc 5` → 510 (11 barres) → `[class] 510 shortcuts`
  appliqué — l'ancienne série n'est plus figée.

#### 3.1.5 Divers 1-ligne —  fait
- `StorageItemCell` : `["itemId"]` ajouté au dict drag → drop
  banque→hotbar accepté (`itemId` requis par `HotbarPanel`).
- `SkillCast.Cast` : sort non-hostile (`TargetType != 1`) + cible mob
  → cible forcée à 0 → cast part en NCID 17 (self-cast). **Live** :
  hostile 50033 reste en 9 sur mob ; heal 51000 passe en 17 (avant :
  soin CASTÉ sur le mob ennemi).
- Commentaire stale `HotbarPanel` (repli SkillBarWindow) corrigé.

### 3.2 Retours utilisateur divers

#### 3.2.1 Multi-joueurs : invisibles entre eux sur la même map
Deux clients lancés sur la même zone ne se voient pas. Vérifier :
spawn/présence des autres joueurs (NCID entrée/sortie de vue),
interaction, invitation party, duel. Lancer 2 clients en parallèle
(si pas de lag machine) et constater en Flameshot — croiser serveur
(`UpdateSightAsync`, listes de watchers) vs client (`EntityRegistry`).

##### 3.2.1.1 [en cours — fix partiel non prouvé]
- Il existe certaines textures encore en noir dans le jeu (certaines fleurs etc).
- Fix appliqué : `ToonMaterializer` n'active plus `VertexColorUseAsAlbedo`
  quand le mesh a un COLOR_0 absent ou quasi-noir (données vent/anim,
  pas de teinte) — corrige les props UD516/522/523/525/UC419 (avg=0).
- Reste : des props noirs persistent sur S001 (buissons noirs visibles
  vers 298,68 près des champignons géants) — le prop fautif n'est PAS
  encore identifié (les GLBs S001 scannés n'ont pas de COLOR_0 noir ;
  piste suivante : texture absente/noire, alpha, ou matériau blend).
  Et il en reste pas que en noir, sur plein de map j'ai des muret qui sont rouge non texture, j'ai des éléments du décor pas texturé.

#### 3.2.2 Position joueur fausse sur minimap ET carte monde
L'emplacement indiqué ne correspond pas à la position réelle :
vérifier l'échelle/offset map↔monde (`MinimapPanel`, `WorldMapWindow`,
`MapLoader`) sur plusieurs zones — probable facteur/translation
globale faux, ou coordonnées wire interprétées en pixels.
Quand on se déplace de monde en monde qu'importe le moyen de déplacement, on arrive JAMAIS a l'endroit où on doit spawn de base ET la mini-carte et carte indique encore n'importe quoi.
La mini carte et carte ne doit pas indiquer en rouge les mobs, le SEUL moment où elle doit indiquer en rouge les mobs c'est uniquement sur le quest tracker quand on fait clique droite sur un objectif et que l'objectif est lié a des mobs (collect, kill...).
Les mobs ont par moment les mauvais model (principalement les entité a kill qui ont 1 pv pour les quete, des oeuf de mobs, des coffres, des machines... tout ce qui est immobile est a des coordonnée de spawn bien spécifié dans le jeu originel, d'ailleurs ces entité spécial pour quete ne spawn pas a ces endroits spécialement défini).

#### 3.2.3 Équipement : swap ne rend pas l'item + dual→2H
- Équiper sur un slot occupé : l'item présent **disparaît** au lieu
  de retourner dans l'inventaire → swap serveur/client à corriger.
- 2 armes 1-main équipées + équip d'une arme 2-mains → les deux
  1-mains doivent être déséquipées (retour inventaire).
- Inventaire plein → **bloquer** le changement + warning joueur
  (jamais de destruction silencieuse).

#### 3.2.4 Boîtes / items donnant des items
Gérer l'ouverture de box et tout item qui en donne d'autres
(clic droit → octroi multi-items, tirage c_itemchest — `t_itemchest`
FR dispo). Identifier le NCID natif d'ouverture (re/ + captures).

#### 3.2.5 HUD de status (buffs actifs sur le joueur)
Déplaçable facilement quand un status est actif + temps restant
affiché plus gros (lisible), taille ajustable par le coin droit
(augmentation horizontal+vertical). Ref `BuffBar`/`TargetFrame`.
Certain lancement de skill simple sans effet sont marqué comme status sur les ennemi (aucun sens), et certain nerf que j'applique a l'ennemi je le vois dans mon hud de status (aucun sens), on ne voit dans le hud d'une entité que ce qui est bonus/état ou malus appliqué a cette même entité.

#### 3.2.6 Contraste du tracker de quêtes
Fond un peu plus sombre pour la lisibilité (panneau semi-transparent
actuel illisible sur fonds clairs — `QuestTracker`).

#### 3.2.7 Murs invisibles absents + angle de port des armes
- On traverse murs/bâtiments/eau : retrouver où les **murs invisibles**
  (collision) étaient codés/placés — analyser le client Reforged,
  `EDENFR/` et `eden_server/client/` (fichiers map/collision,
  `re/ghidra` pour le code de chargement), puis reproduire dans le
  client Godot. **Essentiel.**
- Angle de placement des armes sur le modèle erroné → retrouver la
  valeur/la pose native (mêmes sources) et la réappliquer.

#### 3.2.8 Rendu joueur : T-pose profil + « coupure » visuelle
- Panel profil : le personnage affiché est en **T-pose** (pas de pose
  idle/anim) → brancher l'animation d'idle du viewport.
- En jeu : le modèle joueur semble **coupé en deux verticalement**
  au milieu + moins net que les anciens clients → investiguer
  shading/normales/seam UV des modèles importés (comparer Reforged).

#### 3.2.9 Barres rouge/noir sur les mobs en contre-plongée 
Faire en sorte dans les paramètr de créer une nouvelle catégorie dédié aux préférences, dedans il y aura des sous catégorie pour trier, et des options selecteurs ou coche etc pour modifier l'UI.
Le premier choix a être rajouté sera  le fait de vouloir oui ou non voir la barre rouge de pv au dessus des mobs, et un autre choix sera la même choses mais pour soi-même , et un autre choix encore pour les autres joueurs. (3 choix, sous-catégorisé dans "Cible").
Il faut aussi des options d'animation quand la barre descend dans targetframe et dans les barre de vie juste au dessus des entité, l'animation c'est : Quand les pv descendent, mettre en orange la différence de pv entre avant et après les pv descendu, puis après 0.7 secondes la barre du précédent pv perdu fait descend (pas cumulable, si 2 hit a 0.5 sec d'intervale il y a aura 2 descente animé), il faut également ces choix multiples dans les paramètre.
Actuellement, selon les déplacement de ma caméra, j'ai parfois 0 pv ou 100% de pv (si je regarde vers le nord ma barre de vie rouge juste au dessus du character est noir vide, et si je regarder vers le sud elle est rouge 100% pv).


### 3.3.0 TargetFrame mal fait
Certains NPC ont 0 hp (barre de vie vide), leurs noms sont pas du tout dans le cadre (warning -> gérer les cas de pseudo long ou titre).

### 3.3.1 Instancier une config coté serveur
Il faudrait instancier une sorte de config.txt, ça permettrait de modifier des choses très rapide comme :
- Activation d'event noel ou halloween etc
- Modification de damage proc par un triple hit ou double hit
- Rate exp character, exp classe, exp classe éveil, drop, taux de réussite augmenté pour les craft etc.
- Move speed de base augmenter de X% (pour débuter ou autre)
- et autres idées intéressante pour controler rapidement du gameplay joueur.

### 3.3.2 NPC mal Texturé et mal placé
Pas mal de npc regardent dans des directions qui ont aucun sens par rapport a là où ils devraient regarder, et certain npc n'ont même pas leur couleur de texture.

### 3.3.3 Pseudo joueur
A part pour les autres joueurs ou entité, le joueur actuel ne doit pas voir son propre pseudo au dessus de son personnage.

### 3.3.4 Sauvegarde
Quand on quitte le jeu, le serveur ne sauvegarde RIEN du joueur (littéralement rien du tout, je perd exp et level, raccourci skillbar, map où j'étais etc, je perd tout).

### 3.3.5 Déplacement via questtracker
Si on clique gauche sur un objectif qui est atteignable en marchant, alors il faut avoir l'auto-follow même si c'est inter-monde.
---

## Sources de vérité ajoutées

- `knowledge/` — 12 840 pages wiki EN indexées, 243 pages + 130 guides
  FR, 64 feuilles Vendetta en md. `knowledge/README.md` = point
  d'entrée. Dumps bruts dans `raw/` (non versionnés).
- `eden_godot/docs/client-assets/ref-shots-catalog.md` — 103 captures
  cataloguées avec comportement UI attendu.
- `.gitignore` : `knowledge/raw/`, xlsx, `src_csv/`, `__pycache__`.

## Points de vigilance transverses

- Corps natifs absents de `decompiled.c` (plages 0x084x-0x086x) →
  `objdump` obligatoire pour 2.3.0/2.4 avant de coder les procs/stacking.
- Toute mutation `EntryState`/`LiveMob` via `ZoneLoop` — pas de
  `Task.Run`. Paquets spéculatifs interdits (formats prouvés : 287=7B,
  299=23B, 301 à dorettenir par golden).
- DoT qui tue : attribution + `FinishMobDeathAsync` ; enchants mob =
  registre partagé, purge au respawn, jamais persisté.
- Ne pas casser les customs assumés (cast mobile, extras slots 11/12 =
  page 10 par classe).
  
  
  
## Suite a ce plan
- Tout le système de quetes au complet gérant tous les cas possibles de quetes sur le jeu et refonte légère du questtracker
- Donjons (système d'entrée, de Boss, breakpoints, drop et panel pour voir les skills/drop/stats des mobs...)

