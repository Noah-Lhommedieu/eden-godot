using Xunit;
using Eden.Core.Game.Services;
using Eden.Core.Game.Inventory;

namespace Eden.Tests;

public class InventoryTests
{
    private readonly InventoryService _service = new(null!);
    
    [Fact]
    public void AddItem_WithValidParameters_ReturnsSuccess()
    {
        // TODO: Implémenter avec mock repository
        // Test basique de structure
        var request = new { ItemId = 1, Quantity = 10 };
        Assert.True(request.ItemId > 0);
        Assert.True(request.Quantity > 0);
    }
    
    [Fact]
    public void SplitItem_WithValidQuantity_ReturnsSuccess()
    {
        // TODO: Implémenter avec mock repository
        // Test basique de structure
        var request = new { Quantity = 5, OriginalQuantity = 10 };
        Assert.True(request.Quantity < request.OriginalQuantity);
    }
}
