using System.Text;
using Eden.Core.Data;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Encodages non-Unicode du protocole et des données Eden :
/// <list type="bullet">
/// <item><b>Big5</b> — chaînes wire (<see cref="EdenEncoding.Big5"/>,
/// InBinStream/OutBinStream) et .ini de <c>server_us/Data/db_decoded</c>
/// (client référence chinois traditionnel).</item>
/// <item><b>GBK</b> — textes quêtes/dialogues natifs
/// (QuestEngine, ZoneEntryFlow.Mission).</item>
/// </list>
/// Le provider CodePages est enregistré par le module initializer de
/// Eden.Core (DapperBootstrap) — ces tests prouvent l'absence de
/// corruption encode→decode dans un processus ayant chargé Eden.Core.
/// </summary>
public class EncodingTests
{
    /// <summary>Chaîne réseau Big5 : encode puis décode doit rendre la
    /// chaîne exacte (caractères chinois traditionnels présents dans les
    /// .ini db_decoded, ex. T_TextIndex).</summary>
    [Fact]
    public void Big5_RoundTrip_PreservesTraditionalChinese()
    {
        var s = "\u7e6a\u9f8d\u85e5\u6c34"; // "" potion / quête — réel contenu jeu
        var bytes = EdenEncoding.Big5.GetBytes(s);
        Assert.NotEqual(s.Length, bytes.Length); // multi-octet utilisé
        Assert.Equal(s, EdenEncoding.Big5.GetString(bytes));
    }

    /// <summary>GetEncoding("GBK") direct — utilisé tel quel par
    /// QuestEngine/ZoneEntryFlow.Mission ; lève sans provider.</summary>
    [Fact]
    public void Gbk_Registered_RoundTripPreservesSimplifiedChinese()
    {
        var enc = Encoding.GetEncoding("GBK");
        var s = "\u7b80\u4f53\u4e2d\u6587"; // ""
        Assert.Equal(s, enc.GetString(enc.GetBytes(s)));
    }

    /// <summary>GetEncoding("Big5") direct — utilisé par
    /// NativeItemCodec (champ maker) et IniTable.</summary>
    [Fact]
    public void Big5_GetEncodingName_Works()
    {
        var enc = Encoding.GetEncoding("Big5");
        var s = "\u827e\u723e\u7279\u65af"; // ""
        Assert.Equal(s, enc.GetString(enc.GetBytes(s)));
    }

    /// <summary>Le décodage de bytes Big5 typiques (noms de quêtes dans
    /// les .ini) ne produit pas de '?'. Les caractères choisis ont des
    /// séquences Big5 valides connues.</summary>
    [Fact]
    public void Big5_TypicalIniBytes_DecodeWithoutLoss()
    {
        var enc = EdenEncoding.Big5;
        var s = "\u9f8d\u4e4b\u8c37"; // ""
        var bytes = enc.GetBytes(s);
        var decoded = enc.GetString(bytes);
        Assert.DoesNotContain('?', decoded);
        Assert.Equal(s, decoded);
    }
}
