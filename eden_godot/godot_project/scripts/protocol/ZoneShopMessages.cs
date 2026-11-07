using EdenClient.Net;

namespace EdenClient.Protocol;

// Shops PNJ, transport payant, banque (S→C + C→S).
//
// Sources de preuve :
//   - $EDEN/server/.../Generated/ZoneMessages.g.cs —
//     CNC_CZ_ClientEnterShop (304), ClientEnterTransport (393),
//     ClientEnterStorage (357), ClientBuyUp (308), ClientDie (305) ;
//   - $EDEN/server/src/Eden.Zone/Handlers/ShopHandlers.cs — wire des
//     BuyItems/SellItems/Transport ;
//   - $EDEN/server/src/Eden.Zone/Codecs/NativeItemCodec.cs —
//     BuildGetStorageData (358, désasm Serialize @0x82366b0) ;
//   - C_Store.ini / C_Transport.ini / T_Transport.ini côté client.

// ---------- C→S : commerce ----------

/// <summary>C→S NCID 22 — BuyItems [u16 storeSlot][u16 count][u8 buyback].
/// CNE_CZ_ZoneServerBuyItems — ShopHandlers.cs : storeSlot = index de
/// la ligne dans C_Store du shop ouvert ; buyback!=0 → rachat buyback
/// (itemIndex = index de la file).</summary>
public sealed class ZoneBuyItems : NetMessage
{
    public override int Opcode => 22;
    public override string Name => "Zone.ZoneServerBuyItems";
    /// <summary>u16 — index de ligne dans le store (0..N).</summary>
    public ushort ShopIndex { get; set; }
    /// <summary>u16 — quantité (ou index buyback si BuyBack!=0).</summary>
    public ushort ItemIndex { get; set; }
    /// <summary>u8 — 0 achat normal, !=0 rachat buyback.</summary>
    public byte BuyBack { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)ShopIndex);
        s.WriteShort((short)ItemIndex);
        s.WriteByte(BuyBack);
    }
    public override void Deserialize(BinReader s)
    {
        ShopIndex = s.ReadUShort();
        ItemIndex = s.ReadUShort();
        BuyBack = s.ReadByte();
    }
}

/// <summary>C→S NCID 23 — SellItems [i32 n][n×(u16 slot,u16 count,u16 container)]
/// — champs ENTRELACÉS (désasm Deserialize natif, ShopHandlers.cs).</summary>
public sealed class ZoneSellItems : NetMessage
{
    public override int Opcode => 23;
    public override string Name => "Zone.ZoneServerSellItems";
    /// <summary>Triades (slot, quantité, conteneur).</summary>
    public List<(ushort Slot, ushort Count, ushort Container)> Items { get; }
        = new();
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Items.Count);
        foreach (var (slot, count, cont) in Items)
        {
            s.WriteShort((short)slot);
            s.WriteShort((short)count);
            s.WriteShort((short)cont);
        }
    }
    public override void Deserialize(BinReader s)
    {
        var n = s.ReadInt();
        for (var i = 0; i < n && s.Remaining >= 6; i++)
            Items.Add((s.ReadUShort(), s.ReadUShort(), s.ReadUShort()));
    }
}

