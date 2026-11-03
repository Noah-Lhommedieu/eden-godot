using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Zone;
using Npgsql;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régressions class-lifelong — NCIDs 554
/// <c>CNC_CZ_ClientUpdateLifelongPoint</c> « [s16 groupe][s16 type]
/// [i32 compteur] » et 555 <c>CNC_CZ_ClientUpdateLifelongGold</c>
/// « [i32] », formules d'or S_Parameter 419-439
/// (AddLifelongPointAndGold @0x084192b0), clé « groupe waken »
/// S_Class col2 et table <c>class_lifelong</c>.
/// </summary>
public class LifelongTests
{
    private static readonly string DbConn =
        TestDb.ConnStr("edenfr_db");

    /// <summary>player_id jetable — aucune FK sur class_lifelong ;
    /// nettoyé après chaque test.</summary>
    private const int TestPlayer = 999000111;

    private static async Task<bool> DbUp()
    {
        try
        {
            await using var c = new NpgsqlConnection(DbConn);
            await c.OpenAsync();
            return true;
        }
        catch { return false; }
    }

    // ── Wire 554/555 ──────────────────────────────────────────────

    [Fact]
    public void Wire554_GroupeTypeCompteur()
    {
        var cmd = new CNC_CZ_ClientUpdateLifelongPoint
        { Field1 = 2, Field2 = 3, Field3 = 42 };
        var s = new OutBinStream();
        cmd.Serialize(s);
        var b = s.ToArray();
        Assert.Equal(8, b.Length);
        Assert.Equal(2, BitConverter.ToInt16(b, 0));   // groupe waken
        Assert.Equal(3, BitConverter.ToInt16(b, 2));   // type daily
        Assert.Equal(42, BitConverter.ToInt32(b, 4));  // compteur
    }

    [Fact]
    public void Wire555_OrAbsolu()
    {
        var cmd = new CNC_CZ_ClientUpdateLifelongGold { Value = 12345 };
        var s = new OutBinStream();
        cmd.Serialize(s);
        var b = s.ToArray();
        Assert.Equal(4, b.Length);
        Assert.Equal(12345, BitConverter.ToInt32(b, 0));
    }

    // ── Groupe waken (S_Class col2) ───────────────────────────────

    [Fact]
    public void Group_DisplayIndexPasClassId()
    {
        var defs = new List<ClassStatsParser.ClassDefinition>
        {
            new() { Id = 21, DisplayIndex = 1 },   // guerrier avancé
            new() { Id = 22, DisplayIndex = 1 },
            new() { Id = 59, DisplayIndex = 59 },  // groupe propre
            new() { Id = 60, DisplayIndex = 59 },
        };
        Assert.Equal(1, ZoneEntryFlow.LifelongGroup(defs, 21));
        Assert.Equal(1, ZoneEntryFlow.LifelongGroup(defs, 22));
        Assert.Equal(59, ZoneEntryFlow.LifelongGroup(defs, 60));
        // Classe inconnue → class_id à défaut.
        Assert.Equal(77, ZoneEntryFlow.LifelongGroup(defs, 77));
    }

    // ── Paliers d'or S_Parameter 419-439 ──────────────────────────

    /// <summary>Jeu de paramètres fidèle aux S_Parameter.ini réels :
    /// type0 seuils 10/30/70 → or 10/6/3/0 ; type1 5/15/35 → 20/10/5/0 ;
    /// type2 100/300/700 → 3/2/1/0.</summary>
    private static ParameterParser.ParameterSet RealParams()
    {
        var v = new Dictionary<int, float>
        {
            [419] = 10, [420] = 30, [421] = 70,
            [422] = 10, [423] = 6, [424] = 3, [425] = 0,
            [426] = 5, [427] = 15, [428] = 35,
            [429] = 20, [430] = 10, [431] = 5, [432] = 0,
            [433] = 100, [434] = 300, [435] = 700,
            [436] = 3, [437] = 2, [438] = 1, [439] = 0,
        };
        return new ParameterParser.ParameterSet(v,
            new Dictionary<int, string>());
    }

