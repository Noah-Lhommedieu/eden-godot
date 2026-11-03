using Godot;
using EdenClient.Session;
using EdenClient.Ui;

namespace EdenClient.Screens;

/// <summary>Passage de témoin entre les écrans (boot) et game.tscn —
/// la scène de jeu est chargée via ChangeSceneToFile, donc le transfert
/// se fait par champ statique.</summary>
public static class SessionCarry
{
    /// <summary>Session authentifiée au World (liste persos chargée) —
    /// null = mode solo.</summary>
    public static EdenSession? Session;
    /// <summary>Personnage choisi à l'écran de sélection.</summary>
    public static CharacterSummary? Character;
    /// <summary>Monde/channel choisi (nom affiché).</summary>
    public static string WorldName = "";
    /// <summary>.fsm pré-parsé par l'écran de chargement (peut être
    /// null — GameScene reparse alors normalement).</summary>
    public static EdenClient.World.FsmFile? PreparedFsm;
}

/// <summary>
/// Gestionnaire d'écrans — racine de <c>scenes/boot.tscn</c>
/// (Control plein écran). Enchaîne :
///   boot → login → channel → sélection perso → (création) → jeu.
///
/// Une seule <see cref="EdenSession"/> vit ici et suit le flux :
/// login (:6543) → monde (:5200) → jeu (zone :5201, dans game.tscn).
/// </summary>
public partial class ScreenManager : Control
{
    /// <summary>Contexte CEGUI partagé (scheme/looknfeel/imagesets).</summary>
    public CeguiContext? Ui { get; private set; }
    /// <summary>Cache textures atlas (PNG sous ui_textures/).</summary>
    public UiTextureCache? Tex { get; private set; }
    /// <summary>Config client (gamedata/client.cfg).</summary>
    public ClientConfig Config { get; private set; } = new();
    /// <summary>Session réseau courante (null hors flux).</summary>
    public EdenSession? Session { get; set; }
    /// <summary>Liste des mondes reçue au login.</summary>
    public Protocol.WorldList? Worlds { get; set; }
    /// <summary>Monde choisi à l'écran channel.</summary>
    public string WorldName { get; set; } = "";

    /// <summary>Répertoire uidata (layouts/imagesets natifs).</summary>
    public string UIDataDir { get; private set; } = "";

    /// <summary>true quand le chargement async du contexte CEGUI est
    /// terminé (avec ou sans succès — repli autorisé).</summary>
    public bool UiReady { get; private set; }
    /// <summary>QA override requested by --screen=; prevents BootScreen
    /// from racing the asynchronous fake-screen transition.</summary>
    public static bool ScreenOverrideRequested => ScreenArg() != null;

    public override void _Ready()
    {
        // VFS pack (Phase 0) — monté avant toute lecture de données
        // (client.cfg peut être packagé) ; no-op sans *.epk ni si
        // EDEN_VFS=0.
        Vfs.EdenVfs.Mount();
        Config = ClientConfig.Load();
        // L'écran de boot s'affiche immédiatement ; le contexte UI
        // (scheme + looknfeel + imagesets, ~1 s de parsing XML) est
        // chargé en tâche de fond pour ne pas bloquer le premier draw.
        Show(new BootScreen());
        EdenClient.Perf.PerfTrace.Abs("screens : boot affiché");
        LoadUiContextAsync(); // fire-and-forget (async void)
        // Préchauffe le proto S218_room10b (hall natif de création) —
        // le parse GLB part en tâche de fond pendant le boot.
        TempleBackdrop.Preload();
    }

    /// <summary>Chargement du contexte CEGUI hors thread principal.
    /// La suite (login ou --screen=…) repart sur le thread principal
    /// via la continuation du <c>await</c>.</summary>
    private async void LoadUiContextAsync()
    {
        // uidata : EDEN_DATA_DIR → <exe>/gamedata/uidata (package) →
        // raw_assets/uidata (dev) → res://gamedata/uidata.
        var uidata = DataPaths.FindSubdir("uidata") ?? "";
        UIDataDir = uidata;
        CeguiContext? ctx = null;
        if (uidata.Length > 0)
        {
            try
            {
                ctx = await Task.Run(() => CeguiContext.Load(uidata));
            }
            catch (Exception e)
            {
                GD.PrintErr($"[screens] échec chargement uidata : {e}");
            }
        }
        else
        {
            GD.PrintErr("[screens] uidata introuvable (racines : " +
                string.Join(", ", DataPaths.CandidateDataRoots()) +
                ") — les écrans utiliseront un rendu de repli.");
        }
        Ui = ctx;
        if (ctx != null)
        {
            Tex = new UiTextureCache(ctx.Imagesets);
            Tex.SetTheme(UiTextureCache.SavedTheme());
        }
        UiReady = true;
        GD.Print($"[screens] ui ready — uidata='{UIDataDir}' ctx={(Ui!=null)}");
        EdenClient.Perf.PerfTrace.Abs("screens : uidata prêt");

        var target = ScreenArg();
        if (target is { } t)
        {
            // Deferred : la continuation d'un await n'est pas garantie
            // sur le thread principal selon la config du runtime.
            GD.Print($"[screens] queue override {t}");
            ShowDeferred(FakeScreen(t));
        }
    }

