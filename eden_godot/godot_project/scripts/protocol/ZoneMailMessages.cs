using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages de la boîte aux lettres — service NPC type 7 (vendeur courrier).
// Le client ne parle JAMAIS directement à la mission : la zone relaie les
// commandes mail en interne via NCID 21 AgentDBACommand (texte) et re-route
// les réponses mission 68-77 vers le client sous les NCID 400-409.
//
// Sources de preuve :
//   - $EDEN/server/.../Eden.Zone/Handlers/SocialHandlers.cs
//     (parse C→S 95-102 + émission 403 côté envoi) ;
//   - $EDEN/server/.../Eden.Zone/ZoneEntryFlow.Mission.cs
//     ForwardMailListAsync + case 68-77 → 400-409 ;
//   - $EDEN/server/.../Eden.Zone/ZoneEntryFlow.Dialogue.cs
//     service 7 → 399 ClientMailOpenBox ;
//   - $EDEN/server/.../Generated/ZoneMessages.g.cs :
//     CNC_CZ_ClientMailList::Deserialize (entry ~120B + trailing
//     `[s8][u16 n][n×s16]` obligatoire — commentaire serveur : sans ce
//     bloc le client natif lit trop loin et crashe),
//     CNC_ZC_MailSend / MailQuery / MailRead / MailDelete / MailGetItem /
//     MailGetGold / MailReturn ;
//   - natif zone.exe `k_cac_mail` : commandes DBA `MailQuery page_number=%d
//     accept_only_friend_mail=%d`, `MailRead mail_id=%d`, etc.

// ---------------------------------------------------------------- C→S ---

/// <summary>C→S NCID 95 — MailOpenBox `[s16 op]`.
/// op=7 : le client acquitte l'ouverture boîte (serveur :
/// ResetOperation → renvoie 399 avec npcUid=0).</summary>
public sealed class ZoneMailOpenBox : NetMessage
{
    public override int Opcode => 95;
    public override string Name => "Zone.MailOpenBox";
    /// <summary>s16 — opération (7 = ouverture acquittée).</summary>
    public short Op { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(Op);
    public override void Deserialize(BinReader s) => Op = s.ReadShort();
}

/// <summary>C→S NCID 96 — MailQuery `[s16 page][s16 onlyFriend]`.
/// Le serveur C# ignore les deux champs (renvoie la boîte entière) ;
/// on envoie page=0/onlyFriend=0, le format reste fidèle.</summary>
public sealed class ZoneMailQuery : NetMessage
{
    public override int Opcode => 96;
    public override string Name => "Zone.MailQuery";
    /// <summary>s16 — numéro de page (non utilisé serveur).</summary>
    public short Page { get; set; }
    /// <summary>s16 — 1 = ne montrer que le courrier d'amis (ignoré).</summary>
    public short AcceptOnlyFriend { get; set; }
    public override void Serialize(BinWriter s)
    { s.WriteShort(Page); s.WriteShort(AcceptOnlyFriend); }
    public override void Deserialize(BinReader s)
    { Page = s.ReadShort(); AcceptOnlyFriend = s.ReadShort(); }
}

/// <summary>C→S NCID 97 — MailRead `[i32 mailId]` : ouvre/marque lu.</summary>
public sealed class ZoneMailRead : NetMessage
{
    public override int Opcode => 97;
    public override string Name => "Zone.MailRead";
    /// <summary>i32 — id du courrier lu.</summary>
    public int MailId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(MailId);
    public override void Deserialize(BinReader s) => MailId = s.ReadInt();
}

/// <summary>C→S NCID 98 — MailDelete `[i32 mailId][u16 n][n×i32]`.
/// mailId seul ou lot (mailId=0 + liste), voir HandleMailDeleteAsync.</summary>
public sealed class ZoneMailDelete : NetMessage
{
    public override int Opcode => 98;
    public override string Name => "Zone.MailDelete";
    /// <summary>i32 — id unique (0 si suppression en lot via <see cref="Ids"/>).</summary>
    public int MailId { get; set; }
    /// <summary>n×i32 — ids du lot (peut être vide).</summary>
    public List<int> Ids { get; } = new();
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(MailId);
        s.WriteUShort((ushort)Ids.Count);
        foreach (var id in Ids) s.WriteInt(id);
    }
    public override void Deserialize(BinReader s)
    {
        MailId = s.ReadInt();
        Ids.Clear();
        var n = s.ReadUShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++) Ids.Add(s.ReadInt());
    }
}

