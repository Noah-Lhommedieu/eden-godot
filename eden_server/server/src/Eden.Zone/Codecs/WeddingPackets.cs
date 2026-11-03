using Eden.Core.Data;

namespace Eden.Zone.Codecs;

/// <summary>
/// Cérémonies de mariage — codecs du relais Mission→Zone→Client.
///
/// Preuves (relu byte-par-byte) :
/// - `CNC_ZMis_ZoneServerWeddingsInfo::Serialize` mission @0x80db220 :
///   `[i32 requester][u8 n]` puis n×
///   `[i32 loveId][str name1][str name2][u8 status][i32 +0x14][i32 +0x18]`
///   (+0x14 = now à l'émission, +0x18 = stepEnd - now).
/// - `CNE_ZMis_ZoneServerWeddingsInfo::Execute` zone @0x833ff00 :
///   pour chaque record `leftTime = +0x18 - (now - +0x14)` puis émet
///   `CNC_CZ_ClientWeddingsInfo` (599) sur la session du requester.
/// - `CNC_CZ_ClientWeddingsInfo::Serialize` zone (WedInfo stride 5) :
///   `[u8 n][n×{i32 loveId, str name1, str name2, u8 status,
///   i32 leftTime}]`.
/// </summary>
public static class WeddingPackets
{
    /// <summary>Un record mariage tel que désérialisé par le CNE
    /// natif (vecteur 6 champs à `this+0x1c`).</summary>
    public readonly record struct ZWedding(
        int LoveId, string Name1, string Name2,
        byte Status, int StartTime, int Remaining);

    /// <summary>Parse le corps ZMis 193 (sans le ncid) : saute le
    /// requester i32 puis lit les records. Retourne null si tronqué.</summary>
    public static List<ZWedding>? ParseWeddingsInfo(byte[] body)
    {
        if (body.Length < 5) return null;
        var n = body[4];
        var list = new List<ZWedding>(n);
        var o = 5;
        for (var i = 0; i < n; i++)
        {
            if (o + 5 > body.Length) return null;
            var loveId = BitConverter.ToInt32(body, o); o += 4;
            var l1 = BitConverter.ToUInt16(body, o); o += 2;
            if (o + l1 + 2 > body.Length) return null;
            var name1 = EdenEncoding.Big5.GetString(body, o, l1); o += l1;
            var l2 = BitConverter.ToUInt16(body, o); o += 2;
            if (o + l2 + 9 > body.Length) return null;
            var name2 = EdenEncoding.Big5.GetString(body, o, l2); o += l2;
            var status = body[o]; o += 1;
            var start = BitConverter.ToInt32(body, o); o += 4;
            var remaining = BitConverter.ToInt32(body, o); o += 4;
            list.Add(new ZWedding(loveId, name1, name2,
                status, start, remaining));
        }
        return list;
    }

    /// <summary>Corps du 599 `CNC_CZ_ClientWeddingsInfo` — miroir de
    /// la boucle `Execute` native : `leftTime = rec.remaining -
    /// (nowEpoch - rec.startTime)` (temps restant de l'étape).</summary>
    public static byte[] BuildClientWeddingsInfo(
        IReadOnlyList<ZWedding> recs, int nowEpoch)
    {
        var b = new List<byte>();
        b.Add((byte)recs.Count);                            // u8 : n
        foreach (var r in recs)
        {
            b.AddRange(BitConverter.GetBytes(r.LoveId));    // i32 : love_id
            var n1 = EdenEncoding.Big5.GetBytes(r.Name1);
            var n2 = EdenEncoding.Big5.GetBytes(r.Name2);
            b.AddRange(BitConverter.GetBytes((ushort)n1.Length));
            b.AddRange(n1);                                 // str : nom 1
            b.AddRange(BitConverter.GetBytes((ushort)n2.Length));
            b.AddRange(n2);                                 // str : nom 2
            b.Add(r.Status);                                // u8 : status
            b.AddRange(BitConverter.GetBytes(
                r.Remaining - (nowEpoch - r.StartTime)));   // i32 : leftTime
        }
        return b.ToArray();
    }
}
