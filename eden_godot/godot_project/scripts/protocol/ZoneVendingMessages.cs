using EdenClient.Net;

namespace EdenClient.Protocol;

// Étal joueur (market booth / player vending) — NCIDs C→S 71-80
// (seuls 72/74/75/76/77 ont un effet) et S→C 375-379.
//
// Sources de preuve :
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.Vending.cs — routing
//     (71 no-op, 72 ouvre la vue, 74 ferme l'étal, 75 ferme la vue,
//     76 achat, 77 ouverture/remplissage, 78/80 no-ops natifs) ;
//   - $EDEN/server/src/Eden.Zone/Codecs/VendorPackets.cs — wire
//     exact des 76/77 entrants et 375-379 sortants, porté du désasm
//     natif (ClientVendorUpdateView::Serialize @0x8134ad0 — record
//     goods de 98 octets, ordre permuté des train-slots) ;
//   - zone/decompiled.c : CPlayerFactory::VendorSellItem @0x81145e0
//     (remplissage SStallGoodsInfo), VendorBuy @0x80f9770.
//
// Rappels structurels : les marchandises RESTENT dans le sac du
// vendeur — l'étal n'est que des métadonnées (container+slot) ;
// l'achat porte sur le lot complet du slot (le serveur exige
// qty == count). Régions 600-609 (S001 en définit 600/601/602 —
// SceneTable.StallRegions côté client).
//
// ÉCARTS connus du serveur C# actuel vs natif (documentés, tolérés) :
//   - goods.LocIndex (+0x20) : natif = clone+0xc = quantité du lot ;
//     le C# y met item.Slot (index de sac) ;
//   - goods +0x56 : natif = clone+0x4c = embedded_amount (qty stack) ;
//     le C# y met la colonne witchcraft (0 le plus souvent) ;
//   → la quantité de lot n'est PAS fiable sur le fil C# : le client
//   envoie qty = max(1, EmbeddedAmount) — correct pour les items
//   non-empilables (Quantity=1), rejeté par le serveur pour les
//   stacks tant que le champ n'est pas rempli correctement.

// ============================================================
// C→S
// ============================================================

/// <summary>C→S NCID 74 — VendorClose `[u8 mode][u8 ignoré]` : le
/// vendeur ferme son étal (réponse = 375 au vendeur, 376 + texte
/// 0xf07 aux viewers). Le natif lit deux u8 et n'utilise que le
/// premier (VendorPackets.ParseClose). CNE_CZ_ZoneServerVendorClose.
/// </summary>
public sealed class ZoneVendorClose : NetMessage
{
    public override int Opcode => 74;
    public override string Name => "Zone.ZoneServerVendorClose";
    /// <summary>u8 — mode de fermeture (recopié dans le 375).</summary>
    public byte Mode { get; set; }
    /// <summary>u8 — second octet natif ignoré.</summary>
    public byte Ignored { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Mode);
        s.WriteByte(Ignored);
    }
    public override void Deserialize(BinReader s)
    {
        Mode = s.ReadByte();
        if (s.Remaining >= 1) Ignored = s.ReadByte();
    }
}

/// <summary>C→S NCID 75 — VendorCloseView `[i32 vendeur]` : l'acheteur
/// ferme la vue de l'étal (reset op côté serveur, pas de réponse).
/// CNE_CZ_ZoneServerVendorCloseView.</summary>
public sealed class ZoneVendorCloseView : NetMessage
{
    public override int Opcode => 75;
    public override string Name => "Zone.ZoneServerVendorCloseView";
    /// <summary>i32 — charId du vendeur dont on ferme la vue.</summary>
    public int SellerId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(SellerId);
    public override void Deserialize(BinReader s) => SellerId = s.ReadInt();
}

