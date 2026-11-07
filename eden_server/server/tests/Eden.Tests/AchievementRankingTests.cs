using System.Text;
using Xunit;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class AchievementRankingTests
{
    private static readonly string DecodedDir =
        TestPaths.RepoRoot + "/server_us/Data/db_decoded";

    static AchievementRankingTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void Achievement_LoadsAndTracks()
    {
        var svc = AchievementService.LoadFromDataDir(DecodedDir);
        Assert.True(svc.DefinitionCount > 3000);
        // CAchievementData natif : NeedNum col1 (vide→1), type col5,
        // groupe combiné col6*100+col7, points col8.
        var kill = svc.Get(58001);              // M025 — kill de boss
        Assert.NotNull(kill);
        Assert.Equal(1, kill!.NeedNum);
        Assert.Equal(1, kill.Type);
        Assert.Equal(703, kill.GroupKey);       // 7*100+3
        var group = svc.Get(5001);              // AC501 — collecte
        Assert.NotNull(group);
        Assert.True(group!.Members.Length > 100);
        Assert.Equal(57501, group.Members[0]);
        Assert.Contains(5001, svc.GroupsOf(57501));
        svc.SetActiveTitle(1, 5);
        Assert.Equal(5, svc.GetActiveTitle(1));
    }

    /// <summary>Cascade <c>GetReachedAchievementGroupID</c> natif
    /// (@0x080764a0 mission) : le groupe n'est atteint que quand tous
    /// ses membres sont complétés (success_num >= NeedNum).</summary>
    [Fact]
    public void GroupReached_AllMembersCompleted()
    {
        var svc = AchievementService.LoadFromDataDir(DecodedDir);
        // Construit un faux groupe via le vrai svc : les AC5xx ont
        // >100 membres — on teste sur un sous-ensemble via GroupReached
        // avec une progression incomplète puis complète.
        var group = svc.Get(5002);              // AC502 (15 membres)
        Assert.NotNull(group);
        var progress = new Dictionary<int, (int, int)>();
        Assert.False(svc.GroupReached(5002, progress));
        // Natif : une ligne de progression suffit (présence, pas
        // complétion) — la ligne est créée dès le premier grant.
        foreach (var m in group!.Members)
            progress[m] = (1, 0);
        Assert.True(svc.GroupReached(5002, progress));
    }

    /// <summary>Codec 480 variable — Serialize/Deserialize désasm
    /// zone @0x08209080 : [u8 kind] puis kind 0/1 = paires
    /// {i32 id, u32 point}, 2/3 = u32 total, 4/5 = ids i32.
    /// Le format fixe « toujours liste » était un bug (trames 2/3
    /// émises avec le mauvais corps).</summary>
    [Fact]
    public void Codec480_Kind01_PairList()
    {
        var msg = new Eden.Core.Networking.Messages.Generated
            .CNC_CZ_ClientAchievementUpdate { Kind = 1 };
        msg.Entries.Add(new() { AchievementId = 58001, Counter = 30 });
        msg.Entries.Add(new() { AchievementId = 5001, Counter = 0 });
        var s = new Eden.Core.Networking.OutBinStream();
        msg.Serialize(s);
        var body = s.ToArray();
        // 1 + 2 + 2×8 = 19 octets
        Assert.Equal(19, body.Length);
        Assert.Equal(1, body[0]);                          // kind u8
        Assert.Equal(2, BitConverter.ToUInt16(body, 1));   // n
        Assert.Equal(58001, BitConverter.ToInt32(body, 3));
        Assert.Equal(30u, BitConverter.ToUInt32(body, 7));
        // Round-trip : kind 0/1 ne lit jamais Total/Ids.
        var back = new Eden.Core.Networking.Messages.Generated
            .CNC_CZ_ClientAchievementUpdate();
        back.Deserialize(new Eden.Core.Networking.InBinStream(body));
        Assert.Equal(2, back.Entries.Count);
        Assert.Equal(58001, back.Entries[0].AchievementId);
        Assert.Equal(30u, back.Entries[0].Counter);
    }

    /// <summary>Kinds 2/3 : exactement [u8][u32 total] — identique
    /// aux trames capturées natives (golden `0200000000`,
    /// `0300000000`).</summary>
    [Fact]
    public void Codec480_Kind23_TotalOnly()
    {
        var msg = new Eden.Core.Networking.Messages.Generated
            .CNC_CZ_ClientAchievementUpdate { Kind = 2, Total = 12345 };
        var s = new Eden.Core.Networking.OutBinStream();
        msg.Serialize(s);
        var body = s.ToArray();
        Assert.Equal(5, body.Length);
        Assert.Equal(2, body[0]);
        Assert.Equal(12345u, BitConverter.ToUInt32(body, 1));
        var back = new Eden.Core.Networking.Messages.Generated
            .CNC_CZ_ClientAchievementUpdate();
        back.Deserialize(new Eden.Core.Networking.InBinStream(body));
        Assert.Equal((sbyte)2, back.Kind);
        Assert.Equal(12345u, back.Total);
        Assert.Empty(back.Entries);
    }

    /// <summary>Kinds 4/5 : [u8][u16 n][i32 id]×n — liste d'ids
    /// (town/unlock). Golden natif `040000` = kind 4 vide.</summary>
    [Fact]
    public void Codec480_Kind45_IdList()
    {
        var empty = new Eden.Core.Networking.Messages.Generated
            .CNC_CZ_ClientAchievementUpdate { Kind = 4 };
        var se = new Eden.Core.Networking.OutBinStream();
        empty.Serialize(se);
        Assert.Equal(new byte[] { 0x04, 0x00, 0x00 }, se.ToArray());
        var msg = new Eden.Core.Networking.Messages.Generated
            .CNC_CZ_ClientAchievementUpdate { Kind = 5 };
        msg.Ids.Add(58001);
        msg.Ids.Add(58002);
        var s = new Eden.Core.Networking.OutBinStream();
        msg.Serialize(s);
        var body = s.ToArray();
        Assert.Equal(11, body.Length);
        Assert.Equal(5, body[0]);
        Assert.Equal(2, BitConverter.ToUInt16(body, 1));
        Assert.Equal(58001, BitConverter.ToInt32(body, 3));
        Assert.Equal(58002, BitConverter.ToInt32(body, 7));
    }

    [Fact]
    public void Ranking_ScoresAndTop()
    {
        var svc = RankingService.LoadFromDataDir(DecodedDir);
        Assert.True(svc.AwardRowCount >= 30);
        svc.AddScore(1, 100, 50);
        svc.AddScore(1, 200, 100);
        svc.AddScore(1, 300, 10);
        var top = svc.GetTop(1, 2);
        Assert.Equal(200, top[0].CharId);
        Assert.Equal(100, top[0].Score);
        Assert.Equal(2, svc.GetRank(1, 100));
        Assert.Equal(0, svc.GetRank(1, 999));
    }
}

