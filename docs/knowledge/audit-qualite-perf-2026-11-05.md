# Audit qualité de code + perf client/serveur — 2026-11-05

Statut : **verified** pour les 3 correctifs de lag (mesures `--hitch`/`--perf`
avant/après, banc `tools/run.sh -- --login=test123:test123 --channel --char=TestGirl --perf`).
Les constats « patterns » (§4) sont des comptages `grep`, pas un audit ligne à ligne.

## 1. Lags mesurés et corrigés

| Symptôme | Cause (mesurée) | Correctif | Résultat |
|---|---|---|---|
| Mini-freeze 0,1 s à chaque kill avec XP | Le serveur renvoie 489 (liste de classes) à chaque gain d'XP de classe (`ZoneEntryFlow.Combat.Rewards.cs`, `HandleQueryClassListAsync`). Le client le prenait pour un changement de classe : `EquipSlotsPanel` refaisait toute la teinture du corps (`CharVisual.ApplyEquipment`), `ClassWindow.Refresh` ×6 sous-refresh. 4 kills = 4 re-teintures, hitch 147 ms / 512 objets. | `EquipSlotsPanel` : reconstruction seulement si `(classe, style)` change. `ClassWindow` : `OnClassesChanged` ne rafraîchit que fenêtre ouverte. | Plus de re-teinture au kill. |
| Freeze au level-up de classe | ~70 paquets 286 (un par sort) d'affilée, chacun relançait `PlayerSkills.Changed` (hotbar 36 slots + fenêtre Classe) = 70 reconstructions. | `PlayerSkills.SetLevel` groupe `Changed` à une fois par image (`Defer`, posé par `SpellFx._Ready`). `ClassWindow.RefreshSkills` saute si masquée. | Propriétaire : « aucun lag quand je up ». |
| 25-35 fps pendant ~7 s après l'entrée en jeu (frame 17→41 ms, rampe linéaire, puis 4,4 ms net à t≈12 s) | `KfAnimSet.PumpWarm` ajoutait **une** animation par image à la `AnimationLibrary` vivante du joueur (~300 anims : `AnimNames.CharWanted`). Chaque `AddAnimation` invalide le cache de l'`AnimationPlayer`, reconstruit en O(pistes totales) à l'image suivante → coût croissant. Identique sans la carte (`--strip=MapModel/Objects`) : ce n'est pas le contenu de scène. Repéré par les lignes `[atkprobe] Ensure g.kf:rshootNN` qui s'arrêtent à `rshoot79` pile quand les fps remontent. | `PumpWarm` : budget 4 ms/image, construction dans une zone de transit (`_staged`), publication par lots de 100 (`FlushStaged`) ; `Ensure(name)` publie immédiatement si l'anim demandée est en transit. | fps ≈ 220 dès t=7 s (avant : t=12 s), plus de rampe. |

Autres coûts mesurés à l'entrée (non traités, ponctuels) : `ClientSeeMob` jusqu'à
70 ms (1ʳᵉ création d'un mob), `misc:495` 63 ms (1ʳᵉ anim d'un mob),
`ClientMissionStates` 29-33 ms, `ClientEnterNodeEquipAndSpell` 20 ms.
Préchauffage ajouté : `SpellFx.WarmKnownSpellFx` parse en arrière-plan les .nif d'effets
des sorts connus (`fx:s11711` = 32 ms sur le thread principal au 1ᵉʳ cast).

Outils ajoutés : scopes `proc.*` (`HudRoot`, `Hotbar*`, `Player`, `Registry`,
`GameScene`) dans les `_Process` — visibles dans les lignes `[hitch] … events:`.

## 2. Méthode qui a marché (à réutiliser)

1. Lancer avec `--perf --hitch --hitch-ms=25` ; lire la **tendance** des lignes `[perf] t=` (pas que les pics).
2. Une rampe ou un plateau borné dans le temps = travail étalé par image : chercher des
   logs répétitifs (`Ensure …`) qui s'arrêtent à l'instant où les fps remontent.
3. Bissecter avec `--strip=…` : même courbe sans carte = ce n'est pas le rendu de la scène.
4. `proc-dump` (fin de run `--perf-seconds`) liste les nœuds en `_Process`.
5. `eu-stack -p <pid>` donne des piles natives mais sans symboles managés : peu utile ici.

## 3. Dette perf restante (classée)

