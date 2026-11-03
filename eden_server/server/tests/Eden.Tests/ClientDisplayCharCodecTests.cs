using Eden.Core.Game.Inventory;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>Tests wire de <see cref="ClientDisplayCharCodec"/> —
/// layout vérifié contre
/// <c>CNC_CZ_ClientDisplayChar::Serialize</c> zone @0x081e03f0.</summary>
public sealed class ClientDisplayCharCodecTests
{
    private static ClientDisplayCharCodec.DisplayCharSnapshot Base()
        => new(
            CharId: 0x1122, Name: "Test",
            X: 10f, Z: 20f, Facing: 1.5f, MoveSpeed: 5f,
            AttackSpeedLevel: 6, Flags: new byte[] { 1, 0, 0, 0 },
            Level: 12, ClassId: 4, ClassLevel: 9,
            Appearance32: Enumerable.Range(0, 32)
                .Select(i => (byte)i).ToArray(),
            Hp: 300, MaxHp: 400, MaxMp: 400,
            Scale290: 100, RideKey: 0, TransformTpl: 0,
            Equips0: new ItemEntity?[11],
            Equips1: new ItemEntity?[11],
            ModelRev: 7, EnchantRev: 0, FamilyRev: 2,
            AppellationRev: 0, DoRev: 0,
            FamilyId: 55, FamilyName: "Famille",
            CastingSpell: 0, AppellationId: 3,
            Passengers: Array.Empty<uint>(),
            StallTitle: "",
            TerritoryTeam: -1, MixElfId: 0,
            Versus: 0, VersusTeam: 0);

    [Fact]
    public void Build_HeaderOrdre()
    {
        var b = ClientDisplayCharCodec.Build(Base());
        using var r = new BinaryReader(new MemoryStream(b));
        Assert.Equal(0x1122u, r.ReadUInt32());            // charId
        var nameLen = r.ReadUInt16();
        Assert.Equal("Test", System.Text.Encoding.ASCII
            .GetString(r.ReadBytes(nameLen)));
        Assert.Equal(10f, r.ReadSingle());                // x
        Assert.Equal(20f, r.ReadSingle());                // z
        Assert.Equal(1.5f, r.ReadSingle());               // dir
        Assert.Equal(5f, r.ReadSingle());                 // moveSpeed
        Assert.Equal((byte)6, r.ReadByte());              // atkSpd R
        Assert.Equal((byte)6, r.ReadByte());              // atkSpd L
        Assert.Equal(4, r.ReadBytes(4).Length);           // flags
        Assert.Equal((ushort)12, r.ReadUInt16());         // level
        Assert.Equal((byte)4, r.ReadByte());              // classId
        Assert.Equal((ushort)9, r.ReadUInt16());          // classLevel
        var blob = r.ReadBytes(0x20);
        Assert.Equal(Enumerable.Range(0, 32)
            .Select(i => (byte)i).ToArray(), blob);
        // mask : hp≠0(1)+maxHp≠0(2)+maxHp32≠0(4)+maxMp==maxHp32(0)
        // → 0x07
        Assert.Equal((byte)0x07, r.ReadByte());
        Assert.Equal(300L, r.ReadInt64());                // hp
        Assert.Equal(400L, r.ReadInt64());                // maxHp i64
        Assert.Equal(400, r.ReadInt32());                 // maxHp i32
        // maxMp==maxHp32 → non émis (sentinelle chaînée)
        Assert.Equal(0u, r.ReadUInt32());                 // mask equips0
        Assert.Equal(0u, r.ReadUInt32());                 // mask equips1
    }

