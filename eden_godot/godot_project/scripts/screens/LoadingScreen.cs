using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Screens;

/// <summary>
/// Écran de chargement (<c>scenes/loading.tscn</c>) — style natif :
/// artwork <c>UI/loadingframe/Loading_&lt;nodeId&gt;.png</c>
/// (rf_game charge <c>Loading_%hd.dds</c> où %hd = nodeId du
/// EnterNodeNodeData — FUN_00684310), nom+description de zone en
/// haut-gauche/bas-gauche, tip en haut à droite, barre de progression
/// réelle en bas à droite.
///
/// Phases réelles :
///   1. <b>ticket</b>      SelectCharacter + GetZoneTicket (réseau)
///   2. <b>connexion</b>   ZoneConnect = handshake + UseTicket (réseau)
///   3. <b>données</b>     parse .fsm + scène ini en tâche de fond
///   4. <b>objets</b>      pré-charge les GLB dans le cache assets
///   5. <b>entrée</b>      l'overlay migre sur la racine du viewport
///                         (survit à ChangeSceneToFile) puis GameScene
///                         rapporte monde/entités → fondu de sortie.
///
/// GameScene parle via <see cref="Report"/>/<see cref="Finish"/> —
/// null-safe quand l'écran n'a pas été monté (mode solo).
/// </summary>
public partial class LoadingScreen : Control
{
    /// <summary>Instance active (pour <see cref="Report"/>/<see cref="Finish"/>).</summary>
    public static LoadingScreen? Active { get; private set; }

    /// <summary>Rapports de progression postés depuis les tâches de
    /// fond (chargement map hors thread) — drainés dans
    /// <see cref="_Process"/> (les Labels ne se touchent que sur le
    /// thread principal).</summary>
    private static readonly System.Collections.Concurrent
        .ConcurrentQueue<(float Frac, string Label)> _reports = new();

    private Control? _barClip;
    private TextureRect? _mascot;
    private TextureRect? _human;
    private AtlasTexture[] _mascotFrames = [];
    private Label? _phase;
    private Label? _tip;
    private TextureRect? _art;
    private float _target;
    private float _shown;
    private double _fadeT = -1;
    private bool _done;
    private bool _overlayOnly;
    private int _overrideNodeId;

    /// <summary>Largeur logique de la barre native (textures
    /// <c>loadingframe00/01</c> = 600×150).</summary>
    private const float BarW = 600f;

