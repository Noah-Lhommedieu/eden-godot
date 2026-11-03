# Audio & vidéo — `sound/`, `bgm/`, `movies/`, `Movies/`, `cutscene/`

| Dossier | Fichiers | Format | Contenu |
|---|---|---|
| `sound/` | 2 448 | WAV | effets sonores — préfixes : `m###` monstres,
|  |  |  | `e###` effets, `s###`/`S###` scènes, `r###` montures,
|  |  |  | `UI###` interface/usage item (id partagé avec les icônes),
|  |  |  | `atks/ms/con/cond/trig/add` combat |
| `bgm/` | 86 | OGG | musiques `bgmNNN.ogg` |
| `movies/`, `Movies/` | 12 | SWF/OGV | cinématiques + éléments Flash |
| `cutscene/` | 6 | — | cinématiques de coupe |

Rien à décoder : WAV PCM et OGG/Ogv standard. Le mapping
son↔événement vit dans les tables (`c_spell`, `monsterlist`,
`c_sceneevent`) et non dans les noms.
