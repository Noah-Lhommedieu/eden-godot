using EdenClient.Net;
using EdenClient.Protocol;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>KB-16 : 471 type 1 (reset talents) = snapshot qui remplace les
/// points affichés (le serveur réémet la map restante).</summary>
public class TalentResetTests
{
    private static ZoneUpdateAdvancedAbility Pkt(byte type,
        params (short Id, short Pt)[] e)
    {
        var b = new List<byte> { type };
        b.AddRange(BitConverter.GetBytes((ushort)e.Length));
        foreach (var (id, pt) in e)
        {
            b.AddRange(BitConverter.GetBytes(id));
            b.AddRange(BitConverter.GetBytes(pt));
        }
        var m = new ZoneUpdateAdvancedAbility();
        m.Deserialize(new BinReader(b.ToArray()));
        return m;
    }

    [Fact]
    public void Type1_ReplacesTalents_Type0And2Merge()
    {
        PlayerProfileState.Apply(Pkt(1));   // départ propre
        PlayerProfileState.Apply(Pkt(0, (5001, 3), (5002, 1)));
        PlayerProfileState.Apply(Pkt(2, (6001, 2)));
        Assert.Equal(3, PlayerProfileState.Talents[5001]);
        Assert.Equal(2, PlayerProfileState.Talents[6001]);
        // reset : snapshot = seule la classe « autre » reste
        PlayerProfileState.Apply(Pkt(1, (6001, 2)));
        Assert.False(PlayerProfileState.Talents.ContainsKey(5001));
        Assert.False(PlayerProfileState.Talents.ContainsKey(5002));
        Assert.Equal(2, PlayerProfileState.Talents[6001]);
        PlayerProfileState.Apply(Pkt(1));
        Assert.Empty(PlayerProfileState.Talents);
    }
}