/// <summary>C→S NCID 99 — MailSend — envoi de courrier.
/// Wire `[str to][str titre][str corps][s16 bag][s16 slot][i32 or]
/// [i32 cod][u8 flag]` — SocialHandlers.cs : Field4/Field5 = bag/slot
/// (GetItemAsync(charId, Field4, Field5), -1 = pas de pièce jointe),
/// Field6 = or joint, Field7 = montant COD exigé, Field8 = flag natif.</summary>
public sealed class ZoneMailSend : NetMessage
{
    public override int Opcode => 99;
    public override string Name => "Zone.MailSend";
    /// <summary>str — nom du personnage destinataire.</summary>
    public string To { get; set; } = "";
    /// <summary>str — titre du courrier.</summary>
    public string Title { get; set; } = "";
    /// <summary>str — corps.</summary>
    public string Body { get; set; } = "";
    /// <summary>s16 — container inventaire de la PJ (-1 = aucune).</summary>
    public short ItemBag { get; set; } = -1;
    /// <summary>s16 — slot inventaire de la PJ (-1 = aucune).</summary>
    public short ItemSlot { get; set; } = -1;
    /// <summary>i32 — or joint (0 = aucun).</summary>
    public int Gold { get; set; }
    /// <summary>i32 — montant COD demandé au destinataire (0 = aucun).</summary>
    public int CodAmount { get; set; }
    /// <summary>u8 — flag natif (0).</summary>
    public byte Flag { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteString(To); s.WriteString(Title); s.WriteString(Body);
        s.WriteShort(ItemBag); s.WriteShort(ItemSlot);
        s.WriteInt(Gold); s.WriteInt(CodAmount); s.WriteByte(Flag);
    }
    public override void Deserialize(BinReader s)
    {
        To = s.ReadString(); Title = s.ReadString(); Body = s.ReadString();
        ItemBag = s.ReadShort(); ItemSlot = s.ReadShort();
        Gold = s.ReadInt(); CodAmount = s.ReadInt(); Flag = s.ReadByte();
    }
}

/// <summary>C→S NCID 100 — MailGetItem `[i32 mailId][u16 n][n×i32]
/// [s16 freeNeeded]` — prend la/les PJ item (mailId=0 → lot via ids).
/// freeNeeded = slots inventaire libres requis (éviter les PJ trop
/// volumineuses si le sac est plein).</summary>
public sealed class ZoneMailGetItem : NetMessage
{
    public override int Opcode => 100;
    public override string Name => "Zone.MailGetItem";
    /// <summary>i32 — id du courrier (0 = prendre toutes les PJ listées).</summary>
    public int MailId { get; set; }
    /// <summary>n×i32 — ids lot (optionnel).</summary>
    public List<int> Ids { get; } = new();
    /// <summary>s16 — slots libres minimum exigés (défaut 1).</summary>
    public short FreeNeeded { get; set; } = 1;
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(MailId);
        s.WriteUShort((ushort)Ids.Count);
        foreach (var id in Ids) s.WriteInt(id);
        s.WriteShort(FreeNeeded);
    }
    public override void Deserialize(BinReader s)
    {
        MailId = s.ReadInt();
        Ids.Clear();
        var n = s.ReadUShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++) Ids.Add(s.ReadInt());
        if (s.Remaining >= 2) FreeNeeded = s.ReadShort();
    }
}

/// <summary>C→S NCID 101 — MailGetGold `[i32 mailId]`.</summary>
public sealed class ZoneMailGetGold : NetMessage
{
    public override int Opcode => 101;
    public override string Name => "Zone.MailGetGold";
    /// <summary>i32 — id du courrier.</summary>
    public int MailId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(MailId);
    public override void Deserialize(BinReader s) => MailId = s.ReadInt();
}

/// <summary>C→S NCID 102 — MailReturn `[i32 mailId]` : renvoie le
/// courrier (et ses PJ) à l'expéditeur.</summary>
public sealed class ZoneMailReturn : NetMessage
{
    public override int Opcode => 102;
    public override string Name => "Zone.MailReturn";
    /// <summary>i32 — id du courrier.</summary>
    public int MailId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(MailId);
    public override void Deserialize(BinReader s) => MailId = s.ReadInt();
}

// ---------------------------------------------------------------- S→C ---

