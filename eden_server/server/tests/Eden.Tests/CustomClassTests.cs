using Eden.Core.Game.Classes;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régressions du sous-système de classes personnalisées —
/// wire 592 (`CNC_CZ_ClientPlayerClassCustomData::Serialize` zone
/// @0x081fed00), clés de stash `SwitchClassEquip` @0x080f1f90 et
/// format entrant 242 (`Deserialize` @0x08273a30).
/// </summary>
public class CustomClassTests
{
    private static CustomClassSlot Card(byte equip, byte weapon,
        params int[] spells)
    {
        var c = new CustomClassSlot { EquipType = equip, WeaponType = weapon };
        c.Spells.AddRange(spells);
        return c;
    }

    // --------------------------- codec 592 ---------------------------

    /// <summary>flag=1 : `[u8 1][u8 slot][s16 1][u8 equip][s16 1]
    /// [u8 weapon][u32 1][s16 n][i32 ×n]` — corps émis par
    /// PlayerCCDataSave::Execute.</summary>
    [Fact]
    public void BuildSavedSlot_LayoutNatif()
    {
        var body = CustomClassPackets.BuildSavedSlot(2,
            Card(3, 1, 50010, 50020));
        var expected = new List<byte>
        {
            1, 2,                // flag=1, slot=2
            1, 0, 3,             // s16 n=1, u8 equip=3
            1, 0, 1,             // s16 n=1, u8 weapon=1
            1, 0, 0, 0,          // u32 nslots=1 (natif écrit uint)
            2, 0,                // s16 : 2 sorts
        };
        expected.AddRange(BitConverter.GetBytes(50010));
        expected.AddRange(BitConverter.GetBytes(50020));
        Assert.Equal(expected.ToArray(), body);
    }

    /// <summary>flag=0 : les 3 cartes dans l'ordre — réponse
    /// QueryClassList (166) de l'Execute natif @0x082767d0.</summary>
    [Fact]
    public void BuildAllSlots_LayoutNatif()
    {
        var cards = new List<CustomClassSlot>
        {
            Card(1, 0, 100, 200),
            Card(2, 1),
            Card(3, 0, 300),
        };
        var body = CustomClassPackets.BuildAllSlots(1, cards);
        var expected = new List<byte>
        {
            0, 1,                // flag=0, actif=1
            3, 0, 1, 2, 3,       // s16 n=3, equips 1/2/3
            3, 0, 0, 1, 0,       // s16 n=3, weapons 0/1/0
            3, 0, 0, 0,          // u32 nslots=3 (natif écrit uint)
            2, 0,                // carte0 : 2 sorts
        };
        expected.AddRange(BitConverter.GetBytes(100));
        expected.AddRange(BitConverter.GetBytes(200));
        expected.AddRange(new byte[] { 0, 0 });            // carte1 : 0 sort
        expected.AddRange(new byte[] { 1, 0 });            // carte2 : 1 sort
        expected.AddRange(BitConverter.GetBytes(300));
        Assert.Equal(expected.ToArray(), body);
    }

    /// <summary>Carte sans sorts — vecteur vide natif (s16 0).</summary>
    [Fact]
    public void BuildSavedSlot_SansSorts()
    {
        var body = CustomClassPackets.BuildSavedSlot(0, Card(1, 0));
        Assert.Equal(new byte[]
        {
            1, 0, 1, 0, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0,
        }, body);
    }

    // ------------------------- stash keys -------------------------

    /// <summary>`SwitchClassEquip` : type 1→conteneur 4, 2→3, 3→2
    /// (clé = 5 − type).</summary>
    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 3)]
    [InlineData(3, 2)]
    public void StashContainerKey_Natif(int type, int key)
        => Assert.Equal(key, CustomClass.StashContainerKey(type));

    /// <summary>L'enchant AngelChangeStyleID natif = 46605
    /// (donnée statique @0x087b4b0c, -18931 signé).</summary>
    [Fact]
    public void AngelStyleEnchantId_Est46605()
        => Assert.Equal(46605, CustomClass.AngelStyleEnchantId);

    /// <summary>Carte seedée = défaut natif (equip=1, weapon=0,
    /// sorts vides).</summary>
    [Fact]
    public void Default_MatcheSeedNatif()
    {
        var d = CustomClassSlot.Default();
        Assert.Equal(1, d.EquipType);
        Assert.Equal(0, d.WeaponType);
        Assert.Empty(d.Spells);
    }
}
