namespace Eden.Zone.Features.Gather;

/// <summary>
/// Paquet custom Reborn 9063 S→C « état de récolte » (hors protocole natif) :
/// <c>[u8 state][u8 kind][u16 tickSeconds][i32 toolItemId][i32 itemId][u16 qty][u8 reason]</c>
/// — <c>state</c> 1 = début, 2 = tick (ressource obtenue), 0 = arrêt (reason =
/// <c>GatherStopReason</c>), 3 = notice au joueur (reason = <c>GatherRules.NoticeIdFor</c>). Le client affiche le statut « En cours de minage… »,
/// joue l'animation de récolte et écrit la ressource obtenue.
/// </summary>
public static class GatherPackets
{
    public const ushort Ncid = 9063;

    public enum State : byte { Stop = 0, Start = 1, Tick = 2, Notice = 3 }

    public static byte[] Build(State state, int kind, int tickSeconds, int toolItemId,
        int itemId = 0, int qty = 0, int stopReason = 0)
    {
        using var ms = new MemoryStream(15);
        using var w = new BinaryWriter(ms);
        w.Write((byte)state);
        w.Write((byte)kind);
        w.Write((ushort)Math.Clamp(tickSeconds, 0, ushort.MaxValue));
        w.Write(toolItemId);
        w.Write(itemId);
        w.Write((ushort)Math.Clamp(qty, 0, ushort.MaxValue));
        w.Write((byte)stopReason);
        return ms.ToArray();
    }
}
