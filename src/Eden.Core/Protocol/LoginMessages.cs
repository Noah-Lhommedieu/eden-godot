using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages du LoginServer (:6543) — layouts copiés depuis
// $EDEN/server/src/Eden.Core/Networking/Messages/Generated/LoginMessages.g.cs
// et vérifiés contre captures/mitm_native_entry.log.

/// <summary>
/// S→C — salut serveur. Source : CNC_CL_ServerHello (LoginMessages.g.cs).
/// [str]
/// </summary>
public sealed class LoginServerHello : NetMessage
{
    public override int Opcode => 0;
    public override string Name => "Login.ServerHello";
    public string Value { get; set; } = "";
    public override void Serialize(BinWriter s) => s.WriteString(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadString();
}

/// <summary>
/// C→S — authentification compte.
/// Source : CNC_CL_ServerLoginAccount (LoginMessages.g.cs) +
/// mitm_native_entry.log ligne 2 :
///   NCID=1 [str account][str md5(password) 32 hex][str version "002.003.05.67"]
/// </summary>
public sealed class LoginAccount : NetMessage
{
    public override int Opcode => 1;
    public override string Name => "Login.LoginAccount";
    public string Account { get; set; } = "";
    /// <summary>md5(password) en hex minuscule — le serveur accepte
    /// plaintext ou md5(stocké) (LoginPacketHandlers.cs ~l.123).</summary>
    public string PasswordHash { get; set; } = "";
    public string Version { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteString(Account);
        s.WriteString(PasswordHash);
        s.WriteString(Version);
    }
    public override void Deserialize(BinReader s)
    {
        Account = s.ReadString();
        PasswordHash = s.ReadString();
        Version = s.ReadString();
    }
}

/// <summary>
/// C→S — sélection d'un channel/monde.
/// DIVERGENCE vs LoginMessages.g.cs (qui n'a que [short][string]) :
/// le client natif envoie TROIS champs sur le wire — capture
/// mitm_native_entry.log ligne 4 :
///   NCID=2 [i16 worldId=1010][str account][str md5("")]
/// Le serveur ne lit que worldId + la 1re string (Deserialize g.cs) et
/// ignore le reliquat. La 3e string observée = d41d8cd98f00b204e9800998ecf8427e
/// = md5("") — le client natif n'envoie pas le mot de passe à cette étape.
/// </summary>
public sealed class LoginWorldSelect : NetMessage
{
    public override int Opcode => 2;
    public override string Name => "Login.WorldSelect";
    public short WorldId { get; set; }
    public string Account { get; set; } = "";
    /// <summary>Valeur capturée = md5("") — reproduite à l'identique.</summary>
    public string ExtraHash { get; set; } = "d41d8cd98f00b204e9800998ecf8427e";
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(WorldId);
        s.WriteString(Account);
        s.WriteString(ExtraHash);
    }
    public override void Deserialize(BinReader s)
    {
        WorldId = s.ReadShort();
        Account = s.ReadString();
        ExtraHash = s.ReadString();
    }
}

/// <summary>
/// S→C — réponse du LoginServer à WorldSelect : [i16 worldId][str ip]
/// (HandleLoginWorldAsync envoie CNC_CL_ServerLoginWorld avant le ticket).
/// </summary>
public sealed class LoginWorldReply : NetMessage
{
    public override int Opcode => 2;
    public override string Name => "Login.WorldReply";
    public short WorldId { get; set; }
    public string Address { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(WorldId);
        s.WriteString(Address);
    }
    public override void Deserialize(BinReader s)
    {
        WorldId = s.ReadShort();
        Address = s.ReadString();
    }
}

