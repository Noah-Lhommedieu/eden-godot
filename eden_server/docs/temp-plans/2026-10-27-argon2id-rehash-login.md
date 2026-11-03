# Plan — Argon2id + rehash-at-login (Phase 4 / T2)

Référence : `2026-10-26-phase4-transport-protocole.md` §T2 Argon2id.

## État constaté

- `LoginPacketHandlers.HandleLoginAccountAsync` (~l.119-124) : vérifie
  `account.Password == reçu` OU `md5(account.Password) == reçu` —
  `fnaccount.accounts.password` = plaintext (colonnes varchar(32)).
- Le client Godot (`EdenSession.LoginAsync`, client repo) envoie
  **toujours `md5(mdp)`** (32 hex minuscule) = « credential wire ».
- Bots (`Eden.Bots`) = ticket→zone direct, ne passent pas par Login.
- `Eden.Admin account create` → `AccountRepository.CreateAccountAsync`
  écrit `password` plaintext seul.
- Pas de `password_hash` en base (schéma vérifié en live).
- Lib : aucune présente → `Konscious.Security.Cryptography.Argon2` 1.3.1
  (CPM). La lib n'encode pas le PHC → encodage/décodage maison
  (`$argon2id$v=19$m=65536,t=3,p=1$<b64nopad salt>$<b64nopad hash>`),
  comparaison `CryptographicOperations.FixedTimeEquals`.

## Design

- **Credential wire canonique** : `IsHex32(reçu) ? lower(reçu) : md5(reçu)`
  — couvre Godot (md5) et un émetteur plaintext (normalisé vers la forme
  Godot) : le hash stocké couvre toujours `md5(mdp_saisi)`.
- `password_hash` (varchar(255), NULL) = PHC argon2id **du credential
  canonique**. Paramètres : m=65536 KiB (64 Mio), t=3, p=1, sel 16B,
  hash 32B.
- Auth : `IsArgon2Phc(password_hash)` → `Verify(canonique, phc)` ;
  sinon legacy (2 formes) → succès ⇒ `needsRehash`.
- Rehash **post-émission** de la liste des mondes (hors chemin
  critique), awaité dans le handler (déterministe pour les tests) ;
  échec = log, jamais bloquant. Log `[auth] rehash <login>`.
- Pas de rehash pour le compte synthétique `test123` (DB down).
- PHC malformé/non-argon2 → chemin legacy (pas de crash).
- `CreateAccountAsync` écrit `password` (legacy, transition) +
  `password_hash = argon2id(md5(password))` — le param `password` reste
  le plaintext saisi par l'admin (Admin/Godot envoient md5 au login).
- `fnmember.tb_user`/PIN/`char_password`/`gm_tool_accounts` : hors scope.

## Points d'édition

- [x] `server/Directory.Packages.props` + `Eden.Core.csproj` : Konscious 1.3.1
- [x] `Eden.Core/Security/PasswordHasher.cs` (nouveau)
- [x] `Entities/Account.cs` : `PasswordHash`
- [x] `IAccountRepository.cs` : `UpdatePasswordHashAsync`
- [x] `AccountRepository.cs` : SELECT ×2, INSERT, UPDATE
- [x] `Eden.Login/Handlers/LoginPacketHandlers.cs` : auth PHC + rehash
- [x] `Database/013-account-password-hash.sql` + appliquée live (psycopg2)
- [x] `tests/Eden.Tests/PasswordHasherTests.cs` (nouveau)
- [x] `tests/Eden.Tests/LoginTests.cs` : fake + cas PHC/rehash/legacy
- [x] `dotnet build` 0 err / `dotnet test tests/Eden.Tests`
- [x] Live : Login service + login réel → `password_hash` rempli (preuve SQL)
- [x] Cocher T2 dans le plan Phase 4

## Comptes jamais reconnectés

Restent en legacy (`password` plaintext, `password_hash` NULL) tant
qu'ils ne se reconnectent pas — prévu par design (pas de migration de
masse possible : les plaintext md5-hex stockés éventuels ne sont pas
réversibles). Script admin possible plus tard : `UPDATE accounts SET
password=NULL` sur les lignes migrées, ou purge des comptes legacy
après une date butoir.
