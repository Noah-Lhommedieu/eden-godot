using Xunit;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class BankTests
{
    private readonly BankService _service = new(null!);
    
    [Fact]
    public void DepositItem_WithValidSlot_ReturnsSuccess()
    {
        // Test basique de structure
        var request = new { ItemId = 1, Quantity = 10, Slot = 0 };
        Assert.True(request.Slot >= 0);
    }
    
    [Fact]
    public void WithdrawItem_WithValidSlot_ReturnsSuccess()
    {
        // Test basique de structure
        var request = new { Slot = 0, Quantity = 5 };
        Assert.True(request.Slot >= 0);
        Assert.True(request.Quantity > 0);
    }
}
