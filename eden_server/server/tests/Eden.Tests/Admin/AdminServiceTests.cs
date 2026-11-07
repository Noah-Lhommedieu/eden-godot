using Eden.Core.Game.Admin;
using Eden.Core.Game.Foundation;

namespace Eden.Tests.Admin;

/// <summary>Couche d'administration partagée (D5) et garde-fous (D8).</summary>
public class AdminServiceTests
{
    private sealed class Host : IAdminHost
    {
        public List<string> Calls = new(); public List<string> Backups = new();
        public Dictionary<string, int> Chars = new() { ["Bob"] = 11, ["Eve"] = 12 };
        public List<(int, string)> OnlineList = new() { (11, "Bob"), (12, "Eve") };
        public Task<int?> ResolveCharAsync(string name) => Task.FromResult(Chars.TryGetValue(name, out var i) ? (int?)i : null);
        public Task<bool> GiveAsync(int c, int i, int q) { Calls.Add($"give {c} {i} {q}"); return Task.FromResult(true); }
        public Task<bool> RemoveAsync(int c, int i, int q) { Calls.Add($"remove {c} {i} {q}"); return Task.FromResult(c != 12); }
        public IReadOnlyList<(int CharId, string Name)> Online() => OnlineList;
        public Task<string> GrantVipAsync(string a, DateTimeOffset? u) { Calls.Add($"vip {a} {u?.ToString("yyyy-MM-dd")}"); return Task.FromResult($"vip {a}"); }
        public Task<string> RevokeVipAsync(string a) { Calls.Add($"unvip {a}"); return Task.FromResult($"unvip {a}"); }
        public Task<string> BackupMassActionAsync(string d, IReadOnlyList<int> ids) { Backups.Add($"{d}:{ids.Count}"); return Task.FromResult("bk"); }
    }

    private sealed class Audit : IAdminAuditStore
    {
        public List<AuditEntry> Rows = new();
        public Task<AuditEntry> AppendAsync(DateTimeOffset at, AdminActor a, string cmd, string args, bool dry, bool ok, string msg, string req)
        {
            var prev = Rows.Count == 0 ? AuditChain.Genesis : Rows[^1].Hash;
            var e = new AuditEntry(Rows.Count + 1, at, a.AccountId, a.AccountName, a.CharId, cmd, args, dry, ok, msg, req, prev,
                AuditChain.Hash(prev, AuditChain.Content(at, a.AccountId, a.AccountName, a.CharId, cmd, args, dry, ok, msg, req)));
            Rows.Add(e); return Task.FromResult(e);
        }
        public Task<IReadOnlyList<AuditEntry>> RecentAsync(int limit) => Task.FromResult<IReadOnlyList<AuditEntry>>(Rows.TakeLast(limit).Reverse().ToList());
        public Task<AdminResult?> FindByRequestAsync(string id)
        {
            var e = Rows.FirstOrDefault(r => r.RequestId == id && r.Command != "x");
            return Task.FromResult(e == null ? null : (AdminResult?)new AdminResult(e.Ok, e.Message, e.Ok ? "" : "failed"));
        }
    }

    private sealed class Totps : ITotpStore
    {
        public Dictionary<int, string> S = new();
        public Task<string?> GetAsync(int id) => Task.FromResult(S.TryGetValue(id, out var v) ? v : null);
        public Task SetAsync(int id, string s) { S[id] = s; return Task.CompletedTask; }
    }

    private readonly Host _host = new(); private readonly Audit _audit = new(); private readonly Totps _totp = new();
    private readonly FakeGameClock _clock = new(DateTimeOffset.Parse("2026-11-07T12:00:00Z"), new GameClockOptions(6, TimeZoneInfo.Utc));
    private AdminService Svc(AdminPolicy? p = null) => new(_host, _audit, _totp, _clock, p ?? new AdminPolicy());
    private static AdminActor Who(AdminLevel l, int acc = 1, string? totp = null) => new(acc, $"acc{acc}", 100 + acc, l, totp);
    private static AdminRequest Req(string cmd, AdminActor a, string id = "", bool dry = false, params (string, string)[] args) =>
        new(id, cmd, args.ToDictionary(x => x.Item1, x => x.Item2), a, dry);

    [Fact] public async Task UnknownCommand_IsRefused() =>
        Assert.Equal("unknown_command", (await Svc().ExecuteAsync(Req("nope", Who(AdminLevel.Root)))).Code);

    [Theory]
    [InlineData(AdminLevel.None)] [InlineData(AdminLevel.Viewer)]
    public async Task Give_NeedsSupportLevel(AdminLevel l) =>
        Assert.Equal("forbidden", (await Svc().ExecuteAsync(Req("give", Who(l), "", false, ("item", "1"), ("reason", "x")))).Code);

