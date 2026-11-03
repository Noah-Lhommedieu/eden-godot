using EdenClient.Data;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Règles pures du tooltip d'item (preuves : désasm rf_game
/// @0x00897fe0/@0x00898d70, t_textindex, colonnes c_item recoupées avec
/// les ref-shots « item hover * »).</summary>
public class ItemTipFormatTests
{
    [Fact]
    public void ResistOrder_FollowsNativeColumns()
    {
        // cols 63..71 : Slash, Strike, Pierce, Fire, Ice, Lightning,
        // Nature, Holy, Dark (gemmes : Ice = col 67, Thunder = 68).
        Assert.Equal(9, ItemTipFormat.ResistNames.Length);
        Assert.Equal("Slash Resistance", ItemTipFormat.ResistName(0));
        Assert.Equal("Fire Resistance", ItemTipFormat.ResistName(3));
        Assert.Equal("Ice Resistance", ItemTipFormat.ResistName(4));
        Assert.Equal("Lightning Resistance", ItemTipFormat.ResistName(5));
        Assert.Equal("Nature Resistance", ItemTipFormat.ResistName(6));
        Assert.Equal("Holy Resistance", ItemTipFormat.ResistName(7));
        Assert.Equal("Dark Resistance", ItemTipFormat.ResistName(8));
        Assert.Equal("", ItemTipFormat.ResistName(9));
    }

    [Fact]
    public void Flags_CantTrade_BitTwo()
    {
        // Blazing Jet col11 = 34359738372 → bas 32 bits = 4.
        var jet = 34359738372L & 0xFFFFFFFFL;
        Assert.True(ItemTipFormat.CantTrade(jet));
        Assert.True(ItemTipFormat.CantTrade(4));     // Neondo, Elephant
        Assert.False(ItemTipFormat.CantTrade(1));    // Trophy Enchant
        Assert.False(ItemTipFormat.CantTrade(131));  // Light Beam Gun
    }

    [Fact]
    public void Flags_BoundAndBindOnEquip()
    {
        // Light Beam Gun col11 = 131 (bit 7), instance liée 0x18.
        Assert.Equal(new[] { "Already Bound" },
            ItemTipFormat.FlagKeys(131, 0x18));
        Assert.Equal(new[] { "Bind on Equip" },
            ItemTipFormat.FlagKeys(131, 0x08));
        Assert.Equal(new[] { "Can't be Traded", "Already Bound" },
            ItemTipFormat.FlagKeys(4, 0x18));
        Assert.Empty(ItemTipFormat.FlagKeys(1, 0x08));
        Assert.True(ItemTipFormat.IsBound(0x10));
        Assert.False(ItemTipFormat.IsBound(0x08));
    }

    [Theory]
    [InlineData(20, "2.0")]
    [InlineData(15, "1.5")]
    [InlineData(30, "3.0")]
    public void AtkSpeed_OneDecimal(int raw, string expected) =>
        Assert.Equal(expected, ItemTipFormat.AtkSpeed(raw));

    [Fact]
    public void Cooldown_MinSec()
    {
        Assert.Equal("1 min. 30 sec.", ItemTipFormat.Cooldown(900));
        Assert.Equal("1 min.", ItemTipFormat.Cooldown(600));
        Assert.Equal("5 sec.", ItemTipFormat.Cooldown(50));
        Assert.Equal("1 sec.", ItemTipFormat.Cooldown(1));
    }

    [Fact]
    public void Fortify_ThreePercentPerLevel()
    {
        Assert.Equal(1155, ItemTipFormat.Fortify(3209, 12));
        Assert.Equal(0, ItemTipFormat.Fortify(3209, 0));
        Assert.Equal(0, ItemTipFormat.Fortify(0, 12));
    }

    [Fact]
    public void Signed_PlusOnlyPositive()
    {
        Assert.Equal("+83", ItemTipFormat.Signed(83));
        Assert.Equal("-8", ItemTipFormat.Signed(-8));
    }

