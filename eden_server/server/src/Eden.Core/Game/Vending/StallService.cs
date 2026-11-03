using System.Collections.Concurrent;

namespace Eden.Core.Game.Vending;

/// <summary>
/// Étals joueurs (player vending / market booth) — miroir du
/// <c>CStall</c> natif accroché à <c>char+0x7ec</c> : titre, map
/// <c>&lt;u8,SStallGoodsInfo&gt;</c> de marchandises et set
/// <c>&lt;int&gt;</c> de viewers.
///
/// Source de preuve : zone/decompiled.c —
/// <c>CPlayerFactory::VendorSellItem</c> (création, ~0x80f8xxx),
/// <c>CheckBeginStallForPlayer</c> / <c>CheckBeginStallForStallGoods</c>
/// (validations), <c>CPlayerFactory::VendorBuy</c> (@0x80f9770),
/// <c>CPlayerFactory::VendorClose</c> (@0x80f93xx),
/// <c>CPlayerFactory::VendorContainer</c> (vue acheteur).
///
/// Ce service ne fait ni I/O ni paquets : il garde l'état et la
/// logique de validation atomique ; l'orchestration réseau/DB vit
/// dans <c>ZoneEntryFlow.Vending.cs</c>. Toutes les mutations
/// passent par <see cref="_locks"/> (un verrou par vendeur) pour
/// rendre l'achat atomique : identité d'item re-vérifiée sous
/// verrou par le flux avant le transfert.
/// </summary>
public sealed class StallService
{
    /// <summary>Type d'opération natif <c>CVendorOp</c> (vendeur) —
    /// IsA 0x2c, posé à l'ouverture de l'étal.</summary>
    public const int VendorOpType = 0x2c;
    /// <summary>Type d'opération natif <c>CVendorViewOp</c>
    /// (acheteur consultant) — IsA 0x2d.</summary>
    public const int VendorViewOpType = 0x2d;
    /// <summary>Cap d'or natif — <c>999999999</c> (VendorBuy :
    /// seller_gold + price > cap → texte 0xfd3).</summary>
    public const int GoldCap = 999_999_999;
    /// <summary>Prix max d'une ligne de marchandise
    /// (CheckBeginStallForStallGoods : 0 ≤ prix ≤ 999999999).</summary>
    public const int MaxPrice = 999_999_999;
    /// <summary>Max de lignes à prix 0 autorisées (natif : rejet
    /// [0][0x16] au-delà de 13).</summary>
    public const int MaxFreeGoods = 13;
    /// <summary>Titre max en octets natifs (natif : length &lt; 0x5b
    /// = 91).</summary>
    public const int MaxTitleBytes = 90;
    /// <summary>Distance vendeur max, au carré —
    /// DAT_087cc680 = 100.0f → 10 unités monde
    /// (TargetInVendorRange, texte 0x333 au-delà).</summary>
    public const float VendorRangeSquared = 100f;
    /// <summary>Délai minimal entre deux tentatives d'ouverture —
    /// CheckBeginStallForPlayer : 1500 ms → statut [0][0x12].</summary>
    public static readonly TimeSpan OpenCooldown =
        TimeSpan.FromMilliseconds(1500);
    /// <summary>Premier / dernier type de région d'étal natif
    /// (CheckStallRegion : 600 ≤ type ≤ 609).</summary>
    public const int StallRegionFirst = 600;
    public const int StallRegionLast = 609;

