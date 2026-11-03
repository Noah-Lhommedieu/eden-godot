# Phase 4 — Transport & protocole (Godot-only)

Plan-racine : `eden_godot/docs/plan-refonte_architecture-complet.md` (Phase 4).
Design audité 2026-10-26 (rapport subagent — signatures/sites validés).

## Design retenu

`ITransport` = **stream d'octets + transforms payload + handshake transport**.
`NetSession` garde Channel borné/drain unique/métriques/trace/rate-limit.

```csharp
interface ITransport : IAsyncDisposable {
  string RemoteEndPoint { get; } bool IsEncrypted { get; }
  Task StartAsync(ITransportContext ctx, CancellationToken ct); // TLS auth ou RSA/RC4
  ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct);
  ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken ct); // drain seul
  bool HasSendTransform { get; }
  void TransformSend(Span<byte> p); void TransformRecv(Span<byte> p);
  void Abort();
}
interface ITransportContext { Task SendRawAsync(...); Task<int> ReadRawAsync(...); }
```

Impls : `TcpTransport` (clair/no-op), `LegacyLapisTransport` (RSA blob + RC4,
`encryptPayloads=false` pour Mission qui échange RSA puis parle clair),
`TlsTransport` (SslStream TLS 1.3).

## Incréments (ordre — chacun laisse la stack jouable)

- [x] **(a) `ITransport` pure** — aucun changement wire. `NetSession(ITransport)`;
      ctor `NetSession(TcpClient)`→wrappe `TcpTransport` (tests intacts) ;
      handshake RSA/RC4 migre dans `LegacyLapisTransport.StartAsync` ; items de
      file `cipherOffset`→`transformOffset` ; drain appelle `TransformSend` si
      `HasSendTransform`. Liens : `ZoneLinkServer`, `TicketSession`,
      `MissionSession`, `TicketServerClient`, `ZoneWorldLink`, `ZoneMissionLink`
      (le même motif dupliqué ×6 converge). Tests : suite verte + Fake/Loopback.
- [x] **(b) Varint LEB128** — remplace u16 len partout : `PacketReceiveBuffer`
      (parser unique + « besoin d'octets » ≠ « malformé »), `NetSession` envois,
      liens ×4, `TicketServerClient`, client `EdenConnection` (+ ring buffer au
      lieu de `List<byte>` O(n²)), `BotTransport`, outils Python
      (`eden_headless`, `eden_session`, `ticketserver_stub`, `lapis.lua`).
      `VarintCodec` miroir serveur/client. Cap `MaxFrameSize` 1 Mio, overlong
      → close. **Cutover atomique** (wire-breaking).
- [x] **(c) TLS 1.3** — `SslStream` des deux côtés. Cert dev auto-signé
      persistant `server/certs/eden-dev.pfx` (gitignored) via
      `CertificateRequest`, SAN localhost ; `EDEN_TLS_PFX[_PASSWORD]` en prod ;
      client : pinning SHA-256 `tls_thumbprint` (`ClientConfig`) ou
      `tls_insecure=1` dev-flag ; validation standard en prod.
      Transition `EDEN_TRANSPORT=modern|legacy` (défaut modern, legacy
      = RSA/RC4 strictement pré-c sur accept-paths + liens) ;
      `EDEN_LINK_TLS=pin|insecure` pour les liens internes ; bots
      `--transport`/`--tls-thumbprint`/`--tls-insecure`.
      **Suppression repoussée à un lot phase4-c2** (demande explicite :
      garder le repli utilisable) — RSA/RC4/`Rc4`/`EdenCrypto`/
      `EDEN_RSA_PEM`/`*_CRYPTO`/`EDEN_PLAIN_HELLO`/`ForcePlaintext`/
      `InstallTestCipher`/`CryptoReady`/`OutboundNcidShiftAbove`
      restent en place, marqués `[Obsolete]`/commentaires c2.
- [x] **(d) Handshake version** — trames dédiées 0xFFFF `VersionHello`
      `[magic EDN1][protoMajor][protoMin][str clientVer][str gamedataHash]` →
      0xFFFE Ack / 0xFFFD Reject (1=UpdateRequired 2=TooNew 3=Malformed),
      après TLS avant dispatch, `VersionGate` posé sur les accept-paths
      client-facing Login/World/Zone (liens internes ZoneLink/Ticket/Mission
      et Gateway exclus — framing interne/hors scope).
      `GameDataHash` = SHA-256 manifest déterministe (`*.ini` récursif du
      corpus gamedata, chemins triés normalisés), calculé au boot.
      Config `EDEN_PROTOCOL_VERSION`, `EDEN_MIN_PROTOCOL_VERSION`,
      `EDEN_MIN_CLIENT_VERSION`, `EDEN_GAMEDATA_HASH`,
      `EDEN_MANIFEST_STRICT`, `EDEN_VERSION_CHECK`.
      Rejet → `UpdateRequiredException` → « mise à jour requise ».
      Fait 2026-10-27 (`docs/temp-plans/2026-10-26-phase4d-version-handshake.md`)
      : serveur+client 0/0, 1204 tests serveur + 324 NetTests, live
      `[VERSION_OK]` Godot sur Login/World/Zone + bots 5/5, bot
      `--no-version` rejeté `UpdateRequired`, manifest `89ac91b2…` (166
      .ini) identique sur les 3 services.
- [x] **T2 Argon2id** — `LoginPacketHandlers.cs:119-124` vérifie md5/plain ;
      comptes stockés plaintext (`AccountRepository.CreateAccountAsync:68-88`).
      Sous TLS : mdp clair dans le tunnel ; vérifie par préfixe PHC vs legacy ;
      succès → rehash `Argon2id` + `UpdatePasswordHashAsync` (m=64Mio,t=3,p=1,
      salt16 ; `Isopoh.Cryptography.Argon2`) ; `Eden.Admin` crée en Argon2id.
      `fnmember.tb_user`/PIN = séparé (intégrations natives).
      Fait 2026-10-27 (`docs/temp-plans/2026-10-27-argon2id-rehash-login.md`) :
      `Konscious.Security.Cryptography.Argon2` 1.3.1 + `PasswordHasher`
      (PHC encode/verify maison), credential canonique = md5(mdp) ; preuve
      live : `admin` migré au login (`[auth] rehash admin`), verify au
      login suivant, mauvais mdp rejeté.

## Risques

- (a) ordering drain/ownership stream→transport ; (b) cutover atomique requis ;
- (c) provisioning cert + validation Godot + edge-cases SslStream ;
- (d) deadlock ordre + déterminisme hash.
- Outils Python deviennent legacy post-TLS ; bots C#/NetSmoke/Godot = preuves.
- Gateway : mal framé, hors chemin client — migrer en dernier/supprimer.
