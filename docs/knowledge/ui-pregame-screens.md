# UI — écrans pré-jeu : thème au boot, avatar sélecteur, chat « Obtenu »

Statut : **verified** (build Debug vert, NetTests 820/820, captures lues).
Version testée : `HEAD=611f5a4` + patchs locaux, Godot 4.8-beta2.mono,
.NET 10.0.402 — 2026-11-03.

## Thème HUD non appliqué au boot (cause racine trouvée)

**Symptôme** : en live (`--login --channel --char`), le thème sauvegardé
n'apparaissait que partiellement — `[theme] Purple` (swap d'atlas interne
à `HudRoot.BuildAll`) mais jamais `[theme-apply]` → palette `EeSkin`,
modulate et `ui_scale` non posés.

**Cause** : `SettingsApplier.TickThemeBoot` attendait le HUD + atlas avec
un budget de **1800 frames**. `GameScene._Ready` charge la map en async :
les `_Process` de l'autoload défilent à ~300-600 fps pendant les awaits,
et `MountHud` n'arrive qu'en fin de `_Ready`. Le budget (≈4-9 s réels)
s'épuisait toujours **avant** l'existence du HUD — trace `[theme-boot]
wait=1500→0 hud=null` pendant tout le chargement, puis `[theme] Purple`
après l'épuisement. Personne n'était alors abonné à `HudReady` (le hook
n'était posé qu'après un apply réussi) → `ApplyTheme` jamais appelé.

**Fix** (`SettingsApplier.cs`) :
1. Budget en **secondes** (`ThemeWaitSeconds = 120`, décrémenté par
   `delta`) — indépendant du fps de chargement.
2. `HookHud(hud)` dès que le nœud HUD existe, sans attendre l'atlas et
   sans dépendre du budget → `OnHudReady` (émis en fin de `BuildAll`)
   applique thème + palette + `ApplyUiScale` de façon déterministe.
3. `PrintErr` une fois si le HUD existe mais l'atlas n'est jamais prêt
   au bout de 120 s (diagnostic, jamais vu en pratique).

**Preuve** : run live → `[theme-apply] hud.Tex=ok theme=Purple
themed=True` imprimé juste après `[hud] monté` (avant : absent de tout
le log). Capture `/tmp/game_theme_after.png` lue : HUD violet complet.

## Avatar « manquant » du sélecteur de personnage

**Symptôme** : avec ~6+ personnages, le dernier avatar de la rangée
semblait absent.

**Cause** : `CharSelectScreen.FrameRow` cadrait la rangée sur toute la
largeur écran sans réserver la colonne d'actions (ancres 0,835-0,985 ×
0,50-0,97). À 1280×720, Fov 40°, Spacing 2,5 : le centre du 7e perso se
projette à x≈0,862 → sous les boutons (avatar + plaque + zone cliquable).

**Fix** (`CharSelectScreen.FrameRow`) : distance caméra bornée par
`centerOff / (safeNdc·tan)` avec `SafeRight = 0,75` — le centre du
dernier perso reste ≤ 0,75 écran (plaque incluse à ~0,83 < 0,835).
Rangée centrée inchangée pour n ≤ 5. Override QA :
`EDEN_CHARSEL_SAFE=1.0` restaure l'ancien comportement.

**Preuve** : `--screen=chars --mock-chars=7 --select-shot=` —
`/tmp/charsel7_before.png` (safe=1.0) : 7e avatar et sa plaque sous la
colonne ; `/tmp/charsel7_after.png` : les 7 avatars + plaques visibles,
bord droit ~x=1015 < 1069.

**Outil ajouté** : `--mock-chars=N` complète la liste par des persos
synthétiques (copie de liste, session intacte ; apparences recyclées
des persos réels/fakes) — banc de cadrage sans compte multi-persos.

Non prouvé / hors scope : compte réel à 7 persos non testé (la pile de
test n'a qu'un perso) ; `CharBodyCache.GetOrBuild` et le catch
par-avatar sont sains (Duplicate par slot, aucune exception partagée).

## Chat « 獲得/得到 » → « Obtenu »

Chaîne vérifiée bout-en-bout : serveur émet des noms d'objet Big5 ;
`HudRoot.AddChat` → `ChatLocalizer.Localize` remplace chaque run CJK
par `FrForCjk` : mots-système via `ChatLocalizer.SysWordFr`
(« Obtenu : »), sinon `UiTextEn.Translate`, sinon index CJK→id depuis
`c_item.ini`/`c_itemmall.ini` (parsés en **Big5**, col7) puis
`TextResolver.ItemName` (t_item.ini/t_itemmall.ini, **CP1252** via
`EdenEncoding.TableFallback`) — table FR correcte, encodages corrects.

Refactor : le mapping littéral `獲得/得到 → "Obtenu :"` est passé de
`HudRoot.FrForCjk` (privé, moteur) à `ChatLocalizer.SysWordFr` (pur) →
couvert par `NetTests/UiBootTextTests.Chat_ObtenuSysWordMapped`
(4 cas verts, 16/16 sur la classe).

Non vérifié : le paquet loot réel en jeu (pas de moyen reproductible de
déclencher un « 獲得 » serveur pendant la session) — la mécanique et la
table sont prouvées par tests et lecture du code.
