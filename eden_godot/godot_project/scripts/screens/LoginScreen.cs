using Godot;
using EdenClient.Data;
using EdenClient.Hud;
using EdenClient.Session;

namespace EdenClient.Screens;

/// <summary>
/// Écran de connexion. Fond = image FIXE (<c>gamedata/login_bg.jpg</c>,
/// capture du hall S218 : ne change jamais, aucun GLB à parser) +
/// vignette ; formulaire verre sombre à liseré doré (composition de
/// l'ancien Eden : titre, Compte, Mot de passe, Confirmer / Annuler,
/// serveur + boutons bas-gauche, version bas-droite). Noms de nœuds
/// historiques conservés (AccountEdit/PasswordEdit/StartButton/
/// WarentText/VersionText/ExitGameButton) pour
/// <see cref="ScreenManager.SetText"/> et le pilote --login=.
/// Pendant la saisie, le temple 3D et les corps de la dernière
/// session sont préchauffés (<see cref="PreGameWarmup"/>).
/// </summary>
public partial class LoginScreen : Control
{
    private ScreenManager Mgr => (ScreenManager)GetParent();
    private LineEdit? _account;
    private LineEdit? _password;
    private LineEdit? _server;
    private Label? _error;
    private bool _busy;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        EdenClient.Perf.PerfTrace.Abs("login : _Ready");
        var forceTemple = OS.GetCmdlineUserArgs().Concat(OS.GetCmdlineArgs())
            .Contains("--login-temple");
        if (forceTemple)
            TempleBackdrop.Attach(this, TempleBackdrop.Shot.Select);   // QA : génère login_bg.jpg
        else
            BuildBackground();
        if (!forceTemple)
        {
            BuildPanel();
            BuildFooter();
            BuildSettingsButton();
            _account?.GrabFocus();
        }
        // Préchauffage une fois l'écran affiché (jamais avant la 1re frame).
        Callable.From(() => PreGameWarmup.Start(Mgr.Config.Account))
            .CallDeferred();
    }

    public override void _Process(double delta)
    {
        // QA --login-temple : pas de splash (capture propre du hall).
        if (GetNodeOrNull("TemplePlaceholder") is { } ph) ph.QueueFree();
    }

    /// <summary>Image fixe plein écran (couvre sans déformer) +
    /// vignette. Repli : dégradé sombre si l'image est absente.</summary>
    private void BuildBackground()
    {
        Texture2D? tex = null;
        var path = DataPaths.ResolveFile("login_bg.jpg");
        if (path != null)
        {
            var img = Vfs.EdenVfs.LoadImage(path);
            if (img != null && !img.IsEmpty())
                tex = ImageTexture.CreateFromImage(img);
        }
        if (tex != null)
        {
            var bg = new TextureRect
            {
                Name = "LoginBackground",
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(bg);
        }
        else
        {
            var flat = new ColorRect { Color = new Color(0.07f, 0.07f, 0.1f),
                MouseFilter = MouseFilterEnum.Ignore };
            flat.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(flat);
        }
        AddChild(PreGameStyle.Vignette());
    }

    /// <summary>Engrenage « Paramètres » haut-droite.</summary>
    private void BuildSettingsButton()
    {
        var gear = new Button
        {
            Text = "⚙",
            TooltipText = UiTextEn.Translate("Settings"),
            CustomMinimumSize = new Vector2(38, 32),
        };
        PreGameStyle.Button(gear, fontSize: 16);
        gear.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        gear.Position = new Vector2(-48, 10);
        gear.Pressed += () => Settings.SettingsApplier.Open();
        AddChild(gear);
    }

    /// <summary>Cadre centré : titre + formulaire + boutons.</summary>
    private void BuildPanel()
    {
        var panel = new PanelContainer { Name = "LoginPanel" };
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        panel.CustomMinimumSize = new Vector2(400, 0);
        panel.GrowHorizontal = GrowDirection.Both;
        panel.GrowVertical = GrowDirection.Both;
        panel.AddThemeStyleboxOverride("panel", PreGameStyle.Panel(0.84f, 28));
        AddChild(panel);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);

        var title = PreGameStyle.Text(UiTextEn.Translate("EDEN LIMITLESS"),
            34, PreGameStyle.Gold, HorizontalAlignment.Center);
        box.AddChild(title);
        box.AddChild(PreGameStyle.Text(UiTextEn.Translate("Server Login"),
            14, PreGameStyle.Dim, HorizontalAlignment.Center));
        box.AddChild(PreGameStyle.Rule());
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });

        box.AddChild(FieldLabel(UiTextEn.Translate("Account")));
        _account = new LineEdit
        {
            Name = "Root_Login_AccountEdit",
            PlaceholderText = UiTextEn.Translate("Account name"),
            MaxLength = 20,
            CustomMinimumSize = new Vector2(0, 38),
        };
        PreGameStyle.Field(_account);
        box.AddChild(_account);

        box.AddChild(FieldLabel(UiTextEn.Translate("Password")));
        _password = new LineEdit
        {
            Name = "Root_Login_PasswordEdit",
            PlaceholderText = UiTextEn.Translate("Password"),
            Secret = true,
            MaxLength = 20,
            CustomMinimumSize = new Vector2(0, 38),
        };
        PreGameStyle.Field(_password);
        box.AddChild(_password);

        _error = new Label
        {
            Name = "Root_Login_WarentText",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 24),
        };
        _error.AddThemeColorOverride("font_color", PreGameStyle.Err);
        box.AddChild(_error);

        var start = new Button
        {
            Name = "Root_Login_StartButton",
            Text = UiTextEn.Translate("Confirm"),
            CustomMinimumSize = new Vector2(0, 42),
        };
        PreGameStyle.Button(start, primary: true, fontSize: 16);
        start.Pressed += OnStart;
        box.AddChild(start);

        var exit = new Button
        {
            Name = "Root_Login_ExitGameButton",
            Text = UiTextEn.Translate("Cancel"),
            CustomMinimumSize = new Vector2(0, 34),
        };
        PreGameStyle.Button(exit);
        exit.Pressed += () => GetTree().Quit();
        box.AddChild(exit);

        var acc = Mgr.Config.Account;
        if (acc.Length > 0) _account.Text = acc;
        if (EdenSession.LastDisconnectReason is { Length: > 0 } reason)
        {
            _error.Text = reason;
            EdenSession.LastDisconnectReason = null;
        }
    }

    /// <summary>Bande basse : serveur éditable + Official Website /
    /// Quit à gauche, disclaimer centré, version à droite.</summary>
    private void BuildFooter()
    {
        var bar = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        bar.OffsetTop = -52;
        AddChild(bar);

        var row = new HBoxContainer { Name = "FooterRow" };
        row.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        row.OffsetTop = -44; row.OffsetBottom = -8;
        row.OffsetLeft = 14; row.OffsetRight = -14;
        row.AddThemeConstantOverride("separation", 8);
        AddChild(row);

        var serverLbl = PreGameStyle.Text(UiTextEn.Translate("Server"),
            13, PreGameStyle.Dim);
        serverLbl.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(serverLbl);

        _server = new LineEdit
        {
            Name = "ServerEdit",
            Text = $"{Mgr.Config.Host}:{Mgr.Config.LoginPort}",
            PlaceholderText = "host:port",
            CustomMinimumSize = new Vector2(180, 30),
        };
        PreGameStyle.Field(_server);
        _server.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(_server);

        var web = new Button
        {
            Name = "OfficialWebSiteButton",
            Text = UiTextEn.Translate("Official Website"),
            CustomMinimumSize = new Vector2(130, 30),
        };
        PreGameStyle.Button(web, fontSize: 13);
        web.Pressed += () => OS.ShellOpen(
            "https://www.x-legend.com/online/edeneternal/en/");
        row.AddChild(web);

        var quit = new Button
        {
            Name = "FooterQuitButton",
            Text = UiTextEn.Translate("Quit"),
            CustomMinimumSize = new Vector2(80, 30),
        };
        PreGameStyle.Button(quit, fontSize: 13);
        quit.Pressed += () => GetTree().Quit();
        row.AddChild(quit);

        var disclaimer = PreGameStyle.Text(
            UiTextEn.Translate("Eden Eternal © X-Legend Entertainment Corp. — ") +
            UiTextEn.Translate("Eden Limitless is an unofficial fan client."),
            11, new Color(0.62f, 0.64f, 0.72f), HorizontalAlignment.Center);
        disclaimer.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        disclaimer.VerticalAlignment = VerticalAlignment.Center;
        disclaimer.ClipText = true;
        row.AddChild(disclaimer);

        var ver = PreGameStyle.Text(
            UiTextEn.Translate($"Ver:{Mgr.Config.ClientVersion}"),
            13, new Color(0.7f, 0.72f, 0.8f));
        ver.Name = "Root_Login_VersionText";
        ver.VerticalAlignment = VerticalAlignment.Center;
        row.AddChild(ver);
    }

    private static Label FieldLabel(string text) =>
        PreGameStyle.Text(text, 13, PreGameStyle.Dim);

    /// <summary>QA : remplit les champs et soumet le formulaire
    /// (utilisé par le pilote automatique --login=user:pass).</summary>
    public void FillAndSubmit(string account, string password)
    {
        if (_account != null) _account.Text = account;
        if (_password != null) _password.Text = password;
        OnStart();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey k && k.Pressed &&
            k.Keycode is Key.Enter or Key.KpEnter)
            OnStart();
    }

    private void SetError(string msg)
    {
        if (_error != null)
        {
            _error.Text = msg;
            _error.AddThemeColorOverride("font_color",
                new Color(1f, 0.35f, 0.3f));
        }
        if (msg.Length > 0) GD.PrintErr($"[login] {msg}");
    }

    private async void OnStart()
    {
        if (_busy) return;
        _busy = true;
        SetError("");
        var acc = _account?.Text.Trim() ?? "";
        var pwd = _password?.Text ?? "";
        if (acc.Length > 0) Mgr.Config.Account = acc;   // clé du préchauffage
        if (acc.Length == 0 || pwd.Length == 0)
        {
            SetError(UiTextEn.Translate("Account and password required."));
            _busy = false;
            return;
        }
        var (host, port) = (Mgr.Config.Host, Mgr.Config.LoginPort);
        if (_server != null && _server.Text.Contains(':'))
        {
            var parts = _server.Text.Split(':');
            host = parts[0].Trim();
            if (int.TryParse(parts[1], out var p)) port = p;
        }
        else if (_server != null && _server.Text.Trim().Length > 0)
            host = _server.Text.Trim();

        _status(UiTextEn.Translate("Connecting…"));
        try
        {
            var opt = new EdenSessionOptions
            {
                Host = host,
                LoginPort = port,
                TicketPort = Mgr.Config.TicketPort,
                WorldPort = Mgr.Config.WorldPort,
                ZonePort = Mgr.Config.ZonePort,
                Account = acc,
                Password = pwd,
                ClientVersion = Mgr.Config.ClientVersion,
                MachineId = Mgr.Config.MachineId,
                Transport = Mgr.Config.Transport,
                TlsThumbprint = Mgr.Config.TlsThumbprint,
                TlsInsecure = Mgr.Config.TlsInsecure,
            };
            var s = new EdenSession(opt);
            Mgr.Session = s;
            await s.ConnectGatewayAsync();
            var worlds = await s.LoginAsync();
            Mgr.Worlds = worlds;
            // Transition différée : garantit le retour sur le thread
            // principal même si le sync context Godot est absent.
            Mgr.ShowDeferred(new ChannelSelectScreen());
        }
        catch (Exception ex)
        {
            _busy = false;
            Mgr.Session = null;
            SetError(FriendlyError(ex));
        }
    }

    private void _status(string t)
    {
        if (_error != null)
        {
            _error.Text = t;
            _error.AddThemeColorOverride("font_color",
                new Color(0.9f, 0.9f, 0.6f));
        }
    }

    /// <summary>Codes LoginFail natifs (Eden.Login/LoginPacketHandlers :
    /// 2 auth, 6 monde down, 9 plein, 10 générique).</summary>
    private static string FriendlyError(Exception ex) => ex switch
    {
        System.Net.Sockets.SocketException =>
            $"{UiTextEn.Translate("Server unreachable")} ({ex.Message})",
        TimeoutException => $"{UiTextEn.Translate("Timed out")} — {ex.Message}",
        InvalidOperationException i when
            i.Message.Contains("code 2") => UiTextEn.Translate("Wrong account or password."),
        InvalidOperationException i when
            i.Message.Contains("code 6") => UiTextEn.Translate("World unavailable."),
        InvalidOperationException i when
            i.Message.Contains("code 9") => UiTextEn.Translate("World full."),
        _ => ex.Message,
    };

}
