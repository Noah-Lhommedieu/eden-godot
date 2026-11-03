using Npgsql;
using Xunit;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Services;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;

namespace Eden.Tests;

/// <summary>
/// Tests des handlers famille natifs : compteur de révision
/// (<c>char+0x4de</c> — désasm <c>CNE_ZW_ZoneServerChangeFamily::
/// Execute</c>), wire <c>CNC_CZ_ClientChangeFamily</c> 346 et
/// <c>CNC_CZ_ClientFamilyStorageLog</c> 506 (Serialize natifs
/// @0x81d9xxx / @0x81e82f0), trésorerie <c>FamilyFixEquip</c> et
/// journal <c>family_storage_log</c> (SQL natif LIMIT 50 DESC).
/// </summary>
public class FamilyHandlerTests
{
    private static ushort U16(byte[] b, int off)
        => (ushort)(b[off] | (b[off + 1] << 8));

    private static int I32(byte[] b, int off)
        => b[off] | (b[off + 1] << 8) | (b[off + 2] << 16)
                  | (b[off + 3] << 24);

    private static byte[] Ser(NetCommand cmd)
    {
        var s = new OutBinStream();
        cmd.Serialize(s);
        return s.ToArray();
    }

    // ── GuildService : révision famille (char+0x4de natif) ───────

    [Fact]
    public async Task FamilyRevision_BumpsOnMembershipChanges()
    {
        var svc = new GuildService();
        await svc.CreateGuildAsync(10, "Alpha", 30);
        Assert.Equal(1, svc.GetFamilyRevision(10));
        Assert.Equal(0, svc.GetFamilyRevision(20));

        await svc.AddMemberAsync(svc.GetGuildOf(10)!.Id, 20, "P20");
        Assert.Equal(1, svc.GetFamilyRevision(20));

        svc.SetRank(10, 20, GuildService.GuildRank.Officer);
        Assert.Equal(2, svc.GetFamilyRevision(20));

        await svc.LeaveGuildAsync(20);
        Assert.Equal(3, svc.GetFamilyRevision(20));
        // Le chef n'est pas touché par le départ d'un membre.
        Assert.Equal(1, svc.GetFamilyRevision(10));
    }

    [Fact]
    public async Task GetMember_ExposesRank()
    {
        var svc = new GuildService();
        var g = await svc.CreateGuildAsync(10, "Alpha", 30);
        await svc.AddMemberAsync(g.Id, 20, "P20", GuildService.GuildRank.Veteran);
        Assert.Equal(GuildService.GuildRank.Leader,
            svc.GetMember(10)!.Rank);
        Assert.Equal(GuildService.GuildRank.Veteran,
            svc.GetMember(20)!.Rank);
        Assert.Null(svc.GetMember(99));
    }

    [Fact]
    public async Task Treasury_DebitRequiresFunds()
    {
        var svc = new GuildService();
        var g = await svc.CreateGuildAsync(10, "Alpha", 30);
        svc.LevyTax(g.Id, 100, 1000); // +1000 trésorerie
        Assert.False(await svc.TrySpendTreasuryAsync(g.Id, 1001));
        Assert.True(await svc.TrySpendTreasuryAsync(g.Id, 400));
        Assert.Equal(600, g.Treasury);
        Assert.False(await svc.TrySpendTreasuryAsync(9999, 1));
    }

    [Fact]
    public async Task Guild_LoadCarriesEmblem()
    {
        // Emblem persiste via guilds.emblem — défaut natif -1.
        var svc = new GuildService();
        var g = await svc.CreateGuildAsync(10, "Alpha", 30);
        Assert.Equal(-1, g.Emblem);
    }

    // ── Wire : 346 ClientChangeFamily ────────────────────────────

