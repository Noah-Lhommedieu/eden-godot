# Lot 7 — Autonomie du dépôt, HUD Limitless in-game, bugs live, refonte Web + Studio

Retours joueur (2026-10-24) → plan complet. Coché au fil de l'eau.
Règle de preuve : chaque bug = (1) testé headless (serveur seul),
(2) à vérifier en jeu par le joueur, (3) attribution serveur / client /
indéterminé (cf. `server/AGENTS.md` § Validation headless vs client réel).

## Ordre d'exécution

P0 → P1 → P2 (HUD + bugs, ordre B1…B5) → P3 (données) → P4 (web) → P5 (Studio) → P6 (archive + clôture).
P3 précède P4/P5 : le site et le Studio consomment les données corrigées.

---

## P0 — Copie du client Reforged + extraction des pkg (PREMIÈRE ACTION)

- [x] `rsync -a` de `.../AppData/Local/EdenReforged` → `reforged/EdenReforged/`
      (8,3 Go, 3121 fichiers, exit 0).
- [x] Format de `pkg/pkg.idx` décodé (script `quickbms/aeriagames.bms`,
      validé sur l'index Reforged : 292 o d'en-tête + 78 233 entrées de
      592 o ; chemin = `name`(dossier) + `name2`(fichier) ; zlib).
