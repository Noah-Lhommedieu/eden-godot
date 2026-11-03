using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages de l'hôtel des ventes — service NPC type 18 (enchérisseur).
// Contrairement au courrier, les enchères sont 100% ZONE-LOCALES :
// ShopHandlers.cs + AuctionService + AuctionRepository (SQL
// active_auctions) — aucun relais mission. La livraison des items
// achetés/annulés/expirés se fait par courrier (MailRepository), donc
// la MailWindow est aussi l'UI de livraison des enchères.
//
// Sources de preuve :
//   - $EDEN/server/.../Eden.Zone/Handlers/ShopHandlers.cs:285-427
//     (handlers C→S 114-118 + MakeAuctionEntry/MakeBuyEntry — seuls
//     AuctionId/ItemId/Price/SellerId/ExpireTime sont remplis, le reste
//     du record natif reste à zéro) ;
//   - $EDEN/server/.../Eden.Zone/ZoneEntryFlow.Dialogue.cs
//     service 18 → 430 ClientAuctionHouse `[i32 npcUid]` ;
//   - $EDEN/server/.../Generated/ZoneMessages.g.cs :
//     CNC_CZ_ClientAuctionBuyList::Deserialize (:2421) /
//     ClientAuctionSellList (:2549+) — même record ~90B ;
//     CNE_CZ_ZoneServerAuction{BuyQuery,Buy,Cancel,Sell,SellQuery}
//     (:19474-19623) ;
//   - natif rf_game : `AuctionWnd` — onglets Parcourir/Mes ventes.
//
// Résidus assumés :
//   - Pas d'enchère progressive : BuyAsync règle BuyoutPrice ?? Price —
//     un seul bouton « Acheter » (115), pas de bid.
//   - Les filtres du 114 (catégorie/prix/niveau/nom) sont IGNORÉS par le
//     serveur (Auctions.Search() sans args) — filtres locaux seulement.
//   - La quantité n'est PAS dans le record — non affichée.
//   - Durée fixe 48 h côté serveur — pas de sélecteur de durée.

// ---------------------------------------------------------------- C→S ---

/// <summary>C→S NCID 114 — AuctionBuyQuery `[s16][s16][i32][i32][i32]
/// [s16×4][str][s16]` — demande le catalogue des annonces actives.
/// Champs natifs = filtres de recherche (catégorie, fourchette prix,
/// niveau, texte, page) ; le serveur C# les ignore tous
/// (ShopHandlers.cs:420 → Search() non filtré) — on envoie les
/// défauts et on filtre le nom côté client.</summary>
public sealed class ZoneAuctionBuyQuery : NetMessage
{
    public override int Opcode => 114;
    public override string Name => "Zone.AuctionBuyQuery";
    /// <summary>s16 — catégorie native (0 = toutes ; ignoré serveur).</summary>
    public short Category { get; set; }
    /// <summary>s16 — sous-catégorie native (ignoré serveur).</summary>
    public short SubCategory { get; set; }
    /// <summary>i32 — prix mini natif (ignoré serveur).</summary>
    public int MinPrice { get; set; }
    /// <summary>i32 — prix maxi natif (ignoré serveur).</summary>
    public int MaxPrice { get; set; }
    /// <summary>i32 — filtre natif (rareté/classe ; ignoré serveur).</summary>
    public int Filter { get; set; }
    /// <summary>s16×4 — filtres natifs divers (ignorés serveur).</summary>
    public short F6, F7, F8, F9;
    /// <summary>str — texte de recherche natif (ignoré serveur ;
    /// le filtre nom est refait côté client).</summary>
    public string SearchText { get; set; } = "";
    /// <summary>s16 — page native (ignoré serveur).</summary>
    public short Page { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Category); s.WriteShort(SubCategory);
        s.WriteInt(MinPrice); s.WriteInt(MaxPrice); s.WriteInt(Filter);
        s.WriteShort(F6); s.WriteShort(F7); s.WriteShort(F8);
        s.WriteShort(F9); s.WriteString(SearchText); s.WriteShort(Page);
    }
    public override void Deserialize(BinReader s)
    {
        Category = s.ReadShort(); SubCategory = s.ReadShort();
        MinPrice = s.ReadInt(); MaxPrice = s.ReadInt(); Filter = s.ReadInt();
        F6 = s.ReadShort(); F7 = s.ReadShort(); F8 = s.ReadShort();
        F9 = s.ReadShort(); SearchText = s.ReadString(); Page = s.ReadShort();
    }
}

