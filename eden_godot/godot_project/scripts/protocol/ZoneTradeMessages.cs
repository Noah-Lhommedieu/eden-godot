using EdenClient.Net;

namespace EdenClient.Protocol;

// Échange joueur↔joueur (trade) — NCIDs C→S 36-42/56/91/92 et S→C
// 331-340/353/394/395.
//
// Sources de preuve :
//   - $EDEN/server/src/Eden.Zone/Handlers/TradeHandlers.cs —
//     enregistrement des C→S et ce que le serveur C# émet en retour ;
//   - $EDEN/server/.../Generated/ZoneMessages.g.cs — layouts plats
//     émis/acceptés par le serveur C# (référence wire effective) ;
//   - zone/decompiled.c natif : CPlayerFactory::RequestTrade
//     (@0x80f2780, dist ≤ 10 + ClientTradeCancel raison 1 + texte
//     0x333), AcceptTrade (@0x8121590 → ClientTradeStart aux deux),
//     AddItemToTrade (@0x8122260 → ClientTradeAddItem au PARTENAIRE
//     avec l'item complet ; Execute CNE @0x82956a0 : fils wire C→S =
//     [tradeSlot][container][loc][count]), RemoveItemFromTrade
//     (@0x8121bc0 → 334 partenaire), ModifyGoldInTrade (@0x80f7460 →
//     336 partenaire), ConfirmTrade/ReadyTrade/ExchangeTrade
//     (→ 338/394 partenaire, 340 aux deux), CTradeNegotiateOp::Cancel
//     (@0x85707c0 → 339 aux deux [u32 autre][s16 raison]).
//
// ÉCARTS du serveur C# actuel (documentés — le client les tolère) :
//   - 333/334/336 sont émis au DEMANDEUR comme ACKs vides (le
//     partenaire n'est pas notifié — sa colonne reste vide en live) ;
//   - 339 n'est émis qu'au demandeur (le partenaire n'est pas prévenu) ;
//   - l'ordre des fils du C→S 38 est C# : [bag][slot][tradeSlot][count]
//     (F1=bag, F2=slot, F3 ignoré, F4=count) — différent du natif.

// ============================================================
// C→S
// ============================================================

/// <summary>C→S NCID 36 — TradeRequest `[u32 charId cible]` :
/// demande d'échange à un joueur (≤ 10 u, rate-limit 0,5 s natif
/// char+0x650). CNE_CZ_ZoneServerTradeRequest.</summary>
public sealed class ZoneTradeRequest : NetMessage
{
    public override int Opcode => 36;
    public override string Name => "Zone.ZoneServerTradeRequest";
    /// <summary>u32 — charId du joueur visé (entityId des joueurs).</summary>
    public uint TargetCharId { get; set; }
    public override void Serialize(BinWriter s) =>
        s.WriteInt((int)TargetCharId);
    public override void Deserialize(BinReader s) =>
        TargetCharId = s.ReadUInt();
}

/// <summary>C→S NCID 37 — TradeRequestAccept `[u32 charId initiateur]` :
/// accepte l'invitation reçue par 331 → 332 TradeStart aux deux.
/// CNE_CZ_ZoneServerTradeRequestAccept.</summary>
public sealed class ZoneTradeRequestAccept : NetMessage
{
    public override int Opcode => 37;
    public override string Name => "Zone.ZoneServerTradeRequestAccept";
    /// <summary>u32 — charId du demandeur (F1 du 331 reçu).</summary>
    public uint InitiatorCharId { get; set; }
    public override void Serialize(BinWriter s) =>
        s.WriteInt((int)InitiatorCharId);
    public override void Deserialize(BinReader s) =>
        InitiatorCharId = s.ReadUInt();
}

