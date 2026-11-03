using Godot;

namespace EdenClient.Settings;

/// <summary>
/// Autoload unique du lot D (scenes/settings_applier.tscn,
/// <c>[autoload] SettingsApplier</c> dans project.godot) —
/// survit aux changements de scène (boot → jeu).
///
/// Rôle :
///   - charge <see cref="ClientSettings"/> (<c>gamedata/settings.cfg</c>)
///     et applique tout au démarrage : fenêtrage, VSync, limite FPS,
///     buses audio, bindings InputMap, scale UI/thème/police ;
///   - ré-applique graphismes + interface à chaque changement de
///     scène (les nœuds monde sont recréés — Sun, Environment,
///     Grass, caméra du joueur) ;
///   - dispatch les actions <c>eden_*</c> dans <see cref="_Input"/>
///     (le passage par <c>SetInputAsHandled</c> évite le double
///     déclenchement avec les touches codées en dur de HudRoot :
///     <c>_Input</c> tourne avant <c>_UnhandledInput</c>) ;
///   - héberge la fenêtre <see cref="SettingsWindow"/> dans un
///     CanvasLayer dédié (layer 100, au-dessus du HUD layer 10).
///
/// Args QA : <c>--settings-open</c> (ouvre la fenêtre au démarrage),
/// <c>--uiscale=&lt;f&gt;</c> (force le scale UI — captures).
/// </summary>
public partial class SettingsApplier : Node
{
    /// <summary>Singleton (posé dans _Ready).</summary>
    public static SettingsApplier? Instance { get; private set; }

    /// <summary>Paramètres courants (chargés au boot).</summary>
    public ClientSettings S { get; private set; } = new();

    private CanvasLayer? _layer;
    private SettingsWindow? _win;
    private Hud.HudRoot? _hud;
    private bool _hudReadyHooked;
    private bool _menuHooked;
    private Node? _scene;
    private int _applyIn;
    private int _qaTab = -1;
    /// <summary>Frames restantes pour poser le thème dès que le HUD et
    /// son atlas existent (le HUD se monte après le chargement des
    /// settings : sans attente le thème sauvegardé n'était appliqué
    /// qu'au premier changement de réglage).</summary>
    private int _themeWait;
    private const int ThemeWaitFrames = 1800;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;
        S = ClientSettings.Load();
        _layer = new CanvasLayer { Name = "SettingsLayer", Layer = 100 };
        AddChild(_layer);

        // Args QA avant le premier apply.
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
        {
            if (a == "--settings-open")
                CallDeferred(nameof(OpenSettings));
            else if (a.StartsWith("--settings-tab=") &&
                     int.TryParse(a[15..], out var tab))
                _qaTab = tab;
            else if (a.StartsWith("--uiscale=") &&
                     float.TryParse(a[10..],
                         System.Globalization.CultureInfo
                             .InvariantCulture, out var us))
                S.UiScale = us;
        }

        // Le ContentScaleSize doit suivre la taille RÉELLE de la
        // fenêtre : WindowSetSize est asynchrone — sans ce hook un
        // changement de résolution laisse un canvas périmé → bandes
        // noires sur les côtés (constat live). Expand = la fenêtre
        // plus grande montre plus de monde (MMO), jamais de barres.
        var win = GetViewport().GetWindow();
        if (win != null)
            win.SizeChanged += () =>
                ApplyUiScale(win, S.UiScale);

