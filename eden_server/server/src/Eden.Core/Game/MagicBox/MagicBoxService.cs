using Eden.Core.Data.Parsers;

namespace Eden.Core.Game.MagicBox;

/// <summary>
/// Domaine boîte magique — portage de
/// `CItemFactory::{CheckMagicBoxCosts,DoMagicBox,
/// GetRandMagicBoxItemCombo,MagicBoxLevelUpGetCombo}`
/// (désasm zone @0x8088200 / @0x80a8b90 / @0x80a3eb0 / @0x80a8230).
///
/// Conversion (type 1) : les sacrifices d'équipement éligibles
/// accumulent de l'énergie ; au seuil `ConverEnergyAmount` le niveau
/// d'étoile monte (+1) et un combo est roulé aux stars 1/3/5.
/// Scouring (type 2) : reroll des combos sélectionnés au prix
/// d'équipements (nombre `ScouringNeedEquipAmount`) ou d'un catalyseur
/// « wild » d'item mall.
/// Catalyseur item mall : enchant commande **8022** (0x1f56) dont les
/// 6 arguments sont {énergie, %, nbStars, %, nbCombosScour, wild}.
/// </summary>
public sealed class MagicBoxService
{
    private readonly Dictionary<int, ItemParser.ItemDef> _itemDefs;
    private readonly IReadOnlyList<CraftRecipeParser.ItemCombo> _combos;
    private readonly ParameterParser.ParameterSet _params;
    private readonly Random _random;

    public MagicBoxService(IEnumerable<ItemParser.ItemDef> itemDefs,
        IReadOnlyList<CraftRecipeParser.ItemCombo> combos,
        ParameterParser.ParameterSet parameters, Random? random = null)
    {
        _itemDefs = itemDefs.ToDictionary(i => i.Id);
        _combos = combos;
        _params = parameters;
        _random = random ?? Random.Shared;
    }

    /// <summary>ItemDef par id — GameData::QueryItemData natif.</summary>
    public ItemParser.ItemDef? ItemDef(int itemId) =>
        _itemDefs.TryGetValue(itemId, out var d) ? d : null;

    /// <summary>Snapshot d'un item d'inventaire pour la validation —
    /// champs natifs lus par Check/Do (clone+0x2c qualité, +0x6c magic,
    /// +0x7c énergie, +0x7e star, +0x70..74 combos).</summary>
    public sealed record Sacrifice(int Uid, int ItemId, int Quality,
        int Magic, int ItemLevel);

    /// <summary>Arguments de l'enchant 8022 du catalyseur item mall —
    /// les 6 doubles natifs piVar16+{1,3,5,7,9,0xb} convertis en int
    /// (DoMagicBox @0x80a8b90).</summary>
    public sealed record MallParams(int EnergyAmount, int EnergyRate,
        int StarCount, int StarRate, int ScourComboCount, bool Wild)
    {
        /// <summary>Paire énergie renseignée (d0&gt;0 &amp;&amp; d1&gt;0).</summary>
        public bool HasEnergy => EnergyAmount > 0 && EnergyRate > 0;
        /// <summary>Paire star renseignée (d2&gt;0 &amp;&amp; d3&gt;0).</summary>
        public bool HasStar => StarCount > 0 && StarRate > 0;
        /// <summary>Pattern scouring à N combos (d4&gt;0 &amp;&amp; !wild).</summary>
        public bool HasScourCount => ScourComboCount > 0 && !Wild;
        /// <summary>Chaque paire est complètement renseignée ou vide
        /// et les deux ne le sont pas en même temps — patterns natifs
        /// valides en conversion (autre → return 0 dans
        /// CheckMagicBoxCosts).</summary>
        public bool ValidConvertPattern
        {
            get
            {
                var energySet = EnergyAmount > 0 || EnergyRate > 0;
                var starSet = StarCount > 0 || StarRate > 0;
                return !(energySet && starSet) &&
                       (!energySet || HasEnergy) &&
                       (!starSet || HasStar);
            }
        }
        /// <summary>Consommation natif en conversion : exactement une
        /// des deux paires renseignée (XOR natif DoMagicBox).</summary>
        public bool ConsumedOnConvert => HasEnergy != HasStar;
        /// <summary>Consommation natif en scouring :
        /// `(d4==0 && wild) || (d4!=0 && !wild)`.</summary>
        public bool ConsumedOnScour =>
            (ScourComboCount == 0 && Wild) ||
            (ScourComboCount != 0 && !Wild);
    }