/// <summary>C→S NCID 38 — TradeAddItem
/// `[u16 bag][u16 slot][u16 tradeSlot][u16 count]` : pose un item du
/// sac dans la fenêtre. ⚠ ordre du serveur C#
/// (TradeHandlers.cs : bag=F1, slot=F2, tradeSlot=F3 ignoré,
/// count=F4) — DIFFÉRENT de l'ordre natif
/// [tradeSlot][container][loc][count] (Execute @0x82956a0).
/// CNE_CZ_ZoneServerTradeAddItem.</summary>
public sealed class ZoneTradeAddItem : NetMessage
{
    public override int Opcode => 38;
    public override string Name => "Zone.ZoneServerTradeAddItem";
    /// <summary>u16 — conteneur source (bag ; 0 = sac principal).</summary>
    public ushort Bag { get; set; }
    /// <summary>u16 — slot inventaire source.</summary>
    public ushort Slot { get; set; }
    /// <summary>u16 — case visée dans la fenêtre de trade (0-7 ;
    /// ignoré par le serveur C#, rempli pour compat natif).</summary>
    public ushort TradeSlot { get; set; }
    /// <summary>u16 — quantité (les non-empilables partent entiers).</summary>
    public ushort Count { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Bag);
        s.WriteShort((short)Slot);
        s.WriteShort((short)TradeSlot);
        s.WriteShort((short)Count);
    }
    public override void Deserialize(BinReader s)
    {
        Bag = s.ReadUShort();
        Slot = s.ReadUShort();
        TradeSlot = s.ReadUShort();
        Count = s.ReadUShort();
    }
}

/// <summary>C→S NCID 39 — TradeRemoveItem `[u16 index]` : retire
/// l'entrée n°index de MA colonne d'offre (index dans la liste
/// côté serveur — TradeService.RemoveItemAsync tradeIndex).
/// CNE_CZ_ZoneServerTradeRemoveItem.</summary>
public sealed class ZoneTradeRemoveItem : NetMessage
{
    public override int Opcode => 39;
    public override string Name => "Zone.ZoneServerTradeRemoveItem";
    /// <summary>u16 — index dans mon offre (0-8 natif).</summary>
    public ushort Index { get; set; }
    public override void Serialize(BinWriter s) =>
        s.WriteShort((short)Index);
    public override void Deserialize(BinReader s) =>
        Index = s.ReadUShort();
}

/// <summary>C→S NCID 40 — TradeModifyGold `[u32][u32 or]` : règle
/// l'or offert. Le serveur C# lit le SECOND u32
/// (TradeHandlers → ModifyGoldAsync(msg.Field2)) ; le natif
/// duplique le montant dans les deux champs (ModifyGoldInTrade
/// @0x80f7460 — les deux sont bornés par le porte-monnaie).
/// CNE_CZ_ZoneServerTradeModifyGold.</summary>
public sealed class ZoneTradeModifyGold : NetMessage
{
    public override int Opcode => 40;
    public override string Name => "Zone.ZoneServerTradeModifyGold";
    /// <summary>u32 — miroir natif du montant.</summary>
    public uint GoldMirror { get; set; }
    /// <summary>u32 — montant offert (lu par le serveur C#).</summary>
    public uint Gold { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)GoldMirror);
        s.WriteInt((int)Gold);
    }
    public override void Deserialize(BinReader s)
    {
        GoldMirror = s.ReadUInt();
        Gold = s.ReadUInt();
    }
}

/// <summary>C→S NCID 41 — TradeConfirm (vide) : verrouille mon offre.
/// Quand les deux parties ont confirmé, le serveur exécute
/// l'échange. CNE_CZ_ZoneServerTradeConfirm.</summary>
public sealed class ZoneTradeConfirm : NetMessage
{
    public override int Opcode => 41;
    public override string Name => "Zone.ZoneServerTradeConfirm";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>C→S NCID 42 — TradeCancel `[u32][s16 raison]` : abandonne
/// l'échange (ou refuse une invite — la session trade existe dès le
/// 36). Les champs sont ignorés par le serveur C# ; le natif les
/// recopie dans le 339 émis aux deux (raisons : 1=trop loin, 6/7).
/// CNE_CZ_ZoneServerTradeCancel.</summary>
public sealed class ZoneTradeCancel : NetMessage
{
    public override int Opcode => 42;
    public override string Name => "Zone.ZoneServerTradeCancel";
    /// <summary>u32 — charId de l'autre partie (natif).</summary>
    public uint PartnerCharId { get; set; }
    /// <summary>s16 — raison (natif : code interne).</summary>
    public short Reason { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)PartnerCharId);
        s.WriteShort(Reason);
    }
    public override void Deserialize(BinReader s)
    {
        PartnerCharId = s.ReadUInt();
        Reason = s.ReadShort();
    }
}

