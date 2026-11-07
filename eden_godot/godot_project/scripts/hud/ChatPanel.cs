using Godot;
using EdenClient.Data;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre de chat bas-gauche — instancie le layout natif
/// <c>ChannelWndV2.layout</c> (MainWnd ChannelWindow + InputWnd avec
/// EditBox et les 9 boutons-canal + EmojiPalette).
///
/// Le StaticText natif de MainWnd est remplacé par un RichTextLabel
/// scrollable (canaux colorés, préfixe <c>&lt;Peer&gt;</c> mauve…) ;
/// l'EditBox envoie via <see cref="SendChat"/> (callback posé par
/// HudRoot → GameScene, NCID 34).
/// </summary>
public partial class ChatPanel : Control
{
    /// <summary>Canaux des boutons natifs (ordre des ChannelBtn) —
    /// ids du wire : 0 say, 1 peer/whisper, 2 party, 3 guild,
    /// 4 zone/shout, 5 world, 6 buzz.</summary>
    private static readonly (string Label, byte Id)[] Channels =
    {
        ("Say", 0), ("Peer", 1), ("Party", 2), ("Guild", 3),
        ("Zone", 4), ("World", 5), ("Buzz", 6),
    };

    private static readonly Color[] ChannelColors =
    {
        new(1f, 1f, 1f),        // Say — blanc
        new(0.85f, 0.5f, 0.9f), // Peer — mauve
        new(0.45f, 0.7f, 1f),   // Party — bleu
        new(0.45f, 0.9f, 0.5f), // Guild — vert
        new(1f, 0.75f, 0.35f),  // Zone — orange
        new(0.45f, 0.95f, 0.95f), // World — cyan
        new(1f, 0.55f, 0.55f),  // Buzz — rouge clair
        new(1f, 0.33f, 0.28f), // System — rouge (ref ChatPanel-Peer), éclairci : lisible sur fond sombre
    };
    /// <summary>Palette centralisée : timestamp et fond du log
    /// (sombre semi-opaque comme le ref — tous les textes de canal
    /// sont clairs et restent lisibles quel que soit le thème).</summary>
    private const string TimestampHex = "#b4b4b4";
    private static readonly Color LogBackdrop = new(0, 0, 0, 0.55f);
    private static readonly Color LogOutline = new(0, 0, 0, 0.9f);

    private static readonly string[] ChannelPrefix =
        { "Say", "Peer", "Party", "Guild", "Zone", "World", "Buzz", "" };

    private RichTextLabel? _log;
    private LineEdit? _input;
    private Control? _emojiPalette;
    private CheckBox?[] _channelBtns =
        new CheckBox?[Channels.Length];
    private byte _channel = 0;
    /// <summary>Destinataire du message privé (canal 1 + cible).</summary>
    private string _whisperTo = "";

    // ---- C5 : onglets + timestamps -------------------------------
    /// <summary>Un message reçu (canal de <see cref="ChannelColors"/>,
    /// 7 = système).</summary>
    private readonly record struct ChatEntry(int Channel,
        string Sender, string Text, string Ts);

    /// <summary>Historique commun (cap 300) — re-rendu au switch
    /// d'onglet / toggle timestamps.</summary>
    private readonly List<ChatEntry> _history = new();
    private const int HistoryCap = 300;
    /// <summary>true = onglet « Battle » (système/combat seulement) ;
    /// false = « Normal » (tout). Natif EE : 2 onglets.</summary>
    private bool _battleTab;
    /// <summary>Préfixe [HH:mm] — togglé par le bouton ⚙ natif.</summary>
    private bool _timestamps;

    /// <summary>(canal, cible, texte) → envoi réseau ou écho local.</summary>
    public event Action<byte, string, string>? SendChat;

    /// <summary>true tant que la saisie a le focus clavier.</summary>
    public bool InputFocused => _input?.HasFocus() == true;

