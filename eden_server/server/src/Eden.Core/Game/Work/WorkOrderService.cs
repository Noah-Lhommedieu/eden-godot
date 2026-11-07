using Eden.Core.Data.Parsers;
using Eden.Core.Game.Inventory;
using Eden.Core.Game.Inlay;
using Eden.Core.Game.Services;

namespace Eden.Core.Game.Work;

/// <summary>
/// Logique des ordres de travail (métiers 12-17) — portage de
/// <c>CWorkManager::ToWork / CheckToCollect / CheckToCombine /
/// EndCollect / EndCombine / WorkResultOK</c>
/// (zone/decompiled.c). Aucun I/O : les validations et les tirages
/// sont purs, le flux zone orchestre consommation/persistance/
/// paquets.
///
/// Modèle natif (CCollectData=S_FamilyCollect, CCombineData=
/// S_FamilyCombine) :
/// - durée en unités de 100 ms (enchant) : non-instant →
///   col_durée(min) × 600 ; instant (et type 14 summon) → valeur brute.
/// - instant ⇒ consommation DÉFÉRÉE à la fin (catalyseur, matériaux,
///   booster, or recalculé) ; sinon consommation au démarrage.
/// - échec ⇒ <c>SaveWork(-workId,0)</c> : work_id négatif, produits
///   vides — récupérable au WorkResultOK.
/// - produits collect : tirage indépendant par entrée
///   {id, qté, taux} ; succès si rand%100 &lt;
///   (100 - clamp(100-rate-adjusté)) + ext_success où
///   rate' = rate - rate×decline/100.
/// - produit combine : un seul SWorkItem, sockets roulées sur le def
///   produit (ChangeEmbeddedAmount), malédiction par
///   rand()%1e8+1 ≤ curse_rate (toujours 0 en données), combos par
///   <see cref="ItemComboRollService"/> (SelectWorkItemCombo —
///   équipement kind 1..0x22 ≠0x10, qualité 0).
///
/// Déviations documentées (hors lot) : DiceItemCombo des drops non
/// porté (qualité d'instance non modélisée), événements work_event,
/// famille/bâtiments (fm_*, building_*) et EndSummon spécialisé du
/// type 14.
/// </summary>
public sealed class WorkOrderService
{
    /// <summary>Textes natifs (TextOutPut) émis par ToWork/CheckTo*.</summary>
    public static class Texts
    {
        /// <summary>Item source absent/invalide (ToWork : catalyseur
        /// introuvable → SendTextOutPut 4).</summary>
        public const int InvalidItem = 4;
        /// <summary>Aucun slot de travail libre (0x5a « All work
        /// slots are in use »).</summary>
        public const int NoSlot = 0x5a;
        /// <summary>Niveau du catalyseur hors de la plage joueur
        /// (0xb76).</summary>
        public const int BadLevel = 0xb76;
        /// <summary>Opération/instantané invalide (0x251).</summary>
        public const int BadOperation = 0x251;
        /// <summary>Kind catalyseur ou recette mismatch (0x249).</summary>
        public const int BadCatalyst = 0x249;
        /// <summary>Quantité d'ingrédients insuffisante (0x352).</summary>
        public const int NotEnoughItems = 0x352;
        /// <summary>Pas assez de slots d'inventaire (0x4f9).</summary>
        public const int NoInventorySpace = 0x4f9;
        /// <summary>Or insuffisant — texte 562 « You don't have
        /// enough money! » (T_TextIndex).</summary>
        public const int NotEnoughGold = 562;
    }

    private readonly GameDataService _data;
    private readonly InlayService _inlay;
    private readonly Random _rand;

    /// <summary>Service d'ordres — tables chargées par
    /// GameDataService + roller de sockets partagé avec l'inlay.</summary>
    public WorkOrderService(GameDataService data, InlayService inlay,
        Random? rand = null)
    {
        _data = data;
        _inlay = inlay;
        _rand = rand ?? new Random();
    }

    /// <summary>Unités de durée natives : 1 unité d'enchant = 100 ms
    /// (S_Enchant — même base que LifeSkillCastID=211).</summary>
    public const int UnitMs = 100;

    /// <summary>Multiplicateur natif des durées non-instantanées :
    /// col durée × 600 unités (= minutes).</summary>
    public const int DurationFactor = 600;