/// <summary>C→S NCID 56 — TradeReset (vide) : vide les deux offres
/// (items rendus). CNE_CZ_ZoneServerTradeReset.</summary>
public sealed class ZoneTradeReset : NetMessage
{
    public override int Opcode => 56;
    public override string Name => "Zone.ZoneServerTradeReset";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>C→S NCID 91 — TradeReady (vide) : signal « prêt » final
/// (Ok natif — quand les deux confirmés, l'échange part). Le serveur
/// C# notifie le partenaire par 394. CNE_CZ_ZoneServerTradeReady.</summary>
public sealed class ZoneTradeReady : NetMessage
{
    public override int Opcode => 91;
    public override string Name => "Zone.ZoneServerTradeReady";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>C→S NCID 92 — TradeUnConfirm (vide) : retire mon
/// verrouillage avant exécution. CNE_CZ_ZoneServerTradeUnConfirm.</summary>
public sealed class ZoneTradeUnConfirm : NetMessage
{
    public override int Opcode => 92;
    public override string Name => "Zone.ZoneServerTradeUnConfirm";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

// ============================================================
// S→C
// ============================================================

/// <summary>S→C NCID 331 — ClientTradeRequest `[u32 demandeur][str nom]` :
/// invitation d'échange entrante (popup Accepter/Refuser).
/// CNC_CZ_ClientTradeRequest — ctor natif (u32, string).</summary>
public sealed class ZoneClientTradeRequest : NetMessage
{
    public override int Opcode => 331;
    public override string Name => "Zone.ClientTradeRequest";
    /// <summary>u32 — charId du demandeur.</summary>
    public uint RequesterCharId { get; set; }
    /// <summary>str — nom affiché du demandeur.</summary>
    public string RequesterName { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)RequesterCharId);
        s.WriteString(RequesterName);
    }
    public override void Deserialize(BinReader s)
    {
        RequesterCharId = s.ReadUInt();
        RequesterName = s.ReadString();
    }
}

/// <summary>S→C NCID 332 — ClientTradeStart `[u32][str]` : l'échange
/// démarre — ouvre la fenêtre des deux côtés (AcceptTrade natif
/// envoie aux deux parties id+nom de l'autre).
/// CNC_CZ_ClientTradeStart.</summary>
public sealed class ZoneClientTradeStart : NetMessage
{
    public override int Opcode => 332;
    public override string Name => "Zone.ClientTradeStart";
    /// <summary>u32 — charId du partenaire.</summary>
    public uint PartnerCharId { get; set; }
    /// <summary>str — nom du partenaire.</summary>
    public string PartnerName { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)PartnerCharId);
        s.WriteString(PartnerName);
    }
    public override void Deserialize(BinReader s)
    {
        PartnerCharId = s.ReadUInt();
        PartnerName = s.ReadString();
    }
}

