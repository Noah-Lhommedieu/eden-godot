using Eden.Core.Logging;

namespace Eden.Tests;

public class LogThrottleTests
{
    [Fact]
    public void FirstCallWrites_ThenBlockedWithinPeriod_AndKeysAreIndependent()
    {
        var k = "t-" + Guid.NewGuid();
        Assert.True(LogThrottle.Allow(k, 60));
        Assert.False(LogThrottle.Allow(k, 60));
        Assert.True(LogThrottle.Allow(k + "-autre", 60));
    }
}
