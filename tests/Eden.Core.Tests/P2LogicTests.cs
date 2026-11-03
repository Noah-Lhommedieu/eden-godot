using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;
using EdenClient.Ui.P2;
using Xunit;

namespace EdenClient.Tests;

/// <summary>Logique pure des fenêtres P2 (M7).</summary>
public class P2LogicTests
{
    private static ItemMallEntry E(int key, int idx, uint pt, uint sp = 0) =>
        new() { Key = key, Index = idx, Point = pt, SpecialPrice = sp, ItemId = 10 + idx };

    // ----------------------------------------------------------- Mall
    [Fact]
    public void Mall_FilterGroupsPages()
    {
        var all = new List<ItemMallEntry>();
        for (var i = 0; i < 19; i++) all.Add(E(102, i, 100));
        all.Add(E(104, 0, 50, 20));
        all.Add(E(204, 1, 70));
        var v = new MallView();
        Assert.Equal(20, v.Filtered(all).Count);            // unité 1 seulement
        Assert.Equal(new List<int> { 2, 4 }, v.Groups(all));
        v.SelectCategory(all, 2);                            // 0=toutes, 1=g2, 2=g4
        Assert.Equal(4, v.Group);
        Assert.Single(v.Filtered(all));
        v.SelectCategory(all, 0);
        Assert.Equal(-1, v.Group);
        var f = v.Filtered(all);
        Assert.Equal(3, MallView.PageCount(f.Count));
        v.Page = 99;
        Assert.Equal(4, v.PageItems(f).Count);               // 20 = 8+8+4
        Assert.Equal(2, v.Page);
        v.MoneyUnit = 2;
        Assert.Single(v.Filtered(all));
    }

    [Fact]
    public void Mall_HotBuyRequestSellText()
    {
        var hot = MallView.Hot(new[] { E(102, 1, 100, 50), E(102, 2, 100), E(102, 3, 100, 100) });
        Assert.Single(hot);
        var e = E(305, 7, 100);
        var r = MallView.BuyRequest(e, 42, "Bob");
        Assert.Equal(305, r.Key);
        Assert.Equal(7, r.Index);
        Assert.Equal((ushort)42, r.Version);
        Assert.Equal("Bob", r.Recipient);
        e.SellDate = 2000;
        Assert.Equal("pas encore en vente", MallView.SellText(e, 1000));
        e.SellDate = 0; e.NotSellDate = 0;
        Assert.Equal("en vente", MallView.SellText(e, 1000));
        e.NotSellDate = 1_700_000_000;
        Assert.StartsWith("fin de vente le", MallView.SellText(e, 1000));
        Assert.Equal("Spécial", MallView.CategoryName(99));
        Assert.Equal("Groupe 77", MallView.CategoryName(77));
    }

    // -------------------------------------------------------- Auction
    [Fact]
    public void Auction_TimeFilterSell()
    {
        Assert.Equal("", AuctionView.TimeLeft(0, 100));
        Assert.Equal("expirée", AuctionView.TimeLeft(99, 100));
        Assert.Equal("30min", AuctionView.TimeLeft(100 + 1800, 100));
        Assert.Equal("47h30", AuctionView.TimeLeft(100 + 47 * 3600 + 1800, 100));
        var es = new[]
        {
            new AuctionEntry { AuctionId = 1, ItemName = "Épée rouillée" },
            new AuctionEntry { AuctionId = 2, ItemName = "Potion" },
        };
        Assert.Single(AuctionView.Filter(es, "ÉPÉE", e => e.ItemName));
        Assert.Equal(2, AuctionView.Filter(es, " ", e => e.ItemName).Count);
        Assert.Null(AuctionView.SellRequest(-1, 0, "10", "0", out var err));
        Assert.Contains("sac", err);
        Assert.Null(AuctionView.SellRequest(0, 1, "abc", "", out err));
        Assert.Equal("Prix invalide.", err);
        var s = AuctionView.SellRequest(1, 2, "500", "900", out _)!;
        Assert.Equal((500, 900, (short)1, (short)2), (s.Price, s.Buyout, s.Bag, s.Slot));
        Assert.Equal(0, AuctionView.ParseAmount("-5"));
    }