    // --- phases --------------------------------------------------------

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Active = this;
        Ui.NativeCursor.Apply();
        // H6 : sablier natif pendant le chargement (cellule 11 de
        // SystemCursors — reset à la flèche à la sortie de l'écran).
        GameInput.EdenCursors.Set(GameInput.EdenCursors.Mode.Busy);
        BuildUi();
        if (!_overlayOnly)
            _ = RunAsync();
    }

    /// <summary>Monte ou réarme un overlay de chargement pour une
    /// migration de map en jeu (P0.1/P1.4).</summary>
    public static LoadingScreen EnsureOverlay(SceneTree tree, int nodeId)
    {
        if (Active != null && GodotObject.IsInstanceValid(Active))
        {
            Active._done = false;
            Active._fadeT = -1;
            Active.Modulate = Colors.White;
            Active._target = 0.05f;
            Active._shown = 0f;
            return Active;
        }
        var screen = new LoadingScreen
        {
            _overlayOnly = true,
            _overrideNodeId = nodeId,
        };
        var layer = new CanvasLayer { Name = "LoadingOverlay", Layer = 64 };
        tree.Root.AddChild(layer);
        layer.AddChild(screen);
        return screen;
    }

    private void BuildUi()
    {
        var nodeId = _overrideNodeId > 0
            ? _overrideNodeId
            : (SessionCarry.Character?.NodeId ?? 0);

        _art = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            // Stretch plein-écran comme le client natif : les artworks
            // 1024² ont les bandes dorées cuites dedans — un mode
            // « covered » les rognerait.
            StretchMode = TextureRect.StretchModeEnum.Scale,
        };
        _art.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // nodeId 0 (pas de zone) ou art manquant → artwork neutre :
        // les Loading_0* sont le splash « EDEN REFORGED » de la
        // distribution source — mauvaise marque pour Eden Limitless.
        var tex = (nodeId > 0
            ? AssetCatalog.GetTexture(
                $"UI/loadingframe/Loading_{nodeId}.png")
            : null)
        ?? AssetCatalog.GetTexture("UI/loadingframe/loading_36.png");
        _art.Texture = tex;
        AddChild(_art);
        // Fond noir derrière l'artwork (letterbox).
        var black = new ColorRect { Color = Colors.Black };
        black.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        black.ShowBehindParent = true;
        _art.AddChild(black);

        var (name, sub, desc) = NodeText(nodeId);
        var title = new Label { Text = name };
        // A5.7 : positions mesurées sur le ref-shot (contenu 1280×720,
        // fenêtre à (65,81) dans le PNG) — titre (8,66), sous-titre
        // (8,103), lore bas-gauche (16,477) 490 px de large, tip haut
        // droite y=14 collé au bord, barre collée coin bas-droit
        // (bbox opaque 11..594 de la texture 600×150 = rel. 692..1270).
        // Ancres fractionnaires : tient à toute taille de viewport.
        AnchorAt(title, 8, 66);
        title.AddThemeFontSizeOverride("font_size", 36);
        title.AddThemeColorOverride("font_color", Colors.White);
        title.AddThemeColorOverride("font_shadow_color", Colors.Black);
        title.AddThemeConstantOverride("shadow_offset_x", 2);
        title.AddThemeConstantOverride("shadow_offset_y", 2);
        AddChild(title);
        var subL = new Label { Text = sub };
        AnchorAt(subL, 8, 103);
        subL.AddThemeFontSizeOverride("font_size", 16);
        subL.AddThemeColorOverride("font_color",
            new Color(0.92f, 0.92f, 0.95f));
        subL.AddThemeColorOverride("font_shadow_color", Colors.Black);
        subL.AddThemeConstantOverride("shadow_offset_x", 1);
        subL.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(subL);

        _tip = new Label
        {
            Text = UiTextEn.Translate("Tip:  Obtain Fame to purchase exclusive items ") +
                   UiTextEn.Translate("at Fame Exchanges."),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        AnchorAt(_tip, 1280 - 6, 14);
        _tip.GrowHorizontal = GrowDirection.Begin;
        _tip.AddThemeFontSizeOverride("font_size", 13);
        _tip.AddThemeColorOverride("font_color",
            new Color(0.95f, 0.92f, 0.85f));
        _tip.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _tip.AddThemeConstantOverride("shadow_offset_x", 1);
        _tip.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(_tip);

        if (desc.Length > 0)
        {
            var dl = new Label
            {
                Text = desc,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(490, 0),
            };
            AnchorAt(dl, 16, 477);
            dl.AddThemeFontSizeOverride("font_size", 12);
            dl.AddThemeConstantOverride("line_spacing", -2);
            dl.AddThemeColorOverride("font_color", Colors.White);
            dl.AddThemeColorOverride("font_shadow_color", Colors.Black);
            dl.AddThemeConstantOverride("shadow_offset_x", 1);
            dl.AddThemeConstantOverride("shadow_offset_y", 1);
            AddChild(dl);
        }

        _phase = new Label { Text = UiTextEn.Translate("Connecting…") };
        _phase.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        _phase.Position = new Vector2(-620, -168);
        _phase.CustomMinimumSize = new Vector2(600, 0);
        _phase.HorizontalAlignment = HorizontalAlignment.Right;
        _phase.AddThemeFontSizeOverride("font_size", 14);
        _phase.AddThemeColorOverride("font_color",
            new Color(0.92f, 0.88f, 0.78f));
        _phase.AddThemeColorOverride("font_shadow_color", Colors.Black);
        _phase.AddThemeConstantOverride("shadow_offset_x", 1);
        _phase.AddThemeConstantOverride("shadow_offset_y", 1);
        AddChild(_phase);

        // Barre native : fill rose (loadingframe01) rognée par la
        // progression sous le cadre doré (loadingframe00), mascotte
        // alpaga (animation.png, 5 frames) qui marche au bout du
        // remplissage — comme l'écran de chargement natif.
        var fillTex = AssetCatalog.GetTexture(
            "UI/loadingframe/loadingframe01.png");
        var frameTex = AssetCatalog.GetTexture(
            "UI/loadingframe/loadingframe00.png");
        var animTex = AssetCatalog.GetTexture(
            "UI/loadingframe/animation.png");
        var bar = new Control
        {
            CustomMinimumSize = new Vector2(BarW, 150),
        };
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        bar.Position = new Vector2(-BarW, -150);
        AddChild(bar);
        _barClip = new Control
        {
            ClipContents = true,
            Size = new Vector2(0, 150),
        };
        bar.AddChild(_barClip);
        if (fillTex != null)
        {
            var fill = new TextureRect
            {
                Texture = fillTex,
                Size = new Vector2(BarW, 150),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            };
            _barClip.AddChild(fill);
        }
        if (frameTex != null)
        {
            var frame = new TextureRect
            {
                Texture = frameTex,
                Size = new Vector2(BarW, 150),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.Scale,
            };
            bar.AddChild(frame);
        }
        if (animTex != null)
        {
            // Strip 352×60 = 5 frames ~70 px : 0-1 alpaga qui marche,
            // 2 câlin (alpaga + humain + cœur), 3-4 humain qui marche.
            _mascotFrames = new AtlasTexture[5];
            for (var i = 0; i < 5; i++)
            {
                // Cases de 70,4 px : bornes entières + FilterClip, sinon le filtre bilinéaire
                // laisse voir un liseré de la case voisine (bordure d'animation).
                int x0 = (int)Math.Round(i * 70.4f), x1 = (int)Math.Round((i + 1) * 70.4f);
                _mascotFrames[i] = new AtlasTexture
                {
                    Atlas = animTex,
                    Region = new Rect2(x0, 0, x1 - x0, 60),
                    FilterClip = true,
                };
            }
            _human = new TextureRect
            {
                Texture = _mascotFrames[3],
                Size = new Vector2(70, 60),
                Position = new Vector2(BarW - 88, 40),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode =
                    TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            bar.AddChild(_human);
            _mascot = new TextureRect
            {
                Texture = _mascotFrames[0],
                Size = new Vector2(70, 60),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode =
                    TextureRect.StretchModeEnum.KeepAspectCentered,
            };
            bar.AddChild(_mascot);
        }
    }

    /// <summary>Ancre <paramref name="c"/> au point (x,y) d'un contenu de
    /// référence 1280×720 (fractions du viewport, offsets nuls).</summary>
    private static void AnchorAt(Control c, float x, float y)
    {
        c.AnchorLeft = c.AnchorRight = x / 1280f;
        c.AnchorTop = c.AnchorBottom = y / 720f;
        c.OffsetLeft = c.OffsetRight = c.OffsetTop = c.OffsetBottom = 0;
    }

    /// <summary>Progression « pilotée » : la barre monte en douceur
    /// vers la cible (jamais en arrière).</summary>
    private void SetPhase(float frac, string label)
    {
        _target = Math.Max(_target, frac);
        if (_phase != null) _phase.Text = label;
    }

    /// <summary>Point d'entrée statique pour GameScene/MapLoader —
    /// thread-safe (appelable depuis les tâches de fond : le rapport
    /// est mis en file et appliqué sur le thread principal).</summary>
    public static void Report(float frac, string label) =>
        _reports.Enqueue((frac, label));

    /// <summary>Fin de chargement signalée par GameScene → fondu puis
    /// libération de l'overlay.</summary>
    public static void Finish() => Active?.BeginFade();

    private void BeginFade()
    {
        if (_done) return;
        _done = true;
        _fadeT = 0;
    }

    // --- orchestration -------------------------------------------------

    private async Task RunAsync()
    {
        var s = SessionCarry.Session;
        var c = SessionCarry.Character;
        if (s == null || c == null)
        {
            SetPhase(1f, "Session absente — retour au login");
            await ToSignal(GetTree().CreateTimer(1.2),
                SceneTreeTimer.SignalName.Timeout);
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile,
                "res://scenes/boot.tscn");
            return;
        }
        var nodeId = c.NodeId;
        var mapCode = $"S{nodeId:D3}";
        EdenClient.Perf.PerfTrace.Abs("loading : début");
        try
        {
            // 1) Ticket zone (réseau — quelques RTT).
            SetPhase(0.02f, "Ticket de zone…");
            var t = EdenClient.Perf.PerfTrace.Mark();
            await s.SelectCharacterAsync((uint)c.Id);
            EdenClient.Perf.PerfTrace.Report("  SelectChar", t);
            t = EdenClient.Perf.PerfTrace.Mark();
            await s.GetZoneTicketAsync();
            EdenClient.Perf.PerfTrace.Report("  ZoneTicket", t);
            SetPhase(0.25f, "Connexion au monde…");

            // 2) Connexion zone (handshake + UseTicket). L'entrée
            //    (burst) reste à GameScene — les handlers NCID y sont
            //    branchés avant l'entry.
            t = EdenClient.Perf.PerfTrace.Mark();
            await s.ZoneConnectAsync();
            EdenClient.Perf.PerfTrace.Report("  ZoneConnect", t);
            SetPhase(0.45f, "Chargement de la zone…");

            // 3) Données de map : parse .fsm en tâche de fond — travail
            //    réel (réchauffe aussi le cache disque).
            t = EdenClient.Perf.PerfTrace.Mark();
            var fsm = await Task.Run(() =>
            {
                // Même résolution que MapLoader : [globe_data] de la
                // scène propre déclare la géométrie partagée
                // (S137 → S055.fsm).
                var geo = mapCode;
                if (AssetCatalog.ResolveRawAsset(
                        $"data/scene/{mapCode}.ini") is { } scIni &&
                    Data.Tables.SceneTable.GlobeModelName(scIni)
                        is { Length: > 0 } dd)
                    geo = System.IO.Path.GetFileNameWithoutExtension(dd);
                var p = AssetCatalog.ResolveRawAsset(
                    $"map/model/{geo}/{geo}.fsm");
                return p != null ? FsmFile.Load(p) : null;
            });
            EdenClient.Perf.PerfTrace.Report("  fsm parse", t);
            SessionCarry.PreparedFsm = fsm;
            SetPhase(0.62f, "Objets…");

            // 4) Pré-charge TOUS les protos GLB cités par la map en
            //    tâche de fond parallèle (PreloadGlbProtos) — avant,
            //    60 GLB étaient parsés sur le thread principal à raison
            //    de 2/frame : chaque parse bloquait la frame, la barre
            //    restait figée des secondes sur « Objets… ». Le cache
            //    proto étant partagé, la construction in-game ne fait
            //    ensuite que des Duplicate.
            if (fsm != null)
            {
                var paths = new List<string>();
                var seen = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                if (AssetCatalog.TryGetGlb(
                        $"map/model/{mapCode}/{mapCode}", out var mg))
                    { paths.Add(mg); seen.Add(mg); }
                foreach (var o in fsm.SceneObjects)
                    if (o.Name.Length > 0 &&
                        AssetCatalog.TryGetGlb(
                            $"map/model/object/{o.Name}", out var gp) &&
                        seen.Add(gp))
                        paths.Add(gp);
                // Corps du perso choisi (code corps app[28]>>4 →
                // lettre kfm, switch natif FUN_0073ddc0).
                var app = c.Appearance;
                if (app is { Length: >= 32 })
                {
                    var letter = (app[28] >> 4) switch
                    {
                        1 => "b", 2 => "m", 4 => "f", 6 => "k",
                        8 => "l", 9 => "c", _ => "g",
                    };
                    if (AssetCatalog.TryGetGlb(
                            $"char/model/{letter}", out var cg) &&
                        seen.Add(cg))
                        paths.Add(cg);
                }
                var total = Math.Max(paths.Count, 1);
                var tp = EdenClient.Perf.PerfTrace.Mark();
                for (var i = 0; i < paths.Count; i += 48)
                {
                    var batch = paths.GetRange(i,
                        Math.Min(48, paths.Count - i));
                    await Task.Run(() =>
                        AssetCatalog.PreloadGlbProtos(batch));
                    SetPhase(0.62f +
                        0.30f * Math.Min(i + 48, total) / total,
                        $"Objets… {Math.Min(i + 48, total)}/{total}");
                }
                EdenClient.Perf.PerfTrace.Report(
                    $"  glb preload ({total} protos)", tp);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[loading] {ex}");
            EdenSession.LastDisconnectReason = $"Connection failed: {ex.Message}";
            SetPhase(1f, $"Échec : {ex.Message} — retour…");
            await ToSignal(GetTree().CreateTimer(1.6),
                SceneTreeTimer.SignalName.Timeout);
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile,
                "res://scenes/boot.tscn");
            return;
        }
        // 5) L'écran passe en overlay racine (survit au changement de
        //    scène) puis GameScene poursuit les phases monde/entités.
        EdenClient.Perf.PerfTrace.Abs("loading : → game.tscn");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        MigrateToOverlay();
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile,
            "res://scenes/game.tscn");
    }

    /// <summary>Drain des rapports de progression (file thread-safe)
    /// puis animation de la barre vers la cible.</summary>
    public override void _Process(double delta)
    {
        while (_reports.TryDequeue(out var r))
            SetPhase(r.Frac, r.Label);
        // Barre native animée vers la cible (jamais instantanée) +
        // mascotte qui marche au bout du remplissage.
        if (_barClip != null)
        {
            _shown = _shown < _target
                ? Math.Min(_target, _shown + (float)delta * 0.6f)
                : _target;
            _barClip.Size = new Vector2(BarW * _shown, 150);
            if (_mascot != null && _mascotFrames.Length >= 5)
            {
                var step = (int)(Time.GetTicksMsec() / 160) & 1;
                var full = _done || _shown >= 0.99f;
                if (full)
                {
                    _mascot.Position = new Vector2(BarW - 88, 40);
                    _mascot.Texture = _mascotFrames[2];
                    if (_human != null) _human.Visible = false;
                }
                else
                {
                    _mascot.Position = new Vector2(
                        Math.Clamp(BarW * _shown - 35, 42, BarW - 115), 40);
                    _mascot.Texture = _mascotFrames[step];
                    if (_human != null)
                    {
                        _human.Visible = true;
                        _human.Position = new Vector2(BarW - 88, 40);
                        _human.Texture = _mascotFrames[3 + step];
                    }
                }
            }
        }

        if (_fadeT >= 0)
        {
            _fadeT += delta;
            // Laisse ~0.35 s d'affichage du câlin avant le fondu.
            var alpha = _fadeT < 0.35
                ? 1.0f
                : (float)Math.Max(0, 1.0 - (_fadeT - 0.35) / 0.45);
            Modulate = new Color(1, 1, 1, alpha);
            if (_fadeT >= 0.85)
            {
                _fadeT = -1;
                Active = null;
                GameInput.EdenCursors.Set(GameInput.EdenCursors.Mode.Arrow);
                var parent = GetParent();
                if (parent is CanvasLayer cl && cl.Name == "LoadingOverlay")
                    cl.QueueFree();
                else
                    QueueFree();
            }
        }
    }

    /// <summary>Reparente l'écran sous la racine du viewport dans un
    /// CanvasLayer — survit à <c>ChangeSceneToFile</c> et continue de
    /// recevoir les rapports de <see cref="GameScene"/>.</summary>
    private void MigrateToOverlay()
    {
        var tree = GetTree();           // avant tout détachement !
        var layer = new CanvasLayer { Name = "LoadingOverlay", Layer = 64 };
        GetParent()?.RemoveChild(this);
        tree.Root.AddChild(layer);
        layer.AddChild(this);
        GD.Print("[loading] overlay migré vers la racine");
    }

    // --- textes de zone -------------------------------------------------

    /// <summary>(nom, sous-titre, description) du node via t_node.ini
    /// (Big5 → texte EN quand dispo). Fallback générique.</summary>
    private static (string Name, string Sub, string Desc) NodeText(int id)
    {
        try
        {
            // TextResolver : data/db/lang/<lang>/t_node.ini prioritaire,
            // repli data/db/t_node.ini (EN).
            var name = TextResolver.NodeName(id);
            if (name != null)
            {
                var desc = (TextResolver.NodeDescriptionRaw(id) ?? "")
                    .Replace("$N$", "\n").Trim();
                return (name, TextResolver.NodeSubtitle(id) ?? "", desc);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[loading] t_node.ini : {ex.Message}");
        }
        return (id > 0 ? $"S{id:D3}" : "Eden Limitless", "", "");
    }
}
