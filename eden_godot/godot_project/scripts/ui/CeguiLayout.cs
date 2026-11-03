using System.Globalization;
using System.Xml.Linq;

namespace EdenClient.Ui;

/// <summary>
/// Nœud d'un <c>.layout</c> CEGUI : <c>&lt;Window Type Name&gt;</c>
/// ou <c>&lt;AutoWindow NameSuffix&gt;</c> (conteneur implicite des
/// ScrollablePane — <see cref="IsAutoWindow"/>). Les valeurs de
/// <c>&lt;Property&gt;</c> viennent de l'attribut <c>Value</c> ou, à
/// défaut, du texte interne (le layout <c>Achievement</c> sérialise
/// son Text en contenu d'élément).
/// </summary>
public sealed class CeguiNode
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsAutoWindow { get; set; }

    /// <summary>Propriétés brutes (insensible à la casse). Une
    /// propriété répétée (ex. <c>ColumnHeader</c>) garde toutes ses
    /// occurrences dans <see cref="PropLists"/>.</summary>
    public Dictionary<string, string> Props { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Toutes les occurrences par nom de propriété —
    /// <c>ColumnHeader</c> apparaît une fois par colonne.</summary>
    public Dictionary<string, List<string>> PropLists { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<CeguiNode> Children { get; } = new();

    public string? Get(string key) =>
        Props.TryGetValue(key, out var v) ? v : null;

    /// <summary>Propriété booléenne CEGUI (<c>"True"/"False"</c>).</summary>
    public bool GetBool(string key, bool fallback)
    {
        if (!Props.TryGetValue(key, out var v))
            return fallback;
        return v.Equals("True", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("1", StringComparison.OrdinalIgnoreCase)
            ? true
            : v.Equals("False", StringComparison.OrdinalIgnoreCase) ||
              v.Equals("0", StringComparison.OrdinalIgnoreCase)
                ? false
                : fallback;
    }

    public float GetFloat(string key, float fallback) =>
        Props.TryGetValue(key, out var v) &&
        float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var f)
            ? f
            : fallback;
}

/// <summary>
/// <c>UnifiedAreaRect</c> CEGUI :
/// <c>{{x_scale,x_off},{y_scale,y_off},{w_scale,w_off},{h_scale,h_off}}</c>.
/// Chaque paire résout <c>scale * parentSize + offset</c>. ATTENTION :
/// malgré le nom des champs, les 3e/4e paires sont le **bord droit** et
/// le **bord bas** du rectangle (URect = left/top/right/bottom — doc
/// CEGUI wiki « URect … left, top, right and bottom edges » +
/// <c>Element::getUnclippedOuterRect_impl</c> : taille = max − min).
/// Preuve données : les layouts contiennent des éléments dockés
/// <c>{{1,-72},{0.5,0},{1,-38},{0.5,34}}</c> (34 px au bord droit —
/// impossible si la 3e paire était une largeur).
/// </summary>
public readonly record struct UnifiedRect(
    float XScale, float XOff, float YScale, float YOff,
    float WScale, float WOff, float HScale, float HOff)
{
    /// <summary>Parse la forme <c>{{a,b},{c,d},{e,f},{g,h}}</c>
    /// (4 paires) ou <c>{{a,b},{c,d}}</c> (2 paires, taille unifiée
    /// utilisée par UnifiedMinSize/MaxSize).</summary>
    public static UnifiedRect? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        var nums = new List<float>();
        var span = s.AsSpan();
        int i = 0;
        while (i < span.Length)
        {
            var c = span[i];
            if (char.IsDigit(c) || c == '-' || c == '+' || c == '.')
            {
                int j = i + 1;
                while (j < span.Length &&
                       (char.IsDigit(span[j]) || span[j] == '.' ||
                        span[j] == 'e' || span[j] == 'E' ||
                        ((span[j] == '-' || span[j] == '+') &&
                         (span[j - 1] == 'e' || span[j - 1] == 'E'))))
                    j++;
                if (float.TryParse(span[i..j], NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var v))
                    nums.Add(v);
                i = j;
            }
            else
            {
                i++;
            }
        }
        return nums.Count switch
        {
            >= 8 => new UnifiedRect(nums[0], nums[1], nums[2], nums[3],
                nums[4], nums[5], nums[6], nums[7]),
            >= 4 => new UnifiedRect(nums[0], nums[1], nums[2], nums[3],
                0, 0, 0, 0),
            _ => null,
        };
    }
}

