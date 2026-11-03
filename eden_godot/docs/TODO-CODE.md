# TODO/FIXME recensés — point pour le parent (lot G, qualité light)

> Liste au 25 oct. — grep `TODO|FIXME|HACK` sur `godot_project/scripts/`.
> Volontairement NON traités ici (lots C/F en cours chez d'autres
> agents) — ce fichier sert juste de suivi.

## Protocole / gameplay (lot F)

- `hud/HudNetBridge.cs` — trames routées avec parsing inline ;
  **TODO(agent F)** : promouvoir ces parsers dans `protocol/`.
- `hud/SkillsWindow.cs` — liste des sorts attendue par protocole
  (**TODO agent F**) : grille vide en attendant (le 311 porte déjà
  `KnownSpell`/`PlayerSkills` — relier).
- `hud/NpcDialogueWindow.cs` — dialogue d'un faux marchand ;
  **TODO(proto)** : retirer quand le 285 est pleinement branché.
- `hud/QuestWindow.cs` — Share/GiveUp non câblés (TODO).
- `hud/WorldMapWindow.cs` — **TODO(proto/table)** : mapping
  région → coords Sxxx pour le tooltip « Region N ».

## Perf / structure (lot G — détails dans `docs/PERF.md`)

- Props statiques : regroupement MultiMesh par (modèle × cellule) —
  piste non implémentée (36 k nœuds tolérables aujourd'hui).
- `Node.Duplicate` parallèle : essayé et REVERT (blocage) — ne pas
  réessayer sans benchmark dédié.
- ImageTexture garde la copie CPU — pas d'API de discard ; évaluer un
  pipeline d'import .ctex si la RAM devient critique.
- Spike ~150 ms première frame = compilation shaders — warmup dédié
  à faire.

## Divers

- `settings/SettingsApplier.cs` ApplyFont : `res://ui_textures/*.ttf`
  absent du pck exporté (fichier non importé) — le repli
  `gamedata/uidata/fonts/*.ttf` couvre le package ; écart bénin
  documenté dans `docs/packaging/DISTRIBUTION.md`.
- Shutdown Godot : warnings « RID/Instances leaked » à la sortie forcée
  (`GetTree().Quit` pendant que le rendu tourne) — bruit connu du
  moteur en quit sauvage, pas un bug du client.