/// <summary>S→C NCID 333 — ClientTradeAddItem : un item entre dans
/// l'offre. Layout PLAT du serveur C# (ZoneMessages.g.cs — 27 champs
/// fixes ; le Serialize natif @0x8139f60 utilise en fait un masque
/// 24 bits + fils optionnels, format non reproduit par le C#).
/// En-tête commun aux deux formats (membres +4..+0xc) :
/// F1=u16 slot trade, F2=u16 item_id, F3=s16 quantité,
/// F4=u16 extra (local_32 — 0 non-empilé), F5=str nom.
/// Le serveur C# actuel émet tout à 0 en ACK au demandeur.</summary>
public sealed class ZoneClientTradeAddItem : NetMessage
{
    public override int Opcode => 333;
    public override string Name => "Zone.ClientTradeAddItem";
    /// <summary>u16 — case de la fenêtre de trade (0-8).</summary>
    public ushort TradeSlot { get; set; }
    /// <summary>u16 — item_id S_Item (membre +6).</summary>
    public ushort ItemId { get; set; }
    /// <summary>s16 — quantité posée (membre +8).</summary>
    public short Count { get; set; }
    /// <summary>u16 — membre +0xa (local_32 — clone+0xe).</summary>
    public ushort LocExtra { get; set; }
    /// <summary>str — nom de l'item (membre +0xc).</summary>
    public string ItemName { get; set; } = "";
    /// <summary>s16×8 — membres +0x12..+0x1e (embedded/flags).</summary>
    public short[] Tail1 { get; } = new short[8];
    /// <summary>u32 — membre +0x20.</summary>
    public uint Tail2 { get; set; }
    /// <summary>u16×10 — membres +0x24..+0x36 (extra combos).</summary>
    public ushort[] Tail3 { get; } = new ushort[10];
    /// <summary>s16 — membre +0x3a.</summary>
    public short Tail4 { get; set; }
    /// <summary>s16 — membre +0x3c.</summary>
    public short Tail5 { get; set; }
    /// <summary>i32 — membre +0x40.</summary>
    public int Tail6 { get; set; }
    /// <summary>Vrai si le paquet porte un item (sinon ACK vide du
    /// serveur C# — garder l'affichage optimiste local).</summary>
    public bool HasItem => ItemId != 0 || ItemName.Length > 0;

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)TradeSlot);
        s.WriteShort((short)ItemId);
        s.WriteShort(Count);
        s.WriteShort((short)LocExtra);
        s.WriteString(ItemName);
        foreach (var v in Tail1) s.WriteShort(v);
        s.WriteInt((int)Tail2);
        foreach (var v in Tail3) s.WriteShort((short)v);
        s.WriteShort(Tail4);
        s.WriteShort(Tail5);
        s.WriteInt(Tail6);
    }
    public override void Deserialize(BinReader s)
    {
        TradeSlot = s.ReadUShort();
        ItemId = s.ReadUShort();
        Count = s.ReadShort();
        LocExtra = s.ReadUShort();
        ItemName = s.ReadString();
        for (var i = 0; i < Tail1.Length && s.Remaining >= 2; i++)
            Tail1[i] = s.ReadShort();
        if (s.Remaining >= 4) Tail2 = s.ReadUInt();
        for (var i = 0; i < Tail3.Length && s.Remaining >= 2; i++)
            Tail3[i] = s.ReadUShort();
        if (s.Remaining >= 2) Tail4 = s.ReadShort();
        if (s.Remaining >= 2) Tail5 = s.ReadShort();
        if (s.Remaining >= 4) Tail6 = s.ReadInt();
    }
}

/// <summary>S→C NCID 334 — ClientTradeRemoveItem `[u16 index]` :
/// natif = le partenaire a retiré l'entrée index de son offre ;
/// serveur C# = ACK au demandeur (Value=0).
/// CNC_CZ_ClientTradeRemoveItem.</summary>
public sealed class ZoneClientTradeRemoveItem : NetMessage
{
    public override int Opcode => 334;
    public override string Name => "Zone.ClientTradeRemoveItem";
    /// <summary>u16 — index d'offre retirée.</summary>
    public ushort Index { get; set; }
    public override void Serialize(BinWriter s) =>
        s.WriteShort((short)Index);
    public override void Deserialize(BinReader s) =>
        Index = s.ReadUShort();
}

/// <summary>S→C NCID 335 — ClientTradeAddItemFail `[u16 slot][s16 code]` :
/// mon add a été refusé (codes natifs AddItemToTrade : 0x14 pas
/// d'échange, 0x17 slot&gt;8, 0x18 doublon, 0x19 item absent,
/// 0x1a count&gt;stack). CNC_CZ_ClientTradeAddItemFail.</summary>
public sealed class ZoneClientTradeAddItemFail : NetMessage
{
    public override int Opcode => 335;
    public override string Name => "Zone.ClientTradeAddItemFail";
    /// <summary>u16 — slot visé.</summary>
    public ushort TradeSlot { get; set; }
    /// <summary>s16 — code d'échec natif.</summary>
    public short Reason { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)TradeSlot);
        s.WriteShort(Reason);
    }
    public override void Deserialize(BinReader s)
    {
        TradeSlot = s.ReadUShort();
        Reason = s.ReadShort();
    }
}

/// <summary>S→C NCID 336 — ClientTradeModifyGold `[u32][u32 or]` :
/// natif = l'or offert par le partenaire (F2) ; serveur C# = ACK au
/// demandeur (0,0). CNC_CZ_ClientTradeModifyGold.</summary>
public sealed class ZoneClientTradeModifyGold : NetMessage
{
    public override int Opcode => 336;
    public override string Name => "Zone.ClientTradeModifyGold";
    /// <summary>u32 — miroir du montant.</summary>
    public uint GoldMirror { get; set; }
    /// <summary>u32 — montant d'or offert.</summary>
    public uint Gold { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)GoldMirror);
        s.WriteInt((int)Gold);
    }
    public override void Deserialize(BinReader s)
    {
        GoldMirror = s.ReadUInt();
        Gold = s.ReadUInt();
    }
}

