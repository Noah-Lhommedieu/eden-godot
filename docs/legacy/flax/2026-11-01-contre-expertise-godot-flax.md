# 2026-11-01 — Contre-expertise de ANALYSE-COMPARATIVE-GODOT-FLAX.md

## Mesures faites (portage/ non modifié)

Godot S001, `--solo --nohud --perf`, 1280x720, RTX 4060, `gamedata/settings.cfg` = `vsync=false`, `max_fps=0`.

| Run | Frame CPU (moy.) | GPU (`--gpu-profile`) |
|---|---|---|
| A (sans gpu-profile) | 17,9-18,4 ms (12,8-30 ms) | — |
| B (`--gpu-profile`) | 17,4 ms, max fps 190 | **5,1 ms** : opaque 2,4 / depth pre-pass 0,8 / transparent 0,8 / ombres 0,45 |

- L'hypothèse « plafond VSync 60 Hz » est réfutée : la VSync est coupée et les frames varient de 12,8 à 30 ms.
- Godot est limité par le **CPU** : la frame dure 17,4 ms, le GPU n'en prend que 5,1. Il reste donc environ 12 ms de CPU, non ventilés.
- `dotnet-stack` a échoué (socket IPC dans /tmp, sandbox). On ne sait donc pas encore quelle part revient aux scripts C#, à l'interop et à la soumission du rendu.
- La valeur `proc=32 ms` de PerfProbe dépasse la frame moyenne. Elle n'est pas fiable : la sonde parcourt la scène chaque seconde.
- Godot.NET.Sdk 4.7.2 compile un projet en `net10.0` (build OK). Le chargement runtime par Godot n'a pas été testé.

## Conclusion

L'écart de 4× vient du code client, côté CPU. Le moteur n'en est pas la cause démontrée.
Le GPU de Godot, avec outline et brouillard, prend 5,1 ms. Flax en prend environ 4, sans ces effets.
Recommandation : Godot + loaders natifs (`Eden.Core` net10 partagé), après une journée de profilage CPU.
Flax est gelé, pas supprimé. Seuil de bascule : moins de 8,3 ms/frame S001 avec HUD, après une semaine d'optimisation CPU.