    public void Build(HudRoot hud)
    {
        _hud = hud;
        var root = hud.Build("ChannelWndV2.layout");
        if (root == null)
        {
            BuildFallback();
            return;
        }
        AddChild(root);
        // Panneau racine plein écran : les ancres bas-gauche des enfants se résolvent alors
        // sur le viewport (sa géométrie native dérivait après le premier frame : log et
        // saisie remontaient de ~10 px sous les onglets).
        HudRoot.Pin(root, 0, 0, 1, 1, 0, 0, 0, 0);

        // Recadrage explicite — les UnifiedAreaRect natifs portent
        // une position sauvegardée à une autre résolution : MainWnd
        // faisait 0.712×H de HAUTEUR en plus de son y (≈620 px en
        // 720p, d'où le texte coupé à mi-écran) ; InputWnd idem.
        // Colonne bas-gauche native : log → onglets → saisie → barre
        // EXP/Soul (FucntionBarWnd, épinglée par HotbarPanel).
        // Ref ChatPanel.png / juste le jeu.png (1280×720, ancré bas) :
        // onglets y H-278..H-262, log H-262..H-110 (404 px de large,
        // fenêtre redimensionnée chez EE), saisie H-110..H-52.
        var main = TopLeftVitals.Find<Control>(root, "MainWnd");
        if (main != null)
            HudRoot.Pin(main, 0, 1, 0, 1, 2, -262, 406, -110);
        var input = TopLeftVitals.Find<Control>(root, "InputWnd");
        if (input != null)
            HudRoot.Pin(input, 0, 1, 0, 1, 2, -110, 406, -52);

        // Onglets natifs (TabControl « xMain » + label « xSub ») →
        // deux vrais onglets DejaVu TabButton en haut-gauche du log.
        foreach (var n in new[] { "MainTab", "SubTitle" })
        {
            var c = TopLeftVitals.Find<Control>(root, n);
            if (c != null) c.Visible = false;
        }
        // Texte SOURCE (EN) : AutoTranslate traduit une seule fois ; passer le texte déjà
        // traduit le faisait re-traduire (« Combat » est aussi une clé du dictionnaire).
        _tabNormal = MakeTab("Normal", 2, 52, false);
        _tabBattle = MakeTab("Battle", 54, 98, true);
        AddChild(_tabNormal);
        AddChild(_tabBattle);
        StyleTabs();
        // « + » (ExtendBtn) puis « Settings » en haut-droite.
        var ext = TopLeftVitals.Find<Control>(root, "ExtendBtn");
        if (ext != null)
        {
            HudRoot.Pin(ext, 0, 1, 0, 1, 344, -279, 362, -262);
        }
        var setBtn = TopLeftVitals.Find<Control>(root, "SettingBtn");
        if (setBtn != null)
        {
            HudRoot.Pin(setBtn, 0, 1, 0, 1, 362, -278, 406, -262);
        }

        // Le StaticText « 123… » natif est caché — notre log le couvre.
        var stub = TopLeftVitals.Find<Label>(main, "StaticText");
        if (stub != null) stub.Visible = false;
        if (main != null)
        {
            _log = new RichTextLabel
            {
                Name = "__chatlog",
                BbcodeEnabled = true,
                ScrollFollowing = true,
                // Pass : la molette fait défiler le log (les clics
                // retombent sur la fenêtre native en dessous).
                MouseFilter = MouseFilterEnum.Pass,
            };
            _log.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            ApplyChatFont();
            HudPrefs.Changed -= ApplyChatFont;
            HudPrefs.Changed += ApplyChatFont;
            _log.AddThemeColorOverride("font_outline_color", LogOutline);
            _log.AddThemeConstantOverride("outline_size", 2);
            // Ref : fond sombre semi-opaque + texte rentrant de ~5 px
            // (sinon le premier caractère mord le bord de fenêtre).
            var back = new Panel { MouseFilter = MouseFilterEnum.Ignore };
            back.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                { BgColor = LogBackdrop });
            back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            main.AddChild(back);
            // main peut être encore en setup (warning move_child) →
            // différé : back repasse sous _log fin de frame.
            main.CallDeferred(Node.MethodName.MoveChild, back, 0);
            main.AddChild(_log);
            _log.OffsetLeft = 5;
            _log.OffsetRight = -4;
        }