/// <summary>C→S NCID 115 — AuctionBuy `[i32 auctionId]` :
/// achat direct (buyout ?? price côté serveur ; l'item arrive PAR
/// COURRIER — SettleAuctionPurchaseAsync).</summary>
public sealed class ZoneAuctionBuy : NetMessage
{
    public override int Opcode => 115;
    public override string Name => "Zone.AuctionBuy";
    /// <summary>i32 — id de l'annonce (active_auctions.id).</summary>
    public int AuctionId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(AuctionId);
    public override void Deserialize(BinReader s) => AuctionId = s.ReadInt();
}

/// <summary>C→S NCID 116 — AuctionSellQuery `[s16][s16]` :
/// demande mes annonces actives (solde aussi les annonces expirées
/// du vendeur → retour mail).</summary>
public sealed class ZoneAuctionSellQuery : NetMessage
{
    public override int Opcode => 116;
    public override string Name => "Zone.AuctionSellQuery";
    /// <summary>s16 — champ natif (0 ; ignoré serveur).</summary>
    public short Field1 { get; set; }
    /// <summary>s16 — champ natif (0 ; ignoré serveur).</summary>
    public short Field2 { get; set; }
    public override void Serialize(BinWriter s)
    { s.WriteShort(Field1); s.WriteShort(Field2); }
    public override void Deserialize(BinReader s)
    { Field1 = s.ReadShort(); Field2 = s.ReadShort(); }
}

/// <summary>C→S NCID 117 — AuctionSell `[s16 bag][s16 slot][i32 price]
/// [i32 buyout]` : met en vente l'item du sac (stack complet pour un
/// consommable, 1 pour un équipement ; frais dépôt 5 %, durée 48 h
/// fixe — ShopHandlers.cs:287).</summary>
public sealed class ZoneAuctionSell : NetMessage
{
    public override int Opcode => 117;
    public override string Name => "Zone.AuctionSell";
    /// <summary>s16 — container inventaire (PlayerBag = 0).</summary>
    public short Bag { get; set; }
    /// <summary>s16 — slot inventaire.</summary>
    public short Slot { get; set; }
    /// <summary>i32 — prix de départ (min 1 côté serveur).</summary>
    public int Price { get; set; }
    /// <summary>i32 — prix d'achat immédiat (0 = pas de buyout).</summary>
    public int Buyout { get; set; }
    public override void Serialize(BinWriter s)
    { s.WriteShort(Bag); s.WriteShort(Slot); s.WriteInt(Price); s.WriteInt(Buyout); }
    public override void Deserialize(BinReader s)
    { Bag = s.ReadShort(); Slot = s.ReadShort(); Price = s.ReadInt(); Buyout = s.ReadInt(); }
}

/// <summary>C→S NCID 118 — AuctionCancel `[i32 auctionId]` :
/// annule une de mes annonces (item rendu PAR COURRIER).</summary>
public sealed class ZoneAuctionCancel : NetMessage
{
    public override int Opcode => 118;
    public override string Name => "Zone.AuctionCancel";
    /// <summary>i32 — id de l'annonce.</summary>
    public int AuctionId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(AuctionId);
    public override void Deserialize(BinReader s) => AuctionId = s.ReadInt();
}

// ---------------------------------------------------------------- S→C ---

/// <summary>Une ligne d'annonce (record commun aux listes 425/428 —
/// même Serialize/Deserialize dans ZoneMessages.g.cs). Seuls les
/// champs que le serveur C# remplit sont exposés ; le reste est lu
/// et ignoré.</summary>
public sealed class AuctionEntry
{
    /// <summary>i32 — id de l'annonce (active_auctions.id).</summary>
    public int AuctionId;
    /// <summary>i32 — id t_item de l'objet.</summary>
    public int ItemId;
    /// <summary>i32 — prix demandé.</summary>
    public int Price;
    /// <summary>u8 — flag natif (non rempli par le serveur C#).</summary>
    public byte Flag;
    /// <summary>str — nom de l'item (vide côté serveur C# → résolution
    /// client via les données items).</summary>
    public string ItemName = "";
    /// <summary>i64 — unix time d'expiration (48 h après dépôt).</summary>
    public long ExpireTime;
    /// <summary>u32 — charId du vendeur.</summary>
    public uint SellerId;
}