    /// <summary>Enchant id du slot = 200 + work_num — les enchants
    /// 200-210 « PlayerWork N » sont synthétisés au runtime natif
    /// (CWorkManager::Init). work_event le stocke.</summary>
    public static int EventIdFor(int workNum) => 200 + workNum;

    /// <summary>
    /// Plan de démarrage validé — produit par
    /// <see cref="ValidateStart"/> après les checks natifs
    /// (CheckToCollect/CheckToCombine).
    /// </summary>
    public sealed class StartPlan
    {
        /// <summary>Collecte (types 12-14) vs combinaison (15-17).</summary>
        public bool IsCollect;
        /// <summary>Durée en unités de 100 ms (enchant natif).</summary>
        public int DurationUnits;
        /// <summary>433 StrengthenCasting à émettre — natif : toujours
        /// pour les combines, seulement instant/type-14 pour les
        /// collectes.</summary>
        public bool CastBar;
        /// <summary>Consommation différée à la fin (instant natif —
        /// et type 14 summon chez nous : EndSummon consomme son
        /// catalyseur à la fin comme EndCollect instantané).</summary>
        public bool Deferred;
        /// <summary>Quantité de catalyseur à consommer — collect :
        /// col4 ; combine : la ligne matériau du catalyseur.</summary>
        public int CatalystQty;
        /// <summary>Matériaux à consommer hors catalyseur (combine).</summary>
        public List<(int ItemId, int Qty)> Materials { get; } = new();
        /// <summary>Coût or de la recette (avec taxe, 0 pour NPC).</summary>
        public int GoldCost;
        /// <summary>ext_success_rate du booster (0x1f45/0x1f47/0x1f48).</summary>
        public int ExtSuccessRate;
        /// <summary>ext_building_rate du booster (0x1f44/0x1f48).</summary>
        public int ExtBuildingRate;
        /// <summary>Recettes résolues (l'une des deux).</summary>
        public WorkOrderParser.WorkCollectDef? Collect;
        public WorkOrderParser.WorkCombineDef? Combine;
    }

    /// <summary>Erreur de validation — textId natif à envoyer par
    /// ClientTextOutputIndex.</summary>
    public readonly record struct WorkError(int TextId)
    {
        public bool Ok => TextId == 0;
    }

    /// <summary>
    /// Validation de démarrage — CWorkManager::ToWork natif :
    /// slot libre (0-9, slot 10 en secours), catalyseur à (bag,slot),
    /// kind par type, niveau dans [RequiredLevel, MaxLevel], checks
    /// par catégorie puis calcul de durée.
    /// </summary>
    public WorkError ValidateStart(int workType, int workId,
        WorkSlot[] works, ItemEntity? catalyst, int playerLevel,
        out StartPlan? plan, out int workNum)
    {
        plan = null;
        workNum = FindFreeSlot(workType, works);
        if (!WorkTypes.IsValid(workType))
            return new(Texts.BadOperation);
        if (workNum < 0)
            return new(Texts.NoSlot);
        if (catalyst == null)
            return new(Texts.InvalidItem);
        var catDef = _data.Items.FirstOrDefault(i => i.Id == catalyst.ItemId);
        if (catDef == null)
            return new(Texts.InvalidItem);
        // Plage de niveau du catalyseur — ToWork lit itemdata+0xfc :
        // min = S_Item col14 (RequiredLevel), max = col15 (MaxLevel).
        if (catDef.RequiredLevel > playerLevel
            || (catDef.MaxLevel > 0 && catDef.MaxLevel < playerLevel))
            return new(Texts.BadLevel);
        // Kind du catalyseur par type (CheckToCollect/CheckToCombine).
        if (catDef.Kind != WorkTypes.RequiredCatalystKind(workType))
            return new(Texts.BadCatalyst);
        var err = WorkTypes.IsCollect(workType)
            ? ValidateCollect(workId, workType, catalyst, out plan)
            : ValidateCombine(workId, catalyst, out plan);
        return err;
    }

