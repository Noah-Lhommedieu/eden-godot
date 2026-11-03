using Eden.Core.Database.Entities;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking.Messages.World;
using Eden.Core.Networking.Session;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Gestionnaire des paquets en jeu du ZoneServer (déplacement, vie, sorts, stats, chat de zone).
/// Reversé depuis lapis::CZoneServer et edenfr_db.player_characters.
/// </summary>
public sealed class ZonePacketHandlers
{
    private readonly ICharacterRepository _characterRepository;
    private readonly int _nodeId;

    public ZonePacketHandlers(ICharacterRepository characterRepository, int nodeId = 5)
    {
        _characterRepository = characterRepository;
        _nodeId = nodeId;
    }

    public async Task HandlePlayerMoveAsync(NetSession session, double x, double y, double z, double dir, CancellationToken cancellationToken = default)
    {
        if (!session.CharacterId.HasValue || session.CharacterId.Value <= 0) return;

        // Mise à jour de la position dans la base de données
        await _characterRepository.UpdateCoordinatesAsync(session.CharacterId.Value, _nodeId, x, y, z, dir);
        EdenLog.Write($"[Zone] [MOUVEMENT] Perso ID={session.CharacterId} déplacé -> ({x:F1}, {y:F1}, {z:F1}, dir={dir:F1})");
    }

    public async Task HandleZoneChatAsync(NetSession session, ChatMessage chat, CancellationToken cancellationToken = default)
    {
        EdenLog.Write($"[Zone] [CHAT {chat.Channel}] {chat.SenderName}: {chat.Message}");
        // Diffusion au client
        await session.SendAsync(chat, cancellationToken);
    }
}
