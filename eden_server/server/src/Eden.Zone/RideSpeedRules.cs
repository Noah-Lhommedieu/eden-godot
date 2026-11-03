using Eden.Core.Game.Services;

namespace Eden.Zone;

/// <summary>
/// Vitesse de déplacement de monture — UNE seule source côté serveur.
/// Le bonus de la monture (S_RideStep / enchant 2051|pct) entre dans
/// <c>CombatantSnapshot.MoveSpeedBonusPct</c> via <c>RideStates</c> ;
/// l'étape « ride » du StatPipeline ne doit donc PAS recompter la
/// commande 2051 (sinon +75 % valait +150 %). Le client ne calcule
/// rien : il applique 296 Speeds[0] (voir PlayerController.MoveSpeedMul).
/// </summary>
public static class RideSpeedRules
{
    /// <summary>AttrMovement (déplacement %).</summary>
    public const int CmdAttrMovement = 2051;

    /// <summary>Commandes de l'enchant de ride à passer au StatPipeline :
    /// tout sauf 2051 (déjà compté via RideStates).</summary>
    public static IReadOnlyList<EnchantService.UseCommand> PipelineCommands(
        IReadOnlyList<EnchantService.UseCommand> cmds)
        => cmds.Where(c => c.CommandId != CmdAttrMovement).ToList();

    /// <summary>Vitesse par couleur (spec propriétaire, custom serveur) :
    /// blanc 35 %, vert 55 %, bleu 70 %, or 90 %. S_Item n'a pas de
    /// couleur (halo = client) : la couleur se déduit du bonus natif de
    /// l'enchant, classé comme le wiki FR (blanc ≤ 20-25, vert ~35,
    /// bleu ~50, or ≥ 70). Bonus natif ≤ 0 (montures « sans bonus » ou
    /// malus voulus : Mecha dragon, -50/-75) : inchangé.</summary>
    public static int TierPct(int nativePct)
        => nativePct <= 0 ? nativePct
         : nativePct <= 25 ? 35
         : nativePct <= 42 ? 55
         : nativePct <= 65 ? 70
         : 90;

    /// <summary>Bonus % effectif d'une monture (après couleur).</summary>
    public static int EffectivePct(EnchantService.RideInfo? info)
        => info == null ? 0 : TierPct(info.SpeedPct);

    /// <summary>Bonus % de vitesse actif de la monture du perso (0 à pied).</summary>
    public static float BonusPct(EnchantService enchants,
        IReadOnlyDictionary<int, (int ItemSerial, int EnchantId)> rideStates,
        int charId)
        => rideStates.TryGetValue(charId, out var rs)
            ? EffectivePct(enchants.GetRideInfo(rs.EnchantId)) : 0;
}
