# Plan — refonte frontend HUD + panels (fidélité ref-shots)

Objectif : chaque HUD puis chaque panel doit ressembler **réellement**
au client d'origine (`ref-shots/`) et être fonctionnel selon
`docs/hud-interfaces.md`.

Sources, par ordre d'autorité :
1. `docs/hud-interfaces.md` — règles fonctionnelles **écrites par le
   propriétaire** (prioritaires : ex. Ultimate caché, GoldHud/MountHud
   supprimés, castbar sans nom de sort, EXP perso vert pomme).
2. `ref-shots/*.png` — vérité visuelle (pixels).
3. `docs/REF-SHOTS-SPEC.md` — description texte, **non fiable** :
   toujours re-vérifier contre l'image.

Méthode par élément : crop ref ×2 → capture Godot live
(`--login=admin:adminmdp --channel --char --shot=…`, **un seul client
Godot à la fois**) → diff visuel → correction → recapture → commit.

## Constat initial (capture live 27/10, 1280×720)

- Thème forcé `Purple`/`Blue` alors que la ref = atlas de base gris
  « Noble Irongray Style » (`imagesets/main.png`). → tout le HUD a la
  mauvaise couleur.
- Vitals : textes HP/MP superposés (`2366/2364` doublé), barre vide
  parasite sous la grille 1-12, « 0% » flottant.
- Chat : onglets Normal/Battle **sous** le panneau (ref : en haut à
  gauche, collés au cadre), bouton Settings mal placé, ligne canaux
  illisible (cases parasites).
- Barres EXP : pas de losanges/icônes, pourcentages sans 3 décimales.
- Quest tracker : pas de cadre gris translucide, onglets non dessinés
  en boutons, bouton « − » absent, heures 17:31 qui débordent dedans.
- Hotbar : rangée C1…C+ sans cases, page ▲1▼ absente, boutons
  ✕/verrou/+ absents.
- Menu icônes : 2 lignes débordant à droite (ref : 1 ligne de 9).
- Minimap : boutons dispersés, horloges collées au tracker.

## Phase 0 — socle commun

- [x] Thème par défaut = atlas de base (« Irongray »), option ajoutée
      au sélecteur de thème, `settings.cfg` remis sur Irongray
      (+ résolution par défaut 1280×720 = celle de la ref).
