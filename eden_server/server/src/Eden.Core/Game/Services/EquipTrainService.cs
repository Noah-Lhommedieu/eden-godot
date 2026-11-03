using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Game.Inventory;

namespace Eden.Core.Game.Services;

/// <summary>
/// Mana Infusion (entraînement d'équipement) — miroir des chemins
/// natifs <c>CPlayerFactory::SetEquipTrainItem</c> @0x80ee6b0,
/// <c>CItemFactory::SetItemTrainType</c> @0x8099940,
/// <c>CCharacter::FindEquipTrainItemLocation</c> @0x840b500,
/// <c>SetEquipTrainItemID</c> @0x840b670 et
/// <c>CCharacter::GainEquipExp</c> @0x841b1d0 (zone/decompiled.c).
///
/// Modèle de conteneurs natif :
/// <list type="bullet">
///   <item>type 0 = arbre +0x4e4 — conteneurs <c>CInventory</c>
///         (sacs 0-4, GetType()=0) ;</item>
///   <item>type 1 = arbre +0x4fc — conteneurs <c>CEquipment</c>
///         (groupes d'équipement, GetType()=1) ;</item>
///   <item>clé = <c>GetContainerIndex()</c> (container_index DB) ;</item>
///   <item>index = loc (vecteurs dimensionnés à la capacité, slots à
///         trous — <c>GetEmptySlot</c> les parcourt).</item>
/// </list>
///
/// Le NCID 230 sélectionne l'item ([s16 container][s16 index]
/// [s16 key][u16 n][u8 types×n]) ; la localisation courante est
/// renvoyée par le NCID 570 `[s16 type][s16 index][s16 key]`
/// (−1,−1,−1 sans sélection). L'EXP d'entraînement =
/// <c>charExp × S_Parameter 496</c> au kill (<c>GiveTeamExp</c>
/// @0x849d190).
/// </summary>
public sealed class EquipTrainService
{
    private readonly IInventoryRepository _repo;
    private readonly GameDataService _data;
    private readonly Random _rng = new();

    /// <summary>Dernière localisation émise par le 570 par perso —
    /// miroir de <c>char+0xd58/+0xd5c/+0xd5e</c> natifs : le paquet
    /// n'est envoyé que si la position recalculée diffère.</summary>
    private readonly
        System.Collections.Concurrent.ConcurrentDictionary<int,
            TrainLocation> _lastLoc = new();

    /// <summary>Enregistre et retourne la localisation précédente —
    /// le flux émet le 570 si <c>prev != loc</c> (comportement natif
    /// de SetEquipTrainItemID).</summary>
    public TrainLocation SwapLastLocation(int characterId,
        TrainLocation loc)
        => _lastLoc.AddOrUpdate(characterId, loc, (_, _) => loc);

    /// <summary>Types d'attributs du 230 : -1 = désélection, 0 = sacs,
    /// 1 = équipement (arbres +0x4e4/+0x4fc natifs).</summary>
    public const int ContainerBags = 0, ContainerEquipped = 1;

    /// <summary>Position résolue d'un item entraîné — le triplet émis
    /// dans le 570 <c>CNC_CZ_ClientEquipTrainItemChanged</c>
    /// (ordre natif : type, index, key).</summary>
    public readonly record struct TrainLocation(short Type,
        short Index, short Key)
    {
        /// <summary>Aucune sélection — le natif émet (−1,−1,−1).</summary>
        public static readonly TrainLocation None = new(-1, -1, -1);
    }

    /// <summary>Résultat d'un 230 — porte tout ce que le flux doit
    /// émettre (570 + 369 éventuel).</summary>
    public sealed class SetItemOutcome
    {
        /// <summary>Requête acceptée — l'état serveur a changé.</summary>
        public bool Ok;
        /// <summary>Localisation recalculée à émettre si différente de
        /// la précédente (garde « emit on change » natif).</summary>
        public TrainLocation Loc = TrainLocation.None;
        /// <summary>Item concerné par un 369 d'initialisation —
        /// non null seulement quand <see cref="InitDone"/>.</summary>
        public ItemEntity? Item;
        /// <summary>GetType() natif du conteneur de l'item (0=CInventory,
        /// 1=CEquipment) — champ u16 du header 369.</summary>
        public short ContainerType;
        /// <summary>Clé de conteneur (container_index) — s16 du 369.</summary>
        public short ContainerKey;
        /// <summary>Index (loc) dans le conteneur — u16 du 369.</summary>
        public short VecIndex;
        /// <summary>Les types ont été initialisés par
        /// <c>SetItemTrainType</c> — le natif émet alors un 369 complet
        /// + recalcul des stats.</summary>
        public bool InitDone;
    }

