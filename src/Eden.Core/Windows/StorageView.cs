using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Windows;

/// <summary>
/// Logique pure des vues banque / néobank / coffre famille (port des
/// fenêtres Godot <c>BankWindow</c>, <c>NeobankWindow</c>,
/// <c>StorageItemCell</c>) : quantité d'un stack, filtre de
/// recherche (« nom » ou « ID: n »), pagination de la grille étendue,
/// placement des items par loc.
/// </summary>
public static class StorageView
{
    /// <summary>Stackable = MaxStack&gt;1 et non équipable (règle miroir
    /// serveur <c>BankService.IsStackable</c>).</summary>
    public static bool IsStackable(ItemTable.ItemDef? def) =>
        def is { MaxStack: > 1, EquipLoc: <= 0 };

    /// <summary>Quantité à déposer/retirer pour une ligne : le serveur
    /// range le count dans <c>Durability</c> pour les stackables ; les
    /// items 274 portent aussi <c>Amount</c>.</summary>
    public static int StackCount(InventorySlot it, ItemTable.ItemDef? def)
    {
        var n = IsStackable(def)
            ? Math.Max(it.Durability, it.Amount)
            : Math.Max(it.Amount, 0);
        return Math.Max(n, 1);
    }

    /// <summary>Filtre neobank : vide = tout ; « ID: n » ou nombre nu =
    /// id exact ; sinon sous-chaîne du nom (insensible casse).</summary>
    public static bool Matches(string filter, InventorySlot it,
        Func<int, string?> nameOf)
    {
        if (filter.Length == 0) return true;
        var f = filter.Trim();
        if (f.StartsWith("ID:", StringComparison.OrdinalIgnoreCase))
            f = f[3..].Trim();
        if (int.TryParse(f, out var id))
            return it.ItemId == id;
        return (nameOf(it.ItemId) ?? "").Contains(f,
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Pagination : (première loc, nb de cellules, nb de pages,
    /// page bornée). Mode défilement (<paramref name="paged"/> faux) =
    /// une seule page couvrant toute la capacité.</summary>
    public static (int First, int Count, int Pages, int Page) Page(
        int capacity, int perPage, int page, bool paged)
    {
        var cap = Math.Max(capacity, 1);
        var per = paged ? Math.Max(perPage, 1) : cap;
        var pages = Math.Max(1, (cap + per - 1) / per);
        page = Math.Clamp(page, 0, pages - 1);
        var first = paged ? page * per : 0;
        var count = paged ? Math.Min(per, Math.Max(0, cap - first)) : cap;
        return (first, count, pages, page);
    }

    /// <summary>Items d'un conteneur indexés par loc (dernier gagne).</summary>
    public static Dictionary<int, InventorySlot> ByLoc(
        IEnumerable<InventorySlot> items)
    {
        var d = new Dictionary<int, InventorySlot>();
        foreach (var it in items) d[it.Slot] = it;
        return d;
    }

    /// <summary>Rangée de gauche à droite sur <paramref name="cols"/>
    /// colonnes pour la loc <paramref name="i"/>.</summary>
    public static (int Col, int Row) Cell(int i, int cols) =>
        (i % Math.Max(cols, 1), i / Math.Max(cols, 1));

    /// <summary>Ligne de journal coffre famille (texte serveur tel quel,
    /// action en préfixe pour le tri visuel).</summary>
    public static string LogLine(FamilyStorageLogEntry e) =>
        e.Detail.Length > 0 ? e.Detail : $"#{e.Action}";
}
