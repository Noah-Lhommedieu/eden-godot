---
name: eden-server-stack
description: Lancer, tester ou relancer le serveur C# Eden (PG17, Redis, services Gateway/Login/World/Zone/Mission/Ticket) pour un test live du client. À utiliser pour « serveur », « login », « test live », « base de données », « compte de test ».
---

# Pile serveur

- PG17 et Redis : `cd eden_server && docker compose up -d postgres redis`
  (projet `edengodot`, conteneurs `edengodot-*`, volumes repris
  `edenflax_pgdata` et `edenflax_redisdata`, base `edenfr`). PG17 écoute sur
  le port **5433**. Le 5432 est le PG12 de portage, à ne pas toucher.
- Services natifs : `cd eden_server/server && dotnet build Eden.sln && cd ..
  && ./start-native.sh`. Le serveur qui tourne n'est **pas** recompilé tout
  seul. Les tables et scènes viennent de `corpus/fr-full`, via des liens
  normalisés `/tmp/edenfr_scene_links`.
- Tests : `cd eden_server/server && dotnet test Eden.sln` (1429).
- Comptes : `admin`/`adminmdp`, `test123`/`test123`.
- Client live : `tools/run.sh -- --login=test123:test123 --channel --char`.
- Règles serveur : `eden_server/AGENTS.md`, puis `eden_server/server/AGENTS.md`.
  Le serveur fait autorité : le client ne décide ni des positions, ni des
  dégâts, ni des drops, ni de l'inventaire.
