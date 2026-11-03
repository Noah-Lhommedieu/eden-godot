using System.Text;
using Xunit;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inlay;
using Eden.Core.Game.Inventory;

namespace Eden.Tests;

/// <summary>Tests du système de sockets/inlay —
/// ChangeEmbeddedAmount @0x809baf0 (roll d'ouverture) +
/// UseItemToItemControl case 0x2d (validation, premier socket libre,
/// textes natifs 0xc1/0xc2/0xc3/0xcce).</summary>
public class InlayServiceTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    static InlayServiceTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static Dictionary<int, ItemParser.ItemDef> LoadItems()
        => ItemParser.Parse(Path.Combine(DbDir, "S_Item.ini"))
            .Concat(ItemParser.Parse(
                Path.Combine(DbDir, "S_ItemMall.ini")))
            .GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());

    /// <summary>Random déterministe : file de valeurs Next(100).</summary>
    private sealed class SeqRandom : Random
    {
        private readonly Queue<int> _q;
        public SeqRandom(params int[] values) => _q = new(values);
        public override int Next(int maxValue)
            => _q.Count > 0 ? _q.Dequeue() : 0;
    }

    private static ItemEntity MakeItem(int itemId, int amount,
        params int[] ids)
    {
        var e = new ItemEntity { ItemId = itemId, EmbeddedAmount = amount };
        for (var i = 0; i < ids.Length && i < 6; i++)
            e.SetEmbeddedId(i, ids[i]);
        return e;
    }

    // ---------- Filtres de kinds natifs ----------

    [Fact]
    public void KindFilters_MatchNativeClassRanges()
    {
        // CWeapon = kinds 17-34 (GetAllWeapons : kind-0x11 < 0x12).
        Assert.False(InlayService.IsWeaponKind(16));
        Assert.True(InlayService.IsWeaponKind(17));
        Assert.True(InlayService.IsWeaponKind(34));
        Assert.False(InlayService.IsWeaponKind(35));
        // CArmor = kinds 1-16, 37-40, 78 (+81 aux locs 10/34).
        Assert.True(InlayService.IsArmorKind(1, 1));
        Assert.True(InlayService.IsArmorKind(38, 0));
        Assert.True(InlayService.IsArmorKind(78, 0));
        Assert.False(InlayService.IsArmorKind(81, 1));
        Assert.True(InlayService.IsArmorKind(81, 10));
        Assert.True(InlayService.IsArmorKind(81, 34));
        // CAddAttrItem = kinds {45,46,47,56,59,79}.
        Assert.True(InlayService.IsAddAttrKind(45));
        Assert.True(InlayService.IsAddAttrKind(79));
        Assert.False(InlayService.IsAddAttrKind(44));
    }

    // ---------- Parse des colonnes sockets sur la vraie data ----------

    [Fact]
    public void Parser_ReadsSocketAndGemColumns()
    {
        var items = LoadItems();
        // 30425 : kind 21 (arme), maxsocket=6, rate=70.
        var weapon = items[30425];
        Assert.Equal(21, weapon.Kind);
        Assert.Equal(6, weapon.MaxSocket);
        Assert.Equal(70, weapon.SocketRate);
        // 30937 : gemme kind 45, use-type 5 (item-sur-item).
        var gem = items[30937];
        Assert.Equal(45, gem.Kind);
        Assert.Equal(5, gem.UseType);
        // col35 = 35 : niveau de gemme réel (== son niveau requis
        // col14 — il faut un équipement ≥35 pour l'incruster).
        Assert.Equal(35, gem.GemLevel);
        // 994 : armure kind 1 → socket-capable et éligible au roll.
        var armor = items[994];
        Assert.Equal(1, armor.Kind);
        Assert.Equal(1, armor.MaxSocket);
        Assert.True(InlayService.IsSocketCapable(armor));
        Assert.True(InlayService.IsSocketCapable(weapon));
        Assert.False(InlayService.IsSocketCapable(gem));
    }

    // ---------- Roll d'ouverture (ChangeEmbeddedAmount) ----------

    [Fact]
    public void RollSockets_Rate100_OpensAllUpToMax()
    {
        var svc = new InlayService(new SeqRandom(0, 0, 0, 0, 0, 0));
        var def = new ItemParser.ItemDef
            { Kind = 21, EquipLoc = 9, MaxSocket = 6, SocketRate = 100 };
        var (count, ids) = svc.RollSockets(def);
        Assert.Equal(6, count);
        Assert.All(ids, id => Assert.Equal(0, id));
    }

    [Fact]
    public void RollSockets_StopsAtFirstFailedRoll()
    {
        // Jets : 10,20,99 — le 3e échoue (99 >= rate 70) → 2 sockets.
        var svc = new InlayService(new SeqRandom(10, 20, 99));
        var def = new ItemParser.ItemDef
            { Kind = 21, EquipLoc = 9, MaxSocket = 6, SocketRate = 70 };
        var (count, ids) = svc.RollSockets(def);
        Assert.Equal(2, count);
        Assert.Equal(new[] { 0, 0, -1, -1, -1, -1 }, ids);
    }

    [Fact]
    public void RollSockets_NonEligibleKind_RollsNothing()
    {
        var svc = new InlayService(new SeqRandom(0, 0, 0));
        // Armure kind 37 : socket-capable (CArmor) mais hors du
        // early-return kinds 1-16 de ChangeEmbeddedAmount.
        var def = new ItemParser.ItemDef
            { Kind = 37, EquipLoc = 1, MaxSocket = 6, SocketRate = 100 };
        var (count, _) = svc.RollSockets(def);
        Assert.Equal(0, count);
        // Consommable : jamais éligible.
        var potion = new ItemParser.ItemDef
            { Kind = 45, MaxSocket = 6, SocketRate = 100 };
        Assert.Equal(0, svc.RollSockets(potion).Count);
    }

    [Fact]
    public void RollSockets_ZeroMaxSocket_RollsNothing()
    {
        var svc = new InlayService(new SeqRandom(0));
        var def = new ItemParser.ItemDef
            { Kind = 21, EquipLoc = 9, MaxSocket = 0, SocketRate = 100 };
        Assert.Equal(0, svc.RollSockets(def).Count);
    }

    // ---------- Validation (CheckExecute case 0x2d) ----------

    [Fact]
    public void Validate_TargetNotWeaponOrArmor_ReturnsCannotInlay()
    {
        var svc = new InlayService();
        var gem = new ItemParser.ItemDef { Kind = 45 };
        var targetDef = new ItemParser.ItemDef { Kind = 45, EquipLoc = 0 };
        var target = MakeItem(1, 3, 0, -1, -1);
        var v = svc.ValidateInlay(gem, target, targetDef, 1);
        Assert.False(v.Ok);
        Assert.Equal(InlayService.TextCannotInlay, v.ErrorTextId);
    }

    [Fact]
    public void Validate_GemLevelAboveRequired_ReturnsLevelTooLow()
    {
        var svc = new InlayService();
        var gem = new ItemParser.ItemDef { Kind = 45, LearnSpellId = 40 };
        var targetDef = new ItemParser.ItemDef
            { Kind = 21, EquipLoc = 9, RequiredLevel = 30 };
        var target = MakeItem(1, 2, 0, -1);
        var v = svc.ValidateInlay(gem, target, targetDef, 1);
        Assert.False(v.Ok);
        Assert.Equal(InlayService.TextLevelTooLow, v.ErrorTextId);
    }

    [Fact]
    public void Validate_NoOpenSockets_ReturnsNoSlots()
    {
        var svc = new InlayService();
        var gem = new ItemParser.ItemDef { Kind = 45 };
        var targetDef = new ItemParser.ItemDef
            { Kind = 21, EquipLoc = 9, RequiredLevel = 30 };
        var target = MakeItem(1, 0);          // aucune socket ouverte
        var v = svc.ValidateInlay(gem, target, targetDef, 60);
        Assert.False(v.Ok);
        Assert.Equal(InlayService.TextNoSlots, v.ErrorTextId);
    }

    [Fact]
    public void Validate_SelectsFirstEmptySocket()
    {
        var svc = new InlayService();
        var gem = new ItemParser.ItemDef { Kind = 45 };
        var targetDef = new ItemParser.ItemDef
            { Kind = 21, EquipLoc = 9, RequiredLevel = 30 };
        // count=3 : [gemme, vide, scellée] → index 1.
        var target = MakeItem(1, 3, 30937, 0, -1);
        var v = svc.ValidateInlay(gem, target, targetDef, 60);
        Assert.True(v.Ok);
        Assert.Equal(1, v.SocketIndex);
    }

    [Fact]
    public void Validate_AllSocketsFilled_ReturnsNoSlots()
    {
        var svc = new InlayService();
        var gem = new ItemParser.ItemDef { Kind = 45 };
        var targetDef = new ItemParser.ItemDef
            { Kind = 21, EquipLoc = 9, RequiredLevel = 30 };
        var target = MakeItem(1, 3, 30937, 30938, 30939);
        var v = svc.ValidateInlay(gem, target, targetDef, 60);
        Assert.False(v.Ok);
        Assert.Equal(InlayService.TextNoSlots, v.ErrorTextId);
    }

    [Fact]
    public void Validate_SealedSocketStopsScan_ReturnsNoSlots()
    {
        var svc = new InlayService();
        var gem = new ItemParser.ItemDef { Kind = 45 };
        var targetDef = new ItemParser.ItemDef
            { Kind = 21, EquipLoc = 9, RequiredLevel = 30 };
        // count=3 : [gemme, scellée, vide] — le scan natif s'arrête
        // au premier -1 → échec même si un 0 existe plus loin.
        var target = MakeItem(1, 3, 30937, -1, 0);
        var v = svc.ValidateInlay(gem, target, targetDef, 60);
        Assert.False(v.Ok);
        Assert.Equal(InlayService.TextNoSlots, v.ErrorTextId);
    }

    [Fact]
    public void Validate_GrowEquip_UsesCharLevelAsRequired()
    {
        var svc = new InlayService();
        var gem = new ItemParser.ItemDef { Kind = 45, LearnSpellId = 40 };
        var targetDef = new ItemParser.ItemDef
            { Kind = 21, EquipLoc = 9, RequiredLevel = 1 };
        var target = MakeItem(1, 1, 0);
        // item+0x114 natif ≠ 0 → niveau requis = niveau du perso.
        Assert.False(svc.ValidateInlay(gem, target, targetDef,
            charLevel: 30, targetIsGrowEquip: true).Ok);
        Assert.True(svc.ValidateInlay(gem, target, targetDef,
            charLevel: 45, targetIsGrowEquip: true).Ok);
    }

    // ---------- Application + défauts entité ----------

    [Fact]
    public void ApplyInlay_WritesGemTemplateIdInSlot()
    {
        var target = MakeItem(30425, 3, 30937, 0, -1);
        InlayService.ApplyInlay(target, 1, 30938);
        Assert.Equal(30937, target.EmbeddedId1);
        Assert.Equal(30938, target.EmbeddedId2);
        Assert.Equal(-1, target.EmbeddedId3);
    }

    [Fact]
    public void ItemEntity_DefaultEmbeddedIds_AreSealed()
    {
        var e = new ItemEntity();
        Assert.Equal(0, e.EmbeddedAmount);
        for (var i = 0; i < 6; i++)
            Assert.Equal(-1, e.GetEmbeddedId(i));
    }
}

