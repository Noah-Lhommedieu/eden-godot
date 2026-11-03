# Client Godot — règles locales

Les règles générales sont dans `../AGENTS.md`. Ce fichier n'ajoute que le
spécifique au client.

- Projet : `godot_project/` (`EdenClientReborn.csproj`, Godot.NET.Sdk 4.8.0-beta.2, net10.0).
- Données : `raw_assets -> ../corpus/fr-full`. La résolution des chemins est
  **insensible à la casse** (`EdenVfs.LoosePath`/`CaseResolve`). fr-full est
  en minuscules alors que le code historique demande `DejaVu.scheme`,
  `C_EquipType.ini`, `S002/S002.fsm`. Passer par `EdenVfs`/`AssetCatalog`,
  jamais par `File.*` direct sur une donnée corpus.
- Encodage : les tables `t_*.ini` EDENFR sont en CP1252, le reste en Big5
  (`EdenEncoding.TableFallback`).
- Langue : `TextResolver.NativeOnly = true` (EDENFR pur). `EDEN_LANG_EN=1`
  réactive l'anglais, pour comparaison seulement.
- Code pur testé hors moteur : `tools/NetTests` (inclut `scripts/{net,protocol,session,data,game…}`).
- Drapeaux de diagnostic du jeu : `--map=Sxxx --solo --nohud --perf
  --perf-seconds=N --perf-out=F --shot=F`, `--live`, `--input-test`,
  `--mob-test`, `--combat-test`, `--hud-open=<clés>`, `--hud-dump`.
- Docs historiques du client : `docs/` (portage 4.7). Utiles (`FSM-FORMAT`,
  `NETWORKING`, `packaging/EPK-FORMAT`, `client-assets/ref-shots-catalog`),
  mais **datées** : elles supposent souvent `corpus/official` et le FR+EN.
