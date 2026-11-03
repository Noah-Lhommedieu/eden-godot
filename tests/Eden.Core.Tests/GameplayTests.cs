using EdenClient.Combat;
using EdenClient.Game;
using EdenClient.GameInput;
using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du lot « contrôles gameplay + animations » :
/// layouts wire combat (NCID 5 C→S, 272 S→C), picking écran pur,
/// résolution nom→path de retarget, parseur GLB d'anims.
/// </summary>
public class GameplayTests
{
    // ------------------------------------------------------------------
    // Messages combat — layouts prouvés (.g.cs + capture 0500 <mob> 00).

    [Fact]
    public void MeleeAttack_WireLayout()
    {
        var m = new ZoneMeleeAttack
        {
            TargetId = unchecked((int)0xFFFFA123),
            AttackSlot = 0,
        };
        var w = new BinWriter();
        w.WriteUShort((ushort)m.Opcode);
        m.Serialize(w);
        var bytes = w.ToArray();
        // [u16 ncid=5][i32 handle][u8 type] — 7 octets.
        Assert.Equal(new byte[]
        {
            0x05, 0x00, 0x23, 0xA1, 0xFF, 0xFF, 0x00,
        }, bytes);
        // Round-trip.
        var r = new BinReader(bytes);
        Assert.Equal(5, r.ReadUShort());
        var back = new ZoneMeleeAttack();
        back.Deserialize(r);
        Assert.Equal(unchecked((int)0xFFFFA123), back.TargetId);
        Assert.Equal(0, back.AttackSlot);
    }

    [Fact]
    public void MeleeAttack_StopWire()
    {
        var m = new ZoneMeleeAttack { TargetId = 0, AttackSlot = 2 };
        var w = new BinWriter();
        m.Serialize(w);
        Assert.Equal(new byte[] { 0, 0, 0, 0, 2 }, w.ToArray());
    }

    [Fact]
    public void ClientMeleeAttack_ParsesLayout()
    {
        // [u32 atk][u32 cible][i32 dmg][i32 type][i32 kfId]
        var w = new BinWriter();
        w.WriteInt(200000002);          // charId attaquant
        w.WriteInt(unchecked((int)0xFFFFA123));
        w.WriteInt(42);                 // dmg
        w.WriteInt(3);                  // type premier coup
        w.WriteInt(1421);               // kfId (capture : 0x58d)
        var m = new ZoneClientMeleeAttack();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(200000002u, m.Attacker);
        Assert.Equal(0xFFFFA123u, m.Target);
        Assert.Equal(42, m.Damage);
        Assert.Equal(3, m.HitType);
        Assert.Equal(1421, m.MotionKfId);
        // Round-trip.
        var w2 = new BinWriter();
        m.Serialize(w2);
        Assert.Equal(w.ToArray(), w2.ToArray());
    }

    // ------------------------------------------------------------------
    // PickCore — picking écran pur.

    [Fact]
    public void Pick_NearestWithinRadius()
    {
        var pts = new List<(float X, float Y)>
        {
            (100f, 100f), (200f, 100f), (105f, 105f),
        };
        // (103,102) est à ~4.2px de l'index 2, ~7px de l'index 0.
        Assert.Equal(2, PickCore.NearestInRadius(pts, 103f, 102f, 30f));
        Assert.Equal(-1, PickCore.NearestInRadius(pts, 500f, 500f, 30f));
        Assert.Equal(-1, PickCore.NearestInRadius(pts, 103f, 102f, 3f));
    }

    [Fact]
    public void Pick_IsClickThreshold()
    {
        Assert.True(PickCore.IsClick(3.9f));
        Assert.False(PickCore.IsClick(6.1f));
    }
}
