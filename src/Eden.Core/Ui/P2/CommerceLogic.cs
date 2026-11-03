using System.Text;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Ui.P2;

/// <summary>Boutique cash (ItemMall) — filtrage, catégories, pagination,
/// requête d'achat. Pur : portée de <c>ItemMallWindow.cs</c> Godot,
/// libellés EDENFR.</summary>
public sealed class MallView
{
    public const int RowsPerPage = 8;

    /// <summary>Onglet monétaire (1/2/3).</summary>
    public int MoneyUnit { get; set; } = 1;
    /// <summary>Catégorie (-1 = toutes).</summary>
    public int Group { get; set; } = -1;
    public int Page { get; set; }

    public static string CategoryName(int g) => g switch
    {
        1 => "À la une", 2 => "Objets d'artisanat",
        3 => "Familiers et montures", 4 => "Équipement",
        5 => "Améliorations", 6 => "Mode", 7 => "Teintures",
        8 => "Objets limités", 50 or 99 => "Spécial",
        _ => $"Groupe {g}",
    };

    public List<ItemMallEntry> Filtered(IEnumerable<ItemMallEntry> all)
    {
        var q = all.Where(e => e.MoneyUnit == MoneyUnit);
        if (Group >= 0) q = q.Where(e => e.Group == Group);
        return q.OrderBy(e => e.Group).ThenBy(e => e.Index).ToList();
    }

    /// <summary>Groupes présents pour l'onglet courant (ordre croissant).</summary>
    public List<int> Groups(IEnumerable<ItemMallEntry> all) =>
        all.Where(e => e.MoneyUnit == MoneyUnit)
           .Select(e => e.Group).Distinct().OrderBy(g => g).ToList();

    /// <summary>Sélection de catégorie par index de liste (0 = toutes).</summary>
    public void SelectCategory(IEnumerable<ItemMallEntry> all, int index)
    {
        var groups = Groups(all);
        Group = index <= 0 || index > groups.Count ? -1 : groups[index - 1];
        Page = 0;
    }

    public static int PageCount(int count) =>
        Math.Max(1, (count + RowsPerPage - 1) / RowsPerPage);

    public void ClampPage(int count) =>
        Page = Math.Clamp(Page, 0, PageCount(count) - 1);

    public List<ItemMallEntry> PageItems(List<ItemMallEntry> filtered)
    {
        ClampPage(filtered.Count);
        return filtered.Skip(Page * RowsPerPage).Take(RowsPerPage).ToList();
    }

    /// <summary>Promotions de la catégorie (colonne « soldes »).</summary>
    public static List<ItemMallEntry> Hot(IEnumerable<ItemMallEntry> filtered) =>
        filtered.Where(e => e.SpecialPrice != 0 && e.SpecialPrice < e.Point)
                .Take(6).ToList();

    public static string SellText(ItemMallEntry e, long nowUnix)
    {
        if (e.SellDate != 0 && nowUnix < e.SellDate)
            return "pas encore en vente";
        if (e.NotSellDate != 0)
            return "fin de vente le " +
                DateTimeOffset.FromUnixTimeSeconds(e.NotSellDate)
                    .ToString("dd/MM/yyyy");
        return "en vente";
    }

    public static string Tooltip(ItemMallEntry e, long nowUnix, string? desc)
    {
        var lim = e.NumLimit > 0
            ? $" — limite {e.NumLimit} (acheté {e.Sold})" : "";
        var lvl = e.LevelLimit > 0 ? $" — niv. {e.LevelLimit}+" : "";
        return SellText(e, nowUnix) + lim + lvl +
            (desc != null ? "\n" + desc : "");
    }

    /// <summary>Requête d'achat monde (NCID 23) — clé = unité*100+groupe.</summary>
    public static WorldItemMallBuyRequest BuyRequest(ItemMallEntry e,
        ushort version, string recipient = "") => new()
    {
        Key = (short)(e.MoneyUnit * 100 + e.Group),
        Index = (short)e.Index,
        Amount = 1,
        Version = version,
        Recipient = recipient,
    };

    public static string Detail(ItemMallEntry e, string itemName, string? desc)
    {
        var parts = new List<string>
        {
            $"{itemName} — groupe {e.Group} idx {e.Index} unité {e.MoneyUnit}",
            $"prix {e.Point} promo {e.SpecialPrice} lot ×{e.ItemNum} niv≥{e.LevelLimit}",
        };
        if (e.NumLimit > 0)
            parts.Add($"limite {e.NumLimit}/compte — acheté {e.Sold}");
        if (e.Note.Length > 0) parts.Add("note : " + e.Note);
        if (e.FortuneBag.Length > 0) parts.Add("sac fortune : " + e.FortuneBag);
        if (desc != null) parts.Add(desc);
        return string.Join(" | ", parts);
    }
}

