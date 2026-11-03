using Eden.Core.Game.Fortune;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régressions du sous-système de sacs de fortune — table
/// `fortune_bag` (SELECT natif monde), push ZW 26 en 3 phases
/// (FortuneUpdating), et machine ChanceLuckyBag
/// (`CItemFactory::DoChanceLuckyBagNext` @0x08096b50 :
/// `random()%100+1 < min(rolls×param452, param453)` puis
/// `GetDoubleRand(0,range)` → lower_bound cumProb).
/// </summary>
public class FortuneBagTests
{
    private static List<FortuneBagRow> SampleRows() =>
    [
        // Deux sacs, un set chacun — probabilités non uniformes.
        new FortuneBagRow(50001, 1, 1, 40037, 1, 10f, 0, 0, 0, 0),
        new FortuneBagRow(50001, 2, 1, 40037, 2, 10f, 0, 0, 0, 0),
        new FortuneBagRow(50001, 3, 1, 40040, 1, 30f, 0, 1, 0, 0),
        new FortuneBagRow(50001, 4, 1, 40041, 1, 50f, 0, 0, 0, 0),
        new FortuneBagRow(50002, 1, 1, 41001, 1, 100f, 0, 0, 0, 0),
    ];

    /// <summary>Sac multi-set avec seuil pity (item_counter=40) —
    /// set 1 : 2 lots dont 1 bulletin ; set 2 : 1 lot bulletin ;
    /// set 99 : total cum &lt; 100.</summary>
    private static List<FortuneBagRow> PityRows() =>
    [
        new FortuneBagRow(49014, 1, 1, 40001, 1, 60f, 40, 0, 0, 0),
        new FortuneBagRow(49014, 2, 1, 40002, 1, 40f, 40, 1, 0, 0),
        new FortuneBagRow(49014, 1, 2, 40003, 1, 100f, 40, 1, 0, 0),
        new FortuneBagRow(49014, 1, 99, 40004, 1, 30f, 40, 0, 0, 0),
    ];

    // --------------------------- codec ---------------------------

    /// <summary>Les cumuls sont recalculés par (id,set) dans l'ordre
    /// `sequence` — la clé de la std::map native.</summary>
    [Fact]
    public void RecordsFromRows_CumulativeProbability()
    {
        var recs = FortuneBagWire.RecordsFromRows(SampleRows());
        Assert.Equal(5, recs.Count);
        // Sac 50001 : 10, 20, 50, 100.
        Assert.Equal(new[] { 10f, 20f, 50f, 100f },
            recs.Where(r => r.BagId == 50001)
                .Select(r => r.CumProb).ToArray());
        // Sac 50002 : cumul repart de 0 (clé = id×65536+set).
        Assert.Equal(100f, recs[4].CumProb);
        Assert.Equal(40037, recs[0].ItemId);
        Assert.Equal(2, recs[1].ItemNum);
        Assert.Equal(1, recs[2].Bulletin);
    }

    /// <summary>Champs de comptage natifs : f2 = item_counter (seuil
    /// pity), f3 = nb de sets du sac (slots requis), f5 = nb de lots
    /// du set, f11/f12 = bornes cum basse/haute (prize+4/+8 —
    /// PAS la proba brute).</summary>
    [Fact]
    public void RecordsFromRows_NativeCountersAndBounds()
    {
        var recs = FortuneBagWire.RecordsFromRows(PityRows());
        Assert.All(recs, r => Assert.Equal(40, r.ItemCounter));
        Assert.All(recs, r => Assert.Equal(3, r.RequiredSlots));// 3 sets
        Assert.Equal(2, recs[0].PrizeCount);                  // set 1
        Assert.Equal(1, recs[2].PrizeCount);                  // set 2
        Assert.Equal(1, recs[3].PrizeCount);                  // set 99
        // set 1 : cum basse/haute = (0,60) puis (60,100).
        Assert.Equal(0f, recs[0].CumLower);
        Assert.Equal(60f, recs[0].CumUpper);
        Assert.Equal(60f, recs[1].CumLower);
        Assert.Equal(100f, recs[1].CumUpper);
        Assert.Equal(recs[1].CumProb, recs[1].CumUpper);      // f7=f12
        // set 99 : cum repart de 0 par (id,set).
        Assert.Equal(0f, recs[3].CumLower);
        Assert.Equal(30f, recs[3].CumUpper);
    }

