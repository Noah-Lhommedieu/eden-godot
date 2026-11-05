using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre de dialogue NPC (C8) — instancie le layout natif
/// <c>NpcTalkWnd.layout</c> (FrameWindow + Listbox2 + closeBtn) puis
/// injecte le contenu construit en code comme le client natif :
/// portrait à gauche, nom du NPC, texte parlé (BBCode) et colonne de
/// boutons-options à droite.
///
/// Données : NCID 285 <c>CNC_CZ_ClientDisplayDialogView</c>
/// ([i32 ctx][s16 n][n×(i32 displayId, str, s16)][u8][i32 npcUid] —
/// <c>ZoneEntryFlow.Dialogue.cs</c>), routé par
/// <see cref="HudNetBridge"/>. Les textes (corps parlé + titres
/// d'options) sont résolus dans <c>data/db/t_dialogue.ini</c> —
/// la table EN Reforged (lookup privé : TextResolver n'expose pas
/// encore T_Dialogue, hors périmètre d'édition).
/// </summary>
public partial class NpcDialogueWindow : Control
{
    /// <summary>Une option de dialogue (entrée du 285).</summary>
    public readonly record struct DialogOption(int DisplayId,
        string Name, short Flag);

    /// <summary>Émis quand le joueur clique une option — argument =
    /// INDEX de l'option (le ReplyDialog NCID 11 natif envoie
    /// l'index, pas le displayId — vérifié
    /// <c>HandleReplyDialogAsync</c> serveur).</summary>
    public event Action<short>? OptionChosen;

    /// <summary>Émis par les boutons d'action de la fiche quête
    /// (388) — (op MissionListOp 87, missionId, npcUid) : op 1 =
    /// accept, 2 = claim/reward (SocialHandlers.cs:425-466
    /// serveur).</summary>
    public event Action<short, int, int>? MissionAction;

    private Control? _window;
    private RichTextLabel? _body;
    private VBoxContainer? _options;
    private int _npcUid;
    private QuestSheetWindow? _sheet;
    private static Dictionary<int, TextTable.TextRow>? _dialogue;

    public void Build(HudRoot hud)
    {
        _window = hud.Build("NpcTalkWnd.layout");
        if (_window == null)
        {
            BuildFallback();
        }
        else
        {
            AddChild(_window);
            // Taille IDENTIQUE à la fiche de quête (QuestWnd natif
            // 358×664) et même ancrage à gauche (3,1 % / 1,8 %) : toutes
            // les interfaces de talk (hors shop) ont le même panneau ;
            // reste déplaçable (DragHelper.Persist).
            HudRoot.Pin(_window, TalkAnchorX, TalkAnchorY,
                TalkAnchorX, TalkAnchorY, 0, 0, TalkW, TalkH);
            // La Listbox2 native est remplacée par notre mise en page
            // (portrait + texte + boutons) — la fenêtre EE native la
            // remplit de lignes génériques sans style.
            var list = TopLeftVitals.Find<Control>(_window, "Listbox");
            if (list != null) list.Visible = false;
            var close = TopLeftVitals.Find<BaseButton>(_window,
                "closeBtn");
            // Bouton natif du bas masqué : remplacé par le « Close »
            // sombre du ref (BuildContent).
            if (close != null && close.Name.ToString().Contains("closeBtn"))
                close.Visible = false;
        }
        BuildContent();
        // Fiche de quête native (QuestWnd.layout) — sœur de ce nœud
        // (sa visibilité ne dépend pas du dialogue).
        var sheet = new QuestSheetWindow();
        sheet.Action += (op, mid, arg) => MissionAction?.Invoke(op, mid, arg);
        Callable.From(() =>
        {
            GetParent()?.AddChild(sheet);
            if (sheet.Build(hud)) _sheet = sheet;
            else sheet.QueueFree();
        }).CallDeferred();
        DragHelper.EnsureTitleBar(_window ?? this, "Dialogue");
        DragHelper.Persist(_window ?? this, "npc_dialogue_left");
        Visible = false;
    }

