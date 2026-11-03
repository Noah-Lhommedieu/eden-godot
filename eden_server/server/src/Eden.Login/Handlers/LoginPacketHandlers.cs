using Eden.Core;
using System.Net;
using System.Net.Sockets;
using Eden.Core.Database.Entities;
using Eden.Core.Security;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Messages.Login;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Login.Handlers;

/// <summary>
/// Handlers du protocole client→LoginServer — fidèles aux CNE_CL_* natifs
/// (CNE_CL_ServerHello / ServerLoginAccount / ServerLoginWorld).
///
/// Flux reversé (LoginServer, InitLoginCommandTable + Execute désassemblés,
/// NCIDs = index d'enregistrement — confirmé par capture réelle :
/// hello serveur déchiffré = `03 00 66 66 1e 41` → NCID 3 + float 9.9) :
///   NCID 0 CNC_CL_ServerHello        (str version) → CNC_CL_ClientHello (3, float 9.9)
///   NCID 1 CNC_CL_ServerLoginAccount (str×3 : user, mdp md5, version)
///                                                  → CNC_CL_ClientGetWorldServers (5)
///                                                    ou CNC_CL_ClientLoginFail (4)
///   NCID 2 CNC_CL_ServerLoginWorld   (i16,str)     → CNC_CL_ServerLoginWorld (i16,str)
///                                                  + CNC_CL_ClientReceiveTicketToWorldServer (6, 22B)
/// </summary>
public sealed class LoginPacketHandlers
{
    /// <summary>Tentatives de login par IP — fenêtre 1 min.</summary>
    private static readonly System.Collections.Concurrent
        .ConcurrentDictionary<string, (DateTime Window, int Count)>
        _attempts = new();
    /// <summary>Max tentatives de login par IP et par minute
    /// (<c>EDEN_LOGIN_MAX_ATTEMPTS</c>, défaut 10).</summary>
    private static readonly int MaxAttemptsPerMinute =
        EdenConfig.LoginMaxAttempts;

    /// <summary>Version serveur envoyée dans CNC_CL_ClientHello (float 9.9 natif).</summary>
    public const float ServerVersion = 9.9f;

    /// <summary>Codes d'erreur ClientLoginFail observés dans le binaire natif.</summary>
    public const short LoginFailAuth = 2;      // échec d'authentification générique
    public const short LoginFailWorldDown = 6; // monde indisponible
    public const short LoginFailWorldFull = 9; // monde plein
    public const short LoginFailGeneric = 10;

    private readonly IAccountRepository _accountRepository;
    private readonly IWorldRepository _worldRepository;
    private readonly ITicketRepository? _ticketRepository;
    private readonly TicketServerClient? _ticketServer;
    private readonly string? _advertiseIpOverride;
    private readonly int? _advertisePortOverride;
    /// <summary>Limite de tentatives/min effective — <paramref name="maxAttemptsPerMinute"/>
    /// permet aux tests de neutraliser le rate-limit (la fenêtre statique
    /// est partagée entre tous les tests du process).</summary>
    private readonly int _maxAttemptsPerMinute;

    public LoginPacketHandlers(
        IAccountRepository accountRepository,
        IWorldRepository worldRepository,
        ITicketRepository? ticketRepository = null,
        string? advertiseIpOverride = null,
        int? advertisePortOverride = null,
        TicketServerClient? ticketServer = null,
        int? maxAttemptsPerMinute = null)
    {
        _accountRepository = accountRepository;
        _worldRepository = worldRepository;
        _ticketRepository = ticketRepository;
        _advertiseIpOverride = advertiseIpOverride;
        _advertisePortOverride = advertisePortOverride;
        _ticketServer = ticketServer;
        _maxAttemptsPerMinute = maxAttemptsPerMinute ?? MaxAttemptsPerMinute;
    }

    /// <summary>CNE_CL_ServerHello — le client salue ; réponse ClientHello(9.9).</summary>
    public async Task HandleServerHelloAsync(NetSession session, CNC_CL_ServerHello message, CancellationToken cancellationToken)
    {
        EdenLog.Write($"[Login] [HELLO] ServerHello '{message.Value}' ({session.RemoteEndPoint})");
        await session.SendAsync(new CNC_CL_ClientHello { Value = ServerVersion }, cancellationToken);
    }