/// <summary>C→S NCID 76 — VendorBuy `[i32 vendeur][u16 n][n×(s16 qty,
/// u8 idx)]` : achat de lignes de l'étal. qty = lot complet (le
/// serveur compare qty == quantity du slot live — voir en-tête).
/// CNE_CZ_ZoneServerVendorBuy.</summary>
public sealed class ZoneVendorBuy : NetMessage
{
    public override int Opcode => 76;
    public override string Name => "Zone.ZoneServerVendorBuy";
    /// <summary>Une ligne d'achat.</summary>
    /// <param name="Quantity">s16 — quantité (= lot complet).</param>
    /// <param name="Index">u8 — index de la marchandise (clé map).</param>
    public readonly record struct BuyLine(short Quantity, byte Index);
    /// <summary>i32 — charId du vendeur.</summary>
    public int SellerId { get; set; }
    /// <summary>Lignes achetées.</summary>
    public List<BuyLine> Lines { get; } = new();
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(SellerId);                       // i32 — vendeur
        s.WriteShort((short)Lines.Count);           // u16 — nb lignes
        foreach (var l in Lines)
        {
            s.WriteShort(l.Quantity);               // s16 — quantité
            s.WriteByte(l.Index);                   // u8 — index bien
        }
    }
    public override void Deserialize(BinReader s)
    {
        Lines.Clear();
        SellerId = s.ReadInt();
        var n = s.ReadUShort();
        for (var i = 0; i < n && s.Remaining >= 3; i++)
            Lines.Add(new BuyLine(s.ReadShort(), s.ReadByte()));
    }
}

/// <summary>C→S NCID 77 — VendorSellItem `[i32 région][str titre]
/// [u8 n][n×(s16 conteneur, s16 slot, s16 loc, i32 prix)]` :
/// ouverture ou re-remplissage de l'étal (les items restent dans le
/// sac — conteneur+slot pointent dessus ; loc = clone+0xc attendu
/// côté validation native, ignoré par le serveur C#).
/// CNE_CZ_ZoneServerVendorSellItem.</summary>
public sealed class ZoneVendorSellItem : NetMessage
{
    public override int Opcode => 77;
    public override string Name => "Zone.ZoneServerVendorSellItem";
    /// <summary>Une marchandise mise en vente.</summary>
    /// <param name="Container">s16 — container_index (bag).</param>
    /// <param name="Slot">s16 — slot dans le conteneur.</param>
    /// <param name="Loc">s16 — loc attendu (clone+0xc — validation
    /// native seule, non recopiée serveur).</param>
    /// <param name="Price">i32 — prix du lot entier.</param>
    public readonly record struct SellEntry(short Container,
        short Slot, short Loc, int Price);
    /// <summary>i32 — type de région d'étal (600-609 ; S001 = 600/601/602).</summary>
    public int RegionType { get; set; }
    /// <summary>str — titre de l'étal (≤ 91 octets Big5, non vide).</summary>
    public string Title { get; set; } = "";
    /// <summary>Lignes de marchandise.</summary>
    public List<SellEntry> Entries { get; } = new();
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(RegionType);                     // i32 — région
        s.WriteString(Title);                       // str — titre
        s.WriteByte((byte)Entries.Count);           // u8 — nb lignes
        foreach (var e in Entries)
        {
            s.WriteShort(e.Container);              // s16 — conteneur
            s.WriteShort(e.Slot);                   // s16 — slot
            s.WriteShort(e.Loc);                    // s16 — loc attendu
            s.WriteInt(e.Price);                    // i32 — prix du lot
        }
    }
    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        RegionType = s.ReadInt();
        Title = s.ReadString();
        var n = s.ReadByte();
        for (var i = 0; i < n && s.Remaining >= 10; i++)
            Entries.Add(new SellEntry(s.ReadShort(), s.ReadShort(),
                s.ReadShort(), s.ReadInt()));
    }
}

// ============================================================
// S→C
// ============================================================

/// <summary>S→C NCID 375 — ClientVendorClose `[i32 charId][u8 mode]` :
/// confirmé au vendeur à la fermeture de son étal. Également émis
/// avec (0,0) en ACK du LeaveShop PNJ (ShopHandlers — ignoré si
/// aucun étal ouvert). CNC_CZ_ClientVendorClose.</summary>
public sealed class ZoneClientVendorClose : NetMessage
{
    public override int Opcode => 375;
    public override string Name => "Zone.ClientVendorClose";
    /// <summary>i32 — charId du vendeur (0 en ACK LeaveShop).</summary>
    public int CharId { get; set; }
    /// <summary>u8 — mode de fermeture (écho du C→S 74).</summary>
    public byte Mode { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId);
        s.WriteByte(Mode);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt();
        if (s.Remaining >= 1) Mode = s.ReadByte();
    }
}

