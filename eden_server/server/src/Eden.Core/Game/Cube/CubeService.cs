using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.Cube;

/// <summary>
/// Service du cube Horadric — portage de
/// <c>lapis::CItemFactory::CheckCubeCosts / ExecuteCubeOp /
/// PickAnItem / PickAColor / LoadCubeGroup2Item</c> (désasm zone
/// @0x80ba380 / @0x80bceb0 / @0x809d210 / @0x80a0460 / @0x8364230).
/// Les recettes viennent de S_ItemChest.ini (voir
/// <see cref="ShopAndMiscParsers.ItemChestDef"/>) ; le pool "groupe"
/// est l'index inversé de la col 75 de S_Item (CItemData+0x104).
/// </summary>
public sealed class CubeService
{
    /// <summary>Recettes par id (clé envoyée en 232 field1).</summary>
    private readonly Dictionary<int, ShopAndMiscParsers.ItemChestDef> _recipes = new();

    /// <summary>Index inversé groupe→itemIds —
    /// <c>map&lt;uint,set&lt;uint&gt;&gt;</c> natif CZoneServer+0x3e4,
    /// rempli par LoadCubeGroup2Item depuis CItemData+0x104.</summary>
    private readonly Dictionary<int, HashSet<int>> _groupToItems = new();

    private readonly Dictionary<int, ItemParser.ItemDef> _itemDefs;
    private readonly IReadOnlyList<EnchantParser.EnchantDef> _enchants;
    private readonly Random _rng = new();

    public CubeService(IEnumerable<ItemParser.ItemDef> itemDefs,
        IReadOnlyList<EnchantParser.EnchantDef> enchants)
    {
        _itemDefs = itemDefs.ToDictionary(i => i.Id);
        _enchants = enchants;
        foreach (var def in itemDefs)
            foreach (var g in def.CubeGroupIds)
            {
                if (!_groupToItems.TryGetValue(g, out var set))
                    _groupToItems[g] = set = new HashSet<int>();
                set.Add(def.Id);
            }
    }

    /// <summary>Charge S_ItemChest.ini (appelé au démarrage).</summary>
    public void LoadRecipes(IEnumerable<ShopAndMiscParsers.ItemChestDef> recipes)
    {
        _recipes.Clear();
        foreach (var r in recipes) _recipes[r.Id] = r;
    }

    public int RecipeCount => _recipes.Count;

    /// <summary>Résultat de la simulation CheckCubeCosts.</summary>
    public sealed class CostCheck
    {
        public bool Ok;
        /// <summary>Index de texte client à émettre avant le 434
        /// (0x232 or, 0x4f9 sac plein, 0x15af niveau lucky, 0x21c4
        /// bonus lucky) — 0 = fail silencieux natif.</summary>
        public int FailTextId;
        /// <summary>Ids des items auxiliaires posés (op+0x28 natif) —
        /// pool de produits pour les recettes product==0 et source de
        /// qualité héritée (PickAColor clé 0).</summary>
        public List<int> AuxItemIds { get; } = new();
        /// <summary>Qualités des items auxiliaires (op+0x34 natif —
        /// clone+0x2c).</summary>
        public List<int> AuxQualities { get; } = new();
        /// <summary>Bonus de taux du cube chanceux (enchant 8007
        /// param1) — ajouté à recipe.SuccessRate au jet.</summary>
        public int LuckyBonus;
        /// <summary>Vrai si le lucky cube sera consommé (stack==1 →
        /// libère un slot — compté dans les slots libérés natifs).</summary>
        public bool LuckyConsumed;
        /// <summary>Produit "lié" : vrai seulement si chaque input est
        /// lié ou non-flaggé échangeable (itemdata+0x30 &0x80 / &0x84,
        /// désasm boucle local_1ec @0x80bdd90).</summary>
        public bool ProductBound = true;
        /// <summary>Slots libérés par la consommation (local_12 —
        /// initialisé aux slots vides du sac 0).</summary>
        public int FreeAfterConsume;
        /// <summary>Quantité à consommer par item posé, parallèle à
        /// `placed` (DeductItem natif) — 0 pour les auxiliaires (non
        /// consommés, ils ne font que définir le pool/produits).</summary>
        public List<int> Consumed { get; } = new();
    }