    /// <summary>
    /// CNE_CL_ServerLoginAccount — authentification.
    /// Natif : username = 1re string ("AccountName: [%s]"), password = 2e,
    /// 3e = version client / jeton additionnel transmis au Gateway.
    /// POC : auth directe sur edenfr_account.accounts (le natif délègue au Gateway
    /// via CNC_G_ServerAccountLogin → account_login() sur edenfr_member.tb_user).
    /// </summary>
    public async Task HandleLoginAccountAsync(NetSession session, CNC_CL_ServerLoginAccount message, CancellationToken cancellationToken)
    {
        var username = message.Field1;
        var password = message.Field2;
        EdenLog.Write($"[Login] [AUTH] Tentative compte='{username}' ({session.RemoteEndPoint})");

        // Rate-limit des tentatives par IP : fenêtre d'une minute,
        // au-delà de MaxAttemptsPerMinute → rejet sans consulter la DB.
        var ip = session.RemoteEndPoint?.Split(':')[0] ?? "?";
        var now = DateTime.UtcNow;
        var bucket = _attempts.GetOrAdd(ip, _ => (now, 0));
        if (now - bucket.Window >= TimeSpan.FromMinutes(1))
            bucket = (now, 0);
        bucket = (bucket.Window, bucket.Count + 1);
        _attempts[ip] = bucket;
        if (bucket.Count > _maxAttemptsPerMinute)
        {
            EdenLog.Write($"[Login] [RATE-LIMIT] {ip} : {bucket.Count} tentatives/min");
            await session.SendAsync(new CNC_CL_ClientLoginFail
                { Value = LoginFailGeneric }, cancellationToken);
            return;
        }

        Account? account = null;
        try
        {
            account = await _accountRepository.GetAccountByUsernameAsync(username);
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Login] [DB] Avertissement accès base : {ex.Message}");
        }

        // Credential wire canonique : Godot envoie toujours md5(mdp)
        // (32 hex → normalisé minuscule) ; un émetteur plaintext est
        // ramené à md5(reçu) — le PHC stocké couvre donc toujours la
        // forme md5, quel que soit l'émetteur ayant déclenché le rehash.
        var wireCredential = PasswordHasher.CanonicalWireCredential(password);

        // Argon2id : password_hash rempli (préfixe PHC) → Verify ; sinon
        // chemin legacy — le natif compare via account_login() à
        // tb_user.pwd (md5), notre POC valide contre edenfr_account.accounts :
        // plaintext OU md5(stocké) acceptés. Succès legacy → rehash
        // argon2id post-auth (comptes migrés à leur prochaine connexion).
        bool isPasswordValid;
        bool needsRehash = false;
        if (account != null && PasswordHasher.IsArgon2Phc(account.PasswordHash))
        {
            isPasswordValid = PasswordHasher.Verify(wireCredential, account.PasswordHash);
        }
        else
        {
            isPasswordValid = account != null &&
                (string.Equals(account.Password, password, StringComparison.Ordinal)
                 || string.Equals(PasswordHasher.Md5Hex(account.Password), password, StringComparison.OrdinalIgnoreCase));
            // Un password_hash non-PHC (malformé) est traité comme absent
            // et sera réécrit par le rehash — pas de crash sur contenu DB.
            needsRehash = isPasswordValid;
        }

        // Compte de développement de secours si la DB ne répond pas.
        if (account == null && username == "test123" && password == "test123")
        {
            isPasswordValid = true;
            account = new Account { Id = 1, Username = "test123", Password = "test123", WorldServerId = 1010 };
        }

        if (!isPasswordValid || account == null)
        {
            EdenLog.Write($"[Login] [AUTH_FAIL] Identifiants invalides pour '{username}'");
            await session.SendAsync(
                new CNC_CL_ClientLoginFail { Value = LoginFailAuth }, cancellationToken);
            return;
        }

