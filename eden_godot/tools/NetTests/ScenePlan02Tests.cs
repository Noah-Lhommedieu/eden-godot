using EdenClient.Combat;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Scene;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Plan futur-feature/02 (client) — bulles 268, motion 495,
/// texte localisé du MonsterSay, mode cinéma (chronologie + verrous).
/// Logique pure : le rendu Godot (SpeechBubble3D/CinemaMode) est
/// vérifié en jeu.</summary>
public class ScenePlan02ClientTests
{
    // ---------------------------------------------------------- bulle

    [Fact]
    public void Wrap_BreaksAtSpaces_AndKeepsWordsWhole()
    {
        var w = SceneBubbleLogic.Wrap("Do you think the plan can proceed smoothly today", 20);
        foreach (var line in w.Split('\n'))
            Assert.True(line.Length <= 20, line);
        Assert.Equal("Do you think the plan can proceed smoothly today",
            w.Replace('\n', ' '));
    }

    [Fact]
    public void Wrap_LongWordAndCjk_HardSplit()
    {
        var w = SceneBubbleLogic.Wrap(new string('字', 50), 10);
        Assert.Equal(5, w.Split('\n').Length);
        Assert.All(w.Split('\n'), l => Assert.True(l.Length <= 10));
    }

    [Fact]
    public void Wrap_CapsLines_WithEllipsis()
    {
        var w = SceneBubbleLogic.Wrap(string.Join(' ',
            Enumerable.Repeat("bonjour", 60)), 12);
        var lines = w.Split('\n');
        Assert.Equal(SceneBubbleLogic.MaxLines, lines.Length);
        Assert.EndsWith("…", lines[^1]);
    }

    [Fact]
    public void Sanitize_CollapsesWhitespaceAndControls()
    {
        Assert.Equal("a b c", SceneBubbleLogic.Sanitize("  a\r\n b\t c  "));
        Assert.Equal("", SceneBubbleLogic.Sanitize(null));
        Assert.Equal("", SceneBubbleLogic.Wrap("   "));
    }

    [Fact]
    public void Duration_IsBounded_AndGrowsWithLength()
    {
        Assert.Equal(SceneBubbleLogic.MinSeconds, SceneBubbleLogic.Duration("hi"));
        Assert.Equal(SceneBubbleLogic.MaxSeconds,
            SceneBubbleLogic.Duration(new string('x', 500)));
        Assert.True(SceneBubbleLogic.Duration(new string('x', 60)) >
                    SceneBubbleLogic.Duration(new string('x', 30)));
    }

    // -------------------------------------------- texte localisé (DLG1)

    private static byte[] Blob(int id)
    {
        var b = new byte[8];
        "DLG1"u8.CopyTo(b);
        BitConverter.GetBytes(id).CopyTo(b, 4);
        return b;
    }

    [Fact]
    public void ResolveSayText_UsesLocalizedDialogue_WhenBlobCarriesId()
    {
        string? Fr(int id) => id == 730 ? "Bonne chance !" : null;
        Assert.Equal("Bonne chance !",
            SceneBubbleLogic.ResolveSayText("Good luck!", Blob(730), Fr));
        // id inconnu → texte serveur
        Assert.Equal("Good luck!",
            SceneBubbleLogic.ResolveSayText("Good luck!", Blob(9), Fr));
        // say joueur (blob vide) / item-link → texte serveur
        Assert.Equal("yo",
            SceneBubbleLogic.ResolveSayText("yo", [], Fr));
        Assert.Equal("yo", SceneBubbleLogic.ResolveSayText("yo",
            new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Fr));
        Assert.False(SceneBubbleLogic.TryDecodeDialogueBlob(null, out _));
        Assert.False(SceneBubbleLogic.TryDecodeDialogueBlob(
            "DLG1"u8.ToArray(), out _));      // trop court
    }

    /// <summary>Le 268 serveur (str + blob DLG1) se relit à l'identique.</summary>
    [Fact]
    public void ZoneClientSay_RoundTrip_WithDialogueBlob()
    {
        var w = new BinWriter();
        w.WriteInt(unchecked((int)0xFFFFAB01u));   // handle mob
        w.WriteString("Good luck!");
        w.WriteInt(8);
        w.WriteBytes(Blob(730));
        var m = new ZoneClientSay();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(0xFFFFAB01u, m.CharId);
        Assert.Equal("Good luck!", m.Message);
        Assert.True(SceneBubbleLogic.TryDecodeDialogueBlob(m.Blob, out var id));
        Assert.Equal(730, id);
    }

    // ------------------------------------------------------ motion 495

    private const string MotionSample =
        "[KEY_INFO]\n" +
        "DB0=m010,201,4,death01\n" +
        "DB1=m010,1021,4,attack01\n" +
        "DB2=m010,1011,3,hurt01\n" +
        "DB3=m010,401,5,shoot01\n" +
        "DB4=m010,301,3,magic01\n" +
        "DB5=m010,501,3,condition01\n" +
        "DB6=m010,51,3,trigger01\n" +
        "DB7=m010,52,4,trigger02\n" +
        "DB8=m010,151,6,move51\n" +
        "DB9=m010,101,8,move01\n" +
        "DB10=m010,3,3,stand03\n" +
        "DB11=m029,2,4,stand02\n" +
        "DB12=m010,404,5,shoot04\n";