    [Fact] public async Task Give_ToNamedPlayer_CallsHostAndAudits()
    {
        var r = await Svc().ExecuteAsync(Req("give", Who(AdminLevel.Support), "r1", false, ("target", "Bob"), ("item", "44025"), ("qty", "3"), ("reason", "test")));
        Assert.True(r.Ok); Assert.Equal(new[] { "give 11 44025 3" }, _host.Calls);
        Assert.Equal("give", _audit.Rows.Single().Command);
    }

    [Fact] public async Task Give_RequiresReason_AndPositiveQuantity()
    {
        Assert.Equal("bad_args", (await Svc().ExecuteAsync(Req("give", Who(AdminLevel.Support), "", false, ("target", "Bob"), ("item", "1")))).Code);
        Assert.Equal("bad_args", (await Svc().ExecuteAsync(Req("give", Who(AdminLevel.Support), "", false, ("target", "Bob"), ("item", "1"), ("qty", "0"), ("reason", "r")))).Code);
        Assert.Empty(_host.Calls);
    }

    [Theory]
    [InlineData(AdminLevel.Support, 101, false)] [InlineData(AdminLevel.Support, 100, true)]
    [InlineData(AdminLevel.Content, 1001, false)] [InlineData(AdminLevel.Root, 9999, true)] [InlineData(AdminLevel.Root, 10000, false)]
    public async Task Give_QuantityCapPerLevel(AdminLevel l, int qty, bool ok)
    {
        var r = await Svc().ExecuteAsync(Req("give", Who(l), "", false, ("target", "Bob"), ("item", "1"), ("qty", qty.ToString()), ("reason", "r")));
        Assert.Equal(ok, r.Ok);
    }

    [Fact] public async Task DryRun_ChangesNothing()
    {
        var r = await Svc().ExecuteAsync(Req("give", Who(AdminLevel.Support), "", true, ("target", "Bob"), ("item", "1"), ("reason", "r")));
        Assert.True(r.Ok); Assert.Contains("simulation", r.Message); Assert.Empty(_host.Calls);
    }

    [Fact] public async Task SameRequestId_IsReplayedNotRerun()
    {
        var s = Svc(); var rq = Req("give", Who(AdminLevel.Support), "same", false, ("target", "Bob"), ("item", "1"), ("reason", "r"));
        await s.ExecuteAsync(rq); var again = await s.ExecuteAsync(rq);
        Assert.True(again.Replayed); Assert.Single(_host.Calls);
    }

    [Fact] public async Task Remove_ReportsPartialFailure()
    {
        var r = await Svc().ExecuteAsync(Req("remove", Who(AdminLevel.Support), "", false, ("target", "Eve"), ("item", "1"), ("reason", "r")));
        Assert.Equal("partial", r.Code);
    }