/// <summary>C→S NCID 185 — BuyBackOpen [u8 flag] : bascule d'onglet
/// de la fenêtre shop (Execute natif @0x823eaf0, ShopHandlers.cs) :
/// flag!=0 → onglet « rachat » (réponse : 512 flag=3 puis rows déjà
/// accumulées côté client) ; flag==0 → retour onglet « articles »
/// (réponse : 304 re-émis). CNE_CZ_ZoneServerBuyBackOpen.</summary>
public sealed class ZoneBuyBackOpen : NetMessage
{
    public override int Opcode => 185;
    public override string Name => "Zone.ZoneServerBuyBackOpen";
    /// <summary>u8 — 0 onglet articles, !=0 onglet rachat.</summary>
    public byte Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteByte(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadByte();
}

/// <summary>C→S NCID 71 — AskVendor [i32 npcId] : demande la boutique
/// du NPC (réponse = 304). CNE_CZ_ZoneServerAskVendor.</summary>
public sealed class ZoneAskVendor : NetMessage
{
    public override int Opcode => 71;
    public override string Name => "Zone.ZoneServerAskVendor";
    public int Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadInt();
}

/// <summary>C→S NCID 72 — AskVendorContainer [i32] : onglet conteneur
/// du vendor. CNE_CZ_ZoneServerAskVendorContainer.</summary>
public sealed class ZoneAskVendorContainer : NetMessage
{
    public override int Opcode => 72;
    public override string Name => "Zone.ZoneServerAskVendorContainer";
    public int Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadInt();
}

// ---------- C→S : transport / portails ----------

/// <summary>C→S NCID 88 — Transport [s16 sel] : choix d'une route de
/// transport (id route T_Transport, ou index dans la liste du 393 —
/// le serveur accepte les deux). CNE_CZ_ZoneServerTransport.</summary>
public sealed class ZoneTransport : NetMessage
{
    public override int Opcode => 88;
    public override string Name => "Zone.ZoneServerTransport";
    public short Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadShort();
}

/// <summary>C→S NCID 15 — CheckNodeExit [s16 nodeId][s16 portalId] :
/// le joueur franchit un portail de node. CNE_CZ_ZoneServerCheckNodeExit.</summary>
public sealed class ZoneCheckNodeExit : NetMessage
{
    public override int Opcode => 15;
    public override string Name => "Zone.ZoneServerCheckNodeExit";
    /// <summary>s16 — node destination (aire) demandé.</summary>
    public short NodeId { get; set; }
    /// <summary>s16 — id du portail emprunté.</summary>
    public short PortalId { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(NodeId);
        s.WriteShort(PortalId);
    }
    public override void Deserialize(BinReader s)
    {
        NodeId = s.ReadShort();
        PortalId = s.ReadShort();
    }
}

/// <summary>C→S NCID 20 — Revive [i32 type] : demande de réanimation.
/// CNE_CZ_ZoneServerRevive.</summary>
public sealed class ZoneReviveRequest : NetMessage
{
    public override int Opcode => 20;
    public override string Name => "Zone.ZoneServerRevive";
    /// <summary>i32 — type de revive (0 = point de résurrection).</summary>
    public int ReviveType { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(ReviveType);
    public override void Deserialize(BinReader s) => ReviveType = s.ReadInt();
}

// ---------- C→S : banque ----------

/// <summary>C→S NCID 60 — StorageDeposit [i32][u16][s16][s16] :
/// déposer un item en banque. CNE_CZ_ZoneServerStorageDeposit.</summary>
public sealed class ZoneStorageDeposit : NetMessage
{
    public override int Opcode => 60;
    public override string Name => "Zone.ZoneServerStorageDeposit";
    public int Field1 { get; set; }     // i32 — item/or
    public ushort Field2 { get; set; }  // u16
    public short Field3 { get; set; }   // s16 — slot
    public short Field4 { get; set; }   // s16 — container
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadUShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
    }
}

/// <summary>C→S NCID 61 — StorageWithdraw [i32][u16][s16][s16] :
/// retirer un item de la banque. CNE_CZ_ZoneServerStorageWithdraw.</summary>
public sealed class ZoneStorageWithdraw : NetMessage
{
    public override int Opcode => 61;
    public override string Name => "Zone.ZoneServerStorageWithdraw";
    public int Field1 { get; set; }
    public ushort Field2 { get; set; }
    public short Field3 { get; set; }
    public short Field4 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteShort((short)Field2);
        s.WriteShort(Field3);
        s.WriteShort(Field4);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadUShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadShort();
    }
}

// ---------- S→C ----------

