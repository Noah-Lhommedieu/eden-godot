using Eden.Core.Data.Parsers;
using Eden.Core.Game.Classes;
using Xunit;

namespace Eden.Tests;

public class SpellClassGateTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    [Fact]
    public void GeneralSkill_CastableAfterClassSwitch()
    {
        var spells = SpellParser.Parse(DbDir + "/S_Spell.ini");
        var sp = spells.First(s => s.Id == 50011);   // Deep Impact
        Assert.Equal(0UL, sp.ClassMask & (1UL << 4)); // classe 5 absente du mask
        Assert.True(ClassSkillTable.CanCast(5, sp));
    }

    [Fact]
    public void MonoClassSkill_RefusedOnOtherClass()
    {
        var sp = new SpellParser.SpellDef { Id = 1, ClassMask = 1 };
        Assert.False(ClassSkillTable.CanCast(5, sp));
        Assert.True(ClassSkillTable.CanCast(1, sp));
    }
}
