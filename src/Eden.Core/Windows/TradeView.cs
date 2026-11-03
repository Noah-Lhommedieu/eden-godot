namespace EdenClient.Windows;

/// <summary>Logique pure de la fenêtre d'échange (port
/// <c>TradeWindow.Refresh/ApplyGoldEdit</c>) : statut affiché et
/// saisie d'or bornée.</summary>
public static class TradeView
{
    /// <summary>Clé de statut (texte EN de <c>UiTextEn</c>) selon les
    /// verrous des deux côtés ; vide = rien à afficher.</summary>
    public static string StatusKey(bool completed, bool selfLocked,
        bool partnerLocked) =>
        completed ? "Trade complete!"
        : selfLocked && !partnerLocked ? "Waiting for partner…"
        : partnerLocked && !selfLocked
            ? "Partner locked — review then Lock/Ok"
        : "";

    /// <summary>Saisie d'or : chiffres seuls (espaces/virgules
    /// tolérés), borné à <paramref name="max"/> ; vide → 0.</summary>
    public static uint ParseGold(string text, long max)
    {
        long v = 0;
        foreach (var ch in text)
        {
            if (ch is >= '0' and <= '9')
            {
                v = v * 10 + (ch - '0');
                if (v > uint.MaxValue) { v = uint.MaxValue; break; }
            }
        }
        if (max < 0) max = 0;
        return (uint)Math.Min(v, Math.Min(max, uint.MaxValue));
    }

    /// <summary>Prochaine case d'offre libre, -1 si pleine.</summary>
    public static int NextSlot(int used, int slots) =>
        used < slots ? used : -1;
}
