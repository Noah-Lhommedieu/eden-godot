using System.Text;

namespace EdenClient.Data;

/// <summary>
/// Encodages des données Eden — les tables .ini du client sont en
/// Big5 (code page 950, chinois traditionnel) : <c>data/db/*.ini</c>,
/// <c>data/scene/*.ini</c>, <c>map/model/Sxxx/Sxxx.ini</c>.
/// Centralise l'enregistrement du provider CodePages : sous .NET Core,
/// <c>Encoding.GetEncoding("Big5")</c> lève sans RegisterProvider
/// préalable (paquet <c>System.Text.Encoding.CodePages</c> déjà
/// référencé par le csproj).
/// Port direct de <c>$EDEN/server/src/Eden.Core/Data/EdenEncoding.cs</c>.
/// </summary>
public static class EdenEncoding
{
    static EdenEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Big5 = Encoding.GetEncoding("Big5");
    }

    /// <summary>Big5 / CP950 — encodage natif des fichiers de données
    /// du client (les tables T_* sont en ASCII/UTF-8, décodage Big5
    /// compatible car ASCII ⊆ Big5).</summary>
    public static Encoding Big5 { get; }

    /// <summary>Corpus EDENFR (decision « FR only ») : les tables de
    /// texte <c>t_*.ini</c> sont encodées <b>Latin-1</b> (« Champignoïde
    /// rouge » — Big5 donne du mojibake « Champigno骾e ») ; les tables
    /// structurelles <c>c_*</c>/<c>S_*</c>, les scènes et le reste
    /// restent Big5. Ce fallback n'est utilisé que quand le sniff UTF-8
    /// strict a échoué (fichier ni UTF-8 ni ASCII).
    /// Override global : <c>EDEN_TABLE_ENC=latin1|big5</c>.</summary>
    public static Encoding TableFallback(string path)
    {
        var forced = Environment.GetEnvironmentVariable("EDEN_TABLE_ENC");
        if (!string.IsNullOrEmpty(forced))
            return forced.Equals("latin1", StringComparison.OrdinalIgnoreCase)
                ? Encoding.Latin1 : Big5;
        var name = Path.GetFileName(path);
        return name.StartsWith("t_", StringComparison.OrdinalIgnoreCase)
            ? Encoding.Latin1 : Big5;
    }
}