    /// <summary>Résultat d'un gain d'EXP d'entraînement.</summary>
    public sealed class GainOutcome
    {
        /// <summary>Un changement a été persisté (exp ou level-up).</summary>
        public bool Changed;
        /// <summary>EXP affichée par le texte 5494 (gain moins
        /// reliquat non crédité à la borne max — natif).</summary>
        public long CreditedExp;
        /// <summary>Niveau d'entraînement final.</summary>
        public int Level;
        /// <summary>Niveau AVANT le gain — le natif émet le texte
        /// 5495 pour CHAQUE palier franchi (LevelBefore+1..Level).</summary>
        public int LevelBefore;
        /// <summary>Au moins un palier franchi → texte 5495 +
        /// recalcul des stats natif.</summary>
        public bool Leveled;
        /// <summary>Item muté — nécessaire au 369 final natif.</summary>
        public ItemEntity? Item;
        /// <summary>GetType() du conteneur (u16 header 369).</summary>
        public short ContainerType = 1;
        /// <summary>Clé conteneur / index (header 369).</summary>
        public short ContainerKey, VecIndex;
    }

    public EquipTrainService(IInventoryRepository repo,
        GameDataService data)
    {
        _repo = repo;
        _data = data;
    }

    /// <summary>Multiplicateur d'emplacement natif — switch sur
    /// <c>itemdata+0x28</c> (EquipLoc col9) de
    /// <c>UpdateCloneItemAddAttr</c> @0x8097d00 : 7/8/0x13 → S_Parameter
    /// 497 (1H, ×1.3) ; 9/0x23 → 498 (2H, ×2.6) ; locs d'armure 1-5 →
    /// 499-503 ; tout autre emplacement → 0 natif.</summary>
    public float LocMultiplier(int equipLoc) => equipLoc switch
    {
        7 or 8 or 0x13 => _data.Parameters.Get(497, 1.3f),
        9 or 0x23 => _data.Parameters.Get(498, 2.6f),
        1 => _data.Parameters.Get(499, 1f),
        2 => _data.Parameters.Get(500, 1.2f),
        3 => _data.Parameters.Get(501, 0.8f),
        4 => _data.Parameters.Get(502, 1f),
        5 => _data.Parameters.Get(503, 1f),
        _ => 0f,
    };

    /// <summary>Ratio EXP → EXP d'entraînement — S_Parameter 496
    /// (<c>GiveTeamExp</c> : <c>gearExp = charExp × para496</c>,
    /// chargé en static au premier kill).</summary>
    public int GearExpFor(long charExp)
        => (int)(charExp * _data.Parameters.Get(496, 0.2f));

    /// <summary>Contribution d'entraînement d'UN item équipé vers un
    /// type — <c>slotLevel × mult(EquipLoc) × S_EquipTrain[itemLevel].
    /// coef[type]</c> (UpdateCloneItemAddAttr : la ligne est indexée
    /// par itemdata+0x3e = required level, pas par train_level).</summary>
    public float SlotContribution(ItemParser.ItemDef def, int type,
        int slotLevel)
    {
        if (slotLevel <= 0 || type < 0 ||
            type >= EquipTrainParser.AttrTypeCount ||
            !_data.EquipTrain.TryGetValue(def.RequiredLevel, out var row))
            return 0f;
        return slotLevel * LocMultiplier(def.EquipLoc) * row.Coefs[type];
    }

    /// <summary>Somme des contributions des 3 slots d'un item pour
    /// un type donné.</summary>
    public float ItemContribution(ItemEntity item,
        ItemParser.ItemDef def, int type)
        => SlotContribution(def, type, item.TrainSlot1Level)
            + (item.TrainSlot2Type == type
                ? SlotContribution(def, type, item.TrainSlot2Level) : 0f)
            + (item.TrainSlot3Type == type
                ? SlotContribution(def, type, item.TrainSlot3Level) : 0f);