/// <summary>S→C NCID 337 — ClientTradeModifyGoldFail `[u32][u32]` :
/// mon set d'or refusé (solde insuffisant). Réafficher l'ancienne
/// valeur. CNC_CZ_ClientTradeModifyGoldFail.</summary>
public sealed class ZoneClientTradeModifyGoldFail : NetMessage
{
    public override int Opcode => 337;
    public override string Name => "Zone.ClientTradeModifyGoldFail";
    /// <summary>u32 — champ natif 1.</summary>
    public uint Field1 { get; set; }
    /// <summary>u32 — champ natif 2.</summary>
    public uint Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Field1);
        s.WriteInt((int)Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadUInt();
        Field2 = s.ReadUInt();
    }
}

/// <summary>S→C NCID 338 — ClientTradeConfirm (vide) : une partie a
/// verrouillé son offre (natif : au partenaire ; serveur C# : aux
/// deux). CNC_CZ_ClientTradeConfirm.</summary>
public sealed class ZoneClientTradeConfirm : NetMessage
{
    public override int Opcode => 338;
    public override string Name => "Zone.ClientTradeConfirm";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>S→C NCID 339 — ClientTradeCancel `[u32 autre][s16 raison]` :
/// l'échange est annulé — fermer la fenêtre (raisons natives :
/// 1 = demande refusée/trop loin, 6/7 = annulation op,
/// -1 = add illégal côté pair). CNC_CZ_ClientTradeCancel.</summary>
public sealed class ZoneClientTradeCancel : NetMessage
{
    public override int Opcode => 339;
    public override string Name => "Zone.ClientTradeCancel";
    /// <summary>u32 — charId de l'autre partie.</summary>
    public uint PartnerCharId { get; set; }
    /// <summary>s16 — code raison natif.</summary>
    public short Reason { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)PartnerCharId);
        s.WriteShort(Reason);
    }
    public override void Deserialize(BinReader s)
    {
        PartnerCharId = s.ReadUInt();
        Reason = s.ReadShort();
    }
}

/// <summary>S→C NCID 340 — ClientTradeComplete (vide) : échange
/// réalisé — fermer la fenêtre (natif : aux deux ; le serveur C#
/// ne l'émet pas encore — la complétion est observable via les
/// deltas d'inventaire). CNC_CZ_ClientTradeComplete.</summary>
public sealed class ZoneClientTradeComplete : NetMessage
{
    public override int Opcode => 340;
    public override string Name => "Zone.ClientTradeComplete";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>S→C NCID 353 — ClientTradeReset (vide) : les offres ont
/// été vidées — vider les deux colonnes. CNC_CZ_ClientTradeReset.</summary>
public sealed class ZoneClientTradeReset : NetMessage
{
    public override int Opcode => 353;
    public override string Name => "Zone.ClientTradeReset";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>S→C NCID 394 — ClientTradeReady (vide) : le partenaire a
/// signalé « prêt » (Ok). CNC_CZ_ClientTradeReady.</summary>
public sealed class ZoneClientTradeReady : NetMessage
{
    public override int Opcode => 394;
    public override string Name => "Zone.ClientTradeReady";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>S→C NCID 395 — ClientTradeUnConfirm `[u32]` : le
/// partenaire a retiré son verrouillage. CNC_CZ_ClientTradeUnConfirm.</summary>
public sealed class ZoneClientTradeUnConfirm : NetMessage
{
    public override int Opcode => 395;
    public override string Name => "Zone.ClientTradeUnConfirm";
    /// <summary>u32 — charId du partenaire (valeur native).</summary>
    public uint PartnerCharId { get; set; }
    public override void Serialize(BinWriter s) =>
        s.WriteInt((int)PartnerCharId);
    public override void Deserialize(BinReader s) =>
        PartnerCharId = s.ReadUInt();
}

// ============================================================
// État agrégé pour la fenêtre
// ============================================================

/// <summary>Item posé dans une colonne de l'échange.</summary>
public sealed record TradeOfferItem(int Index, int ItemId,
    string Name, int Count);

/// <summary>
/// État courant de l'échange J↔J — alimenté par les binds réseau de
/// <c>TradeWindow</c>, lu par l'UI. Comme les autres états
/// (<see cref="ShopState"/>, <see cref="PartyState"/>), les mutations
/// arrivent déjà sur le thread principal.
///
/// Sémantique serveur C# vs natif (voir en-tête du fichier) : les
/// offres du partenaire n'arrivent pas encore — la fenêtre garde une
/// mise à jour optimiste de MON offre et tolère les deux formats de
/// notification (ACK vide du C# / paquet natif complet).
/// </summary>
public static class TradeState
{
    /// <summary>Émis à chaque mutation (démarrage, offre, or, statuts).</summary>
    public static event Action? Changed;
    /// <summary>Émis quand une invite 331 arrive (popup).</summary>
    public static event Action? Invited;

