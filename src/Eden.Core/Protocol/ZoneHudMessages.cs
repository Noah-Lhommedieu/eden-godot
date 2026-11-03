using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages zone nécessaires au HUD (chat, vitals, XP, inventaire).
//
// Sources de preuve :
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.Chat.cs — wire natif du
//     chat C→S (désasm Deserialize @0x82415xx) : le .g.cs du serveur
//     est FAUX pour les 8 derniers octets du 34 (u32 blobLen + blob,
//     pas « senderId »).
//   - $EDEN/server/src/Eden.Zone/Codecs/NativeItemCodec.cs — format
//     item/inventaire prouvé byte-à-byte sur captures natives.
//   - $EDEN/server/.../ZoneMessages.g.cs pour les layouts triviaux.
//   - docs/generated/ncid_map.json pour les numéros NCID.

// ---------- C→S ----------

/// <summary>C→S NCID 34 — ChannelMsg : message de chat.
/// Wire NATIF (désasm zone Deserialize @0x82415xx + exécuteur C#
/// ZoneEntryFlow.Chat.cs — le .g.cs généré trompe : les 2×s16 +
/// u32+blob finaux sont obligatoires) :
/// `[u8 channel][str target][str msg][s16][s16][u32 blobLen][blob]`.
/// Canaux (enum ChatChannelType du monde C#) : 0 say, 1 whisper/peer,
/// 2 party, 3 guild, 4 shout/zone, 5 world, 6 system/buzz.</summary>
public sealed class ZoneChannelMsg : NetMessage
{
    public override int Opcode => 34;
    public override string Name => "Zone.ZoneServerChannelMsg";
    /// <summary>u8 — canal (0=say…6=buzz).</summary>
    public byte Channel { get; set; }
    /// <summary>str — cible (nom du destinataire pour whisper, vide sinon).</summary>
    public string Target { get; set; } = "";
    /// <summary>str — texte du message.</summary>
    public string Message { get; set; } = "";
    /// <summary>Blob lié (item-link…) — vide pour le chat simple.</summary>
    public byte[] Blob { get; set; } = [];

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Channel);          // u8 — canal
        s.WriteString(Target);         // str — cible
        s.WriteString(Message);        // str — message
        s.WriteShort(0);               // s16 — usage inconnu (natif lit+ignore)
        s.WriteShort(0);               // s16 — idem
        s.WriteInt(Blob.Length);       // u32 — taille du blob lié
        s.WriteBytes(Blob);            // blob
    }
    public override void Deserialize(BinReader s)
    {
        Channel = s.ReadByte();
        Target = s.ReadString();
        Message = s.ReadString();
        s.ReadShort();
        s.ReadShort();
        var len = s.ReadInt();
        Blob = len > 0 && s.Remaining >= len ? s.ReadBytes(len) : [];
    }
}

// ---------- S→C : chat / texte système ----------

/// <summary>S→C NCID 268 — ClientSay : message « say » d'un joueur
/// proche. CNC_CZ_ClientSay — `[u32 charId][str msg][u32 len][blob]`
/// (ZoneEntryFlow.Chat.cs émet charId+msg+len+blob).</summary>
public sealed class ZoneClientSay : NetMessage
{
    public override int Opcode => 268;
    public override string Name => "Zone.ClientSay";
    /// <summary>u32 — charId de l'émetteur.</summary>
    public uint CharId { get; set; }
    /// <summary>str — texte.</summary>
    public string Message { get; set; } = "";
    /// <summary>Blob lié (item-link), vide en say simple.</summary>
    public byte[] Blob { get; set; } = [];

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)CharId);       // u32 — émetteur
        s.WriteString(Message);        // str — texte
        s.WriteInt(Blob.Length);       // u32 — taille blob
        s.WriteBytes(Blob);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadUInt();
        Message = s.ReadString();
        var len = s.ReadInt();
        Blob = len > 0 && s.Remaining >= len ? s.ReadBytes(len) : [];
    }
}