    public const float TalkAnchorX = 0.0309942f, TalkAnchorY = 0.0182741f;
    public const float TalkW = 358, TalkH = 664;

    private static readonly Color Paper = Color.Color8(238, 236, 228);
    private static readonly Color Ink = Color.Color8(40, 38, 36);
    private ScrollContainer? _scrollBox;

    /// <summary>Contenu commun (layout natif ou repli) — ref « npc talk
    /// propositions / réponse » : cadre sombre, feuille crème (texte
    /// parlé puis lignes de choix icône + libellé), bouton « Close »
    /// sombre en bas à droite.</summary>
    private void BuildContent()
    {
        var host = _window;
        if (host == null) return;

        var paper = new PanelContainer
        {
            Name = "__paper",
            MouseFilter = MouseFilterEnum.Stop,
        };
        paper.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Paper,
            BorderColor = Color.Color8(90, 88, 82),
            BorderWidthTop = 2, BorderWidthBottom = 2,
            BorderWidthLeft = 2, BorderWidthRight = 2,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 12, ContentMarginRight = 8,
            ContentMarginTop = 12, ContentMarginBottom = 10,
        });
        HudRoot.Pin(paper, 0, 0, 1, 1, 8, 28, -8, -46);
        host.AddChild(paper);

        _scrollBox = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            MouseFilter = MouseFilterEnum.Pass,
        };
        paper.AddChild(_scrollBox);
        var col = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        col.AddThemeConstantOverride("separation", 10);
        _scrollBox.AddChild(col);

        _body = new RichTextLabel
        {
            Name = "__body",
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _body.AddThemeFontSizeOverride("normal_font_size", 12);
        _body.AddThemeFontSizeOverride("bold_font_size", 12);
        _body.AddThemeFontSizeOverride("italics_font_size", 12);
        _body.AddThemeColorOverride("default_color", Ink);
        col.AddChild(_body);

        _options = new VBoxContainer { Name = "__options" };
        _options.AddThemeConstantOverride("separation", 3);
        col.AddChild(_options);

        // Bouton « Close » sombre, bas droite (ref).
        var close = new Button
        {
            Name = "__closebtn",
            Text = UiTextEn.Translate("Close"),
            FocusMode = FocusModeEnum.None,
        };
        var sbx = new StyleBoxFlat
        {
            BgColor = Color.Color8(34, 34, 34),
            BorderColor = Color.Color8(92, 92, 90),
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        };
        close.AddThemeStyleboxOverride("normal", sbx);
        close.AddThemeStyleboxOverride("hover", sbx);
        close.AddThemeStyleboxOverride("pressed", sbx);
        close.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
        close.AddThemeColorOverride("font_color", Color.Color8(215, 215, 210));
        close.AddThemeColorOverride("font_hover_color", Colors.White);
        close.AddThemeFontSizeOverride("font_size", 12);
        HudRoot.Pin(close, 1, 1, 1, 1, -84, -38, -12, -12);
        close.Pressed += () => Visible = false;
        host.AddChild(close);
    }

    /// <summary>Ligne de choix : icône (ampoule jaune = quête, bulle
    /// violette = discussion) + libellé encre ; survol = soulignement
    /// bleu.</summary>
    private Control MakeOption(string text, bool quest, Action onPress)
    {
        var row = new HBoxContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            MouseDefaultCursorShape = CursorShape.PointingHand,
        };
        row.AddThemeConstantOverride("separation", 4);
        var icon = new Control
        {
            CustomMinimumSize = new Vector2(16, 16),
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        icon.Draw += () =>
        {
            if (quest)
            {
                icon.DrawCircle(new Vector2(8, 6), 5f,
                    Color.Color8(246, 190, 40));
                icon.DrawArc(new Vector2(8, 6), 5f, 0, Mathf.Tau, 16,
                    Color.Color8(150, 100, 10), 1f);
                icon.DrawRect(new Rect2(5.5f, 11f, 5f, 4f),
                    Color.Color8(120, 120, 130));
            }
            else
            {
                icon.DrawRect(new Rect2(1, 3, 14, 10),
                    Color.Color8(120, 80, 170));
                icon.DrawRect(new Rect2(1, 3, 14, 10),
                    Color.Color8(70, 40, 110), false, 1f);
                icon.DrawColoredPolygon(new[]
                {
                    new Vector2(3, 13), new Vector2(7, 13),
                    new Vector2(3, 16),
                }, Color.Color8(120, 80, 170));
            }
        };
        row.AddChild(icon);
        var l = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", Ink);
        row.AddChild(l);
        row.MouseEntered += () =>
            l.AddThemeColorOverride("font_color", Color.Color8(20, 80, 200));
        row.MouseExited += () =>
            l.AddThemeColorOverride("font_color", Ink);
        row.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true,
                    ButtonIndex: MouseButton.Left }) onPress();
        };
        return row;
    }

    private static bool LooksLikeQuest(string label) =>
        System.Text.RegularExpressions.Regex.IsMatch(label,
            @"^\s*(L|Lv|Nv|Niv|Lvl)\.?\s*\d+\s*[\.:]");

    /// <summary>Affiche le dialogue du 285 : <paramref name="textId"/>
    /// = T_Dialogue du texte parlé ; les options affichent leur nom
    /// embarqué ou le texte T_Dialogue de leur DisplayId.</summary>
    public void ShowDialogue(int npcUid, int textId,
        IReadOnlyList<DialogOption> options)
    {
        _npcUid = npcUid;
        var npcName = TextResolver.NpcName(npcUid) ?? "PNJ";
        var npcTitle = TextResolver.NpcTitle(npcUid);
        SetTitle(npcTitle != null ? $"{npcTitle}  {npcName}" : npcName);
        var body = DialogueText(textId);
        if (_body != null)
            _body.Text = body.Length > 0 ? body : "[i]…[/i]";
        FillOptions(options);
        Visible = true;
    }

    /// <summary>Mock de démo (mode solo / captures) — présente le
    /// dialogue d'un faux marchand. TODO(proto) : retirer quand le 285
    /// couvre tous les cas d'ouverture.</summary>
    public void ShowMock()
    {
        _npcUid = 0;
        SetTitle(UiTextEn.Translate("Carrack"));
        if (_body != null)
            _body.Text = UiTextEn.Translate("Welcome, traveler! The Blue Crystal's light ") +
                UiTextEn.Translate("guides your path. How may I help you today?");
        FillOptions(new[]
        {
            new DialogOption(1, UiTextEn.Translate("Shop"), 0),
            new DialogOption(2, "Quest: Awakening I", 0),
            new DialogOption(3, "Gossip", 0),
            // Entrées quête serveur : displayId -3, Name = id mission en
            // texte -> doit s'afficher en titre, jamais en « 1012 ».
            new DialogOption(-3, "1012", 0),
            new DialogOption(-3, "1013", 0),
            new DialogOption(0, "29931", 0),
            new DialogOption(4, UiTextEn.Translate("Farewell"), 0),
        });
        Visible = true;
    }

    /// <summary>Fiche quête (S→C 388 EnterMissionList, routé par le
    /// bridge) : op 1 = offre (« Accept » → 87 op=1), op 2 =
    /// récompense (« Complete » → 87 op=2), op 3 = suivi en cours,
    /// op 4 = fin (le serveur ferme la fiche). Corps = titre +
    /// description t_mission + objectifs/récompenses c_mission EN.</summary>
    public void ShowMissionSheet(short op, int mid, int npcUid)
    {
        if (mid <= 0 && op != MissionSheetOps.Finish) return;
        if (_sheet != null)
        {
            if (op == MissionSheetOps.Finish) _sheet.Visible = false;
            else
            {
                Visible = false;
                _sheet.Show(op, mid, npcUid);
            }
            return;
        }
        _npcUid = npcUid;
        var title = TextResolver.MissionTitle(mid) ?? "Quête";
        SetTitle(title);
        var sb = new System.Text.StringBuilder();
        sb.Append($"[b]{title}[/b]\n");
        if (op is MissionSheetOps.Open or MissionSheetOps.Reward
            or MissionSheetOps.Share)
        {
            var desc = TextResolver.MissionDescription(mid);
            if (desc != null) sb.Append(desc + "\n\n");
            foreach (var o in MissionObjectives.ObjectiveLines(mid))
                sb.Append($"• {o}\n");
            var rewards = MissionObjectives.RewardLines(mid);
            if (rewards.Count > 0)
                sb.Append(UiTextEn.Translate("\n[i]Reward: ") +
                    string.Join(", ", rewards) + "[/i]");
        }
        else if (op == MissionSheetOps.OnGoing)
        {
            var desc = TextResolver.MissionDescription(mid);
            sb.Append(desc ?? "");
            sb.Append(UiTextEn.Translate("\n\n[i]In progress…[/i]"));
        }
        else if (op == MissionSheetOps.Finish)
        {
            // Fin de quête : le serveur referme l'UI de récompense.
            Visible = false;
            return;
        }
        if (_body != null) _body.Text = sb.ToString();
        if (_options == null) { Visible = true; return; }
        foreach (var c in _options.GetChildren()) c.QueueFree();
        void Act(string label, short missionOp, int? arg = null)
        {
            _options.AddChild(MakeOption(label, true, () =>
            {
                // arg : 3ᵉ champ du 87 (flag d'acceptation de l'op 7) —
                // sinon l'uid du PNJ.
                MissionAction?.Invoke(missionOp, mid, arg ?? npcUid);
                Visible = false;
            }));
        }
        void Dismiss(string label)
        {
            _options.AddChild(MakeOption(label, false,
                () => Visible = false));
        }
        switch (op)
        {
            case MissionSheetOps.Open:
                Act(UiTextEn.Translate("Accept"), MissionListOps.Accept);
                Dismiss(UiTextEn.Translate("Decline"));
                break;
            case MissionSheetOps.Reward:
                Act("Complete", MissionListOps.Reward);
                Dismiss(UiTextEn.Translate("Close"));
                break;
            case MissionSheetOps.Share:
                // Partage d'équipe : Accept = 87 op 7 flag 1 ; Decline
                // = op 7 flag 0 (le serveur prévient le partageur).
                Act(UiTextEn.Translate("Accept"),
                    MissionListOps.ShareAnswer, 1);
                Act(UiTextEn.Translate("Decline"),
                    MissionListOps.ShareAnswer, 0);
                break;
            default:
                Dismiss(UiTextEn.Translate("Close"));
                break;
        }
        Visible = true;
    }

    private void FillOptions(IReadOnlyList<DialogOption> options)
    {
        if (_options == null) return;
        foreach (var c in _options.GetChildren()) c.QueueFree();
        for (var i = 0; i < options.Count; i++)
        {
            var opt = options[i];
            var (label, isQuest) = ResolveOptionLabel(opt);
            var idx = (short)i;
            _options.AddChild(MakeOption(label, isQuest || LooksLikeQuest(label),
                () => OptionChosen?.Invoke(idx)));
        }
    }

    /// <summary>Libellé d'un choix SANS identifiant visible. Entrée quête
    /// serveur : displayId -3 et Name = id de mission en texte ("1012")
    /// -> titre t_mission. Name purement numérique ou « NULL???? » = ID
    /// brut : on retombe sur T_Dialogue/t_mission, puis un libellé neutre.</summary>
    private static (string label, bool quest) ResolveOptionLabel(DialogOption opt)
    {
        var name = opt.Name.Trim();
        if (IsBareId(name) && int.TryParse(name, out var mid) &&
            TextResolver.MissionTitle(mid) is { Length: > 0 } t0)
            return (StripId(t0), true);
        var label = IsBareId(name) ? "" : name;
        if (label.Length == 0 && opt.DisplayId > 0)
        {
            label = DialogueText(opt.DisplayId);
            if (IsBareId(label)) label = "";
        }
        if (label.Length == 0 && opt.DisplayId > 0 &&
            TextResolver.MissionTitle(opt.DisplayId) is { Length: > 0 } mt)
            return (StripId(mt), true);
        if (label.Length == 0)
            return (UiTextEn.Translate("Discuter"), false);
        return (StripId(label), false);
    }

    private static bool IsBareId(string s) =>
        s.Length == 0 || s.StartsWith("NULL?") ||
        System.Text.RegularExpressions.Regex.IsMatch(s, @"^[#\[(]?-?\d+[\])]?$");

    /// <summary>Retire un ID résiduel en tête (« [123] », « #123 »,
    /// « 123: ») ou en queue (« , 29931 », « (123) ») d'un libellé.</summary>
    private static string StripId(string s)
    {
        var r = System.Text.RegularExpressions.Regex.Replace(s,
            @"^\s*[#\[(]\d{3,}[\])]?\s*[:\-.]?\s*", "");
        r = System.Text.RegularExpressions.Regex.Replace(r,
            @"\s*[,(\[#]\s*\d{4,}\s*[\])]?\s*[.]?\s*$", "");
        return r.Length > 0 ? r : s;
    }

    /// <summary>Titre de la barre de titre CEGUI auto-générée.</summary>
    private void SetTitle(string title)
    {
        var bar = _window?
            .FindChildren("*", "", true, false)
            .OfType<Ui.CeguiTitleBar>().FirstOrDefault();
        var lbl = bar?.FindChildren("*", "Label", true, false)
            .OfType<Label>().FirstOrDefault();
        if (lbl != null) lbl.Text = title;
    }

    /// <summary>Texte T_Dialogue (table EN, format id|texte|) — null
    /// → chaîne vide. Chargement paresseux (~80k lignes).</summary>
    private static string DialogueText(int id)
    {
        if (id == 0) return "";
        if (_dialogue == null)
        {
            _dialogue = LoadDialogue("data/db/t_dialogue.ini");
            // FR : table lang/fr prioritaire, repli EN par id.
            if (TextResolver.Language != "en")
                _dialogueLang = LoadDialogue(
                    $"data/db/lang/{TextResolver.Language}/t_dialogue.ini");
        }
        if (_dialogueLang != null &&
            _dialogueLang.TryGetValue(id, out var fr) &&
            TextResolver.Clean(fr.Field(1)) is { Length: > 0 } frText)
            return frText;
        return _dialogue.TryGetValue(id, out var row)
            ? TextResolver.Clean(row.Field(1)) : "";
    }

    private static Dictionary<int, TextTable.TextRow>? _dialogueLang;

    private static Dictionary<int, TextTable.TextRow> LoadDialogue(
        string rel)
    {
        var path = TextResolver.ResolveFile?.Invoke(rel)
            ?? AssetCatalog.ResolveRawAsset(rel);
        try
        {
            return path != null
                ? TextTable.Load(path)
                : new Dictionary<int, TextTable.TextRow>();
        }
        catch (Exception e)
        {
            GD.PrintErr($"[hud] {rel} : {e.Message}");
            return new Dictionary<int, TextTable.TextRow>();
        }
    }

    /// <summary>Fenêtre de repli sans uidata.</summary>
    private void BuildFallback()
    {
        var p = new Panel { Name = "NpcTalkWnd" };
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-200, -230);
        p.Size = new Vector2(400, 460);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.133f, 0.128f, 0.114f, 0.95f),
            BorderColor = new Color(0.468f, 0.45f, 0.401f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        _window = p;
        DragHelper.EnsureTitleBar(p, "Dialogue");
    }
}
