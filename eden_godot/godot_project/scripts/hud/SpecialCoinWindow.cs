using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Devises spéciales (fame shop) — lot D3, côté affichage. Layout
/// natif <c>specialcoinui2_na.layout</c> (« Root/SpecialCoin »,
/// génération Aeria — FrameWindow + ScrollPane) avec lignes
/// <c>specialcoinitem.layout</c> (NameText/Icon/NumText).
///
/// Monnaies affichées : celles que le serveur pousse déjà —
/// WarCoin (576), AlpacaCoin (580), LifelongGold/LifelongPoint et la
/// devise <c>WorldUpdateSpecialCoin</c> (id+valeur). L'achat aux
/// fame-shops (devises natives 2/4/6…) reste dépendant du serveur
/// (lot E3 — §5-S1 de la synthèse) : aucune émission C→S ici.
///
/// Ouverture : icône « SpecialCoin » de la barre d'inventaire
/// (PlayerAction natif : simple affichage local, pas d'op serveur).
/// </summary>
public partial class SpecialCoinWindow : Control
{
    private Control? _window;
    private VBoxContainer? _rows;

    /// <summary>Devises connues — (id, nom affiché, valeur live).</summary>
    private static (string Name, Func<int> Value)[] Coins() =>
        new (string, Func<int>)[]
    {
        (UiTextEn.Translate("War Coins"),
            () => PlayerCoins.WarCoin),
        (UiTextEn.Translate("Alpaca Coins"),
            () => PlayerCoins.AlpacaCoin),
        (UiTextEn.Translate("Lifelong Gold"),
            () => PlayerCoins.LifelongGold),
        (UiTextEn.Translate("Lifelong Points"),
            () => PlayerCoins.LifelongPoint),
    };

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        PlayerCoins.Changed += Refresh;
    }

    public override void _ExitTree()
    {
        PlayerCoins.Changed -= Refresh;
        base._ExitTree();
    }

    public void Open()
    {
        if (_window == null) BuildWindow();
        _window!.Visible = true;
        Visible = true;
        Refresh();
    }

    private void BuildWindow()
    {
        var hud = HudExtensions.FindHud();
        _window = hud?.Build("specialcoinui2_na.layout")
            ?? hud?.Build("cnspecialcoinui.layout");
        if (_window == null)
        {
            _window = new PanelContainer
            { CustomMinimumSize = new Vector2(280, 320) };
            ((PanelContainer)_window).AddChild(new Label
            { Text = "specialcoinui2_na.layout introuvable" });
        }
        AddChild(_window);
        HudExtensions.CenterNative(_window,
            new Vector2(400, 500));
        DragHelper.EnsureTitleBar(_window,
            UiTextEn.Translate("Special currency"));
        DragHelper.Persist(_window, "specialcoin");
        var pane = FindLeaf<ScrollContainer>(_window, "ScrollPane");
        if (pane != null)
        {
            _rows = new VBoxContainer
            { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            pane.AddChild(_rows);
        }
        else
        {
            _rows = new VBoxContainer { Position = new Vector2(12, 40) };
            _window.AddChild(_rows);
        }
    }

    private void Refresh()
    {
        if (_rows == null) return;
        foreach (var c in _rows.GetChildren()) c.QueueFree();
        foreach (var (name, value) in Coins())
        {
            var item = HudExtensions.FindHud()
                ?.Build("specialcoinitem.layout");
            if (item != null)
            {
                // Rang 305×24 dans le layout — le VBox écrase la
                // position mais pas la taille : imposer le minimum.
                item.CustomMinimumSize = new Vector2(0, 24);
                _rows.AddChild(item);
                SetLeafText(item, "NameText", name);
                SetLeafText(item, "NumText", $"{value()}");
            }
            else
            {
                var l = new Label { Text = $"{name}: {value()}" };
                _rows.AddChild(l);
            }
        }
        // Devise poussée par le monde (WorldUpdateSpecialCoin) — si
        // présente et différente des 4 fixes.
        var sp = PlayerProfileState.SpecialCoin;
        if (sp.Id != 0)
        {
            var l = new Label
            { Text = $"#{sp.Id}: {sp.Value}" };
            _rows.AddChild(l);
        }
    }

    private static void SetLeafText(Control root, string leaf,
        string text)
    {
        foreach (var n in root.FindChildren("*", "", true, false))
        {
            if (!n.Name.ToString().EndsWith(leaf)) continue;
            if (n is Label l) { l.Text = text; return; }
            if (n is RichTextLabel r) { r.Text = text; return; }
        }
    }

    private static T? FindLeaf<T>(Node root, string leaf)
        where T : class
    {
        foreach (var n in root.FindChildren("*", typeof(T).Name,
                     true, false))
            if (n.Name.ToString().EndsWith(leaf))
                return n as T;
        return null;
    }
}