/// <summary>S→C NCID 327 — ClientChannelMsg : message sur un canal
/// (whisper/party/guild/world…). CNC_CZ_ClientChannelMsg —
/// `[u8 channel][str sender][str msg][u32 len][blob]`
/// (ZoneEntryFlow.Chat.cs émet ch+name+msg+blob).</summary>
public sealed class ZoneClientChannelMsg : NetMessage
{
    public override int Opcode => 327;
    public override string Name => "Zone.ClientChannelMsg";
    /// <summary>u8 — canal.</summary>
    public byte Channel { get; set; }
    /// <summary>str — nom de l'émetteur.</summary>
    public string Sender { get; set; } = "";
    /// <summary>str — texte.</summary>
    public string Message { get; set; } = "";
    /// <summary>Blob lié (item-link).</summary>
    public byte[] Blob { get; set; } = [];

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Channel);
        s.WriteString(Sender);
        s.WriteString(Message);
        s.WriteInt(Blob.Length);
        s.WriteBytes(Blob);
    }
    public override void Deserialize(BinReader s)
    {
        Channel = s.ReadByte();
        Sender = s.ReadString();
        Message = s.ReadString();
        var len = s.ReadInt();
        Blob = len > 0 && s.Remaining >= len ? s.ReadBytes(len) : [];
    }
}

/// <summary>S→C NCID 266 — ClientTextOutput : texte système affiché
/// dans le chat. CNC_CZ_ClientTextOutput `[u8 type][str]`.</summary>
public sealed class ZoneClientTextOutput : NetMessage
{
    public override int Opcode => 266;
    public override string Name => "Zone.ClientTextOutput";
    /// <summary>u8 — type/canal du texte.</summary>
    public byte Kind { get; set; }
    /// <summary>str — texte.</summary>
    public string Text { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Kind);
        s.WriteString(Text);
    }
    public override void Deserialize(BinReader s)
    {
        Kind = s.ReadByte();
        Text = s.ReadString();
    }
}

/// <summary>S→C NCID 316 — ClientTextOutputIndex : texte système par
/// index de table. CNC_CZ_ClientTextOutputIndex `[s16][str]`.</summary>
public sealed class ZoneClientTextOutputIndex : NetMessage
{
    public override int Opcode => 316;
    public override string Name => "Zone.ClientTextOutputIndex";
    /// <summary>s16 — index du texte.</summary>
    public short Index { get; set; }
    /// <summary>str — paramètre/texte.</summary>
    public string Text { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Index);
        s.WriteString(Text);
    }
    public override void Deserialize(BinReader s)
    {
        Index = s.ReadShort();
        Text = s.ReadString();
    }
}

/// <summary>S→C NCID 474 — ClientScreenMessage : message système rouge
/// centré à l'écran (« You have acquired… »). CNC_CZ_ClientScreenMessage
/// `[s16][str]`.</summary>
public sealed class ZoneClientScreenMessage : NetMessage
{
    public override int Opcode => 474;
    public override string Name => "Zone.ClientScreenMessage";
    /// <summary>s16 — type/durée.</summary>
    public short Kind { get; set; }
    /// <summary>str — texte.</summary>
    public string Text { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Kind);
        s.WriteString(Text);
    }
    public override void Deserialize(BinReader s)
    {
        Kind = s.ReadShort();
        Text = s.ReadString();
    }
}

/// <summary>S→C NCID 600 — UpdateAttrNewExp : XP du personnage.
/// CNC_CZ_ClientUpdateAttrNewExp `[i32 handle][i64 exp]` (corps 12B
/// capturé — ZoneEntryFlow.Combat.Rewards.cs).</summary>
public sealed class ZoneUpdateAttrNewExp : NetMessage
{
    public override int Opcode => 600;
    public override string Name => "Zone.ClientUpdateAttrNewExp";
    /// <summary>i32 — handle self.</summary>
    public int Handle { get; set; }
    /// <summary>i64 — XP courante.</summary>
    public long Exp { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteLong(Exp);
    }
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        Exp = s.ReadLong();
    }
}