    /// <summary>Marchandise d'étal — miroir de
    /// <c>SStallGoodsInfo</c> (noeud map +0x14). Les offsets natifs
    /// du record sérialisé sont en commentaires (ClientVendorUpdateView
    /// ::Serialize — 96 octets par entrée).</summary>
    public sealed class StallGood
    {
        /// <summary>Clé u8 de la map (index client de la ligne).</summary>
        public required byte Index { get; init; }
        /// <summary>u32 — rec+0x00 — item_id S_Item (stale-check
        /// achat vs itemdata+4).</summary>
        public required int ItemId { get; init; }
        /// <summary>i32 — rec+0x04 — itemdata+0x3e : required level
        /// de l'item (u8 natif élargi).</summary>
        public int RequiredLevel { get; init; }
        /// <summary>s16 — rec+0x08 — container natif (vecteur 1 du
        /// paquet d'ouverture = container_index DB).</summary>
        public required short Container { get; init; }
        /// <summary>s16 — rec+0x0a — index natif (vecteur 2 = loc).</summary>
        public required short Slot { get; init; }
        /// <summary>i32 — rec+0x0c — clone+0xc (loc index).</summary>
        public required int LocIndex { get; init; }
        /// <summary>i32 — rec+0x10 — clone+0xe : stale-check achat
        /// (identité d'emplacement ; 0 pour un item non équipé).</summary>
        public required int LocExtra { get; init; }
        /// <summary>u16 — rec+0x14 — clone+0x1c : durability/count.</summary>
        public int Durability { get; init; }
        /// <summary>s16 — rec+0x16 — clone+0x10 (combo_id).</summary>
        public short Combo { get; init; }
        /// <summary>u8 — rec+0x18 — clone+0x24 &amp; 7
        /// (embedded_amount masqué, le bit identify n'est pas émis).</summary>
        public byte FlagLow3 { get; init; }
        /// <summary>s16×6 — rec+0x1a — clone+0x40 (gemmes
        /// incrustées, -1 = non ouverte).</summary>
        public short[] EmbeddedIds { get; init; } = new short[6];
        /// <summary>s16×11 — rec+0x26 — clone+0x34 (extra combos ; le
        /// wire émet les 11 en deux groupes : 0-8 puis 9-10 en fin).</summary>
        public short[] ExtraCombos { get; init; } = new short[11];
        /// <summary>i32 — rec+0x3c — prix du lot complet.</summary>
        public required int Price { get; set; }
        /// <summary>i32 — quantité du lot (le natif soustrait le prix
        /// du record entier → achat = lot complet ; non sérialisé,
        /// champ interne du stale/quantité-check).</summary>
        public required int Quantity { get; init; }
        /// <summary>s16 — rec+0x40 — clone+0x2c (quality/couleur).</summary>
        public short Quality { get; init; }
        /// <summary>s16 — rec+0x42 — clone+0x4c (witchcraft).</summary>
        public short Witchcraft { get; init; }
        /// <summary>s16 — rec+0x44 — clone+0x2e (champ natif non
        /// identifié — train-data basse ; 0 pour nos items).</summary>
        public short Clone2e { get; init; }
        /// <summary>u16 — rec+0x48 — clone+0x58 (train level — pas de
        /// train sur nos items → 0).</summary>
        public short TrainLevel { get; init; }
        /// <summary>u32 — rec+0x4c — clone+0x5c (due_date epoch).</summary>
        public uint DueDate { get; init; }
        /// <summary>s16×3 — rec+0x5c — clone+0x70 (enchantements
        /// boîte magique).</summary>
        public short[] MagicBox { get; init; } = new short[3];
        /// <summary>u16 — rec+0x62 — clone+0x7c (énergie magic box).</summary>
        public int MagicBoxEnergy { get; init; }
        /// <summary>u16 — rec+0x64 — clone+0x7e (niveau d'étoile).</summary>
        public int MagicBoxStar { get; init; }
        /// <summary>s16×6 — rec+0x50..0x5a — clone+0x60..0x6a (slots
        /// de train — émis dans l'ordre natif permuté 0,3,1,4,2,5).</summary>
        public short[] TrainSlots { get; init; } = new short[6];
    }

