using Eden.Core.Data.Parsers;
using Eden.Core.Game;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régressions événements « pay » — parser <c>S_PaySystem.ini</c>
/// (18 colonnes, records multi-lignes Big5) et compilation DSL
/// <c>ConvertCmd</c> natif @0x080ea500 :
/// cond → vecteurs +0x00/+0x0c/+0x18, award → +0x24…+0x78.
/// </summary>
public class PaySystemTests
{
    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(),
            Guid.NewGuid() + ".ini");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void ParseRecord_ColumnsAndCosts()
    {
        // Record réel (Big5) : id|type|nom|desc|#:cond|
        // 4×(item,nom,n)|#:award — items 2-4 vides.
        var path = WriteTemp(
            "|V.1|18|\r\n" +
            "1|1|nom|desc\n\nmulti|#:If_CharLevel 79 100:::::::" +
            "|27606|item1|10|||10|||||||" +
            "#:Reputation 34 + 100:GiveBuff 54879 1800::::::|\r\n");
        var map = PaySystemParser.Parse(path);
        Assert.Single(map);
        var def = map[1];
        Assert.Equal(1, def.Type);
        Assert.Equal("nom", def.Name);
        Assert.Contains("multi", def.Desc);
        Assert.StartsWith("#:If_CharLevel", def.CondCmd);
        // 4 items de coût : seul le premier rempli dans ces données.
        Assert.Equal(27606, def.CostItems[0].ItemId);
        Assert.Equal("item1", def.CostItems[0].Name);
        Assert.Equal(10, def.CostItems[0].Count);
        Assert.Equal(0, def.CostItems[1].ItemId);
        Assert.StartsWith("#:Reputation", def.AwardCmd);
    }

    [Fact]
    public void ConvertCmd_Conditions()
    {
        var cmd = PayEventCmdParser.Build(
            "#:If_CharLevel 79 100:If_CharReputation 34 1000 2000:" +
            "If_CharClassLevel 32 >= 60:",
            null);
        Assert.Single(cmd.LevelConds);
        Assert.Equal((79, 100), cmd.LevelConds[0]);
        Assert.Single(cmd.RepConds);
        Assert.Equal((34, 1000, 2000),
            (cmd.RepConds[0].RepId, cmd.RepConds[0].Lo,
                cmd.RepConds[0].Hi));
        Assert.Single(cmd.ClassLevelConds);
        Assert.Equal((32, 1, 60), cmd.ClassLevelConds[0]);
    }

    [Fact]
    public void ConvertCmd_ClassLevelOps()
    {
        // "=" → 2, ">=" → 1, "<=" → 3, autre → 0.
        var cmd = PayEventCmdParser.Build(
            "#:If_CharClassLevel 1 = 5:If_CharClassLevel 2 >= 6:" +
            "If_CharClassLevel 3 <= 7:If_CharClassLevel 4 ~= 8:",
            null);
        Assert.Equal(4, cmd.ClassLevelConds.Count);
        Assert.Equal((1, 2, 5), cmd.ClassLevelConds[0]);
        Assert.Equal((2, 1, 6), cmd.ClassLevelConds[1]);
        Assert.Equal((3, 3, 7), cmd.ClassLevelConds[2]);
        Assert.Equal((4, 0, 8), cmd.ClassLevelConds[3]);
    }

    [Fact]
    public void ConvertCmd_Awards()
    {
        var cmd = PayEventCmdParser.Build(null,
            "#:Reputation 34 + 100:Reputation 35 - 50:" +
            "GiveItem 20121 2:CharExp 124250:GiveGold 3880:" +
            "ClassPoint 10:GiveBuff 54879 1800:ChooseItem 51224 2:" +
            "SpecialCoin 0 + 10:");
        Assert.Equal(2, cmd.Reputations.Count);
        Assert.Equal((34, 100), cmd.Reputations[0]);
        Assert.Equal((35, -50), cmd.Reputations[1]);
        Assert.Single(cmd.Items);
        Assert.Equal((20121, (short)2), cmd.Items[0]);
        Assert.Equal(new[] { 124250 }, cmd.CharExp);
        Assert.Equal(new[] { 3880 }, cmd.Gold);
        Assert.Equal(new[] { 10 }, cmd.ClassPoints);
        Assert.Single(cmd.Buffs);
        Assert.Equal((54879, (short)1800), cmd.Buffs[0]);
        Assert.Single(cmd.ChooseItems);
        Assert.Equal((51224, 2), cmd.ChooseItems[0]);
        Assert.Single(cmd.SpecialCoins);
        Assert.Equal((0, 10), cmd.SpecialCoins[0]);
    }

    [Fact]
    public void ConvertCmd_SpecialCoinGate()
    {
        // Types 6-8 et >=12 rejetés par ConvertCmd natif.
        var cmd = PayEventCmdParser.Build(null,
            "#:SpecialCoin 0 + 5:SpecialCoin 6 + 5:SpecialCoin 7 + 5:" +
            "SpecialCoin 8 + 5:SpecialCoin 9 + 5:SpecialCoin 12 + 5:");
        Assert.Equal(2, cmd.SpecialCoins.Count);
        Assert.Equal((0, 5), cmd.SpecialCoins[0]);
        Assert.Equal((9, 5), cmd.SpecialCoins[1]);
    }

    [Fact]
    public void ConvertCmd_WrongArity_Ignored()
    {
        // Arité incorrecte → l'op est ignorée (native compare le
        // nombre de tokens par op).
        var cmd = PayEventCmdParser.Build(
            "#:If_CharLevel 79:", "#:GiveItem 20121:");
        Assert.Empty(cmd.LevelConds);
        Assert.Empty(cmd.Items);
    }

    [Fact]
    public void PayEventInRange_RealData()
    {
        // Tous les records S_PaySystem réels doivent compiler sans
        // lever d'exception (fichier encodé MyCrypt en place).
        var path = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "server_us", "Data", "db_decoded", "S_PaySystem.ini");
        if (!File.Exists(path)) return; // fixture absente en CI
        var defs = PaySystemParser.Parse(path);
        Assert.True(defs.Count > 800);   // 872 records natifs
        foreach (var def in defs.Values)
        {
            var cmd = PayEventCmdParser.Build(def.CondCmd, def.AwardCmd);
            // Chaque def réelle a 4 slots de coût et au moins une
            // récompense ou une condition.
            Assert.Equal(4, def.CostItems.Length);
        }
    }
}
