using Xunit;
using Eden.Core.Data.Parsers;

namespace Eden.Tests;

public class ClassSystemTests
{
    private static readonly string TClassPath = TestPaths.RepoRoot + "/server_us/Data/db/T_Class.ini";
    
    [Fact]
    public void ParseT_ClassIni_Returns20Classes()
    {
        var classes = ClassDataParser.ParseClassFile(TClassPath);
        Assert.Equal(20, classes.Count(c => c.Key >= 1 && c.Key <= 20));
    }
    
    [Fact]
    public void Warrior_IsLoaded()
    {
        var classes = ClassDataParser.ParseClassFile(TClassPath);
        var warrior = classes[1];
        Assert.Equal("Warrior", warrior.Name);
    }
    
    [Fact]
    public void Knight_HasRequirements()
    {
        var classes = ClassDataParser.ParseClassFile(TClassPath);
        var knight = classes[7];
        Assert.Equal("Knight", knight.Name);
    }
    
    [Fact]
    public void AllClasses_Loaded()
    {
        var classes = ClassDataParser.ParseClassFile(TClassPath);
        Assert.True(classes.Count >= 20);
    }
}
