# Tuto IA — faire de l'UI et corriger des bugs sur Eden Portage

Public : une IA moins puissante que Claude. Objectif : reproduire la méthode
qui marche ici. Ce document est volontairement concret : chaque règle est
illustrée par un cas réel (refonte du panneau Settings + bugs de la session
du 2026-10-31).

---

## 0. Les 7 règles d'or

1. **Lire avant d'écrire.** `CLAUDE.md`, `.claude/COMMON_MISTAKES.md`,
   `eden_godot/AGENTS.md`, puis le code voisin. Ne jamais inventer une API :
   `grep` pour trouver un exemple qui existe déjà.
2. **Trouver la cause, pas le symptôme.** Un bug = une chaîne
   (donnée → serveur → paquet → client → rendu). Remonter la chaîne jusqu'au
   maillon qui ment, corriger CE maillon.
3. **Réutiliser l'existant.** Nouveau panneau ? Copier le chrome d'un panneau
   qui marche (`EeSkin.MakeWindow`, `DragHelper`, `HudWindows`). Jamais de
   couleur en dur : tout passe par `EeSkin.Palette` (sinon le thème casse).
4. **Une preuve runtime.** « Ça compile » ne prouve rien. Lancer le client,
   agir avec de vraies touches, capturer l'écran réel (Flameshot), lire
   l'image. Si tu n'as pas pu vérifier, **dis-le** (« non vérifié »).
5. **Petit et ciblé.** Un sujet = un commit. Ne pas refactorer ce qui n'est pas
   demandé. Ne pas supprimer les commentaires existants.
6. **FR + EN.** Toute chaîne visible : `UiTextEn.Translate("English")` +
   ligne `EN<TAB>FR` dans `godot_project/gamedata/ui_text_en_fr.tsv`.
7. **Ne rien casser ailleurs.** Après un changement : build client
   (`dotnet build godot_project/EdenClientReborn.csproj`) ET serveur
   (`dotnet build Eden.sln`) → 0 erreur.

---

## 1. Boucle de travail standard

```
Comprendre → Localiser → Reproduire → Cause racine → Corriger petit → Prouver → Noter
```

| Étape | Comment | Outil |
|---|---|---|
| Comprendre | Reformuler le bug en 1 phrase observable (« après X, je vois Y au lieu de Z ») | — |
| Localiser | Chercher le mot-clé de l'UI/du paquet | `grep -rn "mot" --include=*.cs` |
| Reproduire | Lancer le client, refaire le scénario | voir §5 |
| Cause racine | Lire les 2 côtés (client ET serveur) ; logs `[tag]` | DB: `docker exec eden-postgres psql -U postgres -d fndb1 -c "..."` |
| Corriger | Plus petit changement dans la bonne couche | Edit |
| Prouver | Capture réelle + log | Flameshot |
| Noter | Mettre à jour doc d'état si une case du plan est prouvée | docs/ |

**Piège** : le serveur qui tourne n'est PAS recompilé tout seul. Après une
modif serveur, `dotnet build` puis relancer les services
(`eden_server/start-native.sh`). Le dire à l'utilisateur plutôt que redémarrer
sans prévenir (il peut être en train de jouer).

**Piège** : un seul client Godot à la fois, et d'autres sessions peuvent en
lancer un. Utiliser `flock /tmp/godot.lock <script>` pour s'enfiler.

---

## 2. Cas d'école A — refondre l'UI du panneau Settings

### Symptômes rapportés
- « moche, pas compatible avec le système de thème (comme la mini-carte) »
- « des options dépassent sur les côtés »

### Diagnostic (ce qu'il faut regarder)
1. **Comparer avec un panneau sain.** Mini-carte / Archive utilisent
   `EeSkin.MakeWindow(...)` → chrome natif DejaVu, titre, drag, croix,
   `HudWindows` (Échap, premier plan), `DragHelper.Persist` (position
   mémorisée). Le Settings, lui, était un `Window` Godot embarqué
   (`scenes/settings.tscn`) : cadre générique, **aucun** lien avec la palette.
2. **Chercher les couleurs en dur.** `PanelTheme` mettait des violets
   `Color8(58,34,92)` pour les onglets et un `Tab(hud, …)` texturé figé à la
   construction → ne suit pas le thème. Règle : utiliser les fabriques
   *thémées* (`EeSkin.PanelTab`, `ParchmentBox`, `GreyButton`, `InsetBox`) :
   elles sont enregistrées dans `_themed` et **mutées en place** à chaque
   `ApplyThemePalette`.
