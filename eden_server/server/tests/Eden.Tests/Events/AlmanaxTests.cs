using Eden.Core.Game.Calendar;
using Eden.Core.Game.Foundation;
using Eden.Core.Game.Maps;
using Eden.Core.Game.Modifiers;
using Eden.Zone.Features;
using Eden.Zone.Features.Modifiers;

namespace Eden.Tests.Events;

/// <summary>Éphéméride (F5) : rotation déterministe, exclusion, forçage, effet réel sur le moteur, paquet, modules de zone (Q8).</summary>
public class AlmanaxTests
{
    private static AlmanaxCalendar Cal(long seed = 42) => new(AlmanaxCalendar.DefaultTemplates, seed);
    private const int Start = AlmanaxCalendar.AnchorResetDay + 5000;

    [Fact]
    public void Rotation_IsDeterministicForSameSeed_AndDiffersAcrossSeeds()
    {
        var a = Cal(1).Window(Start).Select(d => d.Template.Id).ToArray();
        Assert.Equal(a, Cal(1).Window(Start).Select(d => d.Template.Id).ToArray());
        Assert.NotEqual(a, Cal(2).Window(Start).Select(d => d.Template.Id).ToArray());
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(99)] [InlineData(12345)]
    public void Rotation_NeverRepeatsTheSameThemeTwoDaysInARow(long seed)
    {
        var days = Cal(seed).Window(Start); var more = Cal(seed).Window(Start + 21);
        foreach (var w in new[] { days, more })
            for (var i = 1; i < w.Count; i++) Assert.NotEqual(w[i - 1].Template.Theme, w[i].Template.Theme);
    }

    [Fact]
    public void Window_IsTwentyOneConsecutiveDays()
    {
        var w = Cal().Window(Start);
        Assert.Equal(AlmanaxCalendar.WindowDays, w.Count);
        Assert.Equal(Enumerable.Range(Start, 21), w.Select(d => d.ResetDay));
    }

    [Fact]
    public void Rotation_UsesEveryTemplateOverAYear()
    {
        var ids = Enumerable.Range(Start, 365).Select(d => Cal(7).Day(d).Template.Id).Distinct().OrderBy(i => i).ToArray();
        Assert.Equal(AlmanaxCalendar.DefaultTemplates.Select(t => t.Id).OrderBy(i => i), ids);
    }

    [Fact]
    public void ForcedDay_WinsAndConstrainsTheNextDay()
    {
        var cal = Cal(5); cal.Force(Start + 2, 2);
        Assert.True(cal.Day(Start + 2).Forced);
        Assert.Equal(2, cal.Day(Start + 2).Template.Id);
        Assert.NotEqual("exp", cal.Day(Start + 3).Template.Theme);
        Assert.Throws<ArgumentException>(() => cal.Force(Start, 999));
    }

    [Fact]
    public void ExpDay_ChangesRealXp_OnlyOnFieldMaps()
    {
        var cal = Cal(); cal.Force(Start, 2);
        var old = GameTime.Clock;
        try
        {
            GameTime.Clock = new FakeGameClock(DateTimeOffset.UtcNow, new GameClockOptions(6, TimeZoneInfo.Utc));
            var day = GameTime.Clock.ResetDayNow();
            cal = Cal(); cal.Force(day, 2);
            var engine = new ModifierResolver(new IModifierSource[] { new AlmanaxModifierSource(cal) });
            Assert.Equal(1.25, engine.Resolve(ModifierKeys.ExpKillCharacter, new ModifierContext(MapKind: MapKind.Field)), 6);
            Assert.Equal(1.0, engine.Resolve(ModifierKeys.ExpKillCharacter, new ModifierContext(MapKind: MapKind.Dungeon)), 6);
            var why = engine.ResolveWithExplain(ModifierKeys.ExpKillCharacter, new ModifierContext(MapKind: MapKind.Field));
            Assert.Contains("almanax", why.Explain());
        }
        finally { GameTime.Clock = old; }
    }

    [Fact]
    public void Json_RoundTripsTemplates()
    {
        var t = AlmanaxCalendar.ParseJson("[{\"id\":9,\"name\":\"Test\",\"theme\":\"t\",\"weight\":2,\"rules\":[{\"key\":\"exp.kill.character\",\"mode\":\"AddPercent\",\"value\":10,\"when\":\"Dungeon\"}]}]");
        var r = Assert.Single(Assert.Single(t).Rules);
        Assert.Equal((ModifierMode.AddPercent, 10.0, MapKind.Dungeon), (r.Mode, r.Value, r.When));
    }

    [Fact]
    public void Packet_HasTwentyOneDaysAndReadableDetail()
    {
        var bytes = AlmanaxHandler.Build(Cal(), Start);
        Assert.Equal(Start, BitConverter.ToInt32(bytes, 0));
        Assert.Equal(21, bytes[4]);
        Assert.Contains("%", AlmanaxHandler.Detail(AlmanaxCalendar.DefaultTemplates[1]));
        Assert.Contains("×0,8", AlmanaxHandler.Detail(AlmanaxCalendar.DefaultTemplates[2]).Replace('.', ','));
    }

    [Fact]
    public void ZoneFeatures_HaveUniqueNames() =>
        Assert.Equal(ZoneFeatures.All.Count, ZoneFeatures.All.Select(f => f.Name).Distinct().Count());

    [Fact]
    public void DailyCounters_ResetWithTheDay()
    {
        Assert.Equal(1, DailyCounters.Add(900001, 7, "x", 1));
        Assert.Equal(3, DailyCounters.Add(900001, 7, "x", 2));
        Assert.Equal(1, DailyCounters.Add(900002, 7, "x", 1));
        Assert.Equal(0, DailyCounters.Get(900001, 7, "x"));       // jour passé purgé
    }
}