    /// <summary>Slot libre — ToWork natif : premier slot avec le flag
    /// « running » (work+0x36) à 0 parmi 0-9 ; le slot 10 n'est pris
    /// que si le compte de collectes type 12/13 &lt; 10 pour ces types
    /// (limite native « 10 collect works »).</summary>
    public static int FindFreeSlot(int workType, WorkSlot[] works)
    {
        for (var i = 0; i <= 9 && i < works.Length; i++)
            if (!works[i].Running && works[i].WorkId == 0)
                return i;
        // Slot 10 natif : buffer supplémentaire ; la limite « pas plus
        // de 10 ordres de collecte 12/13 » est répliquée.
        if (works.Length > 10 && !works[10].Running
            && works[10].WorkId == 0
            && (workType is not (WorkTypes.Mining or WorkTypes.Planting)
                || works.Count(w => w.Running
                    && w.WorkType is WorkTypes.Mining or WorkTypes.Planting) < 10))
            return 10;
        return -1;
    }

    /// <summary>CheckToCollect natif : recette présente, catalyseur =
    /// col2 (0x249 sinon) avec qté col4 (0x352), durée et plan.</summary>
    private WorkError ValidateCollect(int workId, int workType,
        ItemEntity catalyst, out StartPlan? plan)
    {
        plan = null;
        if (!_data.WorkCollectById.TryGetValue(workId, out var def)
            || def == null)
            return new(Texts.InvalidItem);
        if (catalyst.ItemId != def.CatalystItemId)
            return new(Texts.BadCatalyst);
        if (catalyst.Quantity < Math.Max(def.CatalystAmount, 1))
            return new(Texts.NotEnoughItems);
        // Type 14 summon : durée brute + cast-bar natif (param_6=0xe
        // force la branche instantanée de ToCollect, et la
        // consommation est différée comme l'instant — EndSummon
        // consomme à la fin ; ici EndCollect générique).
        var rawDuration = def.Instant || workType == WorkTypes.Summon;
        plan = new StartPlan
        {
            IsCollect = true,
            Collect = def,
            CatalystQty = Math.Max(def.CatalystAmount, 1),
            GoldCost = def.Gold,
            Deferred = rawDuration,
            CastBar = rawDuration,
            DurationUnits = rawDuration
                ? def.Duration : def.Duration * DurationFactor,
        };
        return new(0);
    }

    /// <summary>CheckToCombine natif : recette présente, catalyseur
    /// listé dans les matériaux, durée et plan.</summary>
    private WorkError ValidateCombine(int workId, ItemEntity catalyst,
        out StartPlan? plan)
    {
        plan = null;
        if (!_data.WorkCombineById.TryGetValue(workId, out var def)
            || def == null || def.Materials.Count == 0)
            return new(Texts.InvalidItem);
        // Le parchemin de recette fait partie des matériaux natifs —
        // sa ligne impose la qté du catalyseur.
        var catMat = def.Materials.FirstOrDefault(
            m => m.ItemId == catalyst.ItemId);
        if (catMat == null)
            return new(Texts.BadCatalyst);
        if (catalyst.Quantity < Math.Max(catMat.Quantity, 1))
            return new(Texts.NotEnoughItems);
        plan = new StartPlan
        {
            IsCollect = false,
            Combine = def,
            CatalystQty = Math.Max(catMat.Quantity, 1),
            GoldCost = def.Gold,
            Deferred = def.Instant,
            CastBar = true, // ToCombine émet 433 dans tous les cas
            DurationUnits = def.Instant
                ? def.Duration : def.Duration * DurationFactor,
        };
        foreach (var m in def.Materials)
            if (m.ItemId != catalyst.ItemId)
                plan.Materials.Add((m.ItemId, Math.Max(m.Quantity, 1)));
        return new(0);
    }

    /// <summary>
    /// CheckItemNum natif — les matériaux sont cherchés dans tous les
    /// sacs (agrégat par item_id). itemCounts = somme des quantités
    /// par item_id sur l'inventaire.
    /// </summary>
    public bool HasMaterials(StartPlan plan,
        IReadOnlyDictionary<int, long> itemCounts)
    {
        if (plan.IsCollect) return true;
        foreach (var (id, qty) in plan.Materials)
            if (!itemCounts.TryGetValue(id, out var have) || have < qty)
                return false;
        return true;
    }

