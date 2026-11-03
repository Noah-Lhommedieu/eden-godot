using EdenClient.Net;

namespace EdenClient.Protocol;

// ===================================================================
// Item mall — codecs zone + monde.
//
// AUDIT re/ghidra/rf_itemmall (ItemMall.dll, export `ItemMallInit`) :
// la DLL Reforged N'APPORTE AUCUN protocole propre — c'est un ensemble
// de byte-patchs sur le client natif (hook du chemin d'achat
// « paced purchasing », suppression du shader-cache, timer ini
// `IntervalMs`/`MaximumPackages` — strings.txt
// `purchase hook bytes unavailable`, `shader-cache suppression
// active`). Le protocole mall utilisé reste donc le CW natif.
//
// Canal : le mall vit sur la connexion **monde** (:5200), gardée
// ouverte pendant le jeu — les NCIDs ci-dessous sont des NCIDs monde
// (pas zone), transportés par EdenSession.SendWorldAsync / OnWorld.
//
// Preuves des formats :
//   - serveur C# `Eden.World/Handlers/ItemMallHandlers.cs` (port du
//     désasm world natif @0x80dff00…) ;
//   - `Eden.Core/Game/Mall/ItemMallPackets.cs` (62/79 builders,
//     désasm Serialize @0x80…21310/0x8062640) ;
//   - `Eden.Core/Networking/Messages/Generated/WorldMessages.g.cs`
//     (champs C++ originaux des CNE_CW_*) ;
//   - mitm_native_entry.log ligne 30 (C→W 17 = 1500 0000 0000) et
//     ligne ~31 (C→W 25 vide, W→C 64 [i32][i32]) — déjà codés dans
//     WorldMessages.cs (WorldItemMallReceivableQuery,
//     WorldItemMallPointInfoQuery, WorldItemMallPointInfo).
//   - zone : `Generated/ZoneMessages.g.cs` CNE_CZ_* 70/83 et
//     `Eden.Zone/Handlers/ItemActionHandlers.cs` (83 =
//     `[s16 sel][s16 cont][s16 slot][i32 uiItemId][str params]`).
//
// money_unit : 1 = pvalues (« AP », moneyText), 2 = bonus
// (BonusmoneyText), 3 = or « Forever Gold » temps de jeu
// (ForeverGoldText — rabattu côté serveur sur pvalues, pas de billing
// séparé). group_key = money_unit*100 + item_group (même encodage
// dans le catalogue 62, les limites 79 et l'achat 23 — cf.
// ItemMallPackets.cs:69-74 qui documente le bug historique).
// ===================================================================

// ---------------- C→Z : zone ----------------

/// <summary>C→Z NCID 70 — ItemMallReceivableEnter, corps vide.
/// Le natif le pose quand l'UI de réclamation mall s'ouvre : la zone
/// marque l'op <c>CItemMallReceivableOp</c> (IsA 0x2f) dans
/// <c>ShopOpType</c> (ZoneEntryFlow.ItemMall.cs + ZoneEntryFlow.cs
/// case 70). Codec : serveur <c>CNE_CZ_ZoneServerItemMallReceivableEnter
/// </c> — aucun champ sérialisé.</summary>
public sealed class ZoneItemMallReceivableEnter : NetMessage
{
    public override int Opcode => 70;
    public override string Name => "Zone.ItemMallReceivableEnter";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>C→Z NCID 83 — ItemMallUseItem
/// `[s16 sel][s16 contIdx][s16 slot][i32 uiItemId][str params]` :
/// usage d'un item « mall » (même pipeline UseItemParam que l'ItemUse
/// 19 ; sel 0 = sac, 1 = équipement). Codec : serveur
/// <c>CNE_CZ_ZoneServerItemMallUseItem</c> + handler
/// ItemActionHandlers.cs:546-608.</summary>
public sealed class ZoneItemMallUseItem : NetMessage
{
    public override int Opcode => 83;
    public override string Name => "Zone.ItemMallUseItem";
    /// <summary>s16 — 0 = conteneur sac, 1 = équipement.</summary>
    public short Sel { get; set; }
    /// <summary>s16 — index du conteneur dans la map choisie.</summary>
    public short ContainerIndex { get; set; }
    /// <summary>s16 — slot dans le conteneur.</summary>
    public short Slot { get; set; }
    /// <summary>i32 — clé des param-commands kind-keyed (0 usage std).</summary>
    public int UiItemId { get; set; }
    /// <summary>str — paramètres additionnels ("" standard).</summary>
    public string Params { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Sel);
        s.WriteShort(ContainerIndex);
        s.WriteShort(Slot);
        s.WriteInt(UiItemId);
        s.WriteString(Params);
    }
    public override void Deserialize(BinReader s)
    {
        Sel = s.ReadShort();
        ContainerIndex = s.ReadShort();
        Slot = s.ReadShort();
        UiItemId = s.ReadInt();
        Params = s.ReadString();
    }
}