        if (OS.GetCmdlineUserArgs().Contains("--settings-reapply"))
            GetTree().CreateTimer(10.9).Timeout += () => { GD.Print("[settings] reapply QA"); ApplyAll(); };
        ApplyAll();
        GD.Print($"[settings] chargés — ui_scale={S.UiScale} " +
                 $"thème={S.Theme} preset={S.Preset}");
    }

    // ==================================================================
    // Cycle de vie & fenêtre

    /// <summary>Applique tout (display + audio + input + interface +
    /// graphismes) — appelé au boot et par le bouton Appliquer.
    /// Chaque section est isolée : une qui jette ne doit pas laisser
    /// les suivantes mortes (« tout marche pas » si l'exception
    /// frappait en début d'apply).</summary>
    public void ApplyAll()
    {
        void Section(string name, Action a)
        {
            try { a(); }
            catch (Exception ex)
            {
                GD.PrintErr($"[settings] {name} : {ex.Message}");
            }
        }
        Section("display", ApplyDisplay);
        Section("audio", ApplyAudio);
        Section("input", () => InputBindings.Apply(S));
        Section("interface", ApplyInterface);
        Section("graphics", ApplyGraphics);
        // EDENFR pur (eden-godot) : la base du corpus EST le français,
        // l'anglais est en pause — EDEN_LANG_EN=1 réactive la voie
        // historique (overlay lang/ + repli EN) pour comparaison.
        Section("language", () =>
        {
            Data.TextResolver.NativeOnly =
                System.Environment.GetEnvironmentVariable("EDEN_LANG_EN")
                    != "1";
            Data.TextResolver.SetLanguage(S.Language);
        });
    }

    /// <summary>Applique + sauvegarde (chaque changement de la
    /// fenêtre — persistance à chaud, D1).</summary>
    public void ApplyAndSave()
    {
        ApplyAll();
        var p = S.Save();
        if (p == null)
            GD.PrintErr("[settings] écriture settings.cfg impossible");
    }

    /// <summary>Remet les défauts complets puis applique + sauvegarde
    /// (bouton Réinitialiser).</summary>
    public void ResetToDefaults()
    {
        S = new ClientSettings();
        S.ResetBindings();
        ApplyAndSave();
    }

    /// <summary>Ouvre la fenêtre Paramètres (créée à la demande).</summary>
    public void OpenSettings()
    {
        EnsureWindow();
        if (_win == null) return;
        _win.RefreshAll();
        if (_qaTab >= 0) _win.SelectTab(_qaTab);
        Hud.HudWindows.Open(_win);
    }

    /// <summary>Bascule ouvert/fermé (action eden_system + boutons).</summary>
    public void ToggleSettings()
    {
        EnsureWindow();
        if (_win == null) return;
        if (_win.Visible) Hud.HudWindows.Close(_win);
        else OpenSettings();
    }

    /// <summary>Point d'entrée statique (boutons écrans).</summary>
    public static void Open() => Instance?.OpenSettings();
    /// <summary>Point d'entrée statique (toggle).</summary>
    public static void Toggle() => Instance?.ToggleSettings();

    private void EnsureWindow()
    {
        if (_layer == null) return;
        // Cadre natif dispo seulement avec le HUD : on reconstruit la
        // fenêtre quand on passe du login au jeu (ou l'inverse).
        var hasHud = Hud.HudExtensions.FindHud()?.Tex != null;
        if (_win != null && _win.BuiltWithHud != hasHud)
        {
            _win.QueueFree();
            _win = null;
        }
        if (_win != null) return;
        _win = new SettingsWindow();
        _layer.AddChild(_win);
    }

    // ==================================================================
    // Ré-application au changement de scène

    public override void _Process(double delta)
    {
        var cur = GetTree().CurrentScene;
        if (!ReferenceEquals(cur, _scene))
        {
            _scene = cur;
            _drvReg = null;
            _drvPlayer = null;
            _applyIn = 5;           // laisse la scène se monter
            _themeWait = ThemeWaitFrames;
            _hud = null;
            _hudReadyHooked = false;
            _menuHooked = false;
        }
        TickRender(delta);
        TickThemeBoot();
        if (_applyIn > 0 && --_applyIn == 0)
        {
            ApplyGraphics();
            ApplyInterface();
        }
    }

    // ==================================================================
    // Display (D1)

    private void ApplyDisplay()
    {
        DisplayServer.WindowSetMode(S.WindowMode switch
        {
            EdenWindowMode.Fullscreen =>
                DisplayServer.WindowMode.ExclusiveFullscreen,
            EdenWindowMode.Borderless =>
                DisplayServer.WindowMode.Fullscreen,
            _ => DisplayServer.WindowMode.Windowed,
        });
        if (S.WindowMode == EdenWindowMode.Windowed)
            DisplayServer.WindowSetSize(
                new Vector2I(S.ResolutionWidth, S.ResolutionHeight));
        DisplayServer.WindowSetVsyncMode(S.VSync
            ? DisplayServer.VSyncMode.Enabled
            : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = EffectiveMaxFps();
    }

    // ==================================================================
    // Graphismes (D1/D5) — à chaud sur les nœuds de la scène courante

    private void ApplyGraphics()
    {
        var root = (Node?)GetTree().CurrentScene ?? GetTree().Root;
        if (root == null) return;

        foreach (var l in root.FindChildren("*", "DirectionalLight3D",
                     true, false).Cast<DirectionalLight3D>())
        {
            l.ShadowEnabled = S.Shadows;
            // B1 : cascades réglées par preset (Low ≤ 1024 / High).
            var low = S.ShadowMapSize <= 1024;
            l.DirectionalShadowMode = low
                ? DirectionalLight3D.ShadowMode.Parallel2Splits
                : DirectionalLight3D.ShadowMode.Parallel4Splits;
            l.DirectionalShadowMaxDistance = low ? 90f : 220f;
            l.ShadowBlur = low ? 1.5f : 1f;
        }
        RenderingServer.DirectionalShadowAtlasSetSize(
            S.ShadowMapSize, true);

        foreach (var c in root.FindChildren("*", "Camera3D", true,
                     false).Cast<Camera3D>())
        {
            c.Far = S.ViewDistance;
            ApplyDof(c);
        }

        foreach (var we in root.FindChildren("*", "WorldEnvironment",
                     true, false).Cast<WorldEnvironment>())
        {
            if (we.HasMeta("pregame_env") ||
                we.HasMeta("fog_ceiling_env") ||
                we.Environment is not { } env) continue;
            env.FogEnabled = S.Fog;
            // Glow/Glare/Photo/Death : ApplyColorGrade (ApplyRender).
            env.SsaoEnabled = S.Ssao;
            env.SsilEnabled = S.Ssil;
        }

        // FogCeiling (quad shader natif FogMax) — nommé « FogCeiling »
        // par world/FogCeiling.cs (world/ non modifié : on ne fait
        // que basculer Visible par nom).
        foreach (var f in root.FindChildren("FogCeiling", "Node3D",
                     true, false).Cast<Node3D>())
            f.Visible = S.Fog;

        // Herbe : racine « Grass » construite par GrassRenderer.Build.
        foreach (var g in root.FindChildren("Grass", "Node3D",
                     true, false).Cast<Node3D>())
            g.Visible = S.Grass;

        foreach (var p in root.FindChildren("*", "GPUParticles3D",
                     true, false).Cast<GpuParticles3D>())
        {
            p.Visible = S.Particles;
            p.Emitting = S.Particles;
        }

        // Anti-aliasing du viewport racine (MSAA 3D ou FXAA écran).
        var vp = GetViewport();
        switch (S.Antialiasing)
        {
            case AntialiasMode.Fxaa:
                vp.Msaa3D = Viewport.Msaa.Disabled;
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                break;
            case AntialiasMode.Msaa2:
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                vp.Msaa3D = Viewport.Msaa.Msaa2X;
                break;
            case AntialiasMode.Msaa4:
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                vp.Msaa3D = Viewport.Msaa.Msaa4X;
                break;
            case AntialiasMode.Msaa8:
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                vp.Msaa3D = Viewport.Msaa.Msaa8X;
                break;
            default:
                vp.Msaa3D = Viewport.Msaa.Disabled;
                vp.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                break;
        }
        // Models / Textures / Water / grade couleur / limites d'entités.
        ApplyRender(root);
    }

    /// <summary>DOF best-effort via CameraAttributes (la map n'en
    /// pose pas — attributs créés seulement à l'activation).</summary>
    private void ApplyDof(Camera3D cam)
    {
        if (S.Dof)
        {
            if (cam.Attributes is not CameraAttributesPractical pa)
            {
                pa = new CameraAttributesPractical();
                cam.Attributes = pa;
            }
            pa.DofBlurFarEnabled = true;
            pa.DofBlurFarDistance = 120f;
            pa.DofBlurFarTransition = 60f;
        }
        else if (cam.Attributes is CameraAttributesPractical pa2)
        {
            pa2.DofBlurFarEnabled = false;
        }
    }

    // ==================================================================
    // Interface (D3) — scale global, thème, police

    private void ApplyInterface()
    {
        // Scale UI global : content-scale « canvas_items » du viewport
        // racine — la taille logique se contracte (les ancres des
        // fenêtres bord-à-bord restent à l'écran) puis le canvas est
        // re-scalé ×s ; le 3D n'est pas affecté.
        var hud = FindHud();
        GD.Print($"[theme-apply] ApplyInterface hud={(hud == null ? "null" : hud.GetPath().ToString())}");
        if (hud != null) hud.ApplyUiScale(S.UiScale);
        else ApplyUiScale(GetViewport().GetWindow(), S.UiScale);
        if (hud != null) ApplyTheme(hud, S.Theme);
        HookHud(hud);

        // Police globale : fallback ThemeDB (les overrides CEGUI
        // AddThemeFontSizeOverride gardent la priorité — best-effort).
        ThemeDB.FallbackFontSize = S.FontSize;
        ApplyFont(S.Font);
    }

    /// <summary>Scale UI sur un Window/viewport : content-scale
    /// canvas_items (0 = neutre à s≈1) avec aspect <b>Expand</b> —
    /// la fenêtre est toujours remplie, jamais de bandes noires (un
    /// viewport plus large montre plus de monde, les ancrages UI
    /// suivent). Méthode unique — HudRoot l'appelle via son hook
    /// <c>ApplyUiScale</c>.</summary>
    public static void ApplyUiScale(Window win, float s)
    {
        s = ClientSettings.ClampUiScale(s);
        win.ContentScaleMode = Window.ContentScaleModeEnum.CanvasItems;
        win.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
        if (Mathf.Abs(s - 1f) < 0.01f)
        {
            win.ContentScaleSize = Vector2I.Zero;
            return;
        }
        var sz = win.Size;
        win.ContentScaleSize = new Vector2I(
            Math.Max(1, (int)(sz.X / s)),
            Math.Max(1, (int)(sz.Y / s)));
    }

    /// <summary>Teinte des panneaux HUD selon le thème natif
    /// (raw_assets/themes/&lt;Theme&gt;/*.png existent mais ce sont
    /// des imagesets complets non découpés — repli « tint » prévu
    /// par le plan D3).</summary>
    public static Color ThemeTint(string theme) =>
        theme.ToLowerInvariant() switch
        {
            "blue" => new Color(0.82f, 0.88f, 1.00f),
            "coffee" => new Color(1.00f, 0.93f, 0.82f),
            "green" => new Color(0.85f, 1.00f, 0.87f),
            "purple" => new Color(0.94f, 0.88f, 1.00f),
            "red" => new Color(1.00f, 0.86f, 0.86f),
            _ => Colors.White,
        };

    /// <summary>Attend le HUD (nœud + atlas) puis pose le thème
    /// sauvegardé et branche HudReady : déterministe quel que soit
    /// l'ordre d'init autoload / scène.</summary>
    private void TickThemeBoot()
    {
        if (_themeWait <= 0) return;
        _themeWait--;
        var hud = FindHud();
        if (hud == null || Ui.UiThemeBoot.MustWait(true, hud.Tex != null))
            return;
        _themeWait = 0;
        ApplyTheme(hud, S.Theme);
        HookHud(hud);
    }

    private static void ApplyTheme(Hud.HudRoot hud, string theme)
    {
        // Atlas pas encore construit : ne rien faire (l'ancien repli
        // « teinte du thème » colorait tout le HUD en violet pour
        // Purple avant que l'atlas thémé n'existe). TickThemeBoot /
        // HudReady réessaient.
        if (hud.Tex == null) return;
        theme = Ui.UiThemeBoot.Normalize(theme);
        // Swap réel des atlas thémés (bordures/headers recolorés) ;
        // la teinte Modulate reste le repli quand aucun dossier de
        // thème n'est copié.
        var themed = hud.Tex?.SetTheme(theme) ?? false;
        GD.Print($"[theme-apply] hud.Tex={(hud.Tex == null ? "null" : "ok")} " +
                 $"theme={theme} themed={themed}");
        // M1 : les surfaces hand-built suivent le thème — palette
        // dérivée de l'atlas thémé, styleboxes mutées en place.
        Hud.EeSkin.ApplyThemePalette(themed ? hud.Tex : null);
        var tint = themed ? Colors.White : ThemeTint(theme);
        foreach (var child in hud.GetChildren())
            if (child is Control c)
                c.Modulate = tint;
    }

    /// <summary>Police packagée : res://ui_textures/&lt;font&gt;.ttf
    /// puis uidata/fonts/ des données natives.</summary>
    private void ApplyFont(string font)
    {
        if (font.Length == 0)
        {
            ThemeDB.FallbackFont = null;
            return;
        }
        var candidates = new List<string>();
        if (Vfs.EdenVfs.LooseAllowed)
            candidates.Add($"res://ui_textures/{font}.ttf");
        var uidataFont = DataPaths.ResolveFile(
            $"uidata/fonts/{font}.ttf");
        if (uidataFont != null) candidates.Add(uidataFont);
        var uidataFont2 = DataPaths.ResolveFile($"uidata/{font}.ttf");
        if (uidataFont2 != null) candidates.Add(uidataFont2);
        foreach (var path in candidates)
        {
            // Existence d'abord : LoadDynamicFont sur un chemin absent
            // émet un ERROR moteur par tentative (spam ×2 par scène
            // dans les exports). FileAccess.FileExists vérifie la
            // présence réelle dans le .pck — ResourceLoader.Exists
            // est vrai pour les fichiers remappés mais absents.
            // epk:// → Materialize extrait sous user://cache/epk/.
            var real = Vfs.EdenVfs.Materialize(path);
            if (real == null || !Godot.FileAccess.FileExists(real))
                continue;
            var f = new FontFile();
            if (f.LoadDynamicFont(real) == Error.Ok)
            {
                ThemeDB.FallbackFont = f;
                return;
            }
        }
        GD.PrintErr($"[settings] police «{font}» introuvable");
    }

    // ==================================================================
    // Audio (D4) — buses créés si absents

    private void ApplyAudio()
    {
        EnsureBus("Music");
        EnsureBus("Effects");
        SetVolume("Master", S.MasterVolume);
        SetVolume("Music", S.MusicVolume);
        SetVolume("Effects", S.EffectsVolume);
        // Case « Mute » : coupe le bus Master (écrase le mute de volume 0).
        var m = AudioServer.GetBusIndex("Master");
        if (m >= 0 && S.Mute) AudioServer.SetBusMute(m, true);
    }

    private static void EnsureBus(string name)
    {
        if (AudioServer.GetBusIndex(name) >= 0) return;
        var idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, name);
    }

    private static void SetVolume(string bus, float linear)
    {
        var idx = AudioServer.GetBusIndex(bus);
        if (idx < 0) return;
        AudioServer.SetBusVolumeDb(idx,
            Mathf.LinearToDb(Mathf.Max(linear, 0.0001f)));
        AudioServer.SetBusMute(idx, linear <= 0.001f);
    }

    // ==================================================================
    // Dispatch clavier (D2) — actions eden_*

    public override void _Input(InputEvent e)
    {
        // 1. Capture de remap en cours (hors de la fenêtre — les
        //    événements dans la fenêtre passent par son propre
        //    viewport, cf. SettingsWindow._Input).
        if (_win is { Visible: true } && _win.TryCaptureRemap(e))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (e is InputEventKey { Echo: true }) return;
        if (e is not (InputEventKey or InputEventMouseButton)) return;
        // Saisie texte (chat, champs login…) : ne rien intercepter —
        // la saisie passe, le déplacement est déjà suspendu par
        // GameScene via ChatHasFocus.
        if (GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit)
            return;

        foreach (var (action, _) in S.Bindings)
        {
            if (!action.StartsWith("eden_",
                    StringComparison.Ordinal)) continue;
            if (!e.IsActionPressed(action)) continue;
            // Fenêtre ouverte : on avale les touches eden_* (elles
            // n'atteignent plus le HUD) et seul eden_system agit
            // (fermeture).
            if (_win is { Visible: true })
            {
                GetViewport().SetInputAsHandled();
                if (action == "eden_system")
                    ToggleSettings();
                return;
            }
            if (Dispatch(action))
                GetViewport().SetInputAsHandled();
            return;
        }
    }

    /// <summary>Exécute une action eden_* — true si l'événement doit
    /// être consommé (SetInputAsHandled).</summary>
    private bool Dispatch(string action)
    {
        switch (action)
        {
            case "eden_system":
                // Échap (plan v4 H4) : ferme la fenêtre au premier
                // plan, sinon ouvre/ferme le Main Menu (SystemPanel).
                var hud0 = FindHud();
                if (hud0 != null) hud0.HandleEscape();
                else ToggleSettings();
                return true;
            case "eden_inventory":
                return WithHud(h => h.ToggleInventory());
            case "eden_profile":
                return WithHud(h => h.ToggleProfile());
            case "eden_skills":
                return WithHud(h => h.ToggleSkills());
            case "eden_quests":
                return WithHud(h => h.ToggleQuests());
            case "eden_map":
                return WithHud(h => h.ToggleWorldMap());
            case "eden_pet":
                return WithHud(h => h.ToggleElf());
            case "eden_achievements":
                return WithHud(h => h.ToggleAchievements());
            case "eden_social":
                {
                    var sw = GetTree().Root.FindChild("SocialWnd",
                        true, false) as Hud.SocialWindow;
                    if (sw == null) return false;
                    sw.TogglePanel();
                    return true;
                }
            case "eden_chat":
                return WithHud(h =>
                {
                    if (!h.ChatHasFocus) h.Chat?.FocusInput();
                });
            case "eden_sit":
                return WithPlayer(p => p.ToggleSit());
            case "eden_weapon":
                return WithPlayer(p => p.ToggleWeapons());
            case "eden_attack":
                return WithPlayer(p => p.AttackTargetOrNearest());
            case var pm when pm.StartsWith("eden_party_",
                    StringComparison.Ordinal) &&
                    int.TryParse(pm[11..], out var slot):
                return WithPlayer(p => p.TargetPartyMember(slot - 1));
            case "eden_cam_zoom_in":
                return CamZoom(-1.5f);
            case "eden_cam_zoom_out":
                return CamZoom(1.5f);
            // eden_jump : le saut est sondé par PlayerController
            // (Input.IsActionJustPressed en _PhysicsProcess) — ce
            // dispatch d'actions fenêtre/HUD ne le consomme pas ;
            // le binding InputMap est posé par InputBindings.Apply.
            default:
                return false;
        }
    }

    /// <summary>Exécute <paramref name="act"/> sur le HUD s'il est
    /// trouvé (découverte paresseuse — le HUD est monté après la
    /// scène de jeu).</summary>
    private bool WithHud(Action<Hud.HudRoot> act)
    {
        var hud = FindHud();
        if (hud == null) return false;
        act(hud);
        return true;
    }

    /// <summary>Exécute <paramref name="act"/> sur le PlayerController
    /// de la scène de jeu — true seulement si l'action a eu un effet
    /// (sinon la touche reste disponible pour les autres handlers).</summary>
    private bool WithPlayer(Func<Player.PlayerController, bool> act)
    {
        FindDriven();
        return _drvPlayer != null && act(_drvPlayer);
    }

    /// <summary>Zoom caméra par touches (SpringArm du joueur —
    /// découverte par nom, player/ non modifié).</summary>
    private bool CamZoom(float delta)
    {
        var root = GetTree().CurrentScene;
        var spring = root?.FindChild("SpringArm", true, false)
            as SpringArm3D;
        if (spring == null) return false;
        spring.SpringLength = Mathf.Clamp(
            spring.SpringLength + delta, 3f, 18f);
        return true;
    }

    // ==================================================================
    // Branchement HUD — SysBtn « system » du menu bas-droite

    private Hud.HudRoot? FindHud()
    {
        if (_hud != null && GodotObject.IsInstanceValid(_hud))
            return _hud;
        _hud = Hud.HudExtensions.FindHud();
        return _hud;
    }

    /// <summary>S'abonne (une fois) à HudReady/MenuAction : le SysBtn
    /// du FunctionMenu natif ouvre les paramètres. hud/ n'est pas
    /// modifié — l'événement public est souscrit de l'extérieur.</summary>
    private void HookHud(Hud.HudRoot? hud)
    {
        if (hud == null) return;
        if (!_hudReadyHooked)
        {
            _hudReadyHooked = true;
            hud.HudReady += OnHudReady;
        }
        TryHookMenu(hud);
    }

    private void OnHudReady()
    {
        var hud = FindHud();
        if (hud == null) return;
        hud.ApplyUiScale(S.UiScale);
        ApplyTheme(hud, S.Theme);
        TryHookMenu(hud);
    }

    private void TryHookMenu(Hud.HudRoot hud)
    {
        if (_menuHooked || hud.MenuIcons == null) return;
        _menuHooked = true;
        hud.MenuIcons.MenuAction += action =>
        {
            if (action == "system")
                ToggleSettings();
        };
    }
}