    /// <summary>Item placé : entité inventaire + sa définition.</summary>
    public sealed record PlacedItem(int ItemId, int Stack, int Quality,
        bool Bound, ItemParser.ItemDef? Def);

    /// <summary>Simulation de CheckCubeCosts(validateOnly=1).
    /// <paramref name="placed"/> = les (idx,slot) posés résolus
    /// (parallèles comme les vecteurs wire), <paramref name="lucky"/> =
    /// item au slot (luckyIdx,luckySlot) ou null si (-1,-1).
    /// Reproduit les fails natifs : req non couvertes → silencieux ;
    /// or → 0x232 ; place → 0x4f9 ; lucky cap → 0x15af ; bonus lucky
    /// requis (recettes 4567-4590) → 0x21c4.</summary>
    public CostCheck CheckCosts(ShopAndMiscParsers.ItemChestDef recipe,
        int charGold, int charLevel, int emptyBagSlots,
        IReadOnlyList<PlacedItem> placed, PlacedItem? lucky)
    {
        var r = new CostCheck { FreeAfterConsume = emptyBagSlots };
        if (placed.Count == 0) return r;

        // Map des requis {itemId:quantité} — main + sous-ingrédients
        // (local_6c natif). Les groupes col75 des items posés peuvent
        // satisfaire un id requis (IsCubeGroupValid).
        var req = new Dictionary<int, int>();
        if (recipe.ItemId != 0 && recipe.MainAmount != 0)
            req[recipe.ItemId] = recipe.MainAmount;
        foreach (var s in recipe.Subs)
            if (s.ItemId != 0 && s.Amount != 0)
                req[s.ItemId] = req.TryGetValue(s.ItemId, out var q)
                    ? q + s.Amount : s.Amount;

        foreach (var p in placed)
        {
            if (p.Def == null) return r;

            // Qualité aux obligatoire (recipe+0x14) — contrôlée pour
            // les items non-requis (branche aux native).
            var directReq = req.TryGetValue(p.ItemId, out var need) && need > 0;
            var groupReq = !directReq
                && p.Def.CubeGroupIds.Any(g => req.TryGetValue(g, out var n) && n > 0);
            var take = 0;
            if (!directReq && !groupReq)
            {
                // Branche aux : qualité exigée (col5) — 0 libre,
                // 1 accepte aussi la qualité 0 (désasm puVar10[5]).
                if (recipe.AuxQualityReq != 0
                    && p.Quality != recipe.AuxQualityReq
                    && !(recipe.AuxQualityReq == 1 && p.Quality == 0))
                    return r;
                r.AuxItemIds.Add(p.ItemId);
                r.AuxQualities.Add(p.Quality);
            }
            else
            {
                // DeductRequirement : non-stack consomme 1 unité, stack
                // prend min(stack, reste) ; +1 slot libéré si tout part.
                var reqId = directReq ? p.ItemId
                    : p.Def.CubeGroupIds.FirstOrDefault(g =>
                        req.TryGetValue(g, out var n) && n > 0);
                if (req.TryGetValue(reqId, out var left) && left > 0)
                {
                    take = MaxStack(p.ItemId) < 2
                        ? 1 : Math.Min(left, p.Stack);
                    req[reqId] = left - take;
                    if (take >= p.Stack) r.FreeAfterConsume++;
                }
            }
            r.Consumed.Add(take);

            // Propagation du bind natif : un input libre dont la
            // définition n'impose pas le bind (flag 0x80 no-trade de
            // itemdata+0x30) rend le produit libre — sinon lié.
            if (!p.Bound && (p.Def.Flags & 0x80) == 0)
                r.ProductBound = false;
        }

        // Toutes les requêtes doivent être couvertes → fail silencieux.
        if (req.Values.Any(v => v > 0)) return r;

        // Or (char+0x4b0 ≥ recipe+0x24) → texte 0x232.
        if (charGold < recipe.Gold) { r.FailTextId = 0x232; return r; }

        // Niveau (recipe+0x38 ≤ char+0x470) → fail silencieux natif.
        if (recipe.RequiredLevel > charLevel) return r;

        // Lucky cube : CChanceItem (enchant 8007), cap ≥ niveau requis.
        if (lucky != null)
        {
            var luck = LuckyInfo(lucky.Def);
            if (luck == null) return r;
            if (luck.Value.maxLevel < recipe.RequiredLevel)
            { r.FailTextId = 0x15af; return r; }
            // Le cube chanceux est toujours consommé (DeductItem
            // native) ; son slot ne se libère que si le stack part.
            r.LuckyConsumed = true;
            if (lucky.Stack == 1) r.FreeAfterConsume++;
            if (recipe.Id >= 0x11d7 && recipe.Id <= 0x11ee
                && luck.Value.bonus < 1)
            { r.FailTextId = 0x21c4; return r; }
            r.LuckyBonus = luck.Value.bonus;
        }

        // Place pour le produit : non-stack → amount ≤ slots libres ;
        // stackable → > 2 (jusqu'à 3 stacks aléatoires, désasm @0x80bafxx).
        var prodDef = ProductDef(recipe);
        if (prodDef != null)
        {
            // Un produit dont la définition force le bind écrase la
            // propagation (itemdata+0x30 &0x80 = no-trade natif).
            if ((prodDef.Flags & 0x80) != 0) r.ProductBound = true;
            if (prodDef.MaxStack < 2)
            {
                if (recipe.ProductAmount > r.FreeAfterConsume)
                { r.FailTextId = 0x4f9; return r; }
            }
            else if (r.FreeAfterConsume <= 2)
            { r.FailTextId = 0x4f9; return r; }
        }

        r.Ok = true;
        return r;
    }

