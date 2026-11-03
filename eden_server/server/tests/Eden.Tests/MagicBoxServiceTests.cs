using System.Text;
using Xunit;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.MagicBox;

namespace Eden.Tests;

/// <summary>Tests de la boîte magique — tables rodata
/// (ConverEnergyRate/Amount, QualityNeed, Scouring*) + simulation
/// CheckMagicBoxCosts/DoMagicBox (désasm @0x8088200/@0x80a8b90) +
/// sérialisation 613 byte-par-byte.</summary>
public class MagicBoxServiceTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    static MagicBoxServiceTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static MagicBoxService BuildService(Random? rng = null)
    {
        var items = ItemParser.Parse(Path.Combine(DbDir, "S_Item.ini"))
            .Concat(ItemParser.Parse(
                Path.Combine(DbDir, "S_ItemMall.ini")))
            .GroupBy(i => i.Id).Select(g => g.First()).ToList();
        var combos = CraftRecipeParser.ParseItemCombo(
            Path.Combine(DbDir, "S_ItemCombo.ini"));
        var parameters = ParameterParser.Parse(
            Path.Combine(DbDir, "S_Parameter.ini"));
        return new MagicBoxService(items, combos, parameters, rng);
    }

    /// <summary>Un item éligible type0 (arme) réel : FlagsHi bit 8 —
    /// 28594 « 複製的雙劍 » kind 17.</summary>
    private const int EligibleWeapon = 28594;
    /// <summary>Un item éligible type1 (armure) : 23219 kind 68
    /// (hi 0x020).</summary>
    private const int EligibleArmor = 23219;

    [Fact]
    public void ItemParser_Loads64BitFlags()
    {
        var svc = BuildService();
        var weapon = svc.ItemDef(EligibleWeapon)!;
        Assert.NotNull(weapon);
        Assert.Equal(8, weapon.FlagsHi & 8);      // type0 arme
        Assert.Equal(0, MagicBoxTables.MainType(weapon.FlagsHi));
        var armor = svc.ItemDef(EligibleArmor)!;
        Assert.NotNull(armor);
        Assert.Equal(1, MagicBoxTables.MainType(armor.FlagsHi));
        // Un consommable banal n'est jamais éligible.
        var potion = svc.ItemDef(32631)!;
        Assert.Equal(-1, MagicBoxTables.MainType(potion.FlagsHi));
    }

    [Fact]
    public void QualityBuckets_MatchNativeSwitch()
    {
        // switch clone+0x2c natif : {0→1, 2→2, 4→4, 3→8, 5→0x10},
        // magic≠0 → 0x10.
        Assert.Equal(0x01, MagicBoxTables.QualityBucket(0, 0));
        Assert.Equal(0x02, MagicBoxTables.QualityBucket(2, 0));
        Assert.Equal(0x04, MagicBoxTables.QualityBucket(4, 0));
        Assert.Equal(0x08, MagicBoxTables.QualityBucket(3, 0));
        Assert.Equal(0x10, MagicBoxTables.QualityBucket(5, 0));
        Assert.Equal(0x10, MagicBoxTables.QualityBucket(0, 7));
        Assert.Equal(0, MagicBoxTables.QualityBucket(1, 0));
    }

    [Fact]
    public void NativeTables_ResolveThroughSParameter()
    {
        var svc = BuildService();
        // Seuils arme : params 609-613 = {500,1500,3600,7500,20000}.
        Assert.Equal(500, svc.EnergyThreshold(0, 0));
        Assert.Equal(20000, svc.EnergyThreshold(0, 4));
        // Armure : {8,32,128,405,1350} ; accessoire {6,25,102,270,900}.
        Assert.Equal(128, svc.EnergyThreshold(1, 2));
        Assert.Equal(270, svc.EnergyThreshold(2, 3));
        // Scouring armure star3 → param 646 = 4.
        Assert.Equal(4, svc.ScourEquipAmount(1, 3));
        // Arme star1 → 639 = 5.
        Assert.Equal(5, svc.ScourEquipAmount(0, 1));
    }

    [Fact]
    public void ConversionRate_QualityMaskFilter()
    {
        var svc = BuildService();
        // Arme star0 → QualityNeed=2 → seule qualité 2 (bucket 0x02,
        // taux 0.1) acceptée ; qualité 0 (bucket 0x01) rejetée.
        Assert.Equal(0.1f, svc.ConversionRate(0, 0,
            new(1, 28594, 2, 0, 40)));
        Assert.Equal(-1f, svc.ConversionRate(0, 0,
            new(1, 28594, 0, 0, 40)));
        // Arme star4 → QualityNeed=16 → magic≠0 ou qualité 5 requis,
        // taux 0.6.
        Assert.Equal(0.6f, svc.ConversionRate(0, 4,
            new(1, 28594, 5, 0, 40)));
        Assert.Equal(0.6f, svc.ConversionRate(0, 4,
            new(1, 28594, 0, 3, 40)));
        // Armure star0 → QualityNeed=3 → qualité 0 (taux 1) ou 2 (taux 3).
        Assert.Equal(1f, svc.ConversionRate(1, 0,
            new(1, 23219, 0, 0, 40)));
        Assert.Equal(3f, svc.ConversionRate(1, 0,
            new(1, 23219, 2, 0, 40)));
    }

    [Fact]
    public void Convert_NoLevelUp_AccumulatesEnergy()
    {
        var svc = BuildService(new Random(42));
        var def = svc.ItemDef(EligibleArmor)!;
        // Armure star0, seuil 8 — sacrifice qualité 0 → taux 1 →
        // énergie = 0 + int(1+0.5) = 1 (< 8 → pas de star-up).
        var combos = new int[3];
        var outcome = svc.Convert(def, 0, 0, combos,
            new[] { (new MagicBoxService.Sacrifice(1, 23219, 0, 0, 40), 1f) },
            null);
        Assert.False(outcome.Failed);
        Assert.Equal(1, outcome.Energy);
        Assert.Equal(0, outcome.StarLevel);
        Assert.All(combos, c => Assert.Equal(0, c));
    }

    [Fact]
    public void Convert_LevelsUp_AndRollsCombo()
    {
        var svc = BuildService(new Random(1));
        var def = svc.ItemDef(EligibleArmor)!;
        // Armure star0 → seuil 8. Sacrifices qualité 2 → taux 3 ×3 =
        // int(3+0.5)*3 = 9 ≥ 8 → star 1 + combo.
        var combos = new int[3];
        var sacs = Enumerable.Repeat(
            (new MagicBoxService.Sacrifice(1, 23219, 2, 0, 40), 3f), 3)
            .ToList();
        var outcome = svc.Convert(def, 0, 0, combos, sacs, null);
        Assert.False(outcome.Failed);
        Assert.Equal(0, outcome.Energy);      // remise à 0 après level-up
        Assert.Equal(1, outcome.StarLevel);
        Assert.True(outcome.ModifyInfoEmitted);
        Assert.True(combos[0] > 0);           // combo roulé en slot 0
        // Le combo choisi a attrType 5 (armure), tier 1, kind∈liste.
    }

    [Fact]
    public void Convert_WeaponScalesByItemLevel()
    {
        var svc = BuildService(new Random(7));
        var def = svc.ItemDef(EligibleWeapon)!;
        // Arme star0 → seuil 500 ; sacrifice qualité 2 → taux 0.1,
        // énergie = itemLv*0.1+0.5 par sacrifice. lv40 → 4.5→4.
        var combos = new int[3];
        var sacs = Enumerable.Repeat(
            (new MagicBoxService.Sacrifice(1, 28594, 2, 0, 40), 0.1f),
            3).ToList();
        var outcome = svc.Convert(def, 0, 0, combos, sacs, null);
        Assert.Equal(12, outcome.Energy);     // 3 × int(40*0.1+0.5)=3×4
        Assert.Equal(0, outcome.StarLevel);
    }

    [Fact]
    public void Scour_RerollsSelectedCombo()
    {
        var svc = BuildService(new Random(3));
        var def = svc.ItemDef(EligibleArmor)!;
        // Item star1 avec un combo connu attrType5 tier1 → reroll en
        // tier min(1+1, 1)=1.
        var first = svc.PickComboId(def.Kind, def.RequiredLevel, 1, 5);
        Assert.True(first > 0);
        var combos = new[] { first, 0, 0 };
        var outcome = svc.Scour(def, 1, combos,
            new List<short> { (short)first }, null);
        Assert.Single(outcome.NewCombos);
        Assert.True(combos[0] > 0);
        Assert.NotEqual(0, combos[0]);
    }

    [Fact]
    public void CheckScouring_RequiresExactEquipAmount()
    {
        var svc = BuildService();
        var def = svc.ItemDef(EligibleArmor)!;
        // Armure star1 → need 2 équipements (param 644=2) ; bucket
        // ScouringQualityNeed[(1-1)+5]=2 → qualité 2 requise.
        var sacs = new List<MagicBoxService.Sacrifice>
        {
            new(1, 23219, 2, 0, 40), new(2, 23219, 2, 0, 40),
        };
        var check = svc.CheckCosts(2, def, 1, sacs,
            new List<short> { 4009 }, new[] { 4009, 0, 0 }, null);
        Assert.True(check.Ok, check.Error);
        // Un seul sacrifice → refus natif.
        check = svc.CheckCosts(2, def, 1, sacs.Take(1).ToList(),
            new List<short> { 4009 }, new[] { 4009, 0, 0 }, null);
        Assert.False(check.Ok);
        // Sans combo sélectionné → refus.
        check = svc.CheckCosts(2, def, 1, sacs,
            new List<short>(), new[] { 4009, 0, 0 }, null);
        Assert.False(check.Ok);
    }

    [Fact]
    public void MallParams_PatternsNatifs()
    {
        // Consommation conversion : XOR des paires énergie/star.
        var e = new MagicBoxService.MallParams(50, 30, 0, 0, 0, false);
        Assert.True(e.HasEnergy && !e.HasStar);
        Assert.True(e.ValidConvertPattern);
        Assert.True(e.ConsumedOnConvert);
        var s = new MagicBoxService.MallParams(0, 0, 2, 50, 0, false);
        Assert.True(s.ValidConvertPattern && s.ConsumedOnConvert);
        var both = new MagicBoxService.MallParams(1, 1, 1, 1, 0, false);
        Assert.False(both.ValidConvertPattern);
        var partial = new MagicBoxService.MallParams(1, 0, 0, 0, 0, false);
        Assert.False(partial.ValidConvertPattern);
        // Scouring : wild (d5>0) consommé seulement si d4==0.
        var wild = new MagicBoxService.MallParams(0, 0, 0, 0, 0, true);
        Assert.True(wild.Wild && wild.ConsumedOnScour);
        var n4 = new MagicBoxService.MallParams(0, 0, 0, 0, 2, false);
        Assert.True(n4.ConsumedOnScour);
        var bad = new MagicBoxService.MallParams(0, 0, 0, 0, 2, true);
        Assert.False(bad.ConsumedOnScour);
    }

    [Fact]
    public void PickComboId_RespectsNativeFilters()
    {
        var svc = BuildService(new Random(0));
        var def = svc.ItemDef(EligibleArmor)!;
        // attrType 5 (armure), star 1 — combo trouvé, appartient aux
        // kinds armure et tier 1.
        var id = svc.PickComboId(def.Kind, def.RequiredLevel, 1, 5);
        Assert.True(id > 0);
        // Un attrType hors boîte (1) ou une star sans combo → 0.
        Assert.Equal(0, svc.PickComboId(def.Kind, def.RequiredLevel, 9, 5));
        Assert.Equal(0, svc.PickComboId(def.Kind, def.RequiredLevel, 1, 1));
    }

    [Fact]
    public void MagicBoxResult_WireFormat()
    {
        // Format natif vérifié sur CNC_CZ_ClientMagicBoxResult::Serialize.
        var body = Zone.Codecs.MagicBoxPackets.BuildMagicBoxResult(
            1, new short[] { 4009, 0, 0 }, 12, 1,
            new short[] { 0 }, new short[] { 5 }, -1, -1);
        var expected = new byte[]
        {
            0x01, 0x00,             // s16 type=1
            0x03, 0x00,             // u16 n=3
            0xa9, 0x0f,             // s16 4009
            0x00, 0x00, 0x00, 0x00, // s16 0,0
            0x0c, 0x00,             // u16 energy=12
            0x01, 0x00,             // u16 star=1
            0x01, 0x00,             // u16 n=1
            0x00, 0x00,             // s16 idx 0
            0x01, 0x00,             // u16 n=1
            0x05, 0x00,             // s16 loc 5
            0xff, 0xff, 0xff, 0xff, // s16 -1 ×2 (mall)
        };
        Assert.Equal(expected, body);
    }
}
