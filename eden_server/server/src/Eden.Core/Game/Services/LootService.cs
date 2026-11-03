using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de loot — correspond a CTreasure / CItemFactory /
/// CMonster::GiveClassItemDrop.
///
/// Roule les tables de drop ponderees (S_DropItem.ini) et les tables
/// de loot par classe (S_ClassDropItem.ini).
/// </summary>
public sealed class LootService
{
    private readonly Dictionary<int, DropAndSpeedParsers.DropTable> _dropTables;
    private readonly Dictionary<int, GameDataParsers.ClassDropTable> _classDrops;

    public LootService(
        IEnumerable<DropAndSpeedParsers.DropTable> dropTables,
        IEnumerable<GameDataParsers.ClassDropTable> classDrops)
    {
        _dropTables = dropTables.ToDictionary(t => t.Id);
        _classDrops = classDrops.ToDictionary(t => t.Id);
    }

    public sealed record LootResult(int ItemId, int Quantity, string ItemName);

    /// <summary>Table de drop brute d'un mob — accès au header d'or
    /// (GoldChancePct/GoldCenter/GoldSpread, GiveGold @08497200).</summary>
    public DropAndSpeedParsers.DropTable? GetDropTable(int tableId)
        => _dropTables.TryGetValue(tableId, out var t) ? t : null;

    /// <summary>
    /// Roule une table de drop ponderee — les chances somment a ~100.
    /// Retourne l'entree tiree ou null si le roll tombe dans le vide.
    /// </summary>
    /// <param name="rateMult">Multiplicateur de chance de chaque
    /// entrée (perk Limitless « drop_rate » ×2 → chances doublées,
    /// la part « vide » du roll rétrécit). Défaut 1.</param>
    public LootResult? RollDropTable(int tableId,
        double rateMult = 1.0)
    {
        if (!_dropTables.TryGetValue(tableId, out var table)) return null;
        var total = table.Entries.Sum(e => e.ChancePercent) * rateMult;
        if (total <= 0) return null;

        var roll = (float)(Random.Shared.NextDouble() * total);
        foreach (var e in table.Entries)
        {
            roll -= (float)(e.ChancePercent * rateMult);
            if (roll < 0)
                return new LootResult(e.ItemId, Math.Max(1, e.Quantity), e.ItemName);
        }
        return null;
    }

    /// <summary>
    /// Tirages d'un pool de <c>S_DropItem</c> — <c>CItemFactory::GenerateDrop
    /// </c> @0x08091720 : le pool est tiré <c>count</c> fois (col 6 pour le
    /// pool normal, col 91 pour le pool « break »), chaque tirage donnant
    /// au plus UN objet (jet uniforme sur la plage ; la part non couverte
    /// par les chances = rien). Count 0 = aucun drop (retour immédiat
    /// natif). <paramref name="breakPool"/> = <c>GenerateDrop(1, …)</c>,
    /// tiré en plus quand la patience du mob est &lt; 1 à la mort.
    /// </summary>
    /// <param name="rateMult">Multiplicateur de chance (perk drop_rate,
    /// taux serveur) — le jet natif réduit la plage de la « chance »
    /// du tueur, équivalent à multiplier les chances.</param>
    public IReadOnlyList<LootResult> RollDropPool(int tableId,
        bool breakPool, double rateMult = 1.0, Random? rng = null)
    {
        if (!_dropTables.TryGetValue(tableId, out var table))
            return Array.Empty<LootResult>();
        var entries = breakPool ? table.BreakEntries : table.Entries;
        var count = breakPool ? table.BreakRollCount : table.RollCount;
        if (count < 1 || entries.Count == 0)
            return Array.Empty<LootResult>();
        rng ??= Random.Shared;
        var total = entries.Sum(e => e.ChancePercent) * rateMult;
        if (total <= 0) return Array.Empty<LootResult>();
        // Plage minimale 100 % : sans multiplicateur la somme des chances
        // est ≤ 100 (le reste = vide) ; avec, elle ne peut pas dépasser.
        var range = Math.Max(100.0, total);
        var results = new List<LootResult>();
        for (var n = 0; n < count; n++)
        {
            var roll = rng.NextDouble() * range;
            foreach (var e in entries)
            {
                roll -= e.ChancePercent * rateMult;
                if (roll < 0)
                {
                    results.Add(new LootResult(e.ItemId,
                        Math.Max(1, e.Quantity), e.ItemName));
                    break;
                }
            }
        }
        return results;
    }

    /// <summary>
    /// Roule la table de classe d'un monstre — chaque entree a sa propre
    /// chance independante (chance/100 d'etre lootee).
    /// Correspond a GiveClassItemDrop.
    /// </summary>
    public IReadOnlyList<LootResult> RollClassDrop(int monsterClassId)
    {
        if (!_classDrops.TryGetValue(monsterClassId, out var table))
            return Array.Empty<LootResult>();

        var results = new List<LootResult>();
        foreach (var e in table.Entries)
        {
            if (e.Chance >= 100 || Random.Shared.NextDouble() * 100 <
                e.Chance * ServerConfig.EffectiveClassDropRate)
                results.Add(new LootResult(e.ItemId, Math.Max(1, e.Quantity), e.ItemName));
        }
        return results;
    }

    /// <summary>
    /// Loot complet d'un monstre: table ponderee + drops de classe.
    /// </summary>
    public IReadOnlyList<LootResult> LootMonster(int monsterId, int monsterClassId)
    {
        var loot = new List<LootResult>();
        var rolled = RollDropTable(monsterId);
        if (rolled != null) loot.Add(rolled);
        loot.AddRange(RollClassDrop(monsterClassId));
        return loot;
    }
}
