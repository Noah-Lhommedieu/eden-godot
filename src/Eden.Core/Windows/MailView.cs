using EdenClient.Protocol;

namespace EdenClient.Windows;

/// <summary>Logique pure du courrier (port <c>MailWindow</c>) :
/// formats de date, ligne de liste, réponse, validation d'envoi,
/// sélection des pièces jointes à prendre en lot.</summary>
public static class MailView
{
    /// <summary>« dd/MM/yyyy HH:mm » (UTC si <paramref name="utc"/>,
    /// sinon heure locale) ; vide si ≤ 0.</summary>
    public static string Date(long unix, bool utc = false,
        string fmt = "dd/MM/yyyy HH:mm")
    {
        if (unix <= 0) return "";
        var d = DateTimeOffset.FromUnixTimeSeconds(unix);
        return (utc ? d.UtcDateTime : d.LocalDateTime).ToString(fmt,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Ligne de la liste : « ◆ Expéditeur — Sujet [PJ] ».</summary>
    public static string ListLine(MailEntry m) =>
        $"{(m.IsUnread ? "◆" : "◇")} {m.SenderName} — {m.Subject}" +
        (m.HasAttachment ? " [PJ]" : "");

    /// <summary>Sujet d'une réponse (« Re: » une seule fois).</summary>
    public static string ReplySubject(string subject, string prefix = "Re:")
        => subject.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? subject : $"{prefix} {subject}";

    /// <summary>Ids des courriers ayant un item joint (prise en lot).</summary>
    public static List<int> TakeAllIds(IEnumerable<MailEntry> mails) =>
        mails.Where(m => m.AttachItemId > 0).Select(m => m.MailId).ToList();

    /// <summary>Entier ≥ 0 ; texte invalide/négatif → 0.</summary>
    public static int ParseAmount(string? t) =>
        int.TryParse((t ?? "").Trim(), out var v) ? Math.Max(v, 0) : 0;

    /// <summary>Message d'erreur de validation d'envoi (clé EN) ou null.</summary>
    public static string? ValidateSend(string to) =>
        to.Trim().Length == 0 ? "Recipient missing." : null;

    /// <summary>Texte de statut après un 403 (0 = envoyé).</summary>
    public static string SendResultKey(int result) =>
        result == 0 ? "Mail sent." : "Mail refused.";
}
