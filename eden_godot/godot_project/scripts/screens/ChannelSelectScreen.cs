using Godot;
using EdenClient.Data;
using EdenClient.Hud;

namespace EdenClient.Screens;

/// <summary>
/// Sélection du monde/channel — fenêtre centrée style natif
/// (référence : capture <c>Selection_channel.png</c>) sur le hall 3D
/// partagé <see cref="TempleBackdrop"/>.
///
/// Le layout CEGUI <c>CWorldServerListWnd.layout</c> rendait des blocs
/// blancs sans images résolues — on reconstruit la fenêtre à la main :
/// colonne « Server » (mondes) + colonne « Ch. » (canal du monde),
/// bandeau message, Confirm/Cancel, boutons « Site officiel » /
/// « Quitter » en bas à droite comme le natif.
/// </summary>
public partial class ChannelSelectScreen : Control
{
    private ScreenManager Mgr => (ScreenManager)GetParent();
    private ItemList? _worldList;
    private ItemList? _channelList;
    private Label? _error;
    private bool _busy;

    private static readonly Color PanelBg = new(0.07f, 0.09f, 0.14f, 0.97f);
    private static readonly Color Border = new(0.55f, 0.48f, 0.30f);
    private static readonly Color ListBg = new(0.85f, 0.86f, 0.88f, 0.10f);
    private static readonly Color TextDim = new(0.82f, 0.84f, 0.90f);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Même hall natif que login/sélection (preuve reverse : scène
        // ID 99 → S218 ; S406 n'était pas le décor de pré-partie).
        // Même cadrage que le fond fixe du login (login_bg.jpg) : pas de
        // saut visuel login → channel.
        var tb = TempleBackdrop.Attach(this, TempleBackdrop.Shot.Select);
        tb.Camera.Position = new Vector3(318f, 7f, -640f);
        tb.Camera.LookAt(new Vector3(318f, 12f, -600f), Vector3.Up);
        tb.Camera.Fov = 52f;
        tb.ClearMarker();
        AddChild(PreGameStyle.Vignette());

        BuildWindow();
        BuildFooter();
        Populate();
    }

    /// <summary>Fenêtre centrée ~640×400 : deux listes + message +
    /// boutons — reproduit la fenêtre native.</summary>
    private void BuildWindow()
    {
        var win = new Panel { Name = "ChannelWindow" };
        win.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        win.OffsetLeft = -320; win.OffsetRight = 320;
        win.OffsetTop = -205; win.OffsetBottom = 205;
        win.AddThemeStyleboxOverride("panel", PreGameStyle.Panel(0.88f, 16));
        AddChild(win);

        var cols = new HBoxContainer();
        cols.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        cols.OffsetTop = 34;
        cols.OffsetBottom = -72;
        cols.AddThemeConstantOverride("separation", 10);
        win.AddChild(cols);

        var winTitle = PreGameStyle.Text(
            UiTextEn.Translate("Server Selection"), 17,
            PreGameStyle.Gold, HorizontalAlignment.Center);
        winTitle.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        winTitle.OffsetTop = 0; winTitle.OffsetBottom = 26;
        win.AddChild(winTitle);
        var winRule = PreGameStyle.Rule();
        winRule.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        winRule.OffsetTop = 28; winRule.OffsetBottom = 29;
        win.AddChild(winRule);

        // --- colonne Server (mondes) ---
        var srvCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        srvCol.AddThemeConstantOverride("separation", 4);
        cols.AddChild(srvCol);
        srvCol.AddChild(Header(UiTextEn.Translate("Server")));
        _worldList = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FixedIconSize = new Vector2I(20, 20),
        };
        StyleParchmentList(_worldList);
        srvCol.AddChild(_worldList);