    /// <summary>Build/Parse du push phase 1 — 33 octets par record
    /// (14 champs dequeues du Serialize monde).</summary>
    [Fact]
    public void Wire_Phase1_RoundTrip()
    {
        var recs = FortuneBagWire.RecordsFromRows(SampleRows());
        var bytes = FortuneBagWire.Build(1, recs);
        Assert.Equal(4 + 33 * recs.Count, bytes.Length);
        var (phase, parsed) = FortuneBagWire.Parse(bytes);
        Assert.Equal(1, phase);
        Assert.Equal(recs.Count, parsed.Count);
        for (var i = 0; i < recs.Count; i++)
        {
            Assert.Equal(recs[i].BagId, parsed[i].BagId);
            Assert.Equal(recs[i].SetId, parsed[i].SetId);
            Assert.Equal(recs[i].PrizeSeq, parsed[i].PrizeSeq);
            Assert.Equal(recs[i].CumProb, parsed[i].CumProb);
            Assert.Equal(recs[i].ItemId, parsed[i].ItemId);
            Assert.Equal(recs[i].ItemNum, parsed[i].ItemNum);
            Assert.Equal(recs[i].Bulletin, parsed[i].Bulletin);
        }
    }

    /// <summary>Phases 0/2 : [s16 phase] seul, pas de vecteur.</summary>
    [Fact]
    public void Wire_PhaseBoundaries_NoVector()
    {
        Assert.Equal(2, FortuneBagWire.Build(0, []).Length);
        Assert.Equal(2, FortuneBagWire.Build(2, []).Length);
        var (p0, r0) = FortuneBagWire.Parse(FortuneBagWire.Build(0, []));
        var (p2, r2) = FortuneBagWire.Parse(FortuneBagWire.Build(2, []));
        Assert.Equal(0, p0); Assert.Empty(r0);
        Assert.Equal(2, p2); Assert.Empty(r2);
    }

    // -------------------------- registre --------------------------

    /// <summary>Cycle de push natif : phase 0 verrouille, 1 remplit,
    /// 2 publie — Find reste cohérent pendant le rechargement
    /// (double-buffer).</summary>
    [Fact]
    public void Registry_Phases_LockAndPublish()
    {
        var reg = new FortuneBagRegistry();
        var recs = FortuneBagWire.RecordsFromRows(SampleRows());
        reg.Apply(0, []);                          // début d'update
        Assert.True(reg.FortuneUpdating);
        Assert.Equal(0, reg.Count);
        reg.Apply(1, recs);                        // records
        // Non publié tant que la phase 2 n'est pas arrivée.
        Assert.Equal(0, reg.Count);
        Assert.Null(reg.Find(50001));
        reg.Apply(2, []);                          // fin d'update
        Assert.False(reg.FortuneUpdating);
        Assert.Equal(2, reg.Count);
        var bag = reg.Find(50001);
        Assert.NotNull(bag);
        var set = bag!.FirstSet;
        Assert.NotNull(set);
        Assert.Equal(4, set!.Prizes.Count);
        Assert.Equal(100f, set.DiceRange);         // borne haute
    }

    /// <summary>Tirage natif : dé &lt; cumProb → premier lot dont la
    /// borne dépasse le dé (lower_bound du std::map).</summary>
    [Fact]
    public void Roll_LowerBoundSemantics()
    {
        var reg = new FortuneBagRegistry();
        reg.Apply(0, []);
        reg.Apply(1, FortuneBagWire.RecordsFromRows(SampleRows()));
        reg.Apply(2, []);
        var bag = reg.Find(50001)!;
        var set = bag.FirstSet!;
        Assert.Equal(40037, bag.Roll(set, 0f)!.ItemId);    // dé 0
        Assert.Equal(2, bag.Roll(set, 10f)!.ItemNum);      // dé==cum → suivant
        Assert.Equal(40040, bag.Roll(set, 20.5f)!.ItemId);
        Assert.Equal(40041, bag.Roll(set, 99.9f)!.ItemId);
        Assert.Null(bag.Roll(set, 100f));                  // hors borne
    }