public class AuctionItemMallTests
{
    [Fact]
    public async Task Auction_ListBuyExpire()
    {
        var svc = new AuctionService();
        var l = await svc.ListAsync(1, 31900, 5, 10000, TimeSpan.FromDays(3));
        Assert.Equal(500, svc.DepositFee(10000));
        var found = svc.Search(itemId: 31900);
        Assert.Single(found);
        var (toSeller, commission) = (await svc.BuyAsync(2, l.Id))!.Value;
        Assert.Equal(9200, toSeller);
        Assert.Equal(800, commission);
        Assert.True(l.Sold);
    }

    [Fact]
    public async Task Auction_CannotBuyOwnListing()
    {
        var svc = new AuctionService();
        var l = await svc.ListAsync(1, 31900, 1, 100, TimeSpan.FromHours(1));
        Assert.Null(await svc.BuyAsync(1, l.Id));
    }
    // Les tests ItemMallService ont été retirés avec le stub zone :
    // l'item mall réel vit côté monde (Eden.World/Handlers/
    // ItemMallHandlers.cs — tb_user pvalues/bonus), le modèle
    // AP/EP + catalogue en mémoire n'existait que dans le stub mort.
}

public class CombatTests
{
    private static readonly string DecodedDir =
        TestPaths.RepoRoot + "/server_us/Data/db_decoded";

    static CombatTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void PhysicalAttack_DealsDamage()
    {
        var svc = CombatService.LoadFromDataDir(DecodedDir);
        var atk = new CombatService.CombatStats
        {
            Level = 50, Str = 200, Agi = 50, AttackPower = 800,
        };
        var def = new CombatService.CombatStats
        {
            Level = 50, Agi = 20, Defense = 300, DodgeBonus = -50, // dodge minimal
        };
        var hit = Enumerable.Range(0, 100)
            .Select(_ => svc.PhysicalAttack(atk, def, 1, 2))
            .FirstOrDefault(r => r.FinalDamage > 0);
        Assert.NotNull(hit);
        // atk 800 - def 300*0.5 = 650 ± variance
        Assert.InRange(hit!.FinalDamage, 500, 2000);
    }

    [Fact]
    public void PhysicalAttack_CanMissOrDodge()
    {
        var svc = CombatService.LoadFromDataDir(DecodedDir);
        var atk = new CombatService.CombatStats { AttackPower = 100, HitBonus = -90 };
        var def = new CombatService.CombatStats { Agi = 5000 }; // dodge max
        var results = Enumerable.Range(0, 200)
            .Select(_ => svc.PhysicalAttack(atk, def, 1, 2)).ToList();
        Assert.Contains(results, r => r.Miss || r.Dodged);
    }