/// <summary>S→C NCID 399 — ClientMailOpenBox `[i32 npcUid]` :
/// la zone ouvre la boîte aux lettres (service NPC 7).</summary>
public sealed class ZoneMailOpenBoxNotify : NetMessage
{
    public override int Opcode => 399;
    public override string Name => "Zone.ClientMailOpenBox";
    /// <summary>i32 — uid du NPC courrier (0 = acquittement de notre 95).</summary>
    public int NpcUid { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(NpcUid);
    public override void Deserialize(BinReader s) => NpcUid = s.ReadInt();
}

/// <summary>Une entrée de la liste courrier (record ~120B du 400).</summary>
public sealed class MailEntry
{
    /// <summary>i32 — id du courrier (tables player_mail/mail_items).</summary>
    public int MailId;
    /// <summary>str — nom de l'expéditeur.</summary>
    public string SenderName = "";
    /// <summary>i32 — unix time d'envoi.</summary>
    public int SendTime;
    /// <summary>str — sujet.</summary>
    public string Subject = "";
    /// <summary>u8 — flag natif ; bit0 utilisé par le serveur C# = lu.</summary>
    public byte Flag;
    /// <summary>str — corps du courrier.</summary>
    public string Content = "";
    /// <summary>i32 — id de l'item joint (0 = aucun).</summary>
    public int AttachItemId;
    /// <summary>i32 — quantité de l'item joint.</summary>
    public int AttachQty;
    /// <summary>i64 — unix time d'expiration (0 = jamais).</summary>
    public long ExpireTime;
    /// <summary>u32 — or joint.</summary>
    public long AttachGold;

    /// <summary>Courrier non lu (bit0 du flag serveur).</summary>
    public bool IsUnread => (Flag & 1) == 0;
    /// <summary>True si une pièce jointe item ou or existe.</summary>
    public bool HasAttachment => AttachItemId > 0 || AttachGold > 0;
}

/// <summary>S→C NCID 400 — ClientMailList : `[u16 n][n×record]
/// [s8][u16 m][m×s16]` — le bloc terminal est OBLIGATOIRE (commentaire
/// ZoneEntryFlow.Mission.cs : sans lui le client natif lit trop loin).
/// Layout record = Deserialize de CNC_CZ_ClientMailList
/// (ZoneMessages.g.cs:9308) : les champs utiles sont extraits, le reste
/// (~50 padding natifs) est lu puis ignoré.</summary>
public sealed class ZoneMailList : NetMessage
{
    public override int Opcode => 400;
    public override string Name => "Zone.ClientMailList";

    /// <summary>Entrées de la boîte (ordre du serveur).</summary>
    public List<MailEntry> Entries { get; } = new();

    public override void Serialize(BinWriter s)
        => throw new NotSupportedException("S→C uniquement");

    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        var n = s.ReadUShort();
        for (var i = 0; i < n && s.Remaining > 0; i++)
            Entries.Add(ReadEntry(s));
        // Bloc terminal natif : [s8 flag][u16 m][m×s16].
        if (s.Remaining >= 1) s.ReadByte();
        if (s.Remaining >= 2)
        {
            var m = s.ReadUShort();
            for (var i = 0; i < m && s.Remaining >= 2; i++) s.ReadShort();
        }
    }

    private static MailEntry ReadEntry(BinReader s)
    {
        var e = new MailEntry();
        e.MailId = s.ReadInt();                    // i32 id
        e.SenderName = s.ReadString();             // str expéditeur
        e.SendTime = s.ReadInt();                  // i32 unix envoi
        e.Subject = s.ReadString();                // str sujet
        for (var i = 0; i < 5; i++) s.ReadShort(); // 5×s16 natifs
        e.Flag = s.ReadByte();                     // u8 flag (bit0=lu)
        for (var i = 0; i < 7; i++) s.ReadShort(); // 7×s16 natifs
        e.Content = s.ReadString();                // str corps
        e.AttachItemId = s.ReadInt();              // i32 item joint
        e.AttachQty = s.ReadInt();                 // i32 quantité
        for (var i = 0; i < 14; i++) s.ReadShort();// 14×s16 item natifs
        e.ExpireTime = s.ReadLong();               // i64 unix expiration
        s.ReadUShort();                            // u16
        e.AttachGold = s.ReadInt();                // i32 or joint (u32)
        for (var i = 0; i < 6; i++) s.ReadUShort();// 6×u16 natifs
        for (var i = 0; i < 2; i++) s.ReadShort(); // 2×s16
        s.ReadUShort();                            // u16
        for (var i = 0; i < 3; i++) s.ReadShort(); // 3×s16
        for (var i = 0; i < 2; i++) s.ReadUShort();// 2×u16
        for (var i = 0; i < 2; i++) s.ReadShort(); // 2×s16
        s.ReadByte();                              // u8
        s.ReadUShort();                            // u16
        for (var i = 0; i < 2; i++) s.ReadShort(); // 2×s16
        s.ReadUShort();                            // u16 final
        return e;
    }
}

