using Godot;
using EdenClient.Data;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>M7 : fenêtre d'autel (boutons radar « Lty » bleu = Star
/// Diamond / Eden Crystal, « IGLty » rose = Eden Gem). Affiche le
/// solde, le contenu du tirage et les boutons de tirage ; le tirage
/// réel n'a pas de paquet serveur identifié — boutons désactivés.</summary>
public partial class AltarWindow : Control
{
    public string Title { get; set; } = "Altar";
    public string[] Currencies { get; set; } = Array.Empty<string>();
    public string Description { get; set; } = "";

    private Panel? _win;
    private readonly List<Label> _balances = new();

    public override void _Ready()
    {
        var hud = HudExtensions.FindHud();
        if (hud == null) return;
        _win = EeSkin.MakeWindow(hud, Title, 420, 150, 440, 330);
        _win.Name = Name + "Panel";
        AddChild(_win);
        var desc = new Label
        {
            Text = Description,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        HudRoot.Pin(desc, 0, 0, 1, 0, 16, 40, -16, 100);
        EeSkin.Shadowed(desc, 12, Color.Color8(222, 220, 212));
        _win.AddChild(desc);
        var box = new Panel();
        HudRoot.Pin(box, 0, 0, 1, 0, 16, 110, -16, 110 + 30 * Currencies.Length + 12);
        box.AddThemeStyleboxOverride("panel", EeSkin.InsetBox());
        _win.AddChild(box);
        for (var i = 0; i < Currencies.Length; i++)
        {
            var l = new Label { MouseFilter = MouseFilterEnum.Ignore };
            HudRoot.Pin(l, 0, 0, 1, 0, 10, 6 + i * 30, -10, 30 + i * 30);
            EeSkin.Shadowed(l, 13, Color.Color8(240, 200, 70));
            box.AddChild(l);
            _balances.Add(l);
        }
        var y = 110 + 30 * Currencies.Length + 30;
        foreach (var (txt, x0, x1) in new[]
                 { ("Draw ×1", 16, 150), ("Draw ×10", 160, 294),
                   ("Close", 304, 424) })
        {
            var b = EeSkin.Grey(new Button
            {
                Text = UiTextEn.Translate(txt),
                FocusMode = FocusModeEnum.None,
            }, 12);
            if (txt == "Close") b.Pressed += () => HudWindows.Close(_win);
            else
            {
                b.Disabled = true;
                b.TooltipText = UiTextEn.Translate("Altar draws are not available on this server yet");
            }
            HudRoot.Pin(b, 0, 0, 0, 0, x0, y, x1, y + 30);
            _win.AddChild(b);
        }
        _win.Visible = false;
        PlayerCoins.Changed += Refresh;
        Refresh();
    }

    public override void _ExitTree()
    {
        PlayerCoins.Changed -= Refresh;
        base._ExitTree();
    }

    private void Refresh()
    {
        for (var i = 0; i < _balances.Count; i++)
            _balances[i].Text = Currencies[i] switch
            {
                "Alpaca Coin" => UiTextEn.Translate($"Alpaca Coin: {PlayerCoins.AlpacaCoin}"),
                "War Coin" => UiTextEn.Translate($"War Coin: {PlayerCoins.WarCoin}"),
                _ => $"{Currencies[i]}: --",
            };
    }
}