// ---------- S→C : missions (348/391/388) ----------

/// <summary>S→C NCID 348 — ClientMissionStates : registre complet des
/// états de quêtes du personnage (réponse au C→S 50 QueryMissionStates
/// du burst d'entrée). CNC_CZ_ClientMissionStates, Serialize natif
/// @0x8202790 — corps PROUVÉ par le serveur
/// (ZoneEntryFlow.Mission.cs ~l.815-933, aligné sur la capture
/// {1012:3,1537:0}…{1012,0×9}) :
///   [s16 n][n×(u16 mid, u16 state)]  map registre FMS complet
///   [s16 n][n×(u16 mid, s16 prog)]   quêtes en cours (FMS 3/4)
///   [s16 n][n×(u16 mid, s16)]        PMS clé ≤ 0xFFFF
///   [s16 n][n×(u32 key, u16)]        PMS clés instance >0xFFFF
///   [s16 n][n×u8]                    bitvec quêtes « done »
///   [s16 n][n×s16]                   ids « checked » (FMS 5)
///   [u16 n][n×i32]                   10 slots mission_trace.</summary>
public sealed class ZoneMissionStates : NetMessage
{
    public override int Opcode => 348;
    public override string Name => "Zone.ClientMissionStates";
    /// <summary>Registre FMS complet : missionId → état (0..7 —
    /// 1=done, 3/4=en cours, 5=checked).</summary>
    public List<(ushort Mid, ushort State)> Fms { get; } = new();
    /// <summary>Quêtes en cours : missionId → compteur progression.</summary>
    public List<(ushort Mid, short Progress)> InProgress { get; } = new();
    /// <summary>PMS clés ≤ 0xFFFF : missionId → packed status.</summary>
    public List<(ushort Mid, short State)> Pms16 { get; } = new();
    /// <summary>Signal minimal serveur→client : entrée Pms16 réservée
    /// mid=0 (jamais une vraie quête) state≠0 = Portal Stone gratuite
    /// (Privilege>0, voir ZoneEntryFlow.PortalStone).</summary>
    public bool FreePortal => Pms16.Any(p => p.Mid == 0 && p.State != 0);
    /// <summary>PMS clés d'instance > 0xFFFF (0x3c52xxxx natif).</summary>
    public List<(uint Key, ushort State)> Pms32 { get; } = new();
    /// <summary>Bitvec AllFMSIndex — quêtes terminées par ordinal.</summary>
    public List<byte> FmsBitVec { get; } = new();
    /// <summary>Ids des quêtes marquées « checked » (FMS==5).</summary>
    public List<short> CheckedIds { get; } = new();
    /// <summary>10 slots de suivi du HUD (0 = vide).</summary>
    public List<int> Trace { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Fms.Count);
        foreach (var (m, st) in Fms)
        { s.WriteShort((short)m); s.WriteShort((short)st); }
        s.WriteShort((short)InProgress.Count);
        foreach (var (m, p) in InProgress)
        { s.WriteShort((short)m); s.WriteShort(p); }
        s.WriteShort((short)Pms16.Count);
        foreach (var (m, st) in Pms16)
        { s.WriteShort((short)m); s.WriteShort(st); }
        s.WriteShort((short)Pms32.Count);
        foreach (var (k, st) in Pms32)
        { s.WriteInt((int)k); s.WriteShort((short)st); }
        s.WriteShort((short)FmsBitVec.Count);
        foreach (var b in FmsBitVec) s.WriteByte(b);
        s.WriteShort((short)CheckedIds.Count);
        foreach (var id in CheckedIds) s.WriteShort(id);
        s.WriteShort((short)Trace.Count);
        foreach (var t in Trace) s.WriteInt(t);
    }

    public override void Deserialize(BinReader s)
    {
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++)
            Fms.Add(((ushort)s.ReadShort(), (ushort)s.ReadShort()));
        n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++)
            InProgress.Add(((ushort)s.ReadShort(), s.ReadShort()));
        n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++)
            Pms16.Add(((ushort)s.ReadShort(), s.ReadShort()));
        n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 6; i++)
            Pms32.Add((s.ReadUInt(), (ushort)s.ReadShort()));
        n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 1; i++)
            FmsBitVec.Add(s.ReadByte());
        n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 2; i++)
            CheckedIds.Add(s.ReadShort());
        n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++)
            Trace.Add(s.ReadInt());
    }
}