    [Fact]
    public void GoldTier_Type0Territoire()
    {
        var p = RealParams();
        // Palier choisi sur l'ANCIEN compteur (natif : comparaison
        // avant `entry += delta`).
        Assert.Equal(10, ZoneEntryFlow.LifelongGoldTier(p, 0, 0));
        Assert.Equal(10, ZoneEntryFlow.LifelongGoldTier(p, 0, 9));
        Assert.Equal(6, ZoneEntryFlow.LifelongGoldTier(p, 0, 10));
        Assert.Equal(6, ZoneEntryFlow.LifelongGoldTier(p, 0, 29));
        Assert.Equal(3, ZoneEntryFlow.LifelongGoldTier(p, 0, 30));
        Assert.Equal(3, ZoneEntryFlow.LifelongGoldTier(p, 0, 69));
        Assert.Equal(0, ZoneEntryFlow.LifelongGoldTier(p, 0, 70));
        Assert.Equal(0, ZoneEntryFlow.LifelongGoldTier(p, 0, 1000));
    }

    [Fact]
    public void GoldTier_Type1Colosseum()
    {
        var p = RealParams();
        Assert.Equal(20, ZoneEntryFlow.LifelongGoldTier(p, 1, 0));
        Assert.Equal(10, ZoneEntryFlow.LifelongGoldTier(p, 1, 5));
        Assert.Equal(5, ZoneEntryFlow.LifelongGoldTier(p, 1, 15));
        Assert.Equal(0, ZoneEntryFlow.LifelongGoldTier(p, 1, 35));
    }

    [Fact]
    public void GoldTier_Type2Rare()
    {
        var p = RealParams();
        Assert.Equal(3, ZoneEntryFlow.LifelongGoldTier(p, 2, 0));
        Assert.Equal(2, ZoneEntryFlow.LifelongGoldTier(p, 2, 100));
        Assert.Equal(1, ZoneEntryFlow.LifelongGoldTier(p, 2, 300));
        Assert.Equal(0, ZoneEntryFlow.LifelongGoldTier(p, 2, 700));
    }

    // ── class_lifelong — deltas/reset/or/jour (DB live) ───────────

    [Fact]
    public async Task Repo_UpsertResetEtGold()
    {
        if (!await DbUp()) return;
        var repo = new LifelongRepository(
            NpgsqlDataSource.Create(DbConn));
        await Cleanup();
        try
        {
            // Deltas idempotents — UPSERT cumulatif par colonne.
            await repo.AddPointAsync(TestPlayer, 1, 3, 4);   // daily +4
            await repo.AddPointAsync(TestPlayer, 1, 3, 6);   // daily +6
            await repo.AddPointAsync(TestPlayer, 1, 2, 2);   // rare +2
            var map = await repo.LoadAsync(TestPlayer);
            Assert.True(map.TryGetValue(1, out var e));
            Assert.Equal(10, e.DailyMonsterKill);
            Assert.Equal(2, e.RareMonsterKill);
            Assert.Equal(0, e.TerritoryKill);
            Assert.Equal(0, e.ColosseumWin);
            // Reset daily — les autres compteurs survivent.
            await repo.ResetDailyAsync(TestPlayer);
            map = await repo.LoadAsync(TestPlayer);
            Assert.Equal(0, map[1].DailyMonsterKill);
            Assert.Equal(2, map[1].RareMonsterKill);
        }
        finally { await Cleanup(); }
    }

    [Fact]
    public async Task Repo_JourResetDansFlags()
    {
        if (!await DbUp()) return;
        var repo = new LifelongRepository(
            NpgsqlDataSource.Create(DbConn));
        await Cleanup();
        // player_characters requis pour le blob flags (account_id=1 : FK DB-2 vers account.accounts) — ligne
        // temporaire minimale.
        await using (var c = new NpgsqlConnection(DbConn))
        {
            await c.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO player_characters (id, account_id, " +
                "given_name) VALUES (@p, 1, 'll_test') " +
                "ON CONFLICT (id) DO NOTHING", c);
            cmd.Parameters.AddWithValue("p", TestPlayer);
            await cmd.ExecuteNonQueryAsync();
        }
        try
        {
            Assert.Equal(-1, await repo.GetResetDayAsync(TestPlayer));
            await repo.SetResetDayAsync(TestPlayer, 15);
            Assert.Equal(15, await repo.GetResetDayAsync(TestPlayer));
            await repo.SetResetDayAsync(TestPlayer, 16);
            Assert.Equal(16, await repo.GetResetDayAsync(TestPlayer));
        }
        finally { await Cleanup(); }
    }

    private static async Task Cleanup()
    {
        await using var c = new NpgsqlConnection(DbConn);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM class_lifelong WHERE player_id=@p; " +
            "DELETE FROM player_characters WHERE id=@p", c);
        cmd.Parameters.AddWithValue("p", TestPlayer);
        await cmd.ExecuteNonQueryAsync();
    }
}