/// <summary>Hôtel des ventes.</summary>
public static class AuctionView
{
    public static string TimeLeft(long expireUnix, long nowUnix)
    {
        if (expireUnix <= 0) return "";
        var left = TimeSpan.FromSeconds(expireUnix - nowUnix);
        if (left.TotalSeconds <= 0) return "expirée";
        return left.TotalHours >= 1
            ? $"{(int)left.TotalHours}h{left.Minutes:D2}"
            : $"{left.Minutes}min";
    }

    /// <summary>Filtre local par nom (le serveur ignore les filtres 114).</summary>
    public static List<AuctionEntry> Filter(IEnumerable<AuctionEntry> es,
        string query, Func<AuctionEntry, string> nameOf)
    {
        var q = query.Trim();
        return es.Where(e => q.Length == 0 ||
            nameOf(e).Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public static string RowText(string name, int price, string timeLeft) =>
        $"  {name}  —  {price:N0} or   ({timeLeft})";

    public static string BuyLabel(string name, int price) =>
        $"Acheter {name} — {price:N0} or";

    public static string Status(int count) =>
        $"{count} annonce(s) en vente — achat livré par courrier";

    public static int ParseAmount(string? t) =>
        int.TryParse((t ?? "").Trim(), out var v) ? Math.Max(v, 0) : 0;

    /// <summary>Mise en vente : price &gt; 0 requis ; buyout 0 = aucun.</summary>
    public static ZoneAuctionSell? SellRequest(int bag, int slot,
        string? price, string? buyout, out string error)
    {
        error = "";
        if (bag < 0 || slot < 0) { error = "Déposez un objet depuis le sac."; return null; }
        var p = ParseAmount(price);
        if (p <= 0) { error = "Prix invalide."; return null; }
        return new ZoneAuctionSell
        { Bag = (short)bag, Slot = (short)slot, Price = p,
          Buyout = ParseAmount(buyout) };
    }
}

/// <summary>Artisanat (recettes collecte/combinaison).</summary>
public static class CraftView
{
    public static string ItemName(Func<int, string?> names, int id) =>
        names(id) ?? $"objet n°{id}";

    /// <summary>Recettes (id, libellé) d'un type d'ordre.</summary>
    public static List<(int Id, string Label)> Recipes(int workType,
        IReadOnlyDictionary<int, CraftTable.CollectRecipe>? collect,
        IReadOnlyDictionary<int, CraftTable.CombineRecipe>? combine,
        Func<int, string> itemName)
    {
        var res = new List<(int, string)>();
        var cat = workType - 12;
        if (CraftTable.WorkTypes.IsCollect(workType) && collect != null)
        {
            foreach (var r in collect.Values.Where(r => r.Category == cat)
                         .OrderBy(r => r.Id))
                res.Add((r.Id, $"{r.Name}  ({r.CatalystQty}× " +
                    $"{itemName(r.CatalystItemId)}, {r.Gold} or)"));
            if (res.Count == 0)
                foreach (var r in collect.Values.OrderBy(r => r.Id))
                    res.Add((r.Id, $"[cat{r.Category}] {r.Name}"));
        }
        else if (CraftTable.WorkTypes.IsCombine(workType) && combine != null)
        {
            var matched = combine.Values.Where(r => r.Category == cat).ToList();
            var src = matched.Count > 0 ? matched : combine.Values.ToList();
            foreach (var r in src.OrderBy(r => r.Id))
                res.Add((r.Id, $"{itemName(r.ProductItemId)} ×{r.ProductQty}  " +
                    $"({r.Gold} or, {r.SuccessRate}%)"));
        }
        return res;
    }

    public static string Detail(int id,
        IReadOnlyDictionary<int, CraftTable.CollectRecipe>? collect,
        IReadOnlyDictionary<int, CraftTable.CombineRecipe>? combine,
        Func<int, string> itemName)
    {
        var sb = new StringBuilder();
        if (collect != null && collect.TryGetValue(id, out var c))
        {
            sb.AppendLine($"{c.Name} — n°{id}");
            sb.AppendLine($"Catalyseur : {c.CatalystQty}× {itemName(c.CatalystItemId)}");
            sb.AppendLine($"Or : {c.Gold}  Durée : " +
                (c.Instant ? "instantanée" : $"{c.DurationMin} min") +
                $"  EXP : {c.Exp}");
            sb.AppendLine("Produits :");
            foreach (var p in c.Products)
                sb.AppendLine($"  {itemName(p.ItemId)} ×{p.Qty} ({p.Rate:0}%)");
        }
        else if (combine != null && combine.TryGetValue(id, out var cb))
        {
            sb.AppendLine($"{itemName(cb.ProductItemId)} ×{cb.ProductQty} — n°{id}");
            sb.AppendLine($"Réussite {cb.SuccessRate}%  Or : {cb.Gold}  " +
                (cb.Instant ? "instantané" : $"{cb.DurationMin} min"));
            sb.AppendLine("Matériaux :");
            foreach (var m in cb.Materials)
                sb.AppendLine($"  {itemName(m.ItemId)} ×{m.Qty}");
        }
        return sb.ToString();
    }

    /// <summary>Id d'objet catalyseur requis pour la recette (0 = inconnu).</summary>
    public static int WantedCatalyst(int workType, int workId,
        IReadOnlyDictionary<int, CraftTable.CollectRecipe>? collect,
        IReadOnlyDictionary<int, CraftTable.CombineRecipe>? combine,
        Func<int, int?> kindOf)
    {
        if (CraftTable.WorkTypes.IsCollect(workType) && collect != null &&
            collect.TryGetValue(workId, out var c))
            return c.CatalystItemId;
        if (CraftTable.WorkTypes.IsCombine(workType) && combine != null &&
            combine.TryGetValue(workId, out var cb))
        {
            var kind = CraftTable.WorkTypes.CatalystKind(workType);
            if (kind != 0)
                foreach (var m in cb.Materials)
                    if (kindOf(m.ItemId) == kind) return m.ItemId;
            if (cb.Materials.Count > 0) return cb.Materials[0].ItemId;
        }
        return 0;
    }

    /// <summary>Emplacement (sac 0..4, slot) du catalyseur dans l'inventaire.</summary>
    public static (int Bag, int Slot) FindInBag(int wanted,
        IEnumerable<(int ItemId, int Bag, int Slot)> inventory)
    {
        if (wanted == 0) return (-1, -1);
        foreach (var it in inventory)
            if (it.ItemId == wanted && it.Bag is >= 0 and <= 4)
                return (it.Bag, it.Slot);
        return (-1, -1);
    }
}

/// <summary>Calendrier de récompenses quotidiennes (bitmasks serveur).</summary>
public sealed class DailyView
{
    public int DayIndex { get; private set; }
    public int OnlineSeconds { get; private set; }
    public ulong AwardDays { get; private set; }
    public ulong AwardRecords { get; private set; }
    public bool Enabled { get; private set; } = true;

    public void Apply(int dayIndex, int onlineSeconds, long awardDays,
        long awardRecords, int kind)
    {
        DayIndex = dayIndex;
        OnlineSeconds = onlineSeconds;
        AwardDays = (ulong)awardDays;
        AwardRecords = (ulong)awardRecords;
        Enabled = kind != 4;
    }

    private static ulong Bit(int day) => 1UL << ((day - 1) & 0x3f);
    public bool Completed(int day) => (AwardDays & Bit(day)) != 0;
    public bool Claimed(int day) => (AwardRecords & Bit(day)) != 0;
    public bool Claimable(int day) => Completed(day) && !Claimed(day);
    public bool Current(int day) => day == DayIndex + 1;

    public bool HasClaimable(IEnumerable<int> days) =>
        days.Any(Claimable);

    /// <summary>Ouverture auto à la réception (Kind 0 ou 5) s'il y a à réclamer.</summary>
    public bool ShouldAutoOpen(int kind, IEnumerable<int> days) =>
        (kind == 0 || kind == 5) && HasClaimable(days);

    public string Status(IReadOnlyCollection<int> days) =>
        !Enabled ? "Système désactivé sur le serveur."
            : $"Jour {DayIndex + 1} — {days.Count(Completed)}/{days.Count} accomplis";

    public string Timer(IReadOnlyDictionary<int, DailyAwardTable.DailyAwardDef> defs)
    {
        if (!Enabled) return "";
        var next = defs.TryGetValue(DayIndex + 1, out var nd)
            ? nd.RequiredSeconds : 0;
        return $"En ligne : {OnlineSeconds / 60} min" +
            (next > 0 ? $" — jour suivant à {next / 60} min" : "");
    }

    /// <summary>État d'une case : 0 verrouillée, 1 en cours, 2 réclamable, 3 réclamée.</summary>
    public int CellState(int day) =>
        Claimed(day) ? 3 : Claimable(day) ? 2 : Current(day) ? 1 : 0;
}
