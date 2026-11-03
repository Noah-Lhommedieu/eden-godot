using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Les 15 fsm V7 : copie fr-full corrompue (queue non framée, pas de
/// props) vs original EDENFR intact (GRL/GRS/OBJ). Skippé si
/// <c>~/Téléchargements/EDENFR/extracted</c> ou le corpus manque.
/// </summary>
public class V7PropsTests
{
    private static readonly string[] V7 =
    {
        "s002", "s003", "s004", "s005", "s010", "s011", "s021", "s030",
        "s032", "s051", "s054", "s119", "s225", "s307", "s501",
    };

    private static string? Extracted()
    {
        var p = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Téléchargements", "EDENFR", "extracted");
        return Directory.Exists(p) ? p : null;
    }

    private static string? Corpus()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "client_assets");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    [Fact]
    public void Extracted_V7_fsm_are_fully_framed_with_props()
    {
        var ext = Extracted();
        if (ext == null) return;
        foreach (var m in V7)
        {
            var f = FsmFile.Load(Path.Combine(ext, "map/model", m, m + ".fsm"));
            Assert.Equal(0, f.TrailingBytes);
            Assert.True(f.SceneObjects.Count > 300, m + " OBJ");
            Assert.True(f.GrassInstances.Count > 1000, m + " GRS");
        }
    }

    [Fact]
    public void S002_counts_match_oracle()
    {
        var ext = Extracted();
        if (ext == null) return;
        var f = FsmFile.Load(Path.Combine(ext, "map/model/s002/s002.fsm"));
        Assert.Equal(7, f.GrassTypes.Count);
        Assert.Equal(21597, f.GrassInstances.Count);
        Assert.Equal(2064, f.SceneObjects.Count);
    }

    [Fact]
    public void PickIntact_accepts_authentic_corpus_first()
    {
        var ext = Extracted();
        var cor = Corpus();
        if (ext == null || cor == null) return;
        var rel = "map/model/s002/s002.fsm";
        var picked = FsmSource.PickIntact(
            FsmSource.Candidates(new[] { cor, ext }, rel));
        Assert.NotNull(picked);
        // fr-full authentique : le premier candidat est déjà intact.
        Assert.Equal(0, FsmFile.Load(picked!).TrailingBytes);
        Assert.StartsWith(cor, picked);
    }
}
