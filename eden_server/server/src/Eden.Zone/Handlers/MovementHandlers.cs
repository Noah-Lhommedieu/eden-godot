using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Logging;

namespace Eden.Zone.Handlers;

/// <summary>
/// Mouvement : déplacement du personnage + broadcast aux autres joueurs.
/// </summary>
public static class MovementHandlers
{
    public static void Register(PacketDispatcher d, ZoneServices s)
    {
        // Layout : short count + count×(path,aux) + 5 floats + short
        d.Register(() => new CNE_CZ_ZoneServerMoveSelf(),
            async (session, msg, token) =>
            {
                EdenLog.Write($"[Zone] MoveSelf charId={session.CharacterId} " +
                    $"count={msg.Count} f=({msg.F1},{msg.F2},{msg.F3},{msg.F4},{msg.F5}) " +
                    $"type={msg.MoveType}");
                await s.LegacyHandlers.HandlePlayerMoveAsync(session,
                    msg.F1, msg.F2, msg.F3, msg.F4, token);
                // Broadcast du mouvement aux autres joueurs de la zone
                await s.Sessions.BroadcastExceptAsync(session, new CNC_CZ_ClientMoveChar
                {
                    Field1 = msg.F1, Field2 = msg.F2
                }, token);
            });
    }
}
