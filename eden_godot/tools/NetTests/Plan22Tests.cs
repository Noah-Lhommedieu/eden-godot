using EdenClient.Game;
using EdenClient.Ui;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Plan 22 étape B : fusion costume/visuel, resize buffs, pan preview.</summary>
public class Plan22Tests
{
    [Theory]
    [InlineData(3, 2)]   // kind%3==0 → cont2
    [InlineData(5, 3)]   // kind%3==2 → cont3
    [InlineData(4, 4)]   // kind%3==1 → cont4
    public void Costume_PageForKind(int kind, int container) =>
        Assert.Equal(container, CostumeVisual.ContainerForKind(kind));

    /// <summary>Archétype c_class → rangée profil (licence d'armure) :
    /// 1 戰士/騎士/聖騎士 = heavy → « Armor »/conteneur 4 ; 2 light →
    /// « Leather »/3 ; 3 cloth → « Cloth »/2 ; inconnu → repli Cloth.
    /// Même mapping conteneur que ContainerForKind (kind % 3).</summary>
    [Theory]
    [InlineData(1, "Armor", 4)]
    [InlineData(2, "Leather", 3)]
    [InlineData(3, "Cloth", 2)]
    [InlineData(0, "Cloth", 2)]
    [InlineData(99, "Cloth", 2)]
    public void Profile_RowForArchetype(int arch, string row, int cont)
    {
        var r = CostumeVisual.RowForArchetype(arch);
        Assert.Equal(row, r.Row);
        Assert.Equal(cont, r.Container);
    }

    [Fact]
    public void Costume_OverridesSameLocOnly_WeaponsUntouched()
    {
        var worn = new Dictionary<int, int> { { 2, 100 }, { 1, 101 }, { 7, 900 } };
        var kinds = new Dictionary<int, int> { { 100, 3 }, { 101, 3 }, { 900, 21 }, { 555, 3 }, { 556, 4 } };
        var costumes = new Dictionary<(int, int), int>
        {
            { (2, 2), 555 },   // page poids 0, body
            { (4, 1), 556 },   // mauvaise page pour une tête de poids 0
        };
        var r = CostumeVisual.Merge(worn,
            (c, l) => costumes.GetValueOrDefault((c, l)),
            id => kinds.GetValueOrDefault(id, -1));
        Assert.Equal(555, r[2]);
        Assert.Equal(101, r[1]);
        Assert.Equal(900, r[7]);
        Assert.Equal(100, worn[2]);   // entrée non mutée
    }

    [Fact]
    public void Costume_EmptyLocUsesBodyWeight()
    {
        var worn = new Dictionary<int, int> { { 2, 100 } };
        var r = CostumeVisual.Merge(worn,
            (c, l) => c == 2 && l == 4 ? 777 : 0,
            id => id == 100 ? 3 : -1);
        Assert.Equal(777, r[4]);
        Assert.False(CostumeVisual.Merge(new Dictionary<int, int>(),
            (_, _) => 1, _ => -1).Any());
    }

    [Fact]
    public void GridResize_ColsAndRowsClamped()
    {
        Assert.Equal(1, GridResizeMath.Cols(500, 600, 34, 2, 15));
        Assert.Equal(15, GridResizeMath.Cols(500, -9999, 34, 2, 15));
        Assert.Equal(3, GridResizeMath.Cols(500, 500 - 3 * 36 + 2, 34, 2, 15));
        Assert.Equal(1, GridResizeMath.Rows(100, 90, 34, 2, 6));
        Assert.Equal(6, GridResizeMath.Rows(100, 9999, 34, 2, 6));
        Assert.Equal(2, GridResizeMath.Rows(100, 100 + 2 * 36 - 2, 34, 2, 6));
    }

    [Fact]
    public void PreviewPan_UpOnDragUp_Clamped()
    {
        Assert.True(PreviewPanMath.Apply(0, -10) > 0);
        Assert.True(PreviewPanMath.Apply(0, 10) < 0);
        Assert.Equal(PreviewPanMath.Limit, PreviewPanMath.Apply(0, -1e6f));
        Assert.Equal(-PreviewPanMath.Limit, PreviewPanMath.Apply(0, 1e6f));
    }
}