- [x] `SetTheme` re-mappe toujours les AtlasTexture (y compris vers
      l'atlas de base).
- [x] `EeSkin` : briques partagées (cadres 9-slice natifs, onglets
      DejaVu, case fleur-de-lys, barres dégradées, boutons gris).
- [x] ProgressBar : `ShowPercentage` désactivé à la source (texte
      HP/MP doublé, « 0% » parasites).

## Phase 1 — HUD permanent (ordre de traitement)

| # | Élément | Ref | Règles fonctionnelles |
|---|---|---|---|
| 1 | TopLeftVitals | `TopLeftVitals.png` | portrait dans le cercle, HP rose, MP bleu clair, badges lv perso/classe, grille 1-12 ; clic droit portrait → Reset dungeon ; mode loot du groupe visible |
| 2 | Barres EXP | `DoubleEXPbar.png` | EXP perso vert pomme + EXP classe violet, `XX.XXX%`, clic droit → choix précision |
| 3 | UltimateBar | — | **cachée** (feature custom différée) |
| 4 | ChatPanel | `ChatPanel*.png` | onglets haut-gauche, Settings haut-droite, Emoji, canaux ◀ Say…Buzz ▶, couleurs par canal |
| 5 | Hotbar | `HotbarPanel.png` | 2 rangées (C1…C+ / 1…+), page ▲N▼, ✕/+ et verrous, jusqu'à 3 lignes ; drag gauche, clic droit active |
| 6 | Menu icônes | `juste le jeu.png` | 1 ligne de 9 icônes bas-droite, badge « ! » |
| 7 | Minimap | `juste le jeu.png` | titre zone + globe, coords, boutons (mail, mall, altars, BF, arène, rankings, events) tous branchés, 2 horloges, panel un peu plus large |
| 8 | QuestTracker | `QuestTracker.png` | cadre gris translucide, losanges Portal Stone/Background, onglets boutons, « − » repli, objectifs cliquables → auto-follow |
| 9 | Buff/Nerf bars | shot_0045 | à gauche de la minimap, timer m:ss, stack, tooltip |
| 10 | TargetFrame | shots 0034/0037/0085 | portrait + nom + HP/MP + buffs + Target's Target + bouton inspect |
| 11 | CastBar | `TimedCastSkill.png` | barre ornée, **sans nom**, secondes restantes |
| 12 | ZoneBanner / textes centre | shot_0030 | titre zone jaune géant, Entering/Leaving Combat |
| 13 | PartyHud / PetBar / loot | — | clic droit membre → kick / whisper |
| 14 | GoldHud, MountHud | — | **supprimés** |

### Avancement phase 1 (capture `godot-shots/hud-refonte/01-*.png`)

- [x] 1 Vitals : calé (7,2), barres HP rose / MP cyan, textes ombrés,
      grille 1-12 grise, portrait masqué en disque, icônes contextuelles
      (pet, chef, PK, arène, FateRoad) cachées, barre XP parasite retirée.
- [x] 2 Barres EXP : piste sombre + vert pomme / violet, clic droit →
      menu précision XX…XX.XXX, Repair All parasite caché.
- [x] 3 Ultimate cachée.
- [x] 4 Chat : onglets Normal/Battle DejaVu en haut, « + Settings »,
      log 404 px, saisie sombre, 7 canaux jointifs (fond ChannelEnable,
      radio exclusif, Say actif), Mutually/Battlefield retirés.
- [x] 5 Hotbar : ancre native 0.2979×W, rangée 1-+ en bas, C1-C+ au
      dessus, 3e rangée via « + », ✕ replie, cases fleur-de-lys,
      verrous natifs, icônes bornées ; barre doublon SkillBarWindow
      supprimée (logique seule) ; clic droit = activer.
- [x] 6 Menu icônes : 1 rangée de 9 (ordre ref).
- [~] 7 Minimap : horloges locale HH:mm / serveur HH:mm:ss, coords
      « X:n, Y:n », bouton œil natif, zoom ×2.25 par défaut.
- [x] 8 QuestTracker : panneau gris, losanges, « — » repli, boutons
      gris, titres rouges, puces rondes, liste scrollable, clic
      objectif → événement `ObjectiveClicked`.

Points ouverts phase 1 :
- Icônes de niveau (couronne orange / aile bleue) des vitals et des
  barres EXP : introuvables dans les imagesets uidata (assignées par
  code natif) — à retrouver dans le RE.
- Pourcentage EXP : pas de table de paliers fiable côté client
  (`C_Level.ini` à décoder) → l'EXP brute est affichée en attendant.
- Portrait : cadrage OK mais visage sans yeux (texture visage du
  modèle, hors HUD).
- Auto-follow objectif : `ObjectiveClicked` à brancher sur
  `PlayerController.SetMoveTarget` (position PNJ/mob).

### Avancement phase 1 — HUD 9-14 (capture `02-hud-combat-groupe.png`)

- [x] 9 Buff/Nerf : BuffWnd collée à gauche de la minimap (y 58,
      remplissage natif droite→gauche, fond masqué), NerfBar juste à
      gauche, debuffs uniquement dans la NerfBar (plus de doublon),
      compteurs blancs bas-gauche. Buff live prouvé (`--buff-test`).
- [x] 10 TargetFrame : fenêtre native à (217,74), HP rose/MP cyan
      (MP pleine — non transmise), nom jaune, niveau gris, pas de
      chiffres HP pour un mob, BP/Weak*/type cachés, bouton natif
      MonsterInfoBtn = inspecter (fini l'emoji collé en haut-droite),
      Target's Target titré.
- [x] 11 CastBar : cadre orné DejaVu castingBar*, rose, 370 px centré
      à H-170, secondes restantes, sans nom de sort.
- [x] 12 Bannières : « Entering Combat » rouge-orangé / « Leaving
      Combat » blanc (label dédié), titre de zone jaune à y≈118,
      « You have acquired … state » rouge à y≈223.
- [x] 13 Party : un `TeamWndFrame.layout` natif par membre sous les
      vitals (niveau, couronne chef, nom, HP/MP, 10 enchants, grisé
      hors ligne) ; clic droit → Whisper / Kick (chef) / Leave /
      Invite target. Serveur : TeamLeave avec l'id d'un membre =
      exclusion par le chef (`TeamService.KickMember` branché).
- [x] 14 GoldHud / MountHud : déjà absents du code.
- PetFunctionBar : inchangée (layout natif, visible seulement avec un
  familier invoqué — pas de ref pour la comparer).

Points ouverts 9-14 :
- Portrait de la cible : médaillon doré générique de la ref introuvable
  (les `MONT*.dds` sont des portraits de monstres précis).
- Kick non prouvé en live (exige 2 clients) ; serveur à redémarrer.
- `--mob-test` ne trouve aucun mob kind=12 sur la map de test (spawn
  serveur) → cible/bannières validées via `--hud-open=target,cast,combat`.
- Règle de loot du groupe (381 kind 9/10) : correspondance des valeurs
  inconnue, toujours affichée dans le chat seulement.
- Tooltip buff stylé (titre blanc / desc rouge, shot_0045) à faire.
- PetBar (-180..-135) et castbar (-170..-152) se chevauchent si un
  familier est invoqué pendant un cast.

## Phase 2 — panels (après validation HUD)

### Avancement phase 2 (captures `godot-shots/hud-refonte/1x-*.png`)

- [x] Socle : palette sombre bleutée → anthracite chaud (97 couleurs,
      38 fichiers + `SkinUnstyled`) ; `EeSkin.MakeWindow` (liseré
      argenté natif, bandeau titre, croix native), parchemin, encadré,
      onglets, boutons gris, `PanelTheme` (losanges).
- [x] Inventaire « Backpack » (shot_0017) : 837×718, onglets, grille
      22×15 fleur-de-lys, or/argent, 6 sacs, Archive (désactivé),
      fonctions (Stall câblé).
- [x] Classe (ClassePanel.png) : liste 489 + Transform!, en-tête +
      Activate, Reg. Skills (or) / Class Skills (CP) selon C_Spell
      col42, +1 (12) / Max (623), 286 → niveau + CP.
- [x] Character Info (profile.png) : stats 296, aperçu 3D, équipement
      porté intégré, Title (Set Title 93), Achievement → fenêtre.
- [x] Quest (QuestLog.png) : arbre, compteur, détail/objectifs, Drop.
- [x] Region Map (carte.png) : carte de zone, Search Listing,
      City Features (filtres), Coord, auto-route (clic carte,
      double-clic liste) ; tracker : clic objectif → auto-follow.
- [x] Main Menu (SystemPanel.png) : Settings, Reset UI, Logout, Quit,
      Continue (Ch. désactivé).
- [x] Settings (ParameterPanel-*.png) : 340×574, parchemin, losanges,
      Display / Audio / Hotkeys / UI Settings, Default|Confirm|Cancel.

Points ouverts phase 2 :
- Icônes natives des fonctions du sac (Stack/Break/…) et des
  monnaies introuvables → libellés / pastilles.
- Capacité du sac non transmise → pas de cases grises « verrouillées ».
- Classe : License Knowledge / Class News sans données serveur ;
  tooltips de sort détaillés (coût, CD, niveau suivant) à faire.
- Profil : % des taux (CRIT, EVA…) — échelle native non décodée ;
  onglets Fame vide, colonne monnaies absente.
- Carte : vue de toute la zone (la ref zoome sur la ville), catégories
  par fonction de PNJ (marchands…) non mappées.
- Settings : onglet « Chr » (préférences d'affichage) absent faute de
  réglages correspondants.
- Non prouvé en live (interaction souris non injectée) : +1/Max,
  Transform!, Drop, auto-route, Reset UI, Logout.
- **Bloquant test combat** : le client se fige (plus aucun log, même
  réseau) après la migration vers Limestone Mtn. (`--mob-test`) ;
  même figement observé de façon intermittente au chargement.
- Restent : Community, Guild Recruitment, Mail, Event/Daily, Pet,
  Item Mall/Loyalty, Portal, Channel Settings, autres §3-§5.

Ordre : Inventaire → Profil (Stats/Title/Fame/Achievement) → Classe
(Class Skills/License/News) → Quêtes → Carte → Settings (5 onglets) →
Menu système → Community → Guild Recruitment → Mail → Event/Daily →
Pet → Item Mall/Loyalty → Portal → Channel Settings → reste
(`hud-interfaces.md` §3-§5).

## Preuves

Chaque case cochée = capture `godot-shots/hud-refonte/<élément>.png`
(non versionnée) comparée au crop ref + commit dédié.
