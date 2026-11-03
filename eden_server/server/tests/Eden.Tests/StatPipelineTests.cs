using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Services;
using Eden.Core.Game.Stats;
using Xunit;

namespace Eden.Tests;

/// <summary>Pipeline de stats — formule CCharFormula::TotalX native :
/// (lvl + Σpre) × (classPct + Σpct)/100 + Σpost, étapes ordonnées.</summary>
public class StatPipelineTests
{
    [Fact]
    public void ResolveStat_EquipmentPreInsideMultiplier()
    {
        // lvl 100 str, classe 110%, équipement +40 str :
        // (100+40)×110/100 = 154 — l'équipement est DANS le produit.
        var c = new StatContribution().Add(StatIndex.Str, pre: 40);
        Assert.Equal(154, StatPipeline.ResolveStat(
            c, StatIndex.Str, 100, 110));
    }

    [Fact]
    public void ResolveStat_PctAndPostChannels()
    {
        // Titre +10% + buff flat +5 :
        // (100+0)×(110+10)/100+5 = 125.
        var c = new StatContribution()
            .Add(StatIndex.Str, pct: 10, post: 5);
        Assert.Equal(125, StatPipeline.ResolveStat(
            c, StatIndex.Str, 100, 110));
    }

    [Fact]
    public async Task StagesResolveInRegistrationOrder()
    {
        var pipe = new StatPipeline()
            .AddStage("equipment", (_, _, _) => Task.FromResult(
                new StatContribution().Add(StatIndex.Hp, pre: 500)))
            .AddStage("permanents", (_, _, _) => Task.FromResult(
                new StatContribution().Add(StatIndex.Hp, pct: 20)));
        var c = await pipe.ResolveAsync(1);
        // (1000+500)×(100+20)/100 = 1800 — les deux étapes sommées.
        Assert.Equal(1800, StatPipeline.ResolveStat(
            c, StatIndex.Hp, 1000, 100));
        Assert.Equal(new[] { "equipment", "permanents" },
            pipe.StageNames);
    }

    [Fact]
    public async Task EmptyPipelineEqualsBaseOnly()
    {
        var c = await new StatPipeline().ResolveAsync(1);
        Assert.Equal(120, StatPipeline.ResolveStat(
            c, StatIndex.Str, 100, 120));
    }

    private static ItemParser.ItemDef Def(int id, int kind = 1,
        int str = 0, int pAtk = 0, int def = 0, int hp = 0)
        => new() { Id = id, Kind = kind, BonusStr = str,
            PhysicalAttack = pAtk, Defence = def, BonusHp = hp };

    [Fact]
    public async Task ItemStagesOrdered_EquipmentGemsFortifyCostumes()
    {
        var order = new List<string>();
        StatPipeline.Provider P(string n) => (_, _, _) =>
        { order.Add(n); return Task.FromResult(new StatContribution()); };
        var pipe = new StatPipeline().AddStage("equipment", P("equipment"))
            .AddStage("gems", P("gems")).AddStage("fortify", P("fortify"))
            .AddStage("costumes", P("costumes")).AddStage("buffs", P("buffs"));
        await pipe.ResolveAsync(1);
        Assert.Equal(new[] { "equipment", "gems", "fortify", "costumes",
            "buffs" }, order);
    }