/// <summary>
/// S→C — ClientHello f32 (=9.9 côté C# ; 6.666 sur le natif —
/// mitm ligne 1 : 030066661e41).
/// Source : CNC_CL_ClientHello (LoginMessages.g.cs).
/// </summary>
public sealed class LoginClientHello : NetMessage
{
    public override int Opcode => 3;
    public override string Name => "Login.ClientHello";
    public float Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteFloat(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadFloat();
}

/// <summary>
/// S→C — échec de login. Source : CNC_CL_ClientLoginFail. [i16 code]
/// </summary>
public sealed class LoginFail : NetMessage
{
    public override int Opcode => 4;
    public override string Name => "Login.LoginFail";
    public short Code { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(Code);
    public override void Deserialize(BinReader s) => Code = s.ReadShort();
}

/// <summary>
/// S→C — liste des mondes/channels.
/// Source : CNC_CL_ClientGetWorldServers (LoginMessages.g.cs) :
///   i16 count + count × (i16 id, str name, str ip, i16 port, i32 online,
///   i16 maxnum, i16 state, i16 showOrder, i16 flags, i16 chars) + i16 lastWorldId
/// Vérifié : mitm ligne 3 (2 mondes Ch01 id=1010, Ch02 id=1020, lastWorld=1010).
/// </summary>
public sealed class WorldList : NetMessage
{
    public override int Opcode => 5;
    public override string Name => "Login.WorldList";

    public sealed class Entry
    {
        public short Id;
        public string Name = "";
        public string Ip = "";
        public short Port;
        public int Online;
        public short MaxNum;
        public short State;
        public short ShowOrder;
        public short Flags;
        public short CharacterCount;
    }

    public List<Entry> Worlds { get; } = new();
    public short LastWorldId { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Worlds.Count);
        foreach (var w in Worlds)
        {
            s.WriteShort(w.Id);
            s.WriteString(w.Name);
            s.WriteString(w.Ip);
            s.WriteShort(w.Port);
            s.WriteInt(w.Online);
            s.WriteShort(w.MaxNum);
            s.WriteShort(w.State);
            s.WriteShort(w.ShowOrder);
            s.WriteShort(w.Flags);
            s.WriteShort(w.CharacterCount);
        }
        s.WriteShort(LastWorldId);
    }

    public override void Deserialize(BinReader s)
    {
        var count = s.ReadShort();
        for (var i = 0; i < count; i++)
        {
            Worlds.Add(new Entry
            {
                Id = s.ReadShort(),
                Name = s.ReadString(),
                Ip = s.ReadString(),
                Port = s.ReadShort(),
                Online = s.ReadInt(),
                MaxNum = s.ReadShort(),
                State = s.ReadShort(),
                ShowOrder = s.ReadShort(),
                Flags = s.ReadShort(),
                CharacterCount = s.ReadShort(),
            });
        }
        LastWorldId = s.ReadShort();
    }
}

/// <summary>
/// S→C — ticket 22 octets vers le WorldServer.
/// Source : CNC_CL_ClientReceiveTicketToWorldServer (LoginMessages.g.cs) +
/// mitm ligne 7. CTicket natif :
///   [u32 key/accountId][u32 clientIp][u32 worldIp][u16 worldPort]
///   [u32 microtime][u32 random]
/// </summary>
public sealed class WorldTicket : NetMessage
{
    public override int Opcode => 6;
    public override string Name => "Login.WorldTicket";
    public const int TicketLength = 22;
    public byte[] Ticket { get; set; } = new byte[TicketLength];

    /// <summary>Adresse du monde embarquée dans le ticket (offset 8/12).</summary>
    public (string ip, ushort port) WorldEndPoint()
    {
        if (Ticket.Length < 14) return ("127.0.0.1", (ushort)5200);
        var ip = $"{Ticket[8]}.{Ticket[9]}.{Ticket[10]}.{Ticket[11]}";
        var port = (ushort)(Ticket[12] | (Ticket[13] << 8));
        return (ip, port);
    }

    public override void Serialize(BinWriter s) => s.WriteBytes(Ticket);
    public override void Deserialize(BinReader s) => Ticket = s.ReadBytes(TicketLength);
}
