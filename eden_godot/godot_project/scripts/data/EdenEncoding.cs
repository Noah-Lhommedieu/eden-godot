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
        Cp1252 = Encoding.GetEncoding(1252);
    }

    /// <summary>Big5 / CP950 — encodage natif des fichiers de données
    /// du client (les tables T_* sont en ASCII/UTF-8, décodage Big5
    /// compatible car ASCII ⊆ Big5).</summary>
    public static Encoding Big5 { get; }

    /// <summary>Windows-1252 — encodage des tables de texte
    /// <c>t_*.ini</c> du corpus EDENFR (« Champignoïde rouge » ; Big5
    /// donne le mojibake « Champigno骾e »). CP1252 plutôt que Latin-1 :
    /// la plage 0x80-0x9F porte « œ », « ’ », « € » en français.</summary>
    public static Encoding Cp1252 { get; }

    /// <summary>Encodage d'une table non UTF-8 : <c>t_*.ini</c> →
    /// CP1252 (texte FR, quel que soit l'encodage demandé par
    /// l'appelant — hérité du corpus merge où ces tables étaient
    /// Big5/ASCII), sinon <paramref name="requested"/> ou Big5
    /// (tables structurelles <c>c_*</c>/<c>s_*</c>, scènes).
    /// Override global : <c>EDEN_TABLE_ENC=cp1252|big5</c>.</summary>
    public static Encoding TableFallback(string path, Encoding? requested)
    {
        var forced = Environment.GetEnvironmentVariable("EDEN_TABLE_ENC");
        if (!string.IsNullOrEmpty(forced))
            return forced.Equals("big5", StringComparison.OrdinalIgnoreCase)
                ? Big5 : Cp1252;
        return Path.GetFileName(path).StartsWith("t_",
                StringComparison.OrdinalIgnoreCase)
            ? Cp1252 : requested ?? Big5;
    }
}
