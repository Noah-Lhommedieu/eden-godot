using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages du TicketServer (:2067).
// ATTENTION : TicketMessages.g.cs côté serveur a des layouts partiels
// (les classes CNC_T_* générées ne reflètent pas le wire). La référence
// wire réelle = $EDEN/tools/eden_session.py::get_ticket + mitm lignes 5-6
// et zsniff canal auxiliaire :
//   GetTicket  C→S NCID 0 : [u32 id][u32 id2][u32 cip][u32 sip][u16 port]
//   UseTicket  C→S NCID 1 : idem + [u32 t1][u32 t2]  (26 octets de corps)
//   GetTicketReply S→C NCID 2 : même corps 26 octets
//   UseTicketReply S→C NCID 3 : [u32 id][u8 result]

/// <summary>C→S NCID 0 — demande de ticket (18B de corps).</summary>
public sealed class GetTicket : NetMessage
{
    public override int Opcode => 0;
    public override string Name => "Ticket.GetTicket";
    public uint Id;
    public uint Id2;
    public uint ClientIp;
    public uint ServerIp;
    public ushort Port;
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Id);
        s.WriteInt((int)Id2);
        s.WriteInt((int)ClientIp);
        s.WriteInt((int)ServerIp);
        s.WriteShort((short)Port);
    }
    public override void Deserialize(BinReader s)
    {
        Id = (uint)s.ReadInt();
        Id2 = (uint)s.ReadInt();
        ClientIp = (uint)s.ReadInt();
        ServerIp = (uint)s.ReadInt();
        Port = (ushort)s.ReadShort();
    }
}

/// <summary>
/// Corps 26 octets partagé par UseTicket (NCID 1) et GetTicketReply (NCID 2) :
/// [u32 id][u32 id2][u32 cip][u32 sip][u16 port][u32 t1][u32 t2].
/// Le ticket 22B extrait = [id][cip][sip][port][t1][t2]
/// (eden_session.py : ticket = body[0:4] + body[8:26]).
/// </summary>
public abstract class TicketBody : NetMessage
{
    public uint Id;
    public uint Id2;
    public uint ClientIp;
    public uint ServerIp;
    public ushort Port;
    public uint T1;
    public uint T2;

    public byte[] ExtractTicket()
    {
        var w = new BinWriter();
        w.WriteInt((int)Id);
        w.WriteInt((int)ClientIp);
        w.WriteInt((int)ServerIp);
        w.WriteShort((short)Port);
        w.WriteInt((int)T1);
        w.WriteInt((int)T2);
        return w.ToArray(); // 22 octets
    }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Id);
        s.WriteInt((int)Id2);
        s.WriteInt((int)ClientIp);
        s.WriteInt((int)ServerIp);
        s.WriteShort((short)Port);
        s.WriteInt((int)T1);
        s.WriteInt((int)T2);
    }

    public override void Deserialize(BinReader s)
    {
        Id = (uint)s.ReadInt();
        Id2 = (uint)s.ReadInt();
        ClientIp = (uint)s.ReadInt();
        ServerIp = (uint)s.ReadInt();
        Port = (ushort)s.ReadShort();
        T1 = (uint)s.ReadInt();
        T2 = (uint)s.ReadInt();
    }
}

/// <summary>C→S NCID 1 — validation d'un ticket (corps 26B).</summary>
public sealed class UseTicket : TicketBody
{
    public override int Opcode => 1;
    public override string Name => "Ticket.UseTicket";
}

/// <summary>S→C NCID 2 — réponse GetTicket (corps 26B).</summary>
public sealed class GetTicketReply : TicketBody
{
    public override int Opcode => 2;
    public override string Name => "Ticket.GetTicketReply";
}

/// <summary>S→C NCID 3 — résultat UseTicket : [u32 id][u8 result].</summary>
public sealed class UseTicketReply : NetMessage
{
    public override int Opcode => 3;
    public override string Name => "Ticket.UseTicketReply";
    public uint Id;
    public byte Result;
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Id);
        s.WriteByte(Result);
    }
    public override void Deserialize(BinReader s)
    {
        Id = (uint)s.ReadInt();
        Result = s.ReadByte();
    }
}
