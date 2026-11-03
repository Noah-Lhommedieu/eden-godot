using System.Text;
using Xunit;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Services;
using Eden.Core.Game.Work;

namespace Eden.Tests;

/// <summary>Tests de la station combine (service 10 — CCombineOp
/// op 0x27) : CheckCombineItem (recette catégorie 6, catalyseur au
/// loc, booster 0x1f41, or, slot libre bVar4) et le roll natif.</summary>
public class CombineStationTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    static CombineStationTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static readonly GameDataService Data = LoadData();

    private static GameDataService LoadData()
    {
        var gd = new GameDataService(DbDir);
        gd.LoadAll();
        return gd;
    }

    private sealed class SeqRandom : Random
    {
        private readonly Queue<int> _q;
        public SeqRandom(params int[] values) => _q = new(values);
        public override int Next(int maxValue)
            => _q.Count > 0 ? _q.Dequeue() : 0;
    }

    private static CombineStationService Svc(Random? rand = null) =>
        new(Data, rand);

    private static ItemEntity Item(int itemId, int qty = 1) =>
        new() { ItemId = itemId, Quantity = qty, BagId = 0 };

    /// <summary>Une recette catégorie-6 réelle à 4 matériaux (le
    /// natif exige la catégorie et la cardinalité).</summary>
    private static WorkOrderParser.WorkCombineDef? FindRecipe() =>
        Data.WorkCombineById.Values.FirstOrDefault(
            d => d.Category == 6 && d.Materials.Count == 4
                && d.Materials.All(m => m.ItemId != 0));

    [Fact]
    public void Category6Recipes_ExistWith4Materials()
    {
        var r = FindRecipe();
        Assert.NotNull(r);
        Assert.Equal(4, r!.Materials.Count);
    }

    [Fact]
    public void CheckCombine_RejectsWrongCategory()
    {
        var bad = Data.WorkCombineById.Values
            .FirstOrDefault(d => d.Category != 6);
        if (bad == null) return;
        var chk = Svc().CheckCombine(bad.Id, Item(1), null,
            new List<ItemEntity>(), 999999, 20);
        Assert.False(chk.Ok);
        Assert.Equal(CombineStationService.Texts.BadRecipe,
            chk.TextId);
    }

    [Fact]
    public void CheckCombine_RejectsMissingMaterials()
    {
        var def = FindRecipe();
        if (def == null) return;
        var chk = Svc().CheckCombine(def!.Id, Item(1), null,
            new List<ItemEntity>(), 999999, 20);
        Assert.False(chk.Ok);
        Assert.Equal(0, chk.TextId); // abort silencieux natif → 434
    }

    [Fact]
    public void CheckCombine_RejectsCatalystNotAtLoc()
    {
        var def = FindRecipe();
        if (def == null) return;
        // Tous les matériaux présents en quantité, mais le catalyseur
        // pointé n'est PAS le matériau[0].
        var items = def!.Materials
            .Select(m => Item(m.ItemId, m.Quantity + 5)).ToList();
        var wrongCat = Item(def.Materials[1].ItemId,
            def.Materials[1].Quantity + 5);
        var chk = Svc().CheckCombine(def.Id, wrongCat, null,
            items, 999999, 20);
        Assert.False(chk.Ok);
    }

    [Fact]
    public void CheckCombine_AcceptsWithStackOverflow()
    {
        var def = FindRecipe();
        if (def == null) return;
        // Matériaux en excédent → aucune pile ne se vide → 1 slot
        // libre requis pour le produit.
        var items = def!.Materials
            .Select(m => Item(m.ItemId, m.Quantity + 5)).ToList();
        var cat = Item(def.Materials[0].ItemId,
            def.Materials[0].Quantity + 5);
        var chk = Svc().CheckCombine(def.Id, cat, null,
            items, Math.Max(def.Gold, 1), 1);
        Assert.True(chk.Ok);
        // … mais refusé sans slot libre (0x4f9 natif).
        chk = Svc().CheckCombine(def.Id, cat, null,
            items, Math.Max(def.Gold, 1), 0);
        Assert.False(chk.Ok);
        Assert.Equal(CombineStationService.Texts.NoInventorySpace,
            chk.TextId);
    }

    [Fact]
    public void CheckCombine_EmptiedStackFreesSlot()
    {
        var def = FindRecipe();
        if (def == null) return;
        // Catalyseur à la quantité exacte → sa pile sera vidée → pas
        // de slot libre requis.
        var items = def!.Materials
            .Select(m => Item(m.ItemId, m.Quantity)).ToList();
        var cat = Item(def.Materials[0].ItemId,
            def.Materials[0].Quantity);
        var chk = Svc().CheckCombine(def.Id, cat, null,
            items, Math.Max(def.Gold, 1), 0);
        Assert.True(chk.Ok);
    }

    [Fact]
    public void CheckCombine_RejectsInsufficientGold()
    {
        var def = FindRecipe();
        if (def == null || def!.Gold <= 0) return;
        var items = def.Materials
            .Select(m => Item(m.ItemId, m.Quantity)).ToList();
        var cat = Item(def.Materials[0].ItemId,
            def.Materials[0].Quantity);
        var chk = Svc().CheckCombine(def.Id, cat, null,
            items, def.Gold - 1, 0);
        Assert.False(chk.Ok);
    }

    [Fact]
    public void RollSuccess_NativeThreshold()
    {
        var def = new WorkOrderParser.WorkCombineDef
        { SuccessRate = 60 };
        // rand%100 < 60 + bonus : 59 succès, 60 échec, bonus décale.
        Assert.True(Svc(new SeqRandom(59)).RollSuccess(def));
        Assert.False(Svc(new SeqRandom(60)).RollSuccess(def));
        Assert.True(Svc(new SeqRandom(60)).RollSuccess(def, 1));
    }
}