    /// <summary>Pool de produits natif : product==0 → ids aux ;
    /// &lt;100000 → singleton ; ==mainId → groupe−aux (ou groupe si
    /// vide) ; sinon groupe complet (désasm local_15c @0x80be1xx).</summary>
    public HashSet<int> ProductPool(ShopAndMiscParsers.ItemChestDef recipe,
        IReadOnlyList<int> auxIds)
    {
        var pool = new HashSet<int>();
        if (recipe.ProductKey == 0)
        {
            foreach (var id in auxIds) pool.Add(id);
        }
        else if (recipe.ProductKey < 100000)
        {
            pool.Add(recipe.ProductKey);
        }
        else
        {
            if (_groupToItems.TryGetValue(recipe.ProductKey, out var g))
                pool.UnionWith(g);
            if (recipe.ItemId == recipe.ProductKey)
            {
                foreach (var id in auxIds) pool.Remove(id);
                if (pool.Count == 0 && g != null) pool.UnionWith(g);
            }
        }
        // ApplicableSet (col31) : restriction éventuelle du pool.
        if (recipe.ApplicableSet.Count > 0 && pool.Count > 0)
            pool.IntersectWith(recipe.ApplicableSet);
        return pool;
    }

    /// <summary>ItemDef du produit pour le test de stack/place —
    /// natif : productKey ou mainId si 0 (GetItemData(uVar26)).</summary>
    public ItemParser.ItemDef? ProductDef(
        ShopAndMiscParsers.ItemChestDef recipe)
    {
        var key = recipe.ProductKey != 0 && recipe.ProductKey < 100000
            ? recipe.ProductKey : recipe.ItemId;
        return _itemDefs.TryGetValue(key, out var d) ? d : null;
    }

    /// <summary>PickAnItem natif — élément aléatoire uniforme du set
    /// (random() % size → marche du rb-tree).</summary>
    public int PickItem(HashSet<int> pool)
    {
        var n = _rng.Next(pool.Count);
        foreach (var id in pool)
            if (n-- == 0) return id;
        return pool.First();
    }