    /// <summary>Vrai tant qu'une session d'échange est ouverte.</summary>
    public static bool IsOpen { get; private set; }
    /// <summary>charId du partenaire (0 hors échange).</summary>
    public static uint PartnerCharId { get; private set; }
    /// <summary>Nom affiché du partenaire.</summary>
    public static string PartnerName { get; private set; } = "";
    /// <summary>Mon offre d'or (dernière valeur émise).</summary>
    public static uint MyGold { get; private set; }
    /// <summary>Or offert par le partenaire (336 natif ; 0 tant que
    /// le serveur C# ne relaie pas).</summary>
    public static uint PartnerGold { get; private set; }
    /// <summary>J'ai verrouillé mon offre (41 émis/338 reçu — ou
    /// confirmé via 91, qui marque aussi côté serveur).</summary>
    public static bool SelfLocked { get; private set; }
    /// <summary>J'ai signalé « prêt » (91 émis — optimiste, le
    /// serveur ne répond pas à l'émetteur).</summary>
    public static bool SelfReady { get; private set; }
    /// <summary>Le partenaire a verrouillé (338/394 reçus).</summary>
    public static bool PartnerLocked { get; private set; }
    /// <summary>Le partenaire a signalé « prêt » (394).</summary>
    public static bool PartnerReady { get; private set; }
    /// <summary>Vrai quand l'échange a été exécuté côté serveur —
    /// posé par le 340 (ClientTradeComplete, émis aux deux parties
    /// depuis que le serveur C# l'envoie au settle) ou, à défaut,
    /// déduit du double verrouillage via <see cref="CheckDone"/>.</summary>
    public static bool Completed { get; private set; }
    /// <summary>41 émis, écho 338 pas encore attribué — le prochain
    /// 338 reçu est le Mien (NotifyBoth serveur le renvoie aux
    /// deux, émetteur compris).</summary>
    private static bool _selfConfirmPending;
    /// <summary>Invite entrante en attente (331) — popup.</summary>
    public static (uint CharId, string Name)? PendingInvite { get; private set; }
    /// <summary>charId de l'invite sortante (36 émis) — contexte
    /// conservé car le 332 du serveur C# arrive vide.</summary>
    public static uint OutgoingInviteCharId { get; private set; }
    /// <summary>Invite sortante en attente de réponse (36 émis).</summary>
    public static string OutgoingInviteName { get; set; } = "";

    /// <summary>36 émis — mémorise la cible pour le 332 vide.</summary>
    public static void MarkOutgoingInvite(uint charId, string name)
    {
        OutgoingInviteCharId = charId;
        OutgoingInviteName = name;
    }

    private static readonly List<TradeOfferItem> _myItems = new();
    /// <summary>Contenu de MA colonne (ordre d'ajout).</summary>
    public static IReadOnlyList<TradeOfferItem> MyItems => _myItems;
    private static readonly List<TradeOfferItem> _partnerItems = new();
    /// <summary>Contenu de la colonne du partenaire.</summary>
    public static IReadOnlyList<TradeOfferItem> PartnerItems => _partnerItems;

