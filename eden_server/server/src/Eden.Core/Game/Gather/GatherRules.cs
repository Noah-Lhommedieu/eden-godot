using Eden.Core.Game.Foundation;
using Eden.Core.Game.Loot;

namespace Eden.Core.Game.Gather;

/// <summary>Raison d'arrêt d'une récolte continue (journal + message au joueur).</summary>
public enum GatherStopReason
{
    Moved, SpellBreak, Damaged, Died, Migrated, Disconnected,
    ToolChanged, ToolBroken, BagFull, LeftArea,
}

/// <summary>Produit récoltable d'un outil (ligne <c>c_familycollect</c>).</summary>
public sealed record GatherProduct(int ItemId, double RatePercent, int BaseQuantity);

/// <summary>Une récolte réussie.</summary>
public readonly record struct GatherDrop(int ItemId, int Quantity);

/// <summary>
/// Règles de la récolte continue (pioche / gants / boule de cristal), décidées
/// par le propriétaire (PLAN-ACTION-3 A3) : après un cast de 3 s, un statut est
/// posé ; toutes les ~30 s UNE ressource aléatoire parmi celles de l'outil, avec
/// une quantité raisonnée selon la rareté ; la durabilité baisse de 1 point toutes
/// les 10 s (180 points ≈ 30 min) ; bouger annule. Pur : aucune dépendance jeu.
/// </summary>
public static class GatherRules
{
    public const double DefaultTickSeconds = 30;
    public const double DefaultCastSeconds = 3;
    public const double DurabilitySecondsPerPoint = 10;

    /// <summary>Quantité max selon la rareté du produit (son taux) : un commun
    /// (≥ 25 %) peut tomber en plusieurs exemplaires, un rare jamais.</summary>
    public static int MaxQuantityForRate(double ratePercent) => ratePercent switch
    {
        >= 25 => 4,
        >= 5 => 2,
        _ => 1,
    };

    /// <summary>Pool de tirage d'un outil : poids = taux du produit, quantité
    /// entre 1 et le plafond de rareté (× quantité de base).</summary>
    public static WeightedPool<int> BuildPool(IEnumerable<GatherProduct> products) =>
        new(products.Where(p => p.ItemId > 0 && p.RatePercent > 0).Select(p =>
        {
            var baseQty = Math.Max(1, p.BaseQuantity);
            return new PoolEntry<int>(p.ItemId, p.RatePercent, baseQty,
                baseQty * MaxQuantityForRate(p.RatePercent));
        }));

    /// <summary>Un tirage de récolte (un tick). Null si l'outil n'a aucun produit.</summary>
    public static GatherDrop? Tick(WeightedPool<int> pool, IRandom rng) =>
        LootRule.Of(pool).Roll(rng, new Dictionary<string, long>()).FirstOrDefault() is { Quantity: > 0 } d
            ? new GatherDrop(d.Item, d.Quantity) : null;

    /// <summary>Durabilité à retirer après <paramref name="elapsedSeconds"/> de récolte,
    /// avec le multiplicateur <c>gather.durability_cost</c> (VIP 0,75). La fraction
    /// non consommée est reportée dans <paramref name="carry"/> : aucune perte d'arrondi.</summary>
    public static int DurabilityCost(double elapsedSeconds, double costMultiplier,
        ref double carry)
    {
        carry += elapsedSeconds / DurabilitySecondsPerPoint * costMultiplier;
        var whole = (int)Math.Floor(carry);
        carry -= whole;
        return whole;
    }

    /// <summary>Numéro de notice envoyé au client (9063, état 3) : le client formate le
    /// texte FR/EN lui-même — les chaînes non ASCII passent mal par le canal texte
    /// serveur (encodage Big5). 0 = pas de notice.</summary>
    public static int NoticeIdFor(string errorCode) => errorCode switch
    {
        GameErrors.GatherOutOfArea => 1,
        GameErrors.GatherMoved => 2,
        GameErrors.GatherToolBroken => 3,
        GameErrors.GatherBagFull => 4,
        GameErrors.GatherAlready => 5,
        _ => 0,
    };

    /// <summary>Code d'erreur joueur d'une raison d'arrêt.</summary>
    public static string ErrorCodeFor(GatherStopReason reason) => reason switch
    {
        GatherStopReason.Moved => GameErrors.GatherMoved,
        GatherStopReason.ToolBroken => GameErrors.GatherToolBroken,
        GatherStopReason.BagFull => GameErrors.GatherBagFull,
        GatherStopReason.LeftArea => GameErrors.GatherOutOfArea,
        _ => GameErrors.GatherMoved,
    };
}