    /// <summary>Résultat de validation (CheckMagicBoxCosts natif).</summary>
    public sealed record CheckResult(bool Ok, string? Error,
        int MainType = -1)
    {
        public static readonly CheckResult Fail = new(false, "fail");
    }

    /// <summary>Type natif de l'item principal — -1 si inéligible
    /// (flags hi col11 : bits 8/0x20/0x10).</summary>
    public int MainTypeOf(ItemParser.ItemDef def)
        => MagicBoxTables.MainType(def.FlagsHi);

    // ============================================================
    //  Validation — CheckMagicBoxCosts @0x8088200
    // ============================================================

    /// <summary>Vérifie qu'un sacrifice est accepté en conversion :
    /// bucket qualité dans `QualityNeed[type*5+star]` et taux non nul.
    /// Retourne le taux S_Parameter ou -1 (rejet natif).</summary>
    public float ConversionRate(int mainType, int starLevel,
        Sacrifice sac)
    {
        var bucket = MagicBoxTables.QualityBucket(sac.Quality, sac.Magic);
        var need = MagicBoxTables.QualityNeed[mainType * 5 + starLevel];
        if (need == 0 || (bucket & need) == 0) return -1;
        var slot = MagicBoxTables.RateSlot(bucket);
        if (slot < 0) return -1;
        var pid = MagicBoxTables.ConverEnergyRateParam
            [mainType * 5 + slot];
        return _params.Get(pid, 0f);
    }

    /// <summary>Vérifie qu'un sacrifice est accepté en scouring :
    /// bucket dans `ScouringQualityNeed[(star-1)+type*5]`.</summary>
    public bool IsValidScourSacrifice(int mainType, int starLevel,
        Sacrifice sac)
    {
        var bucket = MagicBoxTables.QualityBucket(sac.Quality, sac.Magic);
        var need = MagicBoxTables.ScouringQualityNeed
            [starLevel - 1 + mainType * 5];
        return need != 0 && (bucket & need) != 0;
    }

    /// <summary>Nombre d'équipements requis en scouring —
    /// `ScouringNeedEquipAmount[(star-1)+type*5]` (param float → int).</summary>
    public int ScourEquipAmount(int mainType, int starLevel)
        => (int)_params.Get(MagicBoxTables.ScouringNeedEquipAmountParam
            [starLevel - 1 + mainType * 5], 0f);

    /// <summary>Seuil d'énergie de conversion —
    /// `ConverEnergyAmount[type*5+star]`.</summary>
    public int EnergyThreshold(int mainType, int starLevel)
        => (int)_params.Get(MagicBoxTables.ConverEnergyAmountParam
            [mainType * 5 + starLevel], 0f);

    /// <summary>CheckMagicBoxCosts complet (hors lookup inventaire) —
    /// `main`/`def` déjà résolus, `sacs` les sacrifices résolus.</summary>
    public CheckResult CheckCosts(ushort type, ItemParser.ItemDef mainDef,
        int mainStarLevel, IReadOnlyList<Sacrifice> sacs,
        IReadOnlyList<short> selectedCombos, int[] mainCombos,
        MallParams? mall)
    {
        var mainType = MainTypeOf(mainDef);
        if (mainType < 0)
            return new(false, "OpFlag type not match");
        return type switch
        {
            1 => CheckConversion(mainType, mainStarLevel, sacs, mall),
            2 => CheckScouring(mainType, mainStarLevel, sacs,
                selectedCombos, mainCombos, mall),
            _ => new(false, "bad op type"),
        };
    }

