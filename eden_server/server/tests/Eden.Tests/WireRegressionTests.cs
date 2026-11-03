using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Régressions wire issues de bugs live : chaque test épingle le corps
/// EXACT attendu d'après la preuve native (désasm/capture).
/// </summary>
public class WireRegressionTests
{
    /// <summary>
    /// NCID 271 <c>ClientDisplayMonster</c> — corps natif
    /// <c>[i32 mob][u8 nbEnchants][u8 ownerGen][i32 owner]</c> = 10B à
    /// liste vide (Serialize @0x81e96d0, émis par CAI::ChangeOwner
    /// @0x84d3930). Capture zsniff_native_entry.bin 17:22:58.827 :
    /// <c>0aabffff 00 01 02c2eb0b</c> (mob=0xFFFFAB0A, gen=1,
    /// owner=0x0BEBC202).
    /// Régression : la trame a été émise à 9B (u8 génération supprimé)
    /// — RC4 désynchronisé, freeze client. Testé via le codec généré
    /// utilisé en production.
    /// </summary>
    [Fact]
    public void DisplayMonster_IsNativeTenBytes()
    {
        var msg = new CNC_CZ_ClientDisplayMonster
        {
            MobHandle = 0xFFFFAB0Au, EnchantIds = [],
            OwnerGeneration = 1, OwnerHandle = 0x0BEBC202,
        };
        var outp = new OutBinStream();
        msg.Serialize(outp);
        var body = outp.ToArray();
        Assert.Equal(10, body.Length);
        Assert.Equal(
            Convert.FromHexString("0aabffff000102c2eb0b"), body);
    }

    /// <summary>Le NCID 444 <c>ClientGetSelectLifeTarget</c> a bien un
    /// corps 9B dans la capture (len=11 câble = 2 NCID + 9) — distinct
    /// du 271 : ne pas réutiliser son layout pour le DisplayMonster.
    /// Testé via le codec généré utilisé en production.</summary>
    [Fact]
    public void SelectLifeTarget_NineBytes_FromCapture()
    {
        // Capture 17:22:58.528 : NCID=444 len=11 → body 9B
        // [i32 mob][u8][i32 char].
        var msg = new CNC_CZ_ClientGetSelectLifeTarget
        {
            Field1 = unchecked((int)0xFFFFAB0A), Field2 = 0, Field3 = 0x0BEBC202,
        };
        var outp = new OutBinStream();
        msg.Serialize(outp);
        Assert.Equal("0aabffff0002c2eb0b",
            Convert.ToHexString(outp.ToArray()).ToLowerInvariant());
    }
}