    /// <summary>Étal ouvert — miroir du <c>CStall</c> natif.</summary>
    public sealed class Stall
    {
        public required int OwnerId { get; init; }
        /// <summary>Titre affiché (chaîne stall+0x00 native) —
        /// mutable : ClearVendorInfo le réécrit à chaque
        /// VendorSellItem.</summary>
        public required string Title { get; set; }
        /// <summary>Marchandises par index u8 (map native triée).</summary>
        public SortedDictionary<byte, StallGood> Goods { get; } = new();
        /// <summary>charIds des viewers (set natif stall+0x2c) —
        /// recevront le 378 à chaque mutation et le 376 à la
        /// fermeture.</summary>
        public HashSet<int> Viewers { get; } = new();
    }

    /// <summary>Résultats de validation — mappés 1:1 sur les codes
    /// natifs (ClientVendorStatus [0][code] ou textes).</summary>
    public enum StallError
    {
        Ok = 0,
        /// <summary>[0][0x11] — état incompatible (mort, déjà
        /// vendeur, op en cours).</summary>
        StateBusy,
        /// <summary>[0][0x12] — requête trop rapprochée (1,5 s).</summary>
        TooSoon,
        /// <summary>[0][0x13] — CanDo(0xe) négatif.</summary>
        CannotStall,
        /// <summary>[0][0x14] — titre vide.</summary>
        EmptyTitle,
        /// <summary>[0][0x15] — collision (container,index).</summary>
        GoodsCollision,
        /// <summary>[0][0x16] — plus de 13 marchandises à prix 0.</summary>
        TooManyFree,
        /// <summary>Vecteurs vides / tailles divergentes — rejet
        /// natif silencieux.</summary>
        BadVectors,
        /// <summary>Prix hors [0,999999999] — rejet natif silencieux.</summary>
        BadPrice,
        /// <summary>Node non S001 / hors région 600-609 — rejet
        /// CheckStallNode/CheckStallRegion.</summary>
        BadRegion,
        /// <summary>0xfd8 — en groupe.</summary>
        InParty,
        /// <summary>0xd20 — file d'arène.</summary>
        InArenaQueue,
        /// <summary>0x1160 — file party/matchmaking.</summary>
        InPartyQueue,
    }

    private readonly ConcurrentDictionary<int, Stall> _byOwner = new();
    /// <summary>Un verrou par vendeur — achat atomique (VendorBuy
    /// natif est mono-threadé serveur ; chez nous les paquets
    /// peuvent arriver de sessions distinctes en parallèle).</summary>
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _locks =
        new();
    private readonly ConcurrentDictionary<int, DateTime> _lastOpen =
        new();

    /// <summary>Étal ouvert d'un vendeur, ou null.</summary>
    public Stall? GetStall(int ownerId) =>
        _byOwner.TryGetValue(ownerId, out var s) ? s : null;

    /// <summary>true si le char tient un étal (bit 0x40 de
    /// char+0x3b natif).</summary>
    public bool IsVending(int ownerId) => _byOwner.ContainsKey(ownerId);

