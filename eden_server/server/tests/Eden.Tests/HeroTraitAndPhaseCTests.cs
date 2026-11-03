using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Game;
using Eden.Core.Game.Classes;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Services;
using Eden.Core.Game.Stats;
using Eden.Zone.Combat;
using Xunit;

namespace Eden.Tests;

/// <summary>A4.5 (trait héroïque) + phase C (cores dans le snapshot
/// combat, combos boîte magique) + goldens 32002/9057/9058.</summary>
public class HeroTraitAndPhaseCTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    static HeroTraitAndPhaseCTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static (GameDataService Gd, EnchantService Es) Load()
    {
        var gd = new GameDataService(DbDir);
        gd.LoadAll();
        return (gd, new EnchantService(gd.Enchants));
    }

    // ---------- A4.5 : sémantique du wire create_flag ----------

    [Theory]
    [InlineData(0, 1)] [InlineData(7, 8)]
    public void WireFeature_ZeroBased_StoredOneBased(byte f, int stored)
        => Assert.Equal(stored, HeroTrait.FromWireFeature(f));

    [Theory]
    [InlineData(8)] [InlineData(255)]
    public void WireFeature_OutOfRange_RefusedLikeNative(byte f)
        => Assert.Null(HeroTrait.FromWireFeature(f));

    /// <summary>Les 8 enchants 52001-52008 existent dans S_Enchant et
    /// portent la bonne stat (prouve que le stage « herotrait » donne
    /// des stats).</summary>
    [Fact]
    public void HeroTraitEnchants_ApplyExpectedChannels()
    {
        var (_, es) = Load();
        StatContribution C(int n)
        {
            var c = new StatContribution();
            LicenseService.ApplyEnchantCommands(
                c, es.GetUseCommands(52000 + n));
            return c;
        }
        var lead = C(1);          // PV et PM +12 %
        Assert.Equal(12, lead.Pct(StatIndex.Hp));
        Assert.Equal(12, lead.Pct(StatIndex.Mp));
        Assert.Equal(10, C(4).Pct(StatIndex.Str));   // Force +10 %
        Assert.Equal(10, C(3).Pct(StatIndex.Int));
        Assert.Equal(15, C(5).Pct(StatIndex.Wis));   // Sagesse +15 %
        Assert.Equal(10, C(6).Pct(StatIndex.Agi));
        Assert.Equal(10, C(7).Pct(StatIndex.Lck));
        Assert.Equal(10, C(8).Pct(StatIndex.Lck));
        for (var n = 1; n <= 8; n++)
            Assert.NotEmpty(es.GetUseCommands(52000 + n));
    }

    // ---------- Phase C : cores dans le snapshot combat ----------

    private static ParameterParser.ParameterSet Ps() => new(
        new Dictionary<int, float>
        {
            [38] = 0.5f, [305] = 5f, [306] = 0.3f, [307] = 5f,
            [309] = 0.8f, [310] = 3f, [311] = 150f, [312] = 1f,
            [313] = 3f, [314] = 150f, [315] = 1f, [316] = 1f,
            [317] = 1f, [318] = 5f, [319] = 0.8f, [322] = 1f,
            [325] = 100f, [326] = 3f, [327] = 3f, [562] = 20f,
        }, new Dictionary<int, string>());

    [Fact]
    public void CoreBonus_FromGemsAndTrait_ReachesStrTotalAndMelee()
    {
        var p = NativeCombatFormulas.ResolveParameters(Ps());
        var c = new CombatantSnapshot
        {
            Level = 20, StrTotal = 100, StrPct = 100,
            GearCore = new[] { 8, 0, 0, 0, 0 },   // STR équipement
        };
        var before = NativeCombatFormulas.MeleeDamage(c, p);
        // Pipeline : équipement (8, déjà dans StrTotal) + gemme +30
        // pre + trait +10 % pct.
        var contrib = new StatContribution()
            .Add(StatIndex.Str, pre: 8)
            .Add(StatIndex.Str, pre: 30)
            .Add(StatIndex.Str, pct: 10);
        c.ApplyContribution(contrib);
        Assert.Equal(130, c.StrTotal);       // 100 + 30 (pas 38)
        Assert.Equal(110, c.StrPct);
        Assert.True(NativeCombatFormulas.MeleeDamage(c, p) > before);
        // Retrait du buff : retour à l'état initial.
        c.ApplyContribution(new StatContribution()
            .Add(StatIndex.Str, pre: 8));
        Assert.Equal(100, c.StrTotal);
        Assert.Equal(100, c.StrPct);
        Assert.Equal(before, NativeCombatFormulas.MeleeDamage(c, p));
    }

    /// <summary>+N d'arme (stage fortify → PhysAttackAdd) répercuté sur
    /// les dégâts de mêlée.</summary>
    [Fact]
    public void FortifyPlusN_RaisesMeleeDamage()
    {
        var p = NativeCombatFormulas.ResolveParameters(Ps());
        var c = new CombatantSnapshot
        {
            Level = 20, StrTotal = 100, StrPct = 100, WeaponPAtk = 200,
        };
        var before = NativeCombatFormulas.MeleeDamage(c, p);
        var weapon = new ItemParser.ItemDef
        { Id = 1, Kind = 0x11, PhysicalAttack = 200 };
        var worn = new[] { new ItemEntity { ItemId = 1, BagId = 0,
            EnchantLevel = 10 } };
        c.ApplyContribution(StatContribution.FromBonusStats(
            EquipmentStatService.SumFortify(worn,
                new[] { weapon }, 3, 3)));
        Assert.True(NativeCombatFormulas.MeleeDamage(c, p) > before);
    }

    // ---------- Phase C : combos boîte magique ----------

    [Fact]
    public void MagicBoxCombos_ResolveToEnchantsWithStats()
    {
        var (gd, es) = Load();
        var mb = gd.ItemCombos.Where(x => x.AttrType > 3).ToList();
        Assert.True(mb.Count(x => x.EnchantId > 0) > 1000);
        // Tous les enchants référencés existent dans S_Enchant.
        var known = gd.Enchants.Select(e => e.Id).ToHashSet();
        Assert.All(mb.Where(x => x.EnchantId > 0),
            x => Assert.Contains(x.EnchantId, known));
        // Un combo « Durabilité I »/« Bénédiction de Vie I » (id 4204)
        // → enchant 43217.
        Assert.Equal(43217, gd.ItemCombos.First(x => x.Id == 4204).EnchantId);
    }

    [Fact]
    public void MagicBoxEnchantIds_OnlyWornIdentifiedPieces()
    {
        var combos = new List<CraftRecipeParser.ItemCombo>
        {
            new() { Id = 10, AttrType = 4, EnchantId = 555 },
            new() { Id = 11, AttrType = 4, EnchantId = 556 },
            new() { Id = 12, AttrType = 0, EnchantId = 0 },
        };
        var worn = new ItemEntity { BagId = 0, MagicBoxEnchant1 = 10,
            MagicBoxEnchant2 = 11, MagicBoxEnchant3 = 12 };
        var bag = new ItemEntity { BagId = 3, MagicBoxEnchant1 = 10 };
        var unid = new ItemEntity { BagId = 0, Identify = 0,
            MagicBoxEnchant1 = 10 };
        Assert.Equal(new[] { 555, 556 },
            EquipmentStatService.MagicBoxEnchantIds(
                new[] { worn, bag, unid }, combos).ToArray());
    }

    // ---------- Goldens 9057 / 9058 / 32002 ----------

    [Fact]
    public void Golden_9057_EchoPong_EmptyBody()
    {
        var pong = new Eden.Zone.Handlers.CNC_CZ_ClientEchoPong();
        Assert.Equal(9057, pong.Opcode);
        var o = new Eden.Core.Networking.OutBinStream();
        pong.Serialize(o);
        Assert.Empty(o.ToArray());
    }

    [Fact]
    public void Golden_9058_HeroTrait_OneByte()
    {
        var t = new Eden.Zone.Handlers.CNC_CZ_ClientHeroTrait { Trait = 7 };
        Assert.Equal(9058, t.Opcode);
        var o = new Eden.Core.Networking.OutBinStream();
        t.Serialize(o);
        Assert.Equal("07", Convert.ToHexString(o.ToArray()));
    }

    /// <summary>Golden 32002 — les mêmes octets sont relus par le
    /// client dans NetTests (HeroTraitAndAdvStatsNetTests).</summary>
    [Fact]
    public void Golden_32002_AdvancedStatsBody()
    {
        var body = Eden.Zone.ZoneEntryFlow.EncodeAdvancedStats(
            2, 1.5f, 0.5f, 1.5, 0.5,
            new() { ("Melee", "5*20+100", 200f, "dmg") },
            new() { (31001, 1, 2, 100f, 10, 50, 5, 25) });
        Assert.Equal(AdvStatsGoldenHex, Convert.ToHexString(body));
        Assert.Equal(32002, Eden.Zone.ZoneEntryFlow.OpAdvStatsInfo);
        Assert.Equal(32001, Eden.Zone.ZoneEntryFlow.OpAdvStatsQuery);
    }

    /// <summary>Corps : v1, maxHits 2, f32 1.5, f32 0.5, 1 formule,
    /// 1 source (stacks 2 → chances ×2×1.5, facteurs ×2×0.5).</summary>
    public const string AdvStatsGoldenHex =
        "01020000C03F0000003F010005004D656C65650800352A32302B313030000048430300646D67010019790102000000C8420000F04100004842000070410000C841";
}
