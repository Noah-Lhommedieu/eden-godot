# PLAN D'ACTION — UI, gameplay et systèmes (v2, 2026-10-30)

Plan exhaustif fondé sur **2 passes d'audit** (10 rapports d'agents :
inventaire UI/gameplay/stats/items + approfondissement par item).
Chaque item : **état réel** (fichier:ligne), **référence cible**
(ref-shot / knowledge), **travail** précis, **preuve attendue**.

## Références obligatoires par item

- **Visuel cible** : `ref-shots/` (105 PNG du client original)
  catalogué dans `docs/client-assets/ref-shots-catalog.md` — chaque
  image a une section « Spécifie : » avec verbatims UI ; lire la
  section correspondante AVANT de toucher une UI. Compléments :
  `docs/harvest/refshots-classes.md`, `class-expertise.md`,
  `expertise-stats.md`, `CAPTURE-MATRIX.md`. Trous connus : pas
  d'écran login/création, un seul loading (Aven), aucun coffre au sol,
  pas de panneau Archive ouvert (`shot_0017-0019` = boutons seuls).
- **Mécanique/données** : `knowledge/` (index `knowledge/README.md`)
  = conversion curée du dump `~/Bureau/edeneternal-fandom/` — les
  deux corpus sont **identiques** (wiki1 ≡ `raw/wiki-en` 12 840 pages,
  wiki3 ≡ `wiki-fr`, `wiki_csv_data` ≡ `wiki-tables`, wiki2 ≡
  `guides-fr` source ; `fandom_fr_dump/` = vide). **Toujours utiliser
  `knowledge/`** (indexé `pages-index.tsv` + `categories/`), jamais
  re-crawler le dump. Autorité par source :
  - `raw/wiki-en/` = **officiel** — mécaniques (archive, banque,
    warps, quêtes, monstres/BP, formules stats, traits héroïques,
    guilde, familiers, classes), 7 000+ tooltips item en DOM.
  - `wiki-tables/` (= CSV Vendetta `wiki_csv_data` ≡ xlsx) = **table
    numérique consolidée** que le wiki EN n'a pas : expertise classes,
    succès légendaires, titres, prays, monster stones, trophées,
    donjons, teintures. ⚠️ Valeurs Vendetta → filtrer `scope` dans
    `manifest.json` + lignes >90.
  - `wiki-fr/` + `guides-fr/` = noms FR officiels, montures multi-
    places, règles GvG, star/luna/meteor stones, crafts raciaux,
    quêtes FR détaillées.
  - **Absent de TOUT le corpus** : textes/tips de loading, données
    FPS/ping, terrain mesh, formule proc multi-hit (tooltips
    seulement), liste succès par zone hors Tranquil Hill → combler
    par `data/db/` + désasm.
- **Stats/ordre** : `docs/ETAT-SYSTEME-STATS.md` — source de vérité
  de l'ordre d'application (`StatPipeline` pre/pct/post serveur).
- **Layouts natifs disponibles** : `gamedata/uidata/layouts/` contient
  les `.layout` CEGUI d'origine — préférer `hud.Build("<layout>")` à
  une reconstruction main quand le layout existe (inventaire par item
  ci-dessous).

## Ordre de travail

Tout l'UI/visuel d'abord (A1→A6), puis gameplay/systèmes (B), puis
complétions stats (C). Dans chaque phase : fondations d'abord
(input/focus corrigent des symptômes qui touchent TOUS les panneaux),
petits panneaux avant les grosses refontes.

---

# PHASE A — UI / visuel

## A1. Fondations input — causes racine prouvées

### A1.1 — Lockups après combat/kill + Espace qui rouvre un panneau

Le verrou de cast **n'est PAS** le coupable : `SkillCast._Process`
(`combat/SkillCast.cs:154-157`) pollue l'échéance chaque frame
(`CastLock.cs:14-16` : 3s avant 279, castDs+2,5s après) et toutes les
sorties existent (279/281/287/410/302/313/IsDead). Les vraies causes
trouvées par l'audit :

| # | Symptôme | Cause exacte | Fix minimal |
|---|---|---|---|
| a | Tiré vers un loot indéfiniment | `TreasurePickup.cs:75-97` réémet `SetMoveTarget` chaque physics-frame et **n'écoute pas `Cancelled`** (AutoAttack `62` et SkillCast `146-150` eux s'y abonnent) | `_player.Cancelled += () => _lootTarget = null` à la découverte du player (`:59-60`) |
| b | Plus aucun déplacement/touche après clic monde | Chat focus jamais relâché : `GameScene.cs:3567` `_player.SetPhysicsProcess(!ChatHasFocus)` à chaque frame ; le LineEdit chat toujours visible (`ChatPanel.cs:467-470`) ; clic monde ne relâche PAS le focus (aucun `GuiReleaseFocus`) ; `SettingsApplier._Input` retourne tôt sur focus LineEdit (`:484-488`) → **hotkeys morts aussi** | `GetViewport().GuiReleaseFocus()` dans `PlayerController.HandleMouseButton` (`:381-414`) quand le clic n'est pas sur le HUD — corrige aussi le focus-bouton (d) |
| c | Anims de locomotion mortes après revive | `EntityRegistry.cs:1168-1173` : self `MarkDying` → `Animator.AutoLocomotion = false` ; le seul `=true` est `RemoteEntity.Revive` (`:228`), jamais appelé sur le joueur local | Sur `PlayerVitals.Revived` (lié `GameScene.cs:2781-2785`) → `_player.Animator.AutoLocomotion = true`. Bonus : gate `IsDead` dans `_PhysicsProcess` (un mort peut encore WASD) |
| d | Espace = jump + ré-activation du dernier bouton | `CeguiToGodot.Create` (`ui/CeguiToGodot.cs:373-447`) ne pose **jamais** `FocusMode` → tout Button cliqué garde le focus → Space = `ui_accept` qui re-tire + `eden_jump` | `FocusMode = FocusModeEnum.None` sur Button/TextureButton/CheckBox dans `Create` ; garder le focus sur LineEdit/TextEdit/SpinBox (saisie) et ItemList/Tree/OptionButton (nav clavier). Prouvé sûr : `HotbarPanel.HitSlot` (`HotbarPanel.cs:336-387`) fait déjà `FocusMode.None` + clic OK |
| e | Cible morte encore cliquable / auto-attaque sur cadavre | `PickEntity` (`PlayerController.cs:563-582`) n'exclut pas `e.Dying` ; le corps reste `Monitorable` jusqu'à `CorpseGraceSec` (`EntityRegistry.cs:1408-1416`) → 124 sur handle mort, NCID 5 sur cadavre | `if (e.Dying) continue;` dans `PickEntity` (ou `PickArea.Monitorable=false` dans `RemoteEntity.Die()`) |
| f | Migration : freeze voulu neutralisé | `GameScene.cs:3152` coupe la physique puis `:3567` la rallume chaque frame | Mémoriser `SetPhysicsProcess(false)` de migration dans la condition de `:3567` |
| g | `_lDown/_rDown` coincés (press monde, release sur GUI) | release consommé par le Control → transitoire | hygiene : reset si >6px sans press |

