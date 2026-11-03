using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Windows;

/// <summary>Logique pure de l'étal joueur (port VendingWindow +
/// MinimapPanel.StallRegionAtSelf) : région d'étal, textes d'erreur
/// des statuts 377, saisie du prix et du titre.</summary>
public static class VendingView
{
    /// <summary>Titre max en octets Big5 (serveur : MaxTitleBytes).</summary>
    public const int MaxTitle = 90;
    public const int MaxPrice = 999_999_999;

    /// <summary><c>CheckStallRegion</c> natif : première région de
    /// <paramref name="scene"/> dont le disque contient (x,y) ; null
    /// sinon (le 77 serait rejeté silencieusement).</summary>
    public static (int Type, float X, float Y, float Radius)? RegionAt(
        IReadOnlyDictionary<int, (float X, float Y, float Radius)>? regions,
        float x, float y)
    {
        if (regions == null) return null;
        foreach (var (type, r) in regions)
        {
            var dx = x - r.X;
            var dy = y - r.Y;
            if (dx * dx + dy * dy <= r.Radius * r.Radius)
                return (type, r.X, r.Y, r.Radius);
        }
        return null;
    }

    /// <summary>Clé EN du message d'erreur d'un statut 377 ; null si
    /// succès ([1][..]) — codes natifs VendorPackets / VendorSellItem.</summary>
    public static string? StatusKey(byte action, byte detail)
    {
        if (action == 1) return null;
        return detail switch
        {
            0x11 => "Stall: invalid state (dead/busy/already vending).",
            0x12 => "Stall: too soon — wait ~1.5 s.",
            0x13 => "Stall: stalls are only allowed on map S001.",
            0x14 => "Stall: title must not be empty.",
            0x15 => "Stall: duplicate item in the list.",
            0x16 => "Stall: too many free (price 0) goods.",
            _ => $"Stall: refused (0x{detail:x2}).",
        };
    }

    /// <summary>Prix saisi : entier 0..<see cref="MaxPrice"/> ; -1 si
    /// invalide (négatif / hors borne), 0 si vide.</summary>
    public static int ParsePrice(string text)
    {
        var t = text.Trim().Replace(" ", "").Replace(",", "");
        if (t.Length == 0) return 0;
        return int.TryParse(t, out var v) && v >= 0 && v <= MaxPrice
            ? v : -1;
    }

    /// <summary>Titre nettoyé et tronqué (chars ≤ octets Big5).</summary>
    public static string ClampTitle(string title)
    {
        var t = title.Trim();
        return t.Length > MaxTitle ? t[..MaxTitle] : t;
    }

    /// <summary>Ligne de bien en vente : « Nom ×N — 1 234 ».</summary>
    public static string GoodLine(string name, int lot, int price) =>
        $"{name}{(lot > 1 ? " ×" + lot : "")} — {price:N0}";
}
