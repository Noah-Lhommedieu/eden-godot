using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre de mort (lot S1) — « Vous êtes mort » + bouton
/// Réapparaître. Ouverte par l'event <see cref="PlayerVitals.Died"/>
/// (305 ClientDie du self) et refermée par
/// <see cref="PlayerVitals.Revived"/> (306 ClientRevive).
///
/// Le bouton envoie NCID 20 <see cref="ZoneReviveRequest"/>
/// <c>[i32 type=0]</c> (point de résurrection — même octet que le
/// helper <c>GameScene.SendReviveAsync</c>). L'aire liée du 531
/// (<see cref="ZoneClientReviveInfo"/>, GOLDEN) est affichée si connue.
/// </summary>
public partial class DeathWindow : Control
{
    /// <summary>Types du NCID 20 (serveur : <c>ReviveRules</c>).</summary>
    private static class ReviveRequest
    {
        public const int SavePoint = 0, InPlace = 1, Item = 5, LifeLeaf = 6;
    }

    // Feuille de Vie de Locus (violette, sur place) / Feuille de Vie
    // (verte, point de sauvegarde) — ids c_item/t_itemmall.
    private static readonly int[] LocusLeafIds = { 40036, 41468 };
    private static readonly int[] LifeLeafIds = { 40035, 40157 };

    private BaseButton? _itemBtn;

    /// <summary>Type de revive par feuille que le sac contient
    /// (Locus d'abord), -1 si aucune.</summary>
    private static int LeafRequestType()
    {
        var items = PlayerBag.Items;
        if (items.Any(i => LocusLeafIds.Contains(i.ItemId)))
            return ReviveRequest.Item;
        if (items.Any(i => LifeLeafIds.Contains(i.ItemId)))
            return ReviveRequest.LifeLeaf;
        return -1;
    }

    /// <summary>Grise « Utiliser un item » sans feuille en sac.</summary>
    private void RefreshItemChoice()
    {
        if (_itemBtn == null) return;
        _itemBtn.Disabled = LeafRequestType() < 0;
    }

    internal const string Banner = "Vous êtes mort";
    internal const string ChoiceHere = "Revenir ici";
    internal const string ChoiceSavePoint = "Revenir au point de sauvegarde";
    internal const string ChoiceItem = "Utiliser un item";

    private Control? _panel;
    private Label? _area;

    public override void _Ready()
    {
        // Layout natif ReviveMenuUI : 3 boutons de choix (haut → bas),
        // libellés français ; le décompte / accepter / refuser du revive
        // par un autre joueur ne servent pas ici.
        _panel = HudExtensions.BuildNativeWindow(this,
            "ReviveMenuUI.layout", "death", new Vector2(300, 170));
        if (_panel != null)
        {
            _panel.Name = "__death";
            _panel.OffsetLeft = -150; _panel.OffsetRight = 150;
            _panel.OffsetTop = -85; _panel.OffsetBottom = 85;
            _panel.CustomMinimumSize = new Vector2(300, 170);
            SetTitle(_panel, Banner);
            // Fond sous la barre de titre : sinon le 1er bouton est rogné.
            if (TopLeftVitals.Find<Control>(_panel, "Background")
                    is { } bg)
                bg.OffsetTop = 22;
            Choice("NearbyBtn", ChoiceHere, ReviveRequest.InPlace);
            Choice("DirectBtn", ChoiceSavePoint, ReviveRequest.SavePoint);
            Choice("ItemmallBtn", ChoiceItem, -1);
            _itemBtn = TopLeftVitals.Find<BaseButton>(_panel, "ItemmallBtn");
            RefreshItemChoice();
            foreach (var hidden in new[] { "AcceptBtn", "RefuseBtn",
                         "HelpBtn", "CountDownText" })
                if (TopLeftVitals.Find<CanvasItem>(_panel, hidden)
                        is { } h)
                    h.Visible = false;
        }
        else
        {
        var p = new Panel { Name = "__death" };
        _panel = p;
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-150, -80);
        p.Size = new Vector2(300, 160);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.04f, 0.05f, 0.95f),
            BorderColor = new Color(0.6f, 0.3f, 0.3f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
        });
        AddChild(p);
        DragHelper.EnableBackgroundDrag(p);
        DragHelper.Persist(p, "death");

        var title = new Label { Text = Banner };
        title.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.OffsetTop = 18;
        title.AddThemeFontSizeOverride("font_size", 16);
        title.AddThemeColorOverride("font_color",
            new Color(0.95f, 0.5f, 0.45f));
        p.AddChild(title);

        _area = new Label { Text = "" };
        _area.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _area.HorizontalAlignment = HorizontalAlignment.Center;
        _area.OffsetTop = 52;
        _area.AddThemeFontSizeOverride("font_size", 10);
        _area.AddThemeColorOverride("font_color",
            new Color(0.8f, 0.8f, 0.85f));
        p.AddChild(_area);

        var revive = new Button { Text = ChoiceSavePoint };
        revive.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        revive.Position = new Vector2(-70, -46);
        revive.Size = new Vector2(140, 34);
        revive.Pressed += OnRevive;
        p.AddChild(revive);
        }

        Visible = false;
        PlayerVitals.Died += OnDied;
        PlayerVitals.Revived += OnRevived;
        PlayerBag.Changed += RefreshItemChoice;
    }

    private void Choice(string node, string label, int reviveType)
    {
        if (TopLeftVitals.Find<BaseButton>(_panel!, node) is not { } b)
            return;
        if (b is Button btn)
        {
            btn.Text = label;
            btn.AddThemeColorOverride("font_color", Colors.White);
            btn.AddThemeColorOverride("font_hover_color", Colors.White);
            btn.AddThemeFontSizeOverride("font_size", 12);
        }
        b.Pressed += () =>
            SendRevive(reviveType >= 0 ? reviveType : LeafRequestType());
    }

    /// <summary>Remplace le texte de barre de titre du layout natif.</summary>
    private static void SetTitle(Node root, string text)
    {
        foreach (var n in root.GetChildren())
        {
            if (n is Label l && l.Text.Contains("Revive"))
                l.Text = text;
            SetTitle(n, text);
        }
    }

    private void SendRevive(int reviveType)
    {
        if (reviveType < 0) return;
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true)
        {
            var task = s.SendZoneAsync(new ZoneReviveRequest
                { ReviveType = reviveType },
                CancellationToken.None);
            _ = task.ContinueWith(t =>
                GD.PrintErr($"[death] revive : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
        else
        {
            Visible = false;
        }
    }

    public override void _ExitTree()
    {
        PlayerVitals.Died -= OnDied;
        PlayerVitals.Revived -= OnRevived;
        PlayerBag.Changed -= RefreshItemChoice;
        base._ExitTree();
    }

    /// <summary>État factice pour capture (--hud-open hors
    /// session) — le libellé natif « …casts Revive on you »
    /// suffit, pas de texte additionnel.</summary>
    public void EnsureDemo() => Visible = true;

    /// <summary>305 self — montre la fenêtre (thread principal).</summary>
    private void OnDied(uint killer)
    {
        if (_area != null) _area.Text = "";
        RefreshItemChoice();
        Visible = true;
    }

    /// <summary>306 self — referme.</summary>
    private void OnRevived() => Visible = false;

    /// <summary>Bouton repli → NCID 20 Revive [i32 0].</summary>
    private void OnRevive() => SendRevive(0);
}
