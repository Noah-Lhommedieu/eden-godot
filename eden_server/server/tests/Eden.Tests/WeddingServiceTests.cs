using Eden.Mission.Services;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Tests du sous-système cérémonies de mariage :
/// - codec ZMis 193 ↔ client 599 (formats relus byte-par-byte sur les
///   Serialize/Execute natifs),
/// - machine à statuts CWeddingFactory (0→1 immédiat, 1→2 +600s,
///   2→3 +7200s, 3→fermeture +600s).
/// </summary>
public sealed class WeddingServiceTests
{
    private static byte[] ZMisBody(int requester,
        params (int id, string n1, string n2, byte st, int t1, int t2)[] recs)
    {
        var b = new List<byte>();
        b.AddRange(BitConverter.GetBytes(requester));
        b.Add((byte)recs.Length);
        foreach (var r in recs)
        {
            b.AddRange(BitConverter.GetBytes(r.id));
            var n1 = Eden.Core.Data.EdenEncoding.Big5.GetBytes(r.n1);
            var n2 = Eden.Core.Data.EdenEncoding.Big5.GetBytes(r.n2);
            b.AddRange(BitConverter.GetBytes((ushort)n1.Length));
            b.AddRange(n1);
            b.AddRange(BitConverter.GetBytes((ushort)n2.Length));
            b.AddRange(n2);
            b.Add(r.st);
            b.AddRange(BitConverter.GetBytes(r.t1));
            b.AddRange(BitConverter.GetBytes(r.t2));
        }
        return b.ToArray();
    }

    [Fact]
    public void ParseWeddingsInfo_DecodesNativeLayout()
    {
        var body = ZMisBody(42,
            (7, "Alice", "Bob", 2, 1000, 600),
            (9, "Cid", "Dia", 1, 2000, 300));
        var recs = WeddingPackets.ParseWeddingsInfo(body);
        Assert.NotNull(recs);
        Assert.Equal(2, recs!.Count);
        Assert.Equal(7, recs[0].LoveId);
        Assert.Equal("Alice", recs[0].Name1);
        Assert.Equal("Bob", recs[0].Name2);
        Assert.Equal((byte)2, recs[0].Status);
        Assert.Equal(1000, recs[0].StartTime);
        Assert.Equal(600, recs[0].Remaining);
        Assert.Equal(9, recs[1].LoveId);
    }

    [Fact]
    public void ParseWeddingsInfo_RejectsTruncated()
    {
        Assert.Null(WeddingPackets.ParseWeddingsInfo(new byte[3]));
        var body = ZMisBody(1, (7, "A", "B", 0, 0, 0));
        Assert.Null(WeddingPackets.ParseWeddingsInfo(body[..^3]));
    }

    [Fact]
    public void BuildClientWeddingsInfo_ComputesLeftTime()
    {
        // leftTime = remaining - (now - start) — formule de l'Execute
        // natif zone @0x833ff00.
        var recs = new List<WeddingPackets.ZWedding>
        { new(7, "Alice", "Bob", 2, 1000, 600) };
        var body = WeddingPackets.BuildClientWeddingsInfo(recs, 1100);
        // [u8 1][i32 7][u16 5 "Alice"][u16 3 "Bob"][u8 2][i32 500]
        Assert.Equal(1 + 4 + 2 + 5 + 2 + 3 + 1 + 4, body.Length);
        Assert.Equal(1, body[0]);
        Assert.Equal(7, BitConverter.ToInt32(body, 1));
        Assert.Equal(2, body[1 + 4 + 2 + 5 + 2 + 3]);
        Assert.Equal(500, BitConverter.ToInt32(body, body.Length - 4));
    }

    [Fact]
    public void BuildClientWeddingsInfo_EmptyIsOneByte()
    {
        var body = WeddingPackets.BuildClientWeddingsInfo(
            Array.Empty<WeddingPackets.ZWedding>(), 0);
        Assert.Equal(new byte[] { 0 }, body);
    }

    [Fact]
    public void StepDurations_MatchNativeEventWedding()
    {
        // CEventWedding ctor : status 1/3 = +600, status 2 = +0x1c20.
        Assert.Equal(600, WeddingService.StepDuration(1));
        Assert.Equal(7200, WeddingService.StepDuration(2));
        Assert.Equal(600, WeddingService.StepDuration(3));
        Assert.Equal(0, WeddingService.StepDuration(0));
    }

    [Fact]
    public async Task InfoQuery_SubscribesAndPushesList()
    {
        var sent = new List<int>();
        using var svc = new WeddingService(_ => true,
            charUid => { sent.Add(charUid); return Task.CompletedTask; });
        Assert.True(svc.CreateWedding(7, 100, "A", "B", 100, 200));
        await svc.InfoQueryAsync(100, true);
        Assert.Contains(100, svc.Subscribers);
        Assert.Contains(100, sent);
        // flag=0 → désabonnement (TC_WeddingInfoQuery param_2).
        await svc.InfoQueryAsync(100, false);
        Assert.DoesNotContain(100, svc.Subscribers);
    }

    [Fact]
    public async Task Advance_Status0BecomesStatus1()
    {
        using var svc = new WeddingService(_ => true);
        Assert.True(svc.CreateWedding(7, 100, "A", "B", 100, 200));
        await svc.AdvanceAsync();
        Assert.Equal(1, svc.Weddings[0].Status);
    }

    [Fact]
    public async Task Advance_PrepExpiresToCeremony_WhenBothOnline()
    {
        using var svc = new WeddingService(id => id is 100 or 200);
        Assert.True(svc.CreateWedding(7, 100, "A", "B", 100, 200));
        await svc.AdvanceAsync(); // 0 → 1
        var w = svc.Weddings[0];
        w.StatusSince -= 601;   // préparatifs écoulés
        await svc.AdvanceAsync();
        Assert.Equal(2, svc.Weddings[0].Status);
    }

    [Fact]
    public async Task Advance_PrepExpiresToClose_WhenPartnerOffline()
    {
        // Natif : `wedding+0x50 < 2` amants dans le node →
        // OnEventTimeUp(id,3) → CloseWedding.
        using var svc = new WeddingService(id => id == 100);
        Assert.True(svc.CreateWedding(7, 100, "A", "B", 100, 200));
        await svc.AdvanceAsync(); // 0 → 1
        svc.Weddings[0].StatusSince -= 601;
        await svc.AdvanceAsync();
        Assert.Empty(svc.Weddings);
    }

    [Fact]
    public async Task Advance_ClosingExpires_RemovesWedding()
    {
        using var svc = new WeddingService(_ => true);
        Assert.True(svc.CreateWedding(7, 100, "A", "B", 100, 200));
        await svc.AdvanceAsync();
        var w = svc.Weddings[0];
        w.Status = 3;
        w.StatusSince -= 601;
        await svc.AdvanceAsync();
        Assert.Empty(svc.Weddings);
    }

    [Fact]
    public void CreateWedding_RejectsDuplicateLoveId()
    {
        using var svc = new WeddingService(_ => true);
        Assert.True(svc.CreateWedding(7, 100, "A", "B", 100, 200));
        Assert.False(svc.CreateWedding(7, 101, "C", "D", 101, 201));
    }
}