1. **450 `CooldownOverlay` en `_Process`** (un par case d'inventaire/hotbar), chacun interroge un
   delegate à chaque image. ~1 ms, mais pur gaspillage : passer à un ticker central ou
   `SetProcess(true)` seulement pendant un cooldown (`CooldownTracker` événementiel).
2. **Chargements synchrones au 1ᵉʳ usage sur le thread principal** : première création d'un mob
   (`SetVisual`/`SeeMob`), 1ʳᵉ anim d'un stem (`Ensure` hors `WarmAsync`), `LoadNativeScene`
   (parse .nif) pour les FX non préchauffés. Généraliser le modèle « chemin résolu sur le thread
   principal, parse sur le pool, assemblage par lots budgétés ».
3. **`ClientMissionStates` 30 ms** à l'entrée : construction d'UI quêtes à la réception.
4. **Fenêtres construites en plein jeu** : `settings.build` 25 ms (`SettingsWindow`).
   Construire à la 1ʳᵉ ouverture hors combat ou en frame creuse.
5. `GameScene.cs` (4119 l.), `EntityRegistry.cs` (2178 l.), `ClassWindow.cs` (2358 l.) :
   trop gros pour être testés ; extraire les handlers de paquets en classes pures testables.
6. Serveur — `GrantMobExpToAsync` : ~64 `await` en série par kill (gear, perso, elfe, classe,
   quêtes, DP). Les accès DB indépendants (`GrantGearExpAsync`, `GrantElfExpAsync`, lecture
   classe) peuvent partir en `Task.WhenAll` ; coût surtout latence réseau serveur→DB, sans
   effet sur le freeze client mesuré ci-dessus. Le 489 par kill peut être limité aux
   level-ups de classe côté serveur (le natif l'envoie, mais le client n'en a plus besoin).

## 4. Patterns : pertinence pour un MMORPG

Chiffres (grep) : serveur — 5553 `await`, 43 `Task.Run`, 57 fire-and-forget `_ =`, 71 `lock`,
56 `ConcurrentDictionary`, 6 `Channel<>` ; 43 `.Result/.Wait`, dont 36 dans
`ZoneEntryFlow.EntryPipeline.cs` **après** `Task.WhenAll` (sans risque) et 6 dans
`ZoneLinkServer.cs` (non inspectés). Client — 47 `Task.Run`, 84 `_Process`, 0 `ResourceLoader.LoadThreaded`,
73 `FindChild(`.

Ce qui est pertinent :
- **Serveur autoritaire async/await de bout en bout** + pipeline d'entrée en jeu parallélisée
  (`WhenAll`) + boucle de zone dédiée (`ZoneLoop`, tick unique, pas de `PeriodicTimer` par entité) :
  bon choix pour des milliers de connexions I/O-bound.
- **Redis pour les mutations chaudes** (or/position/vitals) flush ~15 s vers PG : adapté.
- **Client : drains réseau à budget** (`_pending` 8 ms, `reg.net` 6 ms), warm asynchrone
  des stems (`s_mobWarm`), file `SlowFree` : bon modèle, à généraliser (voir §3.2).
- **Pas de `async` sur les nœuds Godot** : seul le thread principal touche l'arbre ; les
  threads préparent des données pures (`KfSource.Baked`, `NifFile.Load`, `GetProto`) — correct.

Ce qui est risqué ou à revoir :
- Événements statiques C# (`PlayerVitals.*Changed`, `PlayerSkills.Changed`,
  `ClassChangeDialog.ClassesChanged`) à abonnés lourds, sans coalescence : un paquet = N
  reconstructions d'UI. Règle à adopter : **un événement par paquet burst = un seul refresh
  par image**, et les abonnés UI vérifient `Visible` avant de reconstruire.
- Le serveur envoie des paquets « refresh complet » (489, 296/492/493, 554…) à chaque gain,
  même sans changement : préférer des deltas ou ne les émettre que sur changement d'état.
- Les 57 `_ = …` fire-and-forget serveur perdent les exceptions : au minimum
  `.ContinueWith(OnlyOnFaulted)` + log (le client le fait déjà dans `ClassWindow.Send`).
- Fichiers God-object (GameScene/EntityRegistry/ClassWindow) : couplage fort, testabilité faible.

## 5. Limites / à vérifier

- Les effets des correctifs « level-up » sont confirmés par le propriétaire, pas par une
  mesure `--hitch` post-correctif (pas de level-up reproduit en banc, pour ne pas spawner de mobs).
- Deux tests `Eden.Core.Tests` échouent après ces changements
  (`DirectionalLocomotionTests.Mount_SideAnims_NativeRecords`, `ProgressionTests.GrantExp_Level30Repro`)
  et un test de perf (`NifParticleMapperTests.MountEmitter_ReskinCost_PerFrame`, 10 s) est sensible
  à la charge machine ; aucun ne touche les fichiers modifiés ici, non confirmé comme antérieur.
