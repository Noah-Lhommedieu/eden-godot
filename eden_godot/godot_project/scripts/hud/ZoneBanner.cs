using Godot;
using EdenClient.Data;

namespace EdenClient.Hud;

/// <summary>
/// Bandeau de zone centré (nom en gros jaune à l'entrée — « Aven » de
/// la capture) + texte système rouge centré (474 ScreenMessage). Les
/// deux s'estompent après quelques secondes.
/// </summary>
public partial class ZoneBanner : Control
{
    private Label? _zone;
    private Label? _sys;
    private Label? _combat, _notice;
    private double _zoneTtl, _sysTtl, _combatTtl, _noticeTtl;

    public override void _Ready() => Ensure();

    /// <summary>Crée les labels à la demande — <c>ShowZone</c> peut
    /// être appelé avant <c>_Ready</c> (montage pendant le chargement
    /// CEGUI asynchrone du HudRoot).</summary>
    private void Ensure()
    {
        if (_zone != null) return;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        _zone = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _zone.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        // Ref juste le jeu.png : « Aven » jaune vif centré à y≈118.
        _zone.OffsetTop = 98;
        _zone.OffsetLeft = -300; _zone.OffsetRight = 300;
        _zone.AddThemeFontSizeOverride("font_size", 34);
        _zone.AddThemeColorOverride("font_color",
            Color.Color8(250, 240, 20));
        _zone.AddThemeColorOverride("font_outline_color",
            Color.Color8(90, 70, 0, 200));
        _zone.AddThemeConstantOverride("outline_size", 3);
        _zone.Modulate = new Color(1, 1, 1, 0);
        AddChild(_zone);

        _sys = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _sys.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        // Ref : « You have acquired <X> state » rouge à y≈223.
        _sys.OffsetTop = 214;
        _sys.OffsetLeft = -400; _sys.OffsetRight = 400;
        _sys.AddThemeFontSizeOverride("font_size", 16);
        _sys.AddThemeColorOverride("font_color",
            Color.Color8(230, 20, 20));
        _sys.AddThemeColorOverride("font_outline_color",
            new Color(0f, 0f, 0f, 0.35f));
        _sys.AddThemeConstantOverride("outline_size", 2);
        _sys.Modulate = new Color(1, 1, 1, 0);
        AddChild(_sys);

        // Ref shot_0037 : « Entering Combat » rouge-orangé centré,
        // bande de texte y=188-216 sur 878p → y≈154 en 720p.
        _combat = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _combat.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        _combat.OffsetTop = 152;
        _combat.OffsetLeft = -300; _combat.OffsetRight = 300;
        _combat.AddThemeFontSizeOverride("font_size", 30);
        _combat.AddThemeConstantOverride("outline_size", 3);
        _combat.Modulate = new Color(1, 1, 1, 0);
        AddChild(_combat);

        // Ref NouveauSkillApresLevelUp/ClassPanel-ChangementDeClasse :
        // « You have learned skill X. » / « Switched Class to X » en
        // gros texte blanc centré (y≈272 sur 720p).
        _notice = new Label
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _notice.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        _notice.OffsetTop = 250;
        _notice.OffsetLeft = -400; _notice.OffsetRight = 400;
        _notice.AddThemeFontSizeOverride("font_size", 34);
        _notice.AddThemeColorOverride("font_color", Colors.White);
        _notice.AddThemeColorOverride("font_outline_color",
            new Color(0.3f, 0.3f, 0.3f, 0.7f));
        _notice.AddThemeConstantOverride("outline_size", 3);
        _notice.Modulate = new Color(1, 1, 1, 0);
        AddChild(_notice);
    }

    /// <summary>Grand texte blanc centré ~4 s (apprentissage de sort,
    /// changement de classe).</summary>
    public void ShowNotice(string text, double seconds = 4.0)
    {
        Ensure();
        if (text.Length == 0 || _notice == null) return;
        _notice.Text = text;
        _noticeTtl = seconds;
        _notice.Modulate = new Color(1, 1, 1, 1);
    }

    /// <summary>« Entering Combat » / « Leaving Combat » ~2,5 s.</summary>
    public void ShowCombat(bool entering, double seconds = 2.5)
    {
        Ensure();
        if (_combat == null) return;
        _combat.Text = entering ? UiTextEn.Translate("Entering Combat") : UiTextEn.Translate("Leaving Combat");
        _combat.AddThemeColorOverride("font_color", entering
            ? Color.Color8(235, 75, 35) : Color.Color8(245, 245, 245));
        _combat.AddThemeColorOverride("font_outline_color", entering
            ? new Color(0.3f, 0.05f, 0f, 0.5f)
            : new Color(0.25f, 0.25f, 0.25f, 0.6f));
        _combatTtl = seconds;
        _combat.Modulate = new Color(1, 1, 1, 1);
    }

    /// <summary>Affiche le nom de zone en grand jaune ~4 s.</summary>
    public void ShowZone(string name)
    {
        Ensure();
        if (name.Length == 0 || _zone == null) return;
        _zone.Text = name;
        _zoneTtl = 4.0;
        _zone.Modulate = new Color(1, 1, 1, 1);
    }

    /// <summary>Texte système rouge centré ~4 s (NCID 474).</summary>
    public void ShowSystemText(string text, double seconds = 4.0)
    {
        Ensure();
        if (text.Length == 0 || _sys == null) return;
        _sys.Text = text;
        _sysTtl = seconds;
        _sys.Modulate = new Color(1, 1, 1, 1);
    }

    public override void _Process(double delta)
    {
        if (_zone != null && _zoneTtl > 0)
        {
            _zoneTtl -= delta;
            _zone.Modulate = new Color(1, 1, 1,
                (float)Math.Clamp(Math.Min(_zoneTtl, 1), 0, 1));
        }
        if (_combat != null && _combatTtl > 0)
        {
            _combatTtl -= delta;
            _combat.Modulate = new Color(1, 1, 1,
                (float)Math.Clamp(Math.Min(_combatTtl, 1), 0, 1));
        }
        if (_notice != null && _noticeTtl > 0)
        {
            _noticeTtl -= delta;
            _notice.Modulate = new Color(1, 1, 1,
                (float)Math.Clamp(Math.Min(_noticeTtl, 1), 0, 1));
        }
        if (_sys != null && _sysTtl > 0)
        {
            _sysTtl -= delta;
            _sys.Modulate = new Color(1, 1, 1,
                (float)Math.Clamp(Math.Min(_sysTtl, 1), 0, 1));
        }
    }
}
