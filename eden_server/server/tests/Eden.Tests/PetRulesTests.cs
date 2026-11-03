using Eden.Core.Data.Parsers;
using Eden.Zone;

namespace Eden.Tests;

/// <summary>Plan 18 — règles pures familiers : stats gear sur le pet
/// live (RefreshEQsAttr) et ramassage du mode loot.</summary>
public sealed class PetRulesTests
{
    private static ItemParser.ItemDef Gear(int lvl, int atk, int hit,
        int crit, int extra) => new()
    {
        Id = 1, Kind = 81, RequiredLevel = lvl, PhysicalAttack = atk,
        Hit = hit, Critical = crit, PetEquipStat = extra,
    };

    [Fact]
    public void GearTotals_SumsStats_SkipsTooHighLevelAndNull()
    {
        var t = PetRules.GearTotals(new ItemParser.ItemDef?[]
        {
            Gear(1, 10, 2, 3, 4), Gear(5, 7, 1, 1, 1),
            Gear(30, 999, 999, 999, 999), null,
        }, petLevel: 10);
        Assert.Equal((17, 3, 4, 5), t);
    }

    [Fact]
    public void AttackPower_AddsGear_FloorOne()
    {
        Assert.Equal(25, PetRules.AttackPower(15, 10));
        Assert.Equal(1, PetRules.AttackPower(0, 0));
    }

    private static GroundTreasureService.Treasure T(uint id, int owner,
        float x, float z, bool pub = false, bool rolled = false) => new()
    {
        Id = id, NodeId = 1, ItemId = 715, Count = 1,
        OwnerCharId = owner, X = x, Z = z, IsPublic = pub,
        IsRolled = rolled,
    };

    [Fact]
    public void LootTarget_PicksNearestEligible()
    {
        var list = new[]
        {
            T(1, 5, 8, 0),            // à moi, loin
            T(2, 5, 3, 0),            // à moi, proche -> gagnant
            T(3, 9, 1, 0),            // à un autre, privé
            T(4, 9, 2, 0, pub: true), // public mais plus loin que 2? 2<3
        };
        var t = PetRules.PickLootTarget(list, 0, 0, 0, 0, ownerCharId: 5);
        Assert.Equal(4u, t!.Id);      // public à 2 u < 3 u
        t = PetRules.PickLootTarget(list.Where(x => x.Id != 4), 0, 0, 0, 0, 5);
        Assert.Equal(2u, t!.Id);
    }

    [Fact]
    public void LootTarget_IgnoresRolledOutOfScanAndLeash()
    {
        var far = T(1, 5, PetRules.LootScanRange + 1, 0);
        var rolled = T(2, 5, 1, 0, rolled: true);
        Assert.Null(PetRules.PickLootTarget(new[] { far, rolled },
            0, 0, 0, 0, 5));
        // Dans le scan du pet mais hors laisse du maître.
        var t = T(3, 5, 5, 0);
        Assert.Null(PetRules.PickLootTarget(new[] { t }, 5, 0,
            5 - PetRules.LootOwnerLeash - 1, 0, 5));
    }

    [Fact]
    public void LootTarget_OwnerZeroIsFreeForAll()
    {
        var t = T(1, 0, 2, 0);
        Assert.Equal(1u, PetRules.PickLootTarget(new[] { t }, 0, 0, 0, 0, 5)!.Id);
    }
}
