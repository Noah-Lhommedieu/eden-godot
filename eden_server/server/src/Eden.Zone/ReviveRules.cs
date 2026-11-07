using Eden.Core.Game.Services;

namespace Eden.Zone;

/// <summary>Règles pures de réanimation du joueur (NCID 20 client).
/// Wire <c>[i32 type]</c> : 0 = point de sauvegarde (téléport +
/// pénalité d'exp), 1 = sur place (pénalité d'exp), 5 = item de revive
/// sur place (sans pénalité — type natif 5 de
/// <c>CNE_CZ_ZoneServerRevive::Execute</c> @0x08280670).</summary>
public static class ReviveRules
{
    public const int TypeSavePoint = 0;
    public const int TypeInPlace = 1;
    public const int TypeItem = 5;
    /// <summary>6 = Feuille de Vie (verte) : revive au point de
    /// sauvegarde (Gardien de l'Âme), sans perte d'exp.</summary>
    public const int TypeLifeLeaf = 6;

    /// <summary>Pénalité d'exp de mort, en % de l'exp du palier.</summary>
    public const int ExpLossPercent = 10;

    /// <summary>« Feuille de Vie de Locus » (violette, item-mall) :
    /// revive sur place sans pénalité. c_item/t_itemmall 40036, 41468
    /// (icône I00523).</summary>
    public static readonly IReadOnlySet<int> LocusLeafIds =
        new HashSet<int> { 40036, 41468 };

    /// <summary>« Feuille de Vie » (verte) : revive près du Gardien de
    /// l'Âme, sans pénalité. 40035, 40157 (icône I00522).</summary>
    public static readonly IReadOnlySet<int> LifeLeafIds =
        new HashSet<int> { 40035, 40157 };

    /// <summary>Ids de feuille acceptés pour un type de revive par item.</summary>
    public static IReadOnlySet<int>? LeafIdsFor(int type) => type switch
    {
        TypeItem => LocusLeafIds,
        TypeLifeLeaf => LifeLeafIds,
        _ => null,
    };

    /// <summary>Le type consomme une feuille.</summary>
    public static bool UsesLeaf(int type) => LeafIdsFor(type) != null;

    /// <summary>L'item est une feuille de vie.</summary>
    public static bool IsLeaf(int itemId) =>
        LocusLeafIds.Contains(itemId) || LifeLeafIds.Contains(itemId);

    /// <summary>Type de revive demandé valide.</summary>
    public static bool IsKnownType(int type) =>
        type is TypeSavePoint or TypeInPlace or TypeItem or TypeLifeLeaf;

    /// <summary>Le type applique la pénalité d'exp.</summary>
    public static bool AppliesExpLoss(int type) => !UsesLeaf(type);

    /// <summary>Pénalité effective : jamais en donjon, quel que soit le type.</summary>
    public static bool AppliesExpLoss(int type, bool inDungeon) =>
        AppliesExpLoss(type) && !inDungeon;

    /// <summary>Le type téléporte au point de sauvegarde.</summary>
    public static bool Teleports(int type) =>
        type is TypeSavePoint or TypeLifeLeaf;

    /// <summary>Perte d'exp : <see cref="ExpLossPercent"/> % de
    /// <paramref name="expNeeded"/>, bornée à l'exp courante (jamais
    /// négative, jamais de niveau perdu).</summary>
    public static long ExpLoss(long currentExp, long expNeeded)
    {
        if (currentExp <= 0 || expNeeded <= 0) return 0;
        return Math.Min(currentExp, expNeeded * ExpLossPercent / 100);
    }

    /// <summary>L'enchant d'usage porte une commande Revive/RevivePlus.</summary>
    public static bool IsReviveEnchant(EnchantService enchants,
        int enchantId)
    {
        if (enchantId <= 0) return false;
        foreach (var c in enchants.GetUseCommands(enchantId))
            if (Eden.Core.Data.EnchantCommands.Names.TryGetValue(
                    c.CommandId, out var n) && n is "Revive" or "RevivePlus")
                return true;
        return false;
    }

    /// <summary>L'item est un item de revive.</summary>
    public static bool IsReviveItem(EnchantService enchants, int itemId,
        int useEnchantId) =>
        IsLeaf(itemId) ||
        IsReviveEnchant(enchants, useEnchantId);
}