// ---------------- C→W : monde ----------------

/// <summary>C→W NCID 15 — ItemMallAnythingReceivable, corps vide :
/// « ai-je des items à réclamer ? » → réponse monde 56 [u8].
/// Codec : serveur <c>CNE_CW_ServerItemMallAnythingReceivable</c>
/// (aucun champ sérialisé).</summary>
public sealed class WorldItemMallAnythingQuery : NetMessage
{
    public override int Opcode => 15;
    public override string Name => "World.ItemMallAnythingReceivable";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>C→W NCID 18 — ItemMallReceieveAttempt `[u32 receiptId]` :
/// réclame l'item du reçu → livraison ZW 24 → 58 résultat.
/// Codec : serveur <c>CNE_CW_ServerItemMallReceieveAttempt</c>
/// (champ C++ `uint`).</summary>
public sealed class WorldItemMallReceiveAttempt : NetMessage
{
    public override int Opcode => 18;
    public override string Name => "World.ItemMallReceiveAttempt";
    /// <summary>u32 — id du reçu (ItemReceivable.Id du 57).</summary>
    public uint ReceiptId { get; set; }
    public override void Serialize(BinWriter s)
        => s.WriteInt((int)ReceiptId);
    public override void Deserialize(BinReader s)
        => ReceiptId = (uint)s.ReadInt();
}

/// <summary>C→W NCID 23 — ItemMallReqBuyItem
/// `[s16 mu*100+grp][s16 index][s16 amount][u16 version][str][str][str]`.
/// Codec : serveur <c>CNE_CW_ServerItemMallReqBuyItem</c> +
/// HandleBuyAsync (Field1 clé encodée, Field4 version catalogue,
/// Field6 = nom du destinataire pour envoi « cadeau » — vide en achat
/// standard).</summary>
public sealed class WorldItemMallBuyRequest : NetMessage
{
    public override int Opcode => 23;
    public override string Name => "World.ItemMallReqBuyItem";
    /// <summary>s16 — money_unit*100 + item_group.</summary>
    public short Key { get; set; }
    /// <summary>s16 — item_index dans le groupe.</summary>
    public short Index { get; set; }
    /// <summary>s16 — quantité (serveur borne à ≥1 et ValidateBuyAmount).</summary>
    public short Amount { get; set; }
    /// <summary>u16 — version du catalogue connue (62).</summary>
    public ushort Version { get; set; }
    /// <summary>str — réservé ("" natif).</summary>
    public string Field5 { get; set; } = "";
    /// <summary>str — nom du destinataire (mail_name — cadeau ; ""
     /// = pour soi).</summary>
    public string Recipient { get; set; } = "";
    /// <summary>str — réservé ("" natif).</summary>
    public string Field7 { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Key);
        s.WriteShort(Index);
        s.WriteShort(Amount);
        s.WriteUShort(Version);
        s.WriteString(Field5);
        s.WriteString(Recipient);
        s.WriteString(Field7);
    }
    public override void Deserialize(BinReader s)
    {
        Key = s.ReadShort();
        Index = s.ReadShort();
        Amount = s.ReadShort();
        Version = s.ReadUShort();
        Field5 = s.ReadString();
        Recipient = s.ReadString();
        Field7 = s.ReadString();
    }
}

/// <summary>C→W NCID 24 — ItemMallListQuery `[s16][u16]` : demande le
/// catalogue (62 batches) + les limites du compte (79). Le serveur
/// ignore le corps (SendCatalogAsync systématique) ; le natif envoie
/// probablement filtre/version — on envoie [s16 0][u16 version connue].
/// Codec : serveur <c>CNE_CW_ServerItemMallListQuery</c> (short,ushort).</summary>
public sealed class WorldItemMallListQuery : NetMessage
{
    public override int Opcode => 24;
    public override string Name => "World.ItemMallListQuery";
    /// <summary>s16 — 0 (filtre inconnu, ignoré serveur).</summary>
    public short Field1 { get; set; }
    /// <summary>u16 — dernière version connue du catalogue.</summary>
    public ushort Version { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteUShort(Version);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Version = s.ReadUShort();
    }
}

