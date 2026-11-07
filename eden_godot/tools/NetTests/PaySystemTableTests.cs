using EdenClient.Data.Tables;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// <see cref="PaySystemTable"/> — c_paysystem.ini 18 colonnes +
/// award_cmd (GiveItem/ChooseItem/GiveGold) — même mapping que le
/// parser serveur PaySystemParser (4 triplets de coûts cols 5-16,
/// award col 17).
/// </summary>
public class PaySystemTableTests
{
    private static string? FindRawAssets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "raw_assets");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void RealFile_CPaysystem_Parses()
    {
        var root = FindRawAssets();
        if (root == null) return; // assets absents — ignoré
        var db = Path.Combine(root, "data/db");
        var list = PaySystemTable.Load(
            Path.Combine(db, "c_paysystem.ini"),
            Path.Combine(db, "t_paysystem.ini"));
        Assert.NotEmpty(list);
        // Le 1er événement connu a des coûts non vides et un award.
        var ev = list[0];
        Assert.True(ev.Id > 0);
        Assert.True(ev.Type > 0);
        Assert.Contains(ev.Cost, c => c.ItemId > 0);
    }

    [Fact]
    public void AwardCmd_ParsesGiveAndChoose()
    {
        var root = FindRawAssets();
        if (root == null) return;
        var list = PaySystemTable.Load(
            Path.Combine(root, "data/db/c_paysystem.ini"));
        // Au moins un événement avec GiveItem ou ChooseItem décodé.
        Assert.Contains(list,
            e => e.GiveItems.Count + e.ChooseItems.Count
                 + e.GiveGold > 0);
    }
}