/// <summary>S→C NCID 401 — ClientMailRead : la zone forwarde
/// `body[6..]` de la réponse mission 69 = `[s16 res]` (record natif
/// riche non reproduit — HYPOTHESIS serveur, parse tolérant).</summary>
public sealed class ZoneMailReadResult : NetMessage
{
    public override int Opcode => 401;
    public override string Name => "Zone.ClientMailRead";
    /// <summary>s16 — résultat (0 = ok).</summary>
    public short Result { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(Result);
    public override void Deserialize(BinReader s)
    { if (s.Remaining >= 2) Result = s.ReadShort(); }
}

/// <summary>S→C NCID 402 — ClientMailDeleteResult `[s16 res][i32 id]`
/// (res = 1 si ok — `ok ? 1 : 0` côté ForwardMailDeleteResult).</summary>
public sealed class ZoneMailDeleteResult : NetMessage
{
    public override int Opcode => 402;
    public override string Name => "Zone.ClientMailDeleteResult";
    /// <summary>s16 — 1 = supprimé.</summary>
    public short Result { get; set; }
    /// <summary>i32 — id du courrier concerné.</summary>
    public int MailId { get; set; }
    public override void Serialize(BinWriter s)
    { s.WriteShort(Result); s.WriteInt(MailId); }
    public override void Deserialize(BinReader s)
    { Result = s.ReadShort(); if (s.Remaining >= 4) MailId = s.ReadInt(); }
}

/// <summary>S→C NCID 403 — ClientMailSendResult `[s16 res]` —
/// 0 = envoyé (Value=0 dans HandleMailSendAsync ; le forward mission 71
/// utilise body[6..8], inconnu — tolérant).</summary>
public sealed class ZoneMailSendResult : NetMessage
{
    public override int Opcode => 403;
    public override string Name => "Zone.ClientMailSendResult";
    /// <summary>s16 — 0 = succès.</summary>
    public short Result { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(Result);
    public override void Deserialize(BinReader s)
    { if (s.Remaining >= 2) Result = s.ReadShort(); }
}

// NCID 404 ClientMailReceive `[s16][s16]` : codec déjà présent dans
// ZoneMessages.cs (ZoneMailReceive, zsniff `9401 0000 0000` —
// notification d'arrivée d'un courrier ; la mission C# actuelle ne
// l'émet pas, le bind est prêt pour le jour où).

/// <summary>S→C NCID 405 — ClientMailGetItemResult `[i32 res]`
/// (1 = ok ; l'item arrive au sac via 274 déjà bindé).</summary>
public sealed class ZoneMailGetItemResult : NetMessage
{
    public override int Opcode => 405;
    public override string Name => "Zone.ClientMailGetItemResult";
    /// <summary>i32 — 1 = PJ récupérée.</summary>
    public int Result { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Result);
    public override void Deserialize(BinReader s)
    { if (s.Remaining >= 4) Result = s.ReadInt(); }
}

/// <summary>S→C NCID 406 — ClientMailGetGoldResult `[i32 res]`
/// (1 = ok ; l'or arrive via 280 PlayerAttrSynchro déjà bindé).</summary>
public sealed class ZoneMailGetGoldResult : NetMessage
{
    public override int Opcode => 406;
    public override string Name => "Zone.ClientMailGetGoldResult";
    /// <summary>i32 — 1 = or récupéré.</summary>
    public int Result { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Result);
    public override void Deserialize(BinReader s)
    { if (s.Remaining >= 4) Result = s.ReadInt(); }
}

/// <summary>S→C NCID 409 — ClientMailReturnResult `[s16 res]` (1 = ok).</summary>
public sealed class ZoneMailReturnResult : NetMessage
{
    public override int Opcode => 409;
    public override string Name => "Zone.ClientMailReturnResult";
    /// <summary>s16 — 1 = renvoyé à l'expéditeur.</summary>
    public short Result { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(Result);
    public override void Deserialize(BinReader s)
    { if (s.Remaining >= 2) Result = s.ReadShort(); }
}

// ---------------------------------------------------------------- état ---

/// <summary>
/// État de la boîte aux lettres — alimenté sur le thread principal par
/// les binds <c>GameScene.RegisterZoneDispatch</c> ; lu par
/// <c>MailWindow</c>. Envoi des requêtes via
/// <see cref="MailWindow"/> (SessionCarry.Session).
/// </summary>
public static class MailState
{
    /// <summary>Émis quand la liste/le statut change (main thread).</summary>
    public static event Action? Changed;
    /// <summary>Émis quand un 399 demande l'ouverture de la fenêtre.</summary>
    public static event Action? Opened;
    /// <summary>Émis quand un 404 signale un nouveau courrier.</summary>
    public static event Action? NewMail;