    /// <summary>
    /// Tirage EndCollect natif — un jet indépendant par entrée
    /// produit ; qté = qté recette + bouns_item_amount.
    /// </summary>
    public void RollCollectProducts(WorkOrderParser.WorkCollectDef def,
        WorkSlot work)
    {
        foreach (var p in def.Products)
        {
            // rate' = 100 - rate ; rate'' = rate' - rate'×decline/100 ;
            // succès si rand%100 < (100 - max(0,rate'')) + ext_success.
            var failAdj = 100 - p.Rate;
            failAdj -= (int)(work.FailureRateOfDecline * 0.01f * failAdj);
            var fail = (int)Math.Max(0, failAdj);
            if (_rand.Next(100) >= (100 - fail) + work.ExtSuccessRate
                + ServerConfig.CraftSuccessBonusPct)
                continue;
            var qty = (short)(work.BonusItemAmount + p.Quantity);
            if (qty > 0) AddWorkItem(work, p.ItemId, qty);
        }
    }

    /// <summary>
    /// Tirage EndCombine natif — un seul produit :
    /// succès si rand%100 < (100 - max(0, (100-rate) ajusté
    /// decline)) + ext_success ; malédiction par curse_rate sur 1e8 ;
    /// sockets roulées par ChangeEmbeddedAmount sur le def produit ;
    /// qté = qté recette + bonus.
    /// </summary>
    public void RollCombineProduct(WorkOrderParser.WorkCombineDef def,
        WorkSlot work)
    {
        var failAdj = 100f - def.SuccessRate;
        failAdj -= work.FailureRateOfDecline * 0.01f * failAdj;
        var fail = (int)Math.Max(0, (int)failAdj);
        if (_rand.Next(100) >= (100 - fail) + work.ExtSuccessRate
                + ServerConfig.CraftSuccessBonusPct)
            return;
        var qty = (short)(work.BonusItemAmount + def.ProductQuantity);
        if (qty <= 0) return;
        var p = AddWorkItem(work, def.ProductItemId, qty);
        if (p == null) return;
        // Malédiction — combine+0x3c sur 1e8 (toujours 0 en données).
        p.Curse = def.CurseRate > 0
            && _rand.Next(100_000_000) + 1 <= def.CurseRate;
        // Sockets du produit — EndCombine arme le même roll que la
        // création d'item (ChangeEmbeddedAmount via la durabilité
        // de l'itemdef).
        var def2 = _data.Items.FirstOrDefault(i => i.Id == def.ProductItemId);
        if (def2 != null)
        {
            var (count, _) = _inlay.RollSockets(def2);
            p.SocketAmount = (short)count;
            // Combos — SelectWorkItemCombo (EndCombine @0x081657b5) :
            // équipement seulement (kind 1..0x22, ≠0x10), qualité 0.
            if (ItemComboRollService.IsWorkComboEligible(def2.Kind))
            {
                var (main, extras) = ItemComboRollService.Roll(
                    0, def2.Kind, def2.RequiredLevel, _data.ItemCombos,
                    next: _rand.Next);
                p.Combo = main;
                for (var i = 0; i < p.ExtraCombos.Length && i < extras.Length; i++)
                    p.ExtraCombos[i] = extras[i];
            }
        }
    }

    /// <summary>Place un produit dans le premier SWorkItem libre —
    /// CWork::AddWrokItem natif (max 4, les excédents sont perdus).</summary>
    private static WorkProduct? AddWorkItem(WorkSlot work, int itemId,
        int qty)
    {
        var slot = work.Products.FirstOrDefault(p => p.IsEmpty);
        if (slot == null) return null;
        slot.ItemId = itemId;
        slot.Amount = qty;
        return slot;
    }

    /// <summary>
    /// Slots d'inventaire requis pour la remise — WorkResultOK natif :
    /// produit stackable (S_Item col29 MaxStack ≥ 2) → +1 seulement si
    /// aucun stack partiel ne peut l'absorber ; non-stackable → +1
    /// par SWorkItem (le natif ne compte PAS qty slots même si
    /// plusieurs copies sont créées — fidélité, bug compris).
    /// </summary>
    public int SlotsNeededFor(WorkSlot work,
        IReadOnlyList<ItemEntity> items)
    {
        var needed = 0;
        foreach (var p in work.Products)
        {
            if (p.IsEmpty) continue;
            var def = _data.Items.FirstOrDefault(i => i.Id == p.ItemId);
            var maxStack = def?.MaxStack ?? 1;
            if (maxStack < 2) { needed++; continue; }
            var mergeable = items.Any(i => i.ItemId == p.ItemId
                && !i.IsEquipped
                && i.Quantity + p.Amount <= maxStack);
            if (!mergeable) needed++;
        }
        return needed;
    }
}