/// <summary>S→C NCID 304 — EnterShop : ouvre une boutique PNJ.
/// DESASM — `[i32 npcUid][s16 storeId][u8][s16]`. La liste des articles
/// est lue côté client dans C_Store.ini (le serveur n'ouvre que la
/// boutique — <see cref="Data.Tables.StoreTable"/>).</summary>
public sealed class ZoneEnterShop : NetMessage
{
    public override int Opcode => 304;
    public override string Name => "Zone.ClientEnterShop";
    /// <summary>i32 — uid du NPC marchand.</summary>
    public int NpcUid { get; set; }
    /// <summary>s16 — id du store (ligne C_Store.ini).</summary>
    public short StoreId { get; set; }
    /// <summary>u8 — flag natif.</summary>
    public byte Flag { get; set; }
    /// <summary>s16 — champ terminal natif.</summary>
    public short Field4 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(NpcUid);
        s.WriteShort(StoreId);
        s.WriteByte(Flag);
        s.WriteShort(Field4);
    }
    public override void Deserialize(BinReader s)
    {
        NpcUid = s.ReadInt();
        StoreId = s.ReadShort();
        if (s.Remaining >= 1) Flag = s.ReadByte();
        if (s.Remaining >= 2) Field4 = s.ReadShort();
    }
}

/// <summary>S→C NCID 308 — BuyUp : ack d'achat (corps VIDE natif —
/// « Serialize = return » dans ShopHandlers.cs).</summary>
public sealed class ZoneBuyUp : NetMessage
{
    public override int Opcode => 308;
    public override string Name => "Zone.ClientBuyUp";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>S→C NCID 393 — EnterTransport : liste des routes d'un
/// transporteur. DESASM — `[u16 n][n×s16 routeId][i32 npcUid]`
/// (Serialize @0x81e6e10). Les ids sont des aires destination
/// (t_node/T_Transport).</summary>
public sealed class ZoneEnterTransport : NetMessage
{
    public override int Opcode => 393;
    public override string Name => "Zone.ClientEnterTransport";
    /// <summary>Ids de routes (aires destination).</summary>
    public List<short> Routes { get; } = new();
    /// <summary>i32 — npcUid du transporteur.</summary>
    public int NpcUid { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Routes.Count);
        foreach (var r in Routes) s.WriteShort(r);
        s.WriteInt(NpcUid);
    }
    public override void Deserialize(BinReader s)
    {
        Routes.Clear();
        var n = s.ReadUShort();
        for (var i = 0; i < n && s.Remaining >= 2; i++)
            Routes.Add(s.ReadShort());
        if (s.Remaining >= 4) NpcUid = s.ReadInt();
    }
}

/// <summary>S→C NCID 357 — EnterStorage [i32 npcUid] : ouvre la
/// banque ; les items suivent via 358 (réponse au C→S 62).</summary>
public sealed class ZoneEnterStorage : NetMessage
{
    public override int Opcode => 357;
    public override string Name => "Zone.ClientEnterStorage";
    /// <summary>i32 — uid du NPC banquier.</summary>
    public int NpcUid { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(NpcUid);
    public override void Deserialize(BinReader s) => NpcUid = s.ReadInt();
}

/// <summary>Un conteneur (onglet) de banque du 358.</summary>
public sealed record StorageContainer(short Capacity, byte Flag,
    List<InventorySlot> Items);

/// <summary>S→C NCID 358 — GetStorageData : contenu de la banque.
/// INCOMPLET côté serveur — codec prouvé NativeItemCodec.
/// BuildGetStorageData (Serialize natif @0x82366b0) :
/// `[s16 type][u8 nContainers]` puis par conteneur
/// `[s16 capacity][u8 flag][s16 blobLen][bitmap MSB-first]`
/// + un item-record WriteItem par bit levé.</summary>
public sealed class ZoneClientGetStorageData : NetMessage
{
    public override int Opcode => 358;
    public override string Name => "Zone.ClientGetStorageData";
    /// <summary>s16 — écho du type de la requête 62.</summary>
    public short DataType { get; set; }
    /// <summary>Onglets de banque.</summary>
    public List<StorageContainer> Containers { get; } = new();

    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        Containers.Clear();
        if (s.Remaining < 3) return;
        DataType = s.ReadShort();
        var nContainers = s.ReadByte();
        for (var c = 0; c < nContainers; c++)
        {
            if (s.Remaining < 5) return;
            var cap = s.ReadShort();
            var flag = s.ReadByte();
            var blobLen = s.ReadShort();
            if (s.Remaining < blobLen) return;
            var bitmap = s.ReadBytes(blobLen);
            var items = new List<InventorySlot>();
            for (var slot = 0; slot < cap; slot++)
            {
                var byteIdx = slot / 8;
                if (byteIdx >= bitmap.Length) break;
                if ((bitmap[byteIdx] & (0x80 >> (slot & 7))) == 0)
                    continue;
                if (!NativeItemRecord.TryReadInventory(s, out var rec))
                    return;
                items.Add(new InventorySlot(c, slot, rec.ItemId,
                    rec.Durability, rec.MaxDurability, rec.Flags,
                    rec.Amount, rec.Strengthen));
            }
            Containers.Add(new StorageContainer(cap, flag, items));
        }
    }
}

