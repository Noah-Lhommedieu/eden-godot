using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages du WorldServer (:5200) — layouts copiés depuis
// $EDEN/server/src/Eden.Core/Networking/Messages/Generated/WorldMessages.g.cs
// et vérifiés contre captures/mitm_native_entry.log (préfixes W_*).

/// <summary>C→S NCID 0 — ServerHello / keepalive sur la connexion World.</summary>
public sealed class WorldServerHello : NetMessage
{
    public override int Opcode => 0;
    public override string Name => "World.ServerHello";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>
/// C→S NCID 1 — login monde = CTicket 22B + machineId.
/// Source : CNE_CW_ServerReceiveTicket (WorldMessages.g.cs) +
/// mitm ligne 8 : [22B ticket][str "BC-EC-A0-43-F3-80"].
/// </summary>
public sealed class WorldLoginTicket : NetMessage
{
    public override int Opcode => 1;
    public override string Name => "World.LoginTicket";
    public byte[] Ticket { get; set; } = new byte[22];
    public string MachineId { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteBytes(Ticket);
        s.WriteString(MachineId);
    }
    public override void Deserialize(BinReader s)
    {
        Ticket = s.ReadBytes(22);
        MachineId = s.ReadString();
    }
}

/// <summary>
/// C→S NCID 3 — sélection du personnage.
/// Source : CNE_CW_ServerLoginCharacter (WorldMessages.g.cs),
/// wire [u32 charId][str machineId][s8 flag] — mitm ligne 15.
/// </summary>
public sealed class WorldSelectCharacter : NetMessage
{
    public override int Opcode => 3;
    public override string Name => "World.SelectCharacter";
    public uint CharId;
    public string MachineId { get; set; } = "";
    public sbyte Flag { get; set; } = 1;
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)CharId);
        s.WriteString(MachineId);
        s.WriteByte((byte)Flag);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = (uint)s.ReadInt();
        MachineId = s.ReadString();
        Flag = (sbyte)s.ReadByte();
    }
}

/// <summary>C→S NCID 10 — QueryFamilyInfo [u8]. mitm ligne 33 : 0a0001.</summary>
public sealed class WorldQueryFamilyInfo : NetMessage
{
    public override int Opcode => 10;
    public override string Name => "World.QueryFamilyInfo";
    public byte Value { get; set; } = 1;
    public override void Serialize(BinWriter s) => s.WriteByte(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadByte();
}

/// <summary>C→S NCID 13 — QueryFriendInfo, vide. mitm ligne 32.</summary>
public sealed class WorldQueryFriendInfo : NetMessage
{
    public override int Opcode => 13;
    public override string Name => "World.QueryFriendInfo";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>
/// C→S NCID 17 — ItemMallReceivableQuery [u16][str][str].
/// mitm ligne 30 : 1100 1500 0000 0000 → u16=21, "", "".
/// </summary>
public sealed class WorldItemMallReceivableQuery : NetMessage
{
    public override int Opcode => 17;
    public override string Name => "World.ItemMallReceivableQuery";
    public ushort Field1 { get; set; } = 21;
    public string Field2 { get; set; } = "";
    public string Field3 { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Field1);
        s.WriteString(Field2);
        s.WriteString(Field3);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = (ushort)s.ReadShort();
        Field2 = s.ReadString();
        Field3 = s.ReadString();
    }
}

/// <summary>C→S NCID 25 — ItemMallPointInfo, vide. mitm ligne 31.</summary>
public sealed class WorldItemMallPointInfoQuery : NetMessage
{
    public override int Opcode => 25;
    public override string Name => "World.ItemMallPointInfoQuery";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>
/// C→S NCID 26 — choix de zone [str zone][u8 flag].
/// Le .g.cs nomme ce message CNE_CW_ServerCaptchaReturn — sur le wire
/// le client l'utilise pour demander l'entrée en zone :
/// mitm ligne 20 : 1a00 0400 "3966" 00. Suivi du ticket zone (NCID 37).
/// </summary>
public sealed class WorldSelectZone : NetMessage
{
    public override int Opcode => 26;
    public override string Name => "World.SelectZone";
    public string Zone { get; set; } = "";
    public byte Flag { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteString(Zone);
        s.WriteByte(Flag);
    }
    public override void Deserialize(BinReader s)
    {
        Zone = s.ReadString();
        Flag = s.ReadByte();
    }
}

/// <summary>S→C NCID 34 — ClientHello f32. CNC_CW_ClientHello. mitm ligne 9.</summary>
public sealed class WorldClientHello : NetMessage
{
    public override int Opcode => 34;
    public override string Name => "World.ClientHello";
    public float Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteFloat(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadFloat();
}

/// <summary>S→C NCID 37 — ticket 22B vers le ZoneServer.
/// CNC_CW_ClientReceiveTicketToZoneServer. mitm ligne 25.
/// CTicket : [u32 charId][u32 clientIp][u32 zoneIp][u16 zonePort][u32 t1][u32 t2].</summary>
public sealed class ZoneTicket : NetMessage
{
    public override int Opcode => 37;
    public override string Name => "World.ZoneTicket";
    public const int TicketLength = 22;
    public byte[] Ticket { get; set; } = new byte[TicketLength];

