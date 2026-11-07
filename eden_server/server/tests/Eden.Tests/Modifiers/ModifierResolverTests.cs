using Eden.Core.Game.Maps;
using Eden.Core.Game.Modifiers;

namespace Eden.Tests.Modifiers;

public class ModifierResolverTests
{
    private static ModifierResolver R(params IModifierSource[] s) => new(s);
    private static StaticModifierSource S(string n, params StaticModifierSource.Rule[] r) => new(n, r);
    private static StaticModifierSource.Rule Rule(ModifierKey k, ModifierMode m, double v, MapKind? mk = null) =>
        new(k.Id, m, v, mk);

    [Fact]
    public void NoSource_ReturnsDefault() =>
        Assert.Equal(1, R().Resolve(ModifierKeys.ExpCharacter, default));

    [Fact]
    public void Multipliers_Compose()
    {
        var r = R(S("config", Rule(ModifierKeys.ExpCharacter, ModifierMode.Multiply, 2)),
                  S("event", Rule(ModifierKeys.ExpCharacter, ModifierMode.Multiply, 1.5)));
        Assert.Equal(3, r.Resolve(ModifierKeys.ExpCharacter, default), 6);
    }

    [Fact]
    public void AdditivePercents_Sum_NotMultiply()
    {
        var r = R(S("charm", Rule(ModifierKeys.ExpCharacter, ModifierMode.AddPercent, 20)),
                  S("vip", Rule(ModifierKeys.ExpCharacter, ModifierMode.AddPercent, 10)));
        Assert.Equal(1.3, r.Resolve(ModifierKeys.ExpCharacter, default), 6);
    }

    [Fact]
    public void Mixed_Multiply_ThenAdditive()
    {
        var r = R(S("config", Rule(ModifierKeys.ExpCharacter, ModifierMode.Multiply, 2)),
                  S("vip", Rule(ModifierKeys.ExpCharacter, ModifierMode.AddPercent, 10)));
        Assert.Equal(2.2, r.Resolve(ModifierKeys.ExpCharacter, default), 6);
    }

    [Fact]
    public void AddFlat_AppliesAfterMultipliers()
    {
        var k = ModifierKeys.DungeonEntriesSolo;
        var r = R(S("event", Rule(k, ModifierMode.AddFlat, 2)),
                  S("config", Rule(k, ModifierMode.Set, 10)));
        Assert.Equal(12, r.Resolve(k, default));
    }

    [Fact]
    public void Set_ReplacesBase_LastWins()
    {
        var k = ModifierKeys.DungeonEntriesSolo;
        var r = R(S("a", Rule(k, ModifierMode.Set, 3)), S("b", Rule(k, ModifierMode.Set, 5)));
        Assert.Equal(5, r.Resolve(k, default));
    }

    [Fact]
    public void Cap_AndKeyBounds_AreAppliedLast()
    {
        var k = ModifierKeys.ExpCharacter;
        var r = R(S("a", Rule(k, ModifierMode.Multiply, 50)), S("cap", Rule(k, ModifierMode.Cap, 4)));
        Assert.Equal(4, r.Resolve(k, default));
        var r2 = R(S("a", Rule(k, ModifierMode.Multiply, 5000)));
        Assert.Equal(k.Max, r2.Resolve(k, default));
        var r3 = R(S("a", Rule(k, ModifierMode.Multiply, -3)));
        Assert.Equal(k.Min, r3.Resolve(k, default));
    }

    [Fact]
    public void MapKind_FilterOnlyAppliesOnThatKind()
    {
        var k = ModifierKeys.ExpCharacter;
        var r = R(S("map", Rule(k, ModifierMode.Multiply, 2, MapKind.Dungeon)));
        Assert.Equal(2, r.Resolve(k, new ModifierContext(MapKind: MapKind.Dungeon)));
        Assert.Equal(1, r.Resolve(k, new ModifierContext(MapKind: MapKind.Field)));
    }

