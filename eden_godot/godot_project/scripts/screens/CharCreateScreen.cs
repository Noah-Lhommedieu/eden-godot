using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Game;
using EdenClient.Hud;
using EdenClient.Protocol;
using EdenClient.Ui;

namespace EdenClient.Screens;

/// <summary>
/// Création de personnage — wire natif (Eden.World/Program.cs) :
///   C→S 32 NameCheck → S→C 39 (code/flag)   — bouton « Vérifier »
///   C→S 4  CreateName → S→C 39            — réservation du nom
///   C→S 5  CreateData [u8 race][u8 class][u8 flag][32B app]
///         → S→C 38 liste fraîche
///   C→S 7  CancelCreate → S→C 41          — bouton Annuler
///
/// Le nibble « corps » du blob d'apparence (app[28]&gt;&gt;4) est le
/// code corps natif (FUN_0073ddc0 + validation serveur
/// CreateCharacterIntoDB @0x080a2170) — mapping prouvé par race dans
/// <see cref="CharCreateBodySpec"/> : Human 0/1, Zumi 2, Anuran 4,
/// Ursun 6, Halfkin 8/9 (codes 3/5/7 refusés par le serveur natif).
/// </summary>
public partial class CharCreateScreen : Control
{
    private ScreenManager Mgr => (ScreenManager)GetParent();
    private LineEdit? _name;
    private OptionButton? _race;
    private OptionButton? _gender;
    private OptionButton? _class;
    private Label? _status;
    // Pickers apparence ←/→ (valeur courante dans le Label central).
    private int _hair, _face, _hairColor, _skin, _eyes;
    private Label? _hairPicker, _facePicker, _hairColorPicker,
        _skinPicker, _eyesPicker;
    private bool _busy;
    private int _bodyCode = 1;

    /// <summary>Races natives (ordre du wire / page de classe :
    /// 1 Human, 2 Zumi, 3 Anuran, 4 Ursun, 5 Halfkin) —
    /// <see cref="CharCreateBodySpec.Races"/>. NB : C_Races.ini ne
    /// livre que l'humain → l'offre des 5 races est notre extension
    /// (le serveur natif valide seulement code corps + classe).</summary>
    private static readonly (int Id, string Name)[] Races =
        { (1, "Human"), (2, "Zumi"), (3, "Anuran"), (4, "Ursun"),
          (5, "Halfkin") };
    /// <summary>Classes de départ (T_Class.ini ids 1..5).</summary>
    private static readonly int[] StartClasses = { 1, 2, 3, 4, 5 };

    private Dictionary<int, ClassTextTable.ClassText>? _classText;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Avatar posé dans le monde sur CharMarker (plan buste
        // [Create] ini) — même approche que CharSelectScreen : le
        // perso vit dans le temple, pas dans un SubViewport flottant.
        TempleBackdrop.Attach(this, TempleBackdrop.Shot.Create);

        var path = AssetCatalog.ResolveRawAsset("data/db/T_Class.ini");
        if (path != null)
            _classText = ClassTextTable.Load(path);

        // Panneau droit ~232 px natif (CreateCharacterWndVer2_2 :
        // CharacterProperty {1,-232},{0,0}→{0,576}) — largeur fixe
        // ~300 px, hauteur au contenu (pas de vide en bas).
        var panel = new Panel();
        panel.AnchorLeft = 1f;
        panel.AnchorRight = 1f;
        panel.AnchorTop = 0f;
        panel.AnchorBottom = 0f;
        panel.OffsetLeft = -312; panel.OffsetRight = -12;
        panel.OffsetTop = 12; panel.OffsetBottom = 536;
        panel.CustomMinimumSize = new Vector2(300, 524);
        // Chrome natif + fond sombre — le panneau flottait transparent
        // sur le temple (« trous » entre les contrôles).
        if (Mgr.Ui != null && Mgr.Tex != null)
        {
            DragHelper.ApplyNativeChrome(panel, Mgr.Ui, Mgr.Tex);
            DragHelper.FillClientArea(panel,
                new Color(0.05f, 0.055f, 0.08f, 0.94f));
        }
        AddChild(panel);
        var vb = new VBoxContainer();
        vb.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        vb.OffsetLeft = 16; vb.OffsetRight = -16;
        vb.OffsetTop = 12; vb.OffsetBottom = -12;
        vb.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vb);