/// <summary>S→C NCID 391 — UpdateMissionState : mise à jour unitaire
/// d'un état de quête. CNC_CZ_ClientUpdateMissionState — layout
/// ZoneMessages.g.cs serveur : `[u8 op][u32 key][u16 mid][s16 state]`
/// (relayé tel quel par la mission, ZoneEntryFlow.Mission.cs case 67).</summary>
public sealed class ZoneUpdateMissionState : NetMessage
{
    public override int Opcode => 391;
    public override string Name => "Zone.ClientUpdateMissionState";
    /// <summary>u8 — sous-opération mission (type d'update).</summary>
    public byte Op { get; set; }
    /// <summary>u32 — clé (mid ou clé d'instance 0x3c52xxxx).</summary>
    public uint Key { get; set; }
    /// <summary>u16 — mission id associé.</summary>
    public ushort MissionId { get; set; }
    /// <summary>s16 — nouvel état/progression.</summary>
    public short State { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Op);                 // u8 — op
        s.WriteInt((int)Key);            // u32 — clé
        s.WriteShort((short)MissionId);  // u16 — mid
        s.WriteShort(State);             // s16 — état
    }
    public override void Deserialize(BinReader s)
    {
        Op = s.ReadByte();
        Key = s.ReadUInt();
        MissionId = (ushort)s.ReadShort();
        State = s.ReadShort();
    }
}

/// <summary>S→C NCID 388 — EnterMissionList : notification liste de
/// missions (CMissionShop → affichage liste NPC). Layout
/// ZoneMessages.g.cs : `[s16 op][i32 arg][i32 npcUid][s16 arg3]`.</summary>
public sealed class ZoneEnterMissionList : NetMessage
{
    public override int Opcode => 388;
    public override string Name => "Zone.ClientEnterMissionList";
    /// <summary>s16 — sous-opération.</summary>
    public short Op { get; set; }
    /// <summary>i32 — argument (op-dépendant).</summary>
    public int Arg { get; set; }
    /// <summary>i32 — uid NPC émetteur de la liste.</summary>
    public int NpcUid { get; set; }
    /// <summary>s16 — argument fin.</summary>
    public short Arg3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Op); s.WriteInt(Arg);
        s.WriteInt(NpcUid); s.WriteShort(Arg3);
    }
    public override void Deserialize(BinReader s)
    {
        Op = s.ReadShort(); Arg = s.ReadInt();
        NpcUid = s.ReadInt(); Arg3 = s.ReadShort();
    }
}

// ---------- S→C : inventaire (312) ----------

/// <summary>Item d'inventaire décodé du corps 312 —
/// <paramref name="Amount"/> = bit6 du mask (quantité empilée, 0/1
/// pour les non-empilables), <paramref name="Strengthen"/> = bit19
/// (+N de renforçage).</summary>
public sealed record InventorySlot(int Bag, int Slot, int ItemId,
    int Durability, int MaxDurability, byte Flags, int Amount = 0,
    int Strengthen = 0);