    // ---------------------------------------------------------- Craft
    [Fact]
    public void Craft_RecipesDetailCatalyst()
    {
        var col = new Dictionary<int, CraftTable.CollectRecipe>
        {
            [1] = new() { Id = 1, Category = 0, CatalystItemId = 500, Name = "Minerai", CatalystQty = 2, Gold = 30 },
            [2] = new() { Id = 2, Category = 1, CatalystItemId = 501, Name = "Fleur", CatalystQty = 1, Gold = 10 },
        };
        string N(int id) => "obj" + id;
        var r = CraftView.Recipes(CraftTable.WorkTypes.Mining, col, null, N);
        Assert.Single(r);
        Assert.Equal(1, r[0].Id);
        Assert.Contains("2× obj500", r[0].Label);
        // catégorie inconnue → repli sur toutes les recettes
        Assert.Equal(2, CraftView.Recipes(14, col, null, N).Count);
        Assert.Equal(500, CraftView.WantedCatalyst(12, 1, col, null, _ => null));
        Assert.Contains("Catalyseur : 2× obj500", CraftView.Detail(1, col, null, N));
        Assert.Equal((2, 7), CraftView.FindInBag(500,
            new[] { (500, 9, 1), (500, 2, 7) }));
        Assert.Equal((-1, -1), CraftView.FindInBag(0, new[] { (0, 0, 0) }));
    }

    // ---------------------------------------------------------- Daily
    [Fact]
    public void Daily_Bits()
    {
        var d = new DailyView();
        d.Apply(dayIndex: 3, onlineSeconds: 3600, awardDays: 0b1111,
            awardRecords: 0b0011, kind: 0);
        Assert.True(d.Claimed(1) && d.Claimed(2));
        Assert.True(d.Claimable(3) && d.Claimable(4));
        Assert.False(d.Claimable(5));
        Assert.True(d.Current(4));
        Assert.Equal(3, d.CellState(1));
        Assert.Equal(2, d.CellState(3));
        Assert.Equal(1, d.CellState(4) == 2 ? 1 : 0);
        Assert.Equal(0, d.CellState(9));
        Assert.True(d.ShouldAutoOpen(0, new[] { 1, 2, 3, 4 }));
        Assert.False(d.ShouldAutoOpen(1, new[] { 1, 2, 3, 4 }));
        Assert.False(d.ShouldAutoOpen(0, new[] { 1, 2 }));
        Assert.Equal("Jour 4 — 4/6 accomplis", d.Status(new[] { 1, 2, 3, 4, 5, 6 }));
        d.Apply(0, 0, 0, 0, 4);
        Assert.Equal("Système désactivé sur le serveur.", d.Status(new[] { 1 }));
        // jour 64 : le masque 0x3f boucle sur le bit 63
        d.Apply(0, 0, long.MinValue, 0, 1);
        Assert.True(d.Completed(64));
    }

    // -------------------------------------------------------- Destiny
    [Fact]
    public void Destiny_Buttons()
    {
        Assert.False(DestinyView.CanRoll(null));
        var r = new DestinyState.Route { RollsLeft = 2, MovePoints = 0, Step = 50 };
        Assert.True(DestinyView.CanRoll(r));
        Assert.False(DestinyView.CanGo(r));
        r.MovePoints = 4;
        Assert.False(DestinyView.CanRoll(r));
        Assert.True(DestinyView.CanGo(r));
        Assert.Equal("Pas 4", DestinyView.Move(r));
        Assert.Equal("Pas -", DestinyView.Move(null));
        Assert.Equal(38, DestinyView.CurrentCell(r));
        Assert.Equal("Vit ?", DestinyView.Vit(-1));
        Assert.Equal("Sacs de fortune : —",
            DestinyView.Fortune(new Dictionary<int, int>()));
        Assert.Equal("Sacs : 5×2", DestinyView.Fortune(new Dictionary<int, int> { [5] = 2 }));
        Assert.Contains("100 vitalité", DestinyView.FailedText(0));
        Assert.Equal("Dés bonus", DestinyTable.TypeName(7));
    }

