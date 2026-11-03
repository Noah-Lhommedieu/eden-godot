using Eden.Core;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Eden.Core.Database.Entities;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Handlers;
using Eden.Core.Game.Services;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Messages.World;
using Eden.Core.Networking.Session;
using Eden.Core.Networking.Transport;
using Eden.World.Handlers;
using Eden.Core.Logging;

// -----------------------------------------------------------------------------
//  Eden.World — Serveur de Monde C# (.NET 8)
//  Reversé fidèlement depuis lapis::WorldServer (Port 5200 / 5712)
//  Gère la création/sélection de personnages, l'état du monde et la transition Zone.
// -----------------------------------------------------------------------------

var port = EdenConfig.WorldPort;
var zoneIp = EdenConfig.ZoneIp;
var zonePort = (short)EdenConfig.ZonePort;
var ticketIp = EdenConfig.TicketIp;
var ticketPort = EdenConfig.TicketPort;
var ticketServerEnabled = EdenConfig.TicketEnabled;
var advertisedWorldIp = EdenConfig.WorldIp;
var advertisedWorldPort = EdenConfig.WorldAdvPort;

EdenLog.Write("=================================================");
EdenLog.Write("        EDEN ETERNAL - WORLD SERVER (.NET 8)     ");
EdenLog.Write("=================================================");
EdenLog.Write($"[World] Port d'écoute : {port} (TCP)");
EdenLog.Write($"[World] Base de données Game : {Eden.Core.Database.DbConnectionFactory.Describe(Eden.Core.Database.DbRole.Game)}");
EdenLog.Write($"[World] Zone cible par défaut : {zoneIp}:{zonePort}");

// NpgsqlDataSource unique par base — pool propriétaire partagé par
// tous les repos et les accès directs.
var db = Eden.Core.Database.DbConnectionFactory.Create(Eden.Core.Database.DbRole.Game);
var accountDb = Eden.Core.Database.DbConnectionFactory.Create(Eden.Core.Database.DbRole.Account);
await EdenObservability.StartAsync("world", db);

var characterRepository = new CharacterRepository(db);
var accountRepository = new AccountRepository(accountDb);
var handlers = new WorldPacketHandlers(characterRepository, accountRepository, zoneIp, zonePort);
var friendService = new FriendService();
// Persistance native `friends` (player_id, friend_id, friend_type) —
// sans Attach le service restait en mémoire et les relations
// sautaient au reboot/offline.
friendService.Attach(new SocialRepository(db));
var ticketRepo = Eden.Core.Hot.HotWiring.CreateTicketRepository("World", db);

// Famille — équivalent de CGlobalInfo::AllFamilys : cache monde chargé
// depuis les tables natives `family` + `player_characters.family_id`
// (remplace le flux mission `CNE_DBA_ClientAllFamily` au boot).
var familyService = new Eden.Core.Game.Family.FamilyService(
    new Eden.Core.Game.Family.FamilyRepository(db));
await familyService.LoadAsync();
Eden.World.ZoneLinkServer.Families = familyService;

// Item mall natif : catalogue/receivables/limits sur edenfr_account
// (CAccountDBO), points sur edenfr_member.tb_user (billing externe natif
// remplacé par une déduction synchrone).
var itemMallRepository = new ItemMallRepository(accountDb);
var memberDb = Eden.Core.Database.DbConnectionFactory.Create(Eden.Core.Database.DbRole.Member);
var userRepository = new UserRepository(memberDb);
var itemMallServices = new Eden.World.Handlers.ItemMallHandlers.Services
{
    Mall = itemMallRepository,
    Users = userRepository,
    Characters = characterRepository,
    // itemExists = null : le monde n'a pas de gamedata chargé — la zone
    // valide l'item à la livraison (répond 0x18 si absent de l'itemdata).
    Catalog = Eden.Core.Game.Mall.ItemMallCatalog.Load(
        await itemMallRepository.GetCatalogAsync()),
    WorldServerId = 1,
};
EdenLog.Write($"[World] ItemMall : {itemMallServices.Catalog.Entries.Count} entrées de catalogue chargées");