/// <summary>Tests de la branche enchant (witchcraft) —
/// UseItemToItemControl::CheckExecute cases 0x38/0x4f @0x84ecd60 :
/// la compatibilité EquipLoc↔kind cible, le refus de re-application
/// et le gate de niveau (textes 2550/2551/2552 T_TextIndex).</summary>
public class WitchcraftValidationTests
{
    private static readonly string DbDir =
        TestPaths.RepoRoot + "/server_us/Data/db";

    static WitchcraftValidationTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static Dictionary<int, ItemParser.ItemDef> LoadItems()
        => ItemParser.Parse(Path.Combine(DbDir, "S_Item.ini"))
            .Concat(ItemParser.Parse(
                Path.Combine(DbDir, "S_ItemMall.ini")))
            .GroupBy(i => i.Id).ToDictionary(g => g.Key, g => g.First());

    private static ItemEntity Target(int itemId, int witchcraft = 0)
        => new() { ItemId = itemId, Witchcraft = witchcraft };

    [Fact]
    public void UniversalScroll_AcceptsArmor()
    {
        var items = LoadItems();
        // 30985 : scroll kind 56, EquipLoc 0 = universel natif.
        var scroll = items[30985];
        Assert.Equal(56, scroll.Kind);
        Assert.Equal(5, scroll.UseType);   // item-sur-item, jamais équipement
        Assert.Equal(0, scroll.EquipLoc);
        // 32302 : robe kind 6 — la cible « arc → body cloth » du bug.
        Assert.Equal(0, InlayService.ValidateWitchcraft(
            scroll, Target(32302), items[32302], charLevel: 60));
    }

