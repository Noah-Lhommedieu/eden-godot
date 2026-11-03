using Eden.Core.Game.Classes;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Accès à la table native `player_license` — certificats de classe
/// possédés par joueur. SQL natif reproduit de
/// `GetLicenseUpdate/InsertSQLCmd` :
///   UPDATE player_license SET loc=%hhd WHERE id=%d AND item_id=%hd
///   INSERT INTO player_license (id,item_id,loc) VALUES (%d,%hd,%hhd)
/// (`CPlayerFactory::GetLicense` @0x80fd2xx pour l'octroi,
/// `SetCurrentClassLicense` @0x82714f0 pour le slotage).
/// </summary>
public interface ILicenseRepository
{
    /// <summary>Toutes les licences du joueur (possédées, loc inclus).</summary>
    Task<IReadOnlyList<LicenseEntity>> GetLicensesAsync(int playerId);
    /// <summary>Octroi natif `GetLicense` — INSERT si absente. `loc`
    /// = slot initial (0 natif, la classe active re-slote ensuite).</summary>
    Task AddLicenseAsync(int playerId, short itemId, short loc);
    /// <summary>`UPDATE ... SET loc` natif — repositionne une licence
    /// (slot 1..7) ou la déséquipe (0).</summary>
    Task SetLicenseLocAsync(int playerId, short itemId, short loc);
    /// <summary>`RemoveLicense` natif — supprime la ligne (licence
    /// remplacée par un tier supérieur via CItemData+0x8c).</summary>
    Task RemoveLicenseAsync(int playerId, short itemId);
}
