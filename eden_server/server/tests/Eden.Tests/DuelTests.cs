using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régression byte-level des paquets duel — formats natifs désasm :
/// CNC_CZ_ClientDuelRequest 451 [i32][str],
/// CNC_CZ_ClientDuelRequestResult 452 [str][str][s16] (codes 2/0 de
/// CPlayerFactory::DuelReject @0x080fe680),
/// CNC_CZ_ClientDuelBegin 453 [i32][i32][s16][s16] (notification
/// [0][0][5][0] puis activation [serial][handle][99][1] de
/// CEventDuelCountdown::Execute @0x08198340),
/// CNC_CZ_ClientDuelResult 454 [i32][str][str][s16] (codes 0/1/3/4/5
/// de CDuelFactory::DuelFinish @0x0805d670/0x0805df80).
/// </summary>
public class DuelTests
{
    /// <summary>Sérialise une commande générée en bytes bruts.</summary>
    private static byte[] Emit(NetCommand cmd)
    {
        var outp = new OutBinStream();
        cmd.Serialize(outp);
        return outp.ToArray();
    }

    [Fact]
    public void DuelRequest_Layout()
    {
        var body = Emit(new CNC_CZ_ClientDuelRequest
        {
            Field1 = 0x2a, Field2 = "Ab",
        });
        // [i32 42][u16 2]["Ab"]
        Assert.Equal(42, BitConverter.ToInt32(body, 0));
        Assert.Equal(2, BitConverter.ToInt16(body, 4));
        Assert.Equal((byte)'A', body[6]);
        Assert.Equal((byte)'b', body[7]);
        Assert.Equal(8, body.Length);
    }

    [Fact]
    public void DuelRequestResult_RejectCodes()
    {
        var body = Emit(new CNC_CZ_ClientDuelRequestResult
        {
            Field1 = "Req", Field2 = "Rej", Field3 = 2,
        });
        // [u16 3]["Req"][u16 3]["Rej"][s16 2]
        Assert.Equal(3, BitConverter.ToInt16(body, 0));
        Assert.Equal(3, BitConverter.ToInt16(body, 5));
        Assert.Equal((short)2, BitConverter.ToInt16(body, 10));
    }

    [Fact]
    public void DuelBegin_CountdownThenActive()
    {
        // Notification d'acceptation AddToDuel : [0][0][5][0].
        var accept = Emit(new CNC_CZ_ClientDuelBegin
        {
            Field1 = 0, Field2 = 0, Field3 = 5, Field4 = 0,
        });
        Assert.Equal(0, BitConverter.ToInt32(accept, 0));
        Assert.Equal(0, BitConverter.ToInt32(accept, 4));
        Assert.Equal((short)5, BitConverter.ToInt16(accept, 8));
        Assert.Equal((short)0, BitConverter.ToInt16(accept, 10));

        // Activation au countdown +5 s : [serial][handle adv][99][1].
        var begin = Emit(new CNC_CZ_ClientDuelBegin
        {
            Field1 = 7, Field2 = 0x1234, Field3 = 99, Field4 = 1,
        });
        Assert.Equal(7, BitConverter.ToInt32(begin, 0));
        Assert.Equal(0x1234, BitConverter.ToInt32(begin, 4));
        Assert.Equal((short)99, BitConverter.ToInt16(begin, 8));
        Assert.Equal((short)1, BitConverter.ToInt16(begin, 10));
    }

    [Fact]
    public void DuelResult_WinnerLoserWitness()
    {
        // Vainqueur : [perdantHandle][nomPerdant][""][0].
        var win = Emit(new CNC_CZ_ClientDuelResult
        {
            Field1 = 55, Field2 = "Loser", Field3 = "", Field4 = 0,
        });
        Assert.Equal(55, BitConverter.ToInt32(win, 0));
        Assert.Equal(5, BitConverter.ToInt16(win, 4));
        Assert.Equal(0, BitConverter.ToInt16(win, 11)); // str "" → u16 0
        Assert.Equal((short)0, BitConverter.ToInt16(win, 13));
        Assert.Equal(15, win.Length);

        // Broadcast témoins : [vainqueurHandle][nomPerdant]
        // [nomVainqueur][4].
        var bc = Emit(new CNC_CZ_ClientDuelResult
        {
            Field1 = 42, Field2 = "L", Field3 = "W", Field4 = 4,
        });
        Assert.Equal(42, BitConverter.ToInt32(bc, 0));
        Assert.Equal((short)4,
            BitConverter.ToInt16(bc, bc.Length - 2));
    }

    [Theory]
    [InlineData(80, 80, 0)]   // égalité → codes 3/3 + broadcast 5
    [InlineData(80, 20, 1)]   // A plus haut %HP gagne
    [InlineData(20, 80, 2)]   // B gagne
    [InlineData(0, 1, 2)]
    public void DuelOutcome_MatchesNativeHpPercentCompare(
        long pctA, long pctB, int expected)
    {
        Assert.Equal(expected, ZoneEntryFlow.DuelOutcome(pctA, pctB));
    }

    [Fact]
    public void DuelPackets_RoundTrip()
    {
        var orig = new CNC_CZ_ClientDuelResult
        {
            Field1 = -1, Field2 = "Perdant", Field3 = "Win", Field4 = 5,
        };
        var body = Emit(orig);
        var back = new CNC_CZ_ClientDuelResult();
        back.Deserialize(new InBinStream(body));
        Assert.Equal(orig.Field1, back.Field1);
        Assert.Equal(orig.Field2, back.Field2);
        Assert.Equal(orig.Field3, back.Field3);
        Assert.Equal(orig.Field4, back.Field4);
    }
}