    [Fact]
    public void SlotKeyedScroll_RefusesWrongTargetKind()
    {
        var items = LoadItems();
        // 36472 : scroll EquipLoc 2 → natif exige kind cible == 2
        // (srcEquipLoc≠0 → == kind). Sur la robe kind 6 → refus 0x9f6.
        var scroll = items[36472];
        Assert.Equal(56, scroll.Kind);
        Assert.Equal(2, scroll.EquipLoc);
        Assert.Equal(InlayService.TextCannotEnchant,
            InlayService.ValidateWitchcraft(
                scroll, Target(32302), items[32302], charLevel: 60));
    }

    [Fact]
    public void NecklacePair7_MapsToKind19()
    {
        var items = LoadItems();
        // Paire native 7→0x13 : un scroll EquipLoc 7 n'enchante que
        // les colliers kind 19.
        var scroll = items[36520];
        Assert.Equal(7, scroll.EquipLoc);
        var neck = items.Values.First(i => i.Kind == 19);
        Assert.Equal(0, InlayService.ValidateWitchcraft(
            scroll, Target(neck.Id), neck, charLevel: 60));
        // Le même scroll sur une armure kind 6 → refus.
        Assert.Equal(InlayService.TextCannotEnchant,
            InlayService.ValidateWitchcraft(
                scroll, Target(32302), items[32302], charLevel: 60));
    }

