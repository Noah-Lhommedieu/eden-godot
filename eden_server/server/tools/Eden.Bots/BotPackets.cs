using System.Buffers.Binary;

namespace Eden.Bots;

/// <summary>Builders des rares paquets client→zone utilisés par le
/// banc. Les layouts reprennent les handlers serveur prouvés :
/// MoveSelf (NCID1), MeleeAttack (5), SpellCastToLife (9), TreasurePickup
/// (35), Echo (57).</summary>
public static class BotPackets
{
    public static byte[] MoveSelf(float curX, float curZ,
        float targetX, float targetZ, float facing) =>
        WithNcid(1, b =>
        {
            BinaryPrimitives.WriteInt16LittleEndian(b, 0);          // n waypoints
            BinaryPrimitives.WriteSingleLittleEndian(b[2..], curX);
            BinaryPrimitives.WriteSingleLittleEndian(b[6..], curZ);
            BinaryPrimitives.WriteSingleLittleEndian(b[10..], targetX);
            BinaryPrimitives.WriteSingleLittleEndian(b[14..], targetZ);
            BinaryPrimitives.WriteSingleLittleEndian(b[18..], facing);
            BinaryPrimitives.WriteInt16LittleEndian(b[22..], 0);    // flag
        }, 24);

    public static byte[] MeleeAttack(uint mobHandle, byte type = 0) =>
        WithNcid(5, b =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(b, mobHandle);
            b[4] = type;
        }, 5);

    public static byte[] SpellCastToLife(ushort spellId, uint mobHandle) =>
        WithNcid(9, b =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(b, spellId);
            BinaryPrimitives.WriteUInt32LittleEndian(b[2..], mobHandle);
        }, 6);

    public static byte[] TreasurePickup(uint treasureId) =>
        WithNcid(35, b => BinaryPrimitives.WriteUInt32LittleEndian(b,
            treasureId), 4);

    public static byte[] Echo() => WithNcid(57, _ => { }, 0);

    /// <summary>TeamInviteAnswer (30) `[s16 type][i32 inviterId]
    /// [u8 accepted][s16]` — wire natif prouvé (+0x12=1 accept,
    /// +0x14 inviteur, +0x18 bool) — codec client
    /// ZoneTeamInviteAnswer.</summary>
    public static byte[] TeamInviteAnswer(int inviterId,
        bool accept = true) =>
        WithNcid(30, b =>
        {
            BinaryPrimitives.WriteInt16LittleEndian(b,
                (short)(accept ? 1 : 0));
            BinaryPrimitives.WriteInt32LittleEndian(b[2..], inviterId);
            b[6] = (byte)(accept ? 1 : 0);
            BinaryPrimitives.WriteInt16LittleEndian(b[7..], 0);
        }, 9);

    /// <summary>NCID0 zone : ticket 22B retourné par TicketServer.</summary>
    public static byte[] ZoneTicket(byte[] ticket22) =>
        WithNcid(0, b => ticket22.CopyTo(b), 22);

    private static byte[] WithNcid(ushort ncid,
        Action<Span<byte>> writeBody, int bodyLen)
    {
        var p = new byte[2 + bodyLen];
        BinaryPrimitives.WriteUInt16LittleEndian(p, ncid);
        writeBody(p.AsSpan(2));
        return p;
    }
}
