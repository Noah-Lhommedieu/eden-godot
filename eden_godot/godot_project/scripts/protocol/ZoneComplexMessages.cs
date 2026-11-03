using System;
using System.Collections.Generic;
using EdenClient.Net;

namespace EdenClient.Protocol;

// Parseurs manuels des codecs S→C « complexes » (boucles/listes) que
// le générateur ne couvre pas — layouts traduits verbatim des codecs
// serveur `Messages/Generated/*.g.cs` (eux-mêmes prouvés natifs).

/// <summary>CNC_CZ_ClientSpellBlastToLocation — 297 : blast vers une
/// position, traînée de positions (deux vecteurs flottants).</summary>
public sealed class ZoneClientSpellBlastToLocation : NetMessage
{
    public override int Opcode => 297;
    public override string Name => "CNC_CZ_ClientSpellBlastToLocation";
    public uint Serial { get; set; }
    public short SpellId { get; set; }
    public int Power { get; set; }
    public float SrcX { get; set; }
    public float SrcY { get; set; }
    public float SrcZ { get; set; }
    public float DstX { get; set; }
    public float DstY { get; set; }
    public float DstZ { get; set; }
    public int BlastId { get; set; }
    public List<float> VecA { get; } = new();
    public List<float> VecB { get; } = new();
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Serial); s.WriteShort(SpellId);
        s.WriteInt(Power);
        s.WriteFloat(SrcX); s.WriteFloat(SrcY); s.WriteFloat(SrcZ);
        s.WriteFloat(DstX); s.WriteFloat(DstY); s.WriteFloat(DstZ);
        s.WriteInt(BlastId);
        s.WriteShort((short)VecA.Count);
        foreach (var v in VecA) s.WriteFloat(v);
        s.WriteShort((short)VecB.Count);
        foreach (var v in VecB) s.WriteFloat(v);
    }
    public override void Deserialize(BinReader s)
    {
        Serial = s.ReadUInt(); SpellId = s.ReadShort();
        Power = s.ReadInt();
        SrcX = s.ReadFloat(); SrcY = s.ReadFloat(); SrcZ = s.ReadFloat();
        DstX = s.ReadFloat(); DstY = s.ReadFloat(); DstZ = s.ReadFloat();
        BlastId = s.ReadInt();
        var n = s.ReadUShort();
        VecA.Clear();
        for (var i = 0; i < n; i++) VecA.Add(s.ReadFloat());
        var m = s.ReadUShort();
        VecB.Clear();
        for (var i = 0; i < m; i++) VecB.Add(s.ReadFloat());
    }
}

/// <summary>CNC_CZ_ClientReceiveShortcutOfPage — 352 : contenu d'une
/// page de la barre de raccourcis (bitmask + i32 par slot + extras).</summary>
public sealed class ZoneClientReceiveShortcutOfPage : NetMessage
{
    public override int Opcode => 352;
    public override string Name => "CNC_CZ_ClientReceiveShortcutOfPage";
    /// <summary>Page demandée (s16).</summary>
    public short Page { get; set; }
    /// <summary>Bit0 = sentinelle page vide, bit(i+1) = slot i rempli.</summary>
    public ushort Mask { get; set; }
    /// <summary>Valeurs i32 des slots marqués, dans l'ordre.</summary>
    public int[] Values { get; set; } = Array.Empty<int>();
    /// <summary>Extras i32 traînants (u16 count + valeurs).</summary>
    public int[] Extras { get; set; } = Array.Empty<int>();
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Page); s.WriteShort((short)Mask);
        foreach (var v in Values) s.WriteInt(v);
        s.WriteShort((short)Extras.Length);
        foreach (var v in Extras) s.WriteInt(v);
    }
    public override void Deserialize(BinReader s)
    {
        Page = s.ReadShort();
        Mask = s.ReadUShort();
        var bits = new List<int>();
        for (var i = 1; i < 16; i++)
            if ((Mask & (1 << i)) != 0) bits.Add(i);
        Values = new int[bits.Count];
        for (var i = 0; i < bits.Count; i++) Values[i] = s.ReadInt();
        var n = s.ReadUShort();
        Extras = new int[n];
        for (var i = 0; i < n; i++) Extras[i] = s.ReadInt();
    }
}

/// <summary>CNC_CZ_ClientLookEquip — 460 : équipement inspecté d'un
/// joueur (charId + u16 count + u16 itemUid par entrée).</summary>
public sealed class ZoneClientLookEquip : NetMessage
{
    public override int Opcode => 460;
    public override string Name => "CNC_CZ_ClientLookEquip";
    public int CharId { get; set; }
    public int U1 { get; set; }
    public int U2 { get; set; }
    public List<ushort> ItemUids { get; } = new();
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId); s.WriteInt(U1); s.WriteInt(U2);
        s.WriteShort((short)ItemUids.Count);
        foreach (var v in ItemUids) s.WriteShort((short)v);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt(); U1 = s.ReadInt(); U2 = s.ReadInt();
        var count = s.ReadUShort();
        ItemUids.Clear();
        for (var i = 0; i < count; i++) ItemUids.Add(s.ReadUShort());
    }
}