    /// <summary>Construit un layout CEGUI (par nom de fichier) en
    /// arbre de Controls — null si uidata absent.</summary>
    public Control? BuildCeguiLayout(string layoutFile)
    {
        if (Ui == null || Tex == null) return null;
        var path = Path.Combine(UIDataDir, "layouts", layoutFile);
        if (!Vfs.EdenVfs.Exists(path)) return null;
        return CeguiToGodot.BuildLayoutFile(path, Ui, Tex);
    }

    /// <summary>Remplace l'écran courant. À appeler sur le thread
    /// principal (handler UI ou continuation Godot).</summary>
    public void Show(Control screen)
    {
        GD.Print($"[screens] show {screen.GetType().Name}");
        foreach (var c in GetChildren())
        {
            RemoveChild(c);
            c.QueueFree();
        }
        AddChild(screen);
    }

    /// <summary>Transition différée — à utiliser depuis un handler
    /// <c>async</c> (post-await réseau) : garantit l'exécution sur le
    /// thread principal même sans SynchronizationContext Godot.</summary>
    public void ShowDeferred(Control screen) =>
        CallDeferred(nameof(Show), screen);

    // --- raccourci QA : --screen=login|channel|chars|create ---------
    // Short-circuite le flux avec des données factices pour tester
    // chaque écran isolément (le contexte UI reste chargé en async).