**Preuve** : tuer un mob après avoir cliqué un panneau → WASD OK ;
cast interrompu par kill → re-cast < 3s ; clic chat → écrire → clic
monde → déplacement sans Enter/Escape ; revive → anims correctes.

### A1.2 — Clic droit dans le vide → deselect targetframe

**Protocole (prouvé)** : C→S `ZoneTargetUpdate` NCID **124**
`[i32 handle]`, deselect = `Value=0` (`ZoneMessages.cs:177-184`).
Serveur `HandleTargetUpdateAsync` (`ZoneEntryFlow.cs:1247-1260`)
accepte 0, écho 444 `[0][0][0]` → client `ApplyTarget`
(`HudNetBridge.cs:320-347`) : `handle==0` → `UnmarkTargetRing()` +
`t.Clear()`. **Gap** : aucun chemin client n'envoie jamais 0.

**Travail** : branche `PickEntity==null` de `OnRightClick`
(`PlayerController.cs:497-519`) au **release sans drag** (le droit
sert aussi à la caméra — réutiliser le seuil >6px existant) :
`Send(new ZoneTargetUpdate{Value=0}); LocalTargeted?.Invoke(0);`.
Bonus : la frame garde un handle mort après revive — le 444-clear
résout aussi ça.

**Preuve** : target un mob, clic droit sol sans bouger → frame vide.

### A1.3 — Clic gauche dans le vide pendant drag item → annule  - NON FAIT

**État** : `InventoryWindow.Held` (static, `:900`) posé par
`OnCellClicked` (`:926-927`) ; annulé par `CancelHeld` (`:905`),
re-clic cellule, clic droit cellule (`:932`), `UpdateHeld` auto
(`:939-941`). **Aucun clearer sur release LMB monde** — la fenêtre
n'a ni `_UnhandledInput` ni `_Input`. Le pattern exact existe déjà :
`HotbarPanel._UnhandledInput` (`:761-780`) drop `Held` au release LMB
quand `GuiGetHoveredControl() == null`.

**Travail** : ajouter `_UnhandledInput` à `InventoryWindow` :
`LMB release && Held != null && GuiGetHoveredControl() == null`
→ `CancelHeld()` (annulation demandée par l'utilisateur ; le discard
natif `AskDiscard` `:780-807` reste pour le drag Godot hors cible).
Propagation : `_UnhandledInput` arrive à tous les abonnés
(PlayerController ne fait pas `SetInputAsHandled`).

**Preuve** : item pris au curseur, clic gauche sol → item reposé,
pas de déplacement intempestif supplémentaire.

## A2. Thème & contraste global

### A2.1 — Settings : refonte thème + contenu natif