    /// <summary>Valeurs agrégées des 15 canaux pour un item —
    /// tableau indexé par type (0=HP … 14=P-Healing).</summary>
    public int[] ItemTrainAdds(ItemEntity item, ItemParser.ItemDef def)
    {
        var adds = new int[EquipTrainParser.AttrTypeCount];
        if (item.TrainLevel <= 0) return adds;
        for (var i = 0; i < 3; i++)
        {
            var (t, l) = i switch
            {
                0 => (item.TrainSlot1Type, item.TrainSlot1Level),
                1 => (item.TrainSlot2Type, item.TrainSlot2Level),
                _ => (item.TrainSlot3Type, item.TrainSlot3Level),
            };
            if (t is >= 0 and < EquipTrainParser.AttrTypeCount && l > 0)
                adds[t] += (int)SlotContribution(def, t, l);
        }
        return adds;
    }

    /// <summary>Localisation courante de l'item sélectionné —
    /// <c>FindEquipTrainItemLocation</c> natif (équipé d'abord, puis
    /// sacs). Retourne l'entité + le triplet 570 ; null si le serial
    /// n'est plus porté.</summary>
    public async Task<(ItemEntity Item, TrainLocation Loc)?>
        FindLocationAsync(int characterId, int serial)
    {
        var hit = await _repo.FindItemBySerialAsync(characterId, serial);
        return hit == null ? null
            : (hit.Value.Item, new TrainLocation(
                (short)hit.Value.ContainerType,
                (short)hit.Value.VectorIndex,
                (short)hit.Value.ContainerKey));
    }

    /// <summary>Résout l'item visé par un 230 — conteneur natif
    /// (0=sacs, 1=équipé), key = container_index, index = loc.</summary>
    public Task<ItemEntity?> ResolveItemAsync(int characterId,
        short container, short key, short index)
        => container == ContainerEquipped
            ? _repo.GetEquipmentItemAsync(characterId, key, index)
            : _repo.GetItemAsync(characterId, key, index);

    /// <summary>Validation des types demandés —
    /// <c>SetEquipTrainItem</c> natif : chaque type ≤ 14, bit
    /// <c>1&lt;&lt;type</c> présent dans <c>itemdata+0x2c</c>
    /// (S_Item col10), aucun doublon.</summary>
    public static bool ValidateTypes(ItemParser.ItemDef def,
        IReadOnlyList<byte> types)
    {
        var seen = 0;
        foreach (var t in types)
        {
            if (t > 0xe) return false;
            var bit = 1 << t;
            if ((def.TrainMask & bit & 0xffff) == 0) return false;
            if ((seen & bit) != 0) return false;
            seen |= bit;
        }
        return true;
    }

    /// <summary>Éligibilité de l'item — <c>-1 &lt; *(char*)(itemdata+
    /// 0x30) || clone+0x24&amp;0x10</c> : octet bas des flags &lt; 0x80
    /// ou item lié (bit bind posé par SetItemBind).</summary>
    public static bool IsEligible(ItemParser.ItemDef def, ItemEntity item)
        => (def.Flags & 0x80) == 0 || item.IsBound;

