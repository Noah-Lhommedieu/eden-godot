using Godot;
using EdenClient.Data;
using EdenClient.Ui;

namespace EdenClient.Hud;

/// <summary>
/// Bloc vitaux haut-gauche — instancie le layout natif
/// <c>MiniInfoWnd.layout</c> (portrait <c>faceFrame</c>, nom, LVText,
/// HpBar/MpBar sur InfoHpBG) et ajoute la grille 12 slots numérotés
/// (1-6 / 7-12) du client natif ainsi qu'une barre XP fine.
/// </summary>
public partial class TopLeftVitals : Control
{
    private ProgressBar? _hp, _mp;
    private Control? _leader;
    private Label? _hpText, _mpText, _name, _lv, _jobLv;
    private HudRoot? _hud;

    public void Build(HudRoot hud)
    {
        _hud = hud;
        var perf = new PerfOverlay();
        hud.AddChild(perf);   // A4.4 : overlay FPS/ping (ui.show_perf)
        perf.Build();
        var root = hud.Build("MiniInfoWnd.layout");
        if (root != null)
        {
            AddChild(root);
            // Ref TopLeftVitals.png : bloc collé en haut-gauche (la
            // sérialisation 0.04×H le décalait de ~30 px vers le bas).
            HudRoot.Pin(root, 0, 0, 0, 0, 7, 2, 7 + 264, 2 + 110);
            _hp = Find<ProgressBar>(root, "HpBar");
            _mp = Find<ProgressBar>(root, "MpBar");
            _hpText = Find<Label>(_hp, "text");
            _mpText = Find<Label>(_mp, "text");
            _name = Find<Label>(root, "NameText");
            HookVitalsPref();
            // Clic droit sur le bloc vitaux : réglages groupe / reset
            // donjon. Face a MousePassThroughEnabled chez le natif —
            // c'est le bloc entier (et non la seule face) qui reçoit
            // le clic, et les enfants de backgrand peuvent couvrir
            // Face (z plus haut) → bind sur la racine.
            root.MouseFilter = MouseFilterEnum.Stop;
            root.GuiInput += e =>
            {
                if (e is InputEventMouseButton
                    { Pressed: true, ButtonIndex: MouseButton.Right } mb)
                    ShowPortraitMenu(mb.GlobalPosition);
            };
            _lv = Find<Label>(root, "LVText");
            _jobLv = Find<Label>(root, "JobLvText");
            // M12 : icône de classe native (JoBIamge%02d chez le natif)
            // — texture posée sur le slot StaticImage du layout.
            _classIcon = Find<TextureRect>(root, "classIcon");
            // CharIcon = trait héroïque (NatrueIamge{feature-52000},
            // features 52001-52008 chez le natif) — A4.5 : create_flag
            // poussé par le paquet custom 9058 (HeroTrait) ; slot vide
            // sans trait.
            _traitIcon = Find<TextureRect>(root, "CharIcon");
            ApplyTrait();
            HeroTrait.Changed += () =>
                Callable.From(ApplyTrait).CallDeferred();
            if (_hp != null)
                _hp.AddThemeStyleboxOverride("fill", EeSkin.HpFill);
            if (_mp != null)
                _mp.AddThemeStyleboxOverride("fill", EeSkin.MpFill);
            foreach (var t in new[] { _hpText, _mpText })
                if (t != null)
                {
                    EeSkin.Shadowed(t, 11);
                    t.HorizontalAlignment = HorizontalAlignment.Center;
                    t.VerticalAlignment = VerticalAlignment.Center;
                }
            foreach (var t in new[] { _lv, _jobLv })
                if (t != null)
                    EeSkin.Shadowed(t, 12, Color.Color8(225, 225, 220));
            if (_name != null)
            {
                EeSkin.Shadowed(_name, 12);
                _name.HorizontalAlignment = HorizontalAlignment.Center;
            }
            // Éléments contextuels (familier, chef, PK, arène, bouton
            // repli) : cachés tant que l'état n'est pas actif — la ref
            // n'en montre aucun.
            foreach (var leaf in new[] { "PetName", "PetHpBar",
                         "LeaderIcon", "pkIcon", "HideBtn",
                         "ArenaWaitingIcon" })
            {
                var n = Find<Control>(root, leaf);
                if (n != null) n.Visible = false;
            }
            _leader = Find<Control>(root, "LeaderIcon");
            // Couronne leader : reflète le roster 324 en continu.
            Protocol.PartyState.Changed += () =>
                SetPartyLeader(Protocol.PartyState.LeaderId != 0 &&
                    Protocol.PartyState.LeaderId ==
                        (Screens.SessionCarry.Character?.Id ?? 0));
            // Vague 2 : FateRoadBtn natif (Visible=False → montré
            // par code chez EE) → fenêtre chemin d'éveil.
            var fate = Find<BaseButton>(root, "FateRoadBtn");
            // Ref : pas de bouton visible à cet endroit (image native
            // absente → carré sombre) — Destiny Road reste accessible
            // par le menu Event.
            if (fate != null)
            {
                fate.Visible = false;
                fate.TooltipText = UiTextEn.Translate("Destiny Road");
                fate.Pressed += () => DestinyWindow.Instance?.Toggle();
            }
        }
        else
        {
            // Repli : cadre sombre + labels (uidata absent).
            var p = new Panel();
            p.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            p.Position = new Vector2(4, 40);
            p.Size = new Vector2(264, 110);
            p.AddThemeStyleboxOverride("panel", FallbackPanel());
            AddChild(p);
            _name = new Label { Text = "—", Position = new Vector2(70, 4) };
            _lv = new Label { Text = UiTextEn.Translate("Lv --"), Position = new Vector2(200, 4) };
            _hp = MakeBar(new Vector2(70, 30), new Color(0.8f, 0.15f, 0.15f));
            _mp = MakeBar(new Vector2(70, 52), new Color(0.15f, 0.4f, 0.85f));
            p.AddChild(_name); p.AddChild(_lv); p.AddChild(_hp); p.AddChild(_mp);
            _hpText = BarText(_hp); _mpText = BarText(_mp);
        }

        // Portrait 3D dans le slot « Face » du layout natif —
        // duplicata du corps joueur (meshes/matériaux partagés),
        // caméra serrée sur la tête comme chez EE.
        var face = Find<Control>(root, "Face");
        if (face != null) MountPortrait(face);

        // Grille 12 slots numérotés (raccourcis consommables natifs) —
        // 2 lignes × 6, cellule 26 px + chiffre, sous le bloc vitaux.
        // Ref : cases grises arrondies 21×19, pas 24×23, origine
        // (86, 83) — sous les barres HP/MP, alignées sur le bloc texte.
        var grid = new GridContainer { Columns = 6, Name = "QuickSlots" };
        grid.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        grid.Position = new Vector2(86, 83);
        grid.AddThemeConstantOverride("h_separation", 3);
        grid.AddThemeConstantOverride("v_separation", 4);
        for (var i = 0; i < 12; i++)
        {
            var cell = new Panel { CustomMinimumSize = new Vector2(21, 19) };
            cell.AddThemeStyleboxOverride("panel", EeSkin.KeyCell());
            var num = new Label
            {
                Text = (i + 1).ToString(),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            num.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            EeSkin.Shadowed(num, i < 9 ? 13 : 10);
            cell.AddChild(num);
            grid.AddChild(cell);
        }
        // Bouton « Events (daily rewards) » : sous la grille de slots,
        // dans la mise en page du bloc vitaux (grille = y 83..125).
        if (hud.Tex != null)
        {
            var ev = new TextureButton
            {
                Name = "EventsBtn",
                TooltipText = UiTextEn.Translate("Events (daily rewards)"),
                TextureNormal = hud.Tex.GetRegion(
                    new CeguiImageRef("Radar", "ColNormal")),
                TextureHover = hud.Tex.GetRegion(
                    new CeguiImageRef("Radar", "ColHover")),
                TexturePressed = hud.Tex.GetRegion(
                    new CeguiImageRef("Radar", "ColPress")),
                IgnoreTextureSize = true,
                StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
            };
            ev.CustomMinimumSize = new Vector2(24, 22);
            ev.Pressed += () =>
            {
                if (GetTree().Root.FindChild("DailyRewardWindow", true,
                        false) is Control c)
                    HudWindows.ToggleIn(c);
            };
            // 13e cellule de la grille : 3e rangée, sous les slots
            // (comptée dans la mise en page du bloc).
            grid.AddChild(ev);
        }
        AddChild(grid);

        SetVitals(0, 0, 0, 0);
    }

    private Node3D? _portraitHolder;

    /// <summary>Viewport portrait dans le slot Face : monde propre,
    /// lumière douce, caméra buste (~0,55 u devant la tête).</summary>
    private void MountPortrait(Control face)
    {
        var svc = new SubViewportContainer
        {
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        svc.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Ref : la tête est DANS le cercle du cadre — masque disque.
        svc.Material = new ShaderMaterial { Shader = CircleMask };
        var vp = new SubViewport
        {
            TransparentBg = true,
            OwnWorld3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Size = new Vector2I(96, 96),
        };
        svc.AddChild(vp);
        var stage = new Node3D();
        vp.AddChild(stage);
        stage.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.ClearColor,
                AmbientLightSource =
                    Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.9f, 0.88f, 0.84f),
                AmbientLightEnergy = 0.85f,
            },
        });
        stage.AddChild(new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-30f, -20f, 0f),
            LightColor = new Color(1f, 0.95f, 0.88f),
            LightEnergy = 0.75f,
            ShadowEnabled = false,
        });
        _portraitCam = new Camera3D
        {
            Current = true, Fov = 27f,
        };
        stage.AddChild(_portraitCam);
        // LookAtFromPosition : fonctionne hors arbre (LookAt exige
        // le nœud dans la scène — le stage n'y est pas encore).
        _portraitCam.LookAtFromPosition(new Vector3(0f, 1.5f, 0.6f),
            new Vector3(0f, 1.45f, 0f), Vector3.Up);
        _portraitHolder = new Node3D();
        stage.AddChild(_portraitHolder);
        face.AddChild(svc);
    }

    private Camera3D? _portraitCam;

    private static readonly Shader CircleMask = new()
    {
        Code = @"shader_type canvas_item;
void fragment() {
    vec4 c = texture(TEXTURE, UV);
    float d = distance(UV, vec2(0.5));
    c.a *= 1.0 - smoothstep(0.46, 0.5, d);
    COLOR = c;
}",
    };

    /// <summary>Alimente le portrait avec le corps du joueur — le
    /// duplicata partage meshes/matériaux (pas de rebuild ~7 s).</summary>
    public void SetPortrait(Node3D? bodySource)
    {
        if (_portraitHolder == null) return;
        foreach (var c in _portraitHolder.GetChildren()) c.QueueFree();
        if (bodySource?.Duplicate() is not Node3D dup) return;
        // Face +Z après conversion Z-up → la caméra à +z voit
        // le visage (même convention que CharacterPreview).
        dup.RotationDegrees = new Vector3(-90f, 0f, 0f);
        _portraitHolder.AddChild(dup);
        // Cadrage la frame suivante : les GlobalTransform ne sont à
        // jour qu'une fois le nœud dans l'arbre.
        Callable.From(() => FramePortrait(dup)).CallDeferred();
    }

    /// <summary>Cadre la caméra sur la tête réelle : haut de l'AABB
    /// <b>globale</b> des meshes visibles (fiable inter-races/poses —
    /// les AABB locales sont en coords natives Z-up).</summary>
    private void FramePortrait(Node3D dup)
    {
        if (!GodotObject.IsInstanceValid(dup) || _portraitCam == null)
            return;
        // Position de l'os tête du squelette (« Bip01 Head ») — les
        // AABB skinnées couvrent tout le corps et ne donnent pas la
        // hauteur du visage.
        Vector3? head = null;
        foreach (var sk in dup
                     .FindChildren("*", "Skeleton3D", true, false)
                     .Cast<Skeleton3D>())
        {
            var idx = sk.FindBone("Bip01 Head");
            if (idx < 0) continue;
            head = (sk.GlobalTransform *
                    sk.GetBoneGlobalPose(idx)).Origin;
            break;
        }
        if (head == null) return;
        // Ref TopLeftVitals.png : visage entier dans le cercle (front
        // → menton) — cadrage plus bas que l'os tête (sinon cheveux).
        _portraitCam.Position = head.Value +
            new Vector3(0f, 0.04f, 0.95f);   // B2 : reculé (0.62 trop zoomé)
        _portraitCam.LookAt(head.Value + new Vector3(0f, -0.01f, 0f),
            Vector3.Up);
    }

    /// <summary>Nom + niveau affichés sous le cadre portrait.</summary>
    public void SetIdentity(string name, int level)
    {
        if (_name != null) _name.Text = name;
        if (_lv != null) _lv.Text = level > 0 ? $"{level}" : "--";
    }

    /// <summary>Niveau de classe (JobLvText natif — le « 100 » du
    /// bloc vitaux chez EE).</summary>
    private PopupMenu? _portraitMenu;

    /// <summary>Règle de loot native — 1 Free, 2 Random, 3
    /// Taunt-Exclusive (textes serveur 208/207/2156).</summary>
    private static string LootRule(short mode) => mode switch
    {
        1 => "Free-for-all",
        2 => UiTextEn.Translate("Random"),
        3 => UiTextEn.Translate("Taunt-exclusive"),
        _ => $"Mode {mode}",
    };

    private void ShowPortraitMenu(Vector2 at)
    {
        _portraitMenu?.QueueFree();
        _portraitMenu = new PopupMenu();
        if (Protocol.PartyState.InTeam)
        {
            var self = Screens.SessionCarry.Character?.Id ?? 0;
            _portraitMenu.AddItem(UiTextEn.Translate("Party loot rules…"));
            var leader = Protocol.PartyState.LeaderId == self && self != 0;
            _portraitMenu.SetItemDisabled(0, !leader);
            if (!leader)
                _portraitMenu.SetItemTooltip(0, "Party: " +
                    LootRule(Protocol.PartyState.TeamMode));
        }
        else
        {
            _portraitMenu.AddItem(UiTextEn.Translate("Not in a party"));
            _portraitMenu.SetItemDisabled(0, true);
        }
        _portraitMenu.AddItem(UiTextEn.Translate("Reset dungeon"));
        _portraitMenu.IndexPressed += idx =>
        {
            if (idx == _portraitMenu.ItemCount - 1)
                Send(new Protocol.ZoneServerNodeGroupReset());
            else if (Protocol.PartyState.InTeam && idx == 0)
                OpenLootWindow();
        };
        AddChild(_portraitMenu);
        _portraitMenu.Position = (Vector2I)at;
        _portraitMenu.Popup();
    }

    private void Send(EdenClient.Net.NetMessage m)
    {
        if (_hud?.SendZoneAsync is { } s) _ = s(m);
    }

    // ---- fenêtre règles de loot (NewLootRuleWnd.layout natif) ----

    private Control? _lootWnd;
    private CheckBox? _lootRand, _lootTaunt,
        _rollWhite, _rollGreen, _rollBlue;

    /// <summary>Ouvre le dialogue natif de distribution (leader). Les
    /// radios reflètent l'état courant ; OK envoie TeamOperation 79
    /// op0 (loot rule) + op3 (roll rule) — retour 381 → PartyState.</summary>
    private void OpenLootWindow()
    {
        if (_lootWnd == null || !GodotObject.IsInstanceValid(_lootWnd))
            BuildLootWindow();
        if (_lootWnd == null) return;
        var tm = Protocol.PartyState.TeamMode;
        _lootRand?.SetPressedNoSignal(tm == 2);
        _lootTaunt?.SetPressedNoSignal(tm == 3);
        _rollWhite?.SetPressedNoSignal(Protocol.PartyState.RollRule == 0);
        _rollGreen?.SetPressedNoSignal(Protocol.PartyState.RollRule == 1);
        _rollBlue?.SetPressedNoSignal(Protocol.PartyState.RollRule == 2);
        HudWindows.Open(_lootWnd);
    }

    private void BuildLootWindow()
    {
        var w = _hud?.Build("NewLootRuleWnd.layout");
        if (w == null) return;
        _lootWnd = w;
        _lootWnd.Name = "LootRuleWnd";
        _lootWnd.Visible = false;
        _hud!.AddChild(_lootWnd);   // top-level : z-order propre
        DragHelper.EnsureTitleBar(_lootWnd, "Loot Rules");
        DragHelper.Persist(_lootWnd, "lootrule");
        SetText("LootText", UiTextEn.Translate("Field loot distribution"));
        SetText("RollText1", UiTextEn.Translate("Dungeon roll rule"));
        _lootRand = Find<CheckBox>(_lootWnd, "RandRadioBtn");
        _lootTaunt = Find<CheckBox>(_lootWnd, "onlyselfBtn");
        _rollWhite = Find<CheckBox>(_lootWnd, "WhiteBtn");
        _rollGreen = Find<CheckBox>(_lootWnd, "GreenBtn");
        _rollBlue = Find<CheckBox>(_lootWnd, "BlueBtn");
        if (_lootRand != null) _lootRand.Text = UiTextEn.Translate("Random");
        if (_lootTaunt != null) _lootTaunt.Text = UiTextEn.Translate("Taunt-exclusive");
        if (_rollWhite != null) _rollWhite.Text = UiTextEn.Translate("White quality");
        if (_rollGreen != null) _rollGreen.Text = UiTextEn.Translate("Green quality");
        if (_rollBlue != null) _rollBlue.Text = UiTextEn.Translate("Blue quality");
        // GroupID CEGUI non converti → exclusivité manuelle.
        RadioGroup(_lootRand, _lootTaunt);
        RadioGroup(_rollWhite, _rollGreen, _rollBlue);
        if (Find<BaseButton>(_lootWnd, "AcceptBtn") is { } ok)
        {
            if (ok is Button b1) b1.Text = UiTextEn.Translate("OK");
            ok.Pressed += ApplyLootRules;
        }
        if (Find<BaseButton>(_lootWnd, "ExitBtn") is { } ex)
        {
            if (ex is Button b2) b2.Text = UiTextEn.Translate("Close");
            ex.Pressed += () => HudWindows.Close(_lootWnd);
        }
    }

    private void SetText(string leaf, string text)
    {
        if (Find<Label>(_lootWnd, leaf) is { } l) l.Text = text;
    }

    private static void RadioGroup(params CheckBox?[] g)
    {
        foreach (var b in g)
        {
            if (b == null) continue;
            b.Toggled += on =>
            {
                if (!on) return;
                foreach (var o in g)
                    if (o != null && o != b) o.SetPressedNoSignal(false);
            };
        }
    }

    /// <summary>OK natif : envoie la règle cochée (op0) + le seuil de
    /// roll (op3). Le serveur ignore sans leadership.</summary>
    private void ApplyLootRules()
    {
        if (_lootRand?.ButtonPressed == true)
            Send(new Protocol.ZoneServerTeamOperation { Op = 0, Param1 = 2 });
        else if (_lootTaunt?.ButtonPressed == true)
            Send(new Protocol.ZoneServerTeamOperation { Op = 0, Param1 = 3 });
        var roll = _rollWhite?.ButtonPressed == true ? (short)0
            : _rollBlue?.ButtonPressed == true ? (short)2 : (short)1;
        Send(new Protocol.ZoneServerTeamOperation { Op = 3, Param1 = roll });
        HudWindows.Close(_lootWnd);
    }

    private TextureRect? _classIcon;
    private TextureRect? _traitIcon;

    private void ApplyTrait()
    {
        if (_traitIcon != null && GodotObject.IsInstanceValid(_traitIcon))
            _traitIcon.Texture = HeroTrait.Icon();
    }

    /// <summary>M12 : icône de la classe actuelle (slot classIcon du
    /// layout natif — JoBIamge%02d chez le natif).</summary>
    public void SetClassIcon(Texture2D? tex)
    {
        if (tex != null && _classIcon != null &&
            GodotObject.IsInstanceValid(_classIcon))
            _classIcon.Texture = tex;
    }

    public void SetJobLv(int classLv)
    {
        if (_jobLv != null)
            _jobLv.Text = classLv > 0 ? $"{classLv}" : "";
    }

    /// <summary>HP/MP courant/max — valeurs natives du 280/296.</summary>
    public void SetVitals(long hp, long maxHp, long mp, long maxMp)
    {
        SetBar(_hp, _hpText, hp, maxHp);
        SetBar(_mp, _mpText, mp, maxMp);
    }

    /// <summary>Couronne de chef de groupe (LeaderIcon natif).</summary>
    public void SetPartyLeader(bool leader)
    {
        if (_leader != null) _leader.Visible = leader;
    }

    private static void SetBar(ProgressBar? bar, Label? text,
        long cur, long max)
    {
        if (bar != null)
            bar.Value = max > 0 ? Math.Clamp((double)cur / max, 0, 1) : 0;
        if (text != null)
        {
            text.Text = $"{cur}/{max}";
            text.Visible = HudPrefs.ShowVitals;
        }
    }

    /// <summary>Pref « Vitals » : valeurs HP/MP numériques sur les
    /// barres (SetBar la réapplique à chaque mise à jour).</summary>
    private void HookVitalsPref()
    {
        void Apply()
        {
            if (!IsInstanceValid(this)) return;
            foreach (var t in new[] { _hpText, _mpText })
                if (t != null && IsInstanceValid(t))
                    t.Visible = HudPrefs.ShowVitals;
        }
        HudPrefs.Changed += Apply;
        TreeExiting += () => HudPrefs.Changed -= Apply;
        Apply();
    }

    private static ProgressBar MakeBar(Vector2 pos, Color fill, int w = 184)
    {
        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = 0,
            Position = pos, Size = new Vector2(w, 10),
            ShowPercentage = false,
        };
        bar.AddThemeStyleboxOverride("background", FallbackPanel());
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        return bar;
    }

    private static Label BarText(ProgressBar bar)
    {
        var l = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        l.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        l.AddThemeFontSizeOverride("font_size", 8);
        bar.AddChild(l);
        return l;
    }

    private static StyleBoxFlat FallbackPanel() => new()
    {
        BgColor = new Color(0.133f, 0.128f, 0.114f, 0.75f),
        BorderColor = new Color(0.468f, 0.45f, 0.401f),
        BorderWidthBottom = 1, BorderWidthTop = 1,
        BorderWidthLeft = 1, BorderWidthRight = 1,
    };

    /// <summary>FindChild par suffixe de nom CEGUI sanitisé —
    /// root nullable (les panneaux optionnels du layout passent
    /// directement leur Control?).</summary>
    internal static T? Find<T>(Node? root, string leaf) where T : class =>
        root?.FindChild($"*{leaf}*", true, false) as T;

    /// <summary>Sonde <c>--loot-test</c> : clic droit synthétique sur
    /// le portrait → menu groupe, puis ouverture du dialogue loot
    /// natif et clic OK (TeamOperation part — preuve U1 live,
    /// même injection que <see cref="HudWindows.EscProbe"/>).</summary>
    public sealed partial class LootProbe : Node
    {
        private readonly TopLeftVitals _v;
        private readonly string _shotsDir;
        private int _frame;

        public LootProbe(TopLeftVitals v)
        {
            _v = v; Name = "__loot_probe__";
            _shotsDir = ProjectSettings.GlobalizePath(
                "res://../godot-shots");
        }

        public override void _Process(double delta)
        {
            _frame++;
            // Loading overlay : le clic n'arme le menu que lorsque le
            // hover touche vraiment Face — on retente jusqu'à ouverture.
            if (_frame >= 80 && _frame <= 150 && _frame % 10 == 0 &&
                (_v._portraitMenu == null || !_v._portraitMenu.Visible))
                RightClickFace();
            switch (_frame)
            {
                case 160:
                    GD.Print($"[loot-test] menu ouvert, items=" +
                        $"{_v._portraitMenu?.ItemCount ?? -1}");
                    Shot("u1_portrait_menu");
                    _v._portraitMenu?.Hide();
                    break;
                case 170:
                    _v.OpenLootWindow();
                    GD.Print($"[loot-test] lootwnd open=" +
                        $"{_v._lootWnd?.IsVisibleInTree()}");
                    break;
                case 190:
                    Shot("u1_loot_rules");
                    _v.ApplyLootRules();   // OK natif → TeamOperation
                    GD.Print("[loot-test] OK cliqué → " +
                        "TeamOperation envoyé (refus attendu si solo)");
                    break;
                case 210:
                    // Reset dungeon : vrai aller-retour en solo —
                    // le serveur répond un texte (chat).
                    _v.Send(new Protocol.ZoneServerNodeGroupReset());
                    GD.Print("[loot-test] NodeGroupReset envoyé");
                    break;
                case 240:
                    Shot("u1_after_reset");
                    SetProcess(false);
                    break;
            }
        }

        private void RightClickFace()
        {
            var face = _v.FindChild("*Face*", true, false) as Control;
            if (face == null)
            {
                GD.PrintErr("[loot-test] Face introuvable");
                return;
            }
            var at = face.GetGlobalRect().GetCenter();
            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right, Pressed = true,
                Position = at, GlobalPosition = at,
            });
            Input.ParseInputEvent(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Right, Pressed = false,
                Position = at, GlobalPosition = at,
            });
        }

        private void Shot(string tag)
        {
            try
            {
                Directory.CreateDirectory(_shotsDir);
                RenderingServer.ForceDraw();
                GetViewport().GetTexture()?.GetImage()?.SavePng(
                    $"{_shotsDir}/{tag}.png");
            }
            catch (Exception e)
            {
                GD.PrintErr($"[loot-test] shot : {e.Message}");
            }
        }
    }
}