// ---------------- W→C : monde ----------------

/// <summary>W→C NCID 56 — ItemMallAnythingReceivable `[u8 bool]`.
/// Codec : serveur <c>CNC_CW_ClientItemMallAnythingReceivable</c>
/// (champ `byte`).</summary>
public sealed class WorldItemMallAnything : NetMessage
{
    public override int Opcode => 56;
    public override string Name => "World.ClientItemMallAnything";
    public bool Anything { get; set; }
    public override void Serialize(BinWriter s) => s.WriteBool(Anything);
    public override void Deserialize(BinReader s)
        => Anything = s.ReadBool();
}

/// <summary>Un reçu réclamable (record du 57).</summary>
public sealed record ItemMallReceipt(uint Id, int ItemId, int Quantity,
    string PurchaseTime);

/// <summary>W→C NCID 57 — ItemMallReceivableList
/// `[s16 n][n×{u32 id, u16 item, u16 qty, str "YYYY-MM-DD HH:MM:SS"}]`.
/// Codec : serveur ItemMallHandlers.BuildReceivableQueryBody
/// (Serialize natif @0x80cd290).</summary>
public sealed class WorldItemMallReceivableList : NetMessage
{
    public override int Opcode => 57;
    public override string Name => "World.ClientItemMallReceivableList";
    public List<ItemMallReceipt> Receipts { get; } = new();
    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Receipts.Count);
        foreach (var r in Receipts)
        {
            s.WriteInt((int)r.Id);
            s.WriteUShort((ushort)r.ItemId);
            s.WriteUShort((ushort)r.Quantity);
            s.WriteString(r.PurchaseTime);
        }
    }
    public override void Deserialize(BinReader s)
    {
        Receipts.Clear();
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 8; i++)
        {
            var id = (uint)s.ReadInt();
            var item = s.ReadUShort();
            var qty = s.ReadUShort();
            var time = s.Remaining >= 2 ? s.ReadString() : "";
            Receipts.Add(new ItemMallReceipt(id, item, qty, time));
        }
    }
}

/// <summary>W→C NCID 58 — ItemMallReceieveAttempt résultat
/// `[s16 result][u32 receipt]` (ItemMallResult : 0 ok, 0xb record
/// introuvable, 0x15 char hors zone, 0x19 inventaire plein, 99 échec).
/// Codec : serveur <c>CNC_CW_ClientItemMallReceieveAttempt</c>
/// (short, uint).</summary>
public sealed class WorldItemMallReceiveResult : NetMessage
{
    public override int Opcode => 58;
    public override string Name => "World.ClientItemMallReceiveResult";
    public short Result { get; set; }
    public uint ReceiptId { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Result);
        s.WriteInt((int)ReceiptId);
    }
    public override void Deserialize(BinReader s)
    {
        Result = s.ReadShort();
        ReceiptId = (uint)s.ReadInt();
    }
}

/// <summary>Une entrée du catalogue mall (record du 62).</summary>
public sealed class ItemMallEntry
{
    /// <summary>s16 — clé encodée money_unit*100 + item_group.</summary>
    public int Key;
    /// <summary>s16 — item_index dans le groupe.</summary>
    public int Index;
    /// <summary>u16 — id d'item réel (S_Item — icône via c_item.ini).</summary>
    public int ItemId;
    /// <summary>u16 — quantité par paquet.</summary>
    public int ItemNum;
    /// <summary>u32 — prix plein.</summary>
    public uint Point;
    /// <summary>u32 — prix promo (0 = pas de promo).</summary>
    public uint SpecialPrice;
    /// <summary>u16 — limite d'achat par compte (groupe 8).</summary>
    public int NumLimit;
    /// <summary>i64 — teinte/variante de l'item livré.</summary>
    public long ItemColor;
    /// <summary>i32 — epoch de fin de vente (0 = sans fin).</summary>
    public int NotSellDate;
    /// <summary>str — clé fortune_bag.</summary>
    public string FortuneBag = "";
    /// <summary>str — note du vendeur.</summary>
    public string Note = "";
    /// <summary>u16 — niveau minimum du perso (0 = pas de limite).</summary>
    public int LevelLimit;
    /// <summary>i32 — epoch de mise en vente (0 = déjà en vente).</summary>
    public int SellDate;
    /// <summary>u16 — déjà acheté par ce compte (vient du 79).</summary>
    public int Sold;