/// <summary>
/// Ancres + offsets Godot équivalents à une UnifiedAreaRect, prêts
/// pour <c>Control.AnchorLeft/OffsetLeft/…</c>.
///
/// Convention CEGUI : les 3e/4e paires de la UAR sont les bords
/// droit/bas — donc <c>left_anchor = x_scale, left_offset = x_off,
/// right_anchor = w_scale, right_offset = w_off</c> (idem vertical).
/// La formule Godot <c>pixel = anchor * parentSize + offset</c> est
/// strictement équivalente à l'unified-coord CEGUI. Les ancres peuvent
/// sortir de [0..1] (légal à l'exécution).
///
/// Alignement CEGUI (HorizontalAlignment/VerticalAlignment) — port de
/// <c>CoordConverter::getBaseXValue</c> (CEGUI v0-8) : la position
/// stockée est <b>conservée</b> puis le rect entier est décalé de
/// <c>(parent − size)/2</c> (Centre) ou <c>parent − size</c>
/// (Right/Bottom). Avec <c>size = (w_scale − x_scale)·P +
/// (w_off − x_off)</c>, le décalage se factorise en
/// ancre <c>(1 − Δscale)/k</c> + offset <c>−Δoffset/k</c> appliqué aux
/// DEUX bords.
/// </summary>
public readonly record struct CeguiAnchors(
    float AnchorLeft, float OffsetLeft,
    float AnchorTop, float OffsetTop,
    float AnchorRight, float OffsetRight,
    float AnchorBottom, float OffsetBottom)
{
    public static CeguiAnchors FromUnified(UnifiedRect r,
        string? hAlign = null, string? vAlign = null)
    {
        // Δ = taille du rect (bord − bord) en dims unifiées.
        var dW = r.WScale - r.XScale;   // w_scale − x_scale
        var dWo = r.WOff - r.XOff;
        var dH = r.HScale - r.YScale;
        var dHo = r.HOff - r.YOff;

        float dxA = 0f, dxO = 0f;
        switch (hAlign)
        {
            case "Centre":
            case "Center":
                dxA = (1f - dW) / 2f;
                dxO = -dWo / 2f;
                break;
            case "Right":
                dxA = 1f - dW;
                dxO = -dWo;
                break;
        }
        float dyA = 0f, dyO = 0f;
        switch (vAlign)
        {
            case "Centre":
            case "Center":
                dyA = (1f - dH) / 2f;
                dyO = -dHo / 2f;
                break;
            case "Bottom":
                dyA = 1f - dH;
                dyO = -dHo;
                break;
        }
        return new CeguiAnchors(
            r.XScale + dxA, r.XOff + dxO,
            r.YScale + dyA, r.YOff + dyO,
            r.WScale + dxA, r.WOff + dxO,
            r.HScale + dyA, r.HOff + dyO);
    }
}

/// <summary>
/// Parseur de <c>uidata/layouts/*.layout</c> (UTF-8, XML :
/// <c>&lt;GUILayout&gt;</c> → <c>&lt;Window&gt;</c> /
/// <c>&lt;AutoWindow&gt;</c> / <c>&lt;Property&gt;</c> imbriqués).
///
/// Tolérances constatées sur les 441 fichiers Reforged : caractères
/// de contrôle (0x0B) dans les Text, une déclaration
/// <c>&lt;?xml?&gt;</c> dupliquée en cours de document, et fichiers
/// en Big5 malgré <c>encoding="UTF-8"</c>. <see cref="ParseFile"/>
/// sanitize puis réessaie en Big5 avant de déclarer l'échec.
/// </summary>
public static class CeguiLayout
{
    static CeguiLayout()
    {
        // Big5 (CP950) hors BCL : provider CodePages — requis par le
        // décodeur de repli (déjà référencé par les deux csproj).
        System.Text.Encoding.RegisterProvider(
            System.Text.CodePagesEncodingProvider.Instance);
    }

    /// <summary>Parse un fichier .layout → nœud racine.</summary>
    public static CeguiNode ParseFile(string path)
    {
        var bytes = Vfs.EdenVfs.ReadAllBytes(path)
            ?? throw new FileNotFoundException(path);
        foreach (var decode in Decoders())
        {
            var text = decode(bytes).TrimStart('\uFEFF');
            var clean = SanitizeXml(text);
            foreach (var candidate in Candidates(clean))
            {
                try
                {
                    return Parse(XDocument.Parse(candidate));
                }
                catch (System.Xml.XmlException)
                {
                    // essai suivant (wrap puis décodage alternatif)
                }
            }
        }
        // Dernier recours : que l'erreur remonte clairement.
        return Parse(XDocument.Parse(
            SanitizeXml(Decoders()[0](bytes))));
    }

