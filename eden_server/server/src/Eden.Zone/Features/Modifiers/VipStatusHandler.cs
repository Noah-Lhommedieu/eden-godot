using Eden.Core.Game.Foundation;
using Eden.Core.Networking;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Session;
using Eden.Zone.Handlers;

namespace Eden.Zone.Features.Modifiers;

/// <summary>Opcode 9065 C→S (custom Reborn, sans corps) : « suis-je VIP ? ».</summary>
public sealed class CNE_CZ_ZoneServerVipStatus : NetCommand
{
    public override int Opcode => 9065;
    public override string Name => "CNE_CZ_ZoneServerVipStatus";
    public override void Serialize(OutBinStream stream) { }
    public override void Deserialize(InBinStream stream) { }
}

/// <summary>
/// Statut VIP affiché au joueur (badge « VIP » du HUD). S→C 9065 : <c>[u8 vip][i64 finUnix]</c>
/// (finUnix 0 = permanent ou non VIP). Cosmétique : les bonus viennent du moteur de modificateurs.
/// </summary>
public static class VipStatusHandler
{
    public const ushort ResponseNcid = 9065;

    public static void Register(PacketDispatcher d, ZoneServices s) =>
        d.RegisterChar(() => new CNE_CZ_ZoneServerVipStatus(),
            async (session, msg, charId, token) =>
            {
                var (vip, until) = VipDirectory.Repo == null ? (false, null)
                    : await VipDirectory.Repo.StatusOfCharAsync(charId, GameTime.Clock.Now);
                await session.SendPacketAsync(ResponseNcid, Build(vip, until), token);
            });

    /// <summary>Corps de la réponse (pur, testé).</summary>
    public static byte[] Build(bool vip, DateTimeOffset? until)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)(vip ? 1 : 0));
        w.Write(vip && until != null ? until.Value.ToUnixTimeSeconds() : 0L);
        return ms.ToArray();
    }
}
