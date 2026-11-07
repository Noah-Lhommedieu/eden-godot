using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Eden.Zone;

namespace Eden.Tests;

/// <summary>Règles de réanimation du joueur : types de revive, perte
/// d'exp bornée, détection des items de revive.</summary>
public sealed class ReviveRulesTests
{
    private static EnchantService Enchants(int id, int command)
    {
        var raw = new string[45];
        Array.Fill(raw, "");
        raw[0] = id.ToString();
        raw[9] = command.ToString();
        raw[10] = "100"; raw[11] = "100";
        return new EnchantService(new[]
        {
            new EnchantParser.EnchantDef { Id = id, Enabled = true, Raw = raw },
        });
    }

    /// <summary>Seuls 0 (sauvegarde), 1 (sur place), 5 (item) passent.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(2, false)]
    [InlineData(-1, false)]
    [InlineData(99, false)]
    public void KnownTypes(int type, bool known) =>
        Assert.Equal(known, ReviveRules.IsKnownType(type));

    /// <summary>L'item évite la pénalité ; seul le type 0 téléporte.</summary>
    [Fact]
    public void ItemRevive_NoPenalty_NoTeleport()
    {
        Assert.False(ReviveRules.AppliesExpLoss(ReviveRules.TypeItem));
        Assert.True(ReviveRules.AppliesExpLoss(ReviveRules.TypeInPlace));
        Assert.True(ReviveRules.AppliesExpLoss(ReviveRules.TypeSavePoint));
        Assert.True(ReviveRules.Teleports(ReviveRules.TypeSavePoint));
        Assert.False(ReviveRules.Teleports(ReviveRules.TypeInPlace));
        Assert.False(ReviveRules.Teleports(ReviveRules.TypeItem));
    }

    /// <summary>10 % du palier, plafonné à l'exp courante, jamais négatif.</summary>
    [Theory]
    [InlineData(5000, 10000, 1000)]
    [InlineData(300, 10000, 300)]
    [InlineData(0, 10000, 0)]
    [InlineData(-5, 10000, 0)]
    [InlineData(5000, 0, 0)]
    public void ExpLoss_BoundedByCurrentExp(long exp, long need, long loss) =>
        Assert.Equal(loss, ReviveRules.ExpLoss(exp, need));

    /// <summary>Un enchant d'usage Revive (1812) ou RevivePlus (1811)
    /// fait de l'item un item de revive ; un soin non.</summary>
    [Theory]
    [InlineData(1812, true)]
    [InlineData(1811, true)]
    [InlineData(1001, false)]
    public void ReviveEnchant_Detected(int command, bool expected)
    {
        var es = Enchants(70001, command);
        Assert.Equal(expected, ReviveRules.IsReviveEnchant(es, 70001));
        Assert.Equal(expected,
            ReviveRules.IsReviveItem(es, 12345, 70001));
    }

    /// <summary>Sans enchant d'usage, ce n'est pas un item de revive.</summary>
    [Fact]
    public void NoEnchant_NotReviveItem() =>
        Assert.False(ReviveRules.IsReviveItem(Enchants(1, 1812), 12345, 0));

    /// <summary>Violette (Locus) = sur place ; verte = point de sauvegarde ;
    /// aucune ne coûte d'exp ; ids issus de c_item/t_itemmall.</summary>
    [Fact]
    public void Leaves_MapToTypes()
    {
        Assert.Contains(40036, ReviveRules.LeafIdsFor(ReviveRules.TypeItem)!);
        Assert.Contains(41468, ReviveRules.LeafIdsFor(ReviveRules.TypeItem)!);
        Assert.Contains(40035, ReviveRules.LeafIdsFor(ReviveRules.TypeLifeLeaf)!);
        Assert.Contains(40157, ReviveRules.LeafIdsFor(ReviveRules.TypeLifeLeaf)!);
        Assert.Null(ReviveRules.LeafIdsFor(ReviveRules.TypeInPlace));
        Assert.False(ReviveRules.AppliesExpLoss(ReviveRules.TypeLifeLeaf));
        Assert.True(ReviveRules.Teleports(ReviveRules.TypeLifeLeaf));
        Assert.False(ReviveRules.Teleports(ReviveRules.TypeItem));
        Assert.True(ReviveRules.IsLeaf(40035));
        Assert.False(ReviveRules.IsLeaf(40037));
    }

    /// <summary>En donjon, aucune pénalité d'exp, pour tous les types.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(6)]
    public void Dungeon_NeverLosesExp(int type)
    {
        Assert.False(ReviveRules.AppliesExpLoss(type, inDungeon: true));
        Assert.Equal(ReviveRules.AppliesExpLoss(type),
            ReviveRules.AppliesExpLoss(type, inDungeon: false));
    }
}
