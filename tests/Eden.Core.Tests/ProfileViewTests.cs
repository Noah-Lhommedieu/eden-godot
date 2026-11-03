using EdenClient.Protocol;
using EdenClient.Windows;
using Xunit;

namespace EdenClient.NetTests;

public class ProfileViewTests
{
    private static PlayerStats S() => new(
        Str: 30, Int: 20, Agi: 25, Wis: 10, Lck: 12,
        Resists: new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 },
        MaxMp: 100, PhysicalAttack: 150, MagicAttack: 90,
        AttackSpeedRate: 1.25f, MagicSpeedRate: 1f, PhysicalDefence: 70,
        Rates: new short[] { 240, 98, 429, 807, 563, 150, 300, 160 },
        Speeds: new float[] { 0, 0, 0, 0, 105f, 0 });

    [Fact]
    public void Null_Stats_Dashes()
    {
        var d = ProfileView.Compute(null, null, null);
        Assert.Equal("-", d["str"].Text);
        Assert.Equal("-", d["block"].Text);
    }

    [Fact]
    public void Core_Tone_Up_Over_Base()
    {
        var d = ProfileView.Compute(S(), new[] { 30, 18, 25, 10, 12 }, null);
        Assert.Equal(StatTone.Normal, d["str"].Tone);
        Assert.Equal(StatTone.Up, d["int"].Tone);
        Assert.Equal("150", d["patk"].Text);
        Assert.Equal(StatTone.Atk, d["patk"].Tone);
        Assert.Equal(StatTone.Def, d["def"].Tone);
    }

    [Fact]
    public void Calibrated_Rates_As_ref_profile_png()
    {
        // 296 sans paliers 493 : colonne valeur = taux brut, % = taux/100.
        var d = ProfileView.Compute(S(), null, null);
        Assert.Equal("429", d["acc"].Text);
        Assert.Equal("4.29%", d["acc%"].Text);
        Assert.Equal("8.07%", d["eva%"].Text);
        Assert.Equal("2.4%", d["parry%"].Text);
        Assert.Equal("0.98%", d["block%"].Text);
        Assert.Equal("5.63%", d["crit%"].Text);
        Assert.Equal("150%", d["critdmg%"].Text);
        Assert.Equal("1.5", d["critdmg"].Text);
        Assert.Equal("105%", d["move%"].Text);
    }

    [Fact]
    public void LevelStats_Override_Values()
    {
        var lv = new float[16];
        lv[0] = 122f; lv[1] = 124.7f; lv[3] = 174f; lv[12] = 1.5f;
        var d = ProfileView.Compute(S(), null, lv);
        Assert.Equal("122", d["acc"].Text);
        Assert.Equal("124.7", d["crit"].Text);
        Assert.Equal("174", d["eva"].Text);
        Assert.Equal("1.5", d["aspd"].Text);
        Assert.Equal("4.29%", d["acc%"].Text);
    }

    [Fact]
    public void Resists_Race_Hint()
    {
        Assert.Equal(new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9" },
            ProfileView.Resists(S()));
        Assert.Equal("0", ProfileView.Resists(null)[3]);
        Assert.Equal("Halfkin", ProfileView.Race(5));
        Assert.Equal("?", ProfileView.Race(9));
        Assert.Equal("32 = 30 + 2", ProfileView.SplitHint(32, 30));
        Assert.Equal("7", ProfileView.SplitHint(7, null));
    }
}