    /// <summary>Adresse de zone embarquée (offset 8/12).</summary>
    public (string ip, ushort port) ZoneEndPoint()
    {
        if (Ticket.Length < 14) return ("127.0.0.1", (ushort)5201);
        var ip = $"{Ticket[8]}.{Ticket[9]}.{Ticket[10]}.{Ticket[11]}";
        var port = (ushort)(Ticket[12] | (Ticket[13] << 8));
        return (ip, port);
    }

    public override void Serialize(BinWriter s) => s.WriteBytes(Ticket);
    public override void Deserialize(BinReader s) => Ticket = s.ReadBytes(TicketLength);
}

/// <summary>
/// S→C NCID 38 — liste des personnages en chunks.
/// Source : CNC_CW_ClientGetCharacters (WorldMessages.g.cs) — layout
/// revérifié octet par octet sur mitm lignes 12-14 (parsé jusqu'au bout) :
///   [u8 count][i16 header] puis count × entrées.
///   header : 0 = début, 1 = données, 2 = fin (section native).
/// </summary>
public sealed class CharacterListChunk : NetMessage
{
    public override int Opcode => 38;
    public override string Name => "World.CharacterListChunk";

    /// <summary>Item d'aperçu = 28 octets : [u16 loc][u16 itemId][s16×8][i64].</summary>
    public sealed class PreviewItem
    {
        public ushort Loc;
        public ushort ItemId;
        public short[] S = new short[8];
        public long L;
    }

    public sealed class Entry
    {
        public int Id;
        public string Name = "";
        public short ClassId;
        public short Feature;   // +0x14 (class_lv) — cf. commentaire g.cs
        public short RaceId;    // +0x10
        public short NodeId;    // +0x0e
        public short Level;     // +0x0c
        public byte B1;         // +0x7d
        public byte[] Appearance = new byte[32]; // +0x24
        public byte B2;         // +0x7c
        public List<KeyValuePair<ushort, List<PreviewItem>>> Preview { get; } = new();
    }

    /// <summary>0 = début, 1 = données, 2 = fin.</summary>
    public short Header;
    public List<Entry> Characters { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteByte((byte)Characters.Count);
        s.WriteShort(Header);
        foreach (var c in Characters)
        {
            s.WriteInt(c.Id);
            s.WriteString(c.Name);
            s.WriteShort(c.ClassId);
            s.WriteShort(c.Feature);
            s.WriteShort(c.RaceId);
            s.WriteShort(c.NodeId);
            s.WriteShort(c.Level);
            s.WriteByte(c.B1);
            s.WriteBytes(c.Appearance);
            s.WriteByte(c.B2);
            s.WriteShort((short)c.Preview.Count);
            foreach (var kv in c.Preview)
            {
                s.WriteShort((short)kv.Key);
                s.WriteByte((byte)kv.Value.Count);
                foreach (var it in kv.Value)
                {
                    s.WriteShort((short)it.Loc);
                    s.WriteShort((short)it.ItemId);
                    for (var i = 0; i < 8; i++) s.WriteShort(it.S[i]);
                    s.WriteLong(it.L);
                }
            }
        }
    }

