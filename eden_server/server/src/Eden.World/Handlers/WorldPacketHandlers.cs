using Eden.Core.Database.Entities;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking.Messages.World;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.World.Handlers;

/// <summary>
/// Gestionnaire des requêtes et événements du WorldServer (création de perso, liste, sélection, chat).
/// Reversé depuis lapis::CWorldServer et edenfr_db.
/// </summary>
public sealed class WorldPacketHandlers
{
    private readonly ICharacterRepository _characterRepository;
    private readonly IAccountRepository _accountRepository;
    private readonly string _zoneIp;
    private readonly short _zonePort;

    public WorldPacketHandlers(
        ICharacterRepository characterRepository,
        IAccountRepository accountRepository,
        string zoneIp = "127.0.0.1",
        short zonePort = 5201)
    {
        _characterRepository = characterRepository;
        _accountRepository = accountRepository;
        _zoneIp = zoneIp;
        _zonePort = zonePort;
    }

    public async Task HandleWorldClientHelloAsync(NetSession session, WorldClientHelloMessage message, CancellationToken cancellationToken = default)
    {
        EdenLog.Write($"[World] Handshake client reçu depuis {session.RemoteEndPoint}, Version={message.ClientVersion}");
        session.IsAuthenticated = true;

        var accountId = session.AccountId ?? 1;
        var characterList = await _characterRepository.GetCharacterSummariesByAccountIdAsync(accountId);
        var response = new CharacterListResponse
        {
            Characters = characterList.ToList()
        };

        EdenLog.Write($"[World] Envoi de {characterList.Count} personnage(s) au compte ID={accountId}");
        await session.SendAsync(response, cancellationToken);
    }

    public async Task HandleCharacterListRequestAsync(NetSession session, CharacterListRequest request, CancellationToken cancellationToken = default)
    {
        var accountId = request.AccountId > 0 ? request.AccountId : (session.AccountId ?? 1);
        EdenLog.Write($"[World] Requête liste des personnages pour AccountId={accountId} ({session.RemoteEndPoint})");
        var characters = await _characterRepository.GetCharacterSummariesByAccountIdAsync(accountId);

        var response = new CharacterListResponse
        {
            Characters = characters.ToList()
        };

        await session.SendAsync(response, cancellationToken);
    }

    public async Task HandleCreateCharacterRequestAsync(NetSession session, CreateCharacterRequest request, CancellationToken cancellationToken = default)
    {
        EdenLog.Write($"[World] Tentative création personnage : '{request.GivenName}', Classe={request.ClassId}, Race={request.RaceId}, Genre={request.GenderId}");

        var existingChar = await _characterRepository.GetCharacterByNameAsync(request.GivenName);
        if (existingChar != null)
        {
            EdenLog.Write($"[World] Échec création : le nom '{request.GivenName}' est déjà pris.");
            var failResponse = new CreateCharacterResponse
            {
                Success = false,
                ErrorMessage = "Character name already taken."
            };
            await session.SendAsync(failResponse, cancellationToken);
            return;
        }

        var newEntity = new PlayerCharacterEntity
        {
            AccountId = session.AccountId ?? 1,
            GivenName = request.GivenName,
            ClassId = request.ClassId,
            RaceId = request.RaceId,
            GenderId = request.GenderId,
            HairStyle = request.HairStyle,
            HairColor = request.HairColor,
            FaceStyle = request.FaceStyle,
            SkinColor = request.SkinColor,
            Level = 1,
            CurrentHp = 100,
            MaxHp = 100,
            CurrentMp = 50,
            MaxMp = 50,
            NodeId = 5, // Zone de départ par défaut
            PositionX = 0,
            PositionY = 0,
            PositionZ = 0
        };

        var createdId = await _characterRepository.CreateCharacterAsync(newEntity);
        EdenLog.Write($"[World] Personnage '{request.GivenName}' créé avec succès (ID={createdId}) !");

        var summary = new CharacterSummary
        {
            CharacterId = createdId,
            Name = request.GivenName,
            Level = 1,
            ClassId = request.ClassId,
            RaceId = request.RaceId,
            GenderId = request.GenderId,
            HairStyle = request.HairStyle,
            FaceStyle = request.FaceStyle,
            HairColor = request.HairColor,
            SkinColor = request.SkinColor,
            NodeId = 5
        };

        var successResponse = new CreateCharacterResponse
        {
            Success = true,
            Character = summary,
            ErrorMessage = string.Empty
        };

        await session.SendAsync(successResponse, cancellationToken);
    }

    public async Task HandleDeleteCharacterRequestAsync(NetSession session, DeleteCharacterRequest request, CancellationToken cancellationToken = default)
    {
        EdenLog.Write($"[World] Requête suppression personnage ID={request.CharacterId} par AccountId={session.AccountId}");

        var character = await _characterRepository.GetCharacterByIdAsync(request.CharacterId);
        if (character == null || (session.AccountId.HasValue && character.AccountId != session.AccountId.Value))
        {
            EdenLog.Write($"[World] Échec suppression : personnage ID={request.CharacterId} introuvable ou non autorisé.");
            var failResponse = new DeleteCharacterResponse { Success = false };
            await session.SendAsync(failResponse, cancellationToken);
            return;
        }

        await _characterRepository.DeleteCharacterAsync(request.CharacterId, session.AccountId ?? 0);
        EdenLog.Write($"[World] Personnage ID={request.CharacterId} supprimé avec succès.");

        var successResponse = new DeleteCharacterResponse { Success = true };
        await session.SendAsync(successResponse, cancellationToken);
    }

    public async Task HandleSelectCharacterRequestAsync(NetSession session, SelectCharacterRequest request, CancellationToken cancellationToken = default)
    {
        EdenLog.Write($"[World] Sélection personnage ID={request.CharacterId} ({session.RemoteEndPoint})");

        var character = await _characterRepository.GetCharacterByIdAsync(request.CharacterId);
        if (character == null)
        {
            EdenLog.Write($"[World] Personnage introuvable ID={request.CharacterId}");
            return;
        }

        session.CharacterId = request.CharacterId;

        // Génération d'un ticket de zone pour redirection vers ZoneServer (22 octets)
        var ticketBytes = new byte[ZoneTicketResponse.TicketLength];
        Random.Shared.NextBytes(ticketBytes);

        var zoneTicket = new ZoneTicketResponse
        {
            ZoneServerIp = _zoneIp,
            ZoneServerPort = _zonePort,
            ZoneTicket = ticketBytes
        };

        EdenLog.Write($"[World] Ticket de zone émis pour le perso '{character.GivenName}' (Node={character.NodeId}) vers {_zoneIp}:{_zonePort}");
        await session.SendAsync(zoneTicket, cancellationToken);
    }

    public async Task HandleChatMessageAsync(NetSession session, ChatMessage chat, CancellationToken cancellationToken = default)
    {
        EdenLog.Write($"[World] [CHAT {chat.Channel}] {chat.SenderName}: {chat.Message}");
        // Echo / Broadcast
        await session.SendAsync(chat, cancellationToken);
    }
}
