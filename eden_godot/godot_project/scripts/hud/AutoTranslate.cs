using Godot;
using EdenClient.Data;

namespace EdenClient.Hud;

/// <summary>
/// Filet de traduction : tout Label/Button/CheckBox (et tooltip) dont le
/// texte exact figure dans le dictionnaire EN→langue est traduit à
/// l'ajout dans l'arbre, et re-traduit au changement de langue. La
/// source EN est mémorisée en meta pour pouvoir re-traduire. Les textes
/// dynamiques (non présents dans le dictionnaire) ne sont pas touchés.
/// </summary>
public partial class AutoTranslate : Node
{
    private const string Src = "eden_i18n_src", SrcTip = "eden_i18n_tip";

    public override void _Ready()
    {
        GetTree().NodeAdded += OnAdded;
        TextResolver.LanguageChanged += Retranslate;
    }

    public override void _ExitTree()
    {
        if (GetTree() != null) GetTree().NodeAdded -= OnAdded;
        TextResolver.LanguageChanged -= Retranslate;
    }

    private void OnAdded(Node n)
    {
        if (n is Label or BaseButton or LineEdit or TabContainer)
            Callable.From(() => Apply(n)).CallDeferred();
    }

    private static string? GetText(Node n) => n switch
    {
        Label l => l.Text,
        Button b => b.Text,
        _ => null,
    };

    private static void SetText(Node n, string t)
    {
        if (n is Label l) l.Text = t;
        else if (n is Button b) b.Text = t;
    }

    private static readonly HashSet<string> _missLogged = new();

    private static void Miss(string text)
    {
        if (TextResolver.Language == "en" || text.Length < 3 ||
            !text.Any(char.IsLetter) || text.Any(c => c > 0x24f) ||
            UiTextEn.IsTranslation(text)) return;
        if (_missLogged.Add(text))
            GD.Print($"[i18n-miss] {text.Replace("\n", "\\n")}");
    }

    private static void Apply(Node n)
    {
        if (!GodotObject.IsInstanceValid(n)) return;
        if (n is TabContainer tc)
        {
            for (var i = 0; i < tc.GetTabCount(); i++)
            {
                var t = tc.GetTabTitle(i);
                var tr = UiTextEn.Translate(t);
                if (tr != t) tc.SetTabTitle(i, tr); else Miss(t);
            }
            return;
        }
        var cur = GetText(n);
        if (!string.IsNullOrEmpty(cur) && cur.Any(char.IsLetter))
        {
            var src = n.HasMeta(Src) ? n.GetMeta(Src).AsString() : cur;
            var tr = UiTextEn.Translate(src);
            // Texte changé par le code depuis la dernière passe : on repart de lui.
            if (n.HasMeta(Src) && cur != UiTextEn.Translate(src) &&
                cur != src) src = cur;
            tr = UiTextEn.Translate(src);
            if (tr == src && !n.HasMeta(Src) && n is not LineEdit) Miss(src);
            if (tr != src || n.HasMeta(Src))
            {
                n.SetMeta(Src, src);
                if (tr != cur) SetText(n, tr);
            }
        }
        if (n is Control c && c.TooltipText.Length > 0)
        {
            var src = c.HasMeta(SrcTip) ? c.GetMeta(SrcTip).AsString() : c.TooltipText;
            var tr = UiTextEn.Translate(src);
            if (tr != src || c.HasMeta(SrcTip))
            {
                c.SetMeta(SrcTip, src);
                if (tr != c.TooltipText) c.TooltipText = tr;
            }
        }
        if (n is LineEdit le && le.PlaceholderText.Length > 0)
            le.PlaceholderText = UiTextEn.Translate(le.PlaceholderText);
    }

    private void Retranslate()
    {
        foreach (var n in GetTree().Root.FindChildren("*", "", true, false))
            if (n is Label or BaseButton or LineEdit or TabContainer) Apply(n);
    }
}
