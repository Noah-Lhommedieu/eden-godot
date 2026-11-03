namespace Eden.Core.Game.Classes;

/// <summary>
/// Licence (certificat de classe) possédée par un joueur —
/// table native `player_license`.
///
/// Schéma natif (`\d player_license` + désasm `GetLicenseSQLCmd`) :
///   id      = player_id (FK personnage)
///   item_id = id de l'item licence S_Item (33xxx/34xxx, s16)
///   loc     = slot équipé 1..7 de la classe ACTIVE
///             (`SetCurrentClassLicense` @0x82714f0 écrit `loc`) —
///             0 = possédée mais non équipée.
///
/// Pour les classes NON actives le layout vit dans
/// `player_classlist.lic_1..7` (`SetOtherClassLicense` @0x82717f0) —
/// `player_license.loc` ne reflète que la classe courante.
/// </summary>
public sealed class LicenseEntity
{
    /// <summary>`id` — player_id (personnage).</summary>
    public int PlayerId { get; set; }
    /// <summary>`item_id` — licence S_Item (s16).</summary>
    public short ItemId { get; set; }
    /// <summary>`loc` — slot actif 1..7, 0 = non équipée.</summary>
    public short Loc { get; set; }
}
