using EdenClient.Game;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Plan 18 : grades lettres, gear pet (dstType 6), catalyseur
/// de fusion, livres de sorts, emotes.</summary>
public class PetClientTests
{
    [Theory]
    [InlineData(0, "D")]
    [InlineData(1, "D+")]
    [InlineData(4, "B")]
    [InlineData(5, "B+")]
    [InlineData(9, "S+")]
    [InlineData(-3, "D")]
    [InlineData(99, "S+")]
    public void Grade_Letters(int grow, string letter) =>
        Assert.Equal(letter, PetClientRules.GradeLetter(grow));

    [Theory]
    [InlineData(81, 7, 0, true)]
    [InlineData(81, 19, 1, true)]
    [InlineData(81, 7, 2, false)]    // arme sur slot collier
    [InlineData(81, 10, 2, true)]
    [InlineData(81, 34, 3, true)]
    [InlineData(81, 10, 1, false)]
    [InlineData(80, 7, 0, false)]    // pas kind 81
    [InlineData(81, 2, 0, false)]    // EquipLoc inconnu
    [InlineData(81, 7, -1, false)]   // slot explicite requis
    [InlineData(81, 7, 4, false)]
    public void Gear_CanEquip(int kind, int loc, int slot, bool ok) =>
        Assert.Equal(ok, PetClientRules.CanEquip(kind, loc, slot));

    [Fact]
    public void Gear_Messages_UseDstType6()
    {
        var e = PetClientRules.EquipMessage(bag: 0, bagSlot: 5, elfLoc: 3,
            elfSlot: 2);
        Assert.Equal((0, 5, 0), (e.SrcType, e.SrcSlot, e.SrcIndex));
        Assert.Equal((6, 2, 3), (e.DstType, e.DstSlot, e.DstIndex));
        var u = PetClientRules.UnequipMessage(3, 2);
        Assert.Equal((6, 2, 3), (u.SrcType, u.SrcSlot, u.SrcIndex));
        Assert.Equal((0, -1, -1), (u.DstType, u.DstSlot, u.DstIndex));
        var w = PetClientRules.SwapMessage(3, 0, 1);
        Assert.Equal((6, 3, 6, 3), (w.SrcType, w.SrcIndex, w.DstType, w.DstIndex));
    }

    [Fact]
    public void Gear_ApplySwitch_EquipSwapsOccupantBack()
    {
        var eq = new List<ZoneEnterNodeElfContainer.ElfEquipSlot>
        { new() { ElfLoc = 3, Slot = 0, ItemId = 111 } };
        // sac(0,5)=222 → elf 3 slot 0 : l'occupant 111 retourne au sac.
        var (srcBag, _) = PetClientRules.ApplySwitch(eq, 0, 5, 0, 6, 0, 3,
            222, 0);
        Assert.Equal(111, srcBag);
        Assert.Equal(222, eq.Single().ItemId);
    }

    [Fact]
    public void Gear_ApplySwitch_UnequipAndSwap()
    {
        var eq = new List<ZoneEnterNodeElfContainer.ElfEquipSlot>
        {
            new() { ElfLoc = 1, Slot = 0, ItemId = 10 },
            new() { ElfLoc = 1, Slot = 1, ItemId = 20 },
        };
        PetClientRules.ApplySwitch(eq, 6, 0, 1, 6, 1, 1, 0, 0);
        Assert.Equal(20, eq.Single(x => x.Slot == 0).ItemId);
        Assert.Equal(10, eq.Single(x => x.Slot == 1).ItemId);
        // déséquipe slot 1 vers un slot sac vide.
        var (_, dstBag) = PetClientRules.ApplySwitch(eq, 6, 1, 1, 0, 7, 0,
            0, 0);
        Assert.Equal(10, dstBag);
        Assert.DoesNotContain(eq, x => x.Slot == 1);
    }

    [Fact]
    public void Fusion_PickCatalyst()
    {
        Assert.True(PetClientRules.IsFusionCatalyst(0x1791));
        Assert.False(PetClientRules.IsFusionCatalyst(715));
        var r = PetClientRules.PickCatalyst(new[]
        { (0, 1, 715), (0, 4, 0x1792), (1, 0, 0x1790) });
        Assert.Equal((0, 4), r);
        Assert.Equal((-1, -1), PetClientRules.PickCatalyst(
            new[] { (0, 1, 715) }));
    }

    private static string[] Raw(int cmd, params double[] args)
    {
        var raw = Enumerable.Repeat("0", 9).ToList();
        raw.Add(cmd.ToString());
        for (var i = 0; i < 6; i++)
            raw.Add(i < args.Length
                ? args[i].ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "0");
        return raw.ToArray();
    }

    [Fact]
    public void Book_ParsesPraySlotAndBattle()
    {
        Assert.True(PetClientRules.TryParseBook(Raw(6038, 3, 1, 2, 3, 0),
            out var pray, out var slot));
        Assert.True(pray); Assert.Equal(3, slot);
        Assert.True(PetClientRules.TryParseBook(Raw(6035, 2, 0, 5000, 0, 0, 1),
            out pray, out slot));
        Assert.False(pray); Assert.Equal(2, slot);
        // wrapper 160
        Assert.True(PetClientRules.TryParseBook(Raw(160, 6036, 5), out pray,
            out slot));
        Assert.True(pray); Assert.Equal(5, slot);
        Assert.False(PetClientRules.TryParseBook(Raw(1031, 1), out _, out _));
        Assert.False(PetClientRules.TryParseBook(Raw(6036, 9), out _, out _));
    }

    [Fact]
    public void Emote_OnlyCmds6To8()
    {
        Assert.NotEmpty(PetClientRules.EmoteAnims(6));
        Assert.NotEmpty(PetClientRules.EmoteAnims(8));
        Assert.Empty(PetClientRules.EmoteAnims(1));
        Assert.Empty(PetClientRules.EmoteAnims(105));
        Assert.Equal("third", PetClientRules.OrdinalKey(3));
    }

    [Fact]
    public void Fr_Tsv_HasPetStrings()
    {
        var dir = Directory.GetCurrentDirectory();
        string? tsv = null;
        for (var i = 0; i < 8 && tsv == null; i++)
        {
            var p = Path.Combine(dir, "godot_project", "gamedata",
                "ui_text_en_fr.tsv");
            if (File.Exists(p)) tsv = p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        Assert.NotNull(tsv);
        var keys = File.ReadLines(tsv!).Select(l => l.Split('\t')[0])
            .ToHashSet();
        foreach (var k in new[]
        {
            "Note: This book will replace the pet's {0} {1} slot.",
            "first", "second", "third", "fourth", "fifth", "pray",
            "battle", "SPD",
        })
            Assert.Contains(k, keys);
    }
}