// Claims orphelins d'un éventuel crash/arrêt précédent : toute ligne
// restée en state=2 (verdict ZW 12 jamais revenu) est remise en file
// pour réapparaître dans la liste réclamable du joueur.
var orphaned = await itemMallRepository.RequeueClaimedReceivablesAsync();
if (orphaned > 0)
    EdenLog.Write($"[World] ItemMall : {orphaned} claim(s) orphelin(s) remis en file");

// Client T_ vers le TicketServer natif (GetTicket enregistre le ticket → la
// ZoneServer le valide via son propre lien T_). Si indisponible, fallback sur
// la fabrication locale de tickets (incompatible avec la zone native).
var ticketClient = new Eden.Core.Networking.TicketServerClient(ticketIp, ticketPort);
if (ticketServerEnabled)
{
    _ = Task.Run(async () =>
    {
        try { await ticketClient.ConnectAsync(); }
        catch (Exception ex) { EdenLog.Write($"[World] T_ TicketServer injoignable : {ex.Message} — tickets locaux"); }
    });
}

var dispatcher = new PacketDispatcher();

// --- Commandes reversées CNE_CW_Server* (NCIDs = index d'enregistrement
// natif dans InitWorldCommandTable : 0..83, extraits du binaire WorldServer) ---
dispatcher.Register(() => new CNE_CW_ServerHello(),
    async (session, msg, token) =>
    {
        EdenLog.Write("[World] CNE_CW_ServerHello reçu");
        // Execute natif : ComposeOutput d'un objet float 0x42110000 = 36.25f
        await session.SendAsync(new CNC_CW_ClientHello { Value = 36.25f }, token);
    });

// Présentation du ticket Login→World : [byte[22] CTicket][string macAddr].
// CTicket wire (CTicket::Generate/Set) : [u32 accountId][u32 clientIp]
// [u32 worldIp][u16 worldPort][u32 microtime][u32 random].
// Chemin natif succès : valide ticket.worldIp/Port == ExtAddress et
// ticket.clientIp == remoteIp, résout le compte via ticket[0:4], puis pousse
// 3 sections CNC_CW_ClientGetCharacters (NCID 38 : headers 0, 1, 2).
dispatcher.Register(() => new CNE_CW_ServerReceiveTicket(),
    async (session, msg, token) =>
    {
        var t = msg.Field1;
        var accountId = (int)BitConverter.ToUInt32(t, 0);
        var ticketClientIp = BitConverter.ToUInt32(t, 4);
        var ticketWorldIp = BitConverter.ToUInt32(t, 8);
        var ticketPort = BitConverter.ToUInt16(t, 12);

        var remoteIp = RemoteIpv4(session);
        var worldIpNum = BitConverter.ToUInt32(
            IPAddress.Parse(advertisedWorldIp).GetAddressBytes());
        var checkAddr = EdenConfig.WorldTicketCheckAddr;
        if (checkAddr && (ticketClientIp != remoteIp || ticketWorldIp != worldIpNum
            || ticketPort != (ushort)advertisedWorldPort))
        {
            EdenLog.Write($"[World] Ticket rejeté (addr) : " +
                $"mac={msg.Field2} ticket={ticketWorldIp:x8}:{ticketPort} " +
                $"client={ticketClientIp:x8} attendu={worldIpNum:x8}:{advertisedWorldPort}/{remoteIp:x8}");
            await session.SendAsync(new CNC_CW_ClientTicketInvalid { Value = -1 }, token);
            return;
        }

        // ticket[0:4] = id du compte (edenfr_account.accounts.id) — émis par le
        // LoginServer (natif ou C#). On résout le nom du compte pour la session.
        var account = await accountRepository.GetAccountByIdAsync(accountId);
        if (account == null)
        {
            EdenLog.Write($"[World] Ticket rejeté : compte inconnu accountId={accountId} mac={msg.Field2}");
            await session.SendAsync(new CNC_CW_ClientTicketInvalid { Value = -1 }, token);
            return;
        }

        // Validation native : le TicketServer confirme+consomme le ticket émis
        // par le LoginServer (ServerGetTicket → ServerUseTicket). Si le T_
        // refuse (ticket fabriqué localement par le Login quand son lien T_
        // était down), repli sur le registre login_tickets (consommation
        // one-shot identique).
        if (ticketServerEnabled && ticketClient.Connected)
        {
            var ok = await ticketClient.UseTicketAsync((uint)accountId, t, token);
            if (!ok)
            {
                var viaDb = await ticketRepo.ConsumeLoginByKeyAsync((uint)accountId);
                if (viaDb == null)
                {
                    EdenLog.Write($"[World] Ticket rejeté (T_ + DB) : account={accountId} mac={msg.Field2}");
                    await session.SendAsync(new CNC_CW_ClientTicketInvalid { Value = -1 }, token);
                    return;
                }
                EdenLog.Write($"[World] Ticket accepté via login_tickets : account={accountId}");
            }
        }
        else
        {
            // Lien T_ absent : validation par le registre login_tickets.
            var viaDb = await ticketRepo.ConsumeLoginByKeyAsync((uint)accountId);
            if (viaDb == null)
            {
                EdenLog.Write($"[World] Ticket rejeté (DB, T_ down) : account={accountId} mac={msg.Field2}");
                await session.SendAsync(new CNC_CW_ClientTicketInvalid { Value = -1 }, token);
                return;
            }
        }

        session.AccountId = account.Id;
        session.Username = account.Username;
        session.IsAuthenticated = true;
        EdenLog.Write($"[World] Ticket accepté : '{session.Username}' (account #{session.AccountId})");
        await SendCharacterListAsync(session, token);
    });

