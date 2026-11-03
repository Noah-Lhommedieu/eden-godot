namespace Eden.Zone.Codecs;

/// <summary>
/// Codec NCID 220 <c>CNE_CZ_ZoneServerRecruitInstance</c> — format
/// variable que la classe générée a aplati (3×s16+u8). Preuve :
/// désasm <c>Deserialize</c> zone @0x0827c210 — lit un s16 count,
/// count×s16 node ids dans un vector, puis s16 (rôle) et u8 (flag :
/// 0 = inscription, 1 = retrait — dispatch de <c>Execute</c>
/// @0x0827c2f0 vers RecruitInstanceAdd / LeaveRecruitInstance).
/// </summary>
public static class RecruitPackets
{
    /// <summary>Requête client décodée : nœuds demandés + rôle +
    /// flag.</summary>
    public readonly record struct Request(short[] Nodes, short Role,
        byte Flag);

    /// <summary>Parse le payload brut du 220. Retourne false si la
    /// trame est tronquée (count×s16 + s16 + u8).</summary>
    public static bool TryParse(byte[] payload, out Request req)
    {
        req = default;
        if (payload.Length < 2) return false;
        var count = BitConverter.ToInt16(payload, 0);       // s16 : nb de nodeIds
        if (count < 0 || payload.Length != 2 + count * 2 + 3)
            return false;
        var nodes = new short[count];
        for (var i = 0; i < count; i++)
            nodes[i] = BitConverter.ToInt16(payload, 2 + i * 2); // s16 : nodeId
        var role = BitConverter.ToInt16(payload, 2 + count * 2); // s16 : rôle
        var flag = payload[4 + count * 2];                       // u8  : 0 add / 1 leave
        req = new Request(nodes, role, flag);
        return true;
    }
}