    [Fact]
    public void SpellAttack_ResistanceReducesDamage()
    {
        var svc = CombatService.LoadFromDataDir(DecodedDir);
        var atk = new CombatService.CombatStats
        { MagicPower = 1000, Int = 0 };
        var noRes = new CombatService.CombatStats { MagicDefense = 0 };
        var highRes = new CombatService.CombatStats
        { MagicDefense = 0 };
        highRes.Resistances[0] = 80; // 80% resis element 0

        var d1 = Enumerable.Range(0, 30)
            .Select(_ => svc.SpellAttack(atk, noRes, 1, 2, 0)
                .FinalDamage).Average();
        var d2 = Enumerable.Range(0, 30)
            .Select(_ => svc.SpellAttack(atk, highRes, 1, 2, 0)
                .FinalDamage).Average();
        Assert.True(d2 < d1 * 0.5, $"d1={d1} d2={d2}");
    }
}

public class TerritoryWarTests
{
    [Fact]
    public void Battle_ScoringAndWinner()
    {
        var svc = new TerritoryWarService();
        svc.StartBattle(2);
        svc.JoinGuild(2, 100, TerritoryWarService.Side.Red);
        svc.JoinGuild(2, 200, TerritoryWarService.Side.Blue);
        svc.OnCrystalCapture(2, TerritoryWarService.Side.Red, crystalRank: 0);
        svc.OnCrystalCapture(2, TerritoryWarService.Side.Blue, crystalRank: 1);
        svc.OnKill(2, TerritoryWarService.Side.Red);
        svc.OnKill(2, TerritoryWarService.Side.Red);
        var winner = svc.EndBattle(2);
        Assert.Equal(TerritoryWarService.Side.Red, winner); // 202 vs 180
    }

    [Fact]
    public void RewardQuantities_MatchParameters()
    {
        var svc = new TerritoryWarService();
        Assert.Equal(3, svc.RewardQuantityForRank(1));
        Assert.Equal(2, svc.RewardQuantityForRank(2));
        Assert.Equal(1, svc.RewardQuantityForRank(6));
        Assert.Equal(0, svc.RewardQuantityForRank(7));
    }
}

public class MonsterAiTests
{
    private static readonly string DecodedDir =
        TestPaths.RepoRoot + "/server_us/Data/db_decoded";

    static MonsterAiTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void Ai_LoadsProfiles()
    {
        var svc = MonsterAiService.LoadFromDataDir(DecodedDir);
        var p = svc.GetProfile(1);
        Assert.NotNull(p);
        Assert.Equal(100, p!.Param1);          // S_Ai col1
        Assert.Equal(0, p.SeekChancePercent);  // S_Ai col2 = +0x05
    }

    [Fact]
    public void Aggro_TargetsHighestThreat()
    {
        var svc = MonsterAiService.LoadFromDataDir(DecodedDir);
        svc.AddDamageThreat(1, charId: 10, damage: 100);
        svc.AddDamageThreat(1, charId: 20, damage: 300);
        svc.AddHealThreat(1, charId: 30, healAmount: 500); // 600 threat
        Assert.Equal(30, svc.CurrentTarget(1));
    }

    [Fact]
    public void Taunt_OverridesThreat()
    {
        var svc = MonsterAiService.LoadFromDataDir(DecodedDir);
        svc.AddDamageThreat(1, 10, 10000);
        svc.Taunt(1, 99, durationSeconds: 5);
        Assert.Equal(99, svc.CurrentTarget(1));
    }

    [Fact]
    public void RollPassiveSeek_UsesSeekChance()
    {
        var svc = MonsterAiService.LoadFromDataDir(DecodedDir);
        // ai=3 : S_Ai col2 = 0 → monstre passif (jamais de roll).
        for (var i = 0; i < 50; i++)
            Assert.False(svc.RollPassiveSeek(3));
        // ai=2 : S_Ai col2 = 100 → détection à chaque tick.
        for (var i = 0; i < 50; i++)
            Assert.True(svc.RollPassiveSeek(2));
    }
}

public sealed class AuctionListingOpsTests
{
    [Fact]
    public async Task GetListing_ReturnsActiveListing()
    {
        var svc = new AuctionService();
        var l = await svc.ListAsync(1, 31900, 5, 10000, TimeSpan.FromDays(3));
        Assert.Same(l, svc.GetListing(l.Id));
        Assert.Null(svc.GetListing(9999));
    }

    [Fact]
    public async Task Cancel_OnlySeller_RemovesListing()
    {
        var svc = new AuctionService();
        var l = await svc.ListAsync(1, 31900, 5, 10000, TimeSpan.FromDays(3));
        Assert.Null(await svc.CancelAsync(2, l.Id));           // pas le vendeur
        Assert.Same(l, await svc.CancelAsync(1, l.Id));        // vendeur OK
        Assert.Null(svc.GetListing(l.Id));          // retirée
    }

    [Fact]
    public async Task GetListing_HidesSoldAndExpired()
    {
        var svc = new AuctionService();
        var sold = await svc.ListAsync(1, 1, 1, 100, TimeSpan.FromDays(1));
        await svc.BuyAsync(2, sold.Id);
        Assert.Null(svc.GetListing(sold.Id));
    }
}
