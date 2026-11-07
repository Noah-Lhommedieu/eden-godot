using Dapper;
using Npgsql;
using Xunit;
using Eden.Core.Game.Family;

namespace Eden.Tests;

/// <summary>
/// Lot E5 — succès de famille : port de
/// <c>CFamily::UpdataAchievement</c> (mission @0x081be030, appelé depuis
/// <c>CGlobalInfo::SetAchievement</c>) et wire variable de
/// <c>CNC_CW_ClientFamilyAchievementUpdate</c> (67 — Serialize natif
/// monde @0x080cdfb0). Couvre l'upsert <c>family_achievement</c>, les
/// caps (999999999 points / 100 spell), le gate <c>elfking_tid</c>,
/// le log type 4 et le layout binaire des flags 0/1/2/3.
/// </summary>
public class FamilyAchievementTests
{
    private static ushort U16(byte[] b, int o)
        => (ushort)(b[o] | (b[o + 1] << 8));
    private static int I32(byte[] b, int o)
        => b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24);

    // ── Codec NCID 67 (Serialize natif monde @0x080cdfb0) ────────

    [Fact]
    public void AchievementUpdate_Flag0_OneEntryThenNoLog()
    {
        // flag=0 : [u8][u16 n][n×(s16,u32,i32,u32)] — sans u32 total
        // ni vecteur log.
        var p = FamilyPackets.FamilyAchievementUpdate(0, 0,
            [(5, 100u, 42, 7u)]);
        Assert.Equal(17, p.Length);
        Assert.Equal(0, p[0]);
        Assert.Equal(1, U16(p, 1));          // n
        Assert.Equal(5, (short)U16(p, 3));   // achievement_type (GroupKey)
        Assert.Equal(100, I32(p, 5));        // point
        Assert.Equal(42, I32(p, 9));         // charId
        Assert.Equal(7, I32(p, 13));         // channel
    }

    [Fact]
    public void AchievementUpdate_Flag1_EmptyVariantThenLogCount()
    {
        // flag=1 : operator<<(param_1, 0) — littéral int natif → i32,
        // puis le vecteur log (vide : u16 0).
        var p = FamilyPackets.FamilyAchievementUpdate(1, 0, []);
        Assert.Equal(new byte[] { 1, 0, 0, 0, 0, 0, 0 }, p);
    }

    [Fact]
    public void AchievementUpdate_Flag2_TotalThenEntriesThenLog()
    {
        // flag=2 : [u8][u32 total][u16 n][n×14][u16 m] — charId/channel
        // réels (pas de fill_assign, réservé au flag 3).
        var p = FamilyPackets.FamilyAchievementUpdate(2, 500,
            [(5, 100u, 42, 7u), (9, 50u, 11, 3u)]);
        Assert.Equal(37, p.Length);
        Assert.Equal(2, p[0]);
        Assert.Equal(500, I32(p, 1));        // u32 total (fam+0x48)
        Assert.Equal(2, U16(p, 5));          // n
        Assert.Equal(5, (short)U16(p, 7));   // 1re entrée : type
        Assert.Equal(9, (short)U16(p, 21));  // 2e entrée : type
        Assert.Equal(50, I32(p, 23));
        Assert.Equal(11, I32(p, 27));
        Assert.Equal(3, I32(p, 31));
        Assert.Equal(0, U16(p, 35));         // m — log vide (table morte)
    }

    [Fact]
    public void AchievementUpdate_Flag3_ZeroesCharAndChannel()
    {
        // flag=3 (synchro login) : _M_fill_assign à 0 sur les vecteurs
        // +0x30 (charId) et +0x24 (channel) du natif.
        var p = FamilyPackets.FamilyAchievementUpdate(3, 500,
            [(5, 100u, 42, 7u)]);
        Assert.Equal(23, p.Length);
        Assert.Equal(3, p[0]);
        Assert.Equal(500, I32(p, 1));
        Assert.Equal(1, U16(p, 5));
        Assert.Equal(5, (short)U16(p, 7));
        Assert.Equal(100, I32(p, 9));
        Assert.Equal(0, I32(p, 13));         // charId → 0
        Assert.Equal(0, I32(p, 17));         // channel → 0
    }

    // ── ApplyFamilyAchievementAsync (UpdataAchievement porté) ────

    private static readonly string Conn = TestDb.ConnStr("edenfr_db");

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

    private static async Task<int> MakeFamily(NpgsqlDataSource ds,
        int elfkingTid = 0)
    {
        var famId = -new Random().Next(100000, 999999);
        await using var conn = ds.CreateConnection();
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "INSERT INTO family (id, name, leader_id, be_create_time, lv, elfking_tid) " +
            "VALUES (@id, @name, 0, 0, 1, @elf)",
            new { id = famId, name = $"test-ach{-famId}", elf = elfkingTid });
        return famId;
    }

    private static async Task Cleanup(NpgsqlDataSource ds, int famId)
    {
        await using var conn = ds.CreateConnection();
        await conn.OpenAsync();
        foreach (var t in new[]
                     { "family_achievement", "family_achievement_rank",
                       "family_msgboard" })
            await conn.ExecuteAsync(
                $"DELETE FROM {t} WHERE family_id = @id",
                new { id = famId });
        // family_log : la colonne famille s'appelle « id » (natif).
        await conn.ExecuteAsync("DELETE FROM family_log WHERE id = @id",
            new { id = famId });
        await conn.ExecuteAsync("DELETE FROM family WHERE id = @id",
            new { id = famId });
    }

    private static async Task<(FamilyService svc, Family fam)> LoadAsync(
        NpgsqlDataSource ds, int famId)
    {
        var svc = new FamilyService(new FamilyRepository(ds));
        await svc.LoadAsync();
        var fam = svc.GetFamily(famId)!;
        svc.AddMember(fam,
            new FamilyMemberSummary { CharacterId = -90001, Name = "T" });
        return (svc, fam);
    }

    [Fact]
    public async Task Achievement_FirstGrant_InsertsRowAndLog()
    {
        if (!await DbUp()) return;
        var ds = NpgsqlDataSource.Create(Conn);
        var famId = await MakeFamily(ds);
        try
        {
            var (svc, fam) = await LoadAsync(ds, famId);
            var r = await svc.ApplyFamilyAchievementAsync(
                -90001, achId: 1001, type: 5, groupKey: 0x65, gained: 30);
            Assert.NotNull(r);
            var rr = r!.Value;
            Assert.Equal(0x65, rr.GroupKey);
            Assert.Equal(30u, rr.GroupPoint);
            Assert.Equal(30u, fam.Achievements[0x65]);
            Assert.Equal(30u, fam.AchievementTotal);
            // Compteurs dual-key fam+0x160 : GroupKey ET Type.
            Assert.Equal(30, fam.Counters[0x65]);
            Assert.Equal(30, fam.Counters[5]);
            // Pas d'elf king → pas de spell point.
            Assert.Equal(0, fam.SpellPoint);
            // Log type 4 : para1=achId, str1=nom du joueur.
            var log = Assert.Single(fam.Logs);
            Assert.Equal(4, log.Type);
            Assert.Equal(1001, log.Para);
            Assert.Equal("T", log.Str1);
            await using var conn = ds.CreateConnection();
            await conn.OpenAsync();
            var point = await conn.ExecuteScalarAsync<int>(
                "SELECT point FROM family_achievement WHERE family_id=@f " +
                "AND achievement_type=101", new { f = famId });
            Assert.Equal(30, point);
            var logCount = await conn.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM family_log WHERE id=@f",
                new { f = famId });
            Assert.Equal(1, logCount);
        }
        finally { await Cleanup(ds, famId); }
    }

    [Fact]
    public async Task Achievement_SecondGrant_UpdatesRow()
    {
        if (!await DbUp()) return;
        var ds = NpgsqlDataSource.Create(Conn);
        var famId = await MakeFamily(ds);
        try
        {
            var (svc, fam) = await LoadAsync(ds, famId);
            await svc.ApplyFamilyAchievementAsync(-90001, 1, 5, 0x65, 30);
            var r = await svc.ApplyFamilyAchievementAsync(
                -90001, 2, 5, 0x65, 20);
            Assert.Equal(50u, r!.Value.GroupPoint);
            Assert.Equal(50u, fam.Achievements[0x65]);
            Assert.Equal(50u, fam.AchievementTotal);
            await using var conn = ds.CreateConnection();
            await conn.OpenAsync();
            var rows = await conn.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM family_achievement WHERE family_id=@f",
                new { f = famId });
            Assert.Equal(1, rows); // UPDATE, pas un 2e INSERT
        }
        finally { await Cleanup(ds, famId); }
    }

    [Fact]
    public async Task Achievement_ZeroOrNegative_IsIgnored()
    {
        if (!await DbUp()) return;
        var ds = NpgsqlDataSource.Create(Conn);
        var famId = await MakeFamily(ds);
        try
        {
            var (svc, fam) = await LoadAsync(ds, famId);
            Assert.Null(await svc.ApplyFamilyAchievementAsync(
                -90001, 1, 5, 0x65, 0));
            Assert.Null(await svc.ApplyFamilyAchievementAsync(
                -90001, 1, 5, 0x65, -10));
            Assert.Empty(fam.Achievements);
            Assert.Equal(0u, fam.AchievementTotal);
            Assert.Empty(fam.Logs);
        }
        finally { await Cleanup(ds, famId); }
    }

    [Fact]
    public async Task Achievement_GroupCap_RefusesBeyond999999999()
    {
        if (!await DbUp()) return;
        var ds = NpgsqlDataSource.Create(Conn);
        var famId = await MakeFamily(ds);
        try
        {
            var (svc, fam) = await LoadAsync(ds, famId);
            fam.Achievements[0x65] = 999999990;
            Assert.Null(await svc.ApplyFamilyAchievementAsync(
                -90001, 1, 5, 0x65, 10)); // 999999990 + 10 > cap natif
            Assert.Equal(999999990u, fam.Achievements[0x65]);
        }
        finally { await Cleanup(ds, famId); }
    }

    [Fact]
    public async Task Achievement_SpellPoint_RequiresElfKingCaps100()
    {
        if (!await DbUp()) return;
        var ds = NpgsqlDataSource.Create(Conn);
        var famId = await MakeFamily(ds, elfkingTid: 7);
        try
        {
            var (svc, fam) = await LoadAsync(ds, famId);
            await svc.ApplyFamilyAchievementAsync(-90001, 1, 5, 0x65, 60);
            Assert.Equal(60, fam.SpellPoint);
            var sp = await svc.ApplyFamilyAchievementAsync(
                -90001, 2, 5, 0x65, 70);
            Assert.Equal(100, fam.SpellPoint);   // cap 100 natif
            Assert.Equal(100, sp!.Value.SpellPoint);
            await using var conn = ds.CreateConnection();
            await conn.OpenAsync();
            var db = await conn.ExecuteScalarAsync<int>(
                "SELECT spell_point FROM family WHERE id=@f",
                new { f = famId });
            Assert.Equal(100, db);
        }
        finally { await Cleanup(ds, famId); }
    }

    [Fact]
    public async Task Msgboard_LoadsIntoMessages_NotSchedule()
    {
        if (!await DbUp()) return;
        var ds = NpgsqlDataSource.Create(Conn);
        var famId = await MakeFamily(ds);
        try
        {
            await using (var conn = ds.CreateConnection())
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync(
                    "INSERT INTO family_msgboard (family_id, message_id, " +
                    "title, content, player_name, create_date) " +
                    "VALUES (@f, 1, 'Titre', 'Corps', 'Auteur', 42)",
                    new { f = famId });
            }
            var svc = new FamilyService(new FamilyRepository(ds));
            await svc.LoadAsync();
            var fam = svc.GetFamily(famId)!;
            // Régression : le NCID 51 émet fam.Messages — le board doit
            // être peuplé depuis family_msgboard (family_schedule est
            // une table morte native, zéro référence dans les binaires).
            var msg = Assert.Single(fam.Messages.Values);
            Assert.Equal("Titre", msg.Title);
            Assert.Equal("Corps", msg.Content);
        }
        finally { await Cleanup(ds, famId); }
    }

    [Fact]
    public async Task Achievement_UnknownPlayer_IsIgnored()
    {
        if (!await DbUp()) return;
        var ds = NpgsqlDataSource.Create(Conn);
        var famId = await MakeFamily(ds);
        try
        {
            var svc = new FamilyService(new FamilyRepository(ds));
            await svc.LoadAsync();
            // charId qui n'appartient à aucune famille → null natif.
            Assert.Null(await svc.ApplyFamilyAchievementAsync(
                -77777, 1, 5, 0x65, 10));
        }
        finally { await Cleanup(ds, famId); }
    }
}