    /// <summary>Verrou d'achat du vendeur — à prendre autour du
    /// stale-check + transfert (miroir de la sérialisation native
    /// VendorBuy).</summary>
    public SemaphoreSlim BuyLock(int ownerId) =>
        _locks.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));

    /// <summary>Horodatage de la dernière tentative d'ouverture —
    /// rate-limit natif 1,5 s de CheckBeginStallForPlayer.</summary>
    public bool CheckOpenCooldown(int ownerId, DateTime now)
    {
        var last = _lastOpen.GetOrAdd(ownerId, DateTime.MinValue);
        return now - last >= OpenCooldown;
    }

    /// <summary>Enregistre la tentative d'ouverture (succès ou échec
    /// — le natif arme le timer dès la tentative).</summary>
    public void MarkOpenAttempt(int ownerId, DateTime now) =>
        _lastOpen[ownerId] = now;

    /// <summary>Validation des vecteurs de marchandises —
    /// <c>CheckBeginStallForStallGoods</c> natif : non vide, même
    /// taille, pas de collision (container,index), prix bornés, ≤13
    /// prix nuls.</summary>
    /// <param name="entries">(container, index, price) par ligne du
    /// paquet 77.</param>
    public static StallError CheckGoods(
        IReadOnlyList<(short Container, short Index, int Price)> entries)
    {
        // Vecteurs vides → retour silencieux natif.
        if (entries.Count == 0) return StallError.BadVectors;
        // Collision (container,index) sur DEUX lignes → [0][0x15]
        // (le natif compare les paires deux à deux).
        for (var i = 0; i < entries.Count; i++)
        {
            var (c, s, p) = entries[i];
            // Prix hors [0,999999999] → retour silencieux natif.
            if (p < 0 || p > MaxPrice) return StallError.BadPrice;
            for (var j = i + 1; j < entries.Count; j++)
                if (entries[j].Container == c && entries[j].Index == s)
                    return StallError.GoodsCollision;
        }
        // Plus de 13 prix à 0 → [0][0x16].
        var free = 0;
        foreach (var e in entries) if (e.Price == 0) free++;
        return free > MaxFreeGoods ? StallError.TooManyFree
            : StallError.Ok;
    }

    /// <summary>Ouvre ou re-remplit l'étal — <c>VendorSellItem</c>
    /// natif : le <c>CStall</c> est créé s'il n'existe pas puis
    /// <c>ClearVendorInfo</c> efface goods+titre (les viewers sont
    /// CONSERVÉS — le set stall+0x2c n'est pas touché). Le client
    /// renvoie la liste complète des marchandises à chaque
    /// modification de l'offre.</summary>
    public Stall OpenOrUpdate(int ownerId, string title,
        IEnumerable<StallGood> goods)
    {
        var stall = _byOwner.GetOrAdd(ownerId,
            id => new Stall { OwnerId = id, Title = title });
        stall.Title = title;
        stall.Goods.Clear();               // ClearVendorInfo natif
        foreach (var g in goods) stall.Goods[g.Index] = g;
        return stall;
    }

    /// <summary>Ferme l'étal — <c>VendorClose</c> : efface goods et
    /// viewers, retourne l'étal pour que le flux notifie les
    /// viewers (376 + 0xf07) et le vendeur (375).</summary>
    public Stall? Close(int ownerId) =>
        _byOwner.TryRemove(ownerId, out var s) ? s : null;

    /// <summary>Retire une marchandise vendue (erase natif du noeud
    /// map après achat).</summary>
    public void RemoveGood(int ownerId, byte index)
    {
        if (_byOwner.TryGetValue(ownerId, out var s))
            s.Goods.Remove(index);
    }

    /// <summary>Enregistre un viewer (CVendorViewOp posé).</summary>
    public void AddViewer(int ownerId, int viewerCharId)
    {
        if (_byOwner.TryGetValue(ownerId, out var s))
            s.Viewers.Add(viewerCharId);
    }

    /// <summary>Retire un viewer — <c>VendorCloseView</c> natif
    /// (reset op côté client fait par le flux).</summary>
    public void RemoveViewer(int ownerId, int viewerCharId)
    {
        if (_byOwner.TryGetValue(ownerId, out var s))
            s.Viewers.Remove(viewerCharId);
    }

    /// <summary>Test de portée vendeur — TargetInVendorRange natif :
    /// dist² X/Z ≤ 100 (10 unités monde).</summary>
    public static bool InVendorRange(float ax, float az,
        float bx, float bz)
    {
        var dx = ax - bx;
        var dz = az - bz;
        return dx * dx + dz * dz <= VendorRangeSquared;
    }

    /// <summary>Nettoyage à la déconnexion : vendeur → Close (le
    /// flux notifie), viewer → retrait des sets (sinon un viewer
    /// mort garde son entrée et reçoit des paquets à jamais).</summary>
    public Stall? CleanupDisconnected(int charId)
    {
        foreach (var s in _byOwner.Values)
            s.Viewers.Remove(charId);
        return Close(charId);
    }
}