// Login en jeu : valide le personnage puis envoie la séquence native
// NCID 40 (ResetTime: u32 unix, i32 -1) → NCID 72 (ApexBuffer: 01 0000)
// → NCID 37 (ticket zone 22 octets).
// CTicket zone : [u32 charId][u32 clientIp][u32 zoneIp][u16 zonePort]
//                [u32 microtime][u32 random] — la zone valide ip/port/remote.
dispatcher.Register(() => new CNE_CW_ServerLoginCharacter(),
    async (session, msg, token) =>
    {
        // Wire natif : [u32 charId][string mac][s8 flag] — le 2e champ est la
        // MAC du client, PAS le nom du perso.
        var character = await characterRepository.GetCharacterByIdAsync((int)msg.CharId);
        if (character == null || character.AccountId != session.AccountId)
        {
            EdenLog.Write($"[World] LoginCharacter rejeté : #{msg.CharId} mac={msg.PlayerName}");
            await session.SendAsync(new CNC_CW_ClientTicketInvalid { Value = -1 }, token);
            return;
        }

        session.CharacterId = character.Id;
        Eden.World.ZoneLinkServer.ClientSessions[character.Id] = session;
        // Mérites spéciaux (special_coins) — le natif pousse
        // SendSpecialCoinInfo (CW 78 `[u16 n][n×(s16 type,i32 total)]`)
        // à l'entrée du perso ; sans ça les compteurs de l'UI
        // mérites/monnaies restent vides jusqu'au prochain gain.
        try
        {
            var coins = await characterRepository
                .GetSpecialCoinsAsync(character.Id);
            if (coins.Count > 0)
            {
                var coinBody = new List<byte>(2 + coins.Count * 6);
                coinBody.AddRange(BitConverter.GetBytes(
                    (ushort)coins.Count));              // u16 : nb d'entrées
                foreach (var (t, total) in coins)
                {
                    coinBody.AddRange(BitConverter.GetBytes(t));      // s16 : type
                    coinBody.AddRange(BitConverter.GetBytes(total));  // i32 : total
                }
                await session.SendPacketAsync(78, coinBody.ToArray());
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write(
                $"[World] SpecialCoins char=#{character.Id} : {ex.Message}");
        }
        // Relations depuis `friends` (mutuels + invitations pendantes +
        // blacklist) — le client demande la liste via QueryFriendInfo
        // juste après, le service doit être hydraté avant.
        await friendService.LoadForAsync(character.Id);
        // Badge « en ligne » chez les amis connectés — le natif pousse
        // un UpdateFriendMember à chaque changement d'état (login/logout).
        _ = Eden.World.Handlers.FriendHandlers
            .NotifyFriendsOnlineStateAsync(character.Id);

        await session.SendAsync(new CNC_CW_ClientResetTime
        {
            Field1 = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Field2 = -1,
        }, token);
        await session.SendAsync(new CNC_CW_ClientApexBufferTransfer
        {
            Field1 = 1,
        }, token);

        var clientIp = RemoteIpv4(session);
        var zoneIpNum = BitConverter.ToUInt32(
            IPAddress.Parse(zoneIp).GetAddressBytes());

        // Chemin natif : le World demande le ticket zone au TicketServer
        // (ServerGetTicket NCID 0 → ClientGetTicket NCID 2). Le TicketServer
        // l'enregistre ; la ZoneServer le validera via son propre UseTicket.
        byte[]? ticket = null;
        if (ticketServerEnabled)
            ticket = await ticketClient.GetTicketAsync(
                (uint)character.Id, clientIp, zoneIpNum, (ushort)zonePort, token);

        if (ticket == null)
        {
            // Fallback local (zone C# ou tests sans TicketServer natif).
            var microtime = (uint)(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() & 0xFFFFFFFF);
            ticket = new byte[22];
            BitConverter.GetBytes((uint)character.Id).CopyTo(ticket, 0);
            BitConverter.GetBytes(clientIp).CopyTo(ticket, 4);
            BitConverter.GetBytes(zoneIpNum).CopyTo(ticket, 8);
            BitConverter.GetBytes((ushort)zonePort).CopyTo(ticket, 12);
            BitConverter.GetBytes(microtime).CopyTo(ticket, 14);
            BitConverter.GetBytes((uint)Random.Shared.Next()).CopyTo(ticket, 18);
        }

        await session.SendAsync(
            new CNC_CW_ClientReceiveTicketToZoneServer { Ticket = ticket }, token);
        EdenLog.Write($"[World] LoginCharacter OK : '{character.GivenName}' " +
            $"#{character.Id} -> zone {zoneIp}:{zonePort}");
    });

// --- Amis : invitation par nom, réponse, suppression, requête de
// liste — NCID 52 (ClientUpdateFriendMember) chunké à 10 + 53
// invitation, cf. Handlers/FriendHandlers.cs ---
Eden.World.Handlers.FriendHandlers.Register(dispatcher,
    new Eden.World.Handlers.FriendHandlers.Services
    {
        Friends = friendService,
        Characters = characterRepository,
        // TC_QueryFriend natif pousse aussi prestige (60) + mérites
        // (78) — même repo que le chemin famille.
        Prestige = new Eden.Core.Database.Repositories
            .PrestigeRepository(db),
        WorldServerId = 1,
    });
// Requêtes post-entrée du client (captures/mitm_native_entry.log, run native
// 17:22:16) : 17->57 `0000`, 25->64 `0,0`, 10->60 prestige
// {1,1,1,300}. Payloads bruts reproduits à l'identique — les classes générées
// pour 57/78 utilisent un format count-prefixe, pas les champs fixes extraits.
// Item mall natif : les 6 commandes CW (15/17/18/23/24/25) — formats
// vérifiés par désasm world (Serialize/Execute de chaque classe).
Eden.World.Handlers.ItemMallHandlers.Register(dispatcher, itemMallServices);

// Entraide (onglet communauté) : état joueur (27) + recherche (33) → 81.
Eden.World.Handlers.MutuallyHelpHandlers.Register(dispatcher,
    new Eden.World.Handlers.MutuallyHelpHandlers.Services
    {
        Characters = characterRepository,
        WorldServerId = 1,
    });

// Famille : NCID 9 (ops), 10 (query info), 73 (query emblem) —
// Handlers/FamilyHandlers.cs ; le stub prestige du cas « sans
// famille » est reproduit dans SendPrestigeStubAsync.
Eden.World.Handlers.FamilyHandlers.Register(dispatcher,
    new Eden.World.Handlers.FamilyHandlers.Services
    {
        Families = familyService,
        Prestige = new Eden.Core.Database.Repositories
            .PrestigeRepository(db),
        WorldServerId = 1,
    });

// Commandes CW éparses : exploit-rank 19, captcha 26 (no-op natif),
// plugin-info 28 (log), refresh mondes 29→66, mot de passe perso 30/31
// → 75/76 — Handlers/MiscHandlers.cs (preuves désasm world par
// commande, cf. en-tête).
Eden.World.Handlers.MiscHandlers.Register(dispatcher,
    new Eden.World.Handlers.MiscHandlers.Services
    {
        Characters = characterRepository,
        Accounts = accountRepository,
        Tickets = ticketClient,
    });

// KeepAlive périodique du client (NCID 2) — accusé de réception simple.
dispatcher.Register(() => new CNE_CW_ServerKeepAlive(),
    (session, msg, token) => Task.CompletedTask);

// --- Création de personnage (NCIDs 32 / 4 / 5) ---
// Codes natifs CNC_CW_ClientVerifyCharacterName (enum dans l'ordre des
// strings du binaire) : 0=success, 3=nom déjà en base, 4=nom en cours de
// création par un autre, 5=compte a déjà 3 persos, 6=compte déjà en création,
// 7=erreur données, 8=nom trop long, 9=nom trop court, 10=caractères illégaux,
// 11=nom elfe illégal, 12=erreur indéfinie.
// Flags observés : NCID 32 OK -> {0,1} ; NCID 4 OK -> {0,0} ; erreur -> {code,0}.
static bool IsLegalCharName(string name)
{
    if (name.Length < 2 || name.Length > 14) return false;
    foreach (var ch in name)
        if (!char.IsLetterOrDigit(ch)) return false;
    return true;
}

static Task SendNameResultAsync(NetSession session, short code, byte flag, CancellationToken token)
    => session.SendAsync(new CNC_CW_ClientVerifyCharacterName
    {
        Field1 = code, Field2 = flag,
    }, token);

dispatcher.Register(() => new CNE_CW_ServerCreateCharacterNameCheck(),
    async (session, msg, token) =>
    {
        var name = msg.Value;
        short code;
        if (name.Length > 14) code = 8;
        else if (name.Length < 2) code = 9;
        else if (!IsLegalCharName(name)) code = 10;
        else if (await characterRepository.GetCharacterByNameAsync(name) != null) code = 3;
        else code = 0;
        EdenLog.Write($"[World] CreateNameCheck '{name}' → code={code}");
        await SendNameResultAsync(session, code, (byte)(code == 0 ? 1 : 0), token);
    });

dispatcher.Register(() => new CNE_CW_ServerCreateCharacterName(),
    async (session, msg, token) =>
    {
        var name = msg.Field1;
        short code;
        if (name.Length > 14) code = 8;
        else if (name.Length < 2) code = 9;
        else if (!IsLegalCharName(name)) code = 10;
        else if (await characterRepository.GetCharacterByNameAsync(name) != null) code = 3;
        else code = 0;
        if (code == 0) session.PendingCharName = name;
        EdenLog.Write($"[World] CreateCharacterName '{name}' → code={code}");
        await SendNameResultAsync(session, code, 0, token);
    });

// Wire natif NCID 5 : [u8 race][u8 class][u8 flag][32B apparence] — le flag
// est renvoyé comme 2e short de l'entrée de liste (create_flag en DB).
dispatcher.Register(() => new CNE_CW_ServerCreateCharacterData(),
    async (session, msg, token) =>
    {
        if (session.AccountId is not { } accountId || session.Username is not { } accountName)
            return;

        var name = session.PendingCharName;
        if (string.IsNullOrEmpty(name))
        {
            await SendNameResultAsync(session, 5, 0, token);
            return;
        }
        if (await characterRepository.GetCharacterCountAsync(accountId) >= 3)
        {
            await SendNameResultAsync(session, 5, 0, token);
            return;
        }

        // Feature natif ∈ [0,7] (CreateCharacterIntoDB : ≥ 8 refusé) ;
        // stocké 1..8 (create_flag) = trait héroïque, enchant 52000+n.
        if (Eden.Core.Game.HeroTrait.FromWireFeature(msg.Field3) is not { } trait)
        {
            EdenLog.Write($"[World] Création '{name}' refusée : " +
                $"Feature invalide {msg.Field3}");
            await SendNameResultAsync(session, 12, 0, token);
            return;
        }

        var app = new byte[32];
        if (msg.Appearance is { Length: > 0 })
            Array.Copy(msg.Appearance, app, Math.Min(32, msg.Appearance.Length));

        // Bitfields natifs du blob — voir AppearanceBlob (désassemblage
        // CCharacterDBO::CreateCharacterIntoDB).
        var ap = Eden.Core.Game.AppearanceBlob.Decode(app);
        var entity = new PlayerCharacterEntity
        {
            GivenName = name,
            AccountId = accountId,
            AccountName = accountName,
            RaceId = msg.Field1,
            ClassId = msg.Field2,
            GenderId = ap.Gender,
            HairStyle = ap.HairStyle,
            FaceStyle = ap.FaceStyle,
            HairColor = ap.HairColor,
            SkinColor = ap.SkinColor,
            EyesColor = ap.EyesColor,
            EyesColor2 = ap.EyesColor2,
            NodeId = 2,
            PositionX = 332.187988,
            PositionY = 298.277008,
            PositionZ = 0.0,
            FacingDirection = 2.356190,
            Level = 1,
            ReviveAreaId = 131075,
            AppearanceData = app,
            CreationFlag = trait,
        };

        try
        {
            await using var fnaccountConn = accountDb.CreateConnection();
            var id = await characterRepository.CreateCharacterNativeAsync(fnaccountConn, entity, (byte)trait);
            EdenLog.Write($"[World] Personnage créé : '{name}' #{id} " +
                $"(race={msg.Field1} class={msg.Field2} feature={msg.Field3} trait={trait})");
            session.PendingCharName = null;
            await SendCharacterListAsync(session, token);
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[World] Création '{name}' échouée : {ex.Message}");
            await SendNameResultAsync(session, 12, 0, token);
        }
    });

// Annulation de création (NCID 7) -> CNC_CW_ClientCancelCreateCharacterResp (41).
dispatcher.Register(() => new CNE_CW_ServerCancelCreateCharacterReq(),
    async (session, msg, token) =>
    {
        session.PendingCharName = null;
        await session.SendAsync(new CNC_CW_ClientCancelCreateCharacterResp(), token);
    });

// Suppression : [int charId][string nom de confirmation] -> liste rafraîchie.
dispatcher.Register(() => new CNE_CW_ServerRemoveCharacter(),
    async (session, msg, token) =>
    {
        if (session.AccountId is not { } accountId) return;
        var deleted = await characterRepository.DeleteCharacterAsync(msg.Field1, accountId);
        EdenLog.Write($"[World] RemoveCharacter #{msg.Field1} '{msg.Field2}' → {deleted}");
        await SendCharacterListAsync(session, token);
    });

// Télémétrie anti-cheat périodique (NCID 71) — ignorée côté monde.
dispatcher.Register(() => new CNE_CW_ServerApexBufferTransfer(),
    (session, msg, token) => Task.CompletedTask);

// Envoie les 3 sections natives : header 0 (vide), 1 (persos), 2 (vide).
async Task SendCharacterListAsync(NetSession session, CancellationToken token)
{
    var entries = new List<CNC_CW_ClientGetCharacters.Entry>();
    if (session.AccountId is { } accountId)
    {
        try
        {
            var rows = await characterRepository.GetCharactersForListAsync(accountId);
            foreach (var r in rows)
            {
                var e = new CNC_CW_ClientGetCharacters.Entry
                {
                    Id = r.Id,
                    Name = r.Name,
                    ClassId = r.ClassId,
                    Feature = r.ClassLv,
                    RaceId = r.RaceId,
                    NodeId = r.NodeId,
                    Level = r.Level,
                };
                Array.Copy(r.Appearance, e.Appearance, 32);
                var node0 = new List<CNC_CW_ClientGetCharacters.PreviewItem>();
                foreach (var (container, loc, itemId) in r.PreviewItems)
                    if (container == 0)
                        node0.Add(CNC_CW_ClientGetCharacters.PreviewItem.Equipped(loc, itemId));
                e.Preview.Add(new KeyValuePair<ushort, List<CNC_CW_ClientGetCharacters.PreviewItem>>(0, node0));
                e.Preview.Add(new KeyValuePair<ushort, List<CNC_CW_ClientGetCharacters.PreviewItem>>(1, new()));
                entries.Add(e);
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[World] Liste personnages : {ex.Message}");
        }
    }
    await session.SendAsync(new CNC_CW_ClientGetCharacters { Header = 0 }, token);
    var section1 = new CNC_CW_ClientGetCharacters { Header = 1 };
    section1.Characters.AddRange(entries);
    await session.SendAsync(section1, token);
    await session.SendAsync(new CNC_CW_ClientGetCharacters { Header = 2 }, token);
    EdenLog.Write($"[World] --> ClientGetCharacters : {entries.Count} perso(s) (sections 0/1/2)");
}

static uint RemoteIpv4(NetSession session)
{
    if (session.RemoteEndPoint is { } ep &&
        IPEndPoint.TryParse(ep, out var ipEp) &&
        ipEp.Address.AddressFamily == AddressFamily.InterNetwork)
        return BitConverter.ToUInt32(ipEp.Address.GetAddressBytes());
    return 0;
}

// Clé RSA-2048 à exposant 2 octets (e=257) — blob natif = 266 octets.
// Mode modern : TLS la remplace — plus matérialisée (retirée en
// phase4-c2). Null passé à ZoneLinkServer en mode modern.
var rsaKey = EdenConfig.LegacyTransport
    ? EdenCrypto.CreateServerRsa() : null;

// Lien interne World<->Zone : la zone s'y connecte (serverstatus.int_port) et
// attend CharacterArrivedDest après validation du ticket du client.
var zwPort = EdenConfig.ZwPort;
var zoneLink = new Eden.World.ZoneLinkServer(zwPort, rsaKey, accountDb, db);
_ = Task.Run(() => zoneLink.RunAsync(CancellationToken.None));
// Mode legacy : RSA/RC4 piloté par WORLD_CRYPTO ; mode modern :
// handshake TLS toujours (le chiffrement ne se désactive plus).
var cryptoHandshake = EdenConfig.WorldCrypto;
var handshakeNeeded = !EdenConfig.LegacyTransport || cryptoHandshake;

var listener = new TcpListener(IPAddress.Any, port);
listener.Start();
EdenLog.Write($"[World] Serveur prêt et en attente de joueurs sur 0.0.0.0:{port}...\n");
// Timestamps pour mesurer le timing exact des déconnexions client.
EdenLog.Write($"[World] WORLD_FLOW={EdenConfig.WorldFlow}");

// Manifest gamedata (phase 4d) — EDEN_GAMEDATA_DIR / EDEN_GAMEDATA_HASH :
// le world ne charge pas de tables mais sert l'empreinte de comparaison
// au handshake de version.
EdenLog.Write(Eden.Core.Data.GameDataManifest.Current is { } m
    ? $"[World] gamedata manifest sha256={m.Hex} ({m.FileCount} .ini)"
    : "[World] gamedata manifest : absent (EDEN_GAMEDATA_DIR non résolu)");

while (true)
{
    var tcpClient = await listener.AcceptTcpClientAsync();
    _ = Task.Run(async () =>
    {
        // Transport par EDEN_TRANSPORT : TLS 1.3 (modern, défaut) ou
        // RSA/RC4 lapis quand WORLD_CRYPTO actif / TCP clair sinon
        // (legacy) — les deux derrière la même NetSession.
        await using var session = new NetSession(
            TransportFactory.ForAcceptedSocket(tcpClient, rsaKey,
                cryptoHandshake));
        var t0 = DateTime.Now;
        EdenLog.Write($"[{t0:HH:mm:ss.fff}] [World] [CONNEXION] Joueur connecté depuis {session.RemoteEndPoint}");
        // Trames pipelinées derrière le VersionHello — dispatchées
        // avant la première lecture socket (rempli par le gate 4d).
        var pending = new List<byte[]>();

        try
        {
            if (handshakeNeeded)
            {
                if (!await session.StartTransportAsync())
                {
                    EdenLog.Write($"[World] Handshake échoué : {session.CloseReason}");
                    return;
                }
                EdenLog.Write($"[World] Handshake {(EdenConfig.LegacyTransport ? "RC4" : "TLS 1.3")} établi");

                // Handshake de version (phase 4d) — transport modern
                // seulement, AVANT le push ClientHello : la 1re trame
                // doit être VersionHello (0xFFFF) ; rejet → Reject
                // (0xFFFD) + fermeture propre.
                if (!EdenConfig.LegacyTransport && EdenConfig.VersionCheck)
                {
                    using var gateCts = new CancellationTokenSource(
                        NetSession.HandshakeTimeout);
                    var gate = await VersionGate.HandshakeAsync(session,
                        VersionGate.Options.FromConfig(), gateCts.Token);
                    if (!gate.Accepted)
                    {
                        EdenLog.Write($"[World] [VERSION_REJECT] {session.RemoteEndPoint} : {gate.Reason}");
                        return;
                    }
                    var h = gate.Hello!;
                    EdenLog.Write($"[World] [VERSION_OK] {session.RemoteEndPoint} " +
                        $"client={h.ClientVersion} proto={h.Proto} ({gate.Reason})");
                    if (gate.Remaining.Count > 0)
                        pending.AddRange(gate.Remaining);
                }

                // Comme au Login : le serveur pousse ClientHello dès la crypto
                // prête (valeur float native observée : 0x42110000 = 36.25f).
                await session.SendAsync(new CNC_CW_ClientHello { Value = 36.25f });
            }

            while (true)
            {
                var packets = pending.Count > 0
                    ? pending
                    : await session.ReadPacketsAsync();
                pending = new List<byte[]>();
                if (packets.Count == 0) break;
                foreach (var payload in packets)
                {
                    if (payload.Length < 2) continue;
                    var opcode = BitConverter.ToUInt16(payload, 0);
                    var inStream = new InBinStream(payload.AsSpan(2).ToArray());
                    try
                    {
                        var dispatched = await dispatcher.DispatchAsync(opcode, inStream, session);
                        EdenLog.Write($"[{DateTime.Now:HH:mm:ss.fff}] " + (dispatched
                            ? $"[World] <-- opcode={opcode} dispatché"
                            : $"[World] <-- opcode={opcode} inconnu ({payload.Length}B)"));
                    }
                    catch (Exception dex)
                    {
                        // Un paquet mal formé ne doit pas terminer la session.
                        EdenLog.Write($"[{DateTime.Now:HH:mm:ss.fff}] [World] <-- opcode={opcode} erreur dispatch : {dex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            EdenLog.Write($"[World] Erreur session ({session.RemoteEndPoint}) : {ex.Message}");
        }
        finally
        {
            if (session.CharacterId is { } cid)
            {
                // Suppression CONDITIONNELLE : une session obsolète qui
                // traîne (FIN détecté tard) ne doit pas évincer la
                // reconnexion déjà enregistrée — sinon les notifs
                // famille/amis/tell sautent jusqu'au prochain login.
                Eden.World.ZoneLinkServer.ClientSessions.TryRemove(
                    new KeyValuePair<int, NetSession>(cid, session));
                // UpdateMutuallyHelpState(char,-1) natif à la déco.
                Eden.World.Handlers.MutuallyHelpHandlers.Unregister(cid);
                // SetLFG(char,false) natif — retrait de la liste LFG.
                Eden.World.Handlers.MiscHandlers.UnregisterLfg(cid);
            }
            EdenLog.Write($"[{DateTime.Now:HH:mm:ss.fff}] [World] [DECONNEXION] Joueur {session.RemoteEndPoint} déconnecté (après {(DateTime.Now - t0).TotalMilliseconds:F0} ms).");
        }
    });
}
