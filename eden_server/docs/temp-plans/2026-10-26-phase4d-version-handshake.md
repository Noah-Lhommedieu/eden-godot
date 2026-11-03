# Phase 4 (d) — Handshake de version + manifest gamedata (Godot-only)

Plan-racine : `2026-10-26-phase4-transport-protocole.md` §(d).
Mission : handshake de version obligatoire post-TLS sur les accept-paths
client-facing (Login :6543, World :5200, Zone :5201) + hash SHA-256
déterministe du corpus gamedata. Legacy (`EDEN_TRANSPORT=legacy`) : aucun
handshake de version — les clients natifs restent jouables.
Liens internes (ZoneLink, Ticket, Mission, Gateway) : hors scope.

## Wire (NCIDs Godot-only, hors plage native <2000)

```
C→S 0xFFFF VersionHello : [u32 magic 'EDN1'][u8 protoMaj][u8 protoMin]
                          [str clientVersion][str manifestHashHex]
S→C 0xFFFE VersionAck   : [u32 magic 'EDN1'][u8 status=0][str serverHashHex]
S→C 0xFFFD VersionReject: [u32 magic 'EDN1'][u8 reason]
                          [str serverHashHex][str minClientVersion]
  reason : 1=UpdateRequired 2=TooNew 3=Malformed
  str    = u16 len + octets (format InBinStream/OutBinStream natif)
```

Séquence : TLS établi → client envoie VersionHello et attend → serveur
lit la 1re trame (obligatoirement 0xFFFF, borné par HandshakeTimeout 10 s),
évalue (proto vs EDEN_[MIN_]PROTOCOL_VERSION, client_version vs
EDEN_MIN_CLIENT_VERSION, manifest vs EDEN_MANIFEST_STRICT), émet Ack ou
Reject puis close (Reject) — AVANT tout push serveur (ClientHello).
Trames pipelinées après le hello = leftovers rendus au dispatch.

## Points d'édition

- `EdenConfig` : `EDEN_PROTOCOL_VERSION` (def 1.0), `EDEN_MIN_PROTOCOL_VERSION`
  (def 1.0), `EDEN_MIN_CLIENT_VERSION`, `EDEN_GAMEDATA_DIR` (→ DATA_DIR/EDEN_DATA),
  `EDEN_GAMEDATA_HASH` (override hex), `EDEN_MANIFEST_STRICT` (def 0),
  `EDEN_VERSION_CHECK` (def 1).
- `Eden.Core/Networking/VersionProtocol.cs` — codec + `ProtocolVersion`.
- `Eden.Core/Networking/Session/VersionGate.cs` — `HandshakeAsync` →
  Result{Accepted, Remaining, reason} ; config injectable pour les tests.
- `Eden.Core/Data/GameDataManifest.cs` — `Compute(dir)` : *.ini récursif,
  relpath trié Ordinal normalisé '/', sha256 incrémental de
  (relpath+'\0'+sha256(fichier)) ; cache par dir.
- `Eden.Login/Eden.World/Eden.Zone Program.cs` — gate entre handshake et
  ClientHello/loop ; leftovers dispatchés ; log « version ok » / rejet.
- Client `net/VersionProtocol.cs` (miroir + UpdateRequiredException),
  `net/ManifestHash.cs`, `EdenConnection` (post-TLS : hello → attente
  réponse sync → ack/reject/fallback-sans-gate), `EdenSession`/`Options`
  (propagation + résolution ManifestDir : GODOT→DataPaths data/db,
  sinon EDEN_DATA_DIR), `ClientConfig` (clés version_check/gamedata_dir).
- Bots : `--client-version` `--manifest-hash` `--no-version` ;
  `BotResult.VersionRejected`.
- Tests : `VersionGateTests`, `GameDataManifestTests` (serveur),
  `VersionProtocolTests` (NetTests).

## Vérifs — fait 2026-10-27

- [x] `dotnet build` sln serveur : 0 erreur, 0 avertissement.
- [x] `dotnet test tests/Eden.Tests` : 1204/1204 (incl. 18 nouveaux —
      VersionGateTests ×13 + GameDataManifestTests ×5).
- [x] `dotnet build godot_project/EdenClientReborn.csproj` : 0/0.
- [x] `dotnet test tools/NetTests` : 324/324 (incl. 11 nouveaux —
      VersionProtocolTests, dont 3 aller-retour TLS réels vs stub gate).
- [x] Live modern (`EDEN_TRANSPORT=modern`, EDEN_VERSION_CHECK défaut) :
      - manifest `89ac91b2f07a…` (166 .ini) identique au boot sur
        Login/World/Zone ;
      - Godot `--login=admin:adminmdp --channel --char` : VERSION_OK
        sur Login+World+Zone (hash client `f4bc7eff…` ≠ serveur →
        « manifest mismatch … admis, non-strict »), map S002 spawnée,
        HUD chargé, trafic zone live ;
      - bots 5/5 `Entered` (scénario entry) ;
      - bot `--no-version` → `0xFFFD UpdateRequired`, `Entered=false`,
        `VERSION_REJECT` logué « première trame non-VersionHello » ;
      - bot `--manifest-hash=<hash serveur>` → « manifest ok ».
- [x] Live legacy (`EDEN_TRANSPORT=legacy`) : bot legacy entre en zone,
      zéro ligne VERSION dans le log — gate absent, repli intact.
- [x] Liens internes non gatés : ZoneLink (World↔Zone) ne passe pas par
      le gate (vérifié par inspection — `ZoneLinkServer` n'appelle pas
      `VersionGate`) ; Gateway hors scope (framing natif malformé).