    /// <summary>La convention de plage reproduit EXACTEMENT les lignes
    /// réelles de motion.ini pour les kfId concernés.</summary>
    [Theory]
    [InlineData(201, "death01")] [InlineData(1021, "attack01")]
    [InlineData(1011, "hurt01")] [InlineData(401, "shoot01")]
    [InlineData(301, "magic01")] [InlineData(501, "condition01")]
    [InlineData(51, "trigger01")] [InlineData(52, "trigger02")]
    [InlineData(151, "move51")] [InlineData(101, "move01")]
    [InlineData(3, "stand03")] [InlineData(404, "shoot04")]
    public void NameFromKfId_MatchesMotionIniConvention(int kf, string name)
    {
        var table = MotionTable.Parse(MotionSample);
        Assert.Equal(name, table.Lookup("m010", kf));
        Assert.Equal(name, MonsterActionAnim.NameFromKfId(kf));
    }

    [Fact]
    public void Candidates_ExactFirst_ThenConvention_NothingForZero()
    {
        var table = MotionTable.Parse(MotionSample);
        Assert.Equal(new[] { "magic01" },
            MonsterActionAnim.Candidates(table, "m010", 301));
        // modèle absent de la table → convention seule
        Assert.Equal(new[] { "stand01" },
            MonsterActionAnim.Candidates(table, "m999", 1));
        Assert.Empty(MonsterActionAnim.Candidates(table, "m010", 0));
        Assert.Empty(MonsterActionAnim.Candidates(null, null, -3));
        Assert.Empty(MonsterActionAnim.Candidates(null, null, 7777));
    }

    [Fact]
    public void ZoneClientPlayMonsterAction_Parses_HandleAndKf()
    {
        var w = new BinWriter();
        w.WriteInt(unchecked((int)0xFFFFAB02u));
        w.WriteShort(1011);
        var m = new ZoneClientPlayMonsterAction();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(unchecked((int)0xFFFFAB02u), m.Field1);
        Assert.Equal((short)1011, m.Field2);
    }

    // ---------------------------------------------------------- cinéma

    [Fact]
    public void Cinema_Lifecycle_BarsAndLock()
    {
        var c = new CinemaTimeline();
        var changes = 0;
        c.Changed += () => changes++;
        Assert.False(c.Active);
        Assert.Equal(0, c.BarFraction);

        c.Begin(2);
        Assert.True(c.Active);
        Assert.Equal(1, changes);
        c.Tick(CinemaTimeline.SlideSeconds / 2);
        Assert.InRange(c.BarFraction, 0.1, 0.9);       // entrée en cours
        c.Tick(CinemaTimeline.SlideSeconds);
        Assert.Equal(1.0, c.BarFraction, 6);           // pleines
        c.Tick(1.0);                                    // t=1,75 < 2
        Assert.Equal(1.0, c.BarFraction, 6);
        Assert.False(c.Tick(0.4));                      // t=2,15 : sortie
        Assert.InRange(c.BarFraction, 0.01, 0.99);
        Assert.True(c.Tick(1.0));                       // fin
        Assert.False(c.Active);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Cinema_Skip_JumpsToExit()
    {
        var c = new CinemaTimeline();
        c.Begin(100);
        c.Tick(1);
        c.Skip();
        Assert.True(c.Tick(CinemaTimeline.SlideSeconds + 0.01));
        Assert.False(c.Active);
        c.Skip();                                       // inactif : sans effet
        Assert.False(c.Active);
    }

    [Fact]
    public void Cinema_LocksPlayerInput_ViaPlayerStateLocks()
    {
        var c = CinemaTimeline.Shared;
        c.Reset();
        PlayerStateLocks.Reset();
        try
        {
            Assert.False(PlayerStateLocks.NoMove);
            c.Begin(5);
            Assert.True(PlayerStateLocks.NoMove);
            Assert.True(PlayerStateLocks.NoAttack);
            Assert.True(PlayerStateLocks.NoAnySkill);
            Assert.True(PlayerStateLocks.NoUseItem);
            c.Reset();
            Assert.False(PlayerStateLocks.NoMove);
            Assert.False(PlayerStateLocks.NoAnySkill);
        }
        finally { c.Reset(); }
    }

    [Fact]
    public void Cinema_Begin_ClampsLength()
    {
        var c = new CinemaTimeline();
        c.Begin(0);                       // borné à 1 s
        c.Tick(CinemaTimeline.SlideSeconds);
        Assert.True(c.Active);
        c.Tick(1 + CinemaTimeline.SlideSeconds);
        Assert.False(c.Active);
    }
}
