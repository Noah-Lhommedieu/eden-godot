# Analyse — faisabilité d'un portage Android (moteur uniquement)

Date : 2026-10-29. Périmètre : moteur de jeu + fonctionnement mobile.
Hors périmètre : réseau, distribution Play Store, serveur.
Conclusion d'analyse statique — aucune modification faite.

## Verdict

**Faisable, cas favorable.** Le client est 100 % C# managé sous Godot
4.7.2 mono (net8.0) : zéro P/Invoke, zéro GDExtension, zéro dépendance
native (seul NuGet : `System.Text.Encoding.CodePages`, supporté sous
net8.0-android). C# sur Android est supporté officiellement depuis
Godot 4.2. L'effort se concentre sur 4 chantiers : accès fichiers,
input tactile, renderer, poids des assets.

## Ce qui porte gratis

- **Aucune interop native** — pas de `DllImport`, `Process.Start`,
  `Registry` dans `scripts/` (253 fichiers).
- **Choke points propres** : `DataPaths` + `AssetCatalog` centralisent
  tout l'accès fichiers (racines candidates, résolution insensible à la
  casse, caches GLB/textures).
- **Shaders customs** (`toon_lit`, `terrain_splat`, `sky_dome`,
  `water`, `fog_ceiling`) : gdshader portable ; `screen_texture` /
  `depth_texture` fonctionnent en renderer Mobile (coûteux mais ok).
- **Threading** (`Task.Run`/`Parallel` pour le preload GLB/anims) :
  fonctionne sur ARM multicœur.
- **`content_scale` UI déjà géré** dans `SettingsApplier`
  (canvas_items) — la brique existe pour la densité DPI mobile.
- **Decals** (`BlobShadow`) : supportés en renderer Mobile (Vulkan) —
  pas en gl_compatibility.
- **Réseau** : `TcpClient`/`SslStream` = .NET standard, tourne sous
  Android (hors périmètre mais sans bloqueur).
- **Pas d'audio implémenté** (bus volumes seulement) — rien à porter.

## Les 4 chantiers

### 1. Couche fichiers — le bloqueur principal

~36 fichiers utilisent `System.IO` direct (vs 5 `Godot.FileAccess`).
Tout le modèle de données repose sur des fichiers lâches lus par
chemins absolus : `uidata/` CEGUI (83 Mo), `data/db` ini (67 Mo),
`map/*.fsm/.ini/.area`, `assets/` (~27 Go) via `Image.LoadFromFile` /
`GltfDocument.AppendFromFile`.

Sur Android, `res://` packé dans le PCK n'existe PAS sur le filesystem
— le fallback `res://gamedata` de `DataPaths` casse aussi. Options :

- **Extraire les données vers `user://` au premier boot** — chemins
  réels, code quasi inchangé (voie lazy, recommandée).
- Shim `FileAccess`/VFS dans `AssetCatalog`+`DataPaths` — plus propre,
  plus de travail.

### 2. Input tactile — couche à écrire

Zéro `InputEventScreenTouch`/`InputEventScreenDrag`/joypad : tout est
`InputEventMouseButton`/`Motion` + WASD (35+ fichiers).
`emulate_mouse_from_touch` donne le tap=clic gauche gratis ; il manque :

- joystick virtuel (déplacement WASD) ;
- long-press = clic droit (parler NPC, auto-attaque mob) ;
- pinch = zoom (molette) ;
- équivalent tactile des tooltips au survol.

Le drag des fenêtres HUD devrait suivre gratis via l'émulation souris.

### 3. Renderer

Aucun renderer fixé dans `project.godot` → défaut Forward+, inutilisable
sur la majorité des GPU Android. Passer en `mobile` (Vulkan) :

- perdu : SSAO/SSIL/DoF (toggles déjà câblés dans les settings —
  dégradation propre) ;
- conservé : fog, glow, shadows, decals, MSAA 3D, tous les shaders.

### 4. Payload assets

~27 Go (58 230 PNG, 8 486 GLB, 14 Go d'anims) : impossible en un APK.
Nécessite slicing par map + compression GPU (ASTC/ETC2 — les PNG sont
de la VRAM non compressée) + téléchargement à la demande. Le pipeline
`tools/` tourne déjà offline — c'est du packaging, pas du portage.

## Détails mineurs

- Settings UI propose windowed/résolution/curseurs — no-ops ou
  incohérents sur mobile (cosmétique).
- `EdenWindowMode`, `DisplayServer.WindowSetSize`, `NativeCursor` :
  inoffensifs sous Android.
- `export_presets.cfg` n'a que Linux/X11 et Windows — preset Android
  à créer + workload .NET Android pour la publication.

## Risques / non vérifié

- Perf réelle du chargement map sur ARM : le preload parallèle aide
  mais parser des milliers de GLB (`GltfDocument`) sur CPU mobile peut
  être long.
- Comportement exact de `GltfDocument.AppendFromFile` sous Android
  (chemins `user://` OK ; `res://` PCK non).
- **VRAM** : 58k PNG non compressés écrouliront un GPU mobile sans
  conversion ASTC préalable — le poste déterminant.
