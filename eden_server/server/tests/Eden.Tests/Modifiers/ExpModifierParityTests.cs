using Eden.Core.Game.Foundation;
using Eden.Core.Game.Modifiers;

namespace Eden.Tests.Modifiers;

/// <summary>Parité des formules d'EXP migrées vers le moteur (PLAN-ACTION-3 F6) et reset 06:00 (F2).</summary>
public class ExpModifierParityTests
{
    private static ModifierResolver Engine() => new(new IModifierSource[]
    {
        new VipModifierSource(VipModifierSource.DefaultRules), new CharmModifierSource(),
    });

    [Fact]
    public void NoCharmNoVip_IsNeutral()
    {
        var ctx = new ModifierContext(ClassKind: ClassKind.Normal);
        Assert.Equal(1, Engine().Resolve(ModifierKeys.ExpCharacter, ctx));
        Assert.Equal(1, Engine().Resolve(ModifierKeys.ExpClass, ctx));
    }

    [Fact]
    public void Charm_AddsPercent_LikeLegacy()
    {
        var ctx = new ModifierContext(ClassKind: ClassKind.Normal, CharmExpPercent: 50);
        Assert.Equal(150, (long)(100 * Engine().Resolve(ModifierKeys.ExpCharacter, ctx)));
    }

    [Fact]
    public void NormalAndAwakenedCharms_AreSeparated()
    {
        var normal = new ModifierContext(ClassKind: ClassKind.Normal, CharmClassPercent: 20, CharmAwakenedPercent: 40);
        var awake = normal with { ClassKind = ClassKind.Awakened };
        var e = Engine();
        Assert.Equal(1.2, e.Resolve(ModifierKeys.ExpFor(ClassKind.Normal), normal), 6);
        Assert.Equal(1.4, e.Resolve(ModifierKeys.ExpFor(ClassKind.Awakened), awake), 6);
    }

    [Fact]
    public void Vip_AddsTenPercentExp()
    {
        var ctx = new ModifierContext(ClassKind: ClassKind.Normal, IsVip: true);
        Assert.Equal(1.1, Engine().Resolve(ModifierKeys.ExpCharacter, ctx), 6);
    }

    [Fact]
    public void DungeonEntries_DefaultTen()
    {
        var ctx = new ModifierContext();
        Assert.Equal(10, Engine().Resolve(ModifierKeys.DungeonEntriesSolo, ctx));
        Assert.Equal(10, Engine().Resolve(ModifierKeys.DungeonEntriesMulti, ctx));
    }

    [Fact]
    public void ResetDay_ChangesAtSixOClock()
    {
        var clock = new FakeGameClock(DateTimeOffset.Parse("2026-11-07T05:59:00Z"),
            new GameClockOptions(6, TimeZoneInfo.Utc));
        var day = clock.ResetDayNow();
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(day + 1, clock.ResetDayNow());
    }
}
