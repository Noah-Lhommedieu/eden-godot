using Eden.Zone.Features.Admin;

namespace Eden.Tests.Admin;

/// <summary>Canal interne site → jeu : signature HMAC et fenêtre de temps (D5/D8).</summary>
public class AdminHttpSignatureTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Fact] public void ValidSignature_Passes()
    {
        var t = Now.ToUnixTimeSeconds().ToString(); var sig = AdminHttpHost.Sign("s3cret", t, "{\"a\":1}");
        Assert.True(AdminHttpHost.Verify("s3cret", t, "{\"a\":1}", sig, Now));
    }

    [Theory]
    [InlineData("autre-secret", "{\"a\":1}", 0)] [InlineData("s3cret", "{\"a\":2}", 0)] [InlineData("s3cret", "{\"a\":1}", 61)] [InlineData("s3cret", "{\"a\":1}", -61)]
    public void Tampering_OrReplayTooOld_IsRejected(string secret, string body, int skew)
    {
        var tSigned = Now.ToUnixTimeSeconds().ToString(); var sig = AdminHttpHost.Sign("s3cret", tSigned, "{\"a\":1}");
        Assert.False(AdminHttpHost.Verify(secret, tSigned, body, sig, Now.AddSeconds(skew)));
    }

    [Fact] public void MissingOrMalformedParts_AreRejected()
    {
        Assert.False(AdminHttpHost.Verify("s", "", "b", "x", Now));
        Assert.False(AdminHttpHost.Verify("s", "abc", "b", "x", Now));
    }
}