/// <summary>S→C NCID 425 — ClientAuctionBuyList `[s16 cat][u16 n]
/// [n×record]` : catalogue des annonces actives (réponse au 114).</summary>
public sealed class ZoneAuctionBuyList : NetMessage
{
    public override int Opcode => 425;
    public override string Name => "Zone.ClientAuctionBuyList";
    /// <summary>s16 — catégorie écho (0 côté serveur C#).</summary>
    public short Category { get; set; }
    /// <summary>Annonces actives.</summary>
    public List<AuctionEntry> Entries { get; } = new();

    public override void Serialize(BinWriter s)
        => throw new NotSupportedException("S→C uniquement");

    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        Category = s.ReadShort();
        var n = s.ReadUShort();
        for (var i = 0; i < n && s.Remaining > 0; i++)
            Entries.Add(ReadEntry(s));
    }

    internal static AuctionEntry ReadEntry(BinReader s)
    {
        var e = new AuctionEntry();
        e.AuctionId = s.ReadInt();                   // i32 id annonce
        s.ReadShort(); s.ReadShort();                // 2×s16 natifs
        e.ItemId = s.ReadInt();                      // i32 t_item id
        e.Price = s.ReadInt();                       // i32 prix
        for (var i = 0; i < 5; i++) s.ReadShort();   // 5×s16 item natifs
        e.Flag = s.ReadByte();                       // u8 flag natif
        for (var i = 0; i < 7; i++) s.ReadShort();   // 7×s16 item natifs
        e.ItemName = s.ReadString();                 // str nom item
        for (var i = 0; i < 10; i++) s.ReadShort();  // 10×s16 item natifs
        e.ExpireTime = s.ReadLong();                 // i64 unix expiration
        s.ReadUShort();                              // u16
        e.SellerId = (uint)s.ReadInt();              // u32 vendeur
        for (var i = 0; i < 6; i++) s.ReadUShort();  // 6×u16 natifs
        for (var i = 0; i < 5; i++) s.ReadShort();   // 5×s16
        s.ReadUShort(); s.ReadUShort();              // 2×u16
        return e;
    }
}

/// <summary>S→C NCID 426 — ClientAuctionBuyOK : corps vide —
/// achat accepté (l'item arrive par courrier).</summary>
public sealed class ZoneAuctionBuyOk : NetMessage
{
    public override int Opcode => 426;
    public override string Name => "Zone.ClientAuctionBuyOK";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>S→C NCID 427 — ClientAuctionCancelOK : corps vide —
/// annulation acceptée (l'item revient par courrier).</summary>
public sealed class ZoneAuctionCancelOk : NetMessage
{
    public override int Opcode => 427;
    public override string Name => "Zone.ClientAuctionCancelOK";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>S→C NCID 428 — ClientAuctionSellList `[u16 n][n×record]` :
/// mes annonces actives (réponse au 116 ; même record que 425, sans
/// le s16 catégorie en tête).</summary>
public sealed class ZoneAuctionSellList : NetMessage
{
    public override int Opcode => 428;
    public override string Name => "Zone.ClientAuctionSellList";
    /// <summary>Mes annonces actives.</summary>
    public List<AuctionEntry> Entries { get; } = new();

    public override void Serialize(BinWriter s)
        => throw new NotSupportedException("S→C uniquement");

    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        var n = s.ReadUShort();
        for (var i = 0; i < n && s.Remaining > 0; i++)
            Entries.Add(ZoneAuctionBuyList.ReadEntry(s));
    }
}

/// <summary>S→C NCID 429 — ClientAuctionSellResult `[s16 res]` —
/// 0 = annonce créée.</summary>
public sealed class ZoneAuctionSellResult : NetMessage
{
    public override int Opcode => 429;
    public override string Name => "Zone.ClientAuctionSellResult";
    /// <summary>s16 — 0 = succès.</summary>
    public short Result { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(Result);
    public override void Deserialize(BinReader s)
    { if (s.Remaining >= 2) Result = s.ReadShort(); }
}

/// <summary>S→C NCID 430 — ClientAuctionHouse `[i32 npcUid]` :
/// la zone ouvre l'hôtel des ventes (service NPC 18).</summary>
public sealed class ZoneAuctionHouse : NetMessage
{
    public override int Opcode => 430;
    public override string Name => "Zone.ClientAuctionHouse";
    /// <summary>i32 — uid du NPC enchérisseur.</summary>
    public int NpcUid { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(NpcUid);
    public override void Deserialize(BinReader s) => NpcUid = s.ReadInt();
}

// ---------------------------------------------------------------- état ---

/// <summary>
/// État de l'hôtel des ventes — alimenté sur le thread principal par
/// les binds <c>GameScene.RegisterZoneDispatch</c> ; lu par
/// <c>AuctionWindow</c>. Deux listes : catalogue (425) et mes ventes
/// (428).
/// </summary>
public static class AuctionState
{
    /// <summary>Émis quand une liste/le statut change (main thread).</summary>
    public static event Action? Changed;
    /// <summary>Émis quand un 430 demande l'ouverture de la fenêtre.</summary>
    public static event Action? Opened;

