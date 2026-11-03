using System.Text;

namespace Eden.Core.Data;

/// <summary>
/// Encodages serveur Eden (fichiers .ini Big5, chaînes réseau).
/// Centralise l'enregistrement du provider CodePages : sous .NET Core,
/// Encoding.GetEncoding("Big5") lève sans RegisterProvider préalable.
/// </summary>
public static class EdenEncoding
{
    static EdenEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Big5 = Encoding.GetEncoding("Big5");
    }

    public static Encoding Big5 { get; }
}