/// <summary>S→C NCID 376 — ClientVendorCloseView `[i32 vendeur]` :
/// l'étal qu'on regardait vient de fermer (accompagné du texte
/// 0xf07 « The seller has closed the stand. »).
/// CNC_CZ_ClientVendorCloseView.</summary>
public sealed class ZoneClientVendorCloseView : NetMessage
{
    public override int Opcode => 376;
    public override string Name => "Zone.ClientVendorCloseView";
    /// <summary>i32 — charId du vendeur.</summary>
    public int SellerId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(SellerId);
    public override void Deserialize(BinReader s) => SellerId = s.ReadInt();
}

/// <summary>S→C NCID 377 — ClientVendorStatus `[u8 action][u8 detail]` :
/// statut d'étal. Codes natifs (VendorSellItem désasm) : [1][2] =
/// étal ouvert/vue ouverte ; [0][0x11] occupé/état, [0][0x12] trop
/// tôt (rate-limit 1,5 s), [0][0x13] pas la bonne map, [0][0x14]
/// titre vide, [0][0x15] doublon de bien, [0][0x16] trop d'items
/// gratuits. CNC_CZ_ClientVendorStatus.</summary>
public sealed class ZoneClientVendorStatus : NetMessage
{
    public override int Opcode => 377;
    public override string Name => "Zone.ClientVendorStatus";
    /// <summary>u8 — action (0 échec, 1 succès).</summary>
    public byte Action { get; set; }
    /// <summary>u8 — détail/code d'erreur.</summary>
    public byte Detail { get; set; }
    /// <summary>Vrai si succès ([1][2] natif ; [1][3] = vue acheteur
    /// chez le client Reforged).</summary>
    public bool IsSuccess => Action == 1;
    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Action);
        s.WriteByte(Detail);
    }
    public override void Deserialize(BinReader s)
    {
        Action = s.ReadByte();
        if (s.Remaining >= 1) Detail = s.ReadByte();
    }
}

/// <summary>Une marchandise du 378 — record natif de 98 octets
/// (noeud de la map&lt;char, SStallGoodsInfo&gt; du vendeur, clé
/// u8 triée — <see cref="ZoneClientVendorUpdateView"/>).</summary>
public sealed record VendorGood
{
    /// <summary>u8 — index/clé de la ligne (u8 trié).</summary>
    public byte Index;
    /// <summary>u32 — item_id (itemdata+4).</summary>
    public uint ItemId;
    /// <summary>i32 — niveau requis (itemdata+0x3e).</summary>
    public int RequiredLevel;
    /// <summary>s16 — conteneur du slot live vendeur.</summary>
    public short Container;
    /// <summary>s16 — slot dans le conteneur (loc index).</summary>
    public short Slot;
    /// <summary>i32 — clone+0xc : natif = quantité du lot ; le
    /// serveur C# y met l'index de sac.</summary>
    public int LocIndex;
    /// <summary>i32 — clone+0xe : natif = durabilité max (stale-check
    /// achat) ; le serveur C# émet 0.</summary>
    public int LocExtra;
    /// <summary>u16 — clone+0x1c (durabilité courante).</summary>
    public ushort Durability;
    /// <summary>s16 — clone+0x10 (combo_id).</summary>
    public short Combo;
    /// <summary>u8 — clone+0x24 &amp; 7 (flags bas).</summary>
    public byte FlagLow3;
    /// <summary>s16×6 — ids d'enchantements sertis (vec +0x40).</summary>
    public short[] EmbeddedIds = new short[6];
    /// <summary>u16×9 — extra combos 0-8 (vec +0x34).</summary>
    public ushort[] ExtraCombos = new ushort[9];
    /// <summary>i32 — prix du lot entier.</summary>
    public int Price;
    /// <summary>s16 — clone+0x2c (qualité).</summary>
    public short Quality;
    /// <summary>s16 — clone+0x4c : natif = embedded_amount (quantité
    /// de stack) ; le serveur C# y met la colonne witchcraft.</summary>
    public short EmbeddedAmount;
    /// <summary>s16 — clone+0x2e (champ d'identification).</summary>
    public short Clone2e;
    /// <summary>u16 — clone+0x58 (niveau de train d'équipement).</summary>
    public ushort TrainLevel;
    /// <summary>u32 — clone+0x5c (due date).</summary>
    public uint DueDate;
    /// <summary>u16×6 — slots de train, ORDRE WIRE natif permuté
    /// (0,3,1,4,2,5 — stockés tels quels).</summary>
    public ushort[] TrainSlots = new ushort[6];
    /// <summary>u16 — extra combo 9 (après les train-slots, ordre
    /// non contigu natif).</summary>
    public ushort Extra9;
    /// <summary>u16 — extra combo 10.</summary>
    public ushort Extra10;
    /// <summary>s16×3 — enchantements magic-box (clone+0x70).</summary>
    public short[] MagicBox = new short[3];
    /// <summary>u16 — clone+0x7c (énergie magic-box).</summary>
    public ushort MagicBoxEnergy;
    /// <summary>u16 — clone+0x7e (niveau étoile magic-box).</summary>
    public ushort MagicBoxStar;