    /// <summary>Uid du NPC enchérisseur (0 = fermé).</summary>
    public static int NpcUid { get; private set; }
    /// <summary>True tant que l'hôtel est considéré ouvert.</summary>
    public static bool IsOpen => _opened;
    /// <summary>Catalogue — annonces actives (dernier 425).</summary>
    public static IReadOnlyList<AuctionEntry> BuyList => _buyList;
    /// <summary>Mes annonces actives (dernier 428).</summary>
    public static IReadOnlyList<AuctionEntry> SellList => _sellList;
    /// <summary>Dernier résultat de mise en vente (-1 = aucun, 0 = ok)
    /// — réarmé à -1 par l'appelant avant un 117 pour distinguer
    /// silence serveur et refus explicite.</summary>
    public static int LastSellResult { get; set; } = -1;
    /// <summary>Id de l'annonce achetée en attente du 426 (le corps
    /// 426 est vide — posé par la fenêtre avant le 115).</summary>
    public static int PendingBuyId { get; set; }
    /// <summary>Id de l'annonce annulée en attente du 427.</summary>
    public static int PendingCancelId { get; set; }

    private static readonly List<AuctionEntry> _buyList = new();
    private static readonly List<AuctionEntry> _sellList = new();
    private static bool _opened;

    /// <summary>430 reçu : ouvre l'hôtel des ventes (main thread).</summary>
    public static void OnOpenHouse(int npcUid)
    {
        var was = _opened;
        _opened = true;
        NpcUid = npcUid;
        if (!was) Opened?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>425 reçu : remplace le catalogue.</summary>
    public static void OnBuyList(ZoneAuctionBuyList m)
    {
        _buyList.Clear();
        _buyList.AddRange(m.Entries);
        Changed?.Invoke();
    }

    /// <summary>428 reçu : remplace mes ventes.</summary>
    public static void OnSellList(ZoneAuctionSellList m)
    {
        _sellList.Clear();
        _sellList.AddRange(m.Entries);
        Changed?.Invoke();
    }

    /// <summary>426 : achat accepté — retire l'annonce du catalogue
    /// (id posé dans <see cref="PendingBuyId"/> avant le 115).</summary>
    public static void OnBuyOk()
    {
        _buyList.RemoveAll(x => x.AuctionId == PendingBuyId);
        PendingBuyId = 0;
        Changed?.Invoke();
    }

    /// <summary>427 : annulation acceptée — retire de mes ventes.</summary>
    public static void OnCancelOk()
    {
        _sellList.RemoveAll(x => x.AuctionId == PendingCancelId);
        PendingCancelId = 0;
        Changed?.Invoke();
    }

    /// <summary>429 : mémorise le résultat de mise en vente.</summary>
    public static void OnSellResult(ZoneAuctionSellResult m)
    {
        LastSellResult = m.Result;
        Changed?.Invoke();
    }

    /// <summary>Peuple l'état pour démonstration (hud-open hors live).</summary>
    public static void SetMock(IEnumerable<AuctionEntry> buy,
        IEnumerable<AuctionEntry> sell)
    {
        _buyList.Clear(); _buyList.AddRange(buy);
        _sellList.Clear(); _sellList.AddRange(sell);
        _opened = true;
        if (NpcUid == 0) NpcUid = -1;
        Opened?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Ferme l'hôtel (sortie de node, fermeture fenêtre).</summary>
    public static void Close()
    {
        if (!_opened) return;
        _opened = false;
        NpcUid = 0;
        Changed?.Invoke();
    }
}
