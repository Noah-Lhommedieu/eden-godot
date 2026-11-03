# Archive du dépôt et portage Windows

## `tools/make_archive.sh`

Archive complète et autonome du dépôt — un seul dossier contient tout :
serveur C#, serveur natif `server_us/`, client projet `client/`, client
Reforged `reforged/` (jeu + préfixe Wine), corpus reverse `re/`,
toolchain llvm-mingw `tools/toolchain/`, Ghidra `ghidra_12.1.4_PUBLIC/`,
site + Studio `limitless/`, docs.

```bash
tools/make_archive.sh                  # tout : .tar.zst + .zip dans archives/
tools/make_archive.sh --no-reforged    # sans reforged/ (−29 Go)
tools/make_archive.sh --no-git         # sans .git/
tools/make_archive.sh --tar-only       # ou --zip-only
tools/make_archive.sh --list           # contenu + taille estimée, rien d'écrit
```

Exclusions fixes : `bin/ obj/ node_modules/ .next/ __pycache__/ *.pyc`,
dumps de crash (`NoName_*.zip`, `eden_crash/`, `eden_d3d_*.log`), secrets
(`.env`), volumes runtime (`dbdata/`, `pgdata/`), `ghidra_projects/`
(état GUI régénérable) et `archives/` elle-même. Le dump
`database.out` et `Database/*.sql` sont inclus.

Tailles mesurées (2026-10-24) : tout ≈ 42,7 Go ; `--no-reforged --no-git`
≈ 13,8 Go.

## Décompression

- `.tar.zst` : `tar --zstd -xf eden_server-*.tar.zst` (zstd requis ;
  Windows : `tar.exe` de Windows 11 gère zstd récent, sinon 7-Zip ≥ 24).
- `.zip` : Explorateur Windows / 7-Zip.

## Portage Windows — quoi réinstaller

| Composant | Sous Windows |
|---|---|
| Serveur C# | **.NET 10 SDK/runtime** — `dotnet build server/Eden.sln`, services lancés directement (`start-native.sh` → équivalent `.bat`/ps1 à écrire ; variables d'env identiques : `DB_CONN`, `ACCOUNT_DB_CONN`, `ZONE_PORT`…). |
| PostgreSQL | **PostgreSQL 16+** (installateur ou docker). Restaurer `database.out` : `psql -U postgres -f database.out`, ou rejouer `Database/*.sql`. Adapter `Host=` dans les connexions. |
| Client Reforged | **Aucun Wine** : `reforged/EdenReforged/game_.exe LogLog` se lance nativement (même exigence : `clal.xmf` à côté d'`ALAudio.dll`, chemin sans junction). `connect.ini` → IP du serveur. |
| Client projet | `client/game_.exe` idem, natif Windows. |
| Serveurs natifs | `server_us/*/…Server.exe` tournent nativement Windows (`start`/`gate` → `.bat`). |
| Mod Limitless | `tools/toolchain/llvm-mingw-*/` (archivée) a aussi sa variante Windows à télécharger ; `clientmod/limitless/build.sh` reste l'exemple de flags (`-shared -nostdlib`, i386). |
| Site `limitless/web` | **Node.js 20+** — `npm ci && npm run dev/build`. |
| Studio `limitless/studio` | .NET 10 — `dotnet run` / `publish` (Avalonia, marche sous Windows). |
| Reverse `re/` | `re/reindex` marche tel quel (Python 3 stdlib + sqlite FTS5) ; pour **décompiler** un nouveau binaire : Ghidra (Java 21+) à réinstaller et pointer via `GHIDRA_HOME` (`re/decompile.sh` est bash — Git Bash / WSL). |
| Outils `tools/*.py` | Python 3.12+ suffit (stdlib : sqlite3, struct, socket…). `wire_diff.py`, `headless_client`, `pkg_extract.py` OK. |

## Ce qui est Linux-spécifique (à porter, non bloquant)

- `launch-reforged*.sh`, `start-native.sh`, `tools/start-local.sh` :
  scripts bash/Wine → sous Windows le jeu se lance **sans** préfixe,
  il suffit de lancer `game_.exe LogLog`.
- `tools/ptrace_sniff.py`, `native_mitm.py`, `zhook` (LD_PRELOAD) :
  outils de sniff Linux-only — remplaçables par Wireshark +
  `tools/lapis.lua` (dissector, portable).
- `docker-compose.yml` : stack PostgreSQL identique sous Docker Desktop.

## Vérifications après décompression

```bash
re/reindex lookup lapis::CItemFactory::BuyItems -b zone | head   # index OK
cd server && dotnet build Eden.sln && dotnet test                # 721+ verts
reforged/EdenReforged/game_.exe LogLog                           # écran de login
```