        // --- colonne Ch. (canaux du monde) ---
        var chCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        chCol.AddThemeConstantOverride("separation", 4);
        cols.AddChild(chCol);
        chCol.AddChild(Header(UiTextEn.Translate("Ch.")));
        _channelList = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FixedIconSize = new Vector2I(20, 20),
        };
        StyleParchmentList(_channelList);
        chCol.AddChild(_channelList);

        // --- bandeau message ---
        var msg = new Panel();
        msg.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        msg.OffsetTop = -70; msg.OffsetBottom = -40;
        msg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.96f, 0.945f, 0.91f),
            BorderColor = new Color(0.58f, 0.57f, 0.54f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 8,
        });
        win.AddChild(msg);
        var msgText = new Label { Text = UiTextEn.Translate("Welcome to Eden Limitless!") };
        msgText.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        msgText.AddThemeColorOverride("font_color", new Color(0.12f, 0.11f, 0.1f));
        msgText.VerticalAlignment = VerticalAlignment.Center;
        msgText.OffsetLeft = 8;
        msg.AddChild(msgText);

        // --- boutons Confirm / Cancel ---
        var confirm = new Button { Text = UiTextEn.Translate("Confirm"), Name = "ConfirmBtn" };
        confirm.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        confirm.CustomMinimumSize = new Vector2(110, 30);
        confirm.Position = new Vector2(-232, -34);
        confirm.CustomMinimumSize = new Vector2(110, 34);
        PreGameStyle.Button(confirm, primary: true);
        confirm.Pressed += OnConfirm;
        win.AddChild(confirm);

        var cancel = new Button { Text = UiTextEn.Translate("Cancel"), Name = "CancelBtn" };
        cancel.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        cancel.CustomMinimumSize = new Vector2(110, 30);
        cancel.Position = new Vector2(-116, -34);
        cancel.CustomMinimumSize = new Vector2(110, 34);
        PreGameStyle.Button(cancel);
        cancel.Pressed += Mgr.BackToLogin;
        win.AddChild(cancel);
    }

    /// <summary>Boutons « Site officiel » / « Quitter » en bas à droite
    /// — présents dans la fenêtre native.</summary>
    private void BuildFooter()
    {
        var web = new Button { Text = UiTextEn.Translate("Official Website") };
        web.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        web.CustomMinimumSize = new Vector2(160, 30);
        web.Position = new Vector2(-176, -76);
        PreGameStyle.Button(web, fontSize: 13);
        web.Pressed += () => OS.ShellOpen(
            "https://www.x-legend.com/online/edeneternal/en/");
        AddChild(web);

        var quit = new Button { Text = UiTextEn.Translate("Quit") };
        quit.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        quit.CustomMinimumSize = new Vector2(160, 30);
        quit.Position = new Vector2(-176, -38);
        PreGameStyle.Button(quit, fontSize: 13);
        quit.Pressed += () => GetTree().Quit();
        AddChild(quit);
    }

    /// <summary>M13 : liste parchemin à texte sombre (ref
    /// Selection_channel) ; sélection grise.</summary>
    private static void StyleParchmentList(ItemList l)
    {
        l.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.96f, 0.945f, 0.91f),
            BorderColor = new Color(0.58f, 0.57f, 0.54f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        });
        var sel = new StyleBoxFlat { BgColor = new Color(0.72f, 0.71f, 0.68f) };
        l.AddThemeStyleboxOverride("selected", sel);
        l.AddThemeStyleboxOverride("selected_focus", sel);
        l.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        l.AddThemeColorOverride("font_color", new Color(0.1f, 0.1f, 0.1f));
        l.AddThemeColorOverride("font_selected_color", new Color(0.05f, 0.05f, 0.05f));
        l.AddThemeColorOverride("font_hovered_color", new Color(0.05f, 0.05f, 0.05f));
        l.AddThemeConstantOverride("v_separation", 4);
    }

    private static Label Header(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeColorOverride("font_color", PreGameStyle.Gold);
        l.AddThemeFontSizeOverride("font_size", 14);
        return l;
    }

    private static void StyleDarkButton(Button b)
    {
        b.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.18f, 0.18f, 0.17f),
            BorderColor = new Color(0.45f, 0.45f, 0.43f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
        b.AddThemeStyleboxOverride("hover", new StyleBoxFlat
        {
            BgColor = new Color(0.30f, 0.30f, 0.28f),
            BorderColor = new Color(0.62f, 0.62f, 0.60f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        });
        b.AddThemeColorOverride("font_color", Colors.White);
    }

    /// <summary>Remplit les deux listes depuis
    /// <see cref="ScreenManager.Worlds"/> : colonne Server = nom du
    /// monde (rouge = recommandé), colonne Ch. = « Ch01 » (icône d'état
    /// → suffixe « (Maintenance) » si le monde est down).</summary>
    private void Populate()
    {
        var worlds = Mgr.Worlds;
        if (worlds == null || _worldList == null || _channelList == null)
            return;
        // Puce gemme d'état — Emoji/gem du looknfeel natif : verte
        // = en ligne, rouge = maintenance (Selection_channel.png).
        var gem = Mgr.Tex?.GetRegion(
            new Ui.CeguiImageRef(UiTextEn.Translate("Emoji"), "gem"));
        var gemColor = (short s) => s == 0
            ? new Color(1f, 0.35f, 0.3f) : new Color(0.45f, 0.85f, 0.45f);
        foreach (var w in worlds.Worlds)
        {
            var state = w.State == 0 ? "(Maintenance)" : "";
            var chars = "0 chara";
            var rec = w.Id == worlds.LastWorldId ? "(Recommended)" : "";
            var idx = _worldList.AddItem(
                $"{w.Name}{rec} {chars}".Trim(), gem);
            _worldList.SetItemIconModulate(idx, gemColor(w.State));
            _worldList.SetItemMetadata(idx, w.Id);
            if (w.State == 0)
                _worldList.SetItemDisabled(idx, true);
            if (rec.Length > 0)
                _worldList.SetItemCustomFgColor(idx,
                    new Color(1f, 0.35f, 0.3f));
            var cidx = _channelList.AddItem($"{w.Name}{state}", gem);
            _channelList.SetItemIconModulate(cidx, gemColor(w.State));
            if (w.State == 0)
                _channelList.SetItemDisabled(cidx, true);
        }
        _worldList.ItemActivated += _ => OnConfirm();
        _channelList.ItemActivated += _ => OnConfirm();
        _worldList.ItemSelected += i =>
        {
            if (_channelList.ItemCount > i) _channelList.Select((int)i);
        };
        _channelList.ItemSelected += i =>
        {
            if (_worldList.ItemCount > i) _worldList.Select((int)i);
        };
        if (_worldList.ItemCount > 0)
        {
            // Préfère le dernier monde utilisé (LastWorldId natif).
            var pick = 0;
            for (var i = 0; i < _worldList.ItemCount; i++)
                if ((short)(int)_worldList.GetItemMetadata(i) ==
                    worlds.LastWorldId && !_worldList.IsItemDisabled(i))
                    pick = i;
            _worldList.Select(pick);
            _channelList.Select(pick);
        }

        _error = new Label { Visible = false };
        _error.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _error.OffsetTop = 220; _error.OffsetLeft = -250;
        _error.OffsetRight = 250;
        _error.HorizontalAlignment = HorizontalAlignment.Center;
        _error.AddThemeColorOverride("font_color",
            new Color(1f, 0.35f, 0.3f));
        AddChild(_error);
    }

    /// <summary>QA : sélectionne le premier monde et confirme
    /// (pilote automatique --channel).</summary>
    public void AutoConfirm()
    {
        if (_worldList != null && _worldList.ItemCount > 0 &&
            _worldList.GetSelectedItems().Length == 0)
            _worldList.Select(0);
        OnConfirm();
    }

    private async void OnConfirm()
    {
        if (_busy || _worldList == null || Mgr.Session == null) return;
        var sel = _worldList.GetSelectedItems();
        if (sel.Length == 0) return;
        _busy = true;
        var worldId = (short)(int)_worldList.GetItemMetadata(sel[0]);
        var s = Mgr.Session;
        EdenClient.Perf.PerfTrace.Abs("channel→chars : clic");
        try
        {
            var w = Mgr.Worlds?.Worlds.Find(x => x.Id == worldId);
            Mgr.WorldName = w?.Name ?? $"world {worldId}";
            // Si le monde était déjà joint, on repart du login.
            if (s.World != null)
            {
                var td = EdenClient.Perf.PerfTrace.Mark();
                await s.DisconnectWorldAsync();
                EdenClient.Perf.PerfTrace.Report("  disconnect world", td);
            }
            var t = EdenClient.Perf.PerfTrace.Mark();
            await s.SelectWorldAsync(worldId);
            EdenClient.Perf.PerfTrace.Report("  SelectWorld", t);
            t = EdenClient.Perf.PerfTrace.Mark();
            await s.WorldLoginAsync();
            EdenClient.Perf.PerfTrace.Report("  WorldLogin", t);
            t = EdenClient.Perf.PerfTrace.Mark();
            await s.ReceiveCharacterListAsync();
            EdenClient.Perf.PerfTrace.Report("  CharList", t);
            EdenClient.Perf.PerfTrace.Abs("channel→chars : liste reçue");
            // Pré-construction des corps listés pendant la transition
            // vers charsel + le temps de réflexion de l'utilisateur —
            // la liste n'existe qu'ici (pas au _Ready du channel).
            var apps = s.Characters.Select(c => c.Appearance)
                .Where(a => a is { Length: > 0 }).ToList();
            if (apps.Count > 0)
                _ = Task.Run(() =>
                {
                    foreach (var a in apps)
                        EdenClient.Game.CharBodyCache.Prewarm(a);
                });
            Mgr.ShowDeferred(new CharSelectScreen());
        }
        catch (Exception ex)
        {
            _busy = false;
            if (ex is EndOfStreamException or System.IO.IOException)
            {
                EdenClient.Session.EdenSession.LastDisconnectReason =
                    "Connection timed out — please sign in again.";
                Mgr.BackToLogin();
                return;
            }
            if (_error != null) { _error.Visible = true; _error.Text = ex.Message; }
            GD.PrintErr($"[channel] {ex}");
        }
    }
}