    // ---------------------------------------------------- Achievement
    [Fact]
    public void Achievement_TreeFilterProgress()
    {
        var defs = new[]
        {
            new AchievementTable.AchievementDef { Id = 1, GroupKey = 101, NeedNum = 10, SortIndex = 2 },
            new AchievementTable.AchievementDef { Id = 2, GroupKey = 101, NeedNum = 5, SortIndex = 1 },
            new AchievementTable.AchievementDef { Id = 3, GroupKey = 502, NeedNum = 1 },
            new AchievementTable.AchievementDef { Id = 4, GroupKey = 502, TargetIds = new[] { 1, 2, 3 } },
        };
        var tree = AchievementView.BuildTree(defs, g => "G" + g);
        Assert.Equal("Tous (4)", tree[0].Label);
        Assert.Contains(tree, n => n.Label.StartsWith("Conquête de monstres (2)"));
        Assert.Contains(tree, n => n.Label.StartsWith("Succès de ville (2)"));
        Assert.Contains(tree, n => n.Group == 502 && n.Label.Contains("G502 (2)"));
        var v = new AchievementView { FilterGroup = -2, Hundred = 5 };
        bool Done(int id) => id == 2 || id == 3;
        Assert.Equal(new[] { 3, 4 }, v.Rows(defs, Done).Select(d => d.Id));
        v.FilterGroup = 101;
        Assert.Equal(new[] { 2, 1 }, v.Rows(defs, Done).Select(d => d.Id)); // accompli d'abord
        Assert.Equal((10, 10), AchievementView.Progress(defs[0], _ => true, _ => 0));
        Assert.Equal((3, 10), AchievementView.Progress(defs[0], _ => false, _ => 3));
        Assert.Equal((10, 10), AchievementView.Progress(defs[0], _ => false, _ => 99));
        Assert.Equal((2, 3), AchievementView.Progress(defs[3], Done, _ => 0));
        Assert.Equal("Total : 9 pts\nChasse : 4 pts\nVille : 5 pts",
            AchievementView.Totals(9, 4, 5));
    }

    // ---------------------------------------------------------- Stats
    [Fact]
    public void Stats_RenderAndExpertise()
    {
        var m = new ZoneAdvStatsInfo { MaxHits = 2, ChanceMult = 1.5f, DamageMult = 2f };
        m.Formulas.Add(new ZoneAdvStatsInfo.Formula("P-ATK", "10+5", 15f, ""));
        m.Formulas.Add(new ZoneAdvStatsInfo.Formula("Hit Rate", "x", 95.5f, "%"));
        m.Formulas.Add(new ZoneAdvStatsInfo.Formula("Zzz", "y", 1f, ""));
        m.Sources.Add(new ZoneAdvStatsInfo.Source(7, 2, 2, 50f, 100f, 40f, 50f, 20f));
        var t = StatsView.Render(m, id => "Ench" + id);
        Assert.Contains("[Offensive]", t);
        Assert.Contains("ATQ physique = 15", t);
        Assert.Contains("Précision = 95.5%", t);
        Assert.Contains("Zzz = 1", t);
        Assert.Contains("Ench7 x2  [Proc 50%]", t);
        // gate 50% × c1 100% × 40 + p2 0.5×0.5×20 = 20 + 5
        Assert.Contains("+25%", t);
        Assert.Contains("Aucune source", StatsView.Render(new ZoneAdvStatsInfo(), _ => ""));
        Assert.Equal("PV +10  ATQ +3", ExpertiseView.StatsText(new[] { 10, 0, 3, 0, 0 }));
        Assert.Equal(new[] { 5, 0 }, ExpertiseView.Delta(new[] { 15, 4 }, new[] { 10, 4 }));
        Assert.Equal("Mage — Aucun bonus", ExpertiseView.Title("Mage", 0));
    }