    public int MoneyUnit => Key / 100;
    public int Group => Key % 100;
    /// <summary>Prix effectif : promo si non nulle, sinon plein.</summary>
    public uint EffectivePrice =>
        SpecialPrice != 0 ? SpecialPrice : Point;
}

/// <summary>W→C NCID 62 — ClientItemMallList (lot de ≤20 records) :
/// `[u16 version][u16 n][n×{s16 key(mu*100+grp), s16 index, u16 item,
/// u16 num, u32 point, u32 special, u16 numLimit, i64 color,
/// i32 notSell, str fortuneBag, str note, u16 lvl, i32 sell}]`.
/// Codec : serveur <c>ItemMallPackets.SerializeCatalog</c> (désasm
/// Serialize natif). Le client accumule les lots jusqu'à requête
/// suivante — chaque lot porte la même version.</summary>
public sealed class WorldItemMallList : NetMessage
{
    public override int Opcode => 62;
    public override string Name => "World.ClientItemMallList";
    public ushort Version { get; set; }
    public List<ItemMallEntry> Entries { get; } = new();
    public override void Serialize(BinWriter s)
    {
        s.WriteUShort(Version);
        s.WriteUShort((ushort)Entries.Count);
        foreach (var e in Entries) WriteEntry(s, e);
    }
    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        if (s.Remaining < 4) return;
        Version = s.ReadUShort();
        var n = s.ReadUShort();
        for (var i = 0; i < n; i++)
        {
            // Record fixe 36 + strings : garde anti-troncature.
            if (s.Remaining < 30) return;
            var e = ReadEntry(s);
            if (e == null) return;
            Entries.Add(e);
        }
    }

    private static void WriteEntry(BinWriter s, ItemMallEntry e)
    {
        s.WriteShort((short)e.Key);
        s.WriteShort((short)e.Index);
        s.WriteUShort((ushort)e.ItemId);
        s.WriteUShort((ushort)e.ItemNum);
        s.WriteUInt(e.Point);
        s.WriteUInt(e.SpecialPrice);
        s.WriteUShort((ushort)e.NumLimit);
        s.WriteLong(e.ItemColor);
        s.WriteInt(e.NotSellDate);
        s.WriteString(e.FortuneBag);
        s.WriteString(e.Note);
        s.WriteUShort((ushort)e.LevelLimit);
        s.WriteInt(e.SellDate);
    }

    private static ItemMallEntry? ReadEntry(BinReader s)
    {
        var e = new ItemMallEntry
        {
            Key = s.ReadShort(),
            Index = s.ReadShort(),
            ItemId = s.ReadUShort(),
            ItemNum = s.ReadUShort(),
            Point = s.ReadUInt(),
            SpecialPrice = s.ReadUInt(),
            NumLimit = s.ReadUShort(),
            ItemColor = s.ReadLong(),
            NotSellDate = s.ReadInt(),
        };
        if (s.Remaining < 4) return null;
        e.FortuneBag = s.ReadString();
        if (s.Remaining < 8) return null;
        e.Note = s.ReadString();
        e.LevelLimit = s.ReadUShort();
        if (s.Remaining < 4) return null;
        e.SellDate = s.ReadInt();
        return e;
    }
}

/// <summary>W→C NCID 63 — résultat d'achat
/// `[s16 result][i32 pvalues][i32 bonus]` (ItemMallResult serveur :
/// 0 ok, 1 solde pvalues insuffisant, 2 version périmée, 3 solde bonus
/// insuffisant, 5/6 limite compte, 7 niveau, 0xb record, 99 échec).
/// Codec : serveur <c>CNC_CW_ClientItemMallReqBuyItem</c> (short,int,int).</summary>
public sealed class WorldItemMallBuyResult : NetMessage
{
    public override int Opcode => 63;
    public override string Name => "World.ClientItemMallBuyResult";
    public short Result { get; set; }
    public int Pvalues { get; set; }
    public int Bonus { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Result);
        s.WriteInt(Pvalues);
        s.WriteInt(Bonus);
    }
    public override void Deserialize(BinReader s)
    {
        Result = s.ReadShort();
        if (s.Remaining >= 8)
        {
            Pvalues = s.ReadInt();
            Bonus = s.ReadInt();
        }
    }
}