/// <summary>
/// Parseur du corps NCID 312 <c>ClientEnterNodeInventory</c> — port
/// client du codec serveur prouvé
/// <c>$EDEN/server/src/Eden.Zone/Codecs/NativeItemCodec.cs</c>
/// (désasm Serialize @0x8233xxx, validé byte-à-byte sur captures).
///
/// Wire : `[u8 nBags] { [u16 capacity][u8 containerIdx][s16 bmpLen]
/// [bitmap MSB-first] { records item pour chaque bit levé, dans
/// l'ordre des slots } } ×nBags` puis `[u8 nHolders] { [u8 idx]
/// [u16 nSlots] { u16 itemId (0=vide) + [str maker][s16][u8 flags]
/// si occupé } }`.
///
/// Record item (WriteItem @0x80e3xxx) : `[u16 itemId][s16 f1][s16 f2]
/// [str maker][u8 flags][u24 mask][champs conditionnels][34B tail]` :
///   bits0-5  : s16 embedded_id     bit6  : s16 embedded_amount
///   bit7     : s16 combo           bit8  : u32 create_time
///   bit9     : u16 clone+0x1c      bits10-18 : u16 extra_combo×9
///   bit19    : s16 strengthen      bit20 : s16 clone+0x2e
///   bit21    : i64 due_date
/// </summary>
public static class NativeInventoryParser
{
    /// <summary>Parse le corps du 312 (sans le NCID) → slots occupés.
    /// Robuste aux troncatures : s'arrête proprement sur un corps court.</summary>
    /// <summary>Capacité réelle (slots) de chaque sac d'après le 312
    /// (sac de base + capacité des sacs équipés) — conteneur → slots.</summary>
    public static readonly Dictionary<int, int> Capacities = new();

    /// <summary>Diagnostic : position où le dernier <see cref="Parse"/> a
    /// dû s'arrêter (record item illisible), null si parse complet.</summary>
    public static string? LastFailure { get; private set; }

    public static List<InventorySlot> Parse(byte[] body)
    {
        var items = new List<InventorySlot>();
        LastFailure = null;
        var r = new BinReader(body);
        if (r.Remaining < 1) return items;
        var nBags = r.ReadByte();
        for (var bag = 0; bag < nBags; bag++)
        {
            if (r.Remaining < 5) break;
            var capacity = r.ReadUShort();      // u16 — slots du sac
            var containerIdx = r.ReadByte();    // u8 — index conteneur
            var bmpLen = r.ReadUShort();        // s16 — octets bitmap
            if (r.Remaining < bmpLen) break;
            lock (Capacities) Capacities[containerIdx] = capacity;
            var bitmap = r.ReadBytes(bmpLen);
            // Compte les slots occupés (MSB-first : slot i → bit
            // 0x80>>(i%8)) puis lit autant de records item.
            for (var slot = 0; slot < capacity; slot++)
            {
                var byteIdx = slot / 8;
                if (byteIdx >= bitmap.Length) break;
                if ((bitmap[byteIdx] & (0x80 >> (slot & 7))) == 0)
                    continue;
                if (!TryReadItem(r, out var item))
                {
                    LastFailure = $"bag{containerIdx} slot{slot} reste={r.Remaining} lus={items.Count}";
                    return items;
                }
                items.Add(item with { Bag = containerIdx, Slot = slot });
            }
        }
        // Blocs porte-sacs (holders 0=inv, 4=banque) — lus sans
        // produire d'items HUD (les sacs équipés servent à la capacité).
        if (r.Remaining < 1) return items;
        var nHolders = r.ReadByte();
        for (var h = 0; h < nHolders; h++)
        {
            if (r.Remaining < 3) break;
            r.ReadByte();                       // u8 — index holder
            var nSlots = r.ReadUShort();
            for (var s = 0; s < nSlots; s++)
            {
                if (r.Remaining < 2) return items;
                var bagItemId = r.ReadUShort(); // u16 — 0 = slot vide
                if (bagItemId == 0) continue;
                if (r.Remaining < 2) return items;
                var makerLen = r.ReadUShort();  // str maker
                if (r.Remaining < makerLen + 3) return items;
                r.ReadBytes(makerLen);
                r.ReadShort();                  // s16 — capacité sac
                r.ReadByte();                   // u8 — flags
            }
        }
        return items;
    }