        session.AccountId = account.Id;
        session.Username = account.Username;
        session.IsAuthenticated = true;
        EdenLog.Write($"[Login] [AUTH_OK] '{account.Username}' authentifié (id={account.Id})");

        List<WorldServerEntry> worldList;
        try
        {
            worldList = await _worldRepository.GetWorldStatesForAccountAsync(account.Id);
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Login] [DB] Liste des mondes indisponible : {ex.Message}");
            worldList = new List<WorldServerEntry>();
        }

        // POC : permet de remplacer l'IP/port annoncés (les IP du dump
        // d'origine 47.242.253.210 ne sont pas joignables en local).
        if (_advertiseIpOverride != null)
        {
            foreach (var w in worldList)
            {
                w.IpAddress = _advertiseIpOverride;
                if (_advertisePortOverride.HasValue) w.Port = (short)_advertisePortOverride.Value;
            }
        }

        var response = new CNC_CL_ClientGetWorldServers { LastWorldId = (short)account.WorldServerId };
        foreach (var w in worldList)
        {
            response.Worlds.Add(new CNC_CL_ClientGetWorldServers.Entry
            {
                Id = w.WorldId,
                Name = w.WorldName,
                Ip = w.IpAddress,
                Port = w.Port,
                Online = w.Population,
                // Le client teste ce slot contre 0x64 (100) : >=100 = "channel
                // has reached its maximum capacity". Le natif y envoie la charge
                // en pourcentage (online*100/maxnum), pas maxnum_user brut.
                MaxNum = (short)(w.MaxNum > 0 ? w.Population * 100 / w.MaxNum : 0),
                State = w.State,
                ShowOrder = w.ShowOrder,
                Flags = 0,
                CharacterCount = w.CharacterCount,
            });
        }

        await session.SendAsync(response, cancellationToken);
        EdenLog.Write($"[Login] --> [WORLDS] {response.Worlds.Count} channel(s) envoyés à '{username}'");

        // Rehash-at-login : migration legacy → Argon2id APRÈS l'envoi
        // de la liste des mondes — le coût volontaire de l'argon2
        // (~60-100 ms) ne retarde pas la réponse d'auth. Échec = log
        // seulement, jamais bloquant pour le joueur.
        if (needsRehash)
            await RehashPasswordAsync(account, wireCredential);
    }

    /// <summary>
    /// Migration d'un compte legacy vers Argon2id : calcule le PHC du
    /// credential wire canonique puis UPDATE <c>accounts.password_hash</c>.
    /// Jamais appelé pour le compte synthétique de secours
    /// (<c>needsRehash</c> reste faux quand le repo n'a pas rendu de
    /// compte — pas de ligne à mettre à jour).
    /// </summary>
    private async Task RehashPasswordAsync(Account account, string wireCredential)
    {
        try
        {
            var phc = PasswordHasher.HashWireCredential(wireCredential);
            await _accountRepository.UpdatePasswordHashAsync(account.Id, phc);
            EdenLog.Write($"[Login] [auth] rehash {account.Username} " +
                $"→ argon2id (id={account.Id})");
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Login] [auth] rehash '{account.Username}' " +
                $"échoué : {ex.Message}");
        }
    }

    /// <summary>
    /// CNE_CL_ServerLoginWorld — sélection d'un channel.
    /// Natif : demande un ticket au TicketServer puis renvoie
    /// CNC_CL_ServerLoginWorld (nom, ip) + ClientReceiveTicketToWorldServer
    /// (22 octets CTicket). POC : ticket CTicket généré localement.
    /// </summary>
    public async Task HandleLoginWorldAsync(NetSession session, CNC_CL_ServerLoginWorld message, CancellationToken cancellationToken)
    {
        if (!session.IsAuthenticated || session.AccountId is not { } accountId)
        {
            EdenLog.Write($"[Login] [REJET] LoginWorld sans auth ({session.RemoteEndPoint})");
            await session.SendAsync(
                new CNC_CL_ClientLoginFail { Value = LoginFailAuth }, cancellationToken);
            return;
        }

        var worldId = message.Field1;
        EdenLog.Write($"[Login] [SELECT] '{session.Username}' → monde {worldId}");

        WorldServerEntity? world = null;
        try
        {
            world = await _worldRepository.GetWorldByIdAsync(worldId);
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[Login] [DB] Recherche monde {worldId} : {ex.Message}");
        }

        if (world == null || world.State >= 3)
        {
            await session.SendAsync(
                new CNC_CL_ClientLoginFail { Value = LoginFailWorldDown }, cancellationToken);
            return;
        }

        try
        {
            await _accountRepository.SaveLastWorldAsync(accountId, (short)worldId);
        }
        catch { /* non bloquant */ }

        var advertiseIp = _advertiseIpOverride ?? world.IpAddress;
        var advertisePort = _advertisePortOverride ?? world.Port;

        // Confirmation du monde choisi — wire S→C : [short worldId][string ip].
        await session.SendAsync(new CNC_CL_ServerLoginWorld
        {
            Field1 = (short)worldId,
            Field2 = advertiseIp,
        }, cancellationToken);

        // CTicket 22 octets (CTicket::Generate natif) :
        // [u32 accountId][u32 clientIp][u32 worldIp]
        // [u16 worldPort][u32 microtime][u32 random]
        // Le World natif extrait l'accountId depuis ticket[0:4] et vérifie
        // clientIp/worldIp/port contre sa propre ExtAddress.
        var clientIp = RemoteIpv4(session);
        var worldIp = Ipv4ToUInt32(advertiseIp);

        // Chemin natif : le Login demande le ticket au TicketServer
        // (ServerGetTicket NCID 0 → ClientGetTicket NCID 2) qui
        // l'ENREGISTRE — le World le validera par ServerUseTicket.
        // Sans ça le ticket fabriqué localement est toujours refusé
        // par le TicketServer («Cannot connect to server» au ch01).
        byte[]? ticket = null;
        if (_ticketServer is { Connected: true })
        {
            ticket = await _ticketServer.GetTicketAsync(
                (uint)accountId, clientIp, worldIp, (ushort)advertisePort,
                cancellationToken);
        }
        if (ticket == null)
        {
            ticket = new byte[CNC_CL_ClientReceiveTicketToWorldServer.TicketLength];
            BitConverter.GetBytes((uint)accountId).CopyTo(ticket, 0);
            BitConverter.GetBytes(clientIp).CopyTo(ticket, 4);
            BitConverter.GetBytes(worldIp).CopyTo(ticket, 8);
            BitConverter.GetBytes((ushort)advertisePort).CopyTo(ticket, 12);
            BitConverter.GetBytes((uint)Environment.TickCount64).CopyTo(ticket, 14);
            BitConverter.GetBytes((uint)Random.Shared.Next()).CopyTo(ticket, 18);
        }

        // Enregistre le ticket pour validation par WorldServer
        // (CNE_CW_ServerReceiveTicket — byte[22]+accountName).
        if (_ticketRepository != null && session.Username != null)
        {
            try
            {
                await _ticketRepository.IssueLoginAsync(
                    (uint)accountId, accountId, session.Username, clientIp, worldIp, (ushort)advertisePort);
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[Login] [DB] Enregistrement ticket login : {ex.Message}");
            }
        }

        await session.SendAsync(
            new CNC_CL_ClientReceiveTicketToWorldServer { Ticket = ticket },
            cancellationToken);
        EdenLog.Write($"[Login] --> [TICKET] '{world.Name}' {advertiseIp}:{advertisePort} (account={accountId})");
    }

    private static uint RemoteIpv4(NetSession session)
    {
        if (session.RemoteEndPoint is { } ep &&
            IPEndPoint.TryParse(ep, out var ipEp) &&
            ipEp.Address.AddressFamily == AddressFamily.InterNetwork)
            return Ipv4ToUInt32(ipEp.Address.ToString());
        return 0;
    }

    private static uint Ipv4ToUInt32(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return 0;
        var b = addr.GetAddressBytes();
        return (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
    }
}
