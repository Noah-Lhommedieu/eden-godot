using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du lot Vague 1-D — succès / titres / calendrier de
/// connexion : codecs wire prouvés côté serveur C#
/// (ZoneEntryFlow.Achievement/Titles/DailyAward + DailyAwardPackets,
/// désasm zone @0x08209080 / @0x080f2400 / @0x81e9650), stores
/// partagés (<see cref="PlayerAchievements"/>,
/// <see cref="PlayerTitles"/>) et tables Reforged
/// (c_achievement / T_Achievement / C_DailyAward / T_Title).
/// </summary>
[Collection("PlayerAchievements")]
public class ZoneAchievementTests
{
    private static byte[] RoundTrip(NetMessage m)
    {
        var w = new BinWriter();
        w.WriteUShort((ushort)m.Opcode);
        m.Serialize(w);
        return w.ToArray();
    }

    private static T Parse<T>(byte[] payload) where T : NetMessage, new()
    {
        var m = new T();
        var r = new BinReader(payload);
        r.ReadUShort(); // NCID
        m.Deserialize(r);
        return m;
    }

    private static string? FindRawAssets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "raw_assets");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    // ------------------------- 480 ClientAchievementUpdate ---------

    [Fact]
    public void AchievementUpdate_Kind1Pairs_RoundTrip()
    {
        // [u8 kind=1][u16 n][i32 id][u32 pts]×n — upsert natif.
        var m = new ZoneAchievementUpdate { Kind = 1 };
        m.Entries.Add((10101, 5u));
        m.Entries.Add((20304, 12u));
        var bytes = RoundTrip(m);
        Assert.Equal(0xE0, bytes[0]);           // 480 = 0x01E0
        Assert.Equal(0x01, bytes[1]);
        var p = Parse<ZoneAchievementUpdate>(bytes);
        Assert.Equal(1, p.Kind);
        Assert.Equal(2, p.Entries.Count);
        Assert.Equal((10101, 5u), p.Entries[0]);
        Assert.Equal((20304, 12u), p.Entries[1]);
    }

    [Fact]
    public void AchievementUpdate_Kind0InitialSync_Parses()
    {
        // Trame native de synchro : kind 0 + 315 entrées max par
        // paquet (SendAllAchievementsAsync). On valide le parse sur 3.
        var w = new BinWriter();
        w.WriteUShort(480);
        w.WriteByte(0);
        w.WriteShort(3);
        foreach (var (id, pts) in new[] { (1001, 5), (1002, 10), (1003, 15) })
        {
            w.WriteInt(id);
            w.WriteUInt((uint)pts);
        }
        var p = Parse<ZoneAchievementUpdate>(w.ToArray());
        Assert.Equal(0, p.Kind);
        Assert.Equal(3, p.Entries.Count);
        Assert.Equal((1003, 15u), p.Entries[2]);
    }

    [Fact]
    public void AchievementUpdate_Kind2And3_Totals()
    {
        // kinds 2/3 = [u8 kind][u32 total] (cumuls chasse/ville).
        var p2 = Parse<ZoneAchievementUpdate>(
            RoundTrip(new ZoneAchievementUpdate { Kind = 2, Total = 777 }));
        Assert.Equal(777u, p2.Total);
        var p3 = Parse<ZoneAchievementUpdate>(
            RoundTrip(new ZoneAchievementUpdate { Kind = 3, Total = 42 }));
        Assert.Equal(42u, p3.Total);
        Assert.Empty(p3.Entries);
    }

    [Fact]
    public void AchievementUpdate_Kind5IdList_RoundTrip()
    {
        // kinds 4/5 = [u8 kind][u16 n][i32 id]×n.
        var m = new ZoneAchievementUpdate { Kind = 5 };
        m.Ids.AddRange(new[] { 7001, 7002 });
        var p = Parse<ZoneAchievementUpdate>(RoundTrip(m));
        Assert.Equal(new[] { 7001, 7002 }, p.Ids);
    }

    // ------------------------- PlayerAchievements ------------------

    [Fact]
    public void Store_Kind0InsertOnly_Kind1Upsert()
    {
        PlayerAchievements.Clear();
        var init = new ZoneAchievementUpdate { Kind = 0 };
        init.Entries.Add((10, 5u));
        PlayerAchievements.Apply(init);
        // kind 0 ne doit pas écraser une valeur connue.
        var stale = new ZoneAchievementUpdate { Kind = 0 };
        stale.Entries.Add((10, 1u));
        PlayerAchievements.Apply(stale);
        Assert.Equal(5u, PlayerAchievements.PointsOf(10));
        // kind 1 = upsert.
        var up = new ZoneAchievementUpdate { Kind = 1 };
        up.Entries.Add((10, 20u));
        PlayerAchievements.Apply(up);
        Assert.Equal(20u, PlayerAchievements.PointsOf(10));
        Assert.True(PlayerAchievements.IsCompleted(10));
        Assert.False(PlayerAchievements.IsCompleted(11));
        Assert.Equal(20, PlayerAchievements.TotalPoints);
        PlayerAchievements.Clear();
    }

    [Fact]
    public void Store_CategoryTotals()
    {
        PlayerAchievements.Clear();
        PlayerAchievements.Apply(
            new ZoneAchievementUpdate { Kind = 2, Total = 300 });
        PlayerAchievements.Apply(
            new ZoneAchievementUpdate { Kind = 3, Total = 50 });
        Assert.Equal(300u, PlayerAchievements.HuntPoints);
        Assert.Equal(50u, PlayerAchievements.TownPoints);
        PlayerAchievements.Clear();
    }

    // ------------------------- titres ------------------------------

    [Fact]
    public void SetAppellation_WireIsU16()
    {
        // 93 C→S = [u16 titleId] — désasm zone @0x080f2400.
        var bytes = RoundTrip(new ZoneSetAppellation { TitleId = 7 });
        Assert.Equal(new byte[] { 0x5D, 0x00, 0x07, 0x00 }, bytes);
    }

    [Fact]
    public void AppellationMessages_RoundTrip()
    {
        var upd = Parse<ZoneUpdateAppellation>(
            RoundTrip(new ZoneUpdateAppellation { TitleId = 42 }));
        Assert.Equal(42, upd.TitleId);
        var add = Parse<ZoneAddAppellation>(
            RoundTrip(new ZoneAddAppellation { TitleId = 9 }));
        Assert.Equal(9, add.TitleId);
        var chg = Parse<ZoneChangeAppellation>(
            RoundTrip(new ZoneChangeAppellation
            { CharId = 1234, TitleId = 5, Seq = 3 }));
        Assert.Equal(1234, chg.CharId);
        Assert.Equal(5, chg.TitleId);
        Assert.Equal(3, chg.Seq);
    }

    [Fact]
    public void Titles_OwnedAndActive()
    {
        PlayerTitles.Clear();
        PlayerTitles.ApplyAdd(3);                 // 398 grant
        PlayerTitles.ApplyUpdate(3);              // 397 actif
        Assert.Equal(3, PlayerTitles.Active);
        Assert.Contains(3, PlayerTitles.Owned);
        // 396 d'un autre perso ne touche pas l'actif self.
        PlayerTitles.ApplyChange(999, 7, 1, 1234);
        Assert.Equal(3, PlayerTitles.Active);
        Assert.Equal((7, (byte)1), PlayerTitles.Others[999]);
        // 396 du self → met à jour l'actif.
        PlayerTitles.ApplyChange(1234, 8, 2, 1234);
        Assert.Equal(8, PlayerTitles.Active);
        PlayerTitles.Clear();
    }

    // ------------------------- daily award -------------------------

    [Fact]
    public void DailyAwardOp_WireIsS16I32()
    {
        // 258 C→S = [s16 op][i32 param] (GOLDEN).
        var bytes = RoundTrip(new ZoneDailyAwardOp { Field1 = 2, Field2 = 7 });
        Assert.Equal(new byte[]
            { 0x02, 0x01, 0x02, 0x00, 0x07, 0x00, 0x00, 0x00 }, bytes);
    }

    [Fact]
    public void DailyAwards_BodyIs26Bytes()
    {
        // 615 S→C : corps dédié 26 octets (DailyAwardPackets.cs).
        var bytes = RoundTrip(new ZoneUpdateDailyAwards
        {
            DayIndex = 4,
            OnlineSeconds = 3600,
            AwardDays = 0b1111,
            AwardRecords = 0b0011,
            Kind = 1,
            Flag = 0,
        });
        Assert.Equal(2 + 26, bytes.Length);       // NCID + corps
        var p = Parse<ZoneUpdateDailyAwards>(bytes);
        Assert.Equal(4, p.DayIndex);
        Assert.Equal(3600, p.OnlineSeconds);
        Assert.Equal(0b1111, p.AwardDays);
        Assert.Equal(0b0011, p.AwardRecords);
        Assert.Equal(1, p.Kind);
    }

    // ------------------------- tables réelles ----------------------

    [Fact]
    public void RealFile_AchievementTable_Loads()
    {
        var root = FindRawAssets();
        if (root == null) return;   // assets absents — test ignoré
        var defs = AchievementTable.Load(
            Path.Combine(root, "data/db/c_achievement.ini"));
        Assert.True(defs.Count > 3000);           // ~3 942 attendus
        var any = defs.Values.First(d => d.Point > 0);
        Assert.True(any.GroupKey > 0);
        Assert.True(any.NeedNum >= 1);
    }

    [Fact]
    public void RealFile_DailyAwardTable_Loads()
    {
        var root = FindRawAssets();
        if (root == null) return;
        var defs = DailyAwardTable.Load(
            Path.Combine(root, "data/db/C_DailyAward.ini"));
        Assert.Equal(16, defs.Count);   // corpus officiel : ids 1-14, 29, 30
        Assert.True(defs[1].ItemId > 0);
        Assert.True(defs[30].ItemId > 0);
        Assert.True(defs[1].RequiredSeconds > 0);
    }

    [Fact]
    public void RealFile_TitleTable_Loads()
    {
        var root = FindRawAssets();
        if (root == null) return;
        var t = Path.Combine(root, "data/db/t_title.ini");
        var c = Path.Combine(root, "data/db/c_title.ini");
        var defs = TitleTable.Load(t, File.Exists(c) ? c : null);
        Assert.NotEmpty(defs);
        Assert.Contains(defs.Values, d => d.Name.Length > 0);
    }
}
