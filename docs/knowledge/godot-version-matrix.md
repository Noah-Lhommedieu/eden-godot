# Matrice de versions

Statut : **verified** — 2026-11-02 — Nobara (KDE Wayland, XWayland `DISPLAY=:0`), RTX 4060 Laptop.

| Élément | Version | Où | Vérifié par |
|---|---|---|---|
| Godot éditeur/runtime | `4.8.beta2.mono.official.cafc17a19` | `engine/current/godot` | `tools/setup.sh` |
| Godot.NET.Sdk / GodotSharp | `4.8.0-beta.2` (nupkgs locaux) | `engine/current/GodotSharp/Tools/nupkgs` | `dotnet build` (0 warning) |
| Templates d'export | `4.8.beta2.mono` | `~/.local/share/godot/export_templates/` | `version.txt` |
| .NET SDK | `10.0.402` (TFM `net10.0` partout) | `~/.dotnet` | `dotnet --version` |
| Renderer | Forward+ / Vulkan 1.4.329, NVIDIA | défaut du projet | log au lancement |
| PostgreSQL / Redis | 17 (port 5433) / 7 | `eden_server/docker-compose.yml` (projet `edengodot`) | `docker ps` |
| Godot de référence | `4.7.2.stable.mono` | `godot` du PATH, pour `../portage/` seulement | — |

## Addons et outils candidats (non installés, à tester sur ce binaire exact)

| Outil | Statut | Note |
|---|---|---|
| GdUnit4 / GdUnit4Net | hypothesis | Prendre la version annoncée compatible avec les snapshots 4.8, jamais `master` |
| MCP Godot | hypothesis | Un seul candidat, sur un projet jetable. Zips déjà téléchargés : `godot-mcp-0.2.0.zip`, `godot-ai-plugin.zip`. À évaluer selon le plan source §7.2 |
| MangoHud | verified (présent) | `/usr/bin/mangohud`, pas encore utilisé dans les scripts |
| Flameshot / Spectacle | présents | La capture in-engine (`--shot`) reste la référence : sous Wayland, le focus est volé (constaté dans Flax) |
