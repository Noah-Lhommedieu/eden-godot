using System.Collections.Concurrent;
using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Links;
using Eden.Core.Logging;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>Sondes de bissectibilité — 3 variantes de SeeMob envoyées à
    /// ~5u du joueur après MigrationDone :
    ///   P1 = paquet 298 capturé verbatim (handle/pos modifiés)
    ///   P2 = BuildSeeMob NPC à la même position
    ///   P3 = BuildSeeMob monstre à la même position
    /// Le joueur rapporte combien d'entités apparaissent.</summary>
    private async Task SendProbesAsync(NetSession session, EntryState st,
        CancellationToken token)
    {
        // P0 : ClientSay "PROBE-OK" attribué à un NPC — prouve la livraison
        // des paquets post-entrée. Format capturé : [h4][u16 len][str][4B].
        await session.SendAsync(new CNC_CZ_ClientSay
        {
            Field1 = 0x88C9930Du,               // u32 : handle NPC capturé
            Field2 = "PROBE-OK",                // str : texte
            Field3 = 0,                         // u32 : blob vide
        }, token);

        // P1 : verbatim NPC capturé — handle famille native + pos patchées.
        var capturedNpc = EntryReplayData.Map[4]
            .Select(Convert.FromHexString)
            .FirstOrDefault(p => p.Length == 77 &&
                                 BitConverter.ToUInt16(p, 0) == 298);
        if (capturedNpc != null)
        {
            var p1 = (byte[])capturedNpc.Clone();
            BitConverter.GetBytes(0x88C9FF01u).CopyTo(p1, 2);      // handle neuf famille 0x88c9
            BitConverter.GetBytes(st.PosX + 4f).CopyTo(p1, 9);     // x
            BitConverter.GetBytes(st.PosZ + 4f).CopyTo(p1, 13);    // y
            st.HandleToEntity[0x88C9FF01] = 50260;
            await session.SendPacketAsync(298, p1[2..], token);
        }
        // P2 : verbatim capturé, MÊME handle que l'original, pos +6 → teste si
        // le client accepte les réémissions d'un handle connu (l'NPC bouge).
        if (capturedNpc != null)
        {
            var p2 = (byte[])capturedNpc.Clone();
            BitConverter.GetBytes(st.PosX + 6f).CopyTo(p2, 9);
            BitConverter.GetBytes(st.PosZ + 6f).CopyTo(p2, 13);
            await session.SendPacketAsync(298, p2[2..], token);
        }
        // P3 : monstre généré — handle famille native des mobs (0xffffaXXX).
        st.HandleToEntity[0xFFFFA801] = 56001;
        await session.SendAsync(BuildSeeMob(
            0xFFFFA801, 56001, true, st.PosX + 4f, st.PosZ + 12f, 0f, 0f), token);
        EdenLog.Write("[Zone-Entry] probes P1/P2/P3 envoyés");
    }

    /// <summary>Sondes de debug (s298/s298b/s269/s271/sall) — fusionnées
    /// dans le dispatch des commandes texte admin (ZoneEntryFlow.Admin.cs).</summary>
    private async Task HandleProbeCommandAsync(NetSession session,
        EntryState st, string cmd, CancellationToken token)
    {
        switch (cmd)
        {
            case "s298": // un mob généré juste à côté du joueur
                await session.SendAsync(BuildSeeMob(
                    0xFFFA8000, 56001, true,
                    st.PosX + 5, st.PosZ + 5, 0, 0), token);
                break;
            case "s298b": // un NPC généré à côté
                await session.SendAsync(BuildSeeMob(
                    0x88EE0000, 50003, false,
                    st.PosX + 5, st.PosZ + 5, 3, 0), token);
                break;
            case "s269": // eradicate du NPC replay le plus proche
                if (st.Sent.Keys.FirstOrDefault() is { } h && h != 0)
                    await session.SendPacketAsync(269, BitConverter.GetBytes(h), token);
                break;
            case "s271": // ClientDisplayMonster — corps natif 10B
                await session.SendAsync(new CNC_CZ_ClientDisplayMonster
                {
                    MobHandle = 0xFFFA8000u, EnchantIds = [],
                    OwnerGeneration = 0,
                    OwnerHandle = (int)st.CharId,
                }, token);
                break;
            case "sall": // force l'envoi de TOUTES les entités du node
                foreach (var e in _services.Live
                             .EnsureNodePopulated(st.NodeId,
                                 st.InstanceGroup,
                                 _services.Scenes.GetScene($"S{st.NodeId:D3}")))
                {
                    if (e.IsMonster &&
                        _services.Live.GetMob(e.Handle) is { Alive: false })
                        continue;
                    await session.SendAsync(BuildSeeMob(
                        e.Handle, e.EntityId, e.IsMonster,
                        e.X, e.Y, e.Z, e.Facing), token);
                }
                break;
        }
    }
}
