# Batch — correctifs rapport live 18/10 (inventaire, shop, sit, regen, scènes, session)

Diagnostics confirmés par `/tmp/eden-zone.log` + schéma DB :

1. **Inventaire vide** — Dapper ne mappe pas `item_id`→`ItemId` sans
   `MatchNamesWithUnderscores` : le 312 émettait 6 items avec itemId=0 →
   ignorés par le client. Fix : `DefaultTypeMap.MatchNamesWithUnderscores=true`
   via `[ModuleInitializer]` Eden.Core.
2. **Shop** — `inventory1.id` NOT NULL sans séquence → INSERT 23502.
   Fix : allocateur natif `max(id)+1` scanné sur les tables items +
   compteur Interlocked (désasm : le natif scanne 16 tables items).
3. **Sit X freeze** — NCID 14 = silence côté serveur → le client coupe.
   Désasm `SimpleAction` : `ClientPlayerAction` (290) → `NotifyClient` +
   `BroadCastToCanSee`. Format 16B `[u32 char][u8 act][f32 +0x58][u8 0]
   [u8 0][i32 cible][u8 1]`.
4. **Regen** — `catch {}` global tuait la boucle au 1er échec (ex.
   `SaveCharacterAsync` → connexion disposée). Fix : try/catch par tick
   avec log, boucle immortelle.
5. **NCID 54 raccourcis** — `using var db` + `return db.ExecuteAsync`
   non-awaité → dispose pendant la requête ("Can't close, state
   Connecting"). 12 méthodes dans SocialRepositories → async/await.
6. **Scènes** — `DATA_DIR` = symlink vers db_decoded → `data/../scene`
   résout dans bin/Release au lieu de `server_us/Data/scene` → 0 entité.
   Fix : `Directory.ResolveLinkTarget`.
7. **Focus perdu → déco** — pas de trafic serveur ambiant (scènes vides
   + regen morte). Avec 4+6 réparés, le flux reprend (mobs, regen).
   Hypothèse résiduelle : watchdog client sur silence prolongé.
8. **XP** — catch silencieux dans GrantKillExp → log au lieu d'avaler.

## Résultat (build 0/0, zone redéployée :5201)

- `DapperBootstrap` (module init) → MatchNamesWithUnderscores.
- `ItemSerialAllocator` partagé (Eden.Core.Database) : seed lazy
  max(id) sur 14 tables items + Interlocked — branché sur
  InventoryRepository.AddItemAsync et BankRepository.AddBankItemAsync.
- 12 méthodes SocialRepositories → async/await (plus de dispose en vol).
- NCID 14 → 290 `[u32][u8 act][f32 act][u8 0][u8 0][i32 cible][u8 1]`
  à soi + broadcast node (SendPlayerActionAsync, Movement.cs).
- Sitting reset au moindre MoveSelf.
- Regen : Task.Delay isolé + try/catch par tick loggé — boucle
  immortelle ; XP : catch loggé.
- Program.cs : `Directory.ResolveLinkTarget(dataDir)` →
  `server_us/Data/scene` retrouvé — **411 scènes, 26967 entités** au
  boot (avant : 0).
- Focus-loss : cause probable = silence serveur (scènes vides + regen
  morte). Ambiant restauré ; à re-tester. Si le client a encore un
  watchdog, la prochaine piste est un keepalive natif à identifier.