    /// <summary>Branche conversion du Check natif : star ≤ 4, chaque
    /// sacrifice accepté, vecteurs taille égale (fait en amont).</summary>
    private CheckResult CheckConversion(int mainType, int starLevel,
        IReadOnlyList<Sacrifice> sacs, MallParams? mall)
    {
        if (starLevel > 4)
            return new(false, "star_level is out");
        if (mall != null && !mall.ValidConvertPattern)
            return new(false, "mall params pattern");
        foreach (var s in sacs)
            if (ConversionRate(mainType, starLevel, s) < 0)
                return new(false, "need quality error", mainType);
        return new(true, null, mainType);
    }

    /// <summary>Branche scouring du Check natif : star ∈ 1..5, au plus
    /// d4+1 (mall) ou 1 combo sélectionné, combos présents dans
    /// l'item, count d'équipements exact sauf wild.</summary>
    private CheckResult CheckScouring(int mainType, int starLevel,
        IReadOnlyList<Sacrifice> sacs, IReadOnlyList<short> selected,
        int[] mainCombos, MallParams? mall)
    {
        if (starLevel < 1 || starLevel > 5)
            return new(false, "starlv out range", mainType);
        if (selected.Count == 0)
            return new(false, "no select any combo", mainType);
        if (sacs.Count > 0 && (mall?.Wild ?? false))
            return new(false, "wild + sacrifices", mainType);
        var maxCombos = (mall?.ScourComboCount ?? 0) + 1;
        if (selected.Count > maxCombos)
            return new(false, "too many combos", mainType);
        if (mall is { HasScourCount: true })
        {
            var existing = mainCombos.Count(c => c > 0);
            if (existing < mall.ScourComboCount)
                return new(false, "not enough combos", mainType);
            if (selected.Count > mall.ScourComboCount)
                return new(false, "combos over limit", mainType);
        }
        if (!mall?.Wild ?? true)
        {
            var need = ScourEquipAmount(mainType, starLevel);
            var valid = sacs.Count(s =>
                IsValidScourSacrifice(mainType, starLevel, s));
            if (valid != need)
                return new(false, "need equip amount", mainType);
        }
        return new(true, null, mainType);
    }

    // ============================================================
    //  Exécution — DoMagicBox @0x80a8b90
    // ============================================================

    /// <summary>Issue d'une conversion — mutations du clone natif
    /// (+0x7c énergie, +0x7e star, +0x70..74 combos).</summary>
    public sealed record ConvertOutcome(int Energy, int StarLevel,
        int[] Combos, bool MallConsumed, bool LevelledUp,
        bool ModifyInfoEmitted, bool Failed);

