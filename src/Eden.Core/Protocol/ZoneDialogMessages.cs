using EdenClient.Net;

namespace EdenClient.Protocol;

// Senders C→S des flux duel / message-board / battlefield — layouts
// verbatim des codecs entrants serveur (`CNE_CZ_*` prouvés natifs).

/// <summary>CNE_CZ_ZoneServerDuelRequest — 135 : défi 1v1
/// [u32 charId cible].</summary>
public sealed class ZoneDuelRequest : NetMessage
{
    public override int Opcode => 135;
    public override string Name => "ZoneServer.DuelRequest";
    public uint TargetId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt((int)TargetId);
    public override void Deserialize(BinReader s) => TargetId = s.ReadUInt();
}

/// <summary>CNE_CZ_ZoneServerDuelRequestAnswer — 136 : réponse au
/// défi [s16 accept][i32 demandeur].</summary>
public sealed class ZoneDuelRequestAnswer : NetMessage
{
    public override int Opcode => 136;
    public override string Name => "ZoneServer.DuelRequestAnswer";
    public short Accept { get; set; }
    public int RequesterId { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Accept); s.WriteInt(RequesterId);
    }
    public override void Deserialize(BinReader s)
    {
        Accept = s.ReadShort(); RequesterId = s.ReadInt();
    }
}

/// <summary>CNE_CZ_ZoneServerMessageListQuery — 138 : liste du
/// panneau d'affichage [s16 page/board].</summary>
public sealed class ZoneMessageListQuery : NetMessage
{
    public override int Opcode => 138;
    public override string Name => "ZoneServer.MessageListQuery";
    public short Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadShort();
}

/// <summary>CNE_CZ_ZoneServerMessageWrite — 139 : écrire un message
/// [str titre][str texte].</summary>
public sealed class ZoneMessageWrite : NetMessage
{
    public override int Opcode => 139;
    public override string Name => "ZoneServer.MessageWrite";
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteString(Title); s.WriteString(Text);
    }
    public override void Deserialize(BinReader s)
    {
        Title = s.ReadString(); Text = s.ReadString();
    }
}

/// <summary>CNE_CZ_ZoneServerMessageRead — 140 : lire un message
/// [i32 id].</summary>
public sealed class ZoneMessageReadRequest : NetMessage
{
    public override int Opcode => 140;
    public override string Name => "ZoneServer.MessageRead";
    public int Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadInt();
}

/// <summary>CNE_CZ_ZoneServerMessageDelete — 141 : supprimer un
/// message [i32 id].</summary>
public sealed class ZoneMessageDelete : NetMessage
{
    public override int Opcode => 141;
    public override string Name => "ZoneServer.MessageDelete";
    public int Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadInt();
}

/// <summary>CNE_CZ_ZoneServerMessageExit — 142 : quitter le
/// panneau (aucun champ).</summary>
public sealed class ZoneMessageExit : NetMessage
{
    public override int Opcode => 142;
    public override string Name => "ZoneServer.MessageExit";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>CNE_CZ_ZoneServerBattlefieldJoin — 144 : rejoindre un
/// champ de bataille [s16×3].</summary>
public sealed class ZoneBattlefieldJoin : NetMessage
{
    public override int Opcode => 144;
    public override string Name => "ZoneServer.BattlefieldJoin";
    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public short Field3 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1); s.WriteShort(Field2); s.WriteShort(Field3);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort(); Field2 = s.ReadShort();
        Field3 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerEnterBattlefieldAnswer — 145 :
/// accepte/décline l'entrée [s16×2].</summary>
public sealed class ZoneEnterBattlefieldAnswer : NetMessage
{
    public override int Opcode => 145;
    public override string Name => "ZoneServer.EnterBattlefieldAnswer";
    public short Field1 { get; set; }
    public short Field2 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1); s.WriteShort(Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort(); Field2 = s.ReadShort();
    }
}

/// <summary>CNE_CZ_ZoneServerBattlefieldLeave — 146 : quitter le
/// champ de bataille (aucun champ).</summary>
public sealed class ZoneBattlefieldLeave : NetMessage
{
    public override int Opcode => 146;
    public override string Name => "ZoneServer.BattlefieldLeave";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}
