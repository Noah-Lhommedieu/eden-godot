using Godot;
using EdenClient.Data;
using EdenClient.Game;
using EdenClient.Hud;
using EdenClient.Session;
using EdenClient.Ui;

namespace EdenClient.Screens;

/// <summary>
/// Sélection de personnage : TOUS les personnages du compte sont
/// posés en même temps sur l'estrade du hall (une rangée centrée sur
/// la caméra), chacun avec sa plaque (icône de classe, nom, niveau,
/// classe, zone) ; un clic sur le perso ou sa plaque le sélectionne
/// (anneau doré, plaque dorée), flèches ←/→ du clavier idem. Plus de
/// « clic pour passer au suivant ». Les corps sont construits en
/// tâche de fond depuis le cache de protos
/// (<see cref="CharBodyCache"/>, préchauffé dès le login par
/// <see cref="PreGameWarmup"/>), le perso sélectionné d'abord.
/// Boutons d'action à droite, « Start Game » bas-centre (layout de
/// la capture native Selection_personnage).
/// </summary>
public partial class CharSelectScreen : Control
{
    private ScreenManager Mgr => (ScreenManager)GetParent();
    private int _selected = -1;
    private Label? _error;
    private bool _busy;
    private static Dictionary<int, Texture2D?>? _classIcons;
    private int _buildGen;
    /// <summary>Tous les avatars attendus sont posés — attendu par
    /// le shot QA (même convention que <see cref="CharCreateScreen"/>).</summary>
    public bool AvatarReady { get; private set; }

    private sealed class Slot
    {
        public Node3D Anchor = null!;     // point au sol (enfant du marqueur)
        public Node3D? Avatar;
        public MeshInstance3D Ring = null!;
        public Button Hit = null!;        // zone cliquable sur le perso
        public PanelContainer Plate = null!;
        public StyleBoxFlat PlateStyle = null!;
        public float Yaw;
    }
    private readonly List<Slot> _slots = new();
    private Control? _plateLayer;
    private const float Spacing = 2.5f;
    private const float AvatarHeight = 2.7f;