/// <summary>CNC_CZ_ClientQueryPlayerCard — 491 : fiche d'un joueur
/// (recherche/panneau carte). Les deux bitvecs finaux sont des masques
/// LSB (u16 n puis ceil(n/8) octets de drapeaux par bloc de 32).</summary>
public sealed class ZoneClientQueryPlayerCard : NetMessage
{
    public override int Opcode => 491;
    public override string Name => "CNC_CZ_ClientQueryPlayerCard";
    public int CharId { get; set; }
    public string PlayerName { get; set; } = "";
    public short Level { get; set; }
    public short ClassId { get; set; }
    public short ClassLv { get; set; }
    public string FamilyName { get; set; } = "";
    public short[] Stats { get; set; } = new short[12];
    public string Intro { get; set; } = "";
    public int[] CollectionFlags { get; set; } = Array.Empty<int>();
    public short[] CollectionShorts { get; set; } = Array.Empty<short>();
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId); s.WriteString(PlayerName);
        s.WriteShort(Level); s.WriteShort(ClassId); s.WriteShort(ClassLv);
        s.WriteString(FamilyName);
        foreach (var v in Stats) s.WriteShort(v);
        s.WriteString(Intro);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt(); PlayerName = s.ReadString();
        Level = s.ReadShort(); ClassId = s.ReadShort();
        ClassLv = s.ReadShort(); FamilyName = s.ReadString();
        for (var i = 0; i < 12; i++) Stats[i] = s.ReadShort();
        Intro = s.ReadString();
        CollectionFlags = ReadBitVecI32(s);
        CollectionShorts = ReadBitVecS16(s);
    }
    private static int[] ReadBitVecI32(BinReader s)
    {
        var n = s.ReadUShort();
        var v = new int[n];
        for (var off = 0; off < n; off += 32)
        {
            var k = Math.Min(32, n - off);
            var mask = s.ReadBytes((k + 7) >> 3);
            for (var i = 0; i < k; i++)
                v[off + i] = (mask[i >> 3] & (1 << (i & 7))) != 0 ? 1 : 0;
        }
        return v;
    }
    private static short[] ReadBitVecS16(BinReader s)
    {
        var n = s.ReadUShort();
        var v = new short[n];
        for (var off = 0; off < n; off += 32)
        {
            var k = Math.Min(32, n - off);
            var mask = s.ReadBytes((k + 7) >> 3);
            for (var i = 0; i < k; i++)
                v[off + i] = (short)((mask[i >> 3] & (1 << (i & 7))) != 0 ? 1 : 0);
        }
        return v;
    }
}

/// <summary>CNC_CZ_ClientSearchTeamInfo — 518 : résultat de recherche
/// d'équipe (membres par équipe + commentaires + flags).</summary>
public sealed class ZoneClientSearchTeamInfo : NetMessage
{
    public override int Opcode => 518;
    public override string Name => "CNC_CZ_ClientSearchTeamInfo";
    public sealed class MemberEntry
    {
        public int CharId; public string CharName = "";
        public short Level, ClassId, U1, U2, U3;
    }
    public sealed class TeamEntry
    {
        public List<MemberEntry> Members { get; } = new();
        public string Comment1 = "", Comment2 = "";
        public short Flags;
    }
    public List<TeamEntry> Teams { get; } = new();
    public short Value { get; set; }
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        var teamCount = s.ReadShort();
        Teams.Clear();
        for (var i = 0; i < teamCount; i++)
        {
            var t = new TeamEntry();
            var memberCount = s.ReadShort();
            for (var j = 0; j < memberCount; j++)
                t.Members.Add(new MemberEntry
                {
                    CharId = s.ReadInt(), CharName = s.ReadString(),
                    Level = s.ReadShort(), ClassId = s.ReadShort(),
                    U1 = s.ReadShort(), U2 = s.ReadShort(),
                    U3 = s.ReadShort(),
                });
            t.Comment1 = s.ReadString();
            t.Comment2 = s.ReadString();
            t.Flags = s.ReadShort();
            Teams.Add(t);
        }
        Value = s.ReadShort();
    }
}