    public override void Deserialize(BinReader s)
    {
        var count = s.ReadByte();
        Header = s.ReadShort();
        for (var i = 0; i < count; i++)
        {
            var c = new Entry
            {
                Id = s.ReadInt(),
                Name = s.ReadString(),
                ClassId = s.ReadShort(),
                Feature = s.ReadShort(),
                RaceId = s.ReadShort(),
                NodeId = s.ReadShort(),
                Level = s.ReadShort(),
                B1 = s.ReadByte(),
                Appearance = s.ReadBytes(32),
                B2 = s.ReadByte(),
            };
            var mapCount = (ushort)s.ReadShort();
            for (var m = 0; m < mapCount; m++)
            {
                var key = (ushort)s.ReadShort();
                var itemCount = s.ReadByte();
                var items = new List<PreviewItem>(itemCount);
                for (var k = 0; k < itemCount; k++)
                {
                    var it = new PreviewItem
                    {
                        Loc = (ushort)s.ReadShort(),
                        ItemId = (ushort)s.ReadShort(),
                    };
                    for (var x = 0; x < 8; x++) it.S[x] = s.ReadShort();
                    it.L = s.ReadLong();
                    items.Add(it);
                }
                c.Preview.Add(new KeyValuePair<ushort, List<PreviewItem>>(key, items));
            }
            Characters.Add(c);
        }
    }
}

// ------------------------------------------------------------------
// Création / suppression de personnage — layouts des .g.cs serveur
// (CNE_CW_Server* / CNC_CW_Client*) + Eden.World/Program.cs pour le
// séquencement. NCID réels du canal World (les numéros 88/89/90/103
// de la doc de mission étaient des suppositions — non confirmés).
// ------------------------------------------------------------------

/// <summary>
/// C→S NCID 32 — vérification de disponibilité d'un nom [str].
/// CNE_CW_ServerCreateCharacterNameCheck. Réponse : NCID 39.
/// </summary>
public sealed class WorldCreateNameCheck : NetMessage
{
    public override int Opcode => 32;
    public override string Name => "World.CreateNameCheck";
    public string Value { get; set; } = "";
    public override void Serialize(BinWriter s) => s.WriteString(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadString();
}

/// <summary>
/// S→C NCID 39 — résultat nom/création [s16 code][u8 flag].
/// CNC_CW_ClientVerifyCharacterName. Codes natifs (Eden.World) :
/// 0=ok, 3=doublon, 4=nom en cours de création, 5=compte plein (3),
/// 6=déjà en création, 7=données, 8=trop long, 9=trop court,
/// 10=caractères illégaux, 11=nom elfe illégal, 12=indéfini.
/// Flag : 1 si OK après un NameCheck (NCID 32), 0 sinon/après
/// CreateName (NCID 4).
/// </summary>
public sealed class WorldVerifyNameResult : NetMessage
{
    public override int Opcode => 39;
    public override string Name => "World.VerifyNameResult";
    public short Code { get; set; }
    public byte Flag { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Code);
        s.WriteByte(Flag);
    }
    public override void Deserialize(BinReader s)
    {
        Code = s.ReadShort();
        Flag = s.ReadByte();
    }
}

/// <summary>
/// C→S NCID 4 — réservation du nom de personnage [str name][str][u32].
/// CNE_CW_ServerCreateCharacterName. Field2/Field3 : usage natif
/// inconnu (vides/0 dans notre implémentation — HYPOTHESIS).
/// Réponse : NCID 39.
/// </summary>
public sealed class WorldCreateCharacterName : NetMessage
{
    public override int Opcode => 4;
    public override string Name => "World.CreateCharacterName";
    public string Value { get; set; } = "";
    public string Field2 { get; set; } = "";
    public uint Field3 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteString(Value);
        s.WriteString(Field2);
        s.WriteInt((int)Field3);
    }
    public override void Deserialize(BinReader s)
    {
        Value = s.ReadString();
        Field2 = s.ReadString();
        Field3 = (uint)s.ReadInt();
    }
}

/// <summary>
/// C→S NCID 5 — données du personnage à créer
/// [u8 race][u8 class][u8 flag][32B apparence].
/// CNE_CW_ServerCreateCharacterData. Le flag est renvoyé comme
/// « create_flag » (Feature) de l'entrée de liste. Réponse : la liste
/// de persos complète (NCID 38, 3 sections).
/// </summary>
public sealed class WorldCreateCharacterData : NetMessage
{
    public override int Opcode => 5;
    public override string Name => "World.CreateCharacterData";
    public byte Race { get; set; }
    public byte ClassId { get; set; }
    public byte Flag { get; set; }
    public byte[] Appearance { get; set; } = new byte[32];
    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Race);
        s.WriteByte(ClassId);
        s.WriteByte(Flag);
        var app = Appearance;
        if (app.Length != 32)
            Array.Resize(ref app, 32);
        s.WriteBytes(app);
    }
    public override void Deserialize(BinReader s)
    {
        Race = s.ReadByte();
        ClassId = s.ReadByte();
        Flag = s.ReadByte();
        Appearance = s.ReadBytes(32);
    }
}

