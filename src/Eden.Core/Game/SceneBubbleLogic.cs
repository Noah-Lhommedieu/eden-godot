using System.Text;

namespace EdenClient.Scene;

/// <summary>
/// Logique PURE des bulles de dialogue 3D (NCID 268 <c>ClientSay</c>) —
/// plan futur-feature/02 : coupe de ligne, durée d'affichage, nettoyage.
/// Spec visuelle : <c>ref-shots/shot_0082_000338.png</c> (bulle noire à
/// liseré clair, texte blanc, au-dessus du nom du personnage).
/// Aucune dépendance Godot (testable dans NetTests).
/// </summary>
public static class SceneBubbleLogic
{
    /// <summary>Largeur max d'une ligne de bulle (caractères).</summary>
    public const int MaxCharsPerLine = 22;

    /// <summary>Nombre max de lignes affichées (le reste est tronqué
    /// par « … »).</summary>
    public const int MaxLines = 5;

    /// <summary>Durée mini / maxi d'affichage (s).</summary>
    public const double MinSeconds = 3.0, MaxSeconds = 9.0;

    /// <summary>Durée d'affichage : 2,5 s + 0,06 s par caractère,
    /// bornée [3, 9] (HYPOTHESIS : le natif n'expose pas de durée
    /// mesurée ; ordre de grandeur des captures).</summary>
    public static double Duration(string text)
        => Math.Clamp(2.5 + 0.06 * (text?.Length ?? 0),
            MinSeconds, MaxSeconds);

    /// <summary>Retire retours chariot / caractères de contrôle et
    /// compacte les espaces (le texte serveur peut porter des
    /// <c>\r\n</c> de table).</summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var sb = new StringBuilder(text.Length);
        var lastSpace = true;
        foreach (var ch in text)
        {
            var c = char.IsControl(ch) ? ' ' : ch;
            if (c == ' ')
            {
                if (lastSpace) continue;
                lastSpace = true;
            }
            else lastSpace = false;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    /// <summary>Coupe <paramref name="text"/> en lignes de
    /// <paramref name="maxChars"/> max (aux espaces ; un mot plus long
    /// est coupé net — le CJK sans espace passe par ce chemin),
    /// plafonné à <see cref="MaxLines"/> lignes (dernière finie par
    /// « … »). Retourne les lignes jointes par « \n ».</summary>
    public static string Wrap(string? text, int maxChars = MaxCharsPerLine)
    {
        var t = Sanitize(text);
        if (t.Length == 0) return "";
        maxChars = Math.Max(4, maxChars);
        var lines = new List<string>();
        var cur = new StringBuilder();
        foreach (var word in t.Split(' '))
        {
            var w = word;
            while (w.Length > maxChars)
            {
                if (cur.Length > 0) { lines.Add(cur.ToString()); cur.Clear(); }
                lines.Add(w[..maxChars]);
                w = w[maxChars..];
            }
            if (w.Length == 0) continue;
            if (cur.Length == 0) cur.Append(w);
            else if (cur.Length + 1 + w.Length <= maxChars)
                cur.Append(' ').Append(w);
            else
            {
                lines.Add(cur.ToString());
                cur.Clear().Append(w);
            }
        }
        if (cur.Length > 0) lines.Add(cur.ToString());
        if (lines.Count > MaxLines)
        {
            lines = lines.Take(MaxLines).ToList();
            var last = lines[^1];
            lines[^1] = (last.Length >= maxChars ? last[..(maxChars - 1)]
                : last) + "…";
        }
        return string.Join('\n', lines);
    }

    /// <summary>Décode le blob « DLG1 »+i32 d'un 268 de monstre (voir
    /// serveur <c>SceneEventRules.EncodeDialogueBlob</c>) → id de
    /// dialogue ; false pour un blob vide / item-link / trop court.</summary>
    public static bool TryDecodeDialogueBlob(byte[]? blob, out int id)
    {
        id = 0;
        if (blob == null || blob.Length < 8 || blob[0] != (byte)'D' ||
            blob[1] != (byte)'L' || blob[2] != (byte)'G' ||
            blob[3] != (byte)'1') return false;
        id = BitConverter.ToInt32(blob, 4);
        return id > 0;
    }

    /// <summary>Texte à afficher : si le blob porte un id de dialogue
    /// connu de <paramref name="dialogue"/> (t_dialogue FR prioritaire,
    /// repli EN — <c>TextResolver.Dialogue</c>) on l'utilise ; sinon le
    /// texte serveur (say joueur, id inconnu).</summary>
    public static string ResolveSayText(string serverText, byte[]? blob,
        Func<int, string?> dialogue)
    {
        if (TryDecodeDialogueBlob(blob, out var id) &&
            dialogue(id) is { Length: > 0 } t)
            return t;
        return serverText;
    }
}
