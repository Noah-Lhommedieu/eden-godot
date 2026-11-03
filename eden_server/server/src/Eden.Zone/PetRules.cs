using Eden.Core.Data.Parsers;

namespace Eden.Zone;

/// <summary>
/// Règles pures des familiers (plan 18) — testables sans serveur :
/// somme des stats d'équipement (miroir <c>CElfPet::RefreshEQsAttr</c>
/// @0x845a230) et choix de la cible de ramassage du mode loot.
/// </summary>
public static class PetRules
{
    /// <summary>Rayon de scan du pet autour de lui-même (HYPOTHESIS :
    /// le mécanisme natif de ramassage loot n'est pas prouvé dans
    /// zone/decompiled.c ; décision de portage, documentée plan 18).
    /// </summary>
    public const float LootScanRange = 12f;

    /// <summary>Laisse : le pet ne s'éloigne pas de plus de cette
    /// distance de son maître pour un trésor.</summary>
    public const float LootOwnerLeash = 18f;

    /// <summary>Portée de ramassage depuis la position du pet (même
    /// <c>CloseEnough</c> 4,0 que le joueur, avec une marge).</summary>
    public const float LootPickupReach = 3.5f;

    /// <summary>Recul après un ramassage raté (sac plein, roll…).</summary>
    public static readonly TimeSpan LootRetryDelay = TimeSpan.FromSeconds(3);

    /// <summary>Somme des stats du conteneur d'équipement d'un pet —
    /// attaque (col45), précision (col51), critique (col53), 4ᵉ stat
    /// (col57). Un item dont le niveau requis dépasse celui du pet est
    /// ignoré (check natif identique à l'équipement).</summary>
    public static (int Atk, int Hit, int Crit, int Extra) GearTotals(
        IEnumerable<ItemParser.ItemDef?> items, int petLevel)
    {
        int atk = 0, hit = 0, crit = 0, extra = 0;
        foreach (var d in items)
        {
            if (d == null || petLevel < d.RequiredLevel) continue;
            atk += d.PhysicalAttack; hit += d.Hit;
            crit += d.Critical; extra += d.PetEquipStat;
        }
        return (atk, hit, crit, extra);
    }

    /// <summary>Attaque effective du pet live : croissance de niveau
    /// (S_PartnerGrow) + équipement, plancher 1.</summary>
    public static int AttackPower(int growAttack, int gearAttack) =>
        Math.Max(1, growAttack + gearAttack);

    /// <summary>Trésor le plus proche que le pet doit ramasser pour
    /// <paramref name="ownerCharId"/> : visible du maître (propriétaire,
    /// public ou sans propriétaire), pas en roll d'équipe, dans le
    /// rayon de scan du pet et dans la laisse du maître.</summary>
    public static GroundTreasureService.Treasure? PickLootTarget(
        IEnumerable<GroundTreasureService.Treasure> treasures,
        float petX, float petY, float ownerX, float ownerY,
        int ownerCharId)
    {
        GroundTreasureService.Treasure? best = null;
        var bestD = float.MaxValue;
        foreach (var t in treasures)
        {
            if (t.IsRolled) continue;
            if (!t.IsPublic && t.OwnerCharId != 0 &&
                t.OwnerCharId != ownerCharId) continue;
            var dp = D2(petX, petY, t.X, t.Z);
            if (dp > LootScanRange * LootScanRange) continue;
            if (D2(ownerX, ownerY, t.X, t.Z) >
                LootOwnerLeash * LootOwnerLeash) continue;
            if (dp < bestD) { bestD = dp; best = t; }
        }
        return best;
    }

    private static float D2(float ax, float ay, float bx, float by)
    {
        var dx = ax - bx; var dy = ay - by;
        return dx * dx + dy * dy;
    }
}
