using Eden.Core.Networking;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>Sérialise une <see cref="NetCommand"/> UNE fois pour
    /// une diffusion à N sessions — avant, chaque
    /// <see cref="NetSession.SendAsync"/> refaisait
    /// <c>OutBinStream</c> + copie par destinataire (N sérialisations
    /// par broadcast). Retourne l'opcode et le payload prêts pour
    /// <see cref="NetSession.SendPacketAsync"/>. Le décalage d'opcode
    /// par build client (<see cref="NetSession.OutboundNcidShiftAbove"/>)
    /// reste appliqué par destinataire à l'envoi.</summary>
    private static (ushort Opcode, byte[] Payload) PreparePacket(
        NetCommand command)
    {
        var stream = new OutBinStream();
        command.Serialize(stream);
        return ((ushort)command.Opcode, stream.ToArray());
    }

    /// <summary>Enfile la trame préparée vers <paramref name="s"/> en
    /// appliquant le décalage d'opcode éventuel de SON build client
    /// (rare — la plupart des sessions restent à
    /// <c>OutboundNcidShiftAbove = int.MaxValue</c>). Une session morte
    /// avale l'erreur : le broadcast ne doit jamais échouer pour un
    /// seul destinataire.</summary>
    private static async Task SendPreparedToAsync(NetSession s,
        ushort opcode, byte[] payload, CancellationToken token)
    {
        var op = opcode;
        if (op >= s.OutboundNcidShiftAbove && op <= 83) op++;
        try { await s.SendPacketAsync(op, payload, token); }
        catch { /* session morte */ }
    }
}
