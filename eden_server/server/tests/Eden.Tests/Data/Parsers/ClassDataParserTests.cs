using Xunit;
using Eden.Core.Data.Parsers;
using System.IO;

namespace Eden.Tests.Data.Parsers;

public class ClassDataParserTests
{
    private readonly string _testDataPath = Path.Combine(
        Directory.GetCurrentDirectory(),
        "..", "..", "..",
        "eden_server", "server_us", "Data", "db", "T_Class.ini"
    );

    [Fact]
    public void ParseClassFile_ShouldLoadAllClasses()
    {
        // Skip test if file doesn't exist (CI environment)
        if (!File.Exists(_testDataPath))
        {
            return;
        }

        var classes = ClassDataParser.ParseClassFile(_testDataPath);

        // Should have classes 1-20 (base) and 21-60 (awakened)
        Assert.True(classes.ContainsKey(1), "Should have class ID 1 (Warrior)");
        Assert.True(classes.ContainsKey(20), "Should have class ID 20 (Mimic)");
        Assert.True(classes.ContainsKey(21), "Should have class ID 21 (Berserker)");
        Assert.True(classes.ContainsKey(60), "Should have class ID 60 (Reaver)");
    }

    [Fact]
    public void ParseClassFile_ShouldCorrectlyParseBaseClasses()
    {
        if (!File.Exists(_testDataPath))
            return;

        var classes = ClassDataParser.ParseClassFile(_testDataPath);
        var warrior = classes[1];

        Assert.Equal("Warrior", warrior.Name);
        Assert.NotEmpty(warrior.ShortDescription);
        Assert.Equal("Bravery", warrior.Trait);
        Assert.NotNull(warrior.Requirements);
    }

    [Fact]
    public void ParseClassFile_ShouldCorrectlyParseAwakeneClasses()
    {
        if (!File.Exists(_testDataPath))
            return;

        var classes = ClassDataParser.ParseClassFile(_testDataPath);
        var berserker = classes[21];
        var shielder = classes[22];

        Assert.Equal("Berserker", berserker.Name);
        Assert.Equal("Shielder", shielder.Name);
        Assert.NotEmpty(berserker.ShortDescription);
        Assert.NotEmpty(shielder.ShortDescription);
    }

    [Fact]
    public void ParseClassFile_ShouldParseRequirements()
    {
        if (!File.Exists(_testDataPath))
            return;

        var classes = ClassDataParser.ParseClassFile(_testDataPath);
        
        // Warrior has no level requirement (beginning job)
        Assert.NotNull(classes[1].Requirements);
        
        // Knight requires level 25 + Warrior level 10
        var knight = classes[7];
        Assert.Equal(25, knight.Requirements.MinCharacterLevel);
        Assert.NotEmpty(knight.Requirements.Prerequisites);
    }

    [Fact]
    public void ParseClassFile_ShouldNotIncludeDuplicateClasses()
    {
        if (!File.Exists(_testDataPath))
            return;

        var classes = ClassDataParser.ParseClassFile(_testDataPath);

        // Should NOT include duplicate ranges (101-120, 201-220 etc)
        Assert.False(classes.ContainsKey(101), "Should not include duplicate class ID 101");
        Assert.False(classes.ContainsKey(102), "Should not include duplicate class ID 102");
    }
}