/// <summary>Une ligne de limite d'achat du compte (record du 79).</summary>
public sealed record ItemMallLimit(short Key, short Index, int ItemId,
    int ItemNum, int Sold);

/// <summary>W→C NCID 79 — ClientItemMallLimit
/// `[u16 version][u16 n][n×{s16 key(mu*100+grp), s16 index, u16 item,
/// u16 num, u16 sell_amount}]`. Codec : serveur
/// <c>ItemMallPackets.BuildLimit</c> (désasm GetAllItemMallLimit).</summary>
public sealed class WorldItemMallLimit : NetMessage
{
    public override int Opcode => 79;
    public override string Name => "World.ClientItemMallLimit";
    public ushort Version { get; set; }
    public List<ItemMallLimit> Rows { get; } = new();
    public override void Serialize(BinWriter s)
    {
        s.WriteUShort(Version);
        s.WriteUShort((ushort)Rows.Count);
        foreach (var r in Rows)
        {
            s.WriteShort(r.Key);
            s.WriteShort(r.Index);
            s.WriteUShort((ushort)r.ItemId);
            s.WriteUShort((ushort)r.ItemNum);
            s.WriteUShort((ushort)r.Sold);
        }
    }
    public override void Deserialize(BinReader s)
    {
        Rows.Clear();
        if (s.Remaining < 4) return;
        Version = s.ReadUShort();
        var n = s.ReadUShort();
        for (var i = 0; i < n && s.Remaining >= 10; i++)
        {
            var key = s.ReadShort();
            var index = s.ReadShort();
            var item = s.ReadUShort();
            var num = s.ReadUShort();
            var sold = s.ReadUShort();
            Rows.Add(new ItemMallLimit(key, index, item, num, sold));
        }
    }
}

// ---------------- état agrégé ----------------

/// <summary>Texte d'un code résultat 63/58 (ItemMallResult serveur —
/// ItemMallModels.cs:103-131, port des constantes du désasm world).</summary>
public static class ItemMallResults
{
    public const short Ok = 0;
    public const short InsufficientPoints = 1;
    public const short VersionMismatch = 2;
    public const short InsufficientBonus = 3;
    public const short LimitCheckFailed = 5;
    public const short LimitExceeded = 6;
    public const short LevelTooLow = 7;
    public const short RecordNotFound = 0xb;
    public const short CharNotInZone = 0x15;
    public const short ItemDefMissing = 0x18;
    public const short InventoryFull = 0x19;
    public const short Failure = 99;

    public static string Text(short code) => code switch
    {
        Ok => "OK",
        InsufficientPoints => "Points insuffisants (AP).",
        VersionMismatch => "Catalogue périmé — actualisation.",
        InsufficientBonus => "Points bonus insuffisants.",
        LimitCheckFailed => "Vérification de la limite d'achat échouée.",
        LimitExceeded => "Limite d'achat atteinte pour cet objet.",
        LevelTooLow => "Niveau du personnage trop bas.",
        RecordNotFound => "Reçu introuvable.",
        CharNotInZone => "Personnage absent de la zone.",
        ItemDefMissing => "Objet inconnu.",
        InventoryFull => "Inventaire plein.",
        _ => $"Erreur {code}",
    };
}

/// <summary>
/// État agrégé du mall (catalogue 62, limites 79, soldes 64/63,
/// réclamables 56/57/58) — alimenté par les handlers monde de
/// <see cref="ItemMallWindow"/> ; la fenêtre se rafraîchit sur
/// <see cref="Changed"/>. Mutations toujours sur le thread principal.
/// </summary>
public static class ItemMallState
{
    /// <summary>Émis à chaque changement de catalogue/solde/reçus.</summary>
    public static event Action? Changed;

    /// <summary>Verrou des mutations — les handlers réseau appliquent
    /// immédiatement (<c>Apply*Data</c>) pendant que l'UI lit des
    /// snapshots ; seul <see cref="Changed"/> reste posté sur le
    /// thread principal via <see cref="RaiseChanged"/>.</summary>
    private static readonly object _gate = new();

