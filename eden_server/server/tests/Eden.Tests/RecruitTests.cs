using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Lot « recrutement d'instance » (NCID 220/553 + 163) — preuves :
/// wire 220 désasm <c>Deserialize</c> zone @0x0827c210 ; règles de
/// match <c>InitRecruitRule</c> mission @0x080cda70 ; parsing
/// préfixe-entier = extraction <c>istream &gt;&gt; u32</c> du
/// Deserialize de S_Node natif (col 40 = CharLimit, 41 = min level).
/// </summary>
public class RecruitTests
{
    // ---------------- wire 220 ----------------

    /// <summary>Trame add 3 nœuds : [3][35][100][284][role 1][0].</summary>
    [Fact]
    public void Wire220_Parse_Add()
    {
        var payload = new byte[]
        {
            3, 0,             // s16 count = 3
            35, 0, 100, 0, 28, 1, // nœuds 35, 100, 284
            1, 0,             // s16 rôle = 1 (heal)
            0,                // u8 flag = 0 (add)
        };
        Assert.True(RecruitPackets.TryParse(payload, out var req));
        Assert.Equal(new short[] { 35, 100, 284 }, req.Nodes);
        Assert.Equal(1, req.Role);
        Assert.Equal(0, req.Flag);
    }

    /// <summary>Trame leave : count peut être 0 (retrait global).</summary>
    [Fact]
    public void Wire220_Parse_LeaveEmpty()
    {
        var payload = new byte[] { 0, 0, 2, 0, 1 };
        Assert.True(RecruitPackets.TryParse(payload, out var req));
        Assert.Empty(req.Nodes);
        Assert.Equal(2, req.Role);
        Assert.Equal(1, req.Flag);
    }

    /// <summary>Trames tronquées / count négatif rejetées — le
    /// dispatch ne doit jamais lire hors bornes.</summary>
    [Fact]
    public void Wire220_Parse_RejectsTruncated()
    {
        Assert.False(RecruitPackets.TryParse(
            new byte[] { 3, 0, 35, 0, 1, 0, 0 }, out _));
        Assert.False(RecruitPackets.TryParse(
            new byte[] { 255, 255, 0, 0, 0, 0, 0 }, out _)); // -1
        Assert.False(RecruitPackets.TryParse(new byte[] { 0 }, out _));
    }

    // ---------------- RecruitBoard ----------------

    /// <summary>Insertion triée par rôle : le multimap natif ordonne
    /// par clé (rôle) puis insertion — TryMatch doit prendre le
    /// « premier » de chaque rôle.</summary>
    [Fact]
    public void Board_OrderedByRole()
    {
        var b = new RecruitBoard();
        b.Add(10, 2, new short[] { 35 });
        b.Add(11, 0, new short[] { 35 });
        b.Add(12, 1, new short[] { 35 });
        var m = b.TryMatch(35, 3);
        Assert.NotNull(m);
        Assert.Equal(3, m!.Count);
        Assert.Equal(11, m[0]); // rôle 0 en premier (leader natif)
        Assert.Equal(12, m[1]); // rôle 1 ensuite
        Assert.Equal(10, m[2]); // slot flex → un rôle restant
    }

    /// <summary>Taille 3 : la règle {1,1,0,0,0,1,0} exige un rôle 0,
    /// un rôle 1, et un flex — trois healers (rôle 1) ne matchent pas
    /// (gate ∃ rôle-0).</summary>
    [Fact]
    public void Board_Rule3_RequiresTankAndHealer()
    {
        var b = new RecruitBoard();
        b.Add(10, 1, new short[] { 35 });
        b.Add(11, 1, new short[] { 35 });
        b.Add(12, 1, new short[] { 35 });
        Assert.Null(b.TryMatch(35, 3));
        b.Add(13, 0, new short[] { 35 });
        var m = b.TryMatch(35, 3);
        Assert.NotNull(m);
        Assert.Equal(13, m![0]);
    }