/// <summary>
/// C→S NCID 6 — suppression de personnage [i32 charId][str name].
/// CNE_CW_ServerRemoveCharacter. Réponse : liste fraîche (NCID 38).
/// </summary>
public sealed class WorldRemoveCharacter : NetMessage
{
    public override int Opcode => 6;
    public override string Name => "World.RemoveCharacter";
    public int CharId { get; set; }
    public string CharName { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId);
        s.WriteString(CharName);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt();
        CharName = s.ReadString();
    }
}

/// <summary>C→S NCID 7 — annulation de création (vide).
/// CNE_CW_ServerCancelCreateCharacterReq → réponse NCID 41.</summary>
public sealed class WorldCancelCreateCharacter : NetMessage
{
    public override int Opcode => 7;
    public override string Name => "World.CancelCreateCharacter";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>C→S NCID 30 — « le perso a-t-il un mot de passe ? »
/// [i32 charId] → réponse NCID 75 [i32 flag].
/// CNE_CW_ServerCheckHasCharPassword (Eden.World MiscHandlers).</summary>
public sealed class WorldCheckCharPassword : NetMessage
{
    public override int Opcode => 30;
    public override string Name => "World.CheckCharPassword";
    public int CharId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(CharId);
    public override void Deserialize(BinReader s) => CharId = s.ReadInt();
}

/// <summary>C→S NCID 31 — opération mot de passe personnage
/// [i32 charId][s16 op][str pw] → réponse NCID 76 [s16 code].
/// CNE_CW_ServerCharPassword (Eden.World MiscHandlers /
/// CPlayerEntity::DoCharPassword @0x8158c30) : op 0 = vérifie le
/// mot de passe courant (code 0 ok / -1 échec), op 3 = définit —
/// une chaîne vide retire le mot de passe (flag NCID 75 repasse à
/// 0 côté serveur).</summary>
public sealed class WorldCharPassword : NetMessage
{
    public override int Opcode => 31;
    public override string Name => "World.CharPassword";
    public int CharId { get; set; }
    public short Op { get; set; }
    public string Password { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId);
        s.WriteShort(Op);
        s.WriteString(Password);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt();
        Op = s.ReadShort();
        Password = s.ReadString();
    }
}

