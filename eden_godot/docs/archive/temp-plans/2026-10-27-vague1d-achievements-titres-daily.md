# Vague 1-D — Succès & titres & daily reward (client Godot)

> Plan Phase 6 §Vague 1-D. Fichiers propres à ce lot ; seuls
> `game.tscn` (2 nœuds ZoneWindows), `ProfileWindow.cs` (section titre
> chirurgicale) et éventuellement `HudRoot.cs` (--hud-open clés) sont
> partagés — éditions minimales.

## Audit NCID (serveur `eden_server/server` — fait)

| NCID | Sens | Format (preuve) |
|---|---|---|
| 480 | S→C `CNC_CZ_ClientAchievementUpdate` | `[u8 kind]` ; 0/1 → `[u16 n][i32 id][u32 points]×n` (0 = synchro complétés par paquets de 315, 1 = upsert) ; 2/3 → `[u32 total]` (cumuls cat. monstre/ville) ; 4/5 → `[u16 n][i32 id]×n`. Désasm zone @0x08209080, `ZoneEntryFlow.Achievement.cs`. |
| 396 | S→C `ClientChangeAppellation` | `[i32 charId][i32 titleId][u8 seq]` — broadcast voyants (`Titles.cs`). |
| 397 | S→C `ClientUpdateAppellation` | `[i32 titleId]` — titre actif self. |
| 398 | S→C `ClientAddAppellation` | `[i32 titleId]` — titre obtenu (grant). |
| 93 | C→S `UpdateAppellation` | `[u16 titleId]` — pose/retire (0) le titre actif ; handler serveur `HandleSetAppellationAsync`. |
| 94 | C→S `QueryAppellation` | `[u32 charId]` → 396 + 280 attr 0x2c ; handler `HandleQueryAppellationAsync`. |
| 258 | C→S `DailyAwardOP` | `[s16 op][i32 param]` — op0 check jour, op1 requête état, op2 claim jour (GOLDEN). |
| 615 | S→C `ClientUpdateDailyAwards` | 26 B `[s16 dayIndex][i32 onlineS][i64 awardDays][i64 awardRecords][s16 type][s16 flag]` — codec `DailyAwardPackets` (désasm @0x81e9650). |

Limites serveur connues :
- La **liste des titres possédés n'est jamais émise** (natif idem :
  vecteur char+0x6d8 chargé DB sans NotifyClient) → le sélecteur ne
  propose que les titres vus en session (398) + actif (397).
- La **progression des succès incomplets n'est pas émise** (seuls les
  complétés partent en 480 t0) → la fenêtre affiche complété/non
  complété + points, pas N/M fin pour les incomplets.
- Pas de NCID « achievement réclamable » côté serveur — grants
  automatiques (kill/item/scène).

## Données client (raw_assets — vérifié)

- `data/db/c_achievement.ini` |V.4|15| : id|needNum|sortIndex|code(icône
  M0xx→`UI/itemicon/`)|nom Big5|type|col6|col7(→groupKey=c6*100+c7)|
  points|seuil|appellation|item|align|exp|desc.
- `data/db/T_Achievement.ini` (3|) : id → nom+desc EN (couvre aussi les
  groupKey 101-150 = noms de sections de quêtes).
- `data/db/c_title.ini` |V.3|6| + `T_Title.ini` (5|) : id|nom|catégorie|
  bonus+desc|obtention — noms EN de titres.
- `data/db/C_DailyAward.ini` |V.1|6| : jour|itemId|amount|?|required_s|?
  (jours 1-28 + spéciaux 29-32).
- Layouts oracles : `Achievement_Ver2.layout` (LeftPanel TreeList +
  3 totaux / AchiPanel liste slots), `LoginReward.layout` (blocks
  jour + GetBtn + Date), `AppellationWnd_ver2.layout`,
  `ref-shots/daily reward.png`.

## Implémentation

- [x] `scripts/protocol/ZoneAchievementMessages.cs` — codecs 480,
      396/397/398, 93, 94, 258, 615.
- [x] `scripts/protocol/PlayerAchievements.cs` — store 480
      (id→points, totaux 2/3, ids 4/5) + event.
