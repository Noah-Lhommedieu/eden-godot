using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Détail PUBLIC d'une ville de guilde (paquet custom Reborn 9064, voir
/// <c>Eden.Zone/Features/TownDetail/TownDetailHandler.cs</c>) : les PNJ réellement posés, que le
/// paquet natif 502 ne transporte pas. C→S <c>[i32 familyId]</c> ; S→C <c>[i32 familyId][u16 n]</c>
/// puis n × <c>[i32 npcTid][s16 loc][u8 role]</c> (1 responsable, 2 service, 3 vendeur d'outils).
/// </summary>
public static class TownDetailUi
{
    public const int Ncid = 9064;

    public enum Role : byte { Housekeeper = 1, Service = 2, ToolVendor = 3 }

    public sealed record Npc(int Tid, int Loc, Role Role);

    public static event Action<int, IReadOnlyList<Npc>>? Received;

    public static void Bind(EdenSession s, Action<Action> post) =>
        s.OnZone(Ncid, r =>
        {
            var family = r.ReadInt(); var n = r.ReadUShort();
            var list = new List<Npc>(n);
            for (var i = 0; i < n; i++)
                list.Add(new Npc(r.ReadInt(), r.ReadShort(), (Role)r.ReadByte()));
            post(() => Received?.Invoke(family, list));
        });

    /// <summary>Demande le détail de la ville <paramref name="familyId"/>.</summary>
    public static void Request(int familyId)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        _ = s.SendZoneAsync(new Query { FamilyId = familyId }, CancellationToken.None);
    }

    private sealed class Query : NetMessage
    {
        public int FamilyId { get; set; }
        public override int Opcode => Ncid;
        public override string Name => "Zone.TownDetail";
        public override void Serialize(BinWriter s) => s.WriteInt(FamilyId);
        public override void Deserialize(BinReader s) => FamilyId = s.ReadInt();
    }
}