    /// <summary>Dernière version du catalogue reçue (0 = jamais reçu).</summary>
    public static ushort Version { get; private set; }

    /// <summary>Solde « AP » (pvalues, money_unit 1).</summary>
    public static int Points { get; private set; }
    /// <summary>Solde bonus (money_unit 2).</summary>
    public static int Bonus { get; private set; }
    /// <summary>Témoin « des items attendent » (56).</summary>
    public static bool AnythingReceivable { get; private set; }

    private static readonly Dictionary<int, ItemMallEntry> _entries = new();
    /// <summary>Catalogue par clé wire `(key&lt;&lt;16)|index` —
    /// snapshot à chaque lecture (mutation possible hors thread UI).</summary>
    public static IReadOnlyDictionary<int, ItemMallEntry> Entries
    {
        get { lock (_gate) return new Dictionary<int, ItemMallEntry>(_entries); }
    }

    private static readonly List<ItemMallReceipt> _receipts = new();
    /// <summary>Reçus réclamables du dernier 57 — snapshot.</summary>
    public static IReadOnlyList<ItemMallReceipt> Receipts
    {
        get { lock (_gate) return _receipts.ToList(); }
    }

    /// <summary>Dernier résultat d'achat (63) — message statut UI.</summary>
    public static string LastStatus { get; private set; } = "";

    private static int EntryId(int key, int index) => (key << 16) | (index & 0xffff);

    /// <summary>Émet <see cref="Changed"/> — à appeler depuis le
    /// thread principal (file <c>_pending</c> du monteur).</summary>
    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>62 : fusionne un lot dans le catalogue (donnée).</summary>
    public static void ApplyListData(WorldItemMallList m)
    {
        lock (_gate)
        {
            Version = m.Version;
            foreach (var e in m.Entries)
                _entries[EntryId(e.Key, e.Index)] = e;
        }
    }

    /// <summary>79 : met à jour les compteurs « déjà achetés ».</summary>
    public static void ApplyLimitData(WorldItemMallLimit m)
    {
        lock (_gate)
            foreach (var r in m.Rows)
                if (_entries.TryGetValue(EntryId(r.Key, r.Index), out var e))
                    e.Sold = r.Sold;
    }

    /// <summary>64 : soldes pvalues/bonus.</summary>
    public static void ApplyPointsData(int pvalues, int bonus)
    {
        lock (_gate) { Points = pvalues; Bonus = bonus; }
    }

    /// <summary>56 : témoin « à réclamer » (+ requête 17 amorcée par la fenêtre).</summary>
    public static void ApplyAnythingData(bool anything)
    {
        lock (_gate) AnythingReceivable = anything;
    }

    /// <summary>57 : liste des reçus.</summary>
    public static void ApplyReceiptsData(WorldItemMallReceivableList m)
    {
        lock (_gate)
        {
            _receipts.Clear();
            _receipts.AddRange(m.Receipts);
            AnythingReceivable = _receipts.Count > 0;
        }
    }

    /// <summary>58 : résultat de réclamation (le reçu disparaît).</summary>
    public static void ApplyReceiveResultData(WorldItemMallReceiveResult m)
    {
        lock (_gate)
        {
            LastStatus = ItemMallResults.Text(m.Result);
            if (m.Result == 0)
            {
                _receipts.RemoveAll(r => r.Id == m.ReceiptId);
                AnythingReceivable = _receipts.Count > 0;
            }
        }
    }

    /// <summary>63 : résultat d'achat + soldes frais.</summary>
    public static void ApplyBuyResultData(WorldItemMallBuyResult m)
    {
        lock (_gate)
        {
            LastStatus = ItemMallResults.Text(m.Result);
            Points = m.Pvalues;
            Bonus = m.Bonus;
        }
    }

    /// <summary>Message de statut libre (UI → bandeau, thread principal).</summary>
    public static void SetStatus(string s)
    {
        lock (_gate) LastStatus = s;
        Changed?.Invoke();
    }

    /// <summary>Reset hors-session (tests/démo, thread principal).</summary>
    public static void Reset()
    {
        lock (_gate)
        {
            Version = 0;
            Points = Bonus = 0;
            AnythingReceivable = false;
            LastStatus = "";
            _entries.Clear();
            _receipts.Clear();
        }
        Changed?.Invoke();
    }
}