// ---------- états agrégés pour le HUD ----------

/// <summary>Ligne d'un store PNJ (C_Store.ini + prix c_item).</summary>
public sealed record ShopEntry(int Index, int ItemId, int Count,
    int Price, string Name);

/// <summary>
/// Boutique PNJ ouverte (304 + C_Store.ini) — lu par la fenêtre shop
/// du HUD (F2) ; achat via <c>GameScene</c> → <see cref="ZoneBuyItems"/>.
/// </summary>
public static class ShopState
{
    /// <summary>Émis quand une boutique (ré)ouvre ou se ferme.</summary>
    public static event Action? Changed;

    /// <summary>Store courant (null = pas de boutique ouverte).</summary>
    public static int StoreId { get; private set; } = -1;
    /// <summary>NPC marchand courant.</summary>
    public static int NpcUid { get; private set; }
    /// <summary>True quand une boutique est ouverte.</summary>
    public static bool IsOpen => StoreId >= 0;

    private static readonly List<ShopEntry> _entries = new();
    /// <summary>Articles du store (nom EN résolu + prix c_item).</summary>
    public static IReadOnlyList<ShopEntry> Entries => _entries;

    /// <summary>Émis quand la file buyback change (512 flag 1/2/3).</summary>
    public static event Action? BuybackChanged;

    private static readonly List<ZoneClientBuyBackList.BuybackRecord>
        _buyback = new();
    /// <summary>Items rachetables (plus ancien → plus récent) —
    /// persiste pendant la session, comme la deque native
    /// (char+0x5d0) : PAS de clear à la fermeture de boutique.</summary>
    public static IReadOnlyList<ZoneClientBuyBackList.BuybackRecord>
        Buyback => _buyback;

    /// <summary>512 : met à jour la file buyback selon le flag
    /// (1 append, 2 retrait à l'index terminal, 3 ouverture — signal
    /// simple, aucun changement de contenu).</summary>
    public static void OnBuyBackList(ZoneClientBuyBackList m)
    {
        switch (m.Flag)
        {
            case ZoneClientBuyBackList.ContentFlag:
                _buyback.AddRange(m.Entries);
                break;
            case ZoneClientBuyBackList.RemoveFlag:
                if (m.RemovedIndex >= 0 &&
                    m.RemovedIndex < _buyback.Count)
                    _buyback.RemoveAt(m.RemovedIndex);
                break;
            // flag 3 = ouverture — le contenu client persiste (le
            // serveur n'envoie que le signal ; les rows arrivent par
            // les deltas flag=1 émis à chaque vente).
        }
        BuybackChanged?.Invoke();
    }