    /// <summary>Quantité de lot affichable/offerte — la meilleure
    /// approximation wire : max(1, EmbeddedAmount) (voir en-tête :
    /// non fiable sur le serveur C# actuel).</summary>
    public int LotQuantity => Math.Max(1, (int)EmbeddedAmount);
}

/// <summary>S→C NCID 378 — ClientVendorUpdateView `[i32 vendeur]
/// [str titre][u8 n][n×98B]` : contenu complet d'un étal — émis à
/// l'ouverture de vue (72), au remplissage (77) et après chaque
/// achat (76) au vendeur + viewers. Codec porté de
/// VendorPackets.BuildUpdateView (désasm Serialize @0x8134ad0).
/// CNC_CZ_ClientVendorUpdateView.</summary>
public sealed class ZoneClientVendorUpdateView : NetMessage
{
    /// <summary>Taille exacte d'un record goods — 98 octets.</summary>
    public const int GoodsRecordBytes = 98;
    public override int Opcode => 378;
    public override string Name => "Zone.ClientVendorUpdateView";
    /// <summary>i32 — charId du vendeur.</summary>
    public int SellerId { get; set; }
    /// <summary>str — titre de l'étal.</summary>
    public string Title { get; set; } = "";
    /// <summary>Marchandises (ordre des clés u8 du serveur).</summary>
    public List<VendorGood> Goods { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(SellerId);
        s.WriteString(Title);
        s.WriteByte((byte)Goods.Count);
        foreach (var g in Goods)
        {
            s.WriteByte(g.Index);
            s.WriteInt((int)g.ItemId);
            s.WriteInt(g.RequiredLevel);
            s.WriteShort(g.Container);
            s.WriteShort(g.Slot);
            s.WriteInt(g.LocIndex);
            s.WriteInt(g.LocExtra);
            s.WriteShort((short)g.Durability);
            s.WriteShort(g.Combo);
            s.WriteByte(g.FlagLow3);
            for (var i = 0; i < 6; i++) s.WriteShort(g.EmbeddedIds[i]);
            for (var i = 0; i < 9; i++) s.WriteShort((short)g.ExtraCombos[i]);
            s.WriteInt(g.Price);
            s.WriteShort(g.Quality);
            s.WriteShort(g.EmbeddedAmount);
            s.WriteShort(g.Clone2e);
            s.WriteShort((short)g.TrainLevel);
            s.WriteInt((int)g.DueDate);
            for (var i = 0; i < 6; i++) s.WriteShort((short)g.TrainSlots[i]);
            s.WriteShort((short)g.Extra9);
            s.WriteShort((short)g.Extra10);
            for (var i = 0; i < 3; i++) s.WriteShort(g.MagicBox[i]);
            s.WriteShort((short)g.MagicBoxEnergy);
            s.WriteShort((short)g.MagicBoxStar);
        }
    }

    public override void Deserialize(BinReader s)
    {
        Goods.Clear();
        if (s.Remaining < 4) return;
        SellerId = s.ReadInt();
        Title = s.ReadString();
        if (s.Remaining < 1) return;
        var n = s.ReadByte();
        for (var i = 0; i < n && s.Remaining >= GoodsRecordBytes; i++)
            Goods.Add(ReadGood(s));
    }

