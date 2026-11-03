using System.Text;
using EdenClient.Data;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du format baké ITB1 (scripts/data/TableBin.cs) : round-trip
/// pipe/comma/Count, CRLF et '|' embarqués, Big5→UTF-8, FindById,
/// corruption et identité Write→Read.
/// </summary>
public class TableBinTests
{
    private static TableBin.Baked Bake(IniTable t)
    {
        using var ms = new MemoryStream();
        TableBin.Write(ms, t);
        return TableBin.Read(ms.ToArray());
    }

    private static byte[] BakeBytes(IniTable t)
    {
        using var ms = new MemoryStream();
        TableBin.Write(ms, t);
        return ms.ToArray();
    }

    private static void AssertSame(IniTable t, TableBin.Baked b)
    {
        Assert.Equal(t.Version, b.Version);
        Assert.Equal(t.ColumnCount, b.ColCount);
        Assert.Equal(t.Separator, b.Sep);
        Assert.Equal(t.Rows.Count, b.Rows.Count);
        for (var i = 0; i < t.Rows.Count; i++)
            Assert.Equal(t.Rows[i], b.Rows[i]);
    }

    [Fact]
    public void RoundTrip_Pipe()
    {
        var t = IniTable.Parse(EdenEncoding.Big5.GetBytes(
            "|V.2|4|\r\n1|戰士|J009|desc|\r\n2|法師|J033|d2|\r\n"),
            EdenEncoding.Big5);
        var b = Bake(t);
        AssertSame(t, b);
        Assert.Equal("J033", b.FindById("2")![2]);
        Assert.Equal("J009", b.FindById(1)![2]);
        Assert.Null(b.FindById("999"));
    }

    [Fact]
    public void RoundTrip_Comma()
    {
        var t = IniTable.Parse(EdenEncoding.Big5.GetBytes(
            "V.2,5,\r\n0,初心者,1,60,,\r\n1,神武門,1,60,,\r\n"),
            EdenEncoding.Big5);
        var b = Bake(t);
        AssertSame(t, b);
        Assert.Equal(',', b.Sep);
        Assert.Equal("神武門", b.FindById("1")![1]);
    }

    [Fact]
    public void RoundTrip_Count_EmbeddedCrLfAndPipe()
    {
        // Cas t_npc.ini : « <Father Christmas>\nSanta » + un '|' dans le
        // texte (absorbé par NormalizeCountRow dans le dernier champ).
        var t = IniTable.Parse(Encoding.ASCII.GetBytes(
            "3|\r\n1|3|21|\r\n" +
            "51973|<Father Christmas>\r\nSanta|2014 Christmas Event|\r\n" +
            "7|npc|text with | inside|\r\n"),
            EdenEncoding.Big5);
        Assert.Equal("<Father Christmas>\r\nSanta", t.Rows[0][1]);
        Assert.Equal(" inside", t.Rows[1][3]);
        var b = Bake(t);
        AssertSame(t, b);
        Assert.Equal("2014 Christmas Event", b.FindById("51973")![2]);
        Assert.Equal(" inside", b.FindById("7")![3]);
    }

    [Fact]
    public void RoundTrip_EmptyCells()
    {
        // cols=4 → 5 cellules/row (la virgule finale laisse un champ vide)
        var t = IniTable.Parse(Encoding.ASCII.GetBytes(
            "V.1,4,\r\n1,,,,\r\n2,a,,,\r\n"), EdenEncoding.Big5);
        Assert.Equal(new[] { "1", "", "", "", "" }, t.Rows[0]);
        var b = Bake(t);
        AssertSame(t, b);
        Assert.Equal("", b.FindById("1")![4]);
    }

    [Fact]
    public void RoundTrip_Big5ToUtf8()
    {
        // Le .ini source est encodé Big5 ; le .inib stocke de l'UTF-8 —
        // le runtime reçoit les chaînes CJK déjà décodées.
        var t = IniTable.Parse(EdenEncoding.Big5.GetBytes(
            "|V.1|3|\r\n1|戰士法師初心者|x|\r\n"), EdenEncoding.Big5);
        var bytes = BakeBytes(t);
        var b = TableBin.Read(bytes);
        AssertSame(t, b);
        Assert.Equal("戰士法師初心者", b.FindById("1")![1]);
        // la table est bien stockée UTF-8 : les bytes Big5 de la clé
        // n'apparaissent plus, la séquence UTF-8 oui
        var big5Key = EdenEncoding.Big5.GetBytes("戰士法師初心者");
        var utf8Key = Encoding.UTF8.GetBytes("戰士法師初心者");
        Assert.Equal(-1, IndexOf(bytes, big5Key));
        Assert.True(IndexOf(bytes, utf8Key) >= 0);
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= hay.Length; i++)
            if (hay.AsSpan(i, needle.Length).SequenceEqual(needle))
                return i;
        return -1;
    }

    [Fact]
    public void FindById_DuplicateIds_ReturnsFirstIniRow()
    {
        var t = IniTable.Parse(Encoding.ASCII.GetBytes(
            "V.1,2,\r\n1,a,\r\n1,b,\r\n2,c,\r\n"), EdenEncoding.Big5);
        var b = Bake(t);
        Assert.Equal("a", b.FindById("1")![1]); // même ordre que FindById ini
        Assert.Equal("a", t.FindById(1)![1]);
    }

    [Fact]
    public void Read_Truncated_Throws()
    {
        var bytes = BakeBytes(IniTable.Parse(Encoding.ASCII.GetBytes(
            "|V.2|2|\r\n1|a|\r\n2|b|\r\n"), EdenEncoding.Big5));
        Assert.Throws<InvalidDataException>(
            () => TableBin.Read(bytes[..(bytes.Length / 2)]));
        Assert.Throws<InvalidDataException>(
            () => TableBin.Read(Array.Empty<byte>()));
    }

    [Fact]
    public void Read_BadMagic_Throws()
    {
        var bytes = BakeBytes(IniTable.Parse(Encoding.ASCII.GetBytes(
            "V.1,2,\r\n1,a,\r\n"), EdenEncoding.Big5));
        bytes[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => TableBin.Read(bytes));
    }

    [Fact]
    public void RoundTrip_Identity_ManyRows()
    {
        // ids non triés + trous — l'index annexe préserve l'ordre ini.
        var sb = new StringBuilder("|V.1|3|\r\n");
        var rng = new Random(42);
        var ids = Enumerable.Range(0, 500).Select(_ => rng.Next(0, 90000))
            .ToList();
        foreach (var id in ids)
            sb.Append(id).Append("|n").Append(id).Append("|v|\r\n");
        var t = IniTable.Parse(Encoding.ASCII.GetBytes(sb.ToString()),
            EdenEncoding.Big5);
        var b = Bake(t);
        AssertSame(t, b);
        foreach (var id in ids.Take(50))
            Assert.Equal("n" + id, b.FindById(id.ToString())![1]);
    }

    [Fact]
    public void RoundTrip_UnrecognizedHeader_ZeroRows()
    {
        // Format hors spec (boundinfo.ini : CSV sans header) — IniTable
        // rend 0 rows et Version = première ligne ; le bake préserve ça.
        var t = IniTable.Parse(Encoding.ASCII.GetBytes(
            "m010010,m0101.nif,0.000000,\r\n"), EdenEncoding.Big5);
        Assert.Empty(t.Rows);
        var b = Bake(t);
        AssertSame(t, b);
        Assert.Null(b.FindById("m010010"));
    }
}