/// <summary>S→C NCID 75 — flag « a un mot de passe » [i32].
/// CNC_CW_ClientCheckHasCharPassword.</summary>
public sealed class WorldCheckCharPasswordResult : NetMessage
{
    public override int Opcode => 75;
    public override string Name => "World.CheckCharPasswordResult";
    public int Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteInt(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadInt();
}

/// <summary>S→C NCID 76 — code retour d'une op mot de passe [s16].
/// CNC_CW_ClientCharPassword.</summary>
public sealed class WorldCharPasswordResult : NetMessage
{
    public override int Opcode => 76;
    public override string Name => "World.CharPasswordResult";
    public short Value { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(Value);
    public override void Deserialize(BinReader s) => Value = s.ReadShort();
}

/// <summary>S→C NCID 41 — accusé d'annulation de création (vide).
/// CNC_CW_ClientCancelCreateCharacterResp.</summary>
public sealed class WorldCancelCreateCharacterResp : NetMessage
{
    public override int Opcode => 41;
    public override string Name => "World.CancelCreateCharacterResp";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>S→C NCID 40 — ResetTime [i32][i32]. mitm ligne 22.</summary>
public sealed class WorldResetTime : NetMessage
{
    public override int Opcode => 40;
    public override string Name => "World.ResetTime";
    public int Field1;
    public int Field2;
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
    }
}

/// <summary>S→C NCID 60 — UpdatePrestigeData [s16][s16][s16][i32]. mitm ligne 36.</summary>
public sealed class WorldUpdatePrestigeData : NetMessage
{
    public override int Opcode => 60;
    public override string Name => "World.UpdatePrestigeData";
    public short Field1;
    public short Field2;
    public short Field3;
    public int Field4;
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteShort(Field2);
        s.WriteShort(Field3);
        s.WriteInt(Field4);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Field2 = s.ReadShort();
        Field3 = s.ReadShort();
        Field4 = s.ReadInt();
    }
}

/// <summary>S→C NCID 64 — ItemMallPointInfo [i32][i32]. mitm ligne 38.</summary>
public sealed class WorldItemMallPointInfo : NetMessage
{
    public override int Opcode => 64;
    public override string Name => "World.ItemMallPointInfo";
    public int Field1;
    public int Field2;
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(Field2);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        Field2 = s.ReadInt();
    }
}

/// <summary>
/// S→C NCID 69 — données de personnage chunkées (blob).
/// Le .g.cs nomme la classe CNC_CW_ClientCaptcha mais le layout
/// [i32×5][str blob] correspond exactement à la capture (mitm lignes 16-19 :
/// 4 chunks ~2745B, field2 = index 1..4, field3 = total 4).
/// </summary>
public sealed class CharacterDataChunk : NetMessage
{
    public override int Opcode => 69;
    public override string Name => "World.CharacterDataChunk";
    public int Field1;
    public int ChunkIndex;   // observé : 1..N
    public int ChunkTotal;   // observé : N
    public int Field4;       // observé : 0xaa = 170
    public int Field5;       // observé : 0x40 = 64
    public string Blob { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Field1);
        s.WriteInt(ChunkIndex);
        s.WriteInt(ChunkTotal);
        s.WriteInt(Field4);
        s.WriteInt(Field5);
        s.WriteString(Blob);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadInt();
        ChunkIndex = s.ReadInt();
        ChunkTotal = s.ReadInt();
        Field4 = s.ReadInt();
        Field5 = s.ReadInt();
        Blob = s.ReadString();
    }
}

/// <summary>S→C NCID 72 — ApexBufferTransfer [u8 flag][u16 count][count bytes].
/// mitm ligne 24 : 4800 01 0000 (flag=1, vide).</summary>
public sealed class WorldApexBuffer : NetMessage
{
    public override int Opcode => 72;
    public override string Name => "World.ApexBuffer";
    public byte Flag { get; set; }
    public byte[] Buffer { get; set; } = [];
    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Flag);
        s.WriteShort((short)Buffer.Length);
        s.WriteBytes(Buffer);
    }
    public override void Deserialize(BinReader s)
    {
        Flag = s.ReadByte();
        var count = (ushort)s.ReadShort();
        Buffer = s.ReadBytes(count);
    }
}