    /// <summary>Lit un record goods de 98 octets (ordre wire exact
    /// du Serialize natif — voir <see cref="VendorGood"/>).</summary>
    private static VendorGood ReadGood(BinReader s)
    {
        var g = new VendorGood
        {
            Index = s.ReadByte(),
            ItemId = s.ReadUInt(),
            RequiredLevel = s.ReadInt(),
            Container = s.ReadShort(),
            Slot = s.ReadShort(),
            LocIndex = s.ReadInt(),
            LocExtra = s.ReadInt(),
            Durability = s.ReadUShort(),
            Combo = s.ReadShort(),
            FlagLow3 = s.ReadByte(),
        };
        for (var i = 0; i < 6; i++) g.EmbeddedIds[i] = s.ReadShort();
        for (var i = 0; i < 9; i++) g.ExtraCombos[i] = s.ReadUShort();
        g.Price = s.ReadInt();
        g.Quality = s.ReadShort();
        g.EmbeddedAmount = s.ReadShort();
        g.Clone2e = s.ReadShort();
        g.TrainLevel = s.ReadUShort();
        g.DueDate = s.ReadUInt();
        for (var i = 0; i < 6; i++) g.TrainSlots[i] = s.ReadUShort();
        g.Extra9 = s.ReadUShort();
        g.Extra10 = s.ReadUShort();
        for (var i = 0; i < 3; i++) g.MagicBox[i] = s.ReadShort();
        g.MagicBoxEnergy = s.ReadUShort();
        g.MagicBoxStar = s.ReadUShort();
        return g;
    }
}

/// <summary>S→C NCID 379 — ClientVendorContainer : réponse d'échec
/// de la requête 72 (cible absente — VendorPackets.BuildContainerEmpty
/// `[i32 cible][u8 0]` ; le serveur C# actuel ne l'émet pas — retour
/// silencieux). Tolère les deux formes ([i32][u8] et [u8] généré).
/// CNC_CZ_ClientVendorContainer.</summary>
public sealed class ZoneClientVendorContainer : NetMessage
{
    public override int Opcode => 379;
    public override string Name => "Zone.ClientVendorContainer";
    /// <summary>i32 — charId de la cible demandée.</summary>
    public int TargetId { get; set; }
    /// <summary>u8 — nb d'entrées (0 = échec).</summary>
    public byte Count { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(TargetId);
        s.WriteByte(Count);
    }
    public override void Deserialize(BinReader s)
    {
        // Forme longue [i32][u8] (BuildContainerEmpty) ou courte
        // [u8] (classe générée aplatie).
        if (s.Remaining >= 5)
        {
            TargetId = s.ReadInt();
            Count = s.ReadByte();
        }
        else if (s.Remaining >= 1)
        {
            Count = s.ReadByte();
        }
    }
}

// ============================================================
// État agrégé pour la fenêtre
// ============================================================

/// <summary>Ligne affichée d'une marchandise d'étal (vue 378 ou
/// staging local de vente).</summary>
public sealed record StallEntry(int Index, int ItemId, int Price,
    int Quantity, short Container, short Slot,
    InventorySlot? SourceItem);

/// <summary>
/// État courant de l'étal joueur — alimenté par les binds réseau de
/// <c>VendingWindow</c> et lu par l'UI (même convention que
/// <see cref="TradeState"/>/<see cref="ShopState"/> : mutations déjà
/// sur le thread principal).
///
/// Deux modes exclusifs : <see cref="Viewing"/> (je regarde l'étal
/// d'un autre — réponses aux 72/76/75) et <see cref="Selling"/> (mon
/// étal est posé côté serveur — réponse au 77, fermeture au 74/375).
/// Le staging de vente (<see cref="StageItem"/>) est purement local
/// jusqu'à l'envoi du 77.
/// </summary>
public static class VendingState
{
    /// <summary>Émis à chaque mutation (vue, statut, staging) — levé
    /// sur le thread principal via <see cref="RaiseChanged"/>.</summary>
    public static event Action? Changed;

    /// <summary>Verrou du store — les mutations réseau (375-378) sont
    /// appliquées directement sur le thread de lecture (les files UI
    /// peuvent geler ~10 s pendant une migration : constat
    /// --vending-test, le 377 arrivait avant les binds de la
    /// fenêtre). Seul l'événement Changed reste posté.</summary>
    private static readonly object _gate = new();