    [Fact]
    public async Task StatFetch_PutSharedBetweenStages()
    {
        var hits = 0;
        var pipe = new StatPipeline()
            .AddStage("a", (_, _, f) =>
            {
                if (f!.TryGet<object>("equipment.all", out _) == false)
                { hits++; f.Put("equipment.all", new object()); }
                return Task.FromResult(new StatContribution());
            })
            .AddStage("b", (_, _, f) =>
            {
                if (f!.TryGet<object>("equipment.all", out _) == false)
                    hits++;
                return Task.FromResult(new StatContribution());
            });
        await pipe.ResolveAsync(1);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void Gems_AddStatsOfEmbeddedGemTemplates()
    {
        var defs = new[] { Def(10, str: 0), Def(500, kind: 45, str: 2, hp: 100) };
        var item = new ItemEntity { ItemId = 10, BagId = 0, EmbeddedAmount = 2,
            EmbeddedId1 = 500, EmbeddedId2 = 0 };
        var g = EquipmentStatService.SumGems(new[] { item }, defs);
        Assert.Equal(100, g.BonusHp);
        Assert.Equal(2 * Eden.Core.Game.GameBalance.CoreStatDivisor, g.Str);
        var c = StatContribution.FromBonusStats(g);
        Assert.Equal(100, c.Pre(StatIndex.Hp));
        // socket fermé (-1) / hors embedded_amount : ignoré
        item.EmbeddedAmount = 0;
        Assert.Equal(0, EquipmentStatService.SumGems(new[] { item }, defs).BonusHp);
    }

    [Fact]
    public void Gems_IgnoredOnCostumePageAndUnidentified()
    {
        var defs = new[] { Def(10), Def(500, hp: 100) };
        var costume = new ItemEntity { ItemId = 10, BagId = 3,
            EmbeddedAmount = 1, EmbeddedId1 = 500 };
        var unid = new ItemEntity { ItemId = 10, BagId = 0, Identify = 0,
            EmbeddedAmount = 1, EmbeddedId1 = 500 };
        Assert.Equal(0, EquipmentStatService
            .SumGems(new[] { costume, unid }, defs).BonusHp);
    }

    [Fact]
    public void Fortify_PlusN_WeaponAttackArmorDefence()
    {
        var defs = new[] { Def(1, kind: 17, pAtk: 1000), Def(2, kind: 2, def: 200) };
        var w = new ItemEntity { ItemId = 1, BagId = 0, EnchantLevel = 10 };
        var a = new ItemEntity { ItemId = 2, BagId = 0, EnchantLevel = 5 };
        var f = EquipmentStatService.SumFortify(new[] { w, a }, defs, 3, 3);
        Assert.Equal(300, f.PAtk);   // 1000 × 10 × 3 %
        Assert.Equal(30, f.Def);     // 200 × 5 × 3 %
        var c = StatContribution.FromBonusStats(f);
        Assert.Equal(300, c.PhysAttackAdd);
        Assert.Equal(30, c.DefenceAdd);
    }

    [Fact]
    public void Witchcraft_CostumePagesVsWornEquipment()
    {
        var scroll = new ItemParser.ItemDef { Id = 900, Kind = 79,
            UseEnchantId = 5555 };
        var defs = new[] { Def(10), scroll };
        var costume = new ItemEntity { ItemId = 10, BagId = 2, Witchcraft = 900 };
        var worn = new ItemEntity { ItemId = 10, BagId = 0, Witchcraft = 900 };
        var items = new[] { costume, worn };
        Assert.Single(EquipmentStatService.WitchcraftEnchantIds(items, defs, true));
        Assert.Equal(5555, EquipmentStatService
            .WitchcraftEnchantIds(items, defs, true).First());
        Assert.Single(EquipmentStatService.WitchcraftEnchantIds(items, defs, false));
    }

    [Fact]
    public void EquipmentSum_StillSkipsCostumeBags()
    {
        var defs = new[] { Def(10, str: 5) };
        var costume = new ItemEntity { ItemId = 10, BagId = 3 };
        Assert.Equal(0, EquipmentStatService.Sum(new[] { costume }, defs).Str);
    }

    [Fact]
    public void GoldEnchant_OnlyWornIdentifiedPieces()
    {
        var worn = new ItemEntity { ItemId = 10, BagId = 0, Identify = 1,
            GoldEnchant = 777 };
        var none = new ItemEntity { ItemId = 11, BagId = 0, Identify = 1 };
        var costume = new ItemEntity { ItemId = 12, BagId = 2, Identify = 1,
            GoldEnchant = 888 };
        var bag = new ItemEntity { ItemId = 13, BagId = 7, Identify = 1,
            GoldEnchant = 999 };
        Assert.Equal(new[] { 777 }, EquipmentStatService
            .GoldEnchantIds(new[] { worn, none, costume, bag }).ToArray());
    }
}