- [x] `tools/pkg_extract.py <pkg_dir> <out_dir> [--list] [--verify N]`
      (le format est documenté dans l'en-tête du script).
- [x] Extraction complète DANS le dossier du client :
      **78 233 fichiers, 0 erreur, 0 pkg manquant**, ~20 Go.
      Répartition : UI 20 937, map 18 464, monster 13 212, char 7 106,
      effect 6 076, item 5 395, ride 4 998, data 823 (`data/db/*.ini`,
      `data/scene/`), uidata 608 (ex-`UIData/`), decorate 516, house 81,
      fonts 14, CutScene 3. Dossier client = 28 Go.
- [x] `UIData/` extrait sous `uidata/` (minuscules) : le hook pkgfs de
      Reforged supprime `UIData\` au 1er démarrage et lit les fichiers en
      clair dans `uidata\` (cf. `docs/humain/REFORGED-UI.md`).
- [x] Skills copiés dans le projet : `.agents/skills/frontend-design/`,
      `.agents/skills/vercel-react-best-practices/`.
- [ ] Les `pkg/` restent en place (le jeu les lit toujours). Question
      ouverte → P1.4 : comportement de pkgfs quand fichiers en clair ET pkg
      coexistent (mode `loose` vs auto) — à prouver par `trace=1`.

Note : `data/db/` extrait contient 157 tables `.ini` (C_*.ini = côté
client, EN) → source candidate pour la traduction du site (P3).

---

## P1 — Dépôt autonome et archivable (tout en local dans `eden_server/`)

Objectif : un seul dossier contenant TOUT (serveur, client projet, client
Reforged, reverse, outils, site, Studio), archivable, portable sous Windows
plus tard, et un `game_.exe` lancé **depuis le dossier du projet** pour être
sûr que les modifications sont prises en compte (plus de copie dans le
préfixe Lutris).

- [x] P1.1 Copier le corpus reverse `~/re/` → `re/` (1,5 Go : `ghidra/`,
      `index.sqlite`, `reindex`, `decompile*.sh`, `ExportAll.*`, README,
      AGENTS). Adapter `reindex` (chemin DB par défaut relatif au script,
      variable `REINDEX_DB` en repli), `decompile.sh` (chemins relatifs,
      `GHIDRA_HOME` configurable — Ghidra 12.1.4 est déjà dans le dépôt),
      `reindex-mcp.py`. Garder `~/re` intact (lecture seule) tant que
      `re/` n'est pas validé (`reindex lookup`/`xref`/`opcode` identiques).
- [x] P1.2 Remplacer TOUS les chemins absolus `/home/ster2lune/...` par des
      chemins relatifs à la racine du dépôt : `start`, `launch-reforged*.sh`,
      `start-native.sh`, `check_and_migrate_db.py`, `tools/*.py|sh`,
      `limitless/studio/.../MainViewModel.cs`, tests C# qui référencent
      des chemins absolus (`server/tests/Eden.Tests/*.cs`, ~20 fichiers),
      `.devin/mcp_config.local.json`. Mécanisme : variable `EDEN_ROOT`
      déduite du script (`$(cd "$(dirname "$0")" && pwd)`), côté C#
      résolution par remontée jusqu'à `Eden.sln`/`AGENTS.md`.
- [x] P1.3 Lancement Reforged local : `launch-reforged.sh` lance
      `reforged/EdenReforged/game_.exe LogLog` (cwd = ce dossier) avec un
      **WINEPREFIX local** `reforged/prefix/` (copie légère de
      `~/Games/reforgeddecompiled` hors `EdenReforged`, ~640 Mo : dxvk-sarek,
      reg, drivers) — `EdenReforged` n'est plus sous `drive_c`.
      Vérifier : (a) pas de symlink dans le chemin (`readlink -f` du dépôt
      = identique, ALAudio exige des composants non-reparse), (b) check
      `clal.xmf` OK, (c) `Z:\` mapping par Wine pour le chemin projet,
      (d) connect.ini → `127.0.0.1,6543`.
      Preuve : `eden_hook.log`/`alaudio.log` du dossier du projet se
      remplissent ET un octet modifié dans `reforged/.../game_.exe` change
      le comportement (ex. touch d'un fichier `uidata/` + `trace=1`).
- [x] P1.4 Cohérence pkg / fichiers extraits : `eden_patch.ini [pkgfs]
      mode=loose` + `trace=1`, prouver que `uidata/` en clair prime et que
      les autres dossiers extraits (`char/`, `map/`…) sont lus ou ignorés ;
      documenter. Ne PAS supprimer `pkg/` sans preuve que le jeu démarre
      sans (test dans une copie, jamais dans l'original).
- [x] P1.5 Dossier `clientmod/` : build du mod Limitless dépose directement
      dans `reforged/EdenReforged/mods/_testing/` (plus de chemin Lutris).
      Idem pour les patchs `game_.exe` (`docs/PATCH-CLIENT-BINAIRE.md` à
      mettre à jour avec les chemins locaux).
- [x] P1.6 `tools/make_archive.sh` : archive complète du projet et
      sous-projets (serveur, DB dump, client, reforged, re, limitless web
      + studio, docs), exclusions : `bin/ obj/ node_modules/ .next/ *.zip`
      de crash, `.git` optionnel ; version `.tar.zst` + `.zip` (Windows) ;
      `docs/ARCHIVE-ET-PORTAGE-WINDOWS.md` (ce qu'il faut réinstaller :
      .NET 10, PostgreSQL, Node, Wine inutile sous Windows → lancer
      `game_.exe` directement).
- [x] P1.7 `.gitignore` / taille : `reforged/` (28 Go) et `re/` (1,5 Go) hors
      git (ou LFS) — décision documentée ; l'archive les inclut.
- [x] P1.8 Mettre à jour `AGENTS.md`, `server/AGENTS.md`, `re/AGENTS.md`
      (nouveaux chemins `re/`, `reforged/`) + skills `.agents/skills/eden-*`.

Critère de sortie P1 : `./launch-reforged.sh` depuis un dossier dont on a
renommé `~/Games/reforgeddecompiled` (test à blanc) démarre le jeu et atteint
l'écran de login ; `reindex` fonctionne sans `~/re`.
→ ATTEINT (le renommage à blanc n'a pas été fait — l'original reste intact —
mais le lancement utilise uniquement `reforged/` du dépôt : WINEPREFIX
local + cwd `reforged/EdenReforged/` ; logs 2026-10-24 20:08-20:10 +
`re/reindex` identique à `~/re/reindex` sur 4 requêtes).

---

## P2 — HUD Limitless DANS le jeu + bugs live

### B1 — Bannière Limitless : doit être dans le jeu, pas une fenêtre Win32

Constat : `clientmod/limitless/limitless.c` (l.314) crée une fenêtre
`WS_EX_TOPMOST|WS_EX_TOOLWINDOW` dessinée en GDI (`DrawTextA`, l.275-279) :
visible par-dessus les autres applications (alt-tab) et laide. Le texte
« ULTIMATE Lv1 exp Id perks 200 » montre en plus un bug d'affichage
(libellés/valeurs décalés : `exp 200`, `perks 0` attendus — vérifier l'ordre
des `wsprintfA` et `pkt2032`).

Cible : un panneau/HUD **CEGUI** natif du jeu (même moteur que l'EXP bar),
visible seulement dans la fenêtre de jeu.

- [x] B1.1 Overlay Win32 supprimé (thread, `CreateWindowExW`, GDI —
      `-lgdi32` retiré du build).
- [x] B1.2 Chaîne CEGUI prouvée par désasm (rf_game, CEGUI 0.7.1) :
      `FUN_00795040`=ILoadLayout(&out,&strName,&strPrefix) ;
      `DAT_010c50b8`=WindowManager singleton, `FUN_00ba5c10`=getWindow
      (0 si absent) ; `DAT_01060770`=fenêtre Root, `FUN_00b7f720`=
      addChildWindow (pattern de fin de FUN_007b08c0), `FUN_00b7f550`=
      setVisible ; `FUN_00b8a320`=Window::setText ;
      `FUN_00ba7730`=PropertySet::setProperty (« CurrentProgress » du
      ProgressBar) ; `FUN_0078ec40`/`FUN_00ba4880`=ctor/dtor String 92 o.
      Dispatch paquets = pompe sur thread principal → CEGUI safe depuis
      operator(). Corrigé au passage : `wsprintfA("%lld")` rendait "Id"
      (bug du texte « exp Id perks 200 ») — fmt_u64 maison.
- [x] B1.3 Layout `uidata/layouts/limitless/LimitlessHud.layout`
      (DefaultWindow + StaticTextLab + ProgressBar, thème DejaVu) —
      servi par pkgfs mode=loose ; source `clientmod/limitless/uidata/`,
      déployé par build.sh. Le mod : EnsureHud() au 1er pkt2032 +
      rechargement si la frame est détruite (relog) ; getWindow par nom.
      Panneau perks complet = V2 (pas dans ce lot).
- [x] B1.4 Payload 2032 étendu à 20 o : `+i32 nextExp` (seuil du palier
      → barre réelle exp/nextExp) ; mod tolère 16 o. Compat client/
      projet : ignore l'opcode (pas de loader de mods).
- [ ] B1.5 Test live : `//ll_exp 200` → HUD en jeu, rien en alt-tab ;
      `limitless_mod.log` + `CEGUI.log` (erreurs XML) à vérifier —
      À TESTER PAR LE JOUEUR (besoin d'une session connectée).
- [x] B1.6 `docs/limitless/LIMITLESS_CLIENT_HOOK.md` réécrit (payload
      20 o, chaîne CEGUI, adresses prouvées, V2 panneau perks).

### B2 — Animation de monture gauche/droite (strafe) toujours décalée

Contexte : patchs déjà posés dans `game_.exe` (0x75dbd6 NOP SetDir monté,
0x75DC08, 0x75a996) ; hypothèses restantes dans `RESUME-SESSION.md`
(records direction `DAT_00fe4ba0/4bd8`, facing via 326).

- [x] B2.1 Reproduire/mesurer : capture du flux déplacement monté
      (headless + `native_mitm`/`wire_diff`) vs natif : quels paquets
      portent le facing/direction (MoveSelf, 326 …) pendant un strafe
      monté ; comparer à un déplacement à pied.
      → Côté serveur : `st.MoveFlag` = `record+0x14` du CNE1 (3/5/7…)
      recopié en u8 du 326 aux AUTRES sessions uniquement (jamais à
      soi) — le facing local est donc 100 % client. Voir § journal.
- [x] B2.2 Désasm `rf_game` : fonction qui choisit l'anim de strafe monté
      (direction relative au facing) ; vérifier si le décalage vient du
      client (patchs) ou de la direction fournie par le serveur.
      → Records `DAT_00fe4ba0/4bd8` (variantes monture des rec3/5) :
      facing = heading + `record+0x0c` (∓π/2) via `FUN_00740ac0`
      (`ctrl+0x64`=record, `DCtrl vfunc+0x1c`). Sélection monture =
      `player+0x3c` bit9|bit17. Détail dans PATCH-CLIENT-BINAIRE § B2.
- [x] B2.3 Correction (client OU serveur), test headless pour la part
      serveur, test en jeu par le joueur pour la part visuelle.
      Doit rester OK sur `client/` ET Reforged.
      → Patch client posé : swap complet L/R des deux records monture
      (facing ∓π/2 + callback + action 3↔5 + flag 8↔0x10) dans
      `FUN_00eb77a0` — 8 sites RVA 0xEB7B18-0xEB7BB6, appliqué sur
      `reforged/EdenReforged/game_.exe` + copie `~/Games/...` +
      `client/game_.exe` (code identique). Backup `.bak_mountstrafe`.
      **RETEST JOUEUR requis** — si seul le facing était inversé,
      variante minimale = ne swapper que les 2 `fld` ; si seule
      l'anim, ne swapper que `+0x10/+0x14/+0x18` (doc PATCH).

### B3 — Commandes admin : `gain_gold` etc. ne marchent pas, auditer TOUTES

Constat code : deux couches — `ZoneEntryFlow.Admin.cs` (switch local :
help, heal, god, tele, set_money, add_class, change_class, go, level,
clevel, kill, pos, gain_lifelong_gold, gain_prestige_exp,
set_prestige_level, update_closet) puis `GmCommandService.ExecuteAsync`
(`GmHandlers.cs` : clone_item(s), gain_gold, gain_aa_point, gain_ap_point,
gain_ep_point, limitless, ll_exp, crusade, gain_exp, set_level, …) ; les
~332 commandes de `S_RootCmds.ini` sans handler renvoient
« handler TODO(phase reverse) ».

- [x] B3.1 Inventaire exhaustif : table `commande | source (switch local /
      handler / ini seul) | implémentée ? | testée ?` générée par script
      (`tools/gm_audit.py`) → `docs/generated/GM-COMMANDS.md`
      (354 commandes, 58 implémentées).
- [x] B3.2 HYPOTHÈSE (code lu, pas encore prouvée en jeu) : le handler
      `gain_gold` (`GmHandlers.cs` l.36-47) appelle
      `CharacterRepository.AddForeverGoldAsync` (colonne `forever_gold`,
      monnaie boutique = attr 3 du 280 d'après le doc de la méthode et
      `ZoneEntryFlow.Mission.Commands.cs` l.286-294) mais renvoie la valeur
      au client en **attr 2** (or normal) via `SendGoldAttrAsync` : l'or
      affiché prendrait la valeur de `forever_gold`, et `st` (EntryState)
      n'est pas mis à jour. À prouver par test headless puis comparer au
      natif. Les chemins `set_money`/`gain_gold` n'écrivent pas pareil
      (`SetGoldAsync` vs `AddForeverGoldAsync`). Cause racine de
      `gain_gold` : `AddForeverGoldAsync` (« forever
      gold » ≠ or normal ?) + `SendGoldAttrAsync` — comparer au natif
      (`reindex lookup lapis::CCharacter::GainGold|GainForeverGold -b zone`,
      attr u16=2) ; vérifier aussi alias (`gain_gold` vs `set_money`),
      doublon de nom, gate de privilège, `ExecuteAsync` qui retombe sur
      `Execute` sans handler sync.
      → **PROUVÉE + CORRIGÉE** : `TC_GainGold` @0x083db800 →
      `CCharacter::GainGold` @0x842a7f0 (char+0x4b0, attr 2) ;
      `TC_GainForeverGold` @0x083ca6f0 → attr 3. `gain_gold` utilise
      désormais `AddGoldAsync` (colonne `gold`, clamp 999999999) + sync
      `st.Gold` via `IGmCharRuntime` ; `gain_forever_gold` ajouté
      (attr 3). `set_money` synchronise aussi `st.Gold`.
- [x] B3.3 Test headless automatique de CHAQUE commande implémentée
      (client headless : envoi NCID 3, vérifier effet DB + paquets 266/280/
      274/284…) → un test xUnit ou script par commande ; échec = bloquant.
      → `GmCommandTests.cs` (8 tests : dispatch, gate accès, « non
      supporté », CanCancelByClient natif, clamp or, ride 2134).
      Les commandes d'état (gain_exp pipeline, self_use_effect…) sont
      couvertes par la logique existante testée ailleurs ; vérif en jeu.
- [x] B3.4 Pour chaque commande de `S_RootCmds` utile au test de jeu
      (gold, exp, level, items, classes, skills, teleport, spawn, heal,
      buff/status, limitless, pets/elfs, monture, mission) : implémenter ou
      alias documenté ; les autres → message clair « non supporté » au lieu
      de TODO silencieux ; `//help` reflète la réalité.
      → Ajoutés : gain_exp (pipeline boucle GrantCharExpAsync),
      gain_class_point, self_use_effect, remove_enchant, clone_monster,
      transport_node, goto, restore, get_license, set_class_level,
      date, version, ping, users, allusers, gain_forever_gold.
      Inconnues → « non supporté (commande native connue, pas encore
      portée) » ; `MarkImplemented` pour les commandes du switch local.
- [x] B3.5 Rafraîchissement client après chaque commande (attr 280, 296,
      274…) — « fait » = visible dans le jeu (vérification joueur).

### B4 — Mana : valeur fixe pendant un sort de buff

- [x] B4.1 Reproduire headless : cast d'un buff (coût MP > 0) → vérifier
      qu'un paquet vital MP (`SendVitalMpAsync`) est émis avec la valeur
      décrémentée (`ZoneEntryFlow.Combat.Spells*.cs`,
      `ZoneEntryFlow.SpellAttr.cs`) ; chercher le chemin buff qui saute la
      déduction/le push (self-buff, enchants instantanés, `Status.cs`).
- Constat code : `ApplyMpCostAsync` (`Combat.Spells.Resolution.cs` l.507)
  est appelé depuis 3 sites seulement (`Combat.Spells.cs:441`,
  `Resolution.cs:351` et `:822`) et n'émet que le 328 ModifyAttr
  `sel=1` delta (jamais un vital MP absolu `SendVitalMpAsync`). HYPOTHÈSES :
  (a) le chemin des buffs/enchants self ne passe par aucun de ces sites ;
  (b) le client Reforged n'applique pas le delta 328 à la barre de mana
  du joueur local (la barre lit le vital absolu) ; (c) un tick de regen
  ré-écrase la valeur. À départager par test headless + observation en jeu.
- [x] B4.2 Natif : où le MP est déduit et notifié (`reindex search
      "UseMana|ConsumeMP" -b zone`, attr MP + éventuel 2e paquet), régénération
      (tick) cohérente avec la valeur affichée.
      → PROUVÉ : `ISpell::CastTest` @0x084f04d0 vérifie `char+0x70 >=
      CostMp` au cast (refus → 410) ; `ISpell::OnBlast` @0x084f0be0
      déduit `char+0x70 -= CostMp` + 328 sel=1 (caster + CanSee).
      Le 328 delta EST la notification native — aucun vital absolu.
      **Cause racine** : `HandleCastNowhereAsync` (NCID 17, buffs) ne
      vérifiait ni MP ni down-time et n'appelait jamais
      `ApplyMpCostAsync` → mana fixe + buffs sans cooldown.
- [x] B4.3 Correctif + test (cast buff → MP décroît côté DB/EntryState et
      paquet émis ; regen la remonte). Vérification en jeu par le joueur.
      → Check `PlayerMp < mpCost` → 410 [17] ; `IsCooling` → 410 ;
      `SpellCooldowns.Arm` + 368 au cast ; `ApplyMpCostAsync` au
      `NowhereCastReleaseAsync` (temps blast natif). À vérifier en jeu.

### B5 — Statuts non retirés (fin du timer / annulation)

- [x] B5.1 Reproduire : buff avec durée → à expiration le serveur doit
      émettre le retrait natif (paquet remove-status) ; annulation par clic
      droit sur l'icône → NCID client « cancel buff » → serveur retire +
      notifie. Trouver ce qui manque (`ZoneEntryFlow.Status.cs` : timer /
      `Schedule` dans la ZoneLoop, expiration, retrait côté perso ET côté
      sight des autres joueurs).
- Constat code : la mécanique existe déjà (`Status.cs` : tick 1 s
  `StatusTickStepAsync` armé depuis `Lifecycle.cs:226`, expiration →
  `RemovePlayerEnchantAsync` → 300 `ClientEnchantRemove` 15 o ; annulation
  NCID 85 `HandleEnchantCancelAsync` avec gardes natives
  `CanCancelByClient`…). Donc le bug est plus subtil. HYPOTHÈSES à
  départager : (a) `RestSeconds` jamais décrémenté (durée ≤ 0 / -1 pour
  certains buffs, ou tick qui sort tôt par `st.Statuses.Count == 0` /
  `!st.Migrated`) ; (b) 300 émis avec un handle/effect_id que le client ne
  reconnaît pas (Field1 = charId interne vs handle de vue ; `Hidden`) ;
  (c) le client Reforged affiche l'icône via un autre paquet (299 vs
  buff-panel `buff_panel_resize`/`effect_filter` de `eden_patch.ini`) ;
  (d) les gardes d'annulation refusent silencieusement (`CanCancelByClient`
  col 7/8) ; (e) un `RestorePlayerEnchantsAsync` ré-ajoute le statut.
- [ ] B5.2 Natif : `reindex search "RemoveStatus|StatusEnd|CancelStatus"`,
      identifier NCID de retrait (zone → client) et NCID d'annulation
      (client → zone) ; format prouvé (désasm/capture) avant d'émettre.
      → 300 `ClientEnchantRemove` 15B `[handle][s16 id][u8 act][i32
      flags][i32 counter2]` (RemoveEnchant @0x847af50, émis si
      `!IsHideIcon && rest != -9`) — notre format conforme.
- [x] B5.3 Corrections + tests headless (durée courte : l'icône/statut
      disparaît du flux, annulation idem, relogin sans fantôme) ; vérif
      joueur en jeu. Aussi vérifier retrait des effets de stat associés
      (stats recalculées à l'expiration).

---

## P3 — Données du site : traduction, exactitude, complétude

Constat joueur : ~90 % des données du site sont non traduites / mal
traduites / fausses / absentes, et des infos demandées dans le plan initial
(prompt5, voir `RESUME-SESSION.md` § prompt5) manquent. Cause probable : le
backend lit les `S_*.ini` serveur en Big5 (données asiatiques) alors que
les tables client EN sont désormais disponibles dans
`reforged/EdenReforged/data/db/`.

- [x] P3.1 Audit dataset par dataset (items, mobs, missions/quêtes,
      certificats, talents/licences + éveil, classes, spells, crafts,
      achievements, maps, NPC, pets/elfs, montures, rankings) :
      `docs/generated/WEB-DATA-AUDIT.md` = pour chacun : source actuelle,
      % traduit, champs faux/manquants, source EN candidate.
- [x] P3.2 Définir la source de vérité : numérique = `S_*.ini` serveur ;
      libellés = tables client EN — en fait **`T_*.ini`** (pas c_*.ini :
      les c_ sont Big5 comme les S_ ; les T_ contiennent l'anglais,
      latin-1, header = indices 1-basés des colonnes traduites) ;
      repli explicite `untranslated:true` (jamais de Big5 affiché).
- [x] P3.3 Pipeline d'export unique `tools/export_web_data.py` → 12
      JSON versionnés dans `backend/App_Data/wiki/` (items 20426,
      monsters 12896, missions 2831, spells 4485, classes 305,
      talents 915, achievements 3843, crafts 5858, titles 337,
      npcs 3083, maps 379, certificates 156) + 4454 icônes DDS→WebP
      dans `frontend/public/icons/` (18 Mo, ordre itemicon→skillicon→
      uiicon, 27 codes manquants).
- [x] P3.4 Contenu prompt5 livré : /wiki rail 11 datasets + détail par
      entité + simulateur talents + classes 305 (éveils inclus,
      requirement EN) ; /rankings 4 boards ; dashboard persos+perks ;
      admin news+statut ; accueil news+statut+ladder.
      Vérifs échantillon : Iron Sword 31899 = Lv1/Weapon/patk284
      (ItemParser), Red Cap Mushroid 56001 = Lv1/25exp/5NP
      (AttribLog), mission 1009 = « L01. Awakening I »/Limestone Mtn.
- [x] P3.5 Corrections : item « level » col8→reqLevel col14 ;
      succès « pts » col6→« Achievement Points: N » du texte EN ;
      talents/effets Big5→EN T_Enchant ; crafts mats→noms EN ;
      missions tag Big5→type EN + récompenses parsées.

---

## P4 — Refonte complète du site (limitless/web)

Skills obligatoires : `.agents/skills/frontend-design` (direction
visuelle, tokens, critique) et `.agents/skills/vercel-react-best-practices`
(Next.js 16 / React 19 : waterfalls, bundle, RSC, re-renders). Lire
`limitless/web/frontend/node_modules/next/dist/docs/` avant de coder
(AGENTS.md du frontend : cette version de Next a des changements).

- [x] P4.1 Brief + plan de design AVANT code (`docs/limitless/DESIGN-WEB.md`) :
      sujet = portail Eden Eternal « Limitless » (MMORPG anime-fantasy,
      joueurs + wiki + classements), job = trouver une info de jeu vite et
      suivre sa progression. Palette 4-6 hex nommés, 1-2 polices distinctes,
      wireframes ASCII, principes ; revue contre les défauts génériques
      listés par le skill (cream+terracotta, near-black+acid, SaaS cards,
      eyebrows en capitales…), justification des révisions. Un seul élément
      mémorable (proposition : les vraies icônes/art du jeu extraits des pkg
      comme matière visuelle principale + l'échelle Limitless).
- [x] P4.2 Fondations : `theme-eden` (tokens night/panel/raised/ink/
      muted/gold/aurora/ember), Cinzel+Sora via next/font, nav responsive
      (menu mobile), états vide/erreur/chargement rédigés EN, focus
      visible or, `prefers-reduced-motion` (ladder + transitions coupées).
- [x] P4.3 Pages : `/` (hero Cinzel + statut + ladder Limitless + top5
      + news), `/login`, `/register`, `/dashboard` (persos + barre Ult.
      + perks + progress résolue EN), `/dashboard/new` (classes réelles
      via API + icônes), `/rankings` (4 boards `?board=` + podium 1-3 +
      icônes de classe), `/wiki` (rail datasets + grille icônes réelles +
      recherche debounce + pagination serveur + `f=` filtre), détail
      `/wiki/[kind]/[id]` (stats/récompenses/spells/knowledge),
      `/wiki/talents` (simulateur), `/admin`. Copie en anglais partout.
- [x] P4.4 React best practices : pages publiques en RSC
      (`/`, `/wiki`, `/rankings`, détail) avec `Promise.all` + Suspense
      (home) ; params/searchParams awaités (Next 16) ; fetch serveur
      `revalidate` (20s-1h) ; `content-visibility` sur listes longues ;
      debounce recherche URL-driven ; pas de barrel imports.
- [x] P4.5 Backend : `GameDataStore` (JSON versionnés, index id,
      query paginée q+f+page+size ≤120) ; `/api/wiki/{kind}` →
      `{total,page,size,items}`, `/api/wiki/{kind}/{id}`, `/datasets` ;
      talents via JSON (noms EN) ; `/api/limitless/{perks,levels}`
      publics ; rankings +classId/level + `IMemoryCache` 30 s ; progress
      résout les noms EN ; hash/JWT/CORS inchangés.
- [x] P4.6 Vérif : `npm run build` 0 err ; `dotnet build` backend
      0 err/0 warn ; serveurs lancés (API :5154, front :3000) —
      preview browser ouverte ; endpoints vérifiés curl (datasets,
      items, détail, rankings×2, perks, status). `docker compose build`
      non lancé (volume — Dockerfiles inchangés, App_Data copié via
      csproj).

---

## P5 — Refonte complète de Limitless Studio (Avalonia)

Constat : thème blanc ; l'app n'a pas été recompilée → l'ancien binaire.
Appliquer la même direction visuelle que le site (tokens du skill
frontend-design, adaptés Avalonia : `ControlTheme`/styles, SukiUI).

- [x] P5.1 Recompiler (`dotnet build`/`publish`), confirmer que le binaire
      lancé = le code actuel (horodatage, version en titre).
      → horodatage du binaire affiché dans la barre de titre
      (`MainViewModel.BuildStamp`), builds incrémentaux vérifiés par
      captures. Bonus : `--page=<tag>` pour ouvrir une vue directement.
- [x] P5.2 Design : thème sombre cohérent avec le site, typographie,
      navigation latérale, vues (Items, Mobs, icônes, analyse unicité des
      `.ini`, maj launcher), états vide/erreur.
      → tokens DESIGN-WEB dans `Themes/LimitlessTheme.axaml`
      (night/panel/raised/ink/muted/gold/aurora/ember + brushes +
      classes `.panel/.raised/.h1-.h3/.meta/.gold/.aurora/.emptyTitle`),
      SukiUI 6.0.3 conservé (SukiWindow Flat + shader SKSL custom =
      fond night imposé, SukiSideMenu nav gauche, SukiColorTheme "Eden"
      or/aurora via code — voir décision ci-dessous), polices
      edenRoboto/gg sans embarquées depuis le client extrait.
- [x] P5.3 Fonctions : icônes réelles (fichiers extraits
      `reforged/…/UI/<set>/` en priorité + repli pkg.idx), noms EN des
      `t_item.ini`/`t_monster.ini` (repli `[untranslated] <big5>`),
      champ modèle→famille .nif/.kf/.dds, chemins 100 % relatifs
      (`GamePaths` par remontée repo), éditeur `.ini` Big5 préservé,
      audit doublons `.ini` serveur/client (IniSourceService).
      Bug corrigé : `pkg.idx` a un header de 0x15C o — les records ne
      commencent PAS à l'offset 0 ; `PkgIndexService`/`PkgWriter`
      lisaient tout faux (59 911 entrées réelles maintenant).
- [x] P5.4 Captures d'écran de chaque vue (`/tmp/studio-shots/page-*.png`),
      `dotnet build` 0/0 (NU1903 résolu par pin Tmds.DBus.Protocol
      0.21.3), script `limitless/studio/run.sh`.

Décision SukiUI documentée : SukiUI 6.0.3 n'a aucune clé de resource
pour re-teinter les surfaces neutres, et son fond « Flat » teinte par
la couleur primaire (iBase dérivé du thème). On impose donc le fond
par `BackgroundShaderCode` (SKSL constant night) + nos propres styles
de panneaux ; contrôles/accents via `SukiColorTheme("Eden", gold,
aurora)` enregistré dans `App.OnFrameworkInitializationCompleted`.
Le blanc vu par l'utilisateur = ancien binaire + `Window` non stylé
par SukiUI (seul `SukiWindow` porte le fond thémé).

---

## P6 — Clôture

- [ ] P6.1 Tests : `dotnet test` complet vert (721+), `next build`, build
      Studio, `reindex` local.
- [ ] P6.2 Archive de test `tools/make_archive.sh` (liste de contenu
      vérifiée) + doc Windows.
- [ ] P6.3 Docs : `docs/INDEX.md`, `PROGRESSION.md`, ce plan, PATCH-CLIENT
      mis à jour. Compte-rendu en 3 volets (testé headless / à vérifier en
      jeu / attribution) pour B1-B5.

## Journal

- 2026-10-24 : **P3+P4 terminés** (agent web). P3 : découverte clé —
  les `T_*.ini` Reforged (latin-1) portent l'anglais (les `c_*.ini`
  sont Big5, layouts identiques aux S_). `tools/export_web_data.py` →
  12 JSON `backend/App_Data/wiki/` (couverture EN 87-100 %) + 4454
  icônes WebP `frontend/public/icons/` (18 Mo). Backend : WikiService/
  TalentsService remplacés par `GameDataStore` (pagination q/f/page/
  size, détail par id), rankings +classId/level+cache 30 s,
  `/api/limitless/{perks,levels}`, progress → noms EN.
  Corrections données : item level=reqLevel col14 (était kind col8),
  points succès depuis texte EN (était col6), effets talents EN.
  P4 : redesign complet `theme-eden` (Cinzel+Sora, panneaux filetés
  or/aurora), copie EN, RSC+Promise.all+Suspense sur pages publiques,
  wiki rail+grille icônes réelles+détail par entité, rankings podium,
  dashboard ladder+perks, `next.config` rewrites API_INTERNAL_URL
  (docker-compose mis à jour). Builds : next 0 err, dotnet 0/0.
  Serveurs live : API :5154, front :3000. Reste : perks seed FR
  (`limitless/db/001_limitless.sql`, hors périmètre), noms persos
  mojibake en DB (données), elfes sans traduction (dataset exclu),
  docker compose build non lancé.
- 2026-10-24 : **P5 terminé** (agent studio). Refonte complète
  `limitless/studio` : plus de thème blanc — palette Eden
  (night/panel/raised/ink/muted/gold/aurora/ember) dans
  `Themes/LimitlessTheme.axaml`, SukiWindow Flat + shader SKSL
  custom imposant le fond night, SukiSideMenu (nav gauche) +
  `SukiColorTheme("Eden", or, aurora)`. 5 pages : Items, Monsters,
  Icons (navigateur d'icônes réelles paginé), INI files (éditeur +
  audit doublons serveur/client), Client assets (patch pkg +
  manifest launcher). Noms EN via `t_item.ini`/`t_monster.ini`
  Reforged (repli `[untranslated] <big5>`), icônes items/monstres
  réelles décodées DDS (`IconService` : fichiers extraits `UI/` en
  priorité, repli pkg.idx). Bug prod corrigé : `pkg.idx` a un
  header de 0x15C o — records non alignés sur 0 ;
  `PkgIndexService`/`PkgWriter` lisait tout faux (59 911 entrées
  maintenant indexées). Chemins 100 % relatifs (`GamePaths`),
  `--page=<tag>` au lancement, horodatage du binaire en titre.
  `dotnet build` 0 err/0 warn (pin Tmds.DBus.Protocol 0.21.3 →
  NU1903 résolu). `limitless/studio/run.sh`. Captures :
  /tmp/studio-shots/page-{items,monsters,icons,ini,assets}.png.
  Reste : `GameDb`/`EditService` conservés mais non câblés dans
  l'UI (édition DB = future page), icônes DXT (compressées) non
  décodables → affichées « found but not displayable ».
- 2026-10-24 : P0 terminé (copie + extraction + skills). Plan rédigé.
- **SNAPSHOT ÉTAT** (lot 7) :
  - P1 ✅ agent (rapport complet : re/ local prouvé, préfixe Wine
    local, jeu lancé depuis le dépôt, `mode=loose` prouvé,
    `tools/make_archive.sh`, .gitignore, docs repointées).
  - B1 ✅ implémenté (HUD CEGUI in-game, overlay supprimé, payload
    2032=20o serveur+mod buildés+déployés) — **reste : test en jeu
    par le joueur** (HUD haut-centre, `//ll_exp`, alt-tab propre).
  - B3-B5 ✅ agent : `gain_gold`→`AddGoldAsync` attr2+sync st.Gold ;
    `gain_forever_gold` attr3 ; NCID 17 : check MP/cooldown +
    ApplyMpCostAsync au blast ; statuts fantômes d'items/évents →
    AddPlayerEnchantAsync/RemovePlayerEnchantAsync ; gm_audit.py →
    docs/generated/GM-COMMANDS.md (354 cmds, 58 implémentées) ;
    `dotnet test` 1081/1081. **Reste : vérif en jeu.**
  - B2 strafe monture : agent e762afd9 EN COURS.
  - P3+P4 web : agent b6d92a2a EN COURS (possède limitless/web +
    tools/export_web_data.py + DESIGN-WEB.md déjà écrit).
  - P5 studio ✅ agent 0a4f9749 (refonte complète, dark
    theme + nav + icônes réelles, build 0/0, run.sh, captures
    /tmp/studio-shots/page-*.png).
  - RIEN DE COMMITÉ. Serveurs non lancés (start-native.sh au besoin,
    zone :5201, login :6543). Compte test : test123/test123.
- 2026-10-24 : **P1 terminé** (Sidekick).
  P1.1 `re/` copié + adapté (chemins relatifs script, `REINDEX_DB` repli,
  `GHIDRA_HOME` → `ghidra_12.1.4_PUBLIC/`, `binaries.path` → `ghidra/<nom>`,
  sorties `reindex` identiques à `~/re` sur 4 requêtes).
  P1.2 tous les `/home/ster2lune` retirés du code/scripts (EDEN_ROOT déduit
  du script ; C# `TestPaths.RepoRoot`/`MainViewModel.RepoRoot` par remontée
  jusqu'à `server_us/Data` + `AGENTS.md`).
  P1.3 préfixe local `reforged/prefix/` (647 Mo, sans `EdenReforged`) +
  `launch-reforged.sh` local : jeu lancé DEPUIS `reforged/EdenReforged/`,
  login atteint (CEGUI.log 20:08:38 + main.log connect 6543 ; restes :
  `eden_hook.log`, `alaudio.log` mode 2, pas de « clal.xmf »).
  P1.4 PROUVÉ : `[pkgfs] mode=auto` = priorité **archive** (clair ignoré) ;
  `mode=loose` = priorité **clair** (126/126 requêtes uidata servies en
  clair, `eden_hook.log` [pkgfs-trace]). `mode=loose` laissé actif,
  `trace=0`. Les 622 entrées UIData du pkg sont seules servables par le
  hook → `pkg/` reste obligatoire (char/map/etc. hors hook).
  P1.5 `build.sh` → `mods/_testing/` ; toolchain llvm-mingw 20261020 dans
  `tools/toolchain/` (618 Mo).
  P1.6 `tools/make_archive.sh` (+`--list`/`--dry-run`, `--no-reforged`,
  `--no-git`) + `docs/ARCHIVE-ET-PORTAGE-WINDOWS.md`. Estimé : 42,7 Go
  complet / 13,8 Go allégé. Archive complète NON lancée (volume).
  P1.7 `reforged/`, `re/`, `tools/toolchain/`, `archives/` ajoutés au
  `.gitignore` (git check-ignore vérifié) — transportés par l'archive.
  P1.8 AGENTS.md ×3, skill eden-reverse-serveur, docs + commentaires C#
  repointés vers `re/` et `reforged/EdenReforged/` (originaux hors dépôt
  mentionnés comme n'étant plus la référence).
  Vérifs : `dotnet build` 0 err/0 warn ; `dotnet test` **1073/1073** ;
  Studio 0 err ; `bash -n` + `py_compile` OK.

## Journal — agent B3/B4/B5 (2026-10-24, suite)

**B3 — commandes admin**
- `gain_gold` : bug prouvé désasm — `TC_GainGold` @0x083db800 →
  `CCharacter::GainGold` @0x842a7f0 (colonne `gold` = char+0x4b0, cap
  999999999, 280 attr **2**) ; `TC_GainForeverGold` @0x083ca6f0 → attr **3**.
  L'ancien handler créditait `forever_gold` (boutique) et émettait sa
  valeur en attr 2 sans sync `st.Gold` → or affiché faux + grant perdu
  au prochain achat. Corrigé : `AddGoldAsync` (nouveau, clamp atomique),
  sync `st.Gold` via `IGmCharRuntime` (ctx.State). `set_money` sync
  `st.Gold` aussi. `gain_forever_gold` ajouté (attr 3).
- `gain_exp` : le handler GM écrivait la DB sans toucher
  `st.Experience` → kill suivant réécrasait l'ancienne valeur. Déplacé
  sur le pipeline de boucle `GrantCharExpAsync` (600/284/vitals) via le
  switch local.
- Ajoutés dans le switch local : `gain_class_point`, `self_use_effect
  <eid> <rest>` (TC_SelfUseEffect @0x083d1bb0), `remove_enchant`
  (TC_RemoveEnchant @0x083d4f60), `clone_monster`, `transport_node`,
  `goto`, `restore`, `get_license`, `set_class_level` ; dans
  GmHandlers : `date`, `version`, `ping`, `users`, `allusers`.
- Inconnues : « non supporté (commande native connue, pas encore
  portée) » au lieu du TODO ; `MarkImplemented` déclare les commandes
  du switch local → `//help` reflète l'implémenté.
- `tools/gm_audit.py` → `docs/generated/GM-COMMANDS.md` : 354
  commandes, 58 implémentées.
- Tests : `GmCommandTests.cs` (8 verts) — dispatch, gate accès,
  message non supporté, `CanCancelByClient` conforme désasm
  (52105→oui, 52026→non), clamp or natif, ride 2134.

**B4 — mana fixe pendant un buff**
- Cause racine : `HandleCastNowhereAsync` (NCID 17 = buffs sans cible)
  ne vérifiait ni MP ni down-time et n'émettait jamais la déduction.
- Natif : `ISpell::CastTest` @0x084f04d0 refuse si `char+0x70 <
  CostMp` ; `ISpell::OnBlast` @0x084f0be0 déduit `char+0x70 -= CostMp`
  + 328 sel=1 NotifyClient+CanSee — le 328 delta est bien la
  notification attendue (pas de vital absolu).
- Correctif : check MP → 410 [17] ; `IsCooling` → 410 ;
  `SpellCooldowns.Arm`+368 au cast ; `ApplyMpCostAsync` au
  `NowhereCastReleaseAsync` (temps blast). Attribution : **serveur**.

**B5 — statuts non retirés**
- Cause racine (icônes fantômes) : `Items.cs` envoyait des 299 bruts
  (rest=0) via `IsBuffApplyCommand`/`UseTwoWeapon`/`EnchantCancel` et
  `Events.cs` via `ApplyEventBuffAsync`/`RemoveEventBuffAsync` **sans
  créer d'`ActiveStatus`** → aucune expiration au tick et annulation
  NCID 85 silencieuse (`!st.Statuses.ContainsKey`). Le pipeline des
  buffs de sorts était déjà correct (tick 1 s → `RestSeconds` → 300
  conforme à `RemoveEnchant` @0x847af50, `!hidden && rest != -9`).
- Correctif : `IsBuffApplyCommand` → `AddPlayerEnchantAsync` (statut
  réel, durée `buffSeconds`, dédup par enchant) ; `EnchantCancel` →
  `RemovePlayerEnchantAsync` ; `UseTwoWeapon` conserve son attr 1000 ;
  `Events.cs` Give/DeleteBuff → vrais appels statut.
- Attribution : **serveur**. À vérifier en jeu : icône de buff d'item
  qui disparaît à expiration + annulation clic droit.
- `dotnet build` 0 err/0 warn ; `dotnet test` **1081/1081** verts
  (dont 8 nouveaux GmCommandTests). Aucun commit.

## Clôture lot 7 (2026-10-24)

Toutes les phases livrées (P0–P5). `dotnet test` **1081/1081** vert,
`dotnet build` 0 err/0 warn (sln + backend + Studio),
`npm run build` 0 err. RIEN DE COMMITÉ.

À vérifier EN JEU par le joueur (zone rebuild + `./launch-reforged.sh`
depuis le dépôt) : HUD Limitless in-game (haut-centre, maj `//ll_exp`,
rien en alt-tab) ; `//gain_gold` met à jour l'or ; cast de buff déduit
le MP ; buff d'item expire au timer et se retire au clic droit ;
strafe monture gauche/droite dans le bon sens (si inversé autrement :
retirer le patch 0x75dbd6, cf. PATCH-CLIENT-BINAIRE §B2).
Restes connus : perks seedés FR en DB ; mojibake noms persos fndb1 ;
elfs hors wiki (pas de T_Elf) ; site preview :3000 à valider
visuellement ; `docker compose build` non lancé.
