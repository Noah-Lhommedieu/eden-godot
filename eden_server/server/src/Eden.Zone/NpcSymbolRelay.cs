namespace Eden.Zone;

/// <summary>Conversion du ZMis 65 (mission→zone) en client 345.
/// Wire mission→zone (natif <c>CNC_ZMis_ZoneServerUpdateNPCSymbols::
/// Deserialize</c> mission @0x081192e0) : [i32 charUid][s16 node][u16 n]
/// [n×(i32 npcUid, s16 sym)]. Wire client 345 : [s16 n][n×(i32 npcUid,
/// s16 sym1, s16 sym2)] — second s16 = état zone local (0).</summary>
public static class NpcSymbolRelay
{
    /// <summary>Corps 345 pour <paramref name="body"/> (65), filtré sur
    /// les PNJ de la scène du joueur (<paramref name="sceneNpcIds"/> ;
    /// null = pas de filtre). Null si le corps est trop court.</summary>
    public static byte[]? ToClient345(byte[] body,
        IReadOnlySet<int>? sceneNpcIds)
    {
        if (body.Length < 8) return null;
        var cnt = BitConverter.ToUInt16(body, 6);          // u16 : n
        var syms = new List<(int Npc, short Sym)>();
        for (var i = 0; i < cnt; i++)
        {
            var at = 8 + i * 6;
            if (at + 6 > body.Length) break;
            var npc = BitConverter.ToInt32(body, at);      // i32 : npcUid
            if (sceneNpcIds != null && !sceneNpcIds.Contains(npc))
                continue;
            syms.Add((npc, BitConverter.ToInt16(body, at + 4)));
        }
        var o = new List<byte>();
        o.AddRange(BitConverter.GetBytes((short)syms.Count)); // s16 : n
        foreach (var (npc, sym) in syms)
        {
            o.AddRange(BitConverter.GetBytes(npc));           // i32 : npcUid
            o.AddRange(BitConverter.GetBytes(sym));           // s16 : sym1
            o.AddRange(BitConverter.GetBytes((short)0));      // s16 : sym2
        }
        return o.ToArray();
    }
}
