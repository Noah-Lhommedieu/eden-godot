namespace Eden.Core.Game.Mall;

/// <summary>
/// Catalogue item mall chargé et validé — équivalent C# du remplissage
/// de <c>CGlobalInfo::AllItemMallItem</c> (map&lt;group, map&lt;index,
/// item*&gt;) dans <c>CAccountDBO::GetAllItemMallItem</c> (désasm world
/// @0x808c650, PGAccountDBO.cc). Chaque validation native est reproduite
/// ligne à ligne ; les dates <c>YYYYMMDDHH</c> sont converties en epoch
/// local comme le <c>timelocal</c> natif.
/// </summary>
public sealed class ItemMallCatalog
{
    /// <summary>Version du catalogue — <c>CGlobalInfo::ItemMallVersion</c>
    /// natif : le client la renvoie dans ReqBuyItem (champ 4) et reçoit
    /// le code 2 s'il est périmé (alors le monde renvoie 62+79).</summary>
    public ushort Version { get; init; } = 1;

    /// <summary>Entrées triées (group puis index) prêtes à sérialiser.</summary>
    public IReadOnlyList<ItemMallEntry> Entries { get; }

    public ItemMallCatalog(IReadOnlyList<ItemMallEntry> entries)
        => Entries = entries;

    /// <summary>Lookup (group, index, moneyUnit) — la table native est
    /// indexée par (group, index) avec money_unit en clé unique DB.</summary>
    public ItemMallEntry? Find(int group, int index, int moneyUnit) =>
        Entries.FirstOrDefault(e =>
            e.Group == group && e.Index == index &&
            (e.MoneyUnit == moneyUnit || e.Group == 99));

    /// <summary>
    /// Filtre les lignes brutes selon les validations natives de
    /// chargement : item_num&lt;0 refusé, 0→1 ; point=0 refusé hors
    /// groupe 99 ; special_price≥point refusé ; item inexistant refusé ;
    /// item_num&gt;maxstack refusé ; money_unit∉{1,2,3} refusé hors
    /// groupe 99 ; not_sell_date passée → entrée sautée ; sell_date
    /// passée → sell_date = 0 (déjà en vente).
    /// </summary>
    /// <param name="rows">Lignes brutes du SELECT sell=1.</param>
    /// <param name="itemExists">Prédicat « item présent + stack max »
    /// (retourne le maxstack, 0 = inexistant) — null = pas de check
    /// (monde sans gamedata chargé).</param>
    /// <param name="now">Horloge injectée pour les tests.</param>
    public static ItemMallCatalog Load(IReadOnlyList<ItemMallEntry> rows,
        Func<int, int>? itemExists = null, DateTimeOffset? now = null)
    {
        var t = (now ?? DateTimeOffset.Now).ToUnixTimeSeconds();
        var list = new List<ItemMallEntry>(rows.Count);
        foreach (var r in rows)
        {
            // item_num < 0 → "[ERROR] amount is 0!" → skip.
            var num = r.ItemNum;
            if (num < 0) continue;
            // item_num == 0 → coerci à 1.
            if (num == 0) num = 1;
            // point == 0 hors groupe 99 → "[ERROR] point is 0!" → skip.
            if (r.Point == 0 && r.Group != 99) continue;
            // special_price != 0 && special_price >= point → skip.
            if (r.SpecialPrice != 0 && r.Point <= r.SpecialPrice) continue;
            // Item inexistant dans l'itemdata → skip (hors groupe 99).
            var maxStack = itemExists?.Invoke(r.ItemId) ?? -1;
            if (r.Group != 99 && maxStack == 0) continue;
            // maxstack > 1 && item_num > maxstack → skip.
            if (maxStack > 1 && num > maxStack) continue;
            // money_unit ∉ {1,2,3} hors groupe 99 → skip.
            if (r.Group != 99 && (uint)(r.MoneyUnit - 1) > 2) continue;
            // not_sell_date YYYYMMDDHH → epoch ; <= now → skip (plus en vente).
            var notSell = DateToEpoch(r.NotSellDate);
            if (r.NotSellDate != 0 && notSell <= t) continue;
            // sell_date → epoch ; <= now → 0 (déjà en vente).
            var sell = r.SellDate != 0 ? DateToEpoch(r.SellDate) : 0;
            if (r.SellDate != 0 && sell <= t) sell = 0;

            list.Add(new ItemMallEntry
            {
                ItemId = r.ItemId,
                Group = r.Group,
                Index = r.Index,
                ItemNum = num,
                ItemColor = r.ItemColor,
                MoneyUnit = r.MoneyUnit,
                Point = r.Point,
                SpecialPrice = r.SpecialPrice,
                NumLimit = r.NumLimit,
                SellDate = (int)sell,
                NotSellDate = (int)notSell,
                FortuneBag = r.FortuneBag,
                Note = r.Note,
                LevelLimit = r.LevelLimit,
            });
        }
        return new ItemMallCatalog(list);
    }

    /// <summary>Conversion <c>YYYYMMDDHH</c> → epoch local — même
    /// décomposition que le natif (tm_year = d/1e6-1900,
    /// tm_mon = d%1e6/1e4-1, tm_mday = d%1e4/100, tm_hour = d%100).</summary>
    public static long DateToEpoch(int yyyymmddhh)
    {
        if (yyyymmddhh == 0) return 0;
        try
        {
            var dt = new DateTime(
                yyyymmddhh / 1000000,
                yyyymmddhh % 1000000 / 10000,
                yyyymmddhh % 10000 / 100,
                yyyymmddhh % 100, 0, 0, DateTimeKind.Local);
            return new DateTimeOffset(dt).ToUnixTimeSeconds();
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Prix unitaire natif pour un achat : special_price si ≠ 0 sinon
    /// point ; le coût total = prix × amount. Pour amount ≥ 2, le natif
    /// exige item_num == 1 et un item « non unique » (itemdata+0x76 ≠ 1)
    /// — vérifié par l'appelant via <see cref="ValidateBuyAmount"/>.
    /// </summary>
    public static int TotalCost(ItemMallEntry e, int amount)
    {
        var unit = e.SpecialPrice != 0 ? e.SpecialPrice : e.Point;
        return (int)(unit * (uint)amount);
    }

    /// <summary>Quantité d'items livrée : amount × item_num (amount=1 →
    /// item_num ; amount≥2 exige item_num==1 côté natif).</summary>
    public static int DeliveredQuantity(ItemMallEntry e, int amount) =>
        amount * e.ItemNum;

    /// <summary>Condition native du multi-achat : amount ≥ 2 exige
    /// item_num == 1 (sinon LAB_080e0188 → échec générique).</summary>
    public static bool ValidateBuyAmount(ItemMallEntry e, int amount) =>
        amount < 2 || e.ItemNum == 1;
}