    /// <summary>331 — popup d'invitation.</summary>
    public static void ApplyInvite(ZoneClientTradeRequest m)
    {
        PendingInvite = (m.RequesterCharId, m.RequesterName);
        Invited?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Invite traitée (acceptée/refusée/expirée).</summary>
    public static void ClearInvite()
    {
        if (PendingInvite == null) return;
        PendingInvite = null;
        Changed?.Invoke();
    }

    /// <summary>332 — ouverture de la fenêtre (invite acceptée).
    /// Le serveur C# émet le paquet VIDE ([u32 0][str ""] aux deux)
    /// — on garde alors le partenaire connu (invite entrante ou
    /// sortante) ; le natif remplit id+nom de l'autre partie.</summary>
    public static void ApplyStart(ZoneClientTradeStart m)
    {
        IsOpen = true;
        if (m.PartnerCharId != 0 || m.PartnerName.Length > 0)
        {
            PartnerCharId = m.PartnerCharId;
            PartnerName = m.PartnerName;
        }
        else if (PendingInvite is { } inv)
        {
            PartnerCharId = inv.CharId;
            PartnerName = inv.Name;
        }
        else
        {
            PartnerCharId = OutgoingInviteCharId;
            PartnerName = OutgoingInviteName;
        }
        MyGold = PartnerGold = 0;
        SelfLocked = SelfReady = PartnerLocked = PartnerReady = false;
        Completed = false;
        _selfConfirmPending = false;
        _myItems.Clear();
        _partnerItems.Clear();
        PendingInvite = null;
        OutgoingInviteCharId = 0;
        OutgoingInviteName = "";
        Changed?.Invoke();
    }

    /// <summary>Ajout optimiste local de MON item (l'ACK 333 vide du
    /// serveur C# ne porte aucune info — l'affichage est déjà fait).</summary>
    public static void AddMyItem(int index, InventorySlot it)
    {
        _myItems.Add(new TradeOfferItem(index, it.ItemId,
            "", it.Amount > 0 ? it.Amount : 1));
        Changed?.Invoke();
    }

    /// <summary>Retrait optimiste d'une entrée de mon offre.</summary>
    public static void RemoveMyItem(int listIndex)
    {
        if (listIndex >= 0 && listIndex < _myItems.Count)
            _myItems.RemoveAt(listIndex);
        Changed?.Invoke();
    }

    /// <summary>335 — mon add refusé : retire l'entrée optimiste à
    /// l'index de trade indiqué (position _myItems.IndexOf(Index)).</summary>
    public static void RevertMyAdd(ZoneClientTradeAddItemFail m)
    {
        var i = _myItems.FindIndex(e => e.Index == m.TradeSlot);
        if (i >= 0) _myItems.RemoveAt(i);
        else if (_myItems.Count > 0) _myItems.RemoveAt(_myItems.Count - 1);
        Changed?.Invoke();
    }

    /// <summary>333 — item ajouté : format natif = offre partenaire
    /// (F1 slot, F2 itemId, F3 count, F5 nom) ; ACK vide C# = ne
    /// rien changer (optimiste local déjà appliqué).</summary>
    public static void ApplyAddItem(ZoneClientTradeAddItem m)
    {
        if (!m.HasItem) return;
        _partnerItems.RemoveAll(e => e.Index == m.TradeSlot);
        _partnerItems.Add(new TradeOfferItem(m.TradeSlot, m.ItemId,
            m.ItemName, m.Count > 0 ? m.Count : 1));
        Changed?.Invoke();
    }

    /// <summary>334 — retrait : index de l'offre adverse (natif) ;
        /// ignoré en ACK C# quand la liste adverse est vide.</summary>
    public static void ApplyRemoveItem(ZoneClientTradeRemoveItem m)
    {
        var i = _partnerItems.FindIndex(e => e.Index == m.Index);
        if (i < 0 && m.Index < _partnerItems.Count) i = m.Index;
        if (i >= 0) { _partnerItems.RemoveAt(i); Changed?.Invoke(); }
    }

    /// <summary>336 — or : natif = montant du partenaire (F2) ;
    /// ACK C# (0) = garder ma valeur saisie.</summary>
    public static void ApplyModifyGold(ZoneClientTradeModifyGold m)
    {
        if (m.Gold != 0 || m.GoldMirror != 0)
            PartnerGold = m.Gold;
        Changed?.Invoke();
    }

    /// <summary>Positionne mon or affiché (saisie émise au 40).</summary>
    public static void SetMyGold(uint gold)
    { MyGold = gold; Changed?.Invoke(); }

    /// <summary>41 vient d'être émis — le prochain 338 est l'écho
    /// serveur de MON verrouillage (NotifyBoth le renvoie à
    /// l'émetteur aussi).</summary>
    public static void MarkConfirmSent()
    { _selfConfirmPending = true; }

    /// <summary>91 vient d'être émis — le serveur C# y répond par
    /// une confirmation ET aucun écho à l'émetteur (ConfirmTradeAsync
    /// marque aussi « confirmé ») → verrouillage optimiste.</summary>
    public static void MarkReadySent()
    {
        SelfLocked = true;
        SelfReady = true;
        CheckDone();
    }

    /// <summary>338 — un verrouillage est arrivé : si mon 41 attend
    /// son écho c'est le Mien, sinon c'est celui du partenaire
    /// (le serveur C# émet aux deux parties, émetteur compris).</summary>
    public static void ApplyConfirm()
    {
        if (_selfConfirmPending && !SelfLocked)
        {
            _selfConfirmPending = false;
            SelfLocked = true;
        }
        else
        {
            PartnerLocked = true;
        }
        CheckDone();
    }

    /// <summary>394 — le partenaire a validé « prêt » (le handler C#
    /// le marque aussi confirmé côté serveur).</summary>
    public static void ApplyReady()
    {
        PartnerReady = true;
        PartnerLocked = true;
        CheckDone();
    }

    /// <summary>395 — le partenaire a retiré son verrouillage
    /// (C→S 92 natif ; le serveur C# ne route pas le 92 mais tolère
    /// le paquet si un jour le relayage est ajouté).</summary>
    public static void ApplyUnConfirm()
    {
        PartnerLocked = PartnerReady = false;
        Changed?.Invoke();
    }

    /// <summary>Complétion déduite : les deux parties ont confirmé →
    /// le serveur exécute. Filet de secours si le 340 se perd ; le
    /// drapeau est réarmé par <see cref="ApplyStart"/> au prochain
    /// échange.</summary>
    private static void CheckDone()
    {
        if (!Completed && SelfLocked && (PartnerLocked || PartnerReady))
            Completed = true;
        Changed?.Invoke();
    }

    /// <summary>340 — échange exécuté côté serveur (settle atomique :
    /// or + items croisés livrés, session supprimée). Ferme la
    /// fenêtre comme <see cref="Close"/> et laisse
    /// <see cref="Completed"/> à true pour la preuve/UI (réarmé au
    /// prochain échange par <see cref="ApplyStart"/>).</summary>
    public static void ApplyComplete()
    {
        Completed = true;
        Close();
        Changed?.Invoke();
    }

    /// <summary>353 — offres vidées (items rendus côté serveur).</summary>
    public static void ApplyReset()
    {
        _myItems.Clear();
        _partnerItems.Clear();
        MyGold = PartnerGold = 0;
        SelfLocked = SelfReady = PartnerLocked = PartnerReady = false;
        Completed = false;
        _selfConfirmPending = false;
        Changed?.Invoke();
    }

    /// <summary>339/fermeture locale — fin de la session. Ne touche
    /// PAS <see cref="Completed"/> : ce drapeau consigne l'issue du
    /// dernier échange (réarmé par ApplyStart/ApplyReset) — le
    /// compte à rebours de fermeture de la fenêtre l'appelle et ne
    /// doit pas effacer la preuve de complétion.</summary>
    public static void Close()
    {
        var had = IsOpen || PendingInvite != null;
        IsOpen = false;
        PartnerCharId = 0;
        PartnerName = "";
        MyGold = PartnerGold = 0;
        SelfLocked = SelfReady = PartnerLocked = PartnerReady = false;
        _selfConfirmPending = false;
        _myItems.Clear();
        _partnerItems.Clear();
        PendingInvite = null;
        OutgoingInviteCharId = 0;
        OutgoingInviteName = "";
        if (had) Changed?.Invoke();
    }

    /// <summary>339 — échange annulé (avant exécution) : session
    /// close ET résultat négatif, contrairement à
    /// <see cref="ApplyComplete"/>.</summary>
    public static void ApplyCancel()
    {
        Completed = false;
        Close();
    }
}
