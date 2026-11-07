using Eden.Core.Game.Services;

namespace Eden.Tests;

/// <summary>
/// Filtres du catalogue hôtel des ventes — port de
/// CExecutionerDBO::QueryAuctionBuy (mission @0x08160710) avec
/// l'adaptation « 0/vide = pas de filtre » du wire Godot.
/// </summary>
public class AuctionFilterTests
{
    private static AuctionService.AuctionQuery All
        => new(0, 0, 0, 0, 0, 0, 0, 0, 0, "", 0);

    private static AuctionService.AuctionDenorm Denorm(
        int type = 0, int level = 0, long cls = 0,
        int qFilter = 1, string name = "")
        => new(type, level, cls, qFilter, name);

    private static async Task<AuctionService.AuctionListing> Sell(
        AuctionService svc, int seller, int item, int price,
        AuctionService.AuctionDenorm d)
        => await svc.ListAsync(seller, item, 1, price,
            TimeSpan.FromHours(1), denorm: d);

    [Fact]
    public async Task Defaults_PassEverything()
    {
        var svc = new AuctionService();
        await Sell(svc, 1, 10, 500, Denorm());
        await Sell(svc, 1, 11, 600, Denorm(type: 45));
        var (entries, total) = svc.Search(All);
        Assert.Equal(2, total);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task ItemType_Equality()
    {
        var svc = new AuctionService();
        await Sell(svc, 1, 10, 500, Denorm(type: 45));
        await Sell(svc, 1, 11, 600, Denorm(type: 52));
        var (entries, total) = svc.Search(All with { ItemType = 45 });
        Assert.Equal(1, total);
        Assert.Equal(10, entries[0].ItemId);
    }

    [Fact]
    public async Task Price_And_Level_Ranges()
    {
        var svc = new AuctionService();
        await Sell(svc, 1, 10, 500, Denorm(level: 20));
        await Sell(svc, 1, 11, 1500, Denorm(level: 70));
        await Sell(svc, 1, 12, 900, Denorm(level: 40));
        // Borne prix [600, 1000] + niveau ≤ 50.
        var (entries, _) = svc.Search(All with
        {
            PriceMin = 600, PriceMax = 1000, LevelMax = 50,
        });
        Assert.Single(entries);
        Assert.Equal(12, entries[0].ItemId);
        // Borne haute seule (min natif 0).
        (entries, _) = svc.Search(All with { PriceMax = 600 });
        Assert.Single(entries);
        Assert.Equal(10, entries[0].ItemId);
    }

    [Fact]
    public async Task QualityFilter_Bitmask()
    {
        var svc = new AuctionService();
        // quality_filter = 1<<quality (SaveAuction natif) : q0→1, q3→8.
        await Sell(svc, 1, 10, 500, Denorm(qFilter: 1));
        await Sell(svc, 1, 11, 600, Denorm(qFilter: 8));
        var (entries, _) = svc.Search(All with { QualityMask = 8 });
        Assert.Single(entries);
        Assert.Equal(11, entries[0].ItemId);
        // Masque couvrant les deux → 2 résultats.
        (entries, _) = svc.Search(All with { QualityMask = 1 | 8 });
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public async Task ClassMask_Bitmask()
    {
        var svc = new AuctionService();
        // class = masque i64 de classes autorisées (c_item col18).
        await Sell(svc, 1, 10, 500, Denorm(cls: 0b110));  // bits 1,2
        await Sell(svc, 1, 11, 600, Denorm(cls: 0b1000)); // bit 3
        var (entries, _) = svc.Search(All with { ClassMask = 0b010 });
        Assert.Single(entries);
        Assert.Equal(10, entries[0].ItemId);
        // Item sans restriction (class=0) : le filtre natif l'exclut
        // quand un masque est demandé (0 & mask = 0).
        (entries, _) = svc.Search(All with { ClassMask = 0b1000 });
        Assert.Single(entries);
        Assert.Equal(11, entries[0].ItemId);
    }

    [Fact]
    public async Task Keyword_MatchesItemName()
    {
        var svc = new AuctionService();
        await Sell(svc, 1, 10, 500, Denorm(name: "Épée longue+7"));
        await Sell(svc, 1, 11, 600, Denorm(name: "Bouclier"));
        var (entries, _) = svc.Search(All with { Keyword = "épée" });
        Assert.Single(entries);
        Assert.Equal(10, entries[0].ItemId);
        // Casse ignorée.
        (entries, _) = svc.Search(All with { Keyword = "BOUC" });
        Assert.Single(entries);
        Assert.Equal(11, entries[0].ItemId);
    }

    [Fact]
    public async Task Sort_NativeAxes_AndDesc()
    {
        var svc = new AuctionService();
        await Sell(svc, 1, 10, 900, Denorm(level: 30, qFilter: 4));
        await Sell(svc, 1, 11, 100, Denorm(level: 10, qFilter: 1));
        await Sell(svc, 1, 12, 500, Denorm(level: 60, qFilter: 8));
        // Tri 3 = prix asc.
        var (entries, _) = svc.Search(All with { Sort = 3 });
        Assert.Equal(new[] { 11, 12, 10 },
            entries.Select(e => e.ItemId).ToArray());
        // Tri 0 = level, desc.
        (entries, _) = svc.Search(All with { Sort = 0, Desc = 1 });
        Assert.Equal(new[] { 12, 10, 11 },
            entries.Select(e => e.ItemId).ToArray());
        // Tri 1 = quality (mask = 1<<quality, monotone).
        (entries, _) = svc.Search(All with { Sort = 1, Desc = 1 });
        Assert.Equal(new[] { 12, 10, 11 },
            entries.Select(e => e.ItemId).ToArray());
    }

    [Fact]
    public async Task Pagination_TwentyPerPage()
    {
        var svc = new AuctionService();
        for (var i = 0; i < 25; i++)
            await Sell(svc, 1, 100 + i, 100 + i, Denorm());
        var (p0, total) = svc.Search(All with { Sort = 3 });
        Assert.Equal(25, total);
        Assert.Equal(20, p0.Count);
        var (p1, _) = svc.Search(All with { Sort = 3, Page = 1 });
        Assert.Equal(5, p1.Count);
        // Page négative → 0 (sVar11 clampé à 0 côté natif).
        (p0, _) = svc.Search(All with { Sort = 3, Page = -1 });
        Assert.Equal(20, p0.Count);
        // Les pages ne se recoupent pas.
        Assert.Empty(p0.Select(e => e.ItemId)
            .Intersect(p1.Select(e => e.ItemId)));
    }

    [Fact]
    public async Task Sold_And_Expired_Excluded()
    {
        var svc = new AuctionService();
        var l = await Sell(svc, 1, 10, 500, Denorm());
        await svc.BuyAsync(2, l.Id);
        var (entries, total) = svc.Search(All);
        Assert.Equal(0, total);
        Assert.Empty(entries);
    }

    [Fact]
    public async Task Denorm_Persisted_OnListing()
    {
        // Les champs dénorm arrivent intacts sur l'annonce en mémoire
        // (l'entité SQL écrite porte les mêmes valeurs).
        var svc = new AuctionService();
        var l = await Sell(svc, 1, 10, 500,
            Denorm(type: 45, level: 42, cls: 0x1fffffffffffffff,
                qFilter: 16, name: "Marteau+3"));
        Assert.Equal(45, l.ItemType);
        Assert.Equal(42, l.Level);
        Assert.Equal(0x1fffffffffffffff, l.Class);
        Assert.Equal(16, l.QualityFilter);
        Assert.Equal("Marteau+3", l.ItemName);
    }
}
