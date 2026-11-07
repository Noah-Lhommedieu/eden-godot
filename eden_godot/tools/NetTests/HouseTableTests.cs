using EdenClient.Data.Tables;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// <see cref="HouseTable"/> — c_buliding.ini (S_Buliding natif :
/// tid → code/type/level/prev/npc_item) + 4PHouse.ini (code → modèle
/// <c>house/model/*.nif</c>). Chaînage hall :
/// <see cref="HouseTable.ResolveHallBuilding"/> = même règle que
/// <c>SpawnFamilyHallNpcsAsync</c> serveur (prev→next jusqu'à
/// level ≤ niveau famille) — prouvé live : famille niv 5 → bld 3.
/// </summary>
[Collection("SharedResolver")]
public class HouseTableTests
{
    private static string? FindRawAssets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var ra = Path.Combine(dir.FullName, "raw_assets");
            if (Directory.Exists(ra)) return ra;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>Le test dépend de <c>TextResolver.ResolveFile</c> —
    /// état partagé muté par d'autres classes en parallèle. Bind +
    /// purge du cache statique (un Ensure passé avec un mauvais
    /// résolveur figeait des dicos vides).</summary>
    private static bool Bind()
    {
        var root = FindRawAssets();
        if (root == null) return false;
        EdenClient.Data.TextResolver.ResolveFile =
            IniTableTests.CaseInsensitiveResolver(root);
        HouseTable.ResetForTests();
        return true;
    }

    [Fact]
    public void RealFile_Building_Parses()
    {
        if (!Bind()) return;
        var b = HouseTable.GetBuilding(101);
        Assert.NotNull(b);
        // Contrat 35081 → tid 101 = H01001 (ferme, type 4 production).
        Assert.Equal("H01001", b.Code);
        Assert.Equal(4, b.Type);
        Assert.Equal(35002, b.NpcItemTid);
    }

    [Fact]
    public void RealFile_House_Parses()
    {
        if (!Bind()) return;
        var h = HouseTable.GetHouse("H01001");
        Assert.NotNull(h);
        Assert.Equal("h010", h.Model);
        Assert.Equal("h01001", h.Texture);
    }

    [Fact]
    public void HallBuilding_ChainByGuildLevel()
    {
        if (!Bind()) return;
        // Base S501 = tid 1 (niveau 1) ; niveau 5 → échelon 3
        // (preuve live : spawn npcItem du bld 3).
        Assert.Equal(1, HouseTable.ResolveHallBuilding(501, 1)?.Tid);
        Assert.Equal(3, HouseTable.ResolveHallBuilding(501, 5)?.Tid);
        // S502 part de la base 51.
        Assert.Equal(51, HouseTable.ResolveHallBuilding(502, 1)?.Tid);
    }
}
