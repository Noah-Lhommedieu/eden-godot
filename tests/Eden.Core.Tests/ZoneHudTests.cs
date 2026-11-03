using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests des messages HUD — chat (268/327/266/316/474/600, émission
/// 34) et parseur d'inventaire natif (<see cref="NativeInventoryParser"/>).
/// Formats d'après ZoneEntryFlow.Chat.cs / NativeItemCodec.cs serveur
/// (désasm prouvé) — le corps 312 du test est construit selon le codec
/// WriteItem natif.
/// </summary>
public class ZoneHudTests
{
    private static T RoundTrip<T>(T msg) where T : NetMessage, new()
    {
        var w = new BinWriter();
        msg.Serialize(w);
        var back = new T();
        back.Deserialize(new BinReader(w.ToArray()));
        return back;
    }

    // ---------- Chat ----------

    [Fact]
    public void ChannelMsg_C2S_WireNatif()
    {
        // Wire réel (désasm) : [u8][str][str][s16][s16][u32 len][blob].
        var w = new BinWriter();
        new ZoneChannelMsg
        {
            Channel = 2, Target = "", Message = "hello",
        }.Serialize(w);
        var bytes = w.ToArray();
        // u8 canal + str vide (2B) + u16 len + 5B + 4B zéros + u32 0
        Assert.Equal(1 + 2 + 2 + 5 + 4 + 4, bytes.Length);
        Assert.Equal(2, bytes[0]);
        var back = new ZoneChannelMsg();
        back.Deserialize(new BinReader(bytes));
        Assert.Equal(2, back.Channel);
        Assert.Equal("hello", back.Message);
        Assert.Empty(back.Blob);
    }

    [Fact]
    public void ClientSay_RoundTrip()
    {
        var m = RoundTrip(new ZoneClientSay
        {
            CharId = 0x0BEBC204, Message = "hi there",
        });
        Assert.Equal(0x0BEBC204u, m.CharId);
        Assert.Equal("hi there", m.Message);
    }

    [Fact]
    public void ClientChannelMsg_RoundTrip()
    {
        var m = RoundTrip(new ZoneClientChannelMsg
        {
            Channel = 4, Sender = "GM", Message = "shout!",
        });
        Assert.Equal((byte)4, m.Channel);
        Assert.Equal("GM", m.Sender);
        Assert.Equal("shout!", m.Message);
    }

    [Fact]
    public void TextOutputs_RoundTrip()
    {
        var a = RoundTrip(new ZoneClientTextOutput { Kind = 3, Text = "sys" });
        Assert.Equal((byte)3, a.Kind);
        Assert.Equal("sys", a.Text);
        var b = RoundTrip(new ZoneClientTextOutputIndex { Index = 42, Text = "p" });
        Assert.Equal(42, b.Index);
        var c = RoundTrip(new ZoneClientScreenMessage { Kind = 1, Text = "acq" });
        Assert.Equal("acq", c.Text);
        var d = RoundTrip(new ZoneUpdateAttrNewExp { Handle = 7, Exp = 123456 });
        Assert.Equal(123456, d.Exp);
    }

    // ---------- Inventaire (corps 312, format NativeItemCodec) ----------

    /// <summary>Construit un corps 312 minimal : 1 sac (capacité 32,
    /// bitmap 4B, 1 item en slot 2) + 2 porte-sacs vides (5 slots).</summary>
    private static byte[] MakeInventoryBody(int itemId)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)1);                 // u8 nBags
        w.Write((ushort)32);              // u16 capacity
        w.Write((byte)0);                 // u8 containerIdx
        var bitmap = new byte[4];
        bitmap[0] = 0x20;                 // slot 2 → bit 0x80>>2
        w.Write((short)4);                // s16 bmpLen
        w.Write(bitmap);
        // Record item (WriteItem) : id, dur×2, str vide, flags, mask 0,
        // queue 34B.
        w.Write((ushort)itemId);
        w.Write((short)50);               // dur cur
        w.Write((short)75);               // dur max
        w.Write((ushort)0);               // str maker vide
        w.Write((byte)0x08);              // flags
        w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); // mask u24
        w.Write(new byte[34]);            // queue fixe
        // Holders : nHolders=2, chacun 5 slots vides.
        w.Write((byte)2);
        foreach (var h in new byte[] { 0, 4 })
        {
            w.Write(h);                   // u8 holderIdx
            w.Write((ushort)5);           // u16 nSlots
            for (var s = 0; s < 5; s++)
                w.Write((ushort)0);       // slot vide
        }
        return ms.ToArray();
    }

    [Fact]
    public void Inventory312_Parse_OneItem()
    {
        var items = NativeInventoryParser.Parse(MakeInventoryBody(31939));
        Assert.Single(items);
        Assert.Equal(31939, items[0].ItemId);
        Assert.Equal(0, items[0].Bag);
        Assert.Equal(2, items[0].Slot);
        Assert.Equal(50, items[0].Durability);
        Assert.Equal(75, items[0].MaxDurability);
    }

    [Fact]
    public void Inventory312_Parse_MaskConditionnels()
    {
        // Item avec mask bit8 (create_time u32) + bit19 (strength s16)
        // → 6 octets conditionnels avant la queue 34B.
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)1);
        w.Write((ushort)32);
        w.Write((byte)1);                 // sac 1
        w.Write((short)4);
        w.Write(new byte[] { 0x40, 0, 0, 0 }); // slot 1
        w.Write((ushort)100);
        w.Write((short)1); w.Write((short)1);
        w.Write((ushort)0);
        w.Write((byte)0x18);              // bound
        // mask = bit8 (0x100) | bit19 (0x80000) → 3 octets LE.
        w.Write((byte)0x00); w.Write((byte)0x01); w.Write((byte)0x08);
        w.Write((uint)0xDEADBEEF);        // bit8 — create_time
        w.Write((short)12);               // bit19 — strengthen
        w.Write(new byte[34]);
        w.Write((byte)0);                 // 0 holder
        var items = NativeInventoryParser.Parse(ms.ToArray());
        Assert.Single(items);
        Assert.Equal(1, items[0].Bag);
        Assert.Equal(1, items[0].Slot);
        Assert.Equal(100, items[0].ItemId);
        Assert.Equal(0x18, items[0].Flags);
    }

    [Fact]
    public void Inventory312_Parse_Vide_EtTronque()
    {
        Assert.Empty(NativeInventoryParser.Parse([]));
        Assert.Empty(NativeInventoryParser.Parse(new byte[] { 0 }));
        // Corps tronqué au milieu d'un item : ne doit pas lever.
        var body = MakeInventoryBody(5);
        var short_ = body[..15];
        Assert.Empty(NativeInventoryParser.Parse(short_));
    }

    [Fact]
    public void Inventory312_Parse_QuantiteEtRenforcement()
    {
        // Item empilé : mask bit6 (amount s16) + bit19 (strength s16).
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)1);
        w.Write((ushort)32);
        w.Write((byte)0);
        w.Write((short)4);
        w.Write(new byte[] { 0x20, 0, 0, 0 });   // slot 2
        w.Write((ushort)42);
        w.Write((short)0); w.Write((short)0);
        w.Write((ushort)0);                       // maker vide
        w.Write((byte)0);
        // mask = 0x40 | 0x80000 → octets LE {0x40,0x00,0x08}.
        w.Write((byte)0x40); w.Write((byte)0x00); w.Write((byte)0x08);
        w.Write((short)17);                       // bit6 — amount 17
        w.Write((short)9);                        // bit19 — +9
        w.Write(new byte[34]);
        w.Write((byte)0);
        var items = NativeInventoryParser.Parse(ms.ToArray());
        Assert.Single(items);
        Assert.Equal(17, items[0].Amount);
        Assert.Equal(9, items[0].Strengthen);
    }

    // ---------- Missions (348/391/388) + données perso (310) ----------

    [Fact]
    public void MissionStates348_RoundTrip()
    {
        var m = new ZoneMissionStates();
        m.Fms.Add((1012, 3)); m.Fms.Add((1537, 0));
        m.InProgress.Add((1012, 0));
        m.Pms16.Add((3749, 0));
        m.Pms32.Add((0x3c52a854u, 1));
        m.FmsBitVec.AddRange(new byte[] { 0x11, 0x22 });
        m.CheckedIds.Add(1012);
        m.Trace.AddRange(new[] { 1012, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        var back = RoundTrip(m);
        Assert.Equal(2, back.Fms.Count);
        Assert.Equal((ushort)1012, back.Fms[0].Mid);
        Assert.Equal((ushort)3, back.Fms[0].State);
        Assert.Single(back.InProgress);
        Assert.Equal(0x3c52a854u, back.Pms32[0].Key);
        Assert.Equal(10, back.Trace.Count);
        Assert.Equal(1012, back.Trace[0]);
    }

    [Fact]
    public void UpdateMissionState391_And_List388_RoundTrip()
    {
        var u = RoundTrip(new ZoneUpdateMissionState
        {
            Op = 2, Key = 0x3c52a854, MissionId = 1012, State = 4,
        });
        Assert.Equal((byte)2, u.Op);
        Assert.Equal(0x3c52a854u, u.Key);
        Assert.Equal((ushort)1012, u.MissionId);
        var l = RoundTrip(new ZoneEnterMissionList
        {
            Op = 2, Arg = 1009, NpcUid = 50003, Arg3 = -1,
        });
        Assert.Equal((short)2, l.Op);
        Assert.Equal(1009, l.Arg);
        Assert.Equal(50003, l.NpcUid);
    }

    /// <summary>Construit un reste 310 (107B+) avec les champs prouvés
    /// aux offsets relatifs du ctor natif.</summary>
    private static byte[] MakePlayerDataRest()
    {
        var b = new byte[110];
        void W16(int off, short v) =>
            BitConverter.GetBytes(v).CopyTo(b, off);
        void W32(int off, int v) =>
            BitConverter.GetBytes(v).CopyTo(b, off);
        void W64(int off, long v) =>
            BitConverter.GetBytes(v).CopyTo(b, off);
        BitConverter.GetBytes(1.0f).CopyTo(b, 2);      // +2 f32 échelle
        b[6] = 1;                                       // +6 u8 race
        b[7] = 1;                                       // +7 u8 genre
        W16(12, 42);                                    // +12 u16 level
        W64(14, 1234);                                  // +14 i64 hp
        W64(22, 321);                                   // +22 i64 mp
        W32(30, 1500);                                  // +30 i32 maxhp
        W32(34, 400);                                   // +34 i32 maxmp
        b[38] = 7;                                      // +38 u8 classe
        for (var i = 0; i < 32; i++) b[40 + i] = 0xAB;  // +40 apparence
        W16(72, 15);                                    // +72 u16 classLv
        W32(74, 98765);                                 // +74 i32 classExp
        W32(94, 33);                                    // +94 i32 CP
        return b;
    }

    [Fact]
    public void PlayerData310_Parse_ChampsProuves()
    {
        var d = NativePlayerDataParser.Parse(MakePlayerDataRest());
        Assert.NotNull(d);
        Assert.Equal(1.0f, d!.Scale);
        Assert.Equal(1, d.RaceId);
        Assert.Equal(42, d.Level);
        Assert.Equal(1234, d.Hp);
        Assert.Equal(321, d.Mp);
        Assert.Equal(1500, d.MaxHp);
        Assert.Equal(400, d.MaxMp);
        Assert.Equal(7, d.ClassId);
        Assert.Equal(0xAB, d.Appearance[0]);
        Assert.Equal(15, d.ClassLevel);
        Assert.Equal(98765, d.ClassExp);
        Assert.Equal(33, d.ClassPoints);
    }

    [Fact]
    public void PlayerData310_Parse_TropCourt()
    {
        Assert.Null(NativePlayerDataParser.Parse([]));
        Assert.Null(NativePlayerDataParser.Parse(new byte[50]));
    }
}