    /// <summary>Lit un record item au format WriteItem natif (même
    /// codec que 311/358 — <see cref="NativeItemRecord"/>) ;
    /// <see langword="false"/> si le corps s'arrête au milieu.</summary>
    private static bool TryReadItem(BinReader r, out InventorySlot item)
    {
        if (NativeItemRecord.TryReadInventory(r, out var rec))
        {
            item = new InventorySlot(0, 0, rec.ItemId, rec.Durability,
                rec.MaxDurability, rec.Flags, rec.Amount, rec.Strengthen);
            return true;
        }
        item = new InventorySlot(0, 0, 0, 0, 0, 0);
        return false;
    }
}

// ---------- S→C : données perso (310, suite du préfixe) ----------

/// <summary>Champs décodés de <see cref="ZoneEnterNodePlayerData.Rest"/>
/// — le reste du corps 310 après `[u32 charId][str name]`.</summary>
public sealed record PlayerHudData(
    float Scale, byte RaceId, byte GenderId, int Level, long Hp,
    long Mp, int MaxHp, int MaxMp, int ClassId, byte[] Appearance,
    int ClassLevel, int ClassExp, int ClassPoints);

/// <summary>
/// Parseur du reste du corps NCID 310 <c>ClientEnterNodePlayerData</c>
/// — layout PROUVÉ par le ctor natif @0x867240d (zone/decompiled.c),
/// cité par <c>ZoneEntryFlow.Lifecycle.cs</c> ~l.596-661 du serveur
/// (offsets absolus pour un nom de 5 octets ; relatifs depuis la fin
/// du str name, delta = lenGBK(nom) − 5) :
///   +0  u16 ?          +2  f32 échelle    +6 u8   +7 u8   +8 bin4
///   +12 u16 LEVEL      +14 i64 HP         +22 i64 MP
///   +30 i32 MaxHP      +34 i32 MaxMP      +38 u8 CLASS   +39 u8
///   +40 32B apparence  +72 u16 classLv    +74 i32 classExp
///   +94 i32 classPoints (CP).
/// Les u8 à +6/+7 sont lus comme race/genre (HYPOTHESIS — non patchés
/// par le serveur ; la race fiable vient du CharacterSummary monde).
/// </summary>
public static class NativePlayerDataParser
{
    /// <summary>Parse le reste du 310 (après charId+str) → données
    /// d'identité/vitaux/progression, ou null si le corps est trop
    /// court pour les champs prouvés.</summary>
    public static PlayerHudData? Parse(byte[] rest)
    {
        if (rest.Length < 107) return null;
        var r = new BinReader(rest);
        r.ReadShort();                          // +0 u16 — rôle inconnu
        var scale = r.ReadFloat();              // +2 f32 — échelle modèle
        var raceId = r.ReadByte();              // +6 u8 — HYPOTHESIS race
        var genderId = r.ReadByte();            // +7 u8 — HYPOTHESIS genre
        r.ReadBytes(4);                         // +8 bin4 — rôle inconnu
        var level = (ushort)r.ReadShort();      // +12 u16 — niveau
        var hp = r.ReadLong();                  // +14 i64 — HP courant
        var mp = r.ReadLong();                  // +22 i64 — MP courant
        var maxHp = r.ReadInt();                // +30 i32 — HP max
        var maxMp = r.ReadInt();                // +34 i32 — MP max
        var classId = r.ReadByte();             // +38 u8 — classe active
        r.ReadByte();                           // +39 u8 — rôle inconnu
        var app = r.ReadBytes(32);              // +40 — blob apparence
        var classLv = (ushort)r.ReadShort();    // +72 u16 — niv. classe
        var classExp = r.ReadInt();             // +74 i32 — exp classe
        r.ReadBytes(94 - 78);                   // +78..+93 — non décodé
        var classPoints = r.ReadInt();          // +94 i32 — CP
        return new PlayerHudData(scale, raceId, genderId, level, hp, mp,
            maxHp, maxMp, classId, app, classLv, classExp, classPoints);
    }
}
