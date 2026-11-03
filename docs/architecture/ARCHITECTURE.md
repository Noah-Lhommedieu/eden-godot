# Architecture — état réel (2026-11-02) et cible

## Arborescence réelle

```text
eden-godot/
  AGENTS.md  CLAUDE.md          règles agents (AGENTS = référence)
  engine/current -> Godot_v4.8-beta2_mono_linux_x86_64/   moteur épinglé (+ GodotSharp/nupkgs)
  eden_godot/
    godot_project/              client Godot C# (scripts/, scenes/, shaders/, gamedata/, ui_textures/)
      assets/                   dérivés GLB/PNG/JSON — liens physiques vers portage (ADR-0004)
    raw_assets -> ../corpus/fr-full
    tools/                      NetTests (714), nif2gltf, textures, anim, edenpack, HeadlessBot, NetSmoke…
    docs/                       docs historiques du client 4.7
  src/Eden.Core/                Core pur net10 issu de Flax (loaders natifs NIF/KF/KFM/DDS/FSM)
  tests/Eden.Core.Tests/        890 tests du Core
  eden_server/                  serveur C# (Eden.sln, 1429 tests), PG17:5433 + Redis
  corpus/fr-full                EDENFR, liens physiques depuis portage, lecture seule
  re/                           exports Ghidra (liens physiques, lecture seule)
  knowledge/  ref-shots/        wikis du jeu, 127 captures du client original
  tools/                        scripts reproductibles (env.sh = versions et chemins)
  .agents/skills/  (.claude/skills -> ../.agents/skills)
  docs/                         plan, ADR, knowledge, workflows, provenance, legacy
  artifacts/                    sorties générées (logs, shots, benchmarks, provenance)
  client_assets, gamedata       liens de compatibilité pour les tests du Core
```

## Frontières

- **Pur** (sans `Godot.*`) : `godot_project/scripts/{net,protocol,session,data,vfs,game…}`,
  compilé tel quel par `NetTests`, et `src/Eden.Core`.
- **Adaptateur Godot** : le reste de `scripts/` (scènes, HUD, monde,
  `AssetCatalog`, `TerrainBuilder`…).
- **Double source du Core** (dette, ADR-0003) : le même domaine existe dans
  `godot_project/scripts` (compilé par Godot) et dans `src/Eden.Core`
  (version Flax, plus riche en formats). Tant que le Lot 1 n'est pas fait,
  tout correctif de format se porte **dans les deux**. `diff` réalisé le
  2026-11-02 : `FsmFile`, `TextResolver` et `UiTextEn` sont alignés sur
  Flax ; `WorldConv`, `ThemePalette`, `ClientSettings` et `EdenVfs`
  divergent encore.

## Cible (plan source §3, à atteindre par lots)

```text
src/Eden.Core  Eden.Assets  Eden.Gameplay  Eden.Network   ← purs, testés hors moteur
src/Eden.Godot (adaptateurs : Mesh/Texture/Animation/RID)  ← seul à voir Godot.*
godot/ (project.godot, scenes, shaders)                     ← référence src/ par ProjectReference
```

On découpe le Core seulement quand un besoin mesuré le justifie : un
projet par frontière réelle, pas par principe (YAGNI). `RenderingServer`,
MultiMesh étendu et GDExtension ne s'adoptent qu'après un profil qui
montre le goulot.
