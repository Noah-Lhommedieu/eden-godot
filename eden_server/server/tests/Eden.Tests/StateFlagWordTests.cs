using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Zone.Codecs;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Mot d'état CLifeEntityFlags porté par le 302
/// <c>CNC_CZ_ClientFlagModify</c> — preuve :
/// <c>CStateController::NodifyFlagChange</c> zone @0x084e3c00,
/// Serialize = <c>[u32 handle(char+0x34)][u32 mot(char+0x3a)]
/// [u8 seq(char+0x280)]</c> ; index de flag = id d'état
/// (<c>S_StateDependence.ini</c> ligne -1, state 9 « mounted » →
/// bit9 = 0x0200). C'est le bit que le client teste
/// (player+0x3c bit9|bit17) pour choisir les records de strafe
/// monture — cf. PATCH-CLIENT-BINAIRE.md §B4.
/// </summary>
public class StateFlagWordTests
{
    /// <summary>Sérialise le corps du 302 (sans le header u16 len +
    /// NCID) pour assertion octet par octet.</summary>
    private static byte[] Body(CNC_CZ_ClientFlagModify cmd)
    {
        var w = new OutBinStream();
        cmd.Serialize(w);
        return w.ToArray();
    }

    [Fact]
    public void Mount_Word_CarriesBit9()
    {
        // Montée : mot de base joueur 0x01000000 | ride 0x0200 =
        // 0x01000200 — attendu §B4 (« mot | 0x0200 »).
        Assert.Equal(0x01000200u, StateFlagWord.Compose(
            dead: false, mounted: true,
            inBattle: false, actionChain: false));
    }

    [Fact]
    public void Dismount_Word_ClearsBit9()
    {
        // Descente : retour au mot de base 0x01000000 — le client
        // efface actor+0x3c bit9 → strafes normaux à pied.
        Assert.Equal(0x01000000u, StateFlagWord.Compose(
            dead: false, mounted: false,
            inBattle: false, actionChain: false));
    }

    [Fact]
    public void Wire_Body_Is9Bytes_NativeLayout()
    {
        // Corps exact natif : [u32 handle][u32 mot][u8 seq] — mot
        // little-endian éclaté [u16 flags][u8 sel][u8 op] par le
        // codec généré.
        var b = Body(StateFlagWord.BuildCommand(
            0x11223344u, 0x01000200u, 0x2a));
        Assert.Equal(9, b.Length);
        Assert.Equal(0x11223344u, BitConverter.ToUInt32(b, 0)); // handle
        Assert.Equal(0x01000200u, BitConverter.ToUInt32(b, 4)); // mot
        Assert.Equal(0x2a, b[8]);                              // seq
    }

    [Fact]
    public void Wire_Mounted_Bit9InLow16()
    {
        var cmd = StateFlagWord.BuildCommand(1u, 0x01000200u, 7);
        Assert.Equal(0x0200, cmd.Flags);     // bit9 dans l'u16 bas
        Assert.Equal(0x00, cmd.Selector);    // bits16-23
        Assert.Equal(0x01, cmd.Operation);   // bit24 « vrai perso »
        Assert.Equal(7, cmd.ActionId);
    }

    [Fact]
    public void Compose_CombatMounted_KeepsAllBits()
    {
        // Monture de combat : le mot cumule ride (0x0200) + chaîne
        // d'action (0x2000) + bataille (0x40000) sur la base perso —
        // les 302 combat émis ensuite doivent garder le bit9.
        var w = StateFlagWord.Compose(dead: false, mounted: true,
            inBattle: true, actionChain: true);
        Assert.Equal(0x01042200u, w);
        var cmd = StateFlagWord.BuildCommand(9u, w, 1);
        Assert.Equal(0x2200, cmd.Flags);  // 0x0200 ride | 0x2000 chain
        Assert.Equal(0x04, cmd.Selector); // bit18 « in battle » = sel 4
        Assert.Equal(0x01, cmd.Operation);
    }

    [Fact]
    public void Compose_Dead_SetsBit0()
    {
        // state 0 « Dead » = flag index 0 = bit0 du mot.
        Assert.Equal(0x01000001u, StateFlagWord.Compose(
            dead: true, mounted: false,
            inBattle: false, actionChain: false));
    }
}
