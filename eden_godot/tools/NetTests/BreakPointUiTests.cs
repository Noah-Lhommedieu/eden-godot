using System.Text;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Plan 13 — logique pure du cadre de cible Break Point :
/// masque de faiblesses → icônes, patience → barre violette, étoiles,
/// label mob « &lt;Titre&gt;\nLvN Nom », échelle de taille, parsing
/// c_monster, 298 (max patience) et 280 (attr 0x13).</summary>
public class BreakPointUiTests
{
    static BreakPointUiTests() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void WeakSlots_FollowMaskBits()
    {
        // Herak : Slash (0) + Lightning (5).
        Assert.Equal(new[] { 0, 5 }, BreakPointUi.WeakSlots(0b100001));
        Assert.Empty(BreakPointUi.WeakSlots(0));
        Assert.Equal(9, BreakPointUi.WeakSlots(511).Count);
        Assert.False(BreakPointUi.IsWeak(0b1, 9));
        Assert.False(BreakPointUi.IsWeak(0b1, -1));
    }

    [Fact]
    public void IconImages_CoverNineAttributes_AndExistInImageset()
    {
        Assert.Equal(9, BreakPointUi.IconImages.Length);
        Assert.Equal(9, BreakPointUi.IconImages.Distinct().Count());
        Assert.Equal("AntiChop", BreakPointUi.IconImages[0]);   // Slash
        Assert.Equal("AntiHoly", BreakPointUi.IconImages[7]);   // Light
        // Les noms existent dans l'imageset natif quand il est présent.
        var set = FindImageset();
        if (set == null) return;
        var txt = File.ReadAllText(set);
        foreach (var n in BreakPointUi.IconImages)
            Assert.Contains($"Name=\"{n}\"", txt);
    }