/// <summary>CNC_CZ_ClientSearchRoomResult — 537 : liste de salles
/// (chambres hôtel/instance).</summary>
public sealed class ZoneClientSearchRoomResult : NetMessage
{
    public override int Opcode => 537;
    public override string Name => "CNC_CZ_ClientSearchRoomResult";
    public sealed class Entry
    {
        public int RoomId; public string RoomName = "";
        public short Type, Mode;
        public int OwnerId, MemberCount, MaxMembers;
    }
    public List<Entry> Entries { get; } = new();
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        var count = s.ReadShort();
        Entries.Clear();
        for (var i = 0; i < count; i++)
            Entries.Add(new Entry
            {
                RoomId = s.ReadInt(), RoomName = s.ReadString(),
                Type = s.ReadShort(), Mode = s.ReadShort(),
                OwnerId = s.ReadInt(), MemberCount = s.ReadInt(),
                MaxMembers = s.ReadInt(),
            });
    }
}

/// <summary>CNC_CZ_ClientFamilyBattleKilledRank — 562 : classement
/// des frags de bataille de familles.</summary>
public sealed class ZoneClientFamilyBattleKilledRank : NetMessage
{
    public override int Opcode => 562;
    public override string Name => "CNC_CZ_ClientFamilyBattleKilledRank";
    public sealed class Entry
    {
        public string PlayerName = "", FamilyName = "";
        public short Kills, Rank;
    }
    public List<Entry> Entries { get; } = new();
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        var count = s.ReadShort();
        Entries.Clear();
        for (var i = 0; i < count; i++)
            Entries.Add(new Entry
            {
                PlayerName = s.ReadString(), FamilyName = s.ReadString(),
                Kills = s.ReadShort(), Rank = s.ReadShort(),
            });
    }
}

/// <summary>CNC_CZ_ClientPVPRankLookEquip — 571 : équipement visible
/// d'un joueur du classement PvP.</summary>
public sealed class ZoneClientPVPRankLookEquip : NetMessage
{
    public override int Opcode => 571;
    public override string Name => "CNC_CZ_ClientPVPRankLookEquip";
    public int CharId { get; set; }
    public string PlayerName { get; set; } = "";
    public short U1 { get; set; }
    public List<ushort> ItemUids { get; } = new();
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt(); PlayerName = s.ReadString();
        U1 = s.ReadShort();
        var count = s.ReadUShort();
        ItemUids.Clear();
        for (var i = 0; i < count; i++) ItemUids.Add(s.ReadUShort());
    }
}

/// <summary>CNC_CZ_ClientLoadCubeItems — 572 : items du cube
/// personnel (3 vecteurs s16 : sacs, slots, quantités).</summary>
public sealed class ZoneClientLoadCubeItems : NetMessage
{
    public override int Opcode => 572;
    public override string Name => "CNC_CZ_ClientLoadCubeItems";
    public short[] ProdBags { get; set; } = Array.Empty<short>();
    public short[] ProdSlots { get; set; } = Array.Empty<short>();
    public short[] ProdQtys { get; set; } = Array.Empty<short>();
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        short[] Vec(BinReader r)
        {
            var n = r.ReadUShort();
            var v = new short[n];
            for (var i = 0; i < n; i++) v[i] = r.ReadShort();
            return v;
        }
        ProdBags = Vec(s); ProdSlots = Vec(s); ProdQtys = Vec(s);
    }
}

/// <summary>CNC_CZ_ClientSearchTeamInfoV2 — 582 : recherche d'équipe
/// étendue (8 champs par membre + flags/commentaire par équipe).</summary>
public sealed class ZoneClientSearchTeamInfoV2 : NetMessage
{
    public override int Opcode => 582;
    public override string Name => "CNC_CZ_ClientSearchTeamInfoV2";
    public sealed class Member
    {
        public int CharId; public string Name = "";
        public short Level, ClassId, M3, M4, M5, M6;
    }
    public sealed class Entry
    {
        public List<Member> Members { get; } = new();
        public short Flag, Type;
        public string Comment = "";
        public byte B1, B2, B3;
    }
    public short Field1 { get; set; }
    public List<Entry> Entries { get; } = new();
    public short Field3 { get; set; }
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        var n = s.ReadUShort();
        Entries.Clear();
        for (var i = 0; i < n; i++)
        {
            var e = new Entry();
            var m = s.ReadUShort();
            for (var j = 0; j < m; j++)
                e.Members.Add(new Member
                {
                    CharId = s.ReadInt(), Name = s.ReadString(),
                    Level = s.ReadShort(), ClassId = s.ReadShort(),
                    M3 = s.ReadShort(), M4 = s.ReadShort(),
                    M5 = s.ReadShort(), M6 = s.ReadShort(),
                });
            e.Flag = s.ReadShort();
            e.Comment = s.ReadString();
            e.Type = s.ReadShort();
            e.B1 = s.ReadByte(); e.B2 = s.ReadByte(); e.B3 = s.ReadByte();
            Entries.Add(e);
        }
        Field3 = s.ReadShort();
    }
}
