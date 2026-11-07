using Eden.Core.Game;
using Eden.Core.Game.Foundation;
using Eden.Core.Game.Modifiers;
using Eden.Zone.Features.Craft;

namespace Eden.Tests.Modifiers;

/// <summary>Événements datés + règles génériques de config (F3) et durées de craft du moteur (F4).</summary>
public class ServerConfigEventTests
{
    private static readonly string[] Cfg =
    {
        "Event.Noel = on",
        "Event.Noel.Start = 2026-12-20 06:00",
        "Event.Noel.End = 2026-12-27 06:00",
        "Event.Noel.AddFlat.dungeon.entries.solo = 2",
        "Mul.craft.duration = 0.5",
        "Event.Off = off",
    };

    [Fact]
    public void Event_IsActiveOnlyInsideItsWindow()
    {
        var ev = ServerConfig.Parse(Cfg).Events["Noel"];
        Assert.False(ev.InWindow(new DateTime(2026, 12, 20, 5, 59, 0)));
        Assert.True(ev.InWindow(new DateTime(2026, 12, 20, 6, 0, 0)));
        Assert.False(ev.InWindow(new DateTime(2026, 12, 27, 6, 0, 0)));
    }

    [Fact]
    public void Event_RulesAndGenericRulesAreParsed()
    {
        var snap = ServerConfig.Parse(Cfg);
        Assert.Single(snap.Events["Noel"].Rules);
        Assert.Equal("dungeon.entries.solo", snap.Events["Noel"].Rules[0].KeyId);
        Assert.Equal(0.5, snap.ModifierRules.Single(r => r.KeyId == "craft.duration").Value);
        Assert.False(snap.Events["Off"].Active);
    }

    [Fact]
    public void CraftDuration_VipIsQuarterShorter()
    {
        var engine = new ModifierResolver(new IModifierSource[] { new VipModifierSource(VipModifierSource.DefaultRules) });
        Assert.Equal(75, (int)(100 * engine.Resolve(ModifierKeys.CraftDuration, new ModifierContext(IsVip: true))));
        Assert.Equal(100, (int)(100 * engine.Resolve(ModifierKeys.CraftDuration, new ModifierContext())));
    }
}