    /// <summary>charId de MON personnage — posé par la fenêtre au
    /// bind (SessionCarry.Character?.Id) ; sert à distinguer un 378
    /// « mon étal » (mise à jour vendeur après achat) d'un 378
    /// « étal regardé ».</summary>
    public static uint SelfCharId { get; set; }
    /// <summary>Vrai quand la vue d'un étal tiers est ouverte (378
    /// reçu après un 72 — voir <see cref="MarkViewRequested"/>).</summary>
    public static bool Viewing { get; private set; }
    /// <summary>charId du vendeur dont on a reçu la dernière vue.</summary>
    public static int SellerId { get; private set; }
    /// <summary>Titre de l'étal de la dernière vue.</summary>
    public static string Title { get; private set; } = "";
    /// <summary>Marchandises de la dernière vue (snapshot).</summary>
    public static IReadOnlyList<VendorGood> Goods
    {
        get { lock (_gate) return _goods.ToArray(); }
    }
    private static readonly List<VendorGood> _goods = new();

    /// <summary>72 émis vers cet étal — la prochaine 378/377 [1][2]
    /// ouvre le mode achat (le 378 seul ne suffit pas à distinguer :
    /// le vendeur en reçoit aussi après chaque vente).</summary>
    private static int _pendingViewSeller;
    /// <summary>77 émis en attente du statut d'ouverture.</summary>
    private static bool _pendingSell;

    /// <summary>À appeler juste avant l'envoi du 72.</summary>
    public static void MarkViewRequested(int sellerId)
    {
        lock (_gate) _pendingViewSeller = sellerId;
    }
    /// <summary>À appeler juste avant l'envoi du 77.</summary>
    public static void MarkSellRequested()
    {
        lock (_gate) _pendingSell = true;
    }
    /// <summary>Vrai quand la dernière vue concerne MON étal
    /// (SellerId == SelfCharId) — panneau vendeur.</summary>
    public static bool IsSelfStall =>
        SelfCharId != 0 && SellerId == (int)SelfCharId;

    /// <summary>Vrai quand MON étal est posé côté serveur (statut
    /// [1][2] reçu après un 77).</summary>
    public static bool Selling { get; private set; }
    /// <summary>Titre en cours de saisie pour mon étal.</summary>
    public static string MyTitle { get; set; } = "";
    /// <summary>Items mis en vente localement (staging — envoyés au
    /// 77 ; le serveur renvoie la vue complète au succès). Snapshot.</summary>
    public static IReadOnlyList<StallEntry> StagedItems
    {
        get { lock (_gate) return _staged.ToArray(); }
    }
    private static readonly List<StallEntry> _staged = new();
    /// <summary>Dernier statut 377 reçu (action, detail) — affichage
    /// d'erreur éventuel dans la fenêtre.</summary>
    public static (byte Action, byte Detail)? LastStatus { get; private set; }

    /// <summary>Lève <see cref="Changed"/> — à poster sur le thread
    /// principal par les handlers réseau.</summary>
    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>378 — (re)charge la vue d'un étal, données seules
    /// (thread réseau). Reçue par le viewer (réponse au 72) ET par le
    /// vendeur + tous les viewers après chaque achat
    /// (BroadcastUpdateViewAsync). Une vue dont SellerId ==
    /// SelfCharId est la mise à jour de MON étal — elle ne bascule
    /// pas en mode achat mais resynchronise les marchandises vendeur
    /// (un item vendu disparaît).</summary>
    public static void ApplyUpdateViewData(ZoneClientVendorUpdateView m)
    {
        lock (_gate)
        {
            var mine = SelfCharId != 0 && m.SellerId == (int)SelfCharId;
            if (mine)
            {
                // Resync vendeur : les lots restants remplacent le
                // staging (prix/index conservés par le record).
                _staged.Clear();
                foreach (var g in m.Goods)
                    _staged.Add(new StallEntry(g.Index, (int)g.ItemId,
                        g.Price, g.LotQuantity, g.Container, g.Slot,
                        null));
                SellerId = m.SellerId;
                Title = m.Title;
                return;
            }
            SellerId = m.SellerId;
            Title = m.Title;
            _goods.Clear();
            _goods.AddRange(m.Goods);
            // Vue ouverte : explicitement demandée (72) ou déjà en
            // vue du même étal (rafraîchissement post-achat).
            if (_pendingViewSeller == m.SellerId ||
                (Viewing && SellerId == m.SellerId))
            {
                Viewing = true;
                _pendingViewSeller = 0;
            }
        }
    }