    /// <summary>bagdef+4 = item_counter (seuil pity) et bagdef+0x1c =
    /// nb de sets — compteurs transportés par le registre.</summary>
    [Fact]
    public void Registry_ItemCounterAndSetCounts()
    {
        var reg = new FortuneBagRegistry();
        reg.Apply(0, []);
        reg.Apply(1, FortuneBagWire.RecordsFromRows(PityRows()));
        reg.Apply(2, []);
        var bag = reg.Find(49014)!;
        Assert.Equal(40, bag.ItemCounter);        // seuil pity
        Assert.Equal(3, bag.RequiredSlots);       // 3 sets → 3 slots
        Assert.Equal(3, bag.Sets.Count);
        Assert.Equal(2, bag.Sets[1].PrizeCount);
        Assert.Equal(30f, bag.Sets[99].DiceRange);
        // Poids = cum haute − cum basse (proba individuelle).
        Assert.Equal(40f, bag.Sets[1].Prizes[1].Probability);
    }

    /// <summary>Map pity native : seuls les lots bulletin==1 entrent,
    /// pondérés par leur proba — dé ∈ [0,Σpoids) → upper_bound ;
    /// aucune clé > dé → null (abort natif).</summary>
    [Fact]
    public void PityWeights_BulletinOnly()
    {
        var reg = new FortuneBagRegistry();
        reg.Apply(0, []);
        reg.Apply(1, FortuneBagWire.RecordsFromRows(PityRows()));
        reg.Apply(2, []);
        var set = reg.Find(49014)!.Sets[1];
        var w = set.PityWeights();
        Assert.Single(w);                          // 1 lot bulletin
        Assert.Equal(40f, w[0].Cum);
        // dé < cum → le lot bulletin ; dé ≥ cum → null.
        Assert.Equal(40002, FortuneSet.PityRoll(w, 39.9f)!.ItemId);
        Assert.Null(FortuneSet.PityRoll(w, 40f));
        // Set 2 : un seul lot bulletin poids 100 — tout dé gagne.
        var w2 = reg.Find(49014)!.Sets[2].PityWeights();
        Assert.Equal(40003, FortuneSet.PityRoll(w2, 99.9f)!.ItemId);
        // Set 99 sans bulletin → map vide → retombe au tirage normal.
        Assert.Empty(reg.Find(49014)!.Sets[99].PityWeights());
    }

    /// <summary>Codec 557 ClientUpdateFortuneBagCounter —
    /// `[u16 n][u16 bag][u16 counter]×n` (Serialize @0x080c3bd0).</summary>
    [Fact]
    public void Counter557_WireLayout()
    {
        var cmd = new Eden.Core.Networking.Messages.Generated
            .CNC_CZ_ClientUpdateFortuneBagCounter();
        // s16 sur le fil = bit pattern u16 (>32767 = négatif signé).
        cmd.Entries.Add(new() { BagId = unchecked((short)49014),
            Count = 3 });
        cmd.Entries.Add(new() { BagId = unchecked((short)50001),
            Count = 0 });
        var s = new Eden.Core.Networking.OutBinStream();
        cmd.Serialize(s);
        var b = s.ToArray();
        Assert.Equal(2 + 4 * 2, b.Length);               // n + 2×(bag+n)
        Assert.Equal(2, BitConverter.ToUInt16(b, 0));    // 2 entrées
        Assert.Equal(49014, BitConverter.ToUInt16(b, 2));
        Assert.Equal(3, BitConverter.ToUInt16(b, 4));
        Assert.Equal(50001, BitConverter.ToUInt16(b, 6));
        Assert.Equal(0, BitConverter.ToUInt16(b, 8));
    }

    // ----------------------- formules natives -----------------------

    /// <summary>Seuil d'échec natif : min(rolls×30, 90) — le premier
    /// lancer est toujours sûr (0×30=0, `1..100 < 0` jamais).</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 90)]
    [InlineData(9, 90)]    // cap S_Parameter 453
    public void FailChance_NativeCaps(int rolls, int expected)
    {
        const float step = 30f, cap = 90f;  // S_Parameter 452/453
        var fail = Math.Min((int)(rolls * step), (int)cap);
        Assert.Equal(expected, fail);
        // Le 1er lancer ne peut jamais échouer nativement.
        if (rolls == 0)
            Assert.False(Random.Shared.Next(1, 101) < fail);
    }
}