    [Fact]
    public void SameWitchcraft_AlreadyApplied_Refused()
    {
        var items = LoadItems();
        var scroll = items[30985];
        // clone+0x4c == scrollId → le natif refuse la re-application.
        Assert.Equal(InlayService.TextCannotEnchant,
            InlayService.ValidateWitchcraft(scroll,
                Target(32302, witchcraft: 30985), items[32302],
                charLevel: 60));
    }

    [Fact]
    public void ScrollLevel_AboveCharLevel_Refused9f8()
    {
        var items = LoadItems();
        // 30985 requiert niveau 25 → perso 10 → texte 0x9f8 natif.
        Assert.Equal(InlayService.TextEnchantLevel,
            InlayService.ValidateWitchcraft(
                items[30985], Target(32302), items[32302],
                charLevel: 10));
    }

    [Fact]
    public void CostumeEnchant79_MatchesTargetEquipLoc()
    {
        var items = LoadItems();
        // 39143 : enchant de costume kind 79, EquipLoc 2 — natif
        // exige EquipLoc==0 ou == EquipLoc de la cible (pas le kind).
        var scroll = items[39143];
        Assert.Equal(79, scroll.Kind);
        Assert.Equal(2, scroll.EquipLoc);
        // Robe kind 6 EquipLoc 2 → admis par la règle 0x4f.
        Assert.Equal(0, InlayService.ValidateWitchcraft(
            scroll, Target(32302), items[32302], charLevel: 60));
    }

    [Fact]
    public void NonEquipmentTarget_Refused()
    {
        var items = LoadItems();
        var scroll = items[30985];
        // Cible = une gemme kind 45 — hors kinds équipement → 0x9f6.
        var gem = items[30937];
        Assert.Equal(InlayService.TextCannotEnchant,
            InlayService.ValidateWitchcraft(
                scroll, Target(30937), gem, charLevel: 60));
        // Cible absente → 0x9f6.
        Assert.Equal(InlayService.TextCannotEnchant,
            InlayService.ValidateWitchcraft(
                scroll, null, null, charLevel: 60));
    }
}
