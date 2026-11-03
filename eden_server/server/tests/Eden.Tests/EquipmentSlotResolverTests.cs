using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;

namespace Eden.Tests;

/// <summary>Tests de la traduction S_Item EquipLoc → CEquipment loc natif.</summary>
public sealed class EquipmentSlotResolverTests
{
    private static readonly string DataDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    /// <summary>Equip @0x80b5810 canonicalise les types d'armes avant de
    /// les placer dans les locs 7/8/10.</summary>
    [Theory]
    [InlineData(9, 7)]
    [InlineData(35, 10)]
    [InlineData(10, 11)]
    public void EquipType_UsesNativeCanonicalLocation(int equipType,
        int expected)
    {
        Assert.Equal(new[] { expected },
            NativeEquipmentSlots.AllowedLocations(equipType));
    }

    /// <summary>Armes dual-wield et accessoires renvoient leurs paires
    /// natives; les items EquipLoc=0 ne sont pas équipables.</summary>
    [Fact]
    public void Resolver_ReturnsPairsAndRejectsNonEquipment()
    {
        Assert.Equal(new[] { 7, 8 },
            NativeEquipmentSlots.AllowedLocations(7));
        Assert.Equal(new[] { 7, 8 },
            NativeEquipmentSlots.AllowedLocations(19));
        Assert.Equal(new[] { 8 },
            NativeEquipmentSlots.AllowedLocations(8));
        Assert.Equal(new[] { 12, 13 },
            NativeEquipmentSlots.AllowedLocations(20));
        Assert.Empty(NativeEquipmentSlots.AllowedLocations(0));
    }

    /// <summary>Les données réelles confirment que les pots et livres de
    /// quête signalés ont Kind consommable et aucune EquipLoc.</summary>
    [Fact]
    public void BrokenDemonPotAndQuestBook_AreNotEquipableInSourceData()
    {
        var items = ItemParser.Parse(
            Path.Combine(DataDir, "S_Item.ini"));
        foreach (var id in new[] { 34539, 34546, 35414, 35416,
                     37558, 37563, 39329 })
        {
            var item = items.First(i => i.Id == id);
            Assert.Empty(NativeEquipmentSlots.AllowedLocations(
                item.EquipLoc));
        }
    }
}