    /// <summary>377 — statut, données seules (thread réseau) :
    /// [1][2] après un 77 = mon étal ouvert ; [1][2] après un 72 =
    /// vue ouverte (la 378 suit) ; [0][code] = échec mémorisé.</summary>
    public static void ApplyStatusData(ZoneClientVendorStatus m)
    {
        lock (_gate)
        {
            LastStatus = (m.Action, m.Detail);
            if (m.IsSuccess)
            {
                if (_pendingSell)
                {
                    _pendingSell = false;
                    // Ouverture de MON étal confirmée — le staging
                    // est la marchandise jusqu'au premier 378.
                    Selling = true;
                    SellerId = (int)SelfCharId;
                    Title = MyTitle;
                }
                // Vue (72) — la 378 qui suit confirme l'ouverture.
            }
            else
            {
                _pendingSell = false;
                _pendingViewSeller = 0;
            }
        }
    }

    /// <summary>375 — fermeture confirmée de l'étal, données seules
    /// (mode vendeur ; (0,0) = ACK LeaveShop PNJ → ignoré hors
    /// Selling).</summary>
    public static void ApplyCloseData(ZoneClientVendorClose m)
    {
        lock (_gate)
        {
            if (!Selling && m.CharId == 0) return;  // ACK shop PNJ
            if (m.CharId != 0 && m.CharId != (int)SelfCharId &&
                !Selling)
                return;
            Selling = false;
            _staged.Clear();
        }
    }

    /// <summary>376 — l'étal regardé a fermé, données seules.</summary>
    public static void ApplyCloseViewData(ZoneClientVendorCloseView m)
    {
        lock (_gate)
        {
            if (!Viewing || m.SellerId != SellerId) return;
            Viewing = false;
            _goods.Clear();
        }
    }

    /// <summary>378 — vue rechargée + notification (appelant déjà
    /// sur le thread principal).</summary>
    public static void ApplyUpdateView(ZoneClientVendorUpdateView m)
    {
        ApplyUpdateViewData(m);
        RaiseChanged();
    }

    /// <summary>377 — statut + notification.</summary>
    public static void ApplyStatus(ZoneClientVendorStatus m)
    {
        ApplyStatusData(m);
        RaiseChanged();
    }

    /// <summary>375 — fermeture + notification.</summary>
    public static void ApplyClose(ZoneClientVendorClose m)
    {
        ApplyCloseData(m);
        RaiseChanged();
    }

    /// <summary>376 — vue fermée + notification.</summary>
    public static void ApplyCloseView(ZoneClientVendorCloseView m)
    {
        ApplyCloseViewData(m);
        RaiseChanged();
    }

    /// <summary>Fermeture locale de la vue (75 émis) ou abandon.</summary>
    public static void CloseView()
    {
        lock (_gate)
        {
            if (!Viewing) return;
            Viewing = false;
            _pendingViewSeller = 0;
            _goods.Clear();
        }
        RaiseChanged();
    }

    /// <summary>Ajoute un item du sac au staging de vente — refusé
    /// si déjà présent (doublon refusé 0x15 côté serveur).</summary>
    public static bool StageItem(InventorySlot it, int price)
    {
        lock (_gate)
        {
            if (_staged.Any(e => e.Container == it.Bag &&
                e.Slot == it.Slot))
                return false;
            _staged.Add(new StallEntry(_staged.Count, it.ItemId, price,
                Math.Max(1, it.Amount), (short)it.Bag, (short)it.Slot,
                it));
        }
        RaiseChanged();
        return true;
    }

    /// <summary>Retire une ligne du staging.</summary>
    public static void UnstageItem(int index)
    {
        lock (_gate)
        {
            if (index < 0 || index >= _staged.Count) return;
            _staged.RemoveAt(index);
            for (var i = 0; i < _staged.Count; i++)
                _staged[i] = _staged[i] with { Index = i };
        }
        RaiseChanged();
    }

    /// <summary>Vide le staging (étal ouvert/annulé).</summary>
    public static void ClearStaged()
    {
        lock (_gate)
        {
            if (_staged.Count == 0) return;
            _staged.Clear();
        }
        RaiseChanged();
    }

    /// <summary>Réinitialise tout (déconnexion / changement de map).</summary>
    public static void Close()
    {
        lock (_gate)
        {
            Viewing = Selling = false;
            SellerId = 0;
            Title = "";
            _pendingViewSeller = 0;
            _pendingSell = false;
            _goods.Clear();
            _staged.Clear();
            LastStatus = null;
        }
        RaiseChanged();
    }
}