        vb.AddChild(new Label { Text = UiTextEn.Translate("New character"),
            HorizontalAlignment = HorizontalAlignment.Center });
        _name = new LineEdit { PlaceholderText = UiTextEn.Translate("Name (2-14 chars)"),
            MaxLength = 14 };
        vb.AddChild(_name);
        vb.AddChild(new HSeparator());
        _race = new OptionButton();
        foreach (var r in Races) _race.AddItem($"{r.Id} — {r.Name}");
        vb.AddChild(_race);
        _gender = new OptionButton();
        _gender.AddItem(UiTextEn.Translate("Girl"), 0); _gender.AddItem(UiTextEn.Translate("Boy"), 1);
        _gender.Selected = 1;
        vb.AddChild(_gender);
        _class = new OptionButton();
        foreach (var id in StartClasses)
            _class.AddItem(_classText?.GetValueOrDefault(id)?.Name
                           is { Length: > 0 } n ? n : $"Classe {id}");
        vb.AddChild(_class);
        vb.AddChild(new HSeparator());
        // Pickers d'apparence ←/→ — valeurs mappées dans le blob 32B
        // via AppearanceCodec (hair[0:6], face[0:6], hair_color,
        // skin_color, eyes_color — bitfields natifs). Bornes larges
        // initiales — resserrées par code corps dans ApplyBounds().
        _hairPicker = MakeCycler(vb, "Hairstyle", 0, 31);
        _facePicker = MakeCycler(vb, "Face", 0, 7);
        _hairColorPicker = MakeCycler(vb, "Hair color", 0, 7);
        _skinPicker = MakeCycler(vb, "Skin", 0, 5);
        _eyesPicker = MakeCycler(vb, "Eyes", 0, 7);
        vb.AddChild(new HSeparator());
        BuildTraitPicker(vb);
        vb.AddChild(new HSeparator());
        _status = new Label { Text = "", ClipText = true };
        vb.AddChild(_status);
        var chk = new Button { Text = UiTextEn.Translate("Check name"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        ScreenStyle.DarkButton(chk);
        chk.Pressed += OnCheck;
        vb.AddChild(chk);
        var btns = new HBoxContainer();
        btns.AddThemeConstantOverride("separation", 6);
        var ok = new Button { Text = UiTextEn.Translate("Create"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        ScreenStyle.DarkButton(ok, ScreenStyle.GoldBorder);
        ok.Pressed += OnCreate;
        var cancel = new Button { Text = UiTextEn.Translate("Back"),
            SizeFlagsHorizontal = SizeFlags.ExpandFill };
        ScreenStyle.DarkButton(cancel);
        cancel.Pressed += OnBack;
        btns.AddChild(ok); btns.AddChild(cancel);
        vb.AddChild(btns);

        // Skin sombre cohérent HUD sur champs/listes/labels natifs.
        if (Mgr.Ui != null)
            CeguiToGodot.SkinUnstyled(panel);

        _race.ItemSelected += _ => RefreshModel();
        _gender.ItemSelected += _ => RefreshModel();
        _class.ItemSelected += _ => RefreshModel();   // tenue de la classe
        // QA : --create-race=<1-5> --create-gender=<0|1> présélectionne
        // la combinaison (vérifie les corps non par défaut, ex. la
        // fille humaine ou le halfkin, sans cliquer).
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--create-race=") &&
                int.TryParse(a["--create-race=".Length..], out var ri))
            {
                var idx = Array.FindIndex(Races, r => r.Id == ri);
                if (idx >= 0) _race.Selected = idx;
            }
            else if (a.StartsWith("--create-gender=") &&
                     int.TryParse(a["--create-gender=".Length..],
                         out var gi))
                _gender.Selected = gi;
            else if (a.StartsWith("--create-trait=") &&
                     int.TryParse(a["--create-trait=".Length..],
                         out var ti))
                SelectTrait(ti);
            else if (a.StartsWith("--create-skin=") &&
                     int.TryParse(a["--create-skin=".Length..],
                         out var si))
                _skin = si;
            else if (a.StartsWith("--create-hair=") &&
                     int.TryParse(a["--create-hair=".Length..], out var hi))
                _hair = hi;
            else if (a.StartsWith("--create-face=") &&
                     int.TryParse(a["--create-face=".Length..], out var fi))
                _face = fi;
            else if (a.StartsWith("--create-haircolor=") &&
                     int.TryParse(a["--create-haircolor=".Length..], out var hci))
                _hairColor = hci;
            else if (a.StartsWith("--create-eyes=") &&
                     int.TryParse(a["--create-eyes=".Length..], out var ei))
                _eyes = ei;
        }
        RefreshModel();
    }

    /// <summary>Bornes max par cycler — réécrites par
    /// <see cref="ApplyBodyRules"/> selon le code corps courant
    /// (Ursun = limites natives plus strictes).</summary>
    private readonly Dictionary<string, int> _bounds = new();

    /// <summary>Ligne picker « label ‹ valeur › » — boutons ←/→
    /// cyclant 0..max ; chaque changement rafraîchit la prévisu.</summary>
    private Label MakeCycler(VBoxContainer vb, string title,
        int min, int max)
    {
        _bounds[title] = max;
        var hr = new HBoxContainer();
        var name = new Label { Text = title,
            CustomMinimumSize = new Vector2(130, 0) };
        var minus = new Button { Text = "←" };
        var value = new Label { Text = "0",
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(30, 0) };
        var plus = new Button { Text = "→" };
        int Get() => title switch
        {
            "Hairstyle" => _hair, "Face" => _face,
            "Hair color" => _hairColor, "Skin" => _skin,
            _ => _eyes,
        };
        void Set(int v)
        {
            var lim = _bounds.TryGetValue(title, out var m) ? m : max;
            v = v > lim ? min : v < min ? lim : v;
            switch (title)
            {
                case "Hairstyle": _hair = v; break;
                case "Face": _face = v; break;
                case "Hair color": _hairColor = v; break;
                case "Skin": _skin = v; break;
                default: _eyes = v; break;
            }
            value.Text = v.ToString();
            RefreshModel();
        }
        minus.Pressed += () => Set(Get() - 1);
        plus.Pressed += () => Set(Get() + 1);
        hr.AddChild(name); hr.AddChild(minus);
        hr.AddChild(value); hr.AddChild(plus);
        vb.AddChild(hr);
        return value;
    }

    // ---- Trait héroïque (A4.5) -------------------------------------
    /// <summary>Les 8 traits dans l'ordre natif (enchants 52001-52008,
    /// bannières <c>NatrueIamge01-08</c>) : nom EN, bonus EN — textes
    /// traduits via <see cref="UiTextEn"/> (guide
    /// <c>traits-heroiques</c>).</summary>
    public static readonly (string Name, string Bonus)[] Traits =
    {
        ("Leadership", "HP and MP +12%"),
        ("Guardian", "DEF +10%, all Elem. Resistances +5%"),
        ("Apostle", "INT +10%, M-ATK +5%"),
        ("Commando", "STR +10%, P-ATK +5%"),
        ("Sanctum", "WIS +15%, G-Healing +5%"),
        ("Maverick", "AGI +10%, ATK SPD +5%"),
        ("Adventurer", "LCK +10%, CRIT DMG +10%"),
        ("Crack Shot", "LCK +10%, M-CRIT DMG +10%"),
    };

    private int _trait;
    private Label? _traitDesc;
    private readonly List<Button> _traitBtns = new();

    /// <summary>Octet « Feature » du wire NCID 5 : 0..7 (natif :
    /// ≥ 8 refusé par CreateCharacterIntoDB). Défaut = 0 (Leadership).
    /// </summary>
    public byte TraitFeature => HeroTraitWire.FeatureFor(_trait);

    private void BuildTraitPicker(VBoxContainer vb)
    {
        vb.AddChild(new Label { Text = UiTextEn.Translate("Heroic trait") });
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 4);
        var group = new ButtonGroup();
        for (var i = 0; i < Traits.Length; i++)
        {
            var idx = i;
            var b = new Button
            {
                ToggleMode = true, ButtonGroup = group,
                CustomMinimumSize = new Vector2(34, 34),
                ExpandIcon = true, FocusMode = FocusModeEnum.None,
                Icon = AssetCatalog.GetTexture(
                    $"UI/uiicon/NatrueIamge{i + 1:00}.png"),
                TooltipText = UiTextEn.Translate(Traits[i].Name),
                ButtonPressed = i == _trait,
            };
            b.Pressed += () => SelectTrait(idx);
            row.AddChild(b);
            _traitBtns.Add(b);
        }
        vb.AddChild(row);
        _traitDesc = new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(0, 40),
        };
        vb.AddChild(_traitDesc);
        SelectTrait(_trait);
    }

    private void SelectTrait(int i)
    {
        _trait = Math.Clamp(i, 0, Traits.Length - 1);
        for (var k = 0; k < _traitBtns.Count; k++)
            _traitBtns[k].SetPressedNoSignal(k == _trait);
        if (_traitDesc != null)
            _traitDesc.Text = UiTextEn.Translate(Traits[_trait].Name)
                + " — " + UiTextEn.Translate(Traits[_trait].Bonus);
    }

    private int RaceId => Races[_race?.Selected ?? 0].Id;

    /// <summary>Code corps natif pour (race, genre) — table prouvée
    /// <see cref="CharCreateBodySpec"/> : Human 0g/1b, Zumi 2m,
    /// Anuran 4f, Ursun 6k, Halfkin 8l/9c ; les races à corps unique
    /// ignorent le genre (3/5/7 rejetés par le serveur natif).</summary>
    private int CurrentBodyCode() =>
        CharCreateBodySpec.BodyCode(RaceId,
            (_gender?.Selected ?? 1) == 1);

    /// <summary>Repliant les bornes des cyclers sur le code corps
    /// courant (Ursun = limites strictes natives) et grise le genre
    /// pour les races à corps unique.</summary>
    private void ApplyBodyRules()
    {
        var code = CurrentBodyCode();
        var b = CharCreateBodySpec.BoundsFor(code);
        _bounds["Hairstyle"] = b.HairMax;
        _bounds["Face"] = b.FaceMax;
        _bounds["Hair color"] = b.HairColorMax;
        _bounds["Skin"] = b.SkinMax;
        _bounds["Eyes"] = b.EyesMax;
        if (_hair > b.HairMax) _hair = b.HairMax;
        if (_face > b.FaceMax) _face = b.FaceMax;
        if (_hairColor > b.HairColorMax) _hairColor = b.HairColorMax;
        if (_skin > b.SkinMax) _skin = b.SkinMax;
        if (_eyes > b.EyesMax) _eyes = b.EyesMax;
        if (_hairPicker != null) _hairPicker.Text = _hair.ToString();
        if (_facePicker != null) _facePicker.Text = _face.ToString();
        if (_hairColorPicker != null)
            _hairColorPicker.Text = _hairColor.ToString();
        if (_skinPicker != null) _skinPicker.Text = _skin.ToString();
        if (_eyesPicker != null) _eyesPicker.Text = _eyes.ToString();
        var gendered = true;
        foreach (var r in CharCreateBodySpec.Races)
            if (r.WireId == RaceId) gendered = r.Gendered;
        if (_gender != null) _gender.Disabled = !gendered;
    }

    private byte[] Blob() => AppearanceCodec.Encode(
        new AppearanceCodec.Appearance(
            BodyCode: CurrentBodyCode(),
            HairStyle: _hair, FaceStyle: _face, HairColor: _hairColor,
            SkinColor: _skin, EyesColor: _eyes, EyesColor2: 0));

    private int _buildGen;
    private Node3D? _avatar;
    /// <summary>Vrai quand l'avatar de fond a été attaché —
    /// sert aux captures QA pour attendre le modèle réel.</summary>
    public bool AvatarReady { get; private set; }

    private void RefreshModel()
    {
        ApplyBodyRules();
        // Positions [Create] ini par race (Kuma posé plus bas,
        // Frog décalé) — Attach initial ne connaît pas la race.
        TempleBackdrop.Shared.ApplyShot(
            TempleBackdrop.Shot.Create, RaceId);
        var gen = ++_buildGen;
        var app = Blob();
        // KB-10 : tenue de base de la classe choisie (plus « 202 »).
        var classNow = StartClasses[_class?.Selected ?? 0];
        // Corps en tâche de fond (meshes/GLB détachés OK worker) ;
        // l'animator C# est rattaché sur le thread principal dans
        // AttachAvatar (EntityAnimator/AnimationPlayer interdits
        // hors thread — crash TieManagedToUnmanaged sinon).
        Task.Run(() =>
        {
            try
            {
                // CharBodyCache : ré-changer de race/classe réaffiche
                // en ~50 ms quand le proto est déjà construit.
                var body = CharBodyCache.GetOrBuild(app);
                if (body == null)
                {
                    GD.PrintErr("[create] BuildBody null");
                    return;
                }
                body.RotationDegrees = new Vector3(-90f, 180f, 0f);
                _ = CharDressing.Apply(body, app, null, null, classNow, 0);
                Callable.From(() =>
                    AttachAvatarAnimated(gen, body, app))
                        .CallDeferred();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[create] avatar build: {ex}");
            }
        });
    }

    private void AttachAvatarAnimated(int gen, Node3D body,
        byte[]? app)
    {
        CharVisual.AttachAnimator(body, CharModelSpec.BodyStem(app));
        CharVisual.PlayIdle(body);
        AttachAvatar(gen, body);
    }

    private void AttachAvatar(int gen, Node3D? body)
    {
        // L'écran peut avoir été libéré pendant le build async
        // (capture --shot, retour) — ne pas toucher un objet disposé.
        if (!GodotObject.IsInstanceValid(this))
        {
            body?.QueueFree();
            return;
        }
        if (gen != _buildGen || body == null)
        {
            body?.QueueFree();
            return;
        }
        var marker = TempleBackdrop.Shared.CharMarker;
        if (!GodotObject.IsInstanceValid(marker)) return;
        foreach (var child in marker.GetChildren())
            if (child is not OmniLight3D)
                child.QueueFree();
        _avatar = body;
        marker.AddChild(body);
        AvatarReady = true;
        // Cadrage sur la taille réelle de l'avatar : le plan fixe
        // 4,4u validé pour l'humain coupe les grandes races (Ursun)
        // et laisse les petites lointaines (Zumi). Différé d'une
        // frame — les GlobalTransform se propagent au prochain tick.
        Callable.From(() =>
            TempleBackdrop.Shared.FitCreateCamera(body))
                .CallDeferred();
    }

    private void Status(string t, bool err = false)
    {
        if (_status == null) return;
        _status.Text = t;
        _status.AddThemeColorOverride("font_color",
            err ? new Color(1f, 0.4f, 0.35f) : new Color(0.5f, 0.9f, 0.5f));
    }

    /// <summary>Codes natifs CNC_CW_ClientVerifyCharacterName.</summary>
    private static string CodeText(short code) => code switch
    {
        0 => UiTextEn.Translate("OK"),
        3 => "Name already taken",
        4 => "Name reserved by another",
        5 => "3 characters max",
        6 => "Creation already in progress",
        8 => "Name too long",
        9 => "Name too short (min 2)",
        10 => "Illegal characters (letters/digits)",
        _ => $"Error {code}",
    };

    private async void OnCheck()
    {
        if (_busy || Mgr.Session == null) return;
        _busy = true;
        try
        {
            var (code, _) = await Mgr.Session
                .VerifyCharacterNameAsync(_name?.Text.Trim() ?? "");
            Status(CodeText(code), code != 0);
        }
        catch (Exception ex) { Status(ex.Message, true); }
        _busy = false;
    }

    private async void OnCreate()
    {
        if (_busy || Mgr.Session == null) return;
        var name = _name?.Text.Trim() ?? "";
        _busy = true;
        try
        {
            var (code, _) = await Mgr.Session.CreateCharacterNameAsync(name);
            if (code != 0) { Status(CodeText(code), true); _busy = false; return; }
            var classId = (byte)StartClasses[_class?.Selected ?? 0];
            var (rej, _) = await Mgr.Session.CreateCharacterDataAsync(
                (byte)RaceId, classId, TraitFeature, Blob());
            if (rej != 0)
            {
                Status(CodeText(rej), true);
                _busy = false;
                return;
            }
            Mgr.ShowDeferred(new CharSelectScreen());
        }
        catch (Exception ex) { Status(ex.Message, true); }
        _busy = false;
    }

    /// <summary>Pilote QA <c>--create=&lt;nom&gt;</c> : vérifie le nom
    /// (NCID 32), le réserve (NCID 4) puis envoie les données
    /// (NCID 5) — la liste fraîche ramène au sélecteur.</summary>
    public async void AutoCreate(string name)
    {
        if (Mgr.Session == null)
        {
            GD.PrintErr("[auto] create : pas de session");
            return;
        }
        _name!.Text = name;
        var (code, _) = await Mgr.Session.VerifyCharacterNameAsync(name);
        GD.Print($"[auto] check '{name}' → {code} ({CodeText(code)})");
        if (code != 0) return;
        var (code2, _) = await Mgr.Session.CreateCharacterNameAsync(name);
        GD.Print($"[auto] reserve '{name}' → {code2}");
        if (code2 != 0) return;
        var classId = (byte)StartClasses[_class?.Selected ?? 0];
        var (rej, n) = await Mgr.Session.CreateCharacterDataAsync(
            (byte)RaceId, classId, TraitFeature, Blob());
        GD.Print($"[auto] create → {n} perso(s) en liste rej={rej}");
        if (rej != 0) return;
        Mgr.ShowDeferred(new CharSelectScreen());
    }

    private async void OnBack() => await Back();

    /// <summary>Annulation création (NCID 7 → attente 41) puis retour
    /// au sélecteur — exposé pour le pilote QA
    /// <c>--create-back</c>.</summary>
    public async Task Back()
    {
        if (Mgr.Session != null)
            await Mgr.Session.CancelCreateCharacterAsync();
        Mgr.ShowDeferred(new CharSelectScreen());
    }
}