**État** : `scenes/settings.tscn` = `Window` nu (340×574 forcé,
`SettingsWindow.cs:42-47`), 6 onglets hand-built hors thème.
**Natif** = **`OptionControlUIVer2.layout`** (existe dans
`uidata/layouts/`, chargé par le client d'origine) : 4 tabs
`TabVideo/TabAudio/TabSetting/TabHotkey` + Default/OK/Cancel
(~344×594). Ref : `ParameterPanel-*.png` (5 groupes) —
`ref-shots-catalog.md:510-546`.

**Gaps vs natif** (audit comparatif) :
- Video : manquent Models Low/High, Background, Outline Off/Char/All,
  sliders Char Draw/Field/**Max FX**, Special FX (Death/Blur/Water
  Wave/3D/Photo/Glare)
- Audio : manque **Mute** (natif = Mute + Music + SFX seulement)
- Hotkeys : manquent binds Party Member F1-F10, Sit/Stand, Toggle
  WPN, Attack, Community, Pet
- UI Settings : manquent **Chat Font Size** + Filter Effect ; note
  rouge « Avoid setting font size larger than UI »
- **Preferences : plus gros trou** — natif ~24 toggles diamond en 2
  colonnes (Name/Guild/Title/Vitals/Health Bar/Damage/Self-Cast/NPC
  Name/Critical Shock/Invert Camera/Refuse Trade/No Duel/No Party/
  Jelly Effect/Camera Follow/Left-click to route…) vs 4 actuels
  (`SettingsWindow.Prefs.cs:17-49`).

**Travail** : (a) basculer la fenêtre sur `hud.Build(
"OptionControlUIVer2.layout")` si le pipeline CEGUI le supporte —
sinon skin thème via `EeSkin.MakeWindow`/`ThemePalette` existant ;
(b) appliquer `ThemePalette` sur tous les contrôles (fond parchemin
+ header thème, contraste ≥ lisible sur thème Purple) ; (c) combler
les toggles manquants dans `ClientSettings`/`HudPrefs` (chaque toggle
= 1 pref + 1 checkbox — les prefs existent déjà pour les HP bars) ;
(d) `ui.show_ids`, `ui.show_perf` (A4.3) rejoignent Preferences.

**Preuve** : capture Flameshot settings Purple vs ref-shot ; toggles
persistés ; thème swap live.

### A2.2 — Profile / journal : contraste

**État** : Profile = hand-built `EeSkin` ; chat = **déjà layout
natif** `ChannelWndV2.layout` (`ChatPanel.cs:75`) avec palette
hardcodée (`:27-37` : Say blanc, Peer mauve 0.85/0.5/0.9, Party bleu,
Guild vert, Zone orange, World cyan, Buzz rouge clair, System rouge
0.92/0.25/0.2) — directions fidèles au ref (`ChatPanel.png` : Peer
violet, System rouge), hex natifs exacts non sourcés.
**Travail** : audit contraste sur fond parchemin des lignes profile
(stats, labels dim) + couleurs chat ; ajuster vers les couleurs du
ref-shot ; corriger les hex trop clairs sur thèmes clairs (la palette
est déjà centralisée = fix en un point).

## A3. Item tooltip refonte + IDs

### A3.1 — Tooltip item par type (le gros morceau)  - NON FAIT (il manque simplement le fait que la durabilité est totalement mal faite car mauvaise durabilité et parfois item qui n'ont pas de durabilité mais indiqué quand même, le reste c'est bon)

**État** : `hud/ItemTooltip.cs` `Build()` (`:100-232`) — UNE seule
voie générique : icône 36px (`UI/itemicon/{Code}.png`, col1), nom
coloré `QualityColor` (`:35-43` : ≤1 gris, 2 blanc, 3 vert, 4 bleu,
5 violet, ≥6 orange), bloc stats fixe, sockets (`ToolTips.png`),
étoiles enhance, durabilité, desc `$N$` (`12`=rouge, `35`=or),
bind `Flags&0x80`, pied `Sell {SellPrice/20}g`. `TypeLabel` (`:56-64`)
= 4 labels seulement.

**Spec native par type** (extraite des 11 ref-shots `item hover *` +
catalogue §5) — champs **parsés mais jamais affichés** :
- `PhysicalVariance` (col47) → « **Basic** P-ATK 24571 **(120 %)** »
- `Resistances[9]` (cols 63-71) → lignes résist élément vertes
- `CooldownDs/CooldownGroup` (21/22) → « Cooldown: 1 min. 30 sec. »
- `TrainMask` (col10) → restrictions classe en rouge (mask→noms via
  `ClassTable`)
- `AttackElement` (44), `MaxLevel` (15), `BuyPrice`/`RepairPrice`,
  `TypeCode` (col3 → catégorie « Gem / Head »)
- `UseEnchantId` (23) → bloc proc « When attacking, 5% chance… » via
  `EnchantTable.DescriptionOf()` (T_Enchant)
- Stats négatives : rendre en `Warn` rouge (trophy enchant : ACC -8%)
- `EquipLoc`=9 → « 2-Handed » ; nom de type arme via `EquipTypeTable`
  (C_EquipType.ini)
- Footer « Can't be Traded » → bit `Flags` col11 ≠ 0x80 (bit exact à
  confirmer côté `ItemParser`) ; « Already Bound » = instance `IsBound`
- Ligne cyan « **Fortification Level: +12** » + bonus verts (weapon 2)

**Kinds → layouts** : armes `0x11-0x1e`/`0x3d-0x42`, armures `1-6`,
gemmes `45`, glyphes `78`, scrolls costume `79`, pet gear `61-68`,
costumes `0x45-0x4d`, consommables `UseType 1/2`, blueprints/plans
(mats+produit via `CraftTable` `c_familycombine.ini` — clé jointure
workId à confirmer côté `WorkOrderParser`), pets légendaires
(`PartnerTable` — grades lettres + skills).

**Travail** : table `Kind/UseType → Compose(def)` (compositions de
sections partagées, pas 10 classes) ; ligne `#ID` footer (`Dim`).
**Preuve** : hover arme/consommable/blueprint/trophy = layouts
différents fidèles aux ref-shots.

### A3.2 — IDs au hover (items + skills + classes)

**État** : `SkillTip` existe (coût/cast/cd/desc) ; aucun id. Point
d'insertion : `ItemTooltip.Build`/`SkillTip` footer.
**Travail** : flag `ui.show_ids` (`ClientConfig.ApplyKnown`
`:87-117` + toggle Preferences A2.1) → `#itemId`/`#skillId`/
`#classId` en `Dim` sur tooltips inventaire, skillbar, classes,
équipement.

## A4. HUD

### A4.1 — Minimap : boutons + drag

**Audit géométrie** : l'hypothèse « bande drag recouvre les boutons »
est **fausse** — `HeaderRect` = y∈[-2,30] du root `RadarUI_Ver2`
(`HudLayout.cs:199-202`, item pin 262×210 à [W-270,8]) et tous les
boutons sont ≥y146 (`MinimapPanel.cs:351-352`). Le drag par l'entête
(~30px titre) **doit fonctionner** ; boutons câblés (`:198-230`).

**Travail** : repro runtime (un clic = une action attendue) — pour
chaque bouton (mail, zoom ±, carte/globe, hide, services) vérifier :
(a) le `Pressed` arrive (log), (b) la fenêtre cible existe/montée,
(c) pas de Control `MouseFilter.Stop` posé dessus par le layout
(`--hud-dump` pour les rects sérialisés). Corriger au cas par cas
(probable : fenêtres cibles absentes, pas le clic lui-même).
**Preuve** : chaque bouton réagit ; drag entête persiste après
re-login.

### A4.2 — MenuIcons (bas-droite) : cause racine prouvée

`HudLayout.ScanChildren` enregistre le **root** `FunctionMenu5pPet`
(550×385, `:101-113`) alors que `MenuIconsPanel` épingle le **child**
`MenuBackgrand` (312×40, `:64-78`). Résultat : `HeaderRect` = bande
fantôme déplacée ~310px au-dessus → **drag jamais armé** + la bande
fantôme peut **voler des clics monde** (arme `GrabArmed` →
`PlayerController` saute le press → motion>6px drag le root
invisible).

**Fix** : enregistrer `MenuBackgrand` au lieu du root — descendre un
niveau dans `ScanChildren` pour les roots CEGUI (`cegui_type` meta
posé `CeguiToGodot.cs:511`), ou repin le root sur le rect du strip.
**Preuve** : drag par le haut de la barre, position persistée.

### A4.3 — Status HUD : entête drag + resize = cases pas taille  - NON FAIT (les cases spawn a droite a la place de spawn a gauche, impossible d'augmenter verticalement la size de l'hud)

**État** : `BuffBar`/`NerfBar` ont drag+scale propres
(`BuffBar.cs:200-238`) — le scale agrandit les cases (refusé).
**Travail** : entête drag via `HudLayout` (pattern existant) ;
remplacer le scale par une grille `FlowContainer` dont on étend
colonnes/lignes (cases taille fixe `IconSlot`), scroll interne si
déborde.
**Preuve** : resize ×2 → 2× icônes même taille.

### A4.4 — FPS + ping (option préférences)

**État** : aucun overlay ; keepalive envoie `ZoneEmptyRequest.Echo`
(NCID 57, `EdenSession.cs:191-238`) **sans mesurer** le RTT — il faut
identifier le paquet de réponse serveur pour horodater.
**Travail** : timestamp à l'envoi → RTT à la réponse (lissé EMA) ;
overlay `Label` coin (toggle `ui.show_perf` dans Preferences A2.1) :
`Engine.GetFramesPerSecond()` + ping ms. Couleurs seuil (vert/jaune/
rouge).
**Preuve** : toggle on → overlay live ; off → rien.

### A4.5 — TopLeftVitals + EXP bar : logos trait héroïque

**État** : slots réservés — `TopLeftVitals.cs:52-55` `CharIcon` =
trait (`NatrueIamge{feature-52000}`), `HotbarPanel.cs:141-149`
`expBackGrand/icon` = trait, `SoulBackGrand/icon` = classe
(`JoBIamge{class}`). Textures existent : `UI/uiicon/NatrueIamge01-
08.dds` (8 bannières, `UI-uiicon.md:3015-3022`).
**Données** : traits = enchants **52001-52008** (`t_enchant.ini`
FR :14765-14772, ex. 52001 Chef = PV/PM +12%). Source serveur la
plus probable : colonne `create_flag` → `PlayerCharacterEntity.
CreationFlag` (choix trait à la création — **inférence à valider**,
`GetHeroFeature` natif @zone 0x840ace0). Wire : seuls NCID 571/529
le portent (écrivent 0 aujourd'hui).
**Travail** : lire `create_flag` (vérifier valeurs réelles en DB) →
exposer au client (petit paquet d'extension ou champ) → mapper
`feature-52000 → NatrueIamge%02d.dds` sur les 2 slots.
**Réf** : `TopLeftVitals.png`, `DoubleEXPbar.png` (l'icône EXP
gauche = le trait), `knowledge/guides-fr/pages/traits-heroiques.md`.

## A5. Panneaux — refontes

### A5.1 — QuestTracker : reflow + mini-titre mob  - NON FAIT

**Audit** : le reflow ne vient **pas** du hover — les lignes sont des
`Label` WordSmart (`QuestTracker.cs` ; pas de RichTextLabel) et le
hover ne fait que toggler un `Panel` de surbrillance sans
min-size (~`:350`). Le reflow vient de `Refresh()` : rebuild complet
à chaque **391** + resize `_scroll.CustomMinimumSize` → re-wrap.
**« Mini titre mob » absent** = la ligne cible sous le titre (natif :
« PNJ cible sous chaque titre ») — résolution du nom monstre/NPC
manquante sur certains types d'objectifs.

**Travail** : (a) figer les hauteurs : hauteur de ligne constante +
scroll dimensionné fixe (pas de min-size réactive au contenu) ;
(b) compléter la ligne cible pour les objectifs `MonsterKilled`/
`Collect` : nom monstre via `MissionObjectives` (type→mobId→
`MonsterTable`+`TextResolver` FR), nom NPC via `c_npc.ini` —
format natif losange coloré (bleu récolte/violet kill) + « x/y » ;
(c) à terme : bascule `QuestTraceWnd.layout` (natif, existe dans
uidata — les lignes items = `QuestTraceItem`).
**Réf** : `QuestTracker.png`, `QuestTracker .png` (verbatims :
titres « N. Nom » colorés par niveau, note jaune « Right-click the
quest book… »).
**Preuve** : hover ne bouge rien ; chaque objectif a sa cible nommée.

### A5.2 — QuestLog : refonte fidèle + objectifs cliquables  - NON FAIT

**Natif** = **`MissionLog.layout`** (existe) : « Quests: n/25 »,
case « Track All », arbre catégories gauche, détail droit avec
« Use Portal Stone » + « Targets to take: <Soul Guardian> [Go] »,
Shift+clic pour tracker, Share/Drop/Close.
**Données** : objectifs/récompenses **pas sur le wire** — parsés de
`data/db/c_mission.ini` (`MissionTable.Ensure` : col14 giver step,
**col16 script objectifs**, col17 submit NPC uid, col20 script
récompenses `RewardGold/Exp/Item/ChooseItem/ClassPoint/Reputation/
SpecialCoin` → `RewardLines()`). Wire = état seul : 348 register,
391 updates, 345 symboles `!`/`?`, 388 sheet, C→S 87 ops, 51 giveup,
**160** goto.
**État** : `QuestWindow.cs` hand-built (tree + RichText detail),
Share/Legend non câblés (`:15-16`), `_usePortal` + `MissionGoto`
déjà envoyés (`:180-182,270-274`).

**Travail** : (a) corps liste : lignes objectif losange+texte+x/y
comme A5.1, section récompenses = icônes items + lignes
XP/CP/or ; (b) « Targets to take » : chaque cible = lien [Go] inline
→ si `_usePortal` et NPC → NCID 160 flag=1 (voir B3), sinon
`QuestRoute`/`AutoRoute` vers coords (B1) ; (c) compteur « n/25 » ;
(d) évaluer la bascule `MissionLog.layout` vs habillage de
l'existant (décision à l'implémentation : le layout natif apporte
les panes mais la logique reste).
**Réf** : `QuestLog.png` + catalogue :472-480 (verbatims complets).
**Preuve** : comparatif ref-shot ; clic [Go] → TP ou route.

### A5.3 — Profile : hover descriptions stats (table prête)

**Natif** : hover core stat = split « **X + Y** » (X = naturel
classe/niveau/race, Y = gear/buffs/certs/talents) —
`Caractéristiques.md:70-81`. Aujourd'hui : boutons « ? » par bloc
(`ProfileWindow.cs:237-244`) + tooltip résist générique (`:504`).

**Table finale à embed** (knowledge + `ui_text_en_fr.tsv`) :

| Stat | FR | Effets |
|---|---|---|
| STR | FOR | P-ATK **+5/pt**, Blocage +1/pt |
| AGI | AGI | ESQ +1/pt, Parade +0.8/pt, VIT ATQ +0.5/pt, VIT INV +1/pt |
| INT | INT | M-ATK +5/pt |
| WIS | SAG | Guérison Gén./Perso. (ratio non publié) |
| LCK | CHA | CRIT +0.8/pt, CRIT MAG +1/pt, PRÉC +1/pt |
| ACC | PRÉC | Chance de toucher vs ESQ (cap 300%) |
| DEF | DÉF | Réduction base cap 70%, réduc DÉG CRIT cap 70% |
| EVA | ESQ | vs PRÉC, cap 100% |
| Parry | Parade | ÷2 dégâts, 2H seulement, cap 70% |
| Block | Blocage | annule dégâts, bouclier seulement, cap 50% |
| CRIT DMG | DÉG CRIT | ×1.5 base, cap 300% |
| Move SPD | Vitesse | cap 200% |
| 9 résists | Feu/Foudre/Glace/Nature/Lumière/Obscur (élément, cap 70%) ; Entaille/Frappe/Percée (physique, cap 50%) | +1%/pt de réduction |

⚠️ Ordre wire 296 ≠ ordre affichage : wire = {8,9,6,4,5,7,1,2,3}
(Holy,Dark,Ice,Fire,Lightning,Nature,Slash,Strike,Pierce —
`ProfileStatEmitter.cs:290-292`) ; labels natifs = Slash,Strike,
Pierce,Fire,Lightning,Ice,Nature,Holy,Dark.
**Travail** : table nom→description (TSV EN/FR) branchée sur le
tooltip de chaque ligne ; bonus : split X+Y si la base est exposable
(NCID 492 porte les bases — `ZoneVitalsMessages.cs:20-55`).
**Preuve** : hover STR → « +5 ATQ PHY, +1 Blocage /pt » (FR).

### A5.4 — « Position Score de Classe » = `ExpertiseWindows.cs`

**Trouvé** : le panel existe et bind de **vraies** données —
`hud/ExpertiseWindows.cs` (« Full Class Ability Bonus List » =
« Position Score de Classe (total des classes) », tsv:2633).
Données = **recomputé client** : `ClassChangeDialog.Classes` (NCID
489 : ClassId/Level/Style/…) × `ClassRankBuffTable.Bonus(classId,
level)` (`c_classrankbuff.ini`, rank = ⌊min(lv,100)×0.2⌋ — formule
native `RefreshJPAward`). Entrée : box bas-gauche de `ClassWindow`
(`:296-321`, icône `J001.png`, tooltip « Class Ability Bonus Total »
— ref verbatim « HP 10108, MP 3309, ATK 7133, Magic 9927,
G-Healing 407 »).

**Manques réels** (vs plainte user) : pas de stats « gagnées » par
ligne affichées en valeurs, scroll/width insuffisants pour noms
longs, 2e fenêtre `ShowDetail` (`:121-157`) = paliers 1..18
(lv=rank×5) mais sans la colonne cumul native.
**Travail** : afficher les stats **par classe** (valeur + stat name
par palier — la table `ClassRankBuffTable` donne {stat,valeur}),
`ScrollContainer` vertical dimensionné, min-width pour noms longs
(pas de clip), détail CE List fidèle au ref (paliers de 5, cumul).
**Réf** : `ClassAbilitybonuslogo.png`, `panel primaire d'expertise`
+ `second panel expertise` ; tables natives lv5→90 dans
`docs/harvest/class-expertise.md:90-118`.
**Preuve** : valeurs = formule serveur (stage `classrank`), liste
scrollable, aucun débordement.

### A5.5 — « Statistiques avancées »

**Wire dispo aujourd'hui** (NCID 296, 96B — tout listé dans
`ETAT-SYSTEME-STATS.md` §9 + détail champs : 5 cores totaux, 9
résists, MaxMP, P-ATK, M-ATK, DEF, 8 rates (Parry/Block/Hit/Dodge/
Crit/CritDmg/MCrit/MCritDmg ×100), 6 f32 (Movement, BuffAttack/
CastSpeed, DefenceReduce, MovementRate, CriDmgReduce) ; + 492 bases,
+ 493 level-stats). Donc : formules dérivées affichables via
`S_Parameter` (677 params serveur) — le panel montre la formule +
les valeurs injectées.

**Multi-hit = PAS sur le wire** : mécanique = commandes enchant
**MultiStroke 1031/2031/2032** (`EnchantCommands.cs`), args
`[chance1%, factor1%, chance2%, factor2%]` (max 2 coups extra,
`Procs.cs:125-223`, tunables `MultiStrokeMaxHits/ChanceMult/
DamageMult` dans `server.config.txt:24-26`). Sources : passifs
`EnchantService.GetAttackCommands` (`:405-417` — talents/certs/
buffs « +N% Double/Triple Hit ») + `ProcSources` (`:99-109`).
**Travail** : (a) panel style profile : sections Formules (param →
expression → résultat), Multi-hit (chance/facteur par palier +
**provenance** : nom talent/trophée/titre/buff source — les sources
sont déjà identifiables par enchant id → `T_Enchant` desc), divers
tryhard (variance, pierce, DefenceReduce, CriDmgReduce) ; (b)
**extension wire** : serveur agrège les args MultiStroke passifs
(comme `ProcSources`) → nouveau paquet ou champ d'extension pour
exposer double/triple chance%+factor%+sources au client.
**Réf** : `skill hover info` (Encourage +15% Double Hit),
`wiki-tables/{T/Trophies,M/Titles,M/Pet_Skills}.md`.

### A5.6 — Achievements : refonte + légendaires

**Natif** = **`Achievement_Ver2.layout`** + 8 layouts frères
(`Achievement{,Group,ViewWnd,Report,InputWnd,TipWnd,TipWnd_Ver2}`,
`AppellationWnd{,_ver2}`) — tous présents dans uidata, **aucun
utilisé** (`AchievementWindow.cs` hand-built).
**Données** : `c_achievement.ini` (~3900 rows) cols : 0 id, 1 count
requis, 2 sort, 3 icon, 4 name, 5 type, **6×100+7 = clé groupe**
(101-108/201-202 = Monster Conquest, 501-506 = Town), 8 points,
9 seuil, 10 titre, 11 item, 14 desc. Wire **480** kinds : 0 sync
complétés, 1 upsert points, 2/3 cumulatifs chasse/ville (4/5 jamais
émis). Titres : 396 broadcast, 397 self active, 398 nouveau ; C→S 93
équipe.
**Natif** (ref verbatim) : compteurs Total/Monster Conquest/Town ;
arbre catégories ; liste « Legendary Achievement 0.00% Complete » ;
tooltip « Achievement Points: 4 / Achievement Title: Secret Treasure
Hunter / Increase the Max HP +500 / **actif même non équipé** » ;
bas = Victory/Defeat Quote + Set Quote.

**Travail** : (a) UI conforme : arbre catégories (clé 6×100+7) +
liste + tooltip succès complet ; (b) **succès légendaires** en
catégorie propre, sous-catégorisés par nature — tables de
`wiki-tables/M/Legendary_Achievements.md` : stats / armes /
montures&costumes (entités lvl≥95 = hors périmètre, marquer) ;
(c) mini-panel détail au clic : objectifs (kill N / craft /
posséder) depuis cols 1/5/14 + progression `count vs required` ;
(d) honor title reward → lien titre (col10 → `t_appellation`).
**Réf** : `Profile-Achievements{,2}.png`, `profile-Title.png`
(arbre Honor Title ~21).
**Preuve** : succès légendaire → objectifs listés ; points/titre
affichés ; catégories natives.

### A5.7 — Loading screen : langue + positionnement

**Natif** : PAS un `.layout` — charge `.\ui\loadingframe\` :
`Loading_<nodeId>.dds` par nœud (~400 fichiers,
`docs/client-assets/UI-loadingframe.md`) + masques partagés
`animation/AnimeAlpha/BarAlpha/LAlpha`. Texte = `t_node.ini` :
**row[0]** id, **[1]** nom map, **[2]** lore long, **[3..]**
sous-zones, **dernier col** sous-titre (« Aven — The Island
Sanctuary »). FR existe : `gamedata/data/db/lang/fr/t_node.ini`.
Tip haut-droite = table tips séparée (à identifier dans data/db).

**État** : `LoadingScreen.NodeText` (`:502-530`) lit
`ResolveRawAsset("data/db/t_node.ini")` → **toujours EN**
(contourne `TextResolver`).
**Travail** : `TextResolver` sur `t_node` (lang/fr prioritaire,
repli EN — même mécanisme que les autres t_*) pour nom/desc/
sous-titre ; positionner titre haut + lore bas-gauche + tip
haut-droite + barre ornée bas (déjà proche, aligner px sur ref).
**Réf** : `changement de map - changement d'étape de connexion.png`.
**Preuve** : loading FR = nom+desc FR ; EN = EN ; char-select → map
couvert (même path `LoadingScreen`).

### A5.8 — Audit exhaustif panels : langue + thème (transverse)

**Demande** : « revoir TOUS les panels existant » + adapter **tout**
le jeu au choix de langue. Au-delà des refontes ci-dessus, chaque
panneau reçoit un passage systématique :
- **Langue** : grep `\bnew Label|Text = "` par panneau → chaîne dure
  → `UiTextEn.Translate` + entrée `ui_text_en_fr.tsv` ; texte data →
  `TextResolver` (le loading `t_node` est le cas connu, A5.7 —
  chercher les autres `ResolveRawAsset.*t_` qui court-circuitent la
  langue).
- **Thème** : chaque fenêtre via `EeSkin.MakeWindow`/`ThemePalette` ;
  les hand-built hors-thème listés : `settings.tscn`, `SystemMenuWindow`,
  `ExpertiseWindows`, `AchievementWindow`, `QuestWindow`, profile/log
  (A2.2) — le passage A2 applique le thème actif + contraste.
- **Inventaire des fenêtres** : `HudWindows`/`HudExtensions`
  registration + grep `MakeWindow(` → liste à cocher une par une
  (checklist dans le rapport de l'audit, pas de sous-item).
**Preuve** : bascule langue → aucun texte résiduel EN sur un panel
FR (sauf repli documenté) ; capture par panneau en Purple.

## A6. Monde / entités visuel

### A6.1 — Nameplates : contraste + lisibilité + HP bars  - NON FAIT

**État exact** (`EntityRegistry.cs:517-533`, `HpLossAnim:153-229`) :
| Rôle | Actuel | Ref natif |
|---|---|---|
| Joueurs/prominent | cyan (0.55,0.95,1) | ok |
| NPC | vert-jaune (0.75,1,0.45) | ok |
| Mob aggro | rouge (1,0.28,0.25) | ok |
| Mob passif | jaune pâle (1,0.97,0.62) | ok |
| Mob trivial (écart≥8) | gris (0.62,0.62,0.62) | **trop sombre → 0.72** |
| Elite/Boss | **jaune comme passif** | **rouge** (`MonsterType>=2`) |
| Treasure | or (0.9,0.8,0.3) | ok |
| Self bar | rouge sombre fixe | **rose/pink** (ref `juste le jeu`) |

Natif : nameplates = quads billboard procéduraux (aucun imageset
3D — confirmé) + toggles préférences par catégorie (NPC Name,
Health Bar…). `IsEliteOrBoss` existe (`MonsterTable.cs:38`), jamais
utilisé pour la couleur. `VisibilityRangeEnd` label=90 vs corps=85+6
→ trou 90-91u.
**Travail** : élites → rouge ; gris → 0.72 ; self bar → rose
(0.95,0.4,0.55) ; aligner ranges (96) ; outline/shadow renforcé ;
nom propre affiché derrière pref (le ref le montre).
**Réf** : `shot_0034/0037-0039`, `targetframe (Elite mob).png`,
`juste le jeu.png`, `TailleMob.png`.
**Preuve** : capture multi-mobs à distances variées, tout lisible.

### A6.2 — Montures : placement par monture  - NON FAIT

**État** : lift empirique — local `VisualTopHeight × 0.38`
(`MissionBridge.cs:416-418`), distant `× 0.72` (`EntityRegistry.cs:98`,
commentaire « HYPOTHESIS : os de selle natif non porté »).
**Données trouvées** : `RideList.ini` (oracle) = 11 cols :
[0] code rNNNxyy (variantes texture/scale), [1] model.kfm, **[2]
scale** (0.5-2.0), [5][6] vec3 (offsets, 0 partout observés),
**[9] f5=1.0-5.0 variable par monture** (r041→1.5, r012→2.0,
r021→3.0, r091→4.6, r501→5.0) = meilleur candidat **hauteur selle**.
Pas de string Saddle/Seat dans rf_game (gap, pas preuve d'absence) ;
os selle GLB non vérifié.
**Travail** : table par **stem modèle** (variantes partagent le stem)
`{liftY, backZ, yawDeg, scaleMul, weaponLoc}` ; seed liftY/scaleMul
depuis cols 2+9 après confirm sémantique (`reindex xref` sur le
parser RideList — à faire à l'implémentation) ; unifier 0.38/0.72 en
une lookup ; vérifier un bone selle dans `assets/models/ride/model/
R012.glb`.
**Réf** : `NouveauSkillApresLevelUp.png`, `wiki-fr/pages/Montures.md`.
**Preuve** : 3-4 montures → assise/arme correctes.

### A6.3 — Coffres au sol par rareté  - NON FAIT (y'a bien un model mais aucune preuve que y'a tous les models bien mis pour les différents rareté ou type de drop, et toute façon les models sont trop petit au sol et certain drop sont sans model quand même).

**État** : 329 → capsule dorée (`EntityRegistry.cs:943-951`);
pickup OK (`TreasurePickup`, NCID 35→386).
**Lien item→modèle (prouvé)** : `c_item` **col3 `TypeCode`**
(G00001-G00010 = famille coffres, chaque `item/model/G*.nif` +
`item/texture/G0000x01.dds` principal + `G0000x5x.dds` **halo** —
ex. G00006 « coffre orné or et rouge » + halo violet G0000651).
Rareté = col19 `Quality` → teinte halo `QualityColor`. **Zéro
changement wire** (itemId résout tout).
**Travail** : `item/model/<TypeCode>.glb` (vérifier présence dans
`assets/models/item/model/` au dev) + halo billboard teinté par
quality ; fallback capsule 0.35u teintée ; label item déjà ok ;
vérifier raycast pickup sur modèles réels.
**Preuve** : drop blanc vs orange → skins distinctes, ramassage ok.

### A6.4 — Coutures terrain (Tranquill Hill x318 y192)

**Diagnostic précis** : tile = 64u (33×33 verts, `FsmFile.cs`); **y
=192 = exactement la frontière de tuile** (192/64=3) — le point user
est une frontière réelle. Causes classées :
1. **Vertex-COLOR discontinu** (plus probable) : lighting baké par
   vertex dans le .fsm ; si les verts de bord dupliqués portent des
   couleurs différentes → ligne d'ombrage (`COLOR` × albedo dans le
   shader `:81`).
2. **Splat border-texel** : rows de bord de 2 splatmaps 128²
   adjacentes divergentes → ligne de texture.
3. Gap géométrie : improbable (positions absolues identiques), sauf
   tuile absente → skirt.
Normales : bakées globales éditeur (pas tile-local clampé) → pas la
cause. `fract(wuv)` UV = convention native, pas un bug.
**Travail** : d'abord **dump** des verts de bord des tuiles N/S de
y=192 (positions/normales/couleurs) → si couleurs diffèrent :
**moyenner les couleurs des verts de bord partagés** (fix le moins
cher) ; si positions/normales diffèrent : weld-by-value.
**Preuve** : capture avant/après au même spot.

### A6.5 — Archive (interface sac)  -  - NON FAIT (codé mais sans preuve de fonctionnement)

**Serveur complet** ; client = données liées + bouton stub
(`InventoryWindow.cs:218`).
**Sources (ordre d'autorité — bien choisir)** :
1. **`knowledge/raw/wiki-en/Archive.html`** (officiel — retrouvé) :
   archive = « trophy rack » personnel **partagé compte** ; ouverture
   = sac (touche B) → bouton **Archive en bas** ; affiche **tous les
   items archivables du jeu** (collection complétionniste) ; tout
   item archivé reprenable par n'importe quel personnage du compte,
   **même no-trade** ; items archivables = item mall, craftés,
   rare drops ; unbind familier (10g) le rend archivable.
   Compléments : `Bank.html`/`Portable_Bank.html` (≠ archive,
   stockage perso), `Trophées.md` (452 trophées archivables).
2. **`wiki-fr/pages/Montures.md:13-34`** + `shot_0017-0019` : forme
   UI = **4e onglet du coffre/sac** via icône valise jaune, dépôt
   drag→slot bas-gauche, retrait clic droit, halo = rareté.
3. **Protocole/données** : `S_Collection.ini` cols : 0 item_id carte,
   2 flag (special→key 1001/1000), 6 locate (mask région, gate dépôt
   err 0x67e), 7 points/carte, 8 group. `S_CollectionPoint.ini` :
   0 id, 3 enchant, 4-13 = 5 paires (key,threshold) → enchants
   permanents au seuil. Wire : **152** query→472 type0
   `[points][n×itemIds]` ; **150** dépôt (flag 0=single / batch) →
   472 type1 + award refresh ; **151** retrait.
**Travail** : fenêtre/onglet Archive depuis `InventoryWindow` :
grille des items archivables (`ItemTable` + filtre `S_Collection`),
état possédé/manquant par `PlayerProfileState.Collections`
(bookId→cardIds) ; dépôt drag→NCID 150, retrait clic droit→151 ;
footer points totaux + aperçu reward au seuil
(`S_CollectionPoint` → `T_Enchant` desc). Rendre compte partagé :
aucun stockage perso local — tout passe par le serveur.
**Preuve** : déposer carte → 472 maj + points ; retirer → item ;
item archivé listé dans la grille collection.

### A6.6 — Costumes : équipement + stats + visuel  - NON FAIT

**État** : visuel via `4pequip.ini` + `CharVisual.ApplyEquipment` OK.
**Données trouvées** : kinds costume = **0x45-0x4d** ; conteneurs
d'équipement **2/3/4 = pages costume** (`kind%3 → page`,
`InventoryHandlers.Helpers.cs:79-198`) ; ces bags sont **skippés par
`EquipmentStatService.Sum`** → les stats costume passent par les
**enchants posés sur les items costume** (scrolls kind 79 inlay →
witchcraft) qui entrent dans le stage `buffs`/`SessionEnchants`.
⚠️ Inférence : vérifier que le natif donne bien les stats costume
par ce canal (vs stage dédié).
**Travail** : (a) slots UI costume si absents (conteneurs 2-4 →
panneau équipement page costume) ; (b) résolution des enchants
portés par les items en bags 2-4 dans la fiche stats (stage ou
extension `Sum`) ; (c) vérifier le rendu visuel équipé (mesh
`#B230`/`B1246`/`X652A`/`NULL`, dye hex).
**Réf** : `knowledge/categories/costumes.md`,
`guides-fr/pages/costumes-legendaires-disponibles-en-jeu.md`.
**Voir** : `ETAT-SYSTEME-STATS.md` § costumes/gemmes.

### A6.7 — Mini panel de prévisualisation item (simulateur visuel)  - NON FAIT (codé mais bizarre, nécessite une refonte visuel entière, rajouter clique droit pour déplacer verticalement la caméra de l'inspection)

**Demande** : fenêtre qui rend le personnage/monture avec un item
appliqué — tester costumes, montures, star stones, luna rocks, dyes
avant achat/craft. **Précédent natif** : `raw/wiki-en/Costume.html`
— aperçu costume par **CTRL+clic** sur l'item (même mécanisme à
étendre aux montures/gemmes).
**Réutilisable (déjà en place)** :
- `CharDressing.cs` — habillage aperçu en char-select (mesh switch
  par item code, exactement ce qu'il faut en jeu)
- `CharVisual.ApplyEquipment` — applique `4pequip.ini`
  (mesh `#B230`/`B1246`/`X652A`/`NULL`, skin, dye hex)
- `CharModelSpec.LoadEquipMap` — table item→visuel
- Rides : `CharVisual.cs:1116-1148` (stem `r{key}` →
  `ride/model/<stem>.glb`, anim attach) — même chemin pour prévoir
  une monture
- `SubViewport` + `WorldEnvironment` : viewport 3D isolé dans un
  Control (pattern standard Godot 4)
**Données** : star stones/luna rocks/meteors/monster stones — pas
de page EN ; listes dans `wiki-tables/misc/Useful_Info.md` (4
Meteors +5%, 5 Star Stones +7% 1h, 3 Luna Rocks +3%, Monster
Stones) + `guides-fr/pages/meteores-etoiles-et-spheres.md`
(acquisition + tables variantes).
**Travail** : petit panel (style `EeSkin.MakeWindow`) :
`SubViewport` 3D (perso nu ou monté) + liste d'items à tester
(clic → `ApplyEquipment`/ride stem sur le mannequin) + rotation
souris du modèle ; entrées : CTRL+clic item dans inventaire/
tooltip (natif), bouton dédié, item mall. Réutiliser le mannequin
du char-select — ne pas dupliquer le pipeline d'habillage.
**Preuve** : CTRL+clic costume → mannequin habillé ; monture →
perso sellé ; dye → teinte appliquée.

---

# PHASE B — Gameplay / systèmes

### B1 — Auto-follow / pathfinding réel

**État** : ligne droite (`PlayerController.cs:689-701`), abandon au
mur (`:645-668`).
**Grille `.smp` (décodée)** : header « SMP2.0 », grille **2w×2h
cellules de 0.5u**, `Cell(x,y)=floor(x*2)` sans offset, 4 cellules/
octet MSB-first ; 0=libre, **0x40=obstacle**, 0x80=non-bloquant
(`NodeMapFile.cs:76-89`). **Dilatation native +1 cellule** (8-
voisinage) pour le pathing — rf_game `FUN_00500750` (preuve doc ;
citation désasm à faire à l'implémentation).
**Route** : `QuestRoute` = BFS inter-map sur `SceneIndex.Gateways/
EventAreas` + `c_node.ini`, avance sur `PortalArrival` — **garder
tel** ; chaque saut intra-map appelle `AutoRoute.ToEden`.
**Travail** : (a) A* sur grille dilatée (8-dir, heuristique
Manhattan, centres cellule `(cx+0.5)/2` → `WorldConv.ToGodot`) ;
(b) **splice unique `AutoRoute.ToEden` (`AutoRoute.cs:26-34`)** :
file de waypoints → `SetMoveTarget(wp[0])`, avance quand
`MoveTarget==null` (arrivée `:693-697`) ; annulation déjà gérée
(input clavier + `Cancelled`) ; (c) chaque hop `QuestRoute` bénéficie
du pathing automatiquement.
**Preuve** : route autour d'un mur invisible → cible atteinte ;
route inter-map portails complète.

### B2 — Move speed 300 % → buff stat + config.txt  - NON FAIT (a vérifier l'existant)

**Formule résolue** (`NativeCombatFormulas.cs:875-907`) :
`BaseMovement=9.425 × buff(min(1+bonusPct/100, cap 200%)) ×
TempMoveSpeedMul` ; `bonusPct = MoveSpeedBonusPct +
BonusMoveSpeedPct + agiTerm`. **`BonusMoveSpeedPct` = enchant
command `2051`** (preuve : mount enchant 58991 `2051|40` = +40%,
`EnchantService.GetRideInfo`).
**Donc** : buff dev = enchant S_Enchant `2051|200` (+200%) posé à
l'entrée via `AddPlayerEnchantAsync` (`ZoneEntryFlow.Status.cs:282`)
— passe par le stage `buffs` → `Movement()` → **296 Field30/34**
(déjà émis). Config : `DevMoveSpeedBuffId` dans `ServerConfig`
(parse à côté de `movespeedbasepct` `:243`).
**Client** : le 296 porte la vraie vitesse mais `PlayerController`
l'ignore (`TempMoveSpeedMul=3f`, `:44-49,704-707`) → câbler
`PlayerVitals.TotalStats.Speeds[0]/9.425` comme multiplicateur.
**Preuve** : vitesse 300% via buff (icône masquable `flags&0x1000`),
config change → valeur change, retrait buff → vitesse normale.

### B3 — Portal stone : TP gratuit par état  - NON FAIT (a vérifier car je ne vois pas de status)

**État** : NCID 160 `flag==1` consomme 1 pierre (0x9C54/0xA476,
scan live `player_items`, refus silencieux) — `flag==0` = gratuit
existant.
**Travail minimal** : dans `HandleMissionGotoAsync`
(`ZoneEntryFlow.PortalStone.cs:34-52`) — sauter la consommation si
le joueur porte l'état « TP libre » : options classées
1) **`st.Privilege > 0`** (flag GM, déjà chargé — diff d'une ligne,
dev-only) ; 2) enchant id réservé (VIP — plus lourd, contenu) ;
3) champ `EntryState` (plomberie inutile).
Client : checkbox « Use Portal Stone » (`QuestTracker.cs:38,78-79`,
`QuestWindow.cs:41`) — label « (gratuit) » si état actif (optionnel).
**Preuve** : avec état → TP sans perte ; sans → consomme 1.

### B4 — Coffres rareté : voir A6.3 · B5 — Ping : voir A4.4

---

# PHASE C — Complétions stats (ordre détaillé : ETAT-SYSTEME-STATS.md)  - NON FAIT (a vérifier)

1. Stage `equipment` explicite (injection ad hoc → stage nommé).
2. **Gemmes** : `embedded_id1-6` → `S_Item` stats dans `Sum`.
3. **Costumes** : enchants sur bags 2-4 (A6.6).
4. **Fortify +N** : appeler `EnchantBonus` (param55/56) + lire
   `magicbox_enchant1-3`/`gold_enchant`.
5. **Buffs guilde/GvG** : aucun mécanisme — à concevoir.
6. **Règle d'or** : nouvelle source = stage nommé ordonné dans
   `Program.cs` — jamais d'injection ad hoc.

---

## Annexes de preuves

- Lockups : tableau complet des 13 chemins de blocage et leur sortie
  (audit input — §A1.1 ci-dessus en résumé ; détail complet dans le
  rapport agent, cast lock confirmé borné).
- NCID 296 : 35 champs listés `ETAT-SYSTEME-STATS.md` §9.
- Layouts natifs inutilisés mais disponibles : `QuestTraceWnd`,
  `AutoQuestList`, `MissionLog`, `QuestWnd`, `Achievement_Ver2`+8,
  `OptionControlUIVer2`, `OptionMenuUI` — évaluer bascule
  `hud.Build()` à l'implémentation de chaque panneau.
- Vérifications restantes à l'implémentation (marquées ⚠️) :
  dilatation .smp verbatim désasm, colonne `create_flag`=trait,
  jointure blueprint→workId, bit « Can't be Traded » exact,
  sémantique cols RideList 9, bone selle GLB, dump verts frontière.