    /// <summary>Traite un NCID 230 — <c>SetEquipTrainItem</c> natif :
    /// <list type="bullet">
    ///   <item>container −1 → <c>SetEquipTrainItemID(0)</c> ;</item>
    ///   <item>0/1 → item résolu, validé (flags, types, doublons) ;</item>
    ///   <item>métadonnées vides → <c>SetItemTrainType</c> (types,
    ///     train_level=1, exp=0, persist, 369) ;</item>
    ///   <item>serial persisté + localisation recalculée.</item>
    /// </list></summary>
    public async Task<SetItemOutcome> SetItemAsync(int characterId,
        short container, short index, short key, byte[] types)
    {
        var outcome = new SetItemOutcome();
        // container −1 : désélection (SetEquipTrainItemID(0) natif).
        if (container == -1)
        {
            await _repo.SetEquipTrainItemIdAsync(characterId, 0);
            outcome.Ok = true;
            return outcome;
        }
        // Natif : seuls les arbres 0 (sacs) et 1 (équipé) sont lus.
        if (container is not (0 or 1)) return outcome;

        var item = await ResolveItemAsync(characterId, container,
            key, index);
        if (item == null) return outcome;
        var def = _data.Items.FirstOrDefault(d => d.Id == item.ItemId);
        if (def == null || !IsEligible(def, item) ||
            !ValidateTypes(def, types))
            return outcome;

        // Natif : initialisation seulement si les champs train sont
        // tous vides — train_level==0 OU (Σtypes==0 ET Σlevels==0).
        if (item.TrainLevel == 0 ||
            (item.TrainSlot1Type + item.TrainSlot2Type +
             item.TrainSlot3Type == 0 &&
             item.TrainSlot1Level + item.TrainSlot2Level +
             item.TrainSlot3Level == 0))
        {
            // SetItemTrainType natif exige exactement 3 types.
            if (types.Length != 3) return outcome;
            item.TrainLevel = 1;
            item.TrainExp = 0;
            item.TrainSlot1Type = types[0];
            item.TrainSlot2Type = types[1];
            item.TrainSlot3Type = types[2];
            item.TrainSlot1Level = item.TrainSlot2Level =
                item.TrainSlot3Level = 0;
            await _repo.UpdateItemTrainAsync(item.Id, item.TrainLevel,
                item.TrainExp, item.TrainSlot1Type,
                item.TrainSlot1Level, item.TrainSlot2Type,
                item.TrainSlot2Level, item.TrainSlot3Type,
                item.TrainSlot3Level);
            outcome.InitDone = true;
            outcome.Item = item;
            outcome.ContainerType = container;
            outcome.ContainerKey = key;
            outcome.VecIndex = index;
        }

        // SetEquipTrainItemID : persist serial + recalc localisation.
        await _repo.SetEquipTrainItemIdAsync(characterId, item.Id);
        var loc = await FindLocationAsync(characterId, item.Id);
        outcome.Ok = true;
        outcome.Loc = loc?.Loc ?? TrainLocation.None;
        return outcome;
    }

