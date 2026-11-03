using EdenClient.Game;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>KB-11 : plusieurs codes 4pequip partagent un mesh « #X2nn »
/// avec des skins ASnn différents ; la tenue de classe doit résoudre sa
/// ligne exacte par CODE (RowForSuffix = première variante, repli).</summary>
public class ClassOutfitRowTests
{
    [Fact]
    public void ClassOutfitRow_IsCodeExact_RowForSuffixIsFirstVariant()
    {
        const string letter = "g";
        var map = CharModelSpec.LoadEquipMap(letter);
        if (map.Count == 0) return;                  // pas de raw_assets
        var differing = 0;
        for (var cls = 1; cls <= 61; cls++)
        {
            var closet = CharModelSpec.ClassCloset(cls);
            for (var style = 0; style < closet.Count; style++)
            {
                var row = CharModelSpec.ClassOutfitRow(letter, cls, style);
                if (row == null) continue;
                var e = closet[style];
                var own = map.TryGetValue(e.Female, out var f) &&
                          f.Mesh.StartsWith('#') ? f : map[e.Male];
                Assert.Equal(own.Skin, row.Skin);          // exact par code
                var first = CharModelSpec.RowForSuffix(letter, row.Mesh[2..]);
                if (first != null && first.Skin != row.Skin) differing++;
            }
        }
        // Preuve du défaut évité : des tenues de classe partagent un mesh
        // dont la 1re variante n'est PAS la leur.
        Assert.True(differing > 0);
    }

    /// <summary>KB-17 : la coiffe de classe (code série H, mesh externe)
    /// est résolue séparément de la tenue corps.</summary>
    [Fact]
    public void ClassHeadRow_ResolvesExternalHeadMesh()
    {
        var map = CharModelSpec.LoadEquipMap("g");
        if (map.Count == 0) return;
        var heads = 0;
        for (var cls = 1; cls <= 61; cls++)
            for (var st = 0; st < CharModelSpec.ClassCloset(cls).Count; st++)
            {
                var h = CharModelSpec.ClassHeadRow("g", cls, st);
                if (h == null) continue;
                heads++;
                Assert.False(h.Mesh.StartsWith('#'));
            }
        Assert.True(heads > 0);
    }
}
