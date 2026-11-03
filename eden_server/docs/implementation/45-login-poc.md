# 45 — POC Login : authentification + liste des channels

> **Objectif** : permettre au client original de se connecter au `Eden.Login`
> recodé, d'authentifier un compte et d'afficher l'écran de sélection
> des channels. Validé par test de fumée wire-level (client simulé).

## 1. Protocole natif reversé (LoginServer)

### Transport (CMSTCPChannel, machine à états `+0x208c`)

| État | Déclencheur | Action |
|------|-------------|--------|
| 1 | `CMSTCPChannel::Open` | `OnWrite` envoie le **blob RSA brut** `[u32 nlen LE][u32 elen LE][n BE][e BE]` (`MakeRSAKeyData`, RSA-2048 généré à l'init) |
| 2 | après envoi complet | `OnRead` → `ReceiveRC4Key` : lit exactement `RSA_size` (256) octets, `RSA_private_decrypt` **PKCS1**, résultat **exactement 5 octets** → 2× `RC4_set_key` |
| 3 | RC4 établi | paquets `[u16 len en clair][RC4(u16 NCID + champs)]` |

`CCLTCPChannel::OnReady` pousse un `CNE_CL_ServerHello` interne
(arg `"HELLO CLIENT"`) dont `Execute` envoie `CNC_CL_ClientHello`
(float `9.9`) — le paquet n'est émis qu'une fois l'état 3 atteint.

### Table de commandes (`InitLoginCommandTable`, NCID = index)

| NCID | Classe | Direction | Layout |
|------|--------|-----------|--------|
| 35 | `CNC_CL_ServerHello` | C→S | `str` |
| 36 | `CNC_CL_ServerLoginAccount` | C→S | `str username, str md5(pwd), str extra` |
| 37 | `CNC_CL_ServerLoginWorld` | C→S | `i16 worldId, str, str` |
| 38 | `CNC_CL_ClientHello` | S→C | `float` (9.9) |
| 39 | `CNC_CL_ClientLoginFail` | S→C | `i16 code` (0x22, 2, 6, 9, 10…) |
| 40 | `CNC_CL_ClientGetWorldServers` | S→C | `i16 count` + count×`(i16 id, str name, str ip, i16 port, i32 online, i16 maxnum, i16 state, i16 order, i16 flags, i16 chars)` + `i16 lastWorldId` |
| 41 | `CNC_CL_ClientReceiveTicketToWorldServer` | S→C | **22 octets bruts** (CTicket) — boucle `cmp ebx,0x16` |

### Authentification réelle (natif)

```
Client → Login → Gateway : CNC_G_ServerAccountLogin (3 strings)
Gateway → fnmember : SELECT account_login(mid, md5pwd, clientIp)
                   → (pIdNum, nRet) : 1=OK 2=inconnu 3=pwd 4=GM-ip 5=banni
Login → fnaccount : accounts / worlds / fake_worlds / character_number
Succès → CNC_CL_ClientGetWorldServers ; échec → CNC_CL_ClientLoginFail
```

**Le client envoie `md5(mot de passe)`** — vérifié par
`tb_user.pwd = md5('test123') = cc03e747a6afbbcbf8be7668acfebee5`.

### Sélection de channel (natif)

`ServerLoginWorld` → Login demande un ticket au TicketServer
(`CNC_T_ClientGetTicket`) → `CNE_T_ClientGetTicket::Execute` copie le
CTicket 22 octets et l'expédie au client via NCID 41.
CTicket : `[u32 key][u32 clientIp][u32 worldIp][u16 worldPort][u32 microtime][u32 random]`.

## 2. Implémentation POC (déviation assumée)

- **`Eden.Login/Program.cs`** : RSA→RC4 complet + framing + dispatch NCID
  via `PacketDispatcher` (plus de parsing heuristique).
- **`LoginPacketHandlers`** : auth directe sur `fnaccount.accounts`
  (le Gateway est contourné — acceptable pour le POC), accepte
  **plaintext ou md5**. Réponses aux layouts natifs exacts.
- **Ticket** : généré localement au format CTicket (le TicketServer
  n'est pas consulté pour l'instant).
- **`POC_ADVERTISE_IP` / `POC_ADVERTISE_PORT`** : IP annoncée dans la
  liste des channels (les IP du dump `47.242.253.210` sont injoignables).

## 3. Compte POC

`Database/login-poc.sql` (idempotent) :

- `fnaccount.accounts` : `admin` / `adminmdp` (id=2, state=0)
- `fnmember.tb_user` : `admin`, `pwd=md5('adminmdp')`, `byauthority=1` (GM)
- `fnaccount.gm_tool_accounts` : `admin`, privilege=255
- `fnaccount.worlds` : channel 1010 → `172.29.131.228:5200` (IP LAN host)

## 4. Mise en route

```bash
docker compose build login
docker compose up -d postgres login

# Volume existant : appliquer le seed manuellement
docker compose exec -T postgres psql -U postgres \
    -f /docker-entrypoint-initdb.d/50-login-poc.sql

# Client sur une autre machine : IP LAN du host
POC_ADVERTISE_IP=192.168.1.36 docker compose up -d login
```

Client : `client/connect.ini` → `Server=<ip>,6543`.

## 5. Tests

- `LoginTests` : round-trip des 5 messages + handlers (md5, échec)
- Smoke wire-level validé : RSA blob → RC4 → ClientHello(9.9) →
  ServerLoginAccount → ClientGetWorldServers

## Status (validé wire-level end-to-end, 2026-10-15)

| Étape | État |
|-------|------|
| Handshake RSA→RC4 | ✅ fidèle natif |
| Hello / Login / WorldList | ✅ implémenté + testé |
| Ticket 22 octets | ✅ émis (login_tickets) + consommé par World |
| Auth Gateway/tb_user | 🔶 POC : auth directe `accounts` (md5 accepté) |
| Migration login→world | ✅ `CNE_CW_ServerReceiveTicket`(85) → `ClientReceiveTicketToWorld`(65) |
| Migration world→zone | 🔶 ticket zone émis, client à tester |

## Lancement local (sans Docker)

```bash
# Pré-requis : PostgreSQL du host avec dump database.out chargé
# + seed Database/login-poc.sql appliqué (compte admin/adminmdp)
tools/start-local.sh     # login:6543 world:5200 zone:5201
tools/stop-local.sh      # arrêt
```

`client/connect.ini` : `Server=172.29.131.228,6543` (IP LAN du host ;
`127.0.0.1` marche si le client tourne sur cette machine, l'IP LAN couvre
les deux cas).
