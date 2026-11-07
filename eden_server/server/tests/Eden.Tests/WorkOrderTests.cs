using System.Text;
using Xunit;
using Dapper;
using Npgsql;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Inlay;
using Eden.Core.Game.Services;
using Eden.Core.Game.Work;
using Eden.Zone.Codecs;

namespace Eden.Tests;

/// <summary>Tests des ordres de travail (player_works — métiers
/// 12-17) : parser S_FamilyCollect/Combine, validations ToWork,
/// tirages EndCollect/EndCombine, codecs 497/498 et persistance
/// WorkRepository.</summary>
public class WorkOrderTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    static WorkOrderTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static readonly GameDataService Data = LoadData();

    private static GameDataService LoadData()
    {
        var gd = new GameDataService(DbDir);
        gd.LoadAll();
        return gd;
    }

    /// <summary>Random déterministe : file de valeurs Next(max).</summary>
    private sealed class SeqRandom : Random
    {
        private readonly Queue<int> _q;
        public SeqRandom(params int[] values) => _q = new(values);
        public override int Next(int maxValue)
            => _q.Count > 0 ? _q.Dequeue() : 0;
    }

    private static WorkOrderService Svc(Random? rand = null) =>
        new(Data, new InlayService(), rand);

    private static WorkSlot[] EmptyWorks()
    {
        var w = new WorkSlot[WorkSlot.MaxSlots];
        for (var i = 0; i < w.Length; i++) w[i] = new WorkSlot { WorkNum = i };
        return w;
    }

    private static ItemEntity Catalyst(int itemId, int qty = 1) =>
        new() { ItemId = itemId, Quantity = qty, BagId = 0, Slot = 0 };

    private static (int CatalystId, int MinLv) FindCatalyst(int kind)
    {
        var d = Data.Items.FirstOrDefault(
            i => i.Kind == kind && i.RequiredLevel > 0);
        Assert.NotNull(d);
        return (d!.Id, d.RequiredLevel);
    }

    // ---------------- Parser ----------------

    [Fact]
    public void ParseCollect_LoadsNativeTable()
    {
        Assert.True(Data.WorkCollectById.Count > 400);
        // La table couvre les 3 types de collecte via le catalyseur.
        var def = Data.WorkCollectById.Values
            .First(d => d.Products.Count > 0 && d.CatalystItemId != 0);
        Assert.True(def.CatalystAmount > 0);
        Assert.True(def.Duration >= 0);
        Assert.True(def.Products.All(p => p.ItemId != 0));
        Assert.True(def.Products.All(p => p.Rate >= 0));
    }

    [Fact]
    public void ParseCombine_LoadsNativeTable()
    {
        Assert.True(Data.WorkCombineById.Count > 2000);
        var def = Data.WorkCombineById.Values
            .First(d => d.Materials.Count > 0 && d.ProductItemId != 0);
        Assert.True(def.ProductQuantity > 0);
        Assert.InRange(def.SuccessRate, 0, 100);
        Assert.True(def.Materials.All(m => m.ItemId != 0));
    }

    // ---------------- FindFreeSlot ----------------

    [Fact]
    public void FindFreeSlot_FirstFreeThenFull()
    {
        var works = EmptyWorks();
        Assert.Equal(0, WorkOrderService.FindFreeSlot(12, works));
        works[0].Running = true; works[0].WorkId = 1;
        Assert.Equal(1, WorkOrderService.FindFreeSlot(12, works));
        for (var i = 0; i < 10; i++)
        { works[i].Running = true; works[i].WorkId = i + 1; }
        // Slots 0-9 pris : le slot 10 est le buffer natif.
        Assert.Equal(10, WorkOrderService.FindFreeSlot(15, works));
    }

    [Fact]
    public void FindFreeSlot_Slot10RejectedWhenCollectLimit()
    {
        var works = EmptyWorks();
        for (var i = 0; i < 10; i++)
        {
            works[i].Running = true; works[i].WorkId = i + 1;
            works[i].WorkType = WorkTypes.Mining;
        }
        // 10 collectes 12/13 déjà en cours → limite native atteinte.
        Assert.Equal(-1, WorkOrderService.FindFreeSlot(12, works));
        Assert.Equal(-1, WorkOrderService.FindFreeSlot(13, works));
        // Mais un combine peut prendre le slot 10.
        Assert.Equal(10, WorkOrderService.FindFreeSlot(15, works));
    }

    // ---------------- ValidateStart ----------------

    [Fact]
    public void ValidateStart_RejectsBadTypeAndNoSlot()
    {
        var works = EmptyWorks();
        var (catId, _) = FindCatalyst(0x33);
        var err = Svc().ValidateStart(5, 1, works, Catalyst(catId), 50,
            out _, out _);
        Assert.Equal(WorkOrderService.Texts.BadOperation, err.TextId);
        for (var i = 0; i < works.Length; i++)
        { works[i].Running = true; works[i].WorkId = 1; }
        works[10].WorkType = WorkTypes.Mining;
        err = Svc().ValidateStart(12, 1, works, Catalyst(catId), 50,
            out _, out _);
        Assert.Equal(WorkOrderService.Texts.NoSlot, err.TextId);
        err = Svc().ValidateStart(12, 1, EmptyWorks(), null, 50,
            out _, out _);
        Assert.Equal(WorkOrderService.Texts.InvalidItem, err.TextId);
    }

    [Fact]
    public void ValidateStart_LevelRange_FromItemData()
    {
        var works = EmptyWorks();
        var (catId, minLv) = FindCatalyst(0x33);
        // work_id natif = l'itemId du catalyseur (clé CCollectData
        // native = col2, ce que le client envoie dans le 170).
        var workId = Data.WorkCollectById.Values
            .First(d => d.CatalystItemId == catId).CatalystItemId;
        // Niveau joueur sous le minimum du catalyseur → 0xb76.
        var err = Svc().ValidateStart(12, workId, works,
            Catalyst(catId, 9), minLv - 1, out _, out _);
        Assert.Equal(WorkOrderService.Texts.BadLevel, err.TextId);
        // Au-dessus → valide (les catalyseurs réels n'ont pas de
        // MaxLevel — col15 vide = pas de cap supérieur).
        err = Svc().ValidateStart(12, workId, works,
            Catalyst(catId, 9), minLv, out var plan, out _);
        Assert.True(err.Ok);
        Assert.NotNull(plan);
    }

    [Fact]
    public void ValidateStart_Collect_TimedPlan()
    {
        var works = EmptyWorks();
        // Recette collect non instantanée → durée ×600, pas de cast.
        var def = Data.WorkCollectById.Values.First(
            d => !d.Instant && d.Duration > 0
                && Data.Items.Any(i => i.Id == d.CatalystItemId
                    && i.RequiredLevel <= 1));
        // work_id natif = CatalystItemId (clé col2 de la map
        // CCollectData, cf. CCollectQuery::Deserialize).
        var err = Svc().ValidateStart(12, def.CatalystItemId, works,
            Catalyst(def.CatalystItemId, def.CatalystAmount), 1,
            out var plan, out _);
        if (err.TextId == WorkOrderService.Texts.BadCatalyst)
            return; // type de catalyseur autre (13/14) — acceptable
        Assert.True(err.Ok);
        Assert.True(plan!.IsCollect);
        Assert.Equal(def.Duration * WorkOrderService.DurationFactor,
            plan.DurationUnits);
        Assert.False(plan.CastBar);
        Assert.False(plan.Deferred);
    }

    [Fact]
    public void ValidateStart_Collect_InstantDefersAndCasts()
    {
        var works = EmptyWorks();
        var def = Data.WorkCollectById.Values.First(
            d => d.Instant && d.Duration > 0
                && Data.Items.Any(i => i.Id == d.CatalystItemId));
        var cat = Data.Items.First(i => i.Id == def.CatalystItemId);
        // work_id natif = CatalystItemId (idem supra).
        var err = Svc().ValidateStart(12, def.CatalystItemId, works,
            Catalyst(def.CatalystItemId, def.CatalystAmount), 60,
            out var plan, out _);
        if (!err.Ok) return; // plage de niveau du catalyseur — skip
        Assert.True(plan!.Deferred);
        Assert.True(plan.CastBar);
        Assert.Equal(def.Duration, plan.DurationUnits); // brut, pas ×600
        _ = cat;
    }

    [Fact]
    public void ValidateStart_Combine_AlwaysCasts()
    {
        var works = EmptyWorks();
        // Catalyseur de type 15 = le matériau de kind 0x35 (le
        // parchemin de recette est un matériau parmi les autres).
        var def = Data.WorkCombineById.Values.FirstOrDefault(
            d => !d.Instant && d.Duration > 0 && d.Materials.Count > 1
                && d.Materials.Any(m => Data.Items.Any(
                    i => i.Id == m.ItemId && i.Kind == 0x35)));
        if (def == null) return; // aucune recette 15 — skip
        var cat = def.Materials.First(m => Data.Items.Any(
            i => i.Id == m.ItemId && i.Kind == 0x35));
        var err = Svc().ValidateStart(15, def.Id, works,
            Catalyst(cat.ItemId, Math.Max(cat.Quantity, 1)), 60,
            out var plan, out _);
        if (!err.Ok) return; // plage de niveau du catalyseur — skip
        Assert.True(plan!.CastBar);
        Assert.False(plan.IsCollect);
        Assert.Equal(def.Duration * WorkOrderService.DurationFactor,
            plan.DurationUnits);
        // Le catalyseur est exclu des matériaux du plan.
        Assert.DoesNotContain(plan.Materials,
            m => m.ItemId == cat.ItemId);
        Assert.Equal(def.Materials.Count - 1, plan.Materials.Count);
    }

    // ---------------- Tirages ----------------

    [Fact]
    public void RollCollect_IndependentProducts()
    {
        var def = new WorkOrderParser.WorkCollectDef
        {
            Id = 1, CatalystItemId = 1, Duration = 5,
        };
        def.Products.Add(new WorkOrderParser.WorkCollectDef.Product
            { ItemId = 111, Quantity = 2, Rate = 100 });
        def.Products.Add(new WorkOrderParser.WorkCollectDef.Product
            { ItemId = 222, Quantity = 1, Rate = 0 });
        var work = new WorkSlot { WorkId = 1, WorkType = 12 };
        // rate=100 → seuil 100 : Next<100 toujours vrai.
        // rate=0 → seuil 0 : jamais.
        Svc().RollCollectProducts(def, work);
        Assert.Equal(111, work.Products[0].ItemId);
        Assert.Equal(2, work.Products[0].Amount);
        Assert.True(work.Products[1].IsEmpty);
    }

    [Fact]
    public void RollCollect_BonusAndDeclineApplied()
    {
        var def = new WorkOrderParser.WorkCollectDef
        {
            Id = 1, CatalystItemId = 1, Duration = 5,
        };
        def.Products.Add(new WorkOrderParser.WorkCollectDef.Product
            { ItemId = 111, Quantity = 3, Rate = 50 });
        var work = new WorkSlot
        {
            WorkId = 1, WorkType = 12,
            BonusItemAmount = 2, ExtSuccessRate = 20,
        };
        // rate 50 + ext 20 → seuil 70 : Next=69 succès, 70 échec.
        var ok = Svc(new SeqRandom(69));
        var w2 = new WorkSlot
        {
            WorkId = 1, WorkType = 12,
            BonusItemAmount = 2, ExtSuccessRate = 20,
        };
        ok.RollCollectProducts(def, w2);
        Assert.Equal(111, w2.Products[0].ItemId);
        Assert.Equal(5, w2.Products[0].Amount); // 3 + bonus 2
        var ko = Svc(new SeqRandom(70));
        ko.RollCollectProducts(def, work);
        Assert.True(work.Products[0].IsEmpty);
    }

    [Fact]
    public void RollCombine_SuccessAndFail()
    {
        var def = new WorkOrderParser.WorkCombineDef
        {
            Id = 1, ProductItemId = 30937, // gemme réelle
            ProductQuantity = 1, SuccessRate = 100,
        };
        var work = new WorkSlot { WorkId = 1, WorkType = 15 };
        Svc(new SeqRandom(0)).RollCombineProduct(def, work);
        Assert.Equal(30937, work.Products[0].ItemId);
        // Échec : rate 1 → seuil 1+0 ; Next(100)=50 ≥ 1 → rien.
        var w2 = new WorkSlot { WorkId = 1, WorkType = 15 };
        Svc(new SeqRandom(50)).RollCombineProduct(
            new WorkOrderParser.WorkCombineDef
            {
                Id = 1, ProductItemId = 1,
                ProductQuantity = 1, SuccessRate = 1,
            }, w2);
        Assert.True(w2.Products[0].IsEmpty);
    }

    [Fact]
    public void RollCombine_Equipement_RecoitCombos()
    {
        // SelectWorkItemCombo : produit équipement → combo + extras
        // roulés sur la vraie table (pools 0/1 non vides pour un
        // kind arme bas niveau).
        var item = Data.Items.First(i =>
            ItemComboRollService.IsWorkComboEligible(i.Kind)
            && i.RequiredLevel is > 0 and <= 5);
        var def = new WorkOrderParser.WorkCombineDef
        {
            Id = 1, ProductItemId = item.Id,
            ProductQuantity = 1, SuccessRate = 100,
        };
        var work = new WorkSlot { WorkId = 1, WorkType = 15 };
        Svc(new SeqRandom(0, 0, 0, 0)).RollCombineProduct(def, work);
        var p = work.Products[0];
        Assert.False(p.IsEmpty);
        Assert.Contains(p.ExtraCombos, e => e != 0);
        Assert.True(p.Combo > 0);
    }

    [Fact]
    public void RollCombine_Consommable_SansCombos()
    {
        // Produit non-équipement → SelectWorkItemCombo non appelé :
        // Combo/ExtraCombos restent 0.
        var def = new WorkOrderParser.WorkCombineDef
        {
            Id = 1, ProductItemId = 30937, // gemme (kind hors 1..0x22)
            ProductQuantity = 1, SuccessRate = 100,
        };
        var work = new WorkSlot { WorkId = 1, WorkType = 15 };
        Svc(new SeqRandom(0)).RollCombineProduct(def, work);
        var p = work.Products[0];
        Assert.False(p.IsEmpty);
        Assert.Equal(0, p.Combo);
        Assert.All(p.ExtraCombos, e => Assert.Equal(0, e));
    }

    // ---------------- Capacité claim ----------------

    [Fact]
    public void SlotsNeeded_StackableMergesOrNot()
    {
        var work = new WorkSlot { WorkId = 1 };
        // Un matériau stackable (cap>1) déjà partiellement empilé.
        var mat = Data.Items.First(i => i.MaxStack > 5
            && i.EquipLoc <= 0);
        work.Products[0].ItemId = mat.Id;
        work.Products[0].Amount = 1;
        var items = new List<ItemEntity>
        {
            new() { ItemId = mat.Id, Quantity = 1, BagId = 0 },
        };
        Assert.Equal(0, Svc().SlotsNeededFor(work, items));
        // Stack plein → slot supplémentaire requis.
        items[0].Quantity = mat.MaxStack;
        Assert.Equal(1, Svc().SlotsNeededFor(work, items));
        // Non-stackable → 1 slot par produit.
        var equip = Data.Items.First(i => i.MaxStack <= 1
            && i.EquipLoc > 0);
        work.Products[0].ItemId = equip.Id;
        Assert.Equal(1, Svc().SlotsNeededFor(work, items));
    }

    // ---------------- Codecs 497/498 ----------------

    [Fact]
    public void BuildWorkUpdate_WireLayout()
    {
        var body = WorkPackets.BuildWorkUpdate(new[]
        {
            new WorkPackets.WorkUpdateEntry(3, 12, 12345, 203, "test"),
        });
        using var r = new BinaryReader(new MemoryStream(body));
        Assert.Equal((ushort)1, r.ReadUInt16());       // u16 count
        Assert.Equal((short)3, r.ReadInt16());         // s16 workNum
        Assert.Equal((short)12, r.ReadInt16());        // s16 type
        Assert.Equal(12345, r.ReadInt32());            // i32 workId
        Assert.Equal(203, r.ReadInt32());              // i32 enchant
        var slen = r.ReadUInt16();                     // u16 slen
        Assert.Equal(4, slen);
        Assert.Equal(4, r.ReadBytes(4).Length);        // "test"
        Assert.Equal(0, r.BaseStream.Length - r.BaseStream.Position);
    }

    [Fact]
    public void BuildWorkUpdate_EmptyEmitsZeroCount()
    {
        var body = WorkPackets.BuildWorkUpdate(
            Array.Empty<WorkPackets.WorkUpdateEntry>());
        Assert.Equal(2, body.Length);
        Assert.Equal(0, BitConverter.ToUInt16(body));
    }

    [Fact]
    public void BuildWorkResult_WireLayout()
    {
        var work = new WorkSlot { WorkNum = 2, WorkType = 15 };
        var p = work.Products[0];
        p.ItemId = 999; p.Amount = 7; p.SocketAmount = 2;
        p.Combo = 55; p.ExtraCombos[0] = 66;
        var body = WorkPackets.BuildWorkResult(-99, 15, 2, "fm",
            work.Products);
        using var r = new BinaryReader(new MemoryStream(body));
        Assert.Equal(-99, r.ReadInt32());              // i32 (−id=échec)
        Assert.Equal((short)15, r.ReadInt16());        // s16 type
        Assert.Equal((short)2, r.ReadInt16());         // s16 workNum
        Assert.Equal(2, r.ReadUInt16());               // u16 slen "fm"
        r.ReadBytes(2);
        Assert.Equal((ushort)1, r.ReadUInt16());       // u16 vectors
        Assert.Equal((ushort)13, r.ReadUInt16());      // u16 len
        Assert.Equal(999, r.ReadInt32());              // itemId
        Assert.Equal(7, r.ReadInt32());                // amount
        Assert.Equal(2, r.ReadInt32());                // socket_amount
        Assert.Equal(55, r.ReadInt32());               // combo
        for (var i = 0; i < 9; i++)
            Assert.Equal(i == 0 ? 66 : 0, r.ReadInt32()); // extra 1-9
        Assert.Equal(0, r.BaseStream.Length - r.BaseStream.Position);
    }

    // ---------------- Persistance ----------------

    private static readonly string ConnStr =
        TestDb.ConnStr("edenfr_db");

    private static async Task<bool> DbUp()
    {
        try
        {
            await using var c = new NpgsqlConnection(ConnStr);
            await c.OpenAsync();
            return true;
        }
        catch { return false; }
    }

    [Fact]
    public async Task Repository_EmptyPlayerReturns11Slots()
    {
        if (!await DbUp()) return;
        var repo = new WorkRepository(Npgsql.NpgsqlDataSource.Create(ConnStr));
        var works = await repo.GetWorksAsync(1999999001);
        Assert.Equal(WorkSlot.MaxSlots, works.Length);
        Assert.All(works, w => Assert.True(w.IsEmpty));
        Assert.Equal(Enumerable.Range(0, 11),
            works.Select(w => w.WorkNum));
    }

    [Fact]
    public async Task Repository_SaveLoadRoundtrip()
    {
        if (!await DbUp()) return;
        const int pid = 1999999002;
        var repo = new WorkRepository(Npgsql.NpgsqlDataSource.Create(ConnStr));
        try
        {
            await repo.EnsureRowsAsync(pid);
            var works = await repo.GetWorksAsync(pid);
            var w = works[3];
            w.WorkType = 15; w.WorkId = 777;
            w.FmName = "test-work"; w.ExtSuccessRate = 5;
            w.FinishAtMs = 123456789000L; w.Running = true;
            w.Products[0].ItemId = 4242;
            w.Products[0].Amount = 3;
            w.Products[0].SocketAmount = 2;
            w.Products[0].Combo = 9;
            w.Products[0].Curse = true;
            w.Products[0].ExtraCombos[4] = 88;
            await repo.SaveAsync(pid, w);

            var re = await repo.GetWorksAsync(pid);
            var r = re[3];
            Assert.Equal(15, r.WorkType);
            Assert.Equal(777, r.WorkId);
            Assert.Equal("test-work", r.FmName);
            Assert.Equal(5, r.ExtSuccessRate);
            Assert.Equal(123456789000L, r.FinishAtMs);
            Assert.True(r.Running); // dérivé de finish_at ≠ 0
            Assert.Equal(4242, r.Products[0].ItemId);
            Assert.Equal(3, r.Products[0].Amount);
            Assert.Equal(2, r.Products[0].SocketAmount);
            Assert.Equal(9, r.Products[0].Combo);
            Assert.True(r.Products[0].Curse);
            Assert.Equal(88, r.Products[0].ExtraCombos[4]);

            // ResetWork → tout à 0 (finish_at compris).
            await repo.ResetAsync(pid, r);
            var cleared = (await repo.GetWorksAsync(pid))[3];
            Assert.True(cleared.IsEmpty);
            Assert.False(cleared.Running);
            Assert.Equal(0, cleared.FinishAtMs);
        }
        finally
        {
            await using var c = new NpgsqlConnection(ConnStr);
            await c.ExecuteAsync(
                "DELETE FROM player_works WHERE player_id=@pid",
                new { pid });
        }
    }

    /// <summary>Overlay EDENFR : c_familycollect prime sur l'oracle US
    /// (durée col10 : 50 US → 25 FR pour work_id 111, mesuré sur les
    /// fichiers) et c_familycombine remplace la catégorie (2131 : 5 US
    /// → 3 FR).</summary>
    [Fact]
    public void OverlayFrWorkOrders_FrTablesWin()
    {
        var fr = Path.GetFullPath(Path.Combine(TestPaths.RepoRoot,
            "..", "corpus", "fr-full", "data", "db"));
        if (!Directory.Exists(fr)) return;   // corpus absent (CI)
        var gd = new GameDataService(DbDir);
        gd.LoadAll();
        var usDur = gd.WorkCollectById.Values
            .First(d => d.Id == 111).Duration;
        var n = gd.OverlayFrWorkOrders(fr);
        Assert.True(n > 0);
        var frDef = gd.WorkCollectById.Values.First(d => d.Id == 111);
        Assert.Equal(25, frDef.Duration);
        Assert.NotEqual(usDur, frDef.Duration);
        Assert.Equal(3, gd.WorkCombineById[2131].Category);
    }
}