    /// <summary>Variantes tentées : texte tel quel ; déclaration
    /// <c>&lt;?xml?&gt;</c> retirée quand elle n'est pas en tête
    /// (un commentaire <c>&lt;!--…--&gt;</c> ou un log collé avant la
    /// déclaration est refusé par XML strict — le commentaire reste
    /// légal avant la racine une fois la déclaration partie) ; puis
    /// enveloppé dans une racine unique (<c>InventoryWnd - Copia.layout</c>
    /// contient deux <c>&lt;GUILayout&gt;</c> concaténés — le premier
    /// est retenu par <see cref="Parse"/>).</summary>
    private static IEnumerable<string> Candidates(string clean)
    {
        yield return clean;
        var decl = clean.IndexOf("<?xml", StringComparison.Ordinal);
        if (decl > 0)
        {
            var end = clean.IndexOf("?>", decl,
                StringComparison.Ordinal);
            if (end >= 0)
                yield return clean[..decl] + clean[(end + 2)..];
        }
        yield return "<__wrap__>" + clean + "</__wrap__>";
    }

    /// <summary>Décodeurs tentés dans l'ordre : UTF-8 déclaré, puis
    /// Big5 (encodage des tables du client).</summary>
    private static Func<byte[], string>[] Decoders() => new Func<byte[], string>[]
    {
        b => System.Text.Encoding.UTF8.GetString(b),
        b => System.Text.Encoding.GetEncoding(950).GetString(b),
    };

    /// <summary>Supprime les caractères invalides en XML 1.0
    /// (contrôles hors \t\r\n) et les déclarations <c>&lt;?xml?&gt;</c>
    /// au-delà de la première.</summary>
    private static string SanitizeXml(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var xmlDeclCount = 0;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c < 0x20 && c != '\t' && c != '\r' && c != '\n')
                continue; // caractère de contrôle → supprimé
            if (c == '<' && i + 4 < text.Length &&
                text.AsSpan(i, 5).SequenceEqual("<?xml"))
            {
                xmlDeclCount++;
                if (xmlDeclCount > 1)
                {
                    var end = text.IndexOf("?>", i, StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        i = end + 1; // saute la déclaration dupliquée
                        continue;
                    }
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static CeguiNode Parse(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("layout vide");
        // Premier <Window>/<AutoWindow> en ordre de document = la
        // fenêtre racine (tolère une enveloppe <__wrap__> ou un
        // <GUILayout> quelconque).
        var win = root.Name.LocalName is "Window" or "AutoWindow"
            ? root
            : root.Descendants().FirstOrDefault(e =>
                e.Name.LocalName is "Window" or "AutoWindow");
        if (win == null)
            throw new InvalidDataException("layout sans <Window> racine");
        return ParseWindow(win);
    }

    private static CeguiNode ParseWindow(XElement el)
    {
        var node = new CeguiNode();
        if (el.Name.LocalName == "AutoWindow")
        {
            node.IsAutoWindow = true;
            node.Type = "__auto__";
            node.Name = el.Attribute("NameSuffix")?.Value ?? "__auto__";
        }
        else
        {
            node.Type = el.Attribute("Type")?.Value ?? "";
            node.Name = el.Attribute("Name")?.Value ?? "";
        }
        foreach (var sub in el.Elements())
        {
            switch (sub.Name.LocalName)
            {
                case "Property":
                    var name = sub.Attribute("Name")?.Value;
                    if (name == null)
                        break;
                    // Value en attribut, sinon texte interne (Text
                    // multiligne sérialisé en contenu d'élément).
                    var value = sub.Attribute("Value")?.Value
                        ?? sub.Value.Trim('\r', '\n', ' ', '\t');
                    if (!node.PropLists.TryGetValue(name, out var list))
                        node.PropLists[name] = list = new List<string>();
                    list.Add(value);
                    if (!node.Props.ContainsKey(name))
                        node.Props[name] = value;
                    break;
                case "Window":
                case "AutoWindow":
                    node.Children.Add(ParseWindow(sub));
                    break;
                // <Event> et autres : ignorés silencieusement (aucun
                // n'est présent dans le set Reforged).
            }
        }
        return node;
    }

    /// <summary>Parse les 4 flottants « scale,offset » d'une taille
    /// unifiée (<c>{{w_s,w_o},{h_s,h_o}}</c>). Retourne
    /// <c>(wScale,wOff,hScale,hOff)</c>.</summary>
    public static (float ws, float wo, float hs, float ho)? ParseUnifiedSize(
        string? s)
    {
        var r = UnifiedRect.Parse(s);
        return r is { } v ? (v.XScale, v.XOff, v.YScale, v.YOff) : null;
    }
}