    [Fact] public async Task RateLimit_BlocksBurst()
    {
        var s = Svc(new AdminPolicy(RateMaxPerMinute: 3));
        for (var i = 0; i < 3; i++) Assert.True((await s.ExecuteAsync(Req("whoami", Who(AdminLevel.Viewer)))).Ok);
        Assert.Equal("rate_limited", (await s.ExecuteAsync(Req("whoami", Who(AdminLevel.Viewer)))).Code);
        Assert.True((await s.ExecuteAsync(Req("whoami", Who(AdminLevel.Viewer, 2)))).Ok);            // autre compte : pas bloqué
        _clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await s.ExecuteAsync(Req("whoami", Who(AdminLevel.Viewer)))).Ok);
    }

    [Fact] public async Task Totp_RequiredWhenSecretExists_AndValidCodePasses()
    {
        _totp.S[1] = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var s = Svc(); var args = new[] { ("account", "x"), ("reason", "r") };
        Assert.Equal("totp_required", (await s.ExecuteAsync(Req("vip", Who(AdminLevel.Content), "", false, args))).Code);
        Assert.Equal("totp_required", (await s.ExecuteAsync(Req("vip", Who(AdminLevel.Content, 1, "000000"), "", false, args))).Code);
        var code = Totp.Code(_totp.S[1], _clock.Now);
        Assert.True((await s.ExecuteAsync(Req("vip", Who(AdminLevel.Content, 1, code), "", false, args))).Ok);
    }

    [Fact] public async Task Totp_PolicyRequire_RefusesWithoutSecret() =>
        Assert.Equal("totp_required", (await Svc(new AdminPolicy(RequireTotp: true)).ExecuteAsync(Req("vip", Who(AdminLevel.Content), "", false, ("account", "x"), ("reason", "r")))).Code);

    [Fact] public async Task TwoFactor_Setup_ThenSecondCallRefused()
    {
        var s = Svc(); var a = Who(AdminLevel.Viewer);
        var r = await s.ExecuteAsync(Req("2fa", a)); Assert.StartsWith("otpauth://totp/", r.Message);
        Assert.Equal("bad_args", (await s.ExecuteAsync(Req("2fa", a))).Code);
    }

    [Fact] public async Task FourEyes_MassGiveWaitsForAnotherAccount()
    {
        var s = Svc(new AdminPolicy(FourEyes: true)); var a = Who(AdminLevel.Publisher, 1); var b = Who(AdminLevel.Publisher, 2);
        var pend = await s.ExecuteAsync(Req("give", a, "", false, ("target", "all"), ("item", "1"), ("qty", "2"), ("reason", "event")));
        Assert.Equal("pending_approval", pend.Code); Assert.Empty(_host.Calls);
        Assert.Equal("bad_args", (await s.ExecuteAsync(Req("approve", a, "", false, ("id", "1")))).Code);   // même compte
        Assert.Empty(_host.Calls);
        var ok = await s.ExecuteAsync(Req("approve", b, "", false, ("id", "1")));
        Assert.True(ok.Ok, ok.Message); Assert.Equal(2, _host.Calls.Count);
    }

    [Fact] public async Task Mass_BacksUpBeforeAndNeedsLevel4()
    {
        Assert.Equal("forbidden", (await Svc().ExecuteAsync(Req("give", Who(AdminLevel.Content), "", false, ("target", "all"), ("item", "1"), ("reason", "r")))).Code);
        var r = await Svc().ExecuteAsync(Req("give", Who(AdminLevel.Publisher), "", false, ("target", "all"), ("item", "1"), ("reason", "r")));
        Assert.True(r.Ok); Assert.Single(_host.Backups); Assert.Equal(2, _host.Calls.Count);
    }

    [Fact] public async Task Vip_GrantWithDays_AndOff()
    {
        var s = Svc(); var a = Who(AdminLevel.Content);
        Assert.True((await s.ExecuteAsync(Req("vip", a, "", false, ("account", "x"), ("days", "7"), ("reason", "r")))).Ok);
        Assert.Contains("2026-11-14", _host.Calls[0]);
        Assert.True((await s.ExecuteAsync(Req("vip", a, "", false, ("account", "x"), ("days", "off"), ("reason", "r")))).Ok);
        Assert.Equal("unvip x", _host.Calls[1]);
        Assert.Equal("bad_args", (await s.ExecuteAsync(Req("vip", a, "", false, ("account", "x"), ("days", "abc"), ("reason", "r")))).Code);
    }

    [Fact] public async Task Help_ListsOnlyCommandsOfTheLevel()
    {
        var viewer = (await Svc().ExecuteAsync(Req("help", Who(AdminLevel.Viewer)))).Message;
        Assert.DoesNotContain("give", viewer); Assert.Contains("whoami", viewer);
        Assert.Contains("give", (await Svc().ExecuteAsync(Req("help", Who(AdminLevel.Support)))).Message);
    }

    [Fact] public async Task Audit_IsChained_AndTamperingIsDetected()
    {
        var s = Svc();
        for (var i = 0; i < 4; i++) await s.ExecuteAsync(Req("whoami", Who(AdminLevel.Viewer)));
        Assert.Null(AuditChain.FirstBroken(_audit.Rows));
        _audit.Rows[1] = _audit.Rows[1] with { Message = "falsifié" };
        Assert.Equal(2, AuditChain.FirstBroken(_audit.Rows));
    }

    [Fact] public async Task Refusals_AreAuditedToo()
    {
        await Svc().ExecuteAsync(Req("give", Who(AdminLevel.Viewer), "", false, ("item", "1")));
        Assert.False(_audit.Rows.Single().Ok);
    }

    [Theory]
    [InlineData(59, "287082")] [InlineData(1111111109, "081804")] [InlineData(1234567890, "005924")] [InlineData(2000000000, "279037")]
    public void Totp_MatchesRfc6238Vectors(long t, string expected) =>
        Assert.Equal(expected, Totp.Code("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", DateTimeOffset.FromUnixTimeSeconds(t)));

    [Fact] public void Totp_AcceptsOneStepDrift_NotTwo()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000); var s = Totp.GenerateSecret();
        Assert.True(Totp.Verify(s, Totp.Code(s, now.AddSeconds(-30)), now));
        Assert.False(Totp.Verify(s, Totp.Code(s, now.AddSeconds(-90)), now));
        Assert.False(Totp.Verify(s, "12345", now));
    }
}
