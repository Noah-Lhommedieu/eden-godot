using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests des messages du métier racial (LifeSkill, lot B5) :
/// 213 query, 214 op, 543 liste apprise, 544 update.
/// Wire prouvé côté serveur : ZoneEntryFlow.LifeSkill.cs +
/// codec Messages/Generated/*.g.cs.
/// </summary>
public class LifeSkillTests
{
    private static T RoundTrip<T>(T msg) where T : NetMessage, new()
    {
        var w = new BinWriter();
        msg.Serialize(w);
        var back = new T();
        back.Deserialize(new BinReader(w.ToArray()));
        return back;
    }

    [Fact]
    public void QueryLifeSkill_EmptyPayload()
    {
        var w = new BinWriter();
        new ZoneServerQueryLifeSkill().Serialize(w);
        Assert.Empty(w.ToArray());
    }

    [Fact]
    public void LifeSkillOp_Wire7B_Produce()
    {
        // 214 [u8 op][u16 recipeId][s16 bag][s16 slot] —
        // op2 = production avec exp.
        var w = new BinWriter();
        new ZoneServerLifeSkill
        {
            Field1 = 2, Field2 = 21, Field3 = -1, Field4 = -1,
        }.Serialize(w);
        var b = w.ToArray();
        Assert.Equal(7, b.Length);
        var m = RoundTrip(new ZoneServerLifeSkill
        {
            Field1 = 0, Field2 = 11, Field3 = 1, Field4 = 5,
        });
        Assert.Equal(0, m.Field1);
        Assert.Equal(11, m.Field2);
        Assert.Equal(1, m.Field3);
        Assert.Equal(5, m.Field4);
    }

    [Fact]
    public void PlayerLifeSkill_543_Parse()
    {
        // [s16 group][u16 n][n×u16 id][u16 n][n×i32 exp]
        // [u16 n][n×i32 due].
        var w = new BinWriter();
        w.WriteShort(2);                       // groupe Zumi
        w.WriteShort(2);                       // 2 ids
        w.WriteShort(21); w.WriteShort(42);
        w.WriteShort(2);                       // 2 exps
        w.WriteInt(150); w.WriteInt(0);
        w.WriteShort(1);                       // 1 due
        w.WriteInt(999);
        var m = new ZonePlayerLifeSkill();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(2, m.Field);
        Assert.Equal(new short[] { 21, 42 }, m.RecipeIds);
        Assert.Equal(new[] { 150, 0 }, m.Exps);
        Assert.Equal(new[] { 999 }, m.Dues);
    }

    [Fact]
    public void LifeSkillUpdate_544_Ops()
    {
        // [u8 op][u16 id][u32 val] — op0 exp, op1 apprise,
        // op2 oubliée, op3 remplacée.
        var m = RoundTrip(new ZoneClientLifeSkillUpdate
        {
            Field1 = 3, Field2 = 21, Field3 = 0x2a,
        });
        Assert.Equal(3, m.Field1);
        Assert.Equal(21, m.Field2);
        Assert.Equal(0x2au, m.Field3);
        var w = new BinWriter();
        m.Serialize(w);
        Assert.Equal(7, w.ToArray().Length);
    }
}
