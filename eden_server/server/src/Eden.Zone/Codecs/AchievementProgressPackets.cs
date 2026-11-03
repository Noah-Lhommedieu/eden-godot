using Eden.Core.Networking;

namespace Eden.Zone.Codecs;

/// <summary>Extension Eden du 480 <c>ClientAchievementUpdate</c> :
/// kind 6 = compteurs des succès NON complétés (<c>success_num</c> en
/// base, ex. « 12/20 »). Le Serialize natif (@0x08209080) ne connaît
/// que les kinds 0-5 — le natif n'envoie jamais ces compteurs, la
/// donnée existe pourtant (player_characters_achievement.success_num) ;
/// la classe générée n'étant pas éditable, le corps est écrit ici, au
/// même gabarit que le kind 0/1 : <c>[u8 6][u16 n][i32 id][u32
/// successNum]×n</c>.</summary>
public static class AchievementProgressPackets
{
    public const byte Kind = 6;
    /// <summary>Même taille de paquet que SendAllAchievement (315).</summary>
    public const int ChunkSize = 315;

    public static byte[] Build(IEnumerable<(int Id, uint SuccessNum)> entries)
    {
        var list = entries.ToList();
        var s = new OutBinStream();
        s.WriteByte(Kind);
        s.WriteShort((short)list.Count);
        foreach (var (id, n) in list)
        {
            s.WriteInt(id);
            s.WriteInt((int)n);
        }
        return s.ToArray();
    }
}