- [x] `scripts/protocol/PlayerTitles.cs` — store titres (possédés
      session, actif, map autre-char) + `RequestSet` → 93.
- [x] `scripts/data/tables/AchievementTable.cs`,
      `TitleTable.cs`, `DailyAwardTable.cs`.
- [x] `scripts/hud/AchievementWindow.cs` — arbre catégories
      (groupKey/centaine + noms T_Achievement), liste succès
      (icône itemicon/code, nom EN, desc, points, état complété),
      totaux ; DragHelper+Persist ; bind 480 + 396/397/398 ;
      bouton « Daily » → DailyRewardWindow.
- [x] `scripts/hud/DailyRewardWindow.cs` — grille jours
      (icône item, qté, required_s, états
      complété/réclamé/courant/verrouillé) + claim (258 op2) +
      refresh (258 op1) + check login (258 op0) à l'entrée.
- [x] `ProfileWindow.cs` — ligne titre actif + OptionButton
      titres possédés → `PlayerTitles.RequestSet` (chirurgical).
- [x] `HudExtensions.MountAll` — montage des 2 fenêtres (même
      pattern que Mail/Auction — pas de nœud .tscn) ; `HudRoot` :
      clés `--hud-open=achievements|daily`, hotkey L, Échap.
- [x] `tools/NetTests/ZoneAchievementTests.cs` — round-trips
      + parse wire-format + stores.

## Preuve obtenue (run final)

- `dotnet build godot_project/EdenClientReborn.csproj` : **0 warn, 0 err**.
- `dotnet test tools/NetTests` : **361/361 verts** (dont les
  round-trips 480/396/397/398/93/94/258/615 + stores).
- Solo `res://scenes/game.tscn -- --solo --hud-open=daily,achievements
  --hud-dump --shot=` :
  - `[hud] --hud-open DailyRewardWindow → /root/GameScene/Hud/DailyRewardWindow`
  - `[hud] --hud-open AchievementWindow → /root/GameScene/Hud/AchievementWindow`
  - dump complet des rects ; capture
    `godot-shots/achievement-window.png` (1366×768).
- Live `--login=admin:adminmdp --channel --char
  --hud-open=daily,achievements --hud-dump` :
  - binds `[hud-ext] … + 1D 480/396/397/398/615 posés` à l'ouverture
    de session zone ;
  - `[wire] E NCID=615 len=28` reçu **avant** le montage des
    fenêtres → mis en stash puis appliqué (pas de perte) ;
  - `[wire] D NCID=258 0201 0100 00000000` = op1 « refresh » émis par
    `DailyRewardWindow.EnsureDemo` à l'ouverture ;
  - `[wire] E NCID=615 … kind=1 flag=1` = réponse serveur à op1 →
    **round-trip 258→615 confirmé sur le wire** ;
  - `[hud] --hud-open DailyRewardWindow → …` +
    `AchievementWindow → …` + dump rects des deux fenêtres.
- Non observé en live : 480/396/397/398 — le perso test1 n'a ni
  succès complété ni titre (aucun paquet émis par le serveur ; la
  grammaire est prouvée par les goldens/tests + le code serveur
  `ZoneEntryFlow.Achievement/Titles.cs`). Pas de chemin GM client
  pour forcer un grant (`set_achievement`/`eden-appellation` sont des
  commandes mission→zone via 58, pas joignables en chat).

## Bug d'intégration trouvé et corrigé

`GetTree().CreateTimer()` dans `HudRoot.BuildAll` (`--hud-open`,
`--hud-dump`) : le wrapper C# du `SceneTreeTimer` n'étant plus
référencé, il était collecté sous la pression GC du chargement live
→ le timeout n'était **jamais émis en live** (solo OK). Remplacé
par des `Godot.Timer` nœuds (`OneShot`, `Autostart`) enfants du
HUD — vérifié : `--hud-open` et `--hud-dump` tirent désormais en
live. `HudReady?.Invoke()` est aussi protégé par try/catch + log
(un abonné défaillant ne doit plus court-circuiter ces timers).