    // --------------------------------------------------------- Divers
    [Fact]
    public void Services_Text()
    {
        Assert.Equal("Colisée, inscription : 3", ServicePanels.ColosseumJoin(3));
        Assert.Contains("1. Ana (Fam) — 12", ServicePanels.FamilyKills(new[] { (1, "Ana", "Fam", 12) }));
        Assert.Equal("Rang : 4 — 1/2 3", ServicePanels.Rank(4, 1, 2, 3));
        Assert.Equal("Salle — 3/8", Hotel.RoomLine("Salle", 3, 8));
        Assert.Equal(7, Hotel.RoomAt(new[] { 5, 7 }, 1));
        Assert.Equal(-1, Hotel.RoomAt(new[] { 5, 7 }, 2));
        Assert.Equal((short)12, Battlefield.ParseId("12"));
        Assert.Equal((short)0, Battlefield.ParseId("x"));
        Assert.Equal("Entrée proposée (champ 3)", Battlefield.OfferText(3));
        Assert.Equal(3, Lottery.Altars.Length);
        Assert.Equal(0x9d31, Lottery.Altars[0].Ticket);
        Assert.Equal("Objet n°9", Inspect.ItemLine(9, null));
        Assert.Equal("Casque", Inspect.ItemLine(9, "Casque"));
    }

    [Fact]
    public void Roll_Ultimate_Trait_Portal_Costume()
    {
        var r = new RollState();
        Assert.Null(r.Choose(1));
        r.Offer(7);
        Assert.True(r.Open);
        Assert.Equal((7u, (short)2), r.Choose(2));
        Assert.False(r.Open);
        Assert.Null(r.Choose(0));

        Assert.Equal("ULTIME  Niv 3   EXP 50/200   Atouts 2", UltimateBar.Text(3, 50, 200, 2));
        Assert.Equal(0.25, UltimateBar.Fraction(50, 200));
        Assert.Equal(0, UltimateBar.Fraction(5, 0));
        Assert.Equal(1, UltimateBar.Fraction(500, 200));

        var t = new HeroTraitState();
        var n = 0;
        t.Changed += () => n++;
        t.Set(9);                       // hors bornes → 0, inchangé
        Assert.Equal(0, n);
        t.Set(3);
        Assert.Equal("ui/uiicon/NatrueIamge03", t.IconPath);
        t.Set(3);
        Assert.Equal(1, n);
        t.Set(0);
        Assert.Null(t.IconPath);

        var p = new PortalArrivalState();
        p.Begin();
        Assert.True(p.Pending);
        p.Arrived();
        Assert.False(p.Pending);
        Assert.Equal(1, p.Version);

        Assert.Equal(0, Costume.PageMod(2));
        Assert.Equal(2, Costume.PageMod(3));
        Assert.Equal(1, Costume.PageMod(4));
        Assert.True(Costume.Accepts(3, 2, 2, 5));      // 5%3 == 2
        Assert.False(Costume.Accepts(3, 2, 2, 6));
        Assert.False(Costume.Accepts(2, 1, 2, 3));     // mauvais emplacement
    }

    [Fact]
    public void Couple_Social_Text()
    {
        var s = new LoverState();
        Assert.Equal("Partenaire : —", CoupleView.Partner(s));
        Assert.Equal("Célibataire", CoupleView.Status(s));
        s.Apply(new ZoneLoverUpdate { Value = 5, Text = "Eve" });
        Assert.Equal("Partenaire : Eve", CoupleView.Partner(s));
        Assert.Equal("En couple ♥", CoupleView.Status(s));
        Assert.Equal("Aucune cérémonie prévue.", CoupleView.Weddings(new List<ZoneWeddingsInfo.Entry>()));
        Assert.Equal("Liste d'amis — <Fam>  Niv 3", SocialView.Header("Fam", 3));
        Assert.Equal("Liste d'amis", SocialView.Header("", 0));
        Assert.Equal("mutuel", SocialView.FriendType(3));
    }
}