        // Fenêtre secondaire au-dessus (SubWnd) — masquée en v1, la
        // capture ne montre qu'une seule zone de texte.
        var sub = TopLeftVitals.Find<Control>(root, "SubWnd");
        if (sub != null) sub.Visible = false;
        var tip = TopLeftVitals.Find<Control>(root, "TipItem");
        if (tip != null) tip.Visible = false;

        var setting = TopLeftVitals.Find<Button>(root, "SettingBtn");
        if (setting != null)
        {
            setting.Text = UiTextEn.Translate("Settings");
            setting.AddThemeFontSizeOverride("font_size", 11);
            // Timestamps optionnels : toggle par le ⚙ natif.
            setting.TooltipText = UiTextEn.Translate("Toggle timestamps");
            setting.Pressed += () =>
            {
                _timestamps = !_timestamps;
                RenderAll();
            };
        }
        // Placeholders texte du layout (« xMain », « fd », « 123… »).
        // « xMain » = caption du pane MainTab/All (DefaultWindow →
        // label fils __text généré par CeguiToGodot).
        var all = TopLeftVitals.Find<Control>(root, "MainTab_All");
        var allTxt = TopLeftVitals.Find<Label>(all, "__text");
        if (allTxt != null) allTxt.Text = "";
        {
            var l = TopLeftVitals.Find<Label>(root, "StaticText");
            if (l != null && l.Text.Trim().Length <= 5)
                l.Text = "";
        }

