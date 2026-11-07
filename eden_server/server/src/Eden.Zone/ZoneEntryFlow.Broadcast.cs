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

    /// <summary>Distance au-delà de laquelle un témoin est « loin » : sous charge, il reçoit moins de mises à jour de déplacement.</summary>
    private const float FarWitnessDistance = 24f;

    /// <summary>File d'envoi à partir de laquelle un témoin LOIN est dégradé (un déplacement sur deux perdu, rattrapé au suivant).</summary>
    private const double FarWitnessPressure = 0.25;

    /// <summary>
    /// Diffusion cosmétique d'un déplacement (PLAN-ACTION-3 lot N) : non bloquante, jamais d'attente d'un témoin lent. Sous charge
    /// les témoins éloignés sont servis en priorité basse ; au-delà de <see cref="NetSession.CosmeticBacklogLimit"/> tout le monde l'est.
    /// </summary>
    private static void SendCosmeticTo(NetSession s, ushort opcode, byte[] payload, float distance)
    {
        if (distance > FarWitnessDistance && s.QueuePressure >= FarWitnessPressure)
        {
            Eden.Core.Logging.EdenMetrics.CosmeticDrops.Add(1, new KeyValuePair<string, object?>("reason", "far"));
            return;
        }
        var op = opcode;
        if (op >= s.OutboundNcidShiftAbove && op <= 83) op++;
        s.TrySendCosmeticPacket(op, payload);
    }
}