    /// <summary>Taille 5 : règle {1,1,0,0,0,1,2} = 1 tank + 1 heal +
    /// 3 flex — un groupe complet est formé, les picks quittent
    /// toutes leurs files.</summary>
    [Fact]
    public void Board_Rule5_MatchAndPurge()
    {
        var b = new RecruitBoard();
        b.Add(10, 0, new short[] { 35, 100 });
        b.Add(11, 1, new short[] { 35 });
        b.Add(12, 2, new short[] { 35 });
        b.Add(13, 2, new short[] { 35 });
        b.Add(14, 2, new short[] { 100 });
        b.Add(15, 2, new short[] { 35 });
        var m = b.TryMatch(35, 5);
        Assert.NotNull(m);
        Assert.Equal(5, m!.Count);
        // 10 était inscrit sur 35 ET 100 → retiré partout.
        Assert.Null(b.NodesOf(10));
        Assert.Equal(0, b.Count(35));
        Assert.Equal(1, b.Count(100)); // il reste 14
    }

    /// <summary>Taille 8 : règle {1,1,0,0,1,1,4}.</summary>
    [Fact]
    public void Board_Rule8()
    {
        var b = new RecruitBoard();
        b.Add(10, 0, new short[] { 35 });
        b.Add(11, 1, new short[] { 35 });
        for (var i = 0; i < 6; i++)
            b.Add(20 + i, 2, new short[] { 35 });
        var m = b.TryMatch(35, 8);
        Assert.NotNull(m);
        Assert.Equal(8, m!.Count);
    }

    /// <summary>Pas de règle pour CharLimit=2 → pas de match (le
    /// natif ne match que les tailles règlementées).</summary>
    [Fact]
    public void Board_NoRuleForSize2()
    {
        var b = new RecruitBoard();
        b.Add(10, 0, new short[] { 35 });
        b.Add(11, 1, new short[] { 35 });
        Assert.Null(b.TryMatch(35, 2));
        Assert.Equal(2, b.Count(35)); // les inscrits restent en file
    }

    /// <summary>Validate écarte les entrées obsolètes (perso hors
    /// ligne / déjà sorti) — le match reprend sans elles.</summary>
    [Fact]
    public void Board_ValidateFiltersStale()
    {
        var b = new RecruitBoard();
        b.Add(10, 0, new short[] { 35 });
        b.Add(11, 0, new short[] { 35 });
        b.Add(12, 1, new short[] { 35 });
        b.Add(13, 1, new short[] { 35 });
        // 10 est morte — il faut un rôle 0 : le match attend 11.
        var m = b.TryMatch(35, 3, cid => cid != 10);
        Assert.NotNull(m);
        Assert.DoesNotContain(10, m!);
        Assert.Equal(11, m[0]);
    }

    /// <summary>Remove retourne les nœuds touchés — un résultat
    /// 553 [1][0] natif est émis par nœud retiré.</summary>
    [Fact]
    public void Board_RemoveReturnsTouchedNodes()
    {
        var b = new RecruitBoard();
        b.Add(10, 0, new short[] { 35, 100 });
        b.Add(11, 1, new short[] { 35 });
        var removed = b.Remove(10);
        Assert.Equal(new short[] { 35, 100 }, removed);
        Assert.Empty(b.Remove(10));
        Assert.Equal(1, b.Count(35));
    }

    // ---------------- NodeIntPrefix ----------------

    /// <summary>Parse préfixe-entier = <c>istream &gt;&gt; u32</c>
    /// natif : '96'→96, '2.35'→2, '0.59'→0, ''→0, 'abc'→0.</summary>
    [Theory]
    [InlineData("96", 96)]
    [InlineData("2.35", 2)]
    [InlineData("0.59", 0)]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData("120x", 120)]
    public void NodeIntPrefix_MatchesNativeRead(string raw, int want)
    {
        var row = new string[45];
        Array.Fill(row, "");
        row[40] = raw;
        Assert.Equal(want, GameDataParsers.NodeIntPrefix(row, 40));
    }
}
