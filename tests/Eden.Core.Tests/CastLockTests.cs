using EdenClient.Combat;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Verrou de cast client : ne reste jamais posé sans réponse
/// serveur (régression « plus de sorts après un combat/TP »).</summary>
public class CastLockTests
{
    [Fact]
    public void NeverRequested_NeverExpires()
    {
        var l = new CastLock();
        Assert.False(l.Expired(1_000_000));
    }

    [Fact]
    public void RequestWithoutAnswer_ExpiresAfterGrace()
    {
        var l = new CastLock();
        l.Requested(10_000);
        Assert.False(l.Expired(10_000 + CastLock.RequestGraceMs - 1));
        Assert.True(l.Expired(10_000 + CastLock.RequestGraceMs));
    }

    [Fact]
    public void InstantAckedButNoImpact_ExpiresAfterResolveGrace()
    {
        // Mob tué entre le 279 et l'impact : jamais de 281.
        var l = new CastLock();
        l.Requested(0);
        l.Acknowledged(200, 0);
        Assert.False(l.Expired(200 + CastLock.ResolveGraceMs - 1));
        Assert.True(l.Expired(200 + CastLock.ResolveGraceMs));
    }

    [Fact]
    public void TimedCast_WaitsCastBarThenGrace()
    {
        var l = new CastLock();
        l.Acknowledged(0, 30);   // 3 s de barre
        Assert.False(l.Expired(3000 + CastLock.ResolveGraceMs - 1));
        Assert.True(l.Expired(3000 + CastLock.ResolveGraceMs));
    }

    [Fact]
    public void Released_ClearsDeadline()
    {
        var l = new CastLock();
        l.Requested(0);
        l.Released();
        Assert.False(l.Expired(1_000_000));
    }
}