    public override void _Ready()
    {
        EdenClient.Perf.PerfTrace.Abs("charsel : _Ready");
        var t = EdenClient.Perf.PerfTrace.Mark();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        TempleBackdrop.Attach(this, TempleBackdrop.Shot.Select);
        AddChild(PreGameStyle.Vignette());
        EdenClient.Perf.PerfTrace.Report("  backdrop attach", t);
        t = EdenClient.Perf.PerfTrace.Mark();
        _plateLayer = new Control { Name = "PlateLayer",
            MouseFilter = MouseFilterEnum.Ignore };
        _plateLayer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_plateLayer);
        BuildTopBar();
        BuildActionColumn();
        BuildBottomBar();
        EdenClient.Perf.PerfTrace.Report("  panels", t);
        t = EdenClient.Perf.PerfTrace.Mark();
        FillSlots();
        EdenClient.Perf.PerfTrace.Report("  fillslots", t);
        EdenClient.Perf.PerfTrace.Abs("charsel : prêt");
    }

    // --- Layout ------------------------------------------------------

    private static StyleBoxFlat PanelStyle() => PreGameStyle.Panel(0.93f, 10);

    /// <summary>Bandeau haut : titre centré + nom du monde (serverNameText
    /// natif « Ch01(Ch01) ») à droite.</summary>
    private void BuildTopBar()
    {
        var title = PreGameStyle.Text(
            UiTextEn.Translate("Choose your character"), 22,
            PreGameStyle.Gold, HorizontalAlignment.Center);
        title.Name = "SelectTitle";
        title.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        title.OffsetTop = 14; title.OffsetBottom = 48;
        AddChild(title);

        var world = Mgr.WorldName;
        if (!string.IsNullOrWhiteSpace(world))
        {
            var server = PreGameStyle.Text($"{world}({world})", 14,
                PreGameStyle.Ink, HorizontalAlignment.Right);
            server.Name = "ServerNameText";
            server.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
            server.OffsetLeft = -260; server.OffsetRight = -18;
            server.OffsetTop = 16; server.OffsetBottom = 40;
            AddChild(server);
        }
    }

    /// <summary>Colonne de boutons bas-droite : Create / Delete + Set
    /// password / Remove password / Select Server / Logout.</summary>
    private void BuildActionColumn()
    {
        var col = new VBoxContainer
        {
            Name = "ActionColumn",
            AnchorLeft = 0.835f,
            AnchorTop = 0.50f,
            AnchorRight = 0.985f,
            AnchorBottom = 0.97f,
            GrowVertical = GrowDirection.Begin,
        };
        col.AddThemeConstantOverride("separation", 8);
        AddChild(col);

        var cd = new HBoxContainer { Name = "CreateDeleteRow" };
        cd.AddThemeConstantOverride("separation", 8);
        cd.AddChild(MakeActionButton(UiTextEn.Translate("Create"),
            () => Mgr.Show(new CharCreateScreen())));
        cd.AddChild(MakeActionButton(UiTextEn.Translate("Delete"), OnDelete));
        col.AddChild(cd);
        col.AddChild(MakeActionButton(UiTextEn.Translate("Set password"),
            () => ShowPasswordDialog(remove: false)));
        col.AddChild(MakeActionButton(UiTextEn.Translate("Remove password"),
            () => ShowPasswordDialog(remove: true)));
        col.AddChild(MakeActionButton(UiTextEn.Translate("Select Server"),
            Mgr.BackToChannelSelect));
        col.AddChild(MakeActionButton(UiTextEn.Translate("Logout"), Mgr.BackToLogin));

        _error = new Label
        {
            Name = "RosterMessage",
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 22),
        };
        _error.AddThemeColorOverride("font_color", PreGameStyle.Err);
        col.AddChild(_error);
    }

    // --- Mot de passe personnage (NCID 30/31 → 75/76) ---------------

    private Panel? _pwPop;
    private LineEdit? _pwEdit;

    /// <summary>Message de statut colonne droite — rouge erreur,
    /// vert doux succès (remplace le label erreur à la demande).</summary>
    private void Note(string text, bool ok)
    {
        if (_error == null) return;
        _error.Text = text;
        _error.AddThemeColorOverride("font_color", ok
            ? new Color(0.55f, 0.85f, 0.55f)
            : new Color(1f, 0.45f, 0.4f));
    }

    /// <summary>Popup centré maison (même pattern que les popups HUD) —
    /// saisie pour « Set », confirmation pour « Remove ».</summary>
    private void ShowPasswordDialog(bool remove)
    {
        var chars = Mgr.Session?.Characters;
        if (chars == null || _selected < 0 || _selected >= chars.Count)
        {
            Note(UiTextEn.Translate("Select a character."), ok: false);
            return;
        }
        var target = chars[_selected];
        _pwPop?.QueueFree();
        var pop = new Panel { Name = "_pwPop" };
        pop.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        pop.Position = new Vector2(-150, -70);
        pop.Size = new Vector2(300, 140);
        if (Mgr.Ui != null && Mgr.Tex != null)
        {
            DragHelper.ApplyNativeChrome(pop, Mgr.Ui, Mgr.Tex);
            DragHelper.FillClientArea(pop,
                new Color(0.05f, 0.055f, 0.08f, 0.95f));
        }
        if (!pop.HasThemeStyleboxOverride("panel"))
            pop.AddThemeStyleboxOverride("panel", PanelStyle());
        AddChild(pop);
        _pwPop = pop;

        var lab = new Label
        {
            Position = new Vector2(12, 8),
            Size = new Vector2(276, 36),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        lab.AddThemeFontSizeOverride("font_size", 13);
        pop.AddChild(lab);

        if (!remove)
        {
            lab.Text = UiTextEn.Translate($"New password for {target.Name}:");
            _pwEdit = new LineEdit
            {
                Position = new Vector2(12, 46),
                Size = new Vector2(276, 28),
                Secret = true,
                PlaceholderText = UiTextEn.Translate("password…"),
                MaxLength = 16,
            };
            pop.AddChild(_pwEdit);
        }
        else
        {
            lab.Text = UiTextEn.Translate($"Remove the password of {target.Name}?");
            _pwEdit = null;
        }

        var ok = new Button
        {
            Text = remove ? UiTextEn.Translate("Remove") : UiTextEn.Translate("Set"),
            Position = new Vector2(72, 100),
            Size = new Vector2(70, 28),
        };
        ok.Pressed += () =>
        {
            var pw = remove ? "" : (_pwEdit?.Text.Trim() ?? "");
            if (!remove && pw.Length == 0)
            {
                Note(UiTextEn.Translate("Password cannot be empty."), ok: false);
                return;
            }
            pop.QueueFree();
            _pwPop = null;
            _ = CharPasswordAsync(target.Id, pw, remove);
        };
        pop.AddChild(ok);
        var cancel = new Button
        {
            Text = UiTextEn.Translate("Cancel"),
            Position = new Vector2(158, 100),
            Size = new Vector2(70, 28),
        };
        cancel.Pressed += () => { pop.QueueFree(); _pwPop = null; };
        pop.AddChild(cancel);
        _pwEdit?.GrabFocus();
    }

    /// <summary>op 3 = définit (pw vide = retire) — le serveur renvoie
    /// 3 en cas de succès (MiscHandlers.DoCharPasswordAsync).</summary>
    private async Task CharPasswordAsync(int charId, string pw,
        bool remove)
    {
        var s = Mgr.Session;
        if (s == null) return;
        try
        {
            var code = await s.CharPasswordOpAsync(charId, 3, pw);
            Note(code == 3
                ? (remove ? UiTextEn.Translate("Password removed.") : UiTextEn.Translate("Password set."))
                : UiTextEn.Translate($"Password op refused (code {code})."), code == 3);
        }
        catch (Exception ex)
        {
            Note(UiTextEn.Translate($"Password op failed: {ex.Message}"), ok: false);
        }
    }

    /// <summary>Bas-centre : flèches de rotation (perso sélectionné) +
    /// « Start Game » doré — composition de la capture native.</summary>
    private void BuildBottomBar()
    {
        var arrows = new HBoxContainer
        {
            Name = "RotateRow",
            AnchorLeft = 0.42f,
            AnchorTop = 0.835f,
            AnchorRight = 0.58f,
            AnchorBottom = 0.89f,
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = MouseFilterEnum.Pass,
        };
        arrows.AddThemeConstantOverride("separation", 34);
        AddChild(arrows);
        arrows.AddChild(MakeTurnButton("CreateSelectL",
            () => RotatePreview(-0.35f)));
        arrows.AddChild(MakeTurnButton("CreateSelectR",
            () => RotatePreview(0.35f)));

        var start = MakeActionButton(UiTextEn.Translate("Start Game"), OnEnter,
            primary: true);
        start.Name = "StartGameButton";
        start.AnchorLeft = 0.40f;
        start.AnchorTop = 0.91f;
        start.AnchorRight = 0.60f;
        start.AnchorBottom = 0.97f;
        AddChild(start);
    }

    /// <summary>Bouton rotation natif : sprite DejaVu
    /// <paramref name="imageStem"/>+« Nor/Hover/Press » via le cache
    /// d'imagesets ; repli texte ‹ › si l'atlas n'est pas résolu.</summary>
    private Control MakeTurnButton(string imageStem, Action action)
    {
        CeguiImageRef img(string suffix) =>
            new("DejaVu", $"{imageStem}{suffix}");
        var normal = Mgr.Tex?.GetRegion(img("Nor"));
        if (normal != null)
        {
            var b = new TextureButton
            {
                TextureNormal = normal,
                TextureHover = Mgr.Tex?.GetRegion(img("Hover")),
                TexturePressed = Mgr.Tex?.GetRegion(img("Press")),
                StretchMode = TextureButton.StretchModeEnum.Scale,
                CustomMinimumSize = new Vector2(70, 32),
                TooltipText = UiTextEn.Translate("Rotate"),
            };
            b.Pressed += action;
            return b;
        }
        var fb = MakeActionButton(
            imageStem.EndsWith("L") ? "‹" : "›", action);
        fb.CustomMinimumSize = new Vector2(52, 32);
        return fb;
    }

    private static Button MakeActionButton(string text, Action action,
        bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, primary ? 48 : 40),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.All,
        };
        PreGameStyle.Button(button, primary, primary ? 18 : 14);
        button.Pressed += action;
        return button;
    }

    private void FillSlots()
    {
        EdenClient.Data.TextResolver.ResolveFile ??= rel =>
            AssetCatalog.ResolveRawAsset(rel) ??
            DataPaths.ResolveFile(rel);
        var chars = Mgr.Session?.Characters ?? new();
        // QA : --mock-chars=N complète la liste (copie, la session
        // n'est pas touchée) par des persos synthétiques pour banc de
        // cadrage — le compte réel n'a souvent qu'un perso, ce qui ne
        // couvre ni le débordement colonne d'actions ni le multi-slot.
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
            if (a.StartsWith("--mock-chars=", StringComparison.Ordinal) &&
                int.TryParse(a["--mock-chars=".Length..], out var want) &&
                want > chars.Count)
            {
                var real = chars.Count;
                chars = chars.Concat(Enumerable.Range(0, want - real)
                    .Select(k =>
                    {
                        var i = real + k;
                        var src = real > 0 ? chars[k % real] : null;
                        return new CharacterSummary(-(i + 1),
                            $"Mock {i + 1}",
                            (short)(1 + i % 20), (short)(20 + i * 9),
                            1, src?.RaceId ?? 1, 1,
                            src?.Appearance, src?.PreviewWorn);
                    }))
                    .ToList();
                GD.Print($"[charsel] mock-chars : {real} réels → " +
                    $"{chars.Count} affichés");
                break;
            }
        // Mémorise les apparences : prochain login = corps préchauffés.
        PreGameWarmup.SaveHint(Mgr.Config.Account,
            chars.Select(c => c.Appearance));
        if (chars.Count == 0)
        {
            var none = PreGameStyle.Text(
                UiTextEn.Translate("No characters — press Create."), 20,
                PreGameStyle.Dim, HorizontalAlignment.Center);
            none.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
            none.OffsetLeft = -300; none.OffsetRight = 300;
            AddChild(none);
            AvatarReady = true;   // rien à poser — le shot n'attend pas
            return;
        }

        FrameRow(chars.Count);
        var marker = TempleBackdrop.Shared.CharMarker;
        var cam = TempleBackdrop.Shared.Camera;
        var right = cam.GlobalTransform.Basis.X;
        right.Y = 0;
        right = right.Normalized();
        var inv = marker.GlobalTransform.Basis.Inverse();
        for (var i = 0; i < chars.Count; i++)
        {
            var off = (i - (chars.Count - 1) * 0.5f) * Spacing;
            var anchor = new Node3D { Name = $"CharSlot{i}",
                Position = inv * (right * off) };
            marker.AddChild(anchor);
            var slot = new Slot { Anchor = anchor };
            slot.Ring = MakeRing();
            anchor.AddChild(slot.Ring);
            BuildSlotUi(slot, chars[i], i);
            _slots.Add(slot);
        }
        Select(_selected >= 0 ? _selected : LastUsedIndex(chars));
        BuildAvatars(chars);
    }

    /// <summary>Recule/élargit la caméra Select pour cadrer la
    /// rangée entière (n persos de <see cref="Spacing"/>).</summary>
    private void FrameRow(int n)
    {
        var tb = TempleBackdrop.Shared;
        var cam = tb.Camera;
        var focus = tb.CharMarker.Position + new Vector3(0f, 1.0f, 0f);
        var vp = GetViewportRect().Size;
        var aspect = Mathf.Max(1.2f, vp.X / Mathf.Max(1f, vp.Y));
        var halfW = (n * Spacing + 3.2f) * 0.5f;
        var tan = Mathf.Tan(Mathf.DegToRad(cam.Fov * 0.5f)) * aspect;
        // La colonne d'actions couvre x écran ≥ ~0,835 (y 0,50-0,97) :
        // à partir de ~6 persos le dernier avatar ET sa plaque
        // débordaient dessus (« avatar manquant »). Le centre du
        // dernier perso doit rester sous SafeRight écran — sinon on
        // recule la caméra (rangée plus serrée, tous visibles).
        // EDEN_CHARSEL_SAFE=1.0 restaure l'ancien comportement (avant/
        // après QA sans rebuild).
        var safeRight =
            float.TryParse(System.Environment.GetEnvironmentVariable(
                    "EDEN_CHARSEL_SAFE"),
                System.Globalization.CultureInfo.InvariantCulture,
                out var sr) ? sr : 0.78f;
        var centerOff = (n - 1) * 0.5f * Spacing;
        var safeNdc = safeRight * 2f - 1f;
        var dist = Mathf.Clamp(
            Mathf.Max(halfW / tan,
                centerOff / Mathf.Max(0.01f, safeNdc * tan)),
            9.5f, 40f);
        var dir = (cam.Position - focus).Normalized();
        cam.Position = focus + dir * dist + new Vector3(0f, 0.6f, 0f);
        cam.LookAt(focus + new Vector3(0f, 0.2f, 0f), Vector3.Up);
    }

    private static MeshInstance3D MakeRing()
    {
        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.85f, 0.45f, 0.18f),
            NoDepthTest = false,
        };
        return new MeshInstance3D
        {
            Name = "Ring",
            Mesh = new CylinderMesh
            {
                TopRadius = 1.0f, BottomRadius = 1.0f, Height = 0.03f,
                RadialSegments = 40,
            },
            MaterialOverride = mat,
            Position = new Vector3(0f, 0.03f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>Plaque (icône classe + nom + niveau/classe + zone) et
    /// zone cliquable transparente couvrant le perso ; positionnées
    /// chaque frame sur la projection écran de l'ancre.</summary>
    private void BuildSlotUi(Slot slot, CharacterSummary c, int idx)
    {
        slot.Hit = new Button
        {
            Name = $"CharHit{idx}",
            Flat = true,
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            TooltipText = c.Name,
        };
        slot.Hit.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        slot.Hit.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
        slot.Hit.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
        slot.Hit.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        var i = idx;
        slot.Hit.Pressed += () => Select(i);
        slot.Hit.GuiInput += e =>
        {
            if (e is InputEventMouseButton
                { ButtonIndex: MouseButton.Left, DoubleClick: true })
                OnEnter();
        };
        _plateLayer!.AddChild(slot.Hit);

        slot.Plate = new PanelContainer
        {
            Name = $"CharPlate{idx}",
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        slot.PlateStyle = PreGameStyle.Panel(0.80f, 6);
        slot.PlateStyle.ShadowSize = 8;
        slot.Plate.AddThemeStyleboxOverride("panel", slot.PlateStyle);
        slot.Plate.GuiInput += e =>
        {
            if (e is InputEventMouseButton
                { ButtonIndex: MouseButton.Left, Pressed: true })
                Select(i);
        };
        var row = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 6);
        slot.Plate.AddChild(row);
        row.AddChild(new TextureRect
        {
            Texture = ClassIcon(c.ClassId),
            CustomMinimumSize = new Vector2(34, 34),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        var txt = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        txt.AddThemeConstantOverride("separation", 0);
        row.AddChild(txt);
        var className = EdenClient.Data.TextResolver.ClassName(c.ClassId)
            ?? $"Class {c.ClassId}";
        txt.AddChild(PreGameStyle.Text(c.Name, 15, PreGameStyle.Ink));
        txt.AddChild(PreGameStyle.Text(
            $"{UiTextEn.Translate("Lv")} {c.Level}  ·  {className} {c.ClassLv}",
            11, PreGameStyle.Dim));
        var zone = EdenClient.Data.TextResolver.NodeName(c.NodeId) ?? "";
        if (zone.Length > 0)
            txt.AddChild(PreGameStyle.Text(zone, 11,
                new Color(0.62f, 0.66f, 0.72f)));
        _plateLayer.AddChild(slot.Plate);
    }

    /// <summary>Icône de classe (c_class.ini col3 → UI/uiicon) —
    /// partagée avec la fenêtre Classe du HUD.</summary>
    internal static Texture2D? ClassIcon(int classId)
    {
        if (_classIcons != null)
            return _classIcons.TryGetValue(classId, out var cached)
                ? cached
                : _classIcons.TryGetValue(classId % 100, out var b0) ? b0 : null;
        _classIcons = new Dictionary<int, Texture2D?>();
        var path = AssetCatalog.ResolveRawAsset("data/db/c_class.ini");
        if (path == null) return null;
        try
        {
            var table = Data.IniTable.ParseFile(path,
                Data.EdenEncoding.Big5);
            foreach (var row in table.Rows)
                if (row.Length > 3)
                {
                    // col0 = id de classe (1-20 base, 21+ avancées, 101-420
                    // composites race×classe) ; col3 = icône UI/uiicon.
                    var id = Data.IniTable.GetInt(row, 0);
                    var code = Data.ClassIconMap.Code(id,
                        Data.IniTable.GetStr(row, 3));
                    if (id > 0 && code.Length > 0)
                        _classIcons[id] = AssetCatalog.GetTexture(
                            $"UI/uiicon/{code}.png");
                }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[charsel] class icon: {e.Message}");
        }
        if (_classIcons.TryGetValue(classId, out var icon)) return icon;
        // Composite race×classe (105 = Thief humain) → classe de base.
        return _classIcons.TryGetValue(classId % 100, out var b) ? b : null;
    }

    private void Select(int idx)
    {
        var chars = Mgr.Session?.Characters;
        if (chars == null || idx < 0 || idx >= chars.Count) return;
        _selected = idx;
        for (var i = 0; i < _slots.Count; i++)
            StyleSlot(_slots[i], i == idx);
    }

    /// <summary>Plaque + anneau : doré si sélectionné, discret sinon.</summary>
    private static void StyleSlot(Slot s, bool sel)
    {
        s.PlateStyle.BorderColor = sel
            ? new Color(1f, 0.88f, 0.45f)
            : new Color(0.45f, 0.45f, 0.48f, 0.8f);
        s.PlateStyle.BorderWidthBottom = s.PlateStyle.BorderWidthTop =
            s.PlateStyle.BorderWidthLeft = s.PlateStyle.BorderWidthRight =
                sel ? 2 : 1;
        s.PlateStyle.BgColor = sel
            ? new Color(0.16f, 0.13f, 0.06f, 0.92f)
            : new Color(0.055f, 0.06f, 0.08f, 0.78f);
        if (s.Ring.MaterialOverride is StandardMaterial3D m)
            m.AlbedoColor = sel ? new Color(1f, 0.85f, 0.4f, 0.55f)
                                : new Color(1f, 0.9f, 0.7f, 0.12f);
        s.Ring.Scale = sel ? new Vector3(1.15f, 1f, 1.15f) : Vector3.One;
    }

    // --- avatars 3D posés sur l'estrade (un par perso) -------------------

    /// <summary>Construit tous les corps en tâche de fond (le
    /// sélectionné d'abord, puis les autres) ; chaque avatar est posé
    /// sur son ancre dès qu'il est prêt, sans attendre les autres.
    /// Le cache de protos rend les doublons d'apparence quasi
    /// gratuits ; un corps déjà préchauffé au login est instantané.</summary>
    private void BuildAvatars(List<CharacterSummary> chars)
    {
        var gen = ++_buildGen;
        AvatarReady = false;
        var order = Enumerable.Range(0, chars.Count)
            .OrderBy(i => i == _selected ? 0 : 1).ThenBy(i => i).ToList();
        var pending = chars.Count;
        Task.Run(() =>
        {
            foreach (var i in order)
            {
                var c = chars[i];
                var app = c.Appearance;
                var worn = c.PreviewWorn;
                Node3D? body = null;
                // Une exception (GLB corrompu, habillage…) tuait toute
                // la tâche : les persos suivants n'avaient plus
                // d'avatar et AvatarReady restait faux.
                try
                {
                    body = CharBodyCache.GetOrBuild(app);
                    if (body != null)
                    {
                        body.RotationDegrees = new Vector3(-90f, 180f, 0f);
                        if (app is { Length: >= 32 })
                            _ = CharDressing.Apply(body, app, worn, ItemLookup,
                                c.ClassId, 0);
                    }
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[charsel] avatar {i} ({c.Name}) : {e}");
                }
                if (body == null)
                {
                    GD.PrintErr("[charsel] avatar : GetOrBuild null " +
                        $"(app={Convert.ToHexString(app ?? [])})");
                    Callable.From(() => { if (--pending <= 0 && gen == _buildGen) AvatarReady = true; })
                        .CallDeferred();
                    continue;
                }
                var idx = i;
                Callable.From(() =>
                {
                    AttachAvatarAnimated(gen, idx, body, app);
                    if (--pending <= 0 && gen == _buildGen)
                    {
                        AvatarReady = true;
                        EdenClient.Perf.PerfTrace.Abs(
                            "charsel : tous les avatars attachés");
                    }
                }).CallDeferred();
            }
        });
    }

    private void AttachAvatarAnimated(int gen, int idx, Node3D body,
        byte[]? app)
    {
        if (!GodotObject.IsInstanceValid(this) || gen != _buildGen ||
            idx >= _slots.Count || !GodotObject.IsInstanceValid(_slots[idx].Anchor))
        {
            body.QueueFree();
            return;
        }
        // L'animator C# ne peut être créé que sur le thread principal.
        CharVisual.AttachAnimator(body, CharModelSpec.BodyStem(app));
        CharVisual.PlayIdle(body);
        var slot = _slots[idx];
        slot.Avatar = body;
        slot.Anchor.AddChild(body);
        EdenClient.Perf.PerfTrace.Abs($"charsel : avatar {idx} attaché");
    }

    private void RotatePreview(float delta)
    {
        if (_selected < 0 || _selected >= _slots.Count) return;
        var s = _slots[_selected];
        s.Yaw += delta;
        if (s.Avatar != null && GodotObject.IsInstanceValid(s.Avatar))
            s.Avatar.RotationDegrees = new Vector3(-90f,
                180f + Mathf.RadToDeg(s.Yaw), 0f);
    }

    public override void _Process(double delta)
    {
        LayoutPlates();
        ShotTick(delta);
    }

    /// <summary>Projette chaque ancre à l'écran : plaque sous les
    /// pieds, zone cliquable de la tête aux pieds.</summary>
    private void LayoutPlates()
    {
        if (_slots.Count == 0) return;
        var cam = TempleBackdrop.Shared.Camera;
        if (!GodotObject.IsInstanceValid(cam) || !cam.IsInsideTree()) return;
        foreach (var s in _slots)
        {
            if (!GodotObject.IsInstanceValid(s.Anchor)) continue;
            var feet = s.Anchor.GlobalPosition;
            if (cam.IsPositionBehind(feet)) { s.Plate.Visible = false; continue; }
            var f2 = cam.UnprojectPosition(feet);
            var h2 = cam.UnprojectPosition(feet + new Vector3(0f, AvatarHeight, 0f));
            var side = cam.UnprojectPosition(feet + new Vector3(0f, 0f, 0f) +
                cam.GlobalTransform.Basis.X * (Spacing * 0.5f));
            var halfW = Mathf.Abs(side.X - f2.X);
            s.Plate.Visible = true;
            s.Plate.ResetSize();
            var ps = s.Plate.Size;
            s.Plate.Position = new Vector2(f2.X - ps.X * 0.5f, f2.Y + 6f);
            s.Hit.Position = new Vector2(f2.X - halfW * 0.85f, h2.Y);
            s.Hit.Size = new Vector2(halfW * 1.7f, Mathf.Max(20f, f2.Y - h2.Y));
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k ||
            (Mgr.Session?.Characters.Count ?? 0) == 0) return;
        var n = Mgr.Session!.Characters.Count;
        switch (k.Keycode)
        {
            case Key.Left:
                Select((Math.Max(0, _selected) + n - 1) % n);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Right:
                Select((Math.Max(0, _selected) + 1) % n);
                GetViewport().SetInputAsHandled();
                break;
            case Key.Enter or Key.KpEnter when _pwPop == null:
                OnEnter();
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    // itemId → (Code, ModelCode) c_item.ini — chargé paresseusement
    // pour la résolution des tenues du sélecteur (même convention que
    // le panneau d'équipement du HUD).
    private static Dictionary<int, Data.Tables.ItemTable.ItemDef>?
        _items;
    private static readonly object _itemsLock = new();

    private static (string Code, string ModelCode)? ItemLookup(int id)
    {
        lock (_itemsLock)
        {
            if (_items == null)
            {
                var path = AssetCatalog.ResolveRawAsset(
                    "data/db/c_item.ini");
                try
                {
                    var t = EdenClient.Perf.PerfTrace.Mark();
                    _items = path != null
                        ? Data.Tables.ItemTable.Load(path)
                        : new Dictionary<int,
                            Data.Tables.ItemTable.ItemDef>();
                    // KB-23 : costumes/montures boutique (ids 40xxx) sont
                    // seulement dans c_itemmall.ini (cf. InventoryWindow).
                    var mall = path != null
                        ? AssetCatalog.ResolveRawAsset("data/db/c_itemmall.ini")
                        : null;
                    if (mall != null)
                        foreach (var (mid, mdef) in Data.Tables.ItemTable.Load(mall))
                            _items.TryAdd(mid, mdef);
                    EdenClient.Perf.PerfTrace.Report(
                        "  c_item.ini (5 Mo)", t);
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[charsel] c_item.ini : {e.Message}");
                    _items = new Dictionary<int,
                        Data.Tables.ItemTable.ItemDef>();
                }
            }
            return _items.TryGetValue(id, out var d)
                ? (d.Code, d.ModelCode) : null;
        }
    }

    // QA : --select-shot=<path> → capture du sélecteur après ~2 s
    // (vérif visuelle orientation/tenue/yeux sans interaction).
    private double _shotT = -1;
    private bool _shotDone;
    private string _shotPath = "/tmp/charsel.png";

    private void ShotTick(double delta)
    {
        if (_shotT < 0)
        {
            if (_shotDone) return;
            foreach (var a in OS.GetCmdlineArgs()
                         .Concat(OS.GetCmdlineUserArgs()))
                if (a.StartsWith("--select-shot="))
                {
                    var shotP = a[14..];
                    _shotPath = System.IO.Path.IsPathRooted(shotP)
                        ? shotP : System.IO.Path.GetFullPath(shotP);
                    _shotT = 0;
                }
            if (_shotT < 0) return;
        }
        _shotT += delta;
        // Le corps du perso est construit en tâche de fond (~7-11 s
        // à froid) — la capture attend l'avatar réel, cap à 20 s.
        if ((_shotT > 2.0 && AvatarReady) || _shotT > 20.0)
        {
            _shotDone = true;
            _shotT = -1;
            RenderingServer.ForceDraw();
            GetViewport().GetTexture()?.GetImage()?.SavePng(_shotPath);
            GD.Print($"[charsel] shot {_shotPath}");
        }
    }

    // --- dernier personnage utilisé du compte --------------------------

    private string LastUsedPath() =>
        $"user://last_char_{Mgr.Session?.AccountId ?? 0}.txt";

    /// <summary>Index du dernier perso entré en jeu par ce compte
    /// (0 si inconnu ou supprimé).</summary>
    private int LastUsedIndex(List<CharacterSummary> chars)
    {
        try
        {
            using var f = Godot.FileAccess.Open(LastUsedPath(), Godot.FileAccess.ModeFlags.Read);
            if (f != null && int.TryParse(f.GetAsText().Trim(), out var id))
            {
                var i = chars.FindIndex(c => c.Id == id);
                if (i >= 0) return i;
            }
        }
        catch { }
        return 0;
    }

    private void RememberLastUsed(int charId)
    {
        try
        {
            using var f = Godot.FileAccess.Open(LastUsedPath(), Godot.FileAccess.ModeFlags.Write);
            f?.StoreString(charId.ToString());
        }
        catch { }
    }

    // --- actions -------------------------------------------------------

    /// <summary>QA : sélectionne un personnage et entre en jeu
    /// (pilote automatique --char). <paramref name="sel"/> = index
    /// 0-based ou nom exact (insensible à la casse) ; défaut = le
    /// premier.</summary>
    public void AutoEnter(string? sel = null)
    {
        var chars = Mgr.Session?.Characters;
        if ((chars?.Count ?? 0) > 0 && chars != null)
        {
            // _selected peut déjà être posé par la sélection auto de
            // la carte 0 — un sélecteur explicite (--char=nom|index)
            // doit quand même être honoré.
            var idx = _selected >= 0 ? _selected : 0;
            if (sel != null)
            {
                var found = int.TryParse(sel, out var n)
                    ? n
                    : chars.FindIndex(c => string.Equals(c.Name, sel,
                        StringComparison.OrdinalIgnoreCase));
                if (found >= 0) idx = found;
            }
            idx = Math.Clamp(idx, 0, chars.Count - 1);
            GD.Print($"[auto] char select idx={idx} " +
                     $"({chars[idx].Name})");
            Select(idx);
        }
        OnEnter();
    }

    private async void OnEnter()
    {
        if (_busy) return;
        var s = Mgr.Session;
        var chars = s?.Characters;
        if (s == null || chars == null || _selected < 0 ||
            _selected >= chars.Count)
        {
            if (_error != null) _error.Text = UiTextEn.Translate("Select a character.");
            return;
        }
        _busy = true;
        var c = chars[_selected];
        RememberLastUsed(c.Id);
        EdenClient.Perf.PerfTrace.Abs("chars→game : clic");
        try
        {
            // Le ticket zone + la connexion sont faits par l'écran de
            // chargement (phases réelles) — on ne fait que passer la
            // session et le perso choisi.
            SessionCarry.Session = s;
            SessionCarry.Character = c;
            SessionCarry.WorldName = Mgr.WorldName;
            // Écran de chargement intermédiaire (natif-style) —
            // thread principal garanti via CallDeferred.
            GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile,
                "res://scenes/loading.tscn");
        }
        catch (Exception ex)
        {
            _busy = false;
            if (_error != null) _error.Text = ex.Message;
            GD.PrintErr($"[charsel] entrée zone : {ex}");
        }
    }

    private async void OnDelete()
    {
        if (_busy) return;
        var s = Mgr.Session;
        var chars = s?.Characters;
        if (s == null || chars == null || _selected < 0 ||
            _selected >= chars.Count)
            return;
        _busy = true;
        var c = chars[_selected];
        try
        {
            await s.RemoveCharacterAsync(c.Id, c.Name);
            FillSlotsRefresh();
        }
        catch (Exception ex)
        {
            if (_error != null) _error.Text = ex.Message;
        }
        _busy = false;
    }

    private void FillSlotsRefresh()
    {
        // Recharge la scène d'écran pour re-binder proprement.
        Mgr.ShowDeferred(new CharSelectScreen());
    }
}