    [Fact]
    public void Build_MaskConditionnels()
    {
        var s = Base() with
        {
            MaxMp = 500,        // ≠ maxHp32 → bit3
            Scale290 = 150,     // ≠ 100 → bit5
            RideKey = 99,       // → bit6
            TransformTpl = 77,  // → bit7
        };
        var b = ClientDisplayCharCodec.Build(s);
        // Saute l'en-tête : 4+2+4+16+2+4+4+2+1+2+32
        // 4(charId)+6(str)+16(f32)+2(atk)+4(flags)+2(lv)+1(cls)+2(clv)+32(blob)
        var off = 4 + 6 + 16 + 2 + 4 + 2 + 1 + 2 + 32;
        Assert.Equal((byte)(1 | 2 | 4 | 8 | 0x20 | 0x40 | 0x80),
            b[off]);
        using var r = new BinaryReader(
            new MemoryStream(b, off + 1, b.Length - off - 1));
        Assert.Equal(300L, r.ReadInt64());
        Assert.Equal(400L, r.ReadInt64());
        Assert.Equal(400, r.ReadInt32());
        Assert.Equal(500, r.ReadInt32());                 // maxMp
        Assert.Equal((byte)150, r.ReadByte());            // scale
        Assert.Equal(99u, r.ReadUInt32());                // rideKey
        Assert.Equal(77u, r.ReadUInt32());                // transform
        Assert.Equal(0u, r.ReadUInt32());                 // mask eq0
        Assert.Equal(0u, r.ReadUInt32());                 // mask eq1
    }

    [Fact]
    public void Build_Equips_MaskEtOrdre()
    {
        var eq = new ItemEntity?[11];
        eq[2] = new ItemEntity { ItemId = 15001, Slot = 2,
            EmbeddedAmount = 0, Combo = 0, EnchantLevel = 0,
            DueDate = 0, Maker = "" };
        for (var i = 0; i < 6; i++) eq[2]!.SetEmbeddedId(i, -1);
        var eq1 = new ItemEntity?[11];
        eq1[0] = new ItemEntity { ItemId = 25001, Slot = 0 };
        var s = Base() with { Equips0 = eq, Equips1 = eq1 };
        var b = ClientDisplayCharCodec.Build(s);
        // 4(charId)+6(str)+16(f32)+2(atk)+4(flags)+2(lv)+1(cls)+2(clv)+32(blob)
        var off = 4 + 6 + 16 + 2 + 4 + 2 + 1 + 2 + 32;
        Assert.Equal((byte)7, b[off]);
        var pos = off + 1 + 8 + 8 + 4;                    // après cond.
        Assert.Equal(4u, BitConverter.ToUInt32(b, pos));  // bit2
        pos += 4;
        // Record item : [u16 tid][u8 flags&7][u16 len][u24 mask]
        Assert.Equal(15001, BitConverter.ToUInt16(b, pos));
        Assert.Equal(0, b[pos + 2]);
        Assert.Equal(0, BitConverter.ToUInt16(b, pos + 3));
        // mask u24 : 6 embedded==-1 → 0 ; amount/combo/enchant/due =0
        Assert.Equal(0L, BitConverter.ToUInt32(b, pos + 5) & 0xffffffL);
        // record = 2+1+2+3 + 34B tail = 42
        pos += 42;
        Assert.Equal(1u, BitConverter.ToUInt32(b, pos));  // mask set1
        pos += 4;
        Assert.Equal(25001, BitConverter.ToUInt16(b, pos));
        // record léger = 22B : [u16][i64][u16][3×s16][u16][u16]
        pos += 22;
        // révisions
        Assert.Equal(7, b[pos]);
        Assert.Equal(0, b[pos + 1]);
        Assert.Equal(2, b[pos + 2]);
        Assert.Equal(0, b[pos + 3]);
        Assert.Equal(0, b[pos + 4]);
        pos += 5;
        Assert.Equal(55u, BitConverter.ToUInt32(b, pos)); // familyId
        pos += 4;
        var flen = BitConverter.ToUInt16(b, pos);
        Assert.Equal("Famille", System.Text.Encoding.ASCII
            .GetString(b, pos + 2, flen));
    }
}