    [Fact]
    public void ItemRef_ParsesFinishedItem()
    {
        Assert.True(ItemTipFormat.TryItemRef("&23552&", out var id));
        Assert.Equal(23552, id);
        Assert.False(ItemTipFormat.TryItemRef("Finished Item:", out _));
        Assert.False(ItemTipFormat.TryItemRef("&&", out _));
        Assert.False(ItemTipFormat.TryItemRef("&abc&", out _));
    }

    [Theory]
    [InlineData(29, "Artillery Gun")]   // Blazing Jet
    [InlineData(39, "Accessory")]       // Neondo trophy
    [InlineData(44, "Item")]            // Light Beam Gun, Elephant
    [InlineData(45, "Gem")]
    [InlineData(51, "Mining Material")] // Ore Location Map
    [InlineData(55, "Magic Pool Material")]
    [InlineData(56, "Enchanting Formula")]
    [InlineData(60, "Pet")]
    [InlineData(81, "Pet Gear")]
    [InlineData(34, "Shield")]
    [InlineData(1, "Heavy Armor")]
    [InlineData(5, "Light Armor")]
    [InlineData(12, "Cloth Armor")]
    [InlineData(999, "")]
    public void KindLabel_NativeTable(int kind, string expected) =>
        Assert.Equal(expected, ItemTipFormat.KindLabel(kind));

    [Theory]
    [InlineData(1, "Head")]
    [InlineData(5, "Legs")]
    [InlineData(9, "2-Handed")]
    [InlineData(12, "Trophy Exclusive")]
    [InlineData(35, "")]
    [InlineData(0, "")]
    public void LocLabel_FollowsTextOrder(int loc, string expected) =>
        Assert.Equal(expected, ItemTipFormat.LocLabel(loc));

    [Fact]
    public void KindClasses()
    {
        Assert.True(ItemTipFormat.IsWeaponKind(29));
        Assert.False(ItemTipFormat.IsWeaponKind(34));
        Assert.True(ItemTipFormat.IsGearKind(34));
        Assert.True(ItemTipFormat.IsGearKind(39));
        Assert.False(ItemTipFormat.IsGearKind(56));
        Assert.False(ItemTipFormat.IsGearKind(44));
    }

    [Fact]
    public void QualityTier_Clamps()
    {
        Assert.Equal(0, ItemTipFormat.QualityTier(0));
        Assert.Equal(3, ItemTipFormat.QualityTier(3));
        Assert.Equal(6, ItemTipFormat.QualityTier(11));
    }

    [Fact]
    public void PetLevel_Template() =>
        Assert.Equal("Pet Level: Lv1 0%",
            ItemTipFormat.PetLevel("Pet Level: Lv%s1 %s2%", 1, 0));

    [Fact]
    public void Durability_HiddenForStackables()
    {
        // potion empilable : def sans durabilité, wire = compteur de pile.
        Assert.Null(ItemTipFormat.DurabilityLine(0, 20, 0, 50, 99));
    }

    [Fact]
    public void Durability_ShownForGearAndWearConsumables()
    {
        Assert.Equal((3, 20), ItemTipFormat.DurabilityLine(20, 20, 35, 3, 20));
        Assert.Equal((80, 80), ItemTipFormat.DurabilityLine(80, 1, 1, 0, 0));
        Assert.Null(ItemTipFormat.DurabilityLine(80, 20, 0, 0, 0));
    }

    [Fact]
    public void Durability_IniColumn74Only_NotStackCap()
    {
        // c_item col29 = cap de pile (potion 100, ressource 999) : jamais
        // une durabilité ; col74 = durabilité d'équipement. Item 20123
        // = armure réelle avec col74=120 dans le corpus (item 26 = 35
        // cellules, pas de col74 → 0 — la valeur 100 historique venait
        // d'une ligne fusionnée par l'ancien parseur).
        var def = EdenClient.Data.Tables.ItemTableCache.Get(20123);
        if (def == null) return;               // table absente (CI)
        Assert.Equal(120, def.MaxDurability);
        Assert.Null(ItemTipFormat.DurabilityLine(0, 20, 0, 50, 99));
    }
}