    /// <summary>Uid du NPC courrier ayant ouvert la boîte (0 = aucun).</summary>
    public static int NpcUid { get; private set; }
    /// <summary>True tant que la boîte est considérée ouverte.</summary>
    public static bool IsOpen => _opened;
    /// <summary>Courriers de la boîte (dernier 400 reçu).</summary>
    public static IReadOnlyList<MailEntry> Mails => _mails;
    /// <summary>Dernier code résultat d'envoi (-1 = aucun).</summary>
    public static int LastSendResult { get; private set; } = -1;
    /// <summary>Id du courrier dont la PJ item est en cours de prise —
    /// posé par la fenêtre avant le 100 (le 405 ne porte pas l'id).</summary>
    public static int PendingGetItemId { get; set; }
    /// <summary>Id du courrier dont l'or est en cours de prise (406).</summary>
    public static int PendingGetGoldId { get; set; }
    /// <summary>Id du courrier en cours de renvoi (409).</summary>
    public static int PendingReturnId { get; set; }

    private static readonly List<MailEntry> _mails = new();
    private static bool _opened;

    /// <summary>399 reçu : la zone ouvre la boîte (main thread).</summary>
    public static void OnOpenBox(int npcUid)
    {
        var was = _opened;
        _opened = true;
        NpcUid = npcUid;
        if (!was) Opened?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>400 reçu : remplace la liste (main thread).</summary>
    public static void OnMailList(ZoneMailList m)
    {
        _mails.Clear();
        _mails.AddRange(m.Entries);
        Changed?.Invoke();
    }

    /// <summary>Marque un courrier lu localement (optimiste après 97).</summary>
    public static void MarkRead(int mailId)
    {
        var e = _mails.Find(x => x.MailId == mailId);
        if (e == null) return;
        e.Flag |= 1;
        Changed?.Invoke();
    }

    /// <summary>402 : supprime le courrier de la liste si succès.</summary>
    public static void OnDeleteResult(ZoneMailDeleteResult m)
    {
        if (m.Result != 1) return;
        _mails.RemoveAll(x => x.MailId == m.MailId);
        Changed?.Invoke();
    }

    /// <summary>403 : mémorise le résultat d'envoi (0 = succès).</summary>
    public static void OnSendResult(ZoneMailSendResult m)
    {
        LastSendResult = m.Result;
        Changed?.Invoke();
    }

    /// <summary>405 : PJ item prise — vide la PJ du courrier dont
    /// l'id a été posé dans <see cref="PendingGetItemId"/>.</summary>
    public static void OnGetItemResult(ZoneMailGetItemResult m)
    {
        if (m.Result != 1) return;
        var e = _mails.Find(x => x.MailId == PendingGetItemId);
        if (e != null) { e.AttachItemId = 0; e.AttachQty = 0; }
        PendingGetItemId = 0;
        Changed?.Invoke();
    }

    /// <summary>406 : PJ or prise — vide l'or du courrier.</summary>
    public static void OnGetGoldResult(ZoneMailGetGoldResult m)
    {
        if (m.Result != 1) return;
        var e = _mails.Find(x => x.MailId == PendingGetGoldId);
        if (e != null) e.AttachGold = 0;
        PendingGetGoldId = 0;
        Changed?.Invoke();
    }

    /// <summary>409 : courrier renvoyé — le retire de la liste.</summary>
    public static void OnReturnResult(ZoneMailReturnResult m)
    {
        if (m.Result != 1) return;
        _mails.RemoveAll(x => x.MailId == PendingReturnId);
        PendingReturnId = 0;
        Changed?.Invoke();
    }

    /// <summary>404 : nouveau courrier → notifie (requery à faire par l'UI).</summary>
    public static void OnReceive() => NewMail?.Invoke();

    /// <summary>Peuple l'état pour démonstration (hud-open hors live).</summary>
    public static void SetMock(IEnumerable<MailEntry> mails)
    {
        _mails.Clear();
        _mails.AddRange(mails);
        _opened = true;
        if (NpcUid == 0) NpcUid = -1;
        Opened?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Ferme la boîte (sortie de node, fermeture fenêtre).</summary>
    public static void Close()
    {
        if (!_opened) return;
        _opened = false;
        NpcUid = 0;
        Changed?.Invoke();
    }
}