    [Fact]
    public void Explain_ListsEverySource()
    {
        var r = R(S("config", Rule(ModifierKeys.ExpCharacter, ModifierMode.Multiply, 2)),
                  new VipModifierSource(VipModifierSource.DefaultRules));
        var res = r.ResolveWithExplain(ModifierKeys.ExpCharacter, new ModifierContext(IsVip: true));
        Assert.Equal(2.2, res.Value, 6);
        Assert.Equal(2, res.Contributions.Count);
        Assert.Contains("vip:AddPercent 10", res.Explain());
    }

    [Fact]
    public void ExpFor_SeparatesNormalAndAwakened()
    {
        Assert.Equal(ModifierKeys.ExpClass, ModifierKeys.ExpFor(ClassKind.Normal));
        Assert.Equal(ModifierKeys.ExpAwakenedClass, ModifierKeys.ExpFor(ClassKind.Awakened));
    }

    [Fact]
    public void AwakenedBonus_DoesNotLeakToNormalClass()
    {
        var r = R(S("charm", Rule(ModifierKeys.ExpAwakenedClass, ModifierMode.AddPercent, 30)));
        Assert.Equal(1.3, r.Resolve(ModifierKeys.ExpFor(ClassKind.Awakened), default), 6);
        Assert.Equal(1.0, r.Resolve(ModifierKeys.ExpFor(ClassKind.Normal), default), 6);
    }
}

public class VipModifierSourceTests
{
    private static ModifierResolver Vip() => new(new[] { new VipModifierSource(VipModifierSource.DefaultRules) });

    [Fact]
    public void NonVip_GetsNothing()
    {
        foreach (var k in ModifierKeys.All)
            Assert.Equal(k.Default, Vip().Resolve(k, new ModifierContext(IsVip: false)));
    }

    [Fact]
    public void Vip_ThreeExpBonuses_PlusTenPercent()
    {
        var c = new ModifierContext(IsVip: true);
        Assert.Equal(1.1, Vip().Resolve(ModifierKeys.ExpCharacter, c), 6);
        Assert.Equal(1.1, Vip().Resolve(ModifierKeys.ExpClass, c), 6);
        Assert.Equal(1.1, Vip().Resolve(ModifierKeys.ExpAwakenedClass, c), 6);
    }

    [Fact]
    public void Vip_TimesAreMinus25Percent()
    {
        var c = new ModifierContext(IsVip: true);
        foreach (var k in new[] { ModifierKeys.CraftCastSeconds, ModifierKeys.CraftDuration,
                     ModifierKeys.GatherTickSeconds, ModifierKeys.GatherDurabilityCost })
            Assert.Equal(0.75, Vip().Resolve(k, c), 6);
    }

    [Fact]
    public void Vip_Expired_BehavesAsNormal()
    {
        var ctx = new ModifierContext(IsVip: true);
        Assert.Equal(0.75, Vip().Resolve(ModifierKeys.CraftDuration, ctx), 6);
        Assert.Equal(1.0, Vip().Resolve(ModifierKeys.CraftDuration, ctx with { IsVip = false }), 6);
    }

    [Fact]
    public void NonOverridableKey_IsIgnored()
    {
        var key = new ModifierKey("x", ModifierUnit.Ratio, 1, 0, 10, false, "t");
        var src = new VipModifierSource(new[] { new VipRule("x", ModifierMode.Multiply, 5) });
        Assert.Equal(1, new ModifierResolver(new[] { src }).Resolve(key, new ModifierContext(IsVip: true)));
    }

    [Fact]
    public void VipAndCharm_AreAdditive()
    {
        var charm = new StaticModifierSource("charm", new[] {
            new StaticModifierSource.Rule("exp.character", ModifierMode.AddPercent, 20) });
        var r = new ModifierResolver(new IModifierSource[] { charm, new VipModifierSource(VipModifierSource.DefaultRules) });
        Assert.Equal(1.3, r.Resolve(ModifierKeys.ExpCharacter, new ModifierContext(IsVip: true)), 6);
    }

    [Fact]
    public void Registry_HasUniqueIds() =>
        Assert.Equal(ModifierKeys.All.Count, ModifierKeys.All.Select(k => k.Id).Distinct().Count());
}