    /// <summary>Conversion native : somme des énergies des sacrifices
    /// validés (arme → `itemLv*rate+0.5`, sinon `rate+0.5`),
    /// bonus énergie mall (dice < d1 → +d0), seuil → star+1,
    /// combo roulé aux stars impairs puis boost star mall (dice < d3 →
    /// MagicBoxLevelUpGetCombo × d2).</summary>
    public ConvertOutcome Convert(ItemParser.ItemDef mainDef,
        int currentEnergy, int starLevel, int[] combos,
        IReadOnlyList<(Sacrifice Sac, float Rate)> validated,
        MallParams? mall)
    {
        var mainType = MainTypeOf(mainDef);
        var energy = currentEnergy;
        // Accumulation sacrifice (DoMagicBox : local_ae).
        foreach (var (sac, rate) in validated)
            energy += mainType == 0
                ? (int)(sac.ItemLevel * rate + 0.5f)
                : (int)(rate + 0.5f);
        // Bonus énergie mall : dice < d1 → +d0.
        if (mall?.HasEnergy == true && _random.Next(100) < mall.EnergyRate)
            energy += mall.EnergyAmount;
        var modifyEmitted = false;
        var newStar = starLevel;
        if (energy < EnergyThreshold(mainType, newStar))
        {
            // Sous le seuil : l'énergie s'accumule (clone+0x7c).
        }
        else
        {
            newStar++;
            energy = 0;
            if (newStar is 1 or 3 or 5)
            {
                var combo = PickComboId(mainDef.Kind,
                    mainDef.RequiredLevel, newStar,
                    MagicBoxTables.AttrTypeOf(mainType));
                if (combo < 1)
                    return new(0, starLevel, combos, false, false,
                        false, true); // GetRandMagicBoxItemCombo error → cancel
                var slot = Array.FindIndex(combos, c => c <= 0);
                if (slot >= 0) combos[slot] = combo;
                modifyEmitted = true; // 369 natif après UpdateCloneItemAddAttr
            }
        }
        // Boost star mall : dice < d3 → d2 level-ups (combos aux impairs).
        if (mall?.HasStar == true
            && _random.Next(100) < mall.StarRate)
        {
            var ups = mall.StarCount;
            while (ups-- > 0 && newStar < 5)
            {
                newStar++;
                if (newStar is 1 or 3 or 5)
                {
                    var combo = PickComboId(mainDef.Kind,
                        mainDef.RequiredLevel, newStar,
                        MagicBoxTables.AttrTypeOf(mainType));
                    if (combo > 0)
                    {
                        var slot = Array.FindIndex(combos, c => c <= 0);
                        if (slot >= 0) combos[slot] = combo;
                    }
                }
            }
            modifyEmitted = true; // MagicBoxLevelUpGetCombo émet 369
        }
        return new(energy, newStar, combos,
            mall?.ConsumedOnConvert ?? false, newStar != starLevel,
            modifyEmitted, false);
    }

    /// <summary>Issue d'un scouring — map slot→nouveau combo.</summary>
    public sealed record ScourOutcome(IReadOnlyDictionary<int, int> NewCombos,
        bool MallConsumed);

    /// <summary>Scouring natif : chaque combo sélectionné (id &gt; 0
    /// présent dans le vecteur 3 combos) est rerollé avec
    /// `level = min(oldLevel+1, star)` — même kind/attrType
    /// (map Rb_tree&lt;slot,comboId&gt; natif puis écriture).</summary>
    public ScourOutcome Scour(ItemParser.ItemDef mainDef, int starLevel,
        int[] combos, IReadOnlyList<short> selected, MallParams? mall)
    {
        var mainType = MainTypeOf(mainDef);
        var result = new Dictionary<int, int>();
        foreach (var comboId in selected)
        {
            if (comboId <= 0) continue;
            var slot = Array.IndexOf(combos, (int)comboId);
            if (slot < 0 || result.ContainsKey(slot)) continue;
            var old = _combos.FirstOrDefault(c => c.Id == comboId);
            var level = Math.Min((old?.Tier ?? 0) + 1, starLevel);
            var newId = PickComboId(mainDef.Kind, mainDef.RequiredLevel,
                level, MagicBoxTables.AttrTypeOf(mainType));
            if (newId < 1) return new(result, false); // échec natif
            result[slot] = newId;
            combos[slot] = newId;
        }
        return new(result, mall?.ConsumedOnScour ?? false);
    }

    /// <summary>`GetRandMagicBoxItemCombo` @0x80a3eb0 — itère tous les
    /// combos : `attrType == attr` (&gt;3), kind dans la liste,
    /// `minLv ≤ reqLv ≤ maxLv`, `level == star`, poids cumulatif puis
    /// `random % total`. 0 si aucun candidat.</summary>
    public int PickComboId(int kind, int reqLevel, int star, int attrType)
    {
        var total = 0;
        var pool = new List<(int Id, int Cum)>();
        foreach (var c in _combos)
        {
            if (c.AttrType != attrType || c.AttrType <= 3) continue;
            if (!c.ApplicableItemTypes.Contains(kind)) continue;
            if (reqLevel < c.MinLevel || reqLevel > c.MaxLevel) continue;
            if (c.Tier != star) continue;
            total += c.Value;
            pool.Add((c.Id, total));
        }
        if (total <= 0) return 0;
        var roll = _random.Next(total);
        foreach (var (id, cum) in pool)
            if (roll < cum) return id;
        return pool[^1].Id;
    }
}
