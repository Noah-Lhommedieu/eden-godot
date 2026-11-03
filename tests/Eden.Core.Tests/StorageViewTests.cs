using EdenClient.Data.Tables;
using EdenClient.Protocol;
using EdenClient.Windows;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Vues banque / néobank / coffre famille (P1).</summary>
public class StorageViewTests
{
    private static ItemTable.ItemDef Def(int maxStack, int equip) =>
        new() { Id = 1, MaxStack = maxStack, EquipLoc = equip };

    [Fact]
    public void StackCount_Stackable_UsesDurabilityOrAmount()
    {
        var it = new InventorySlot(0, 3, 1, 75, 75, 0, 20);
        Assert.Equal(75, StorageView.StackCount(it, Def(99, 0)));
        // équipable : jamais de stack, Amount borné ≥ 1
        Assert.Equal(1, StorageView.StackCount(it with { Amount = 0 },
            Def(1, 7)));
        // def inconnue : Amount seul
        Assert.Equal(20, StorageView.StackCount(it, null));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("ID: 30240", true)]
    [InlineData("id:30241", false)]
    [InlineData("30240", true)]
    [InlineData("épée", true)]
    [InlineData("ÉPÉE", true)]
    [InlineData("bouclier", false)]
    public void Matches_NameOrId(string filter, bool expected)
    {
        var it = new InventorySlot(0, 0, 30240, 1, 1, 0, 1);
        Assert.Equal(expected, StorageView.Matches(filter, it,
            id => id == 30240 ? "Épée rouillée" : null));
    }

    [Fact]
    public void Page_ClampsAndSlices()
    {
        var (first, count, pages, page) = StorageView.Page(40, 16, 99, true);
        Assert.Equal(3, pages);
        Assert.Equal(2, page);
        Assert.Equal(32, first);
        Assert.Equal(8, count);
        var scroll = StorageView.Page(40, 16, 5, false);
        Assert.Equal((0, 40, 1, 0), scroll);
        Assert.Equal(1, StorageView.Page(0, 16, 0, true).Pages);
    }

    [Fact]
    public void ByLoc_LastWins_AndCell()
    {
        var a = new InventorySlot(0, 5, 1, 1, 1, 0, 1);
        var b = new InventorySlot(0, 5, 2, 1, 1, 0, 1);
        Assert.Equal(2, StorageView.ByLoc(new[] { a, b })[5].ItemId);
        Assert.Equal((3, 2), StorageView.Cell(23, 10));
    }

    [Fact]
    public void BankActions_WireShape()
    {
        var d = ZoneBankActions.DepositRequest(1, 7);
        Assert.Equal((short)0, d.SrcType);
        Assert.Equal((short)4, d.DstType);
        var m = ZoneBankActions.MoveRequest(3, 0, 9, 1);
        Assert.Equal((short)9, m.DstSlot);
        Assert.Equal((short)1, m.DstIndex);
    }
}