    /// <summary>Gain d'EXP d'entraînement au kill —
    /// <c>CCharacter::GainEquipExp</c> natif intégral :
    /// <list type="bullet">
    ///   <item>item sélectionné (eqtrain_item_id), équipé, serial
    ///         concordant, durabilité &gt; 0 ;</item>
    ///   <item>train_level non nul, &lt; itemdata+0x3e (niveau requis
    ///         = plafond), &lt; niveau du perso ; types/levels non
    ///         tous nuls ;</item>
    ///   <item>gate d'archétype sur locs d'armure 1-5 :
    ///         <c>kind % 3 == classArchetype % 3</c> (1→{1,4,7,10,13},
    ///         2→{2,5,8,11,14}, 3→{3,6,9,12,15} — désasm) ;</item>
    ///   <item>banque d'exp += gain ; paliers consommés au coût
    ///         <c>row[lv+1].exp</c>, slot pondéré params 504/505/506 ;
    ///   </item>
    ///   <item>borne : banque = seuil quand les caps bloquent
    ///         (natif <c>local_38 = local_34</c>) ; texte crédité =
    ///         gain − banque quand l'item est maxé, gain sinon.</item>
    /// </list></summary>
    /// <param name="archetype">CClassData+0x28 de la classe courante
    /// (1/2/3 — <see cref="ClassStatsParser.ClassDef.Archetype"/>).</param>
    public async Task<GainOutcome> GainEquipExpAsync(int characterId,
        int playerLevel, int archetype, int gearExp,
        CancellationToken token = default)
    {
        var o = new GainOutcome();
        if (gearExp <= 0) return o;
        var serial = await _repo.GetEquipTrainItemIdAsync(characterId);
        if (serial == 0) return o;
        var hit = await _repo.FindItemBySerialAsync(characterId, serial);
        // Natif : type==1 (équipé) exigé + serial concordant.
        if (hit == null || hit.Value.ContainerType != 1) return o;
        var item = hit.Value.Item;
        if (item.Id != serial || item.Durability <= 0) return o;
        var def = _data.Items.FirstOrDefault(d => d.Id == item.ItemId);
        if (def == null) return o;
        var level = item.TrainLevel;
        var levelBefore = level;
        // Plafond = niveau requis de l'item (itemdata+0x3e) + non nul
        // + métadonnées initialisées (Σtypes ou Σlevels ≠ 0).
        if (level == 0 || level >= def.RequiredLevel) return o;
        var hasMeta = item.TrainSlot1Type + item.TrainSlot2Type +
            item.TrainSlot3Type != 0 ||
            item.TrainSlot1Level + item.TrainSlot2Level +
            item.TrainSlot3Level != 0;
        if (!hasMeta) return o;
        var index = (short)hit.Value.VectorIndex;
        var key = (short)hit.Value.ContainerKey;
        // Gate natif : locs d'armure (0 < index < 6) → kind %3 ==
        // archetype %3 (les autres locs sont exemptes).
        if (index is > 0 and < 6 &&
            def.Kind % 3 != archetype % 3)
            return o;

        // --- banque d'exp + boucle de paliers (mir littéral) ---
        if (!_data.EquipTrain.TryGetValue(level + 1, out var row))
            return o;
        var bank = item.TrainExp + gearExp;
        var threshold = row.Exp;
        var leveled = false;
        if (bank >= threshold)
        {
            if (level < playerLevel && level < def.RequiredLevel)
            {
                do
                {
                    leveled = true;
                    // Texte 5495 émis par le flux (nouveau niveau
                    // = level+1 avant incrément — natif local_94).
                    var slot = PickSlot();
                    bank -= threshold;
                    level++;
                    IncrementSlot(item, slot);
                    if (!_data.EquipTrain.TryGetValue(level + 1,
                            out row))
                        goto Save;
                    threshold = row.Exp;
                    if (bank < threshold)
                        goto Save;
                } while (level < playerLevel &&
                         level < def.RequiredLevel);
                bank = threshold;      // caps : banque = seuil (natif)
            }
            else
            {
                bank = threshold;      // caps : idem, sans level-up
            }
        }
    Save:
        item.TrainLevel = level;
        // Natif : < maxLevel → banque persistée, local_38 remis à 0
        // (texte = gain entier) ; ≥ maxLevel → exp écrite 0.
        long displayed;
        if (level < def.RequiredLevel)
        {
            item.TrainExp = bank;
            displayed = gearExp;
        }
        else
        {
            item.TrainExp = 0;
            displayed = gearExp - bank;
        }
        await _repo.UpdateItemTrainAsync(item.Id, item.TrainLevel,
            item.TrainExp, item.TrainSlot1Type, item.TrainSlot1Level,
            item.TrainSlot2Type, item.TrainSlot2Level,
            item.TrainSlot3Type, item.TrainSlot3Level);
        // Natif : le record est re-émis (369) à CHAQUE crédit —
        // train_exp bouge même sans palier. `changed` ne pilote que
        // le refresh de stats (leveled) côté natif, pas le 369.
        o.Changed = true;
        o.CreditedExp = Math.Max(displayed, 0);
        o.Level = level;
        o.LevelBefore = levelBefore;
        o.Leveled = leveled;
        o.Item = item;
        o.ContainerType = 1;
        o.ContainerKey = key;
        o.VecIndex = index;
        return o;
    }

    /// <summary>Tirage pondéré du slot à monter — params 504/505/506
    /// (0.5/0.3/0.2 ; <c>random % (Σ×scale) × invscale</c> natif).</summary>
    private int PickSlot()
    {
        var w0 = _data.Parameters.Get(504, 0.5f);
        var w1 = _data.Parameters.Get(505, 0.3f) + w0;
        var total = w1 + _data.Parameters.Get(506, 0.2f);
        var roll = (float)(_rng.NextDouble() * total);
        return roll <= w0 ? 0 : roll <= w1 ? 1 : 2;
    }

    /// <summary>+1 au niveau du slot — <c>clone+0x66 + slot×2</c>
    /// natif.</summary>
    private static void IncrementSlot(ItemEntity item, int slot)
    {
        switch (slot)
        {
            case 0: item.TrainSlot1Level++; break;
            case 1: item.TrainSlot2Level++; break;
            default: item.TrainSlot3Level++; break;
        }
    }
}