    /// <summary>304 : ouvre la boutique et charge le catalogue.</summary>
    public static void OnEnterShop(ZoneEnterShop m)
    {
        StoreId = m.StoreId;
        NpcUid = m.NpcUid;
        _entries.Clear();
        var store = Data.Tables.StoreTable.Get(m.StoreId);
        if (store != null)
        {
            for (var i = 0; i < store.Items.Count; i++)
            {
                var (itemId, _, count) = store.Items[i];
                var def = Data.Tables.ItemTableCache.Get(itemId);
                var name = Data.TextResolver.ItemName(itemId)
                    ?? def?.Name ?? $"#{itemId}";
                _entries.Add(new ShopEntry(i, itemId, count,
                    def?.SellPrice ?? 0, name));
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Ferme la boutique (LeaveShop 63 / sortie node).</summary>
    public static void Close()
    {
        if (!IsOpen) return;
        StoreId = -1;
        NpcUid = 0;
        _entries.Clear();
        Changed?.Invoke();
    }
}

/// <summary>Route de transport affichée (393 + T_Transport.ini).</summary>
public sealed record TransportRoute(int Index, short RouteId,
    string Destination);

/// <summary>
/// Fenêtre transport ouverte (393) — destinations du transporteur ;
/// sélection via <c>GameScene</c> → <see cref="ZoneTransport"/> (id de
/// route ou index — le serveur accepte les deux).
/// </summary>
public static class TransportState
{
    /// <summary>Émis quand la fenêtre (ré)ouvre ou se ferme.</summary>
    public static event Action? Changed;

    /// <summary>NPC transporteur.</summary>
    public static int NpcUid { get; private set; }
    /// <summary>True quand une liste de routes est affichée.</summary>
    public static bool IsOpen { get; private set; }

    private static readonly List<TransportRoute> _routes = new();
    /// <summary>Destinations proposées (nom EN T_Transport, sinon
    /// t_node, sinon #id).</summary>
    public static IReadOnlyList<TransportRoute> Routes => _routes;

    /// <summary>393 : recharge la liste de routes.</summary>
    public static void OnEnterTransport(ZoneEnterTransport m)
    {
        NpcUid = m.NpcUid;
        _routes.Clear();
        for (var i = 0; i < m.Routes.Count; i++)
        {
            var id = m.Routes[i];
            var name = Data.Tables.TransportTable.RouteName(id)
                ?? Data.TextResolver.NodeName(id)
                ?? $"#{id}";
            _routes.Add(new TransportRoute(i, id, name));
        }
        IsOpen = true;
        Changed?.Invoke();
    }

    /// <summary>Ferme la fenêtre (sélection faite / sortie node).</summary>
    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        NpcUid = 0;
        _routes.Clear();
        Changed?.Invoke();
    }
}

/// <summary>
/// Banque ouverte (357 + contenu 358) — lu par la fenêtre storage du
/// HUD (F1) ; dépôt/retrait via <see cref="ZoneStorageDeposit"/>/
/// <see cref="ZoneStorageWithdraw"/>.
/// </summary>
public static class StorageState
{
    /// <summary>Émis quand la banque (ré)ouvre ou que le 358 arrive.</summary>
    public static event Action? Changed;

    /// <summary>NPC banquier.</summary>
    public static int NpcUid { get; private set; }
    /// <summary>True quand la banque est ouverte.</summary>
    public static bool IsOpen { get; private set; }

    private static readonly List<StorageContainer> _containers = new();
    /// <summary>Onglets/items du dernier 358.</summary>
    public static IReadOnlyList<StorageContainer> Containers => _containers;
    /// <summary>Nombre total d'items (tous onglets).</summary>
    public static int TotalItems =>
        _containers.Sum(c => c.Items.Count);

    /// <summary>357 : ouvre la banque (items en attente du 358).</summary>
    public static void OnEnterStorage(ZoneEnterStorage m)
    {
        NpcUid = m.NpcUid;
        IsOpen = true;
        Changed?.Invoke();
    }

    /// <summary>358 : recharge le contenu.</summary>
    public static void OnStorageData(ZoneClientGetStorageData m)
    {
        _containers.Clear();
        _containers.AddRange(m.Containers);
        Changed?.Invoke();
    }

    /// <summary>Ferme la banque (sortie node / fermeture fenêtre).</summary>
    public static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        NpcUid = 0;
        _containers.Clear();
        Changed?.Invoke();
    }

    /// <summary>Ré-émet <see cref="Changed"/> avec le même contenu —
    /// utilisé par la bascule de vue banque classique ↔ neobank
    /// (<see cref="NeobankSettings.Enabled"/>) : seuls les
    /// présentateurs changent, pas les données.</summary>
    public static void NotifyChanged() => Changed?.Invoke();
}
