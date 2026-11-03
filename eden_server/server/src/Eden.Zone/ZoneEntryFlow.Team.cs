using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Positions des coéquipiers — NCID 392
/// <c>CNC_CZ_ClientTeamMemberPosition</c>, miroir de
/// <c>CPlayerFactory::NotifyTeamMemberPos</c> @0x80fc7c0
/// (zone/decompiled.c ~143889) : appelé par <c>NotifyTeamData</c>
/// (depuis <c>CCharacter::OnMigrationDone</c> @0x843fba0) et
/// <c>RecvWorldTeamJoinTeam</c> @0x81135e0. Pour chaque membre de la
/// CForceTeam autre que soi et migré (type 7), un paquet est envoyé
/// AU destinataire (NotifyClient, pas de broadcast) :
/// <c>[i32 charId][s16 nodeId][f32 x][f32 z]</c> — l'id de node est
/// <c>*(*(char+0x1c)+0x28)+4</c>, la position char+0x28/+0x2c.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Pousse les positions de tous les coéquipiers migrés
    /// de <paramref name="st"/> à sa propre session — un 392 par
    /// membre (le natif saute soi-même via char+0x288 et les membres
    /// non migrés via le type d'objet).</summary>
    private async Task SendTeamMemberPositionsAsync(NetSession session,
        EntryState st, CancellationToken token)
    {
        var team = _services.Teams.GetTeamOf((int)st.CharId);
        if (team == null) return;
        foreach (var m in team.Members)
        {
            if (m.CharacterId == st.CharId) continue;   // char+0x288 = soi
            var ms = _services.Sessions.FindByCharacter(m.CharacterId);
            if (ms == null) continue;
            // Membre migré natif (objet type 7) — un char en cours
            // d'entrée n'a pas de position publiable.
            if (!_states.TryGetValue(ms.SessionId, out var ost) ||
                !ost.Migrated) continue;
            await session.SendAsync(new CNC_CZ_ClientTeamMemberPosition
            {
                Field1 = (int)ost.CharId,        // i32 — char+0x34
                Field2 = (short)ost.NodeId,      // s16 — node du membre
                Field3 = ost.PosX,               // f32 — char+0x28
                Field4 = ost.PosZ,               // f32 — char+0x2c
            }, token);
        }
    }
}