    /// <summary>PickAColor natif @0x80a0460 : roll cumulé ×10000 sur la
    /// map qualité ; clé 0 → qualité d'un aux random ; clé 1 → 0 ;
    /// sinon la clé ; sans map → qualité de base de l'item (col19).</summary>
    public int PickQuality(ShopAndMiscParsers.ItemChestDef recipe,
        IReadOnlyList<int> auxQualities, int itemId)
    {
        var def = _itemDefs.TryGetValue(itemId, out var d) ? d : null;
        var fallback = def?.Quality ?? 0;
        if (recipe.QualityWeights.Count == 0) return fallback;
        var roll = _rng.Next(10000);
        var cum = 0f;
        foreach (var kv in recipe.QualityWeights.OrderBy(k => k.Key))
        {
            // Poids en % ×100 → échelle du roll natif (random()%10000
            // vs cumul DAT_087cc680×valeur).
            cum += kv.Value * 100f;
            if (roll < (int)cum)
            {
                if (kv.Key == 0)
                    return auxQualities.Count > 0
                        ? auxQualities[_rng.Next(auxQualities.Count)]
                        : fallback;
                return kv.Key == 1 ? 0 : kv.Key;
            }
        }
        return fallback;
    }

    /// <summary>Jet de réussite natif : dice = rand(100) &lt;
    /// taux(+0x28) + bonus du cube chanceux — sinon échec (435
    /// Field2=0).</summary>
    public bool RollSuccess(ShopAndMiscParsers.ItemChestDef recipe,
        int luckyBonus) =>
        _rng.Next(100) < recipe.SuccessRate + luckyBonus + ServerConfig.CraftSuccessBonusPct;

    /// <summary>Répartition native des produits stackables : `amount`
    /// découpé en ≤min(amount,3,poolSize) stacks — chaque stack sauf le
    /// dernier prend rand(restant)+1, le dernier prend le reste
    /// (désasm boucle uVar30/uVar24 @0x80be6xx).</summary>
    public List<int> SplitStackAmount(int amount, int poolSize)
    {
        var parts = new List<int>();
        var pieces = Math.Min(Math.Min(amount, 3), poolSize);
        var left = amount;
        for (var i = 0; i < pieces && left > 0; i++)
        {
            var take = i == pieces - 1 ? left : _rng.Next(left) + 1;
            left -= take;
            parts.Add(take);
        }
        return parts;
    }

    /// <summary>Résultat d'une production (CreateCubeItem natif) —
    /// pour AppendProdData (vecteurs sac/slot/qté du 572).</summary>
    public sealed record ProducedItem(int ItemId, int Qty, int Quality,
        bool Bound);    /// <summary>Détecte un cube chanceux : enchant d'usage (col23)
    /// portant la commande 8007 — params = {bonus%, levelCap}
    /// (désasm CChanceItem+0x13e/+0x140 = enchant params 1/2).</summary>
    public (int bonus, int maxLevel)? LuckyInfo(ItemParser.ItemDef? def)
    {
        if (def == null || def.UseEnchantId == 0) return null;
        var ench = _enchants.FirstOrDefault(e => e.Id == def.UseEnchantId);
        if (ench == null) return null;
        for (var i = 0; i + 2 < ench.TriggerParams.Length; i++)
            if (ench.TriggerParams[i] == 8007)
                return (ench.TriggerParams[i + 1], ench.TriggerParams[i + 2]);
        return null;
    }

    /// <summary>Recette par id (GameData+0x1b4 natif).</summary>
    public ShopAndMiscParsers.ItemChestDef? Recipe(int id) =>
        _recipes.TryGetValue(id, out var r) ? r : null;

    /// <summary>MaxStack d'un item (itemdata+0x76).</summary>
    public int MaxStack(int itemId) =>
        _itemDefs.TryGetValue(itemId, out var d) && d.MaxStack > 0
            ? d.MaxStack : 1;

    /// <summary>Définition item (itemdata natif).</summary>
    public ItemParser.ItemDef? ItemDef(int itemId) =>
        _itemDefs.TryGetValue(itemId, out var d) ? d : null;
}
