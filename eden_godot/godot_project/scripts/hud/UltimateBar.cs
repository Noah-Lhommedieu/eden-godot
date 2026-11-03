using Godot;
using EdenClient.Data;

namespace EdenClient.Hud;

/// <summary>
/// Barre « ULTIMATE » haut-centre — instancie le layout natif Reforged
/// <c>limitless/LimitlessHud.layout</c> (FrameWindow draggable,
/// Text + Bar alimentés par NCID 2032 côté client natif ; ici valeurs
/// factices tant que le paquet n'est pas émis par le serveur C#).
/// </summary>
public partial class UltimateBar : Control
{
    private Label? _text;
    private ProgressBar? _bar;

    public void Build(HudRoot hud)
    {
        var root = hud.Build("limitless/LimitlessHud.layout");
        if (root != null)
        {
            AddChild(root);
            _text = TopLeftVitals.Find<Label>(root, "Text");
            _bar = TopLeftVitals.Find<ProgressBar>(root, "Bar");
        }
        else
        {
            // Repli : titre + barre grise centrée en haut.
            var box = new VBoxContainer();
            box.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
            box.Position = new Vector2(-180, 2);
            box.CustomMinimumSize = new Vector2(360, 0);
            var title = new Label
            {
                Text = UiTextEn.Translate("ULTIMATE"),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _text = new Label
            {
                Text = UiTextEn.Translate("ULTIMATE  Lv --"),
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            _bar = new ProgressBar { MinValue = 0, MaxValue = 1 };
            box.AddChild(title); box.AddChild(_text); box.AddChild(_bar);
            AddChild(box);
        }
    }

    /// <summary>Niveau/XP/perks ultimes affichés (texte libre).</summary>
    public void Set(int level, long exp, long next, int perks)
    {
        if (_text != null)
            _text.Text = UiTextEn.Translate($"ULTIMATE  Lv {level}   EXP {exp}/{next}   ") +
                UiTextEn.Translate($"Perks {perks}");
        if (_bar != null)
            _bar.Value = next > 0 ? Math.Clamp((double)exp / next, 0, 1) : 0;
    }
}