public class AudioCatalogTests
{
    [Fact]
    public void Bgm_FromNodeOrScene()
    {
        string Row(int id, string scene, string a, string b)
        {
            var f = new string[27];
            Array.Fill(f, "x");
            f[0] = id.ToString(); f[4] = scene; f[23] = a; f[24] = b;
            return string.Join("|", f);
        }
        var rows = new[] { Row(1, "S001", "", "bgm003"), Row(2, "S002", "bgm012", "") };
        Assert.Equal("bgm012", EdenClient.Vfs.AudioCatalog.BgmForNode(rows, 2, "S002"));
        Assert.Equal("bgm003", EdenClient.Vfs.AudioCatalog.BgmForNode(rows, 0, "s001"));
        Assert.Null(EdenClient.Vfs.AudioCatalog.BgmForNode(rows, 9, "S009"));
        Assert.Null(EdenClient.Vfs.AudioCatalog.BgmForNode(new[] { Row(5, "S005", "", "") }, 5, "S005"));
        Assert.Equal(2, EdenClient.Vfs.AudioCatalog.NodeIdOf("S002"));
        Assert.Equal(0, EdenClient.Vfs.AudioCatalog.NodeIdOf("zz"));
    }

    [Fact]
    public void MotionSounds_Parsed()
    {
        var lines = new[]
        {
            "[INFO]",
            "DB1040=b,1421,0.188804,bow1,NULL,atka011.wav,attack12",
            "DB1701=g,1421,0.330399,bow1,NULL,atka011.wav,attack12",
            "DB1702=g,1421,0.100000,bow2,NULL,atka012.wav,attack12",
            "DB1800=g,1421,0.5,k,NULL,NULL,attack12",
            "DB1900=g,1421,abc,k,NULL,x.wav,attack12",
        };
        var m = EdenClient.Vfs.AudioCatalog.ParseMotionSounds(lines);
        Assert.Single(m);
        var l = m["ATTACK12"];                                // insensible à la casse
        Assert.Equal(3, l.Count);
        Assert.Equal("atka012.wav", l[0].Sound);              // trié par temps
        Assert.Equal("sound/atka011", EdenClient.Vfs.AudioCatalog.SoundKey("atka011.wav"));
    }
}

[Collection("SharedCoreState")]
public class NativeFrTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "eden-nfr-" + Guid.NewGuid().ToString("N"));

    public NativeFrTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "data/db/lang/fr"));
        Directory.CreateDirectory(Path.Combine(_dir, "data/db/lang/en"));
        File.WriteAllText(Path.Combine(_dir, "ui_text_fr.tsv"), "你好\tBonjour\n");
        File.WriteAllText(Path.Combine(_dir, "ui_text_en.tsv"), "你好\tHello\n再見\tBye\n");
        File.WriteAllText(Path.Combine(_dir, "ui_text_en_fr.tsv"), "Close\tFermer\n");
        string? Res(string rel)
        {
            var p = Path.Combine(_dir, rel.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(p) ? p : null;
        }
        EdenClient.Data.TextResolver.ResolveFile = Res;
        EdenClient.Data.UiTextEn.ResolveFile = Res;
        EdenClient.Data.UiTextEn.Reset();
    }

    public void Dispose()
    {
        EdenClient.Data.TextResolver.NativeOnly = false;
        EdenClient.Data.TextResolver.SetLanguage("en");
        EdenClient.Data.TextResolver.ResolveFile = null;
        EdenClient.Data.UiTextEn.ResolveFile = null;
        EdenClient.Data.UiTextEn.Reset();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    [Fact]
    public void NativeOnly_ForcesFrAndNeverMixesEn()
    {
        EdenClient.Data.TextResolver.NativeOnly = true;
        Assert.Equal("fr", EdenClient.Data.TextResolver.Language);
        Assert.Equal("Bonjour", EdenClient.Data.UiTextEn.Translate("你好"));
        // clé absente du dico FR : renvoyée telle quelle, pas la version anglaise
        Assert.Equal("再見", EdenClient.Data.UiTextEn.Translate("再見"));
        Assert.Equal("Fermer", EdenClient.Data.UiTextEn.Translate("Close"));
    }

    [Fact]
    public void NativeOnly_NoFrTsv_NoEnFallback()
    {
        File.Delete(Path.Combine(_dir, "ui_text_fr.tsv"));
        EdenClient.Data.TextResolver.NativeOnly = true;
        Assert.Equal("你好", EdenClient.Data.UiTextEn.Translate("你好"));
    }
}

