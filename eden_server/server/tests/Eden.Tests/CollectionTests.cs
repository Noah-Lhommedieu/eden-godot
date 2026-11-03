using Eden.Core.Data.Parsers;
using Eden.Core.Networking.Messages.Generated;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régressions collection — parsers <c>S_Collection.ini</c> /
/// <c>S_CollectionPoint.ini</c> (<c>CCollectionQuery::Deserialize</c>
/// natif) et wire 472 <c>CNC_CZ_ClientUpdateCollection</c> :
/// <c>[s16 type][i32 points][u16 n][n×s16 item_ids]</c>.
/// </summary>
public class CollectionTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(),
            Guid.NewGuid() + ".ini");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ParseCollection_Columns()
    {
        // [id][name][flag][s16][s16][text][locate][points][group]
        var path = WriteTemp(
            "|V.4|9|\n" +
            "30297|carte A|0|5|1|txt|0|3|7|\n" +
            "30298|carte B|1|5|2|txt|8|1|7|\n");
        var map = CollectionParser.ParseCollection(path);
        Assert.Equal(2, map.Count);
        var a = map[30297];
        Assert.False(a.Special);
        Assert.Equal(0, a.Locate);
        Assert.Equal(3, a.Points);
        Assert.Equal(7, a.Group);
        var b = map[30298];
        Assert.True(b.Special);
        Assert.Equal(8, b.Locate);
        Assert.Equal(1, b.Points);
    }

    [Fact]
    public void ParseCollectionPoint_CondPairs()
    {
        // [id][?][?][enchant][5×(key,threshold)] — clés 0 ignorées.
        var path = WriteTemp(
            "|V.4|14|\n" +
            "1|0|1|11285|1000|200|7|50|||||||||\n");
        var list = CollectionParser.ParseCollectionPoint(path);
        Assert.Single(list);
        Assert.Equal(11285, list[0].Enchant);
        Assert.Equal(2, list[0].Conds.Count);
        Assert.Equal((1000, 200), list[0].Conds[0]);
        Assert.Equal((7, 50), list[0].Conds[1]);
    }

    [Fact]
    public void Update472_WireLayout()
    {
        // [s16 type][i32 points][u16 n][n×s16] — types natifs :
        // 0 liste complète, 1 ajout, 2 retrait.
        var msg = new CNC_CZ_ClientUpdateCollection
        {
            BookId = 2,
            U1 = 41,
        };
        msg.Entries.Add(new CNC_CZ_ClientUpdateCollection.Entry
        { CardId = 30297 });
        msg.Entries.Add(new CNC_CZ_ClientUpdateCollection.Entry
        { CardId = -1 });

        var stream = new Eden.Core.Networking.OutBinStream();
        msg.Serialize(stream);
        var body = stream.ToArray();

        Assert.Equal(2 + 4 + 2 + 4, body.Length);
        Assert.Equal(2, BitConverter.ToInt16(body, 0));     // type
        Assert.Equal(41, BitConverter.ToInt32(body, 2));    // points
        Assert.Equal(2, BitConverter.ToUInt16(body, 6));    // n
        Assert.Equal(30297, BitConverter.ToInt16(body, 8));
        Assert.Equal(-1, BitConverter.ToInt16(body, 10));
    }
}