    private static string? FindImageset()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "dist", "package-linux",
                "gamedata", "uidata", "imagesets", "main.imageset");
            if (File.Exists(c)) return c;
            dir = dir.Parent;
        }
        return null;
    }

    [Theory]
    [InlineData(1, 1)]   // TailleMob.png : mob commun = 1 étoile
    [InlineData(2, 2)]    // ref-shots/targetframe (Elite mob).png : 2 étoiles
    [InlineData(3, 3)]
    [InlineData(5, 5)]
    [InlineData(9, 5)]
    [InlineData(0, 0)]
    public void Stars_FromMonsterType(int type, int stars) =>
        Assert.Equal(stars, BreakPointUi.Stars(type));

    [Fact]
    public void Bar_FractionTextAndVisibility()
    {
        Assert.True(BreakPointUi.ShowBar(33, 821));
        Assert.False(BreakPointUi.ShowBar(0, 100));     // mob sans faiblesse
        Assert.False(BreakPointUi.ShowBar(33, 0));
        Assert.Equal(0.5f, BreakPointUi.Fraction(50, 100));
        Assert.Equal(0f, BreakPointUi.Fraction(-30, 100));   // patience négative
        Assert.Equal(1f, BreakPointUi.Fraction(500, 100));
        Assert.Equal(0f, BreakPointUi.Fraction(5, 0));
        Assert.Equal("0/821", BreakPointUi.BarText(-3, 821));
        Assert.Equal("821/821", BreakPointUi.BarText(821, 821));
    }

    [Fact]
    public void ParsePatience_SameRulesAsServer()
    {
        static string[] Row(string mask, string pat)
        {
            var r = new string[20]; Array.Fill(r, "");
            r[10] = mask; r[12] = pat; return r;
        }
        Assert.Equal(100, BreakPointUi.ParsePatience(Row("0", "")));
        Assert.Equal(0, BreakPointUi.ParsePatience(Row("5", "")));
        Assert.Equal(54023, BreakPointUi.ParsePatience(Row("5", "54023.74166")));
        Assert.Equal(9_999_999, BreakPointUi.ParsePatience(Row("5", "99999999999")));
        Assert.Equal(821, BreakPointUi.ParsePatience(Row("33", "821")));
    }

    [Fact]
    public void MobLabel_TitleOnOwnLine_NoLvGluedToTitle()
    {
        Assert.Equal("<Legion Elite>\nLv81 Archangel of Sin",
            BreakPointUi.FormatMobLabel(81, "<Legion Elite> \nArchangel of Sin"));
        Assert.Equal("Lv5 Mushroom", BreakPointUi.FormatMobLabel(5, "Mushroom"));
        Assert.Equal("Mushroom", BreakPointUi.FormatMobLabel(0, "Mushroom"));
        var (t, lv, n) = BreakPointUi.SplitLabel("<Legion Elite>\nLv81 Archangel of Sin");
        Assert.Equal("<Legion Elite>", t); Assert.Equal(81, lv);
        Assert.Equal("Archangel of Sin", n);
        // Ancien format collé « Lv81 <Titre>\nNom » toujours décodé.
        var (t2, lv2, n2) = BreakPointUi.SplitLabel("Lv81 <Legion Elite>\nArchangel of Sin");
        Assert.Equal("<Legion Elite>", t2); Assert.Equal(81, lv2);
        Assert.Equal("Archangel of Sin", n2);
        var (t3, lv3, n3) = BreakPointUi.SplitLabel("Lv12 Rose Knight");
        Assert.Equal("", t3); Assert.Equal(12, lv3); Assert.Equal("Rose Knight", n3);
        var (_, lv4, n4) = BreakPointUi.SplitLabel("Orc");
        Assert.Equal(0, lv4); Assert.Equal("Orc", n4);
    }

    [Fact]
    public void MobScale_BiggerForRaresAndBosses_DeterministicPerHandle()
    {
        Assert.Equal(BreakPointUi.MobScale(1, 0xFFFF8123),
            BreakPointUi.MobScale(1, 0xFFFF8123));
        for (uint h = 0xFFFF8000; h < 0xFFFF8100; h++)
        {
            Assert.InRange(BreakPointUi.MobScale(1, h), 0.92f, 1.081f);
            Assert.InRange(BreakPointUi.MobScale(2, h), 1.12f, 1.181f);
            Assert.InRange(BreakPointUi.MobScale(3, h), 1.25f, 1.301f);
        }
        Assert.True(BreakPointUi.MobScale(3, 7) > BreakPointUi.MobScale(1, 7));
        var distinct = Enumerable.Range(0, 64)
            .Select(i => BreakPointUi.MobScale(1, (uint)(0xFFFF8000 + i)))
            .Distinct().Count();
        Assert.True(distinct > 10);                        // vraie variété
    }

    [Fact]
    public void WeakTips_AndBarLabel_HaveFrenchEntries()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? tsv = null;
        while (dir != null && tsv == null)
        {
            var c = Path.Combine(dir.FullName, "godot_project", "gamedata",
                "ui_text_en_fr.tsv");
            if (File.Exists(c)) tsv = c;
            dir = dir.Parent;
        }
        Assert.NotNull(tsv);
        var keys = File.ReadLines(tsv!).Select(l => l.Split('\t'))
            .Where(p => p.Length >= 2).GroupBy(p => p[0]).ToDictionary(g => g.Key, g => g.Last()[1]);
        foreach (var e in BreakPointUi.ElementNames)
            Assert.True(keys.ContainsKey($"Weak to {e}"), $"manque FR : Weak to {e}");
        Assert.True(keys.ContainsKey("Break Point"));
        Assert.Equal("Faiblesse : Entaille", keys["Weak to Slash"]);
    }

    // ---- c_monster / paquets ------------------------------------------

    private static string? ServerMonsterIni()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var c = Path.Combine(dir.FullName, "eden_server", "server_us",
                "Data", "db", "S_Monster.ini");
            if (File.Exists(c)) return c;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void MonsterTable_ReadsMaskAndPatience_FromRealData()
    {
        var path = ServerMonsterIni();
        if (path == null) return;                       // dépôt partiel
        var t = MonsterTable.Load(path);
        var herak = t[58471];
        Assert.Equal(0b100001, herak.BpWeakMask);
        Assert.Equal(821, herak.Patience);
        Assert.True(herak.CanBreak);
        Assert.Equal(2, t[60197].MonsterType);          // Archangel of Sin (élite)
        Assert.False(t[56001].CanBreak);                // champignon commun
        Assert.Equal(100, t[56001].Patience);
    }

    [Fact]
    public void SeeMob_MaxPatienceField_AndAttr13_RoundTrip()
    {
        var see = new ZoneSeeMob { Unk4C = 821, Hp = 10, MaxHp = 10 };
        var w = new EdenClient.Net.BinWriter();
        see.Serialize(w);
        var back = new ZoneSeeMob();
        back.Deserialize(new EdenClient.Net.BinReader(w.ToArray()));
        Assert.Equal(821, back.Unk4C);

        var attr = new ZoneUpdateAttr
        { Handle = 5, AttrId = 0x13, Current = -42, Max = 0 };
        var w2 = new EdenClient.Net.BinWriter();
        attr.Serialize(w2);
        var b2 = new ZoneUpdateAttr();
        b2.Deserialize(new EdenClient.Net.BinReader(w2.ToArray()));
        Assert.Equal(0x13, b2.AttrId);
        Assert.Equal(-42, b2.Current);
    }
}
