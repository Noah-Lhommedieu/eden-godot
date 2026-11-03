using Eden.Core.Data;
using Eden.Core.Game.Services;

namespace Eden.Core.Game.Social;

/// <summary>
/// Codec des classements — preuves :
///   émission mission `CNC_ZMis_ZoneServerRankInfo::Serialize`
///   @0x080b78b0 (NCID 763) :
///     [vec&lt;str&gt; names][9× vec&lt;i32&gt;][s16 type][s16 ver]
///     [vec&lt;str&gt; str2 (+0x28 record)][vec&lt;vec&lt;str&gt;&gt;
///     lists (+0x2c record)]
///   lecture zone `CNE_ZMis_ZoneServerRankInfo::Deserialize`
///   @0x08331330 — même ordre ;
///   réponse client `CNC_CZ_ClientRankInfo::Serialize` @0x080b9b40
///   (NCID 521) : [vec&lt;str&gt; names][8× vec&lt;i32&gt;] où les
///   colonnes émises sont record +4/+8/+0xc/+0x10 puis 3 vecteurs
///   vides puis +0x24 (ordre des args du ctor dans
///   `CGlobalInfo::UpdateZoneRankInfo` @0x08085db3).
/// Compteurs u16, strings [u16 len][Big5].
/// </summary>
public static class RankPackets
{
    /// <summary>Record `SRankInfoData` (0x38 natif) aplati.</summary>
    public sealed class RankEntry
    {
        /// <summary>+0x00 — nom (str).</summary>
        public string Name = "";
        /// <summary>+0x04..+0x24 — 9 champs i32.</summary>
        public int[] F = new int[9];
        /// <summary>+0x28 — 2ᵉ chaîne (ex. chef de famille).</summary>
        public string Str2 = "";
        /// <summary>+0x2c — liste de chaînes (ex. membres).</summary>
        public List<string> StrList = new();
    }

    /// <summary>Indices des 8 colonnes i32 émises dans le 521 client —
    /// +4/+8/+0xc/+0x10, 3 vides, +0x24 (−1 = vecteur vide).</summary>
    private static readonly int[] ClientColMap =
        { 0, 1, 2, 3, -1, -1, -1, 8 };

    /// <summary>Trame 763 mission→zone (un type de classement).</summary>
    public static byte[] Build763(short type, short ver,
        IReadOnlyList<RankEntry> rows)
    {
        var body = new List<byte>();
        PutVecStr(body, rows.Select(r => r.Name));
        for (var i = 0; i < 9; i++)
        {
            body.AddRange(BitConverter.GetBytes(
                (ushort)rows.Count));
            foreach (var r in rows)
                body.AddRange(BitConverter.GetBytes(r.F[i]));
        }
        body.AddRange(BitConverter.GetBytes(type));   // s16 type
        body.AddRange(BitConverter.GetBytes(ver));    // s16 version
        PutVecStr(body, rows.Select(r => r.Str2));
        body.AddRange(BitConverter.GetBytes((ushort)rows.Count));
        foreach (var r in rows) PutVecStr(body, r.StrList);
        return body.ToArray();
    }

    /// <summary>Lecture zone du 763 — retourne null sur trame
    /// tronquée. Ordre fil : [vec&lt;str&gt;][9× vec&lt;i32&gt;]
    /// [s16 type][s16 ver][vec&lt;str&gt;][vec&lt;vec&lt;str&gt;&gt;].</summary>
    public static RankInfoCache.RankSet? Parse763(byte[] body,
        out short type)
    {
        type = 0;
        var p = 0;
        var names = ReadVecStr(body, ref p);
        var cols = new List<int>[9];
        for (var i = 0; i < 9; i++)
            cols[i] = ReadVecI32(body, ref p);
        type = ReadS16(body, ref p);
        var ver = ReadS16(body, ref p);
        var str2s = ReadVecStr(body, ref p);
        var nLists = ReadU16(body, ref p);
        var lists = new List<List<string>>(nLists);
        for (var i = 0; i < nLists; i++)
            lists.Add(ReadVecStr(body, ref p));
        if (p > body.Length) return null;
        return new RankInfoCache.RankSet
        {
            Names = names, Cols = cols, Version = ver,
            Str2s = str2s, StrLists = lists,
        };
    }

    /// <summary>Trame 521 zone→client depuis le cache.</summary>
    public static byte[] Build521(RankInfoCache.RankSet? set)
    {
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(
            (ushort)(set?.Names.Count ?? 0)));
        if (set != null)
            foreach (var n in set.Names)
            {
                var b = EdenEncoding.Big5.GetBytes(n);
                body.AddRange(BitConverter.GetBytes((ushort)b.Length));
                body.AddRange(b);
            }
        foreach (var idx in ClientColMap)
        {
            var col = idx >= 0 && set != null
                ? set.Cols[idx] : new List<int>();
            body.AddRange(BitConverter.GetBytes((ushort)col.Count));
            foreach (var v in col)
                body.AddRange(BitConverter.GetBytes(v));
        }
        return body.ToArray();
    }

    /// <summary>`[u16 n][n×(u16 len + Big5)]`.</summary>
    private static void PutVecStr(List<byte> body,
        IEnumerable<string> items)
    {
        var list = items.ToList();
        body.AddRange(BitConverter.GetBytes((ushort)list.Count));
        foreach (var s in list)
        {
            var b = EdenEncoding.Big5.GetBytes(s ?? "");
            body.AddRange(BitConverter.GetBytes((ushort)b.Length));
            body.AddRange(b);
        }
    }

    private static List<string> ReadVecStr(byte[] b, ref int p)
    {
        var n = ReadU16(b, ref p);
        var v = new List<string>(n);
        for (var i = 0; i < n; i++)
        {
            var len = ReadU16(b, ref p);
            if (p + len > b.Length) { p = b.Length + 1; return v; }
            v.Add(EdenEncoding.Big5.GetString(b, p, len));
            p += len;
        }
        return v;
    }

    private static List<int> ReadVecI32(byte[] b, ref int p)
    {
        var n = ReadU16(b, ref p);
        var v = new List<int>(n);
        for (var i = 0; i < n; i++)
        {
            if (p + 4 > b.Length) { p = b.Length + 1; return v; }
            v.Add(BitConverter.ToInt32(b, p));
            p += 4;
        }
        return v;
    }

    private static ushort ReadU16(byte[] b, ref int p)
    {
        if (p + 2 > b.Length) { p = b.Length + 1; return 0; }
        var v = BitConverter.ToUInt16(b, p); p += 2; return v;
    }

    private static short ReadS16(byte[] b, ref int p)
        => unchecked((short)ReadU16(b, ref p));
}