        // Saisie : LineEdit natif de l'InputWnd.
        _input = TopLeftVitals.Find<LineEdit>(root, "EditBox");
        if (_input != null)
        {
            _input.Text = "";
            _input.PlaceholderText = "";
            // Ref : zone de saisie sombre intégrée au bandeau (pas de
            // champ blanc) — fond translucide, texte blanc.
            var ib = new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0.25f),
                ContentMarginLeft = 4,
            };
            _input.AddThemeStyleboxOverride("normal", ib);
            _input.AddThemeStyleboxOverride("focus", ib);
            _input.AddThemeColorOverride("font_color", Colors.White);
            _input.TextSubmitted += OnSubmit;
        }

        // Boutons-canal → libellés anglais de la capture (Say…Buzz) ;
        // les 2 derniers (Lover/Battlefield natifs) sont masqués.
        var idx = 0;
        foreach (var (label, id) in Channels)
        {
            var b = TopLeftVitals.Find<CheckBox>(root,
                $"ChannelBtn{IndexToBtn(idx)}");
            if (b == null) { idx++; continue; }
            b.Text = UiTextEn.Translate(label);
            b.Alignment = HorizontalAlignment.Center;
            // Ref : 7 boutons jointifs entre ◀ (x 6..22) et ▶ (x
            // 384..400) — la grille native (9 cases, Btn9 hors cadre)
            // laissait un trou à la place de Battlefield.
            var w = (382f - 22f) / Channels.Length;
            HudRoot.Pin(b, 0, 0.522727f, 0, 0.954545f,
                22 + idx * w, 0, 22 + (idx + 1) * w, 0);
            foreach (var icon in new[] { "checked", "unchecked",
                         "radio_checked", "radio_unchecked",
                         "checked_disabled", "unchecked_disabled",
                         "radio_checked_disabled",
                         "radio_unchecked_disabled" })
                b.AddThemeIconOverride(icon, NoIcon);
            // Fond natif main:ChannelEnable/Hover/Disable (le canal
            // actif = bouton enfoncé plus sombre, ref « Say »).
            foreach (var (st, img) in new[] { ("normal", "ChannelEnable"),
                         ("hover", "ChannelHover"),
                         ("pressed", "ChannelDisable"),
                         ("hover_pressed", "ChannelDisable") })
                if (hud.Region("main", img) is { } reg)
                    b.AddThemeStyleboxOverride(st, new StyleBoxTexture
                    {
                        Texture = reg,
                        TextureMarginLeft = 4, TextureMarginRight = 4,
                        TextureMarginTop = 4, TextureMarginBottom = 4,
                    });
            b.AddThemeFontSizeOverride("font_size", 12);
            b.AddThemeColorOverride("font_color", Color.Color8(200, 200, 200));
            b.AddThemeColorOverride("font_pressed_color", Colors.White);
            var captured = id;
            b.Toggled += on =>
            {
                if (!on) return;
                _channel = captured;
                _whisperTo = "";
                if (_input != null) _input.PlaceholderText = "";
            };
            _channelBtns[idx] = b;
            idx++;
        }
        // Radio exclusif, « Say » actif au départ (ref).
        var group = new ButtonGroup();
        for (var i = 0; i < Channels.Length; i++)
            if (TopLeftVitals.Find<CheckBox>(root,
                    $"ChannelBtn{IndexToBtn(i)}") is { } rb)
            {
                rb.ButtonGroup = group;
                if (i == 0) rb.ButtonPressed = true;
            }
        // Btn2 « Mutually » (même rect que Btn3) et Btn8
        // « Battlefield » ne font pas partie de la rangée ref.
        foreach (var hidden in new[] { 2, 8 })
        {
            var b = TopLeftVitals.Find<CheckBox>(root,
                $"ChannelBtn{hidden}");
            if (b != null) b.Visible = false;
        }

        // Emoji → palette du layout.
        _emojiPalette = TopLeftVitals.Find<Control>(root, "EmojiPalette");
        if (_emojiPalette != null) _emojiPalette.Visible = false;
        var emoji = TopLeftVitals.Find<Button>(root, "EmojiOpenBtn");
        if (emoji != null)
        {
            emoji.Text = UiTextEn.Translate("Emoji");
            emoji.Pressed += () =>
            {
                if (_emojiPalette != null)
                    _emojiPalette.Visible = !_emojiPalette.Visible;
            };
        }

        AddSystem(UiTextEn.Translate("Chat ready — Say channel."));
        // Le layout CEGUI et la taille de fenêtre se stabilisent après le premier frame :
        // les offsets pixels du bandeau (onglets, +, Settings) sont alors écrasés et les
        // quatre boutons se retrouvaient empilés à gauche. On les ré-épingle ensuite.
        Callable.From(() =>
        {
            foreach (var t in new[] { 0.5, 1.5, 3.0, 6.0 })
                GetTree().CreateTimer(t).Timeout += () => RepinBand(root);
            GetTree().Root.SizeChanged += () => Callable.From(() => RepinBand(root)).CallDeferred();
        }).CallDeferred();
        if (OS.GetCmdlineUserArgs().Contains("--chat-dump"))
        {
            Callable.From(() =>
            {
                DumpRects(root);
                GetTree().CreateTimer(8).Timeout += () => DumpRects(root);
            }).CallDeferred();
        }
    }

    /// <summary>Texte des boutons/labels du bandeau chat centré (H+V) : le layout
    /// natif les laissait collés en haut/à gauche (Équipe, Groupe…).</summary>
    private static void CenterLabels(Control root)
    {
        foreach (var n in root.FindChildren("*", "Button", true, false).Cast<Button>())
        {
            if (n is CheckBox or CheckButton) continue;
            n.Alignment = HorizontalAlignment.Center;
            n.ClipText = true;
            n.AddThemeConstantOverride("h_separation", 2);
        }
        foreach (var n in root.FindChildren("*", "Label", true, false).Cast<Label>())
        {
            n.HorizontalAlignment = HorizontalAlignment.Center;
            n.VerticalAlignment = VerticalAlignment.Center;
        }
    }

    /// <summary>Remet à leur place native les onglets Normal/Battle, « + » et Settings.</summary>
    private void RepinBand(Control root)
    {
        if (!IsInsideTree()) return;
        HudRoot.Pin(root, 0, 0, 1, 1, 0, 0, 0, 0);
        CenterLabels(root);
        // Le bandeau se cale sur la position RÉELLE du journal (la fenêtre peut avoir été
        // décalée par la position mémorisée) : onglets et boutons restent juste au-dessus.
        var main = TopLeftVitals.Find<Control>(root, "MainWnd");
        var h = GetViewportRect().Size.Y;
        var left = (main?.GlobalPosition.X ?? 3.87f) - 1.87f + 2f;
        var top = (main?.GlobalPosition.Y ?? h - 260f) - 17f;
        float T(float dy) => top - h + dy;
        if (_tabNormal != null) HudRoot.Pin(_tabNormal, 0, 1, 0, 1, left, T(0), left + 50, T(16));
        if (_tabBattle != null) HudRoot.Pin(_tabBattle, 0, 1, 0, 1, left + 52, T(0), left + 96, T(16));
        var ext = TopLeftVitals.Find<Control>(root, "ExtendBtn");
        if (ext != null) HudRoot.Pin(ext, 0, 1, 0, 1, left + 342, T(-1), left + 360, T(16));
        var set = TopLeftVitals.Find<Control>(root, "SettingBtn");
        if (set != null) HudRoot.Pin(set, 0, 1, 0, 1, left + 360, T(0), left + 404, T(16));
    }

    /// <summary>--chat-dump : rects globaux des éléments du bandeau
    /// (preuve de non-chevauchement, sans lire l'image).</summary>
    private void DumpRects(Control root)
    {
        foreach (var c in GetTree().Root.FindChildren("*", "Control", true, false).Cast<Control>())
        {
            var r = c.GetGlobalRect();
            if (!c.IsVisibleInTree() || r.Position.Y < 600 || r.Position.Y > 650 || r.Position.X > 130 || r.Size.X > 200) continue;
            var t = (c as Button)?.Text ?? (c as Label)?.Text ?? "";
            GD.Print($"[chat-zone] {c.GetType().Name} text=[{t}] rect={r} path={c.GetPath()}");
        }
        foreach (var n in new Control?[] { _tabNormal, _tabBattle,
                     TopLeftVitals.Find<Control>(root, "ExtendBtn"),
                     TopLeftVitals.Find<Control>(root, "SettingBtn"),
                     TopLeftVitals.Find<Control>(root, "MainWnd"),
                     TopLeftVitals.Find<Control>(root, "InputWnd") })
            if (n != null)
                GD.Print($"[chat-dump] {n.Name} text=[{(n as Button)?.Text}] parent={n.GetParent()?.GetType().Name}:{n.GetParent()?.Name} pos={n.Position} vis={n.IsVisibleInTree()} " +
                    $"rect={n.GetGlobalRect()}");
    }

    /// <summary>Boutons-canal déclarés dans le layout, dans l'ordre
    /// visuel gauche→droite : Btn1, Btn3, Btn4, Btn5, Btn6, Btn7, Btn9
    /// (Btn2 « Mutually » et Btn8 « Battlefield » hors séquence).</summary>
    private Button? _tabNormal, _tabBattle;
    private HudRoot? _hud;
    private static readonly Texture2D NoIcon =
        ImageTexture.CreateFromImage(
            Image.CreateEmpty(1, 1, false, Image.Format.Rgba8));

    /// <summary>Onglet du log (Normal / Battle) — skin DejaVu.</summary>
    private Button MakeTab(string text, int l, int r,
        bool battle)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        HudRoot.Pin(b, 0, 1, 0, 1, l, -278, r, -262);
        b.AddThemeFontSizeOverride("font_size", 12);
        b.Pressed += () => SetTab(battle);
        return b;
    }

    /// <summary>Onglet actif = TabButton*Selected + texte bleu (ref :
    /// « Battle » bleu actif, « Normal » gris).</summary>
    private void StyleTabs()
    {
        foreach (var (b, on) in new[] { (_tabNormal, !_battleTab),
                     (_tabBattle, _battleTab) })
        {
            if (b == null) continue;
            var sb = _hud != null ? EeSkin.Tab(_hud, false) : null;
            foreach (var st in new[] { "normal", "hover", "pressed" })
                if (sb != null) b.AddThemeStyleboxOverride(st, sb);
            // Ref : onglet actif sombre à texte gris, inactif clair à
            // texte bleu.
            var col = on ? Color.Color8(95, 95, 95)
                         : Color.Color8(40, 70, 160);
            b.AddThemeColorOverride("font_color", col);
            b.AddThemeColorOverride("font_hover_color", col);
            b.AddThemeColorOverride("font_pressed_color", col);
        }
    }

    private static int IndexToBtn(int i) =>
        new[] { 1, 3, 4, 5, 6, 7, 9 }[i];

    /// <summary>Message entrant — historisé puis rendu (BBCode
    /// coloré par canal) si l'onglet courant l'affiche.</summary>
    public void AddMessage(int channel, string sender, string text)
    {
        if (_log == null) return;
        var e = new ChatEntry(channel, sender, text,
            DateTime.Now.ToString("HH:mm"));
        _history.Add(e);
        if (_history.Count > HistoryCap)
            _history.RemoveRange(0, _history.Count - HistoryCap);
        if (PassesFilter(e)) _log.AppendText(Render(e));
    }

    /// <summary>Bascule d'onglet (Normal = tout, Battle =
    /// système/combat) puis re-rend l'historique.</summary>
    private void SetTab(bool battle)
    {
        if (_battleTab == battle) return;
        _battleTab = battle;
        StyleTabs();
        RenderAll();
    }

    /// <summary>Onglet Battle : seuls les messages non-canal
    /// (système/combat — canaux ≥ 7 de notre convention).</summary>
    private bool PassesFilter(ChatEntry e) =>
        !_battleTab || e.Channel >= 7;

    /// <summary>Re-rend tout l'historique filtré (switch onglet /
    /// toggle timestamps).</summary>
    private void RenderAll()
    {
        if (_log == null) return;
        _log.Clear();
        foreach (var e in _history)
            if (PassesFilter(e))
                _log.AppendText(Render(e));
    }

    /// <summary>Ligne BBCode d'un message : timestamp optionnel gris,
    /// préfixe canal coloré, sender, texte échappé.</summary>
    private string Render(ChatEntry e)
    {
        var ci = e.Channel >= 0 && e.Channel < ChannelColors.Length
            ? e.Channel : 7;
        var color = ChannelColors[ci];
        var prefix = UiTextEn.Translate(ChannelPrefix[ci]);
        var hex = "#" + color.ToHtml(false);
        var ts = _timestamps
            ? $"[color={TimestampHex}][{e.Ts}][/color] " : "";
        var who = e.Sender.Length > 0 ? $"{e.Sender}: " : "";
        var head = prefix.Length > 0 ? $"<{prefix}>" : "";
        // Ref : toute la ligne prend la couleur du canal, pas juste
        // le préfixe (« <Peer>test1: … » entièrement magenta).
        return $"{ts}[color={hex}]{head}{who}" +
               $"{Escape(e.Text)}[/color]\n";
    }

    /// <summary>Texte système (266/316) — jaune-vert, sans préfixe.</summary>
    public void AddSystem(string text) => AddMessage(7, "", text);

    /// <summary>Écho local d'un envoi (mode solo / serveur qui ne
    /// renvoie pas le say).</summary>
    public void AddLocalEcho(int channel, string text) =>
        AddMessage(channel, "", text);

    public void FocusInput() => _input?.GrabFocus();

    /// <summary>Message privé à <paramref name="name"/> (menu du
    /// cadre de groupe) : canal 1 ciblé, saisie focus.</summary>
    public void StartWhisper(string name)
    {
        if (name.Length == 0) return;
        _channel = 1;
        _whisperTo = name;
        if (_input != null) _input.PlaceholderText = UiTextEn.Translate($"To {name}:");
        FocusInput();
    }
    public void ReleaseInputFocus() => _input?.ReleaseFocus();

    /// <summary>Bascule le canal actif et son bouton radio.</summary>
    private void SetChannel(byte ch)
    {
        _channel = ch;
        _whisperTo = "";
        for (var i = 0; i < Channels.Length; i++)
            if (Channels[i].Id == ch)
                _channelBtns[i]?.SetPressedNoSignal(true);
        if (_input != null) _input.PlaceholderText = "";
    }

    private void OnSubmit(string text)
    {
        text = text.Trim();
        _input?.Clear();
        _input?.ReleaseFocus();
        if (text.Length == 0) return;
        // Préfixe « /x » — natif : /o = canal entraide (Peer, textid
        // 4834) ; s/p/g/z/w/b = convention EE (approximation U5).
        // « // » = commande GM → transmis tel quel (TextCommand 3).
        if (!text.StartsWith("//") && text.Length >= 2 &&
            text[0] == '/')
        {
            var ch = char.ToLowerInvariant(text[1]) switch
            {
                's' => 0, 'o' => 1, 'p' => 2, 'g' => 3,
                'z' => 4, 'w' => 5, 'b' => 6, _ => -1,
            };
            if (ch >= 0 && text.Length == 2)
            {
                SetChannel((byte)ch);
                return;
            }
            if (ch >= 0 && text[2] == ' ')
            {
                SetChannel((byte)ch);
                text = text[3..];
            }
            else if (ch < 0 || text[2] != ' ')
            {
                // « /give 21345 » (un seul slash, pas un canal) = commande
                // GM : le serveur attend « // » (gate privilège côté zone).
                if (char.IsLetter(text[1])) text = "/" + text;
            }
        }
        if (text.Length == 0) return;
        SendChat?.Invoke(_channel, _whisperTo, text);
    }

    private static string Escape(string s) =>
        s.Replace("[", "[lb]");

    /// <summary>Repli sans uidata : panneau sombre + log + saisie.</summary>
    /// <summary>Pref « Chat Font Size » (UI Settings).</summary>
    private void ApplyChatFont()
    {
        if (_log == null || !IsInstanceValid(_log)) return;
        var n = HudPrefs.ChatFontSize;
        foreach (var k in new[] { "normal_font_size", "bold_font_size",
                     "italics_font_size", "bold_italics_font_size",
                     "mono_font_size" })
            _log.AddThemeFontSizeOverride(k, n);
    }

    public override void _ExitTree()
    {
        HudPrefs.Changed -= ApplyChatFont;
        base._ExitTree();
    }

    private void BuildFallback()
    {
        var p = new Panel();
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomLeft);
        p.Position = new Vector2(4, -200);
        p.Size = new Vector2(302, 170);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.116f, 0.112f, 0.1f, 0.6f),
        });
        AddChild(p);
        _log = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true };
        _log.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _log.OffsetBottom = -26;
        p.AddChild(_log);
        _input = new LineEdit { PlaceholderText = UiTextEn.Translate("message…") };
        _input.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
        _input.OffsetTop = -24; _input.OffsetBottom = -2;
        _input.TextSubmitted += OnSubmit;
        p.AddChild(_input);
    }

    /// <summary>--chat-test : sonde QA — saisie réelle dans l'EditBox
    /// puis touche Entrée injectée (TextSubmitted → SendChat →
    /// NCID 34), /o bascule le canal (préfixe prouvé), vérifie
    /// l'écho serveur 268/327 dans le log. Capture dans
    /// godot-shots/u5_chat.png.</summary>
    public sealed partial class ChatProbe : Node
    {
        private readonly ChatPanel _c;
        private readonly string _shotsDir;
        private int _frame, _step;
        private int _historyAtStart = -1;

        public ChatProbe(ChatPanel c)
        {
            _c = c; Name = "__chat_probe__";
            _shotsDir = ProjectSettings.GlobalizePath(
                "res://../godot-shots");
        }

        public override void _Process(double delta)
        {
            if (Screens.LoadingScreen.Active != null ||
                _c._input == null || !_c.IsVisibleInTree())
                return;
            _frame++;
            switch (_step)
            {
                case 0 when _frame > 120:
                    _historyAtStart = _c._history.Count;
                    Submit("Hello say channel");
                    _step = 1;
                    break;
                case 1 when _frame % 40 == 0:
                    // L'écho 268 say met ~1 frame réseau — attendre.
                    GD.Print($"[chat-test] ch={_c._channel} " +
                        $"msgs={_c._history.Count - _historyAtStart}");
                    Submit("/o peer test");
                    _step = 2;
                    break;
                case 2 when _frame % 40 == 0:
                    GD.Print($"[chat-test] /o → ch={_c._channel} " +
                        $"derniers={Tail(2)}");
                    Submit("/p party line");
                    _step = 3;
                    break;
                case 3 when _frame % 40 == 0:
                    GD.Print($"[chat-test] /p → ch={_c._channel} " +
                        $"derniers={Tail(3)}");
                    Shot("u5_chat");
                    SetProcess(false);
                    break;
            }
        }

        /// <summary>Texte réel dans l'EditBox + touche Entrée
        /// injectée — le même chemin que l'utilisateur.</summary>
        private void Submit(string text) => CmdProbe.SubmitTo(_c, text);

        private string Tail(int n)
        {
            var h = _c._history;
            var s = new System.Text.StringBuilder();
            for (var i = Mathf.Max(0, h.Count - n); i < h.Count; i++)
                s.Append($"[{h[i].Channel}]«{h[i].Text}» ");
            return s.ToString();
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
                GD.PrintErr($"[chat-test] shot : {e.Message}");
            }
        }
    }

    /// <summary>--cmd-test=&lt;texte&gt; : sonde QA — soumet une ligne
    /// de chat arbitraire (ex. « //trait 2 ») par le vrai chemin
    /// EditBox + Entrée → SendChat → NCID 34. Une seule soumission,
    /// ~4 s après l'affichage du chat.</summary>
    public sealed partial class CmdProbe : Node
    {
        private readonly ChatPanel _c;
        private readonly string _text;
        private int _frame;

        public CmdProbe(ChatPanel c, string text)
        {
            _c = c; _text = text; Name = "__cmd_probe__";
        }

        public override void _Process(double delta)
        {
            if (Screens.LoadingScreen.Active != null ||
                _c._input == null || !_c.IsVisibleInTree())
                return;
            if (++_frame < 240) return;
            SubmitTo(_c, _text);
            GD.Print($"[cmd-test] soumis «{_text}»");
            SetProcess(false);
        }

        /// <summary>Texte réel dans l'EditBox + touche Entrée
        /// injectée — le même chemin que l'utilisateur.</summary>
        public static void SubmitTo(ChatPanel c, string text)
        {
            c.FocusInput();
            c._input!.Text = text;
            Input.ParseInputEvent(new InputEventKey
            { Keycode = Key.Enter, Pressed = true });
            Input.ParseInputEvent(new InputEventKey
            { Keycode = Key.Enter, Pressed = false });
        }
    }
}