public class ThemeTests
{
    [Fact]
    public void ElevenThemes_AndBasePalette()
    {
        Assert.Equal(11, EdenClient.Settings.ClientSettings.ThemeNames.Length);
        Assert.Equal(EdenClient.Ui.ThemePresets.Names, EdenClient.Settings.ClientSettings.ThemeNames);
        var b = EdenClient.Ui.ThemePalette.For("Irongray");
        Assert.Equal(EdenClient.Ui.Rgba.From8(47, 45, 40), b.WindowBody);
        Assert.Equal(b, EdenClient.Ui.ThemePalette.For("inconnu").WindowBody == b.WindowBody ? b : default);
    }

    [Fact]
    public void ColoredThemes_ShiftTowardsTheirHue()
    {
        var b = EdenClient.Ui.ThemePalette.Irongray;
        var red = EdenClient.Ui.ThemePalette.For("RED");
        var blue = EdenClient.Ui.ThemePalette.For("Blue");
        Assert.True(red.TitleBand.R - red.TitleBand.B > b.TitleBand.R - b.TitleBand.B);
        Assert.True(blue.TitleBand.B - blue.TitleBand.R > b.TitleBand.B - b.TitleBand.R);
        foreach (var n in EdenClient.Settings.ClientSettings.ThemeNames)
        {
            var p = EdenClient.Ui.ThemePalette.For(n);
            foreach (var c in new[] { p.WindowBody, p.TitleBand, p.Parchment, p.Tab, p.GreyBtn })
                Assert.InRange(c.R + c.G + c.B, 0f, 3f);
        }
    }

    [Fact]
    public void UiScale_Clamped()
    {
        Assert.Equal(0.5f, EdenClient.Settings.ClientSettings.ClampUiScale(0.1f));
        Assert.Equal(2.0f, EdenClient.Settings.ClientSettings.ClampUiScale(9f));
        Assert.Equal(1.25f, EdenClient.Settings.ClientSettings.ClampUiScale(1.25f));
    }
}

public class PerfStatsTests
{
    [Fact]
    public void Percentiles_AndBudget()
    {
        var f = new EdenClient.Game.FrameStats(100);
        for (var i = 1; i <= 100; i++) f.Add(i / 10.0, 100);   // 0,1 .. 10 ms
        Assert.Equal(5.0, f.P50, 6);
        Assert.Equal(9.5, f.P95, 6);
        Assert.Equal(10.0, f.Max, 6);
        Assert.Equal(100, f.AllocMean, 6);
        Assert.Empty(f.Check(EdenClient.Game.PerfBudget.M8, 1000, 500));
        f.Add(50, 100000);
        var v = f.Check(EdenClient.Game.PerfBudget.M8, 9999, 5000);
        Assert.Contains(v, s => s.StartsWith("chargement carte"));
        Assert.Contains(v, s => s.StartsWith("RAM"));
    }

    [Fact]
    public void Window_Rolls_And_Violations()
    {
        var f = new EdenClient.Game.FrameStats(50);
        for (var i = 0; i < 200; i++) f.Add(30, 5000);
        Assert.Equal(50, f.Count);
        var v = f.Check(EdenClient.Game.PerfBudget.M8);
        Assert.Equal(2, v.Count);
        var p = new EdenClient.Game.PhaseTimes();
        p.Add("tables", 120); p.Add("terrain", 80);
        Assert.Equal(200, p.Total);
        Assert.Contains("tables=120ms", p.Summary());
    }
}
