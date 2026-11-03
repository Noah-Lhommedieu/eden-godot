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

    /// <summary>Bonus % de vitesse actif de la monture du perso (0 à pied).</summary>
    public static float BonusPct(EnchantService enchants,
        IReadOnlyDictionary<int, (int ItemSerial, int EnchantId)> rideStates,
        int charId)
        => rideStates.TryGetValue(charId, out var rs)
            ? enchants.GetRideInfo(rs.EnchantId)?.SpeedPct ?? 0 : 0;
}