3. **Chercher la cause du débordement.** Dans un `HBoxContainer`, un `Label`
   avec `CustomMinimumSize = (110,0)` + un `OptionButton` min 80 + un slider
   min 60 + marges > largeur de la fenêtre ⇒ la ligne dépasse. Un
   `ScrollContainer` avec scroll horizontal désactivé **ne rogne pas** : il
   étire/rogne mal. Cause = tailles minimales *additionnées*.

### Correctif appliqué (à imiter)
- `SettingsWindow : Panel` (au lieu de `Window`), construit comme les autres
  panneaux : `HudRoot.Pin` → `DragHelper.EnsureTitleBar` →
  `FillClientArea(EeSkin.WindowBody)` → `DragHelper.Persist`.
- Hébergé par `SettingsApplier` dans son `CanvasLayer` (marche aussi à l'écran
  login où le HUD n'existe pas). Le panneau est reconstruit si on passe
  login → jeu (cadre natif seulement avec le HUD) : `BuiltWithHud`.
- Thème : `EeSkin.PanelTheme` n'utilise plus que des styleboxes thémées.
  `ApplyContrast()` recalcule la couleur du texte selon la luminance de
  `Palette.Parchment` ; `RefreshChrome()` re-skinne le cadre natif après un
  changement de thème.
- Anti-débordement (règle générale) :
  - libellé **et** champ en `ExpandFill` avec `SizeFlagsStretchRatio`
    (5:6), **minimum 0** → ils se partagent la largeur, rien ne pousse ;
  - `ClipText = true` sur Label / CheckBox / OptionButton ;
  - CheckBox en `ExpandFill`, min `(0, 20)` ; Slider min `(0, 18)` +
    petit Label de valeur de largeur fixe ;
  - grilles à 2 colonnes : les cases `ExpandFill` min 0.
- Fermeture : `HudWindows.Close(this)` (pas `Hide()`), Échap géré dans
  `_Input` (utile à l'écran login), capture de remap d'abord.

### Checklist « nouveau panneau »
- [ ] Chrome via `EeSkin.MakeWindow` ou `EnsureTitleBar` (jamais un `Window`)
- [ ] Zéro `Color8(...)` en dur → palette/fabriques thémées
- [ ] Largeur : aucun enfant avec min > largeur disponible
- [ ] Textes `UiTextEn.Translate` + TSV
- [ ] `HudWindows.Register` (Échap, premier plan)
- [ ] Position persistée (`DragHelper.Persist("clé")`)
- [ ] Capture Flameshot comparée à `ref-shots/` + test avec 2 thèmes
      (Paramètres → UI Settings → UI Style) + test FR et EN

---

## 3. Cas d'école B — bugs gameplay (chaque ligne = un raisonnement type)

### B1. Deux montures en même temps / monture perdue au changement de map
- **Chaîne** : item monture (NCID ItemUse) → `ItemActionHandlers` →
  `RideApply` → `RideStates[charId]` + enchant persisté → paquets 302/283/545.
- **Cause 1** : la branche « monter » ne descendait pas l'ancienne monture
  (seul le re-clic sur le *même* item démontait). → appeler `RideRemove`
  avant `RideApply` si `RideStates` contient déjà le perso.
- **Cause 2** : à la migration de node le serveur renvoyait seulement le mot
  d'état 302 (bit « monté ») ; le client reconstruit sa scène (donc son
  `MissionBridge`) et n'avait plus le modèle (283 mask=2). → serveur :
  `ResendRideAfterMigrationAsync` (283 self-only + 545) ; client : relais
  statique `_carryRideKey` lié à la session, réappliqué si le pont est neuf.
- **Leçon** : un état « serveur » qui survit à un changement de scène doit être
  *renvoyé* au client, ou *conservé* côté client hors de la scène.

### B2. Rendre une quête avec un sac trop plein
- **Chaîne** : client op=2/4 `MissionListOp` → `QuestEngine.Events.cs` →
  `ApplyRewardsAsync` → commandes zone `eden-give-item`.
- **Piège** : « un slot libre » ne suffit pas. Les piles se fusionnent
  (durabilité = taille de pile), les items de la quête sont retirés *avant*
  les récompenses (ils libèrent des slots), et un `RewardChooseItem` compte
  pour **un seul** item (celui choisi).
- **Solution** : `HasRewardRoomAsync` = simulation (retrait des objectifs →
  fusion dans piles partielles → slots neufs) ; si faux, refus + texte
  système 633 (« Pas assez de place dans le sac à dos ») via la commande zone
  `eden-text <index>`, quête laissée « à rendre ».
- **Leçon** : valider **avant** de consommer/écrire ; refuser proprement avec
  un message, jamais en silence.

### B3. Arc (2 mains) + épée (1 main) visibles en même temps
- **Indice décisif** : requête DB (`equipment1` du perso) → seul l'épée est
  équipée. Donc la base est juste, l'affichage est périmé ⇒ bug d'état client.
- **Cause** : l'auto-déséquipement serveur (`ClearWeaponConflictsAsync`) envoie
  un 277 + 274 à l'acteur, mais ni 276 (observateurs) ni 282. Le client lisait
  `PlayerEquipment.Worn` et ne retirait pas le loc → modèle 3D de l'arc resté.
- **Solution** : client — le handler 277 « équipement → sac » appelle
  `PlayerEquipment.RemoveWorn(loc)` ; serveur — émettre aussi 276 (slot vidé)
  aux observateurs.
- **Leçon** : quand deux vues divergent (profil ≠ modèle 3D), cherche la
  source de vérité de chacune et le paquet qui ne met à jour que l'une.

### B4. CTRL + clic gauche dans l'Archive
- Copier le comportement existant (`InventoryWindow.OnCellClicked`) :
  `ItemPreviewWindow.Preview(id)`. Vérifier `mb.CtrlPressed` **avant** le
  traitement du clic gauche normal.

### B5. Animations de recul / diagonales arrière
- Vérifier d'abord les *données* : lister les clips des GLB
  (`move01..move04`, `jump01`) pour les 7 corps (b g m f k l c) — script
  JSON/GLB ou `scenes/anim_matrix.tscn` (log `[matrix] clips`). Un clip
  manquant retombe silencieusement sur `move01` (`MoveOrFallback`).
- Puis la *logique* : `DirectionalLocomotion.Record/AnimFor` (doc
  `docs/ANIMS-DIRECTIONNELLES.md` : S, S+A, S+D → `move04`).
- Puis le *live* : log `[dirmove] rec=1 anim=move04 cur=…`.
- Les `*2.slim.glb` ne sont pas utilisés par les joueurs ; ne pas les confondre.

### B6. « Grosses marques au sol » (coutures terrain)
- Vérifier d'abord vertex/normales/couleurs aux frontières de tuile
  (`tools/fsm_seam_dump.py S003.fsm 192 y` → tout identique) : ce n'est donc
  PAS la géométrie. Reste la splatmap (128², une par tuile, échantillonnée
  seule). Correctif : moyenner les texels de bord entre tuiles voisines
  (`TerrainBuilder.WeldSplatEdges`).
- Statut : correctif posé, **à valider visuellement** sur Tranquil Hill
  (S003) — si la ligne persiste, tester `--splat-layer=5` (splatmap brute).

---

## 4. Anti-patterns à éviter

- Corriger côté client un problème dont la cause est serveur (ou inverse)
  sans avoir lu l'autre côté.
- Ajouter un `if` spécial au lieu de comprendre la règle (ex. cas de stacks).
- Modifier `re/` ou `raw_assets/` (lecture seule).
- Muter la scène Godot hors thread principal (enqueue depuis les handlers).
- Cocher le plan sans validation utilisateur.
- Lancer deux clients Godot ; oublier de recompiler le serveur.
- Écrire des textes visibles sans passer par `UiTextEn`.

---

## 5. Recettes pratiques

**Lancer le client connecté (compte admin, perso test2)**
```bash
cd eden_godot
DISPLAY=:0 godot --path godot_project -- --login=admin:adminmdp --channel --char=test2
# + --settings-open pour ouvrir le panneau Paramètres
```

**Injecter une touche (XTEST) et capturer l'écran réel**
```python
from Xlib import X, display, XK
from Xlib.ext import xtest
d = display.Display(':0'); kc = d.keysym_to_keycode(XK.string_to_keysym('s'))
xtest.fake_input(d, X.KeyPress, kc); d.sync()   # … attendre …
xtest.fake_input(d, X.KeyRelease, kc); d.sync()
```
```bash
flameshot full --raw > eden_godot/godot-shots/<nom>.png   # puis lire l'image
```

**Interroger la base**
```bash
docker exec eden-postgres psql -U postgres -d fndb1 -Atc \
  "select loc,item_id,container_index from equipment1 where player_id=<id>"
```

**Retrouver qui fait quoi** : `grep -rn "NomDeClasse\|NCID 277\|eden-give-item"`.
Un paquet : serveur `ZoneEntryFlow*.cs` / `Handlers/*.cs`, client
`GameScene.cs` (Bind) et `protocol/*.cs`.

**Tests rapides** : `dotnet test tools/NetTests` (client),
`cd eden_server/server && dotnet test` (serveur).

---

## 6. Modèle de compte rendu final

```
Fait : <quoi, 1 phrase par bug, avec fichier:ligne>
Prouvé : <capture / log / test> ou « non vérifié : <pourquoi> »
Reste : <ce qui n'est pas fait ou à valider par l'utilisateur>
À faire par l'utilisateur : <ex. redémarrer le serveur>
```