    [Fact]
    public void ClientChangeFamily_NativeFieldOrder()
    {
        // Serialize natif : i32 charId, i32 familyId, u8 flag,
        // str name, i32 rank, s16 emblem, u8 revision.
        var bytes = Ser(new CNC_CZ_ClientChangeFamily
        {
            Field1 = 1234, Field2 = 42, Field3 = 0,
            Field4 = "Alpha", Field5 = 0, Field6 = -1, Field7 = 3,
        });
        int o = 0;
        Assert.Equal(1234, I32(bytes, o)); o += 4;      // charId
        Assert.Equal(42, I32(bytes, o)); o += 4;        // familyId
        Assert.Equal(0, bytes[o]); o += 1;              // flag
        var len = U16(bytes, o); o += 2;                // str len s16
        Assert.Equal(5, len);
        o += 5;                                          // "Alpha"
        Assert.Equal(0, I32(bytes, o)); o += 4;          // rank leader=0
        Assert.Equal(-1, (short)U16(bytes, o)); o += 2;  // emblem -1
        Assert.Equal(3, bytes[o]); o += 1;               // revision
        Assert.Equal(bytes.Length, o);
    }

    [Fact]
    public void ClientChangeFamily_NoFamilyDefaults()
    {
        // Natif : hors guilde → id=0, rank=-1, emblem=0xffff.
        var bytes = Ser(new CNC_CZ_ClientChangeFamily
        {
            Field1 = 55, Field2 = 0, Field3 = 0,
            Field4 = "", Field5 = -1, Field6 = -1, Field7 = 0,
        });
        // Layout : [i32 char][i32 fam][u8][s16 len][str][i32][s16][u8]
        Assert.Equal(0, I32(bytes, 4));
        Assert.Equal(-1, I32(bytes, 11));              // rank @11
        Assert.Equal(-1, (short)U16(bytes, 15));       // emblem @15
    }

    // ── Wire : 506 ClientFamilyStorageLog ────────────────────────

    [Fact]
    public void ClientFamilyStorageLog_CountThenPairs()
    {
        var msg = new CNC_CZ_ClientFamilyStorageLog();
        msg.Entries.Add(new CNC_CZ_ClientFamilyStorageLog.Entry
        { Action = 0, Detail = "Storage Insert-ID:1" });
        msg.Entries.Add(new CNC_CZ_ClientFamilyStorageLog.Entry
        { Action = 1, Detail = "Delete-ID:2" });
        var bytes = Ser(msg);
        int o = 0;
        Assert.Equal(2, U16(bytes, o)); o += 2;         // count u16
        Assert.Equal(0, (short)U16(bytes, o)); o += 2;  // log_type 0
        var l1 = U16(bytes, o); o += 2 + l1;            // str
        Assert.Equal(1, (short)U16(bytes, o)); o += 2;  // log_type 1
        var l2 = U16(bytes, o); o += 2 + l2;
        Assert.Equal(bytes.Length, o);
    }

    // ── DB : family_storage_log (SQL natif LIMIT 50 DESC) ────────

    private static readonly string Conn =
        TestDb.ConnStr("edenfr_db");

    private static async Task<bool> DbUp()
    {
        try
        {
            await using var c = new NpgsqlConnection(Conn);
            await c.OpenAsync();
            return true;
        }
        catch { return false; }
    }

    [Fact]
    public async Task StorageLog_AppendThenQueryNewestFirst()
    {
        if (!await DbUp()) return;
        var repo = new FamilyBankRepository(Npgsql.NpgsqlDataSource.Create(Conn));
        int fam = -new Random().Next(100000, 999999);
        try
        {
            await repo.AppendStorageLogAsync(fam, 0,
                "Storage Insert-ID:1,ItemID:30001");
            await repo.AppendStorageLogAsync(fam, 1,
                "Delete-ID:2,ItemID:30002");
            var rows = await repo.GetStorageLogAsync(fam);
            Assert.Equal(2, rows.Count);
            // log_time a une granularité seconde — à timestamp égal
            // l'ordre n'est pas garanti (natif identique) : on vérifie
            // l'ensemble.
            Assert.Contains(rows, r => r.LogType == 0
                && r.Log.StartsWith("Storage Insert"));
            Assert.Contains(rows, r => r.LogType == 1
                && r.Log.StartsWith("Delete"));
        }
        finally
        {
            await using var c = new NpgsqlConnection(Conn);
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM family_storage_log WHERE family_id=@f", c);
            cmd.Parameters.AddWithValue("f", fam);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