    /// <summary>Valeur de l'argument <c>--screen=</c> (null si absent
    /// ou « boot »).</summary>
    private static string? ScreenArg()
    {
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--screen="))
            {
                var v = a.Split('=', 2)[1].Trim().ToLowerInvariant();
                if (v is "login" or "channel" or "chars" or "create"
                        or "loading")
                    return v;
                if (v != "boot")
                    GD.PrintErr($"[screens] --screen='{v}' inconnu " +
                        "(login|channel|chars|create|loading|boot)");
            }
        }
        return null;
    }

    /// <summary>Construit l'écran demandé par --screen= avec des
    /// données factices (session/worlds/persos de test).</summary>
    private Control FakeScreen(string target)
    {
        GD.Print($"[screens] --screen={target} — données factices");
        switch (target)
        {
            case "channel":
                Worlds = new Protocol.WorldList
                {
                    LastWorldId = 1010,
                };
                Worlds.Worlds.Add(new Protocol.WorldList.Entry
                {
                    Id = 1010, Name = "Ch01", Ip = "127.0.0.1",
                    Port = 5200, State = 1, MaxNum = 500,
                });
                Worlds.Worlds.Add(new Protocol.WorldList.Entry
                {
                    Id = 1020, Name = "Ch02", Ip = "127.0.0.1",
                    Port = 5200, State = 0, MaxNum = 500,
                });
                return new ChannelSelectScreen();
            case "chars":
                WorldName = "Ch01";
                var s = new EdenSession();
                Session = s;
                // app[28]>>4 = code corps (1=b, 0=g) — mêmes valeurs
                // que les vrais persos admin (bodyCode=1).
                var appB = new byte[32]; appB[28] = 0x10;
                var appG = new byte[32]; appG[28] = 0x00;
                s.Characters.Add(new CharacterSummary(
                    Id: 1001, Name: "TestHero", ClassId: 1, Level: 42,
                    NodeId: 2, RaceId: 1, ClassLv: 30, Appearance: appB));
                s.Characters.Add(new CharacterSummary(
                    Id: 1002, Name: "ZumiFan", ClassId: 3, Level: 7,
                    NodeId: 9, RaceId: 2, ClassLv: 5, Appearance: appG));
                return new CharSelectScreen();
            case "create":
                Session ??= new EdenSession();
                return new CharCreateScreen();
            case "loading":
                // Pas de session → RunAsync rebondira sur boot après
                // ~1,2 s, mais l'UI (artwork + barre native) est
                // construite dès _Ready — assez pour la capture QA.
                LoadingScreen.Report(0.55f, "Objets… 42/77");
                return new LoadingScreen();
            default:
                return new LoginScreen();
        }
    }

    /// <summary>Trouve un enfant par nom CEGUI sanitisé
    /// (« Root_Login_AccountEdit ») — recherche récursive.</summary>
    public static T? Find<T>(Control root, string leaf) where T : class
    {
        var n = root.FindChild($"*{leaf}*", true, false) as T;
        return n;
    }

    /// <summary>Positionne un texte sur un Control/Label existant
    /// (les labels CEGUI deviennent Label ou Panel+__text).</summary>
    public static void SetText(Control root, string leaf, string text)
    {
        var c = root.FindChild($"*{leaf}", true, false);
        switch (c)
        {
            case Label l: l.Text = text; break;
            case RichTextLabel r: r.Text = text; break;
            case Control p:
                var t = p.FindChild("__text", false, false) as Label;
                if (t != null) t.Text = text;
                break;
        }
    }

    // --- navigation --------------------------------------------------

    private int _shotFrames = -1;
    private bool _shotNoWait;   // --shot-early : pas d'attente temple
    private int _avatarWait;
    private string? _autoLogin;   // "user:pass" — null = absent
    private bool _autoLoginDone;
    private bool _autoChannel, _autoChannelDone;
    private bool _autoChar, _autoCharDone;
    private string? _autoCreate;
    private bool _autoCreateNav, _autoCreateDone, _autoBack;
    private bool _autoArgsParsed;
    /// <summary>Sélecteur du perso auto (« --char=test3 » ou
    /// « --char=2 ») — null = premier.</summary>
    private string? _autoCharSel;

    /// <summary>Lit les arguments du pilote automatique QA :
    /// <c>--login=user:pass</c> (remplit + soumet le login),
    /// <c>--channel</c> (confirme le 1er monde), <c>--char</c> ou
    /// <c>--char=nom|index</c> (entre en jeu avec le perso visé, le
    /// 1er par défaut). Chaînés, ils mènent jusqu'au spawn en
    /// zone.</summary>
    private void ParseAutoArgs()
    {
        _autoArgsParsed = true;
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--login="))
                _autoLogin = a.Split('=', 2)[1];
            else if (a == "--channel")
                _autoChannel = true;
            else if (a == "--char")
                _autoChar = true;
            else if (a.StartsWith("--char="))
            {
                _autoChar = true;
                _autoCharSel = a.Split('=', 2)[1];
            }
            else if (a.StartsWith("--create="))
                _autoCreate = a.Split('=', 2)[1];
            else if (a == "--create-back")
            {
                _autoCreate = "";
                _autoBack = true;
            }
        }
    }

    /// <summary>Avance le flux automatiquement quand l'écran attendu
    /// devient courant (une fois par étape).</summary>
    private void Autopilot()
    {
        if (GetChildCount() == 0) return;
        switch (GetChild(0))
        {
            case LoginScreen l when _autoLogin != null && !_autoLoginDone:
                _autoLoginDone = true;
                var parts = _autoLogin.Split(':', 2);
                GD.Print("[auto] login submit");
                l.FillAndSubmit(parts[0],
                    parts.Length > 1 ? parts[1] : "");
                break;
            case ChannelSelectScreen c when _autoChannel && !_autoChannelDone:
                _autoChannelDone = true;
                GD.Print("[auto] channel confirm");
                c.AutoConfirm();
                break;
            case CharSelectScreen s when _autoChar && !_autoCharDone:
                _autoCharDone = true;
                GD.Print("[auto] char enter");
                s.AutoEnter(_autoCharSel);
                break;
            case CharSelectScreen _ when _autoCreate != null &&
                    !_autoCreateNav:
                _autoCreateNav = true;
                GD.Print("[auto] nav create");
                Show(new CharCreateScreen());
                break;
            case CharCreateScreen c when _autoCreate != null &&
                    _autoCreateNav && !_autoCreateDone:
                _autoCreateDone = true;
                if (_autoBack)
                {
                    GD.Print("[auto] create back");
                    _ = c.Back();
                }
                else
                {
                    GD.Print($"[auto] create '{_autoCreate}'");
                    c.AutoCreate(_autoCreate);
                }
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (!_autoArgsParsed) ParseAutoArgs();
        Autopilot();

        // --shot=<path> : capture d'écran de debug après ~2s puis quit.
        // Avec --login (pilote automatique) le flux continue jusqu'à
        // la scène de jeu — le shot de GameScene (240 f) prend le
        // relais ; on allonge le garde-fou boot à 3600 f pour ne pas
        // couper un chargement lent (uidata/shaders ~10 s+).
        if (_shotFrames < 0)
        {
            foreach (var a in OS.GetCmdlineArgs()
                         .Concat(OS.GetCmdlineUserArgs()))
                if (a.StartsWith("--shot-early"))
                {
                    // Capture précoce (~2 s) sans attendre le temple —
                    // sert à valider le splash placeholder du login.
                    // ~1500 f ≈ 2 s réels au boot (frames à ~700 fps
                    // sur l'écran léger) — tombe dans la fenêtre
                    // login affiché / temple pas encore monté.
                    _shotFrames = 1500;
                    _shotNoWait = true;
                    var shotP = a.Contains('=')
                        ? a.Split('=', 2)[1]
                        : "/tmp/eden_screen_early.png";
                    _shotPath = System.IO.Path.IsPathRooted(shotP)
                        ? shotP : System.IO.Path.GetFullPath(shotP);
                }
                else if (a.StartsWith("--shot"))
                {
                    _shotFrames = _autoLogin != null ? 3600 : 600;
                    var shotP = a.Contains('=')
                        ? a.Split('=', 2)[1] : "/tmp/eden_screen.png";
                    // Relatif → cwd (res:// n'est pas écrivable).
                    _shotPath = System.IO.Path.IsPathRooted(shotP)
                        ? shotP : System.IO.Path.GetFullPath(shotP);
                }
        }
        if (_shotFrames > 0 && --_shotFrames == 0)
        {
            // Attendre d'être sorti du splash : la capture n'a de
            // sens que sur l'écran réellement affiché (login,
            // channel…) — sinon elle tombe sur le BootScreen.
            if (GetChildCount() > 0 && GetChild(0) is BootScreen)
            {
                _shotFrames = 60;
                return;
            }
            // Pilote auto : tant que le login/choix de perso n'a pas
            // abouti, la capture tomberait sur l'écran de connexion.
            if (_autoLogin != null && GetChildCount() > 0 &&
                _loginWait < 600 && GetChild(0) switch
                {
                    LoginScreen => true,
                    ChannelSelectScreen => OS.GetCmdlineUserArgs()
                        .Contains("--channel"),
                    CharSelectScreen => OS.GetCmdlineUserArgs()
                        .Contains("--char"),
                    _ => false,
                })
            {
                _loginWait++;
                _shotFrames = 60;
                return;
            }
            // Si l'écran utilise le temple, attendre son chargement
            // réel : le GLB se parse en tâche de fond et un délai
            // fixe capturait parfois un viewport vide.
            var usesTemple = GetChildCount() > 0 &&
                GetChild(0).GetNodeOrNull<Node3D>("TempleBackdrop") != null;
            var tb = usesTemple ? TempleBackdrop.Shared : null;
            if (!_shotNoWait && tb is { TempleAssetAvailable: true,
                    TempleLoaded: false })
            {
                _shotFrames = 30;
                return;
            }
            // L'écran de création construit l'avatar en tâche de fond
            // (~10 s à froid) — le laisser finir avant la capture
            // (borne ~30 s pour ne pas boucler sur un build raté).
            if ((GetChild(0) is CharCreateScreen { AvatarReady: false } ||
                 GetChild(0) is CharSelectScreen { AvatarReady: false }) &&
                _avatarWait < 30)
            {
                _avatarWait++;
                _shotFrames = 60;
                return;
            }
            GD.Print($"[shot] children={GetChildCount()} " +
                $"size={Size} cur={GetChild(0).GetType().Name} " +
                $"temple={(tb?.TempleLoaded == true)}");
            RenderingServer.ForceDraw();
            var img = GetViewport().GetTexture()?.GetImage();
            img?.SavePng(_shotPath);
            GD.Print($"[shot] {_shotPath}");
            GetTree().Quit();
        }
    }

    private int _loginWait;
    private string _shotPath = "/tmp/eden_screen.png";

    public void GoLogin() => Show(new LoginScreen());

    /// <summary>Retour login : ferme toutes les connexions.</summary>
    public async void BackToLogin()
    {
        if (Session != null)
            await Session.DisconnectAllAsync();
        Session = null;
        Worlds = null;
        ShowDeferred(new LoginScreen());
    }

    /// <summary>Retour à la sélection de channel : reconnecte le monde
    /// sur la connexion login conservée (le serveur tolère un nouveau
    /// SelectWorld — sinon on relogine complètement).</summary>
    public async void BackToChannelSelect()
    {
        if (Session == null || Worlds == null) { BackToLogin(); return; }
        ShowDeferred(new ChannelSelectScreen());
    }
}
