using Godot;
using EdenClient.Data;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Fiche de quête PNJ (S→C 388) — fenêtre native <c>QuestWnd.layout</c>
/// (358×664) habillée comme les refs « proposition nouvelle quete.png » et
/// « quest UI.png » : en-tête « ◆ Nv. N Titre » + état à droite, cadre de
/// lore, cadre d'info (« Aller voir : », « Objectifs : », « Récompense : »,
/// « Récompenses disponibles : » en icônes, champ or, champ « EXP : »),
/// boutons Confirmer / Retour.
/// Textes natifs : <c>Report to:</c>, <c>Objectives:</c>, <c>Reward:</c>,
/// <c>Available quest rewards:</c> (pack FR officiel).
/// </summary>
public partial class QuestSheetWindow : Control
{
    /// <summary>(op MissionListOp 87, mid, 3ᵉ champ) — comme
    /// <see cref="NpcDialogueWindow.MissionAction"/>.</summary>
    public event Action<short, int, int>? Action;

    private Control? _win;
    private Label? _head, _status, _titleLabel;
    private RichTextLabel? _lore;
    private VBoxContainer? _info;
    private BaseButton? _ok, _back;
    private short _op;
    private int _mid, _npc;

    private static readonly Color Ink = Color.Color8(30, 30, 28);
    private static readonly Color Gold = Color.Color8(0xb0, 0x8a, 0x00);

    public bool Build(HudRoot hud)
    {
        var nat = hud.Build("QuestWnd.layout");
        if (nat == null) return false;
        var head = TopLeftVitals.Find<Label>(nat, "MissionText");
        var status = TopLeftVitals.Find<Label>(nat, "statusText");
        var listbox = TopLeftVitals.Find<Control>(nat, "listbox");
        var pane = TopLeftVitals.Find<Control>(nat, "pane");
        var ok = TopLeftVitals.Find<BaseButton>(nat, "AcceptedBtn");
        var back = TopLeftVitals.Find<BaseButton>(nat, "closeBtn");
        if (head == null || status == null || listbox == null ||
            pane == null || ok == null || back == null)
        {
            GD.PrintErr("[questsheet] QuestWnd : widgets attendus absents");
            nat.Free();
            return false;
        }
        _win = nat; _head = head; _status = status; _ok = ok; _back = back;
        AddChild(nat);
        // Position/dimensions natives (3 % / 1,8 %, 358×664) gardées ;
        // tirable comme les autres fenêtres.
        // Même rect que la fenêtre de dialogue PNJ (taille identique,
        // à gauche) — toutes les interfaces de talk hors shop.
        HudRoot.Pin(nat, NpcDialogueWindow.TalkAnchorX,
            NpcDialogueWindow.TalkAnchorY, NpcDialogueWindow.TalkAnchorX,
            NpcDialogueWindow.TalkAnchorY, 0, 0,
            NpcDialogueWindow.TalkW, NpcDialogueWindow.TalkH);
        DragHelper.Persist(nat, "questsheet_left");
        head.AddThemeFontSizeOverride("font_size", 13);
        head.AddThemeColorOverride("font_color", Colors.White);
        head.ClipText = true;
        status.AddThemeFontSizeOverride("font_size", 12);
        status.HorizontalAlignment = HorizontalAlignment.Right;

        // Lore : cadre parchemin à la place de la Listbox native.
        listbox.Visible = false;
        var loreBox = new Panel();
        CopyRect(listbox, loreBox);
        loreBox.AddThemeStyleboxOverride("panel", EeSkin.ParchmentBox());
        listbox.GetParent().AddChild(loreBox);
        _lore = new RichTextLabel
        {
            BbcodeEnabled = true, ScrollActive = true, FitContent = false,
        };
        _lore.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _lore.OffsetLeft = 8; _lore.OffsetTop = 6;
        _lore.OffsetRight = -8; _lore.OffsetBottom = -6;
        _lore.AddThemeFontSizeOverride("normal_font_size", 13);
        _lore.AddThemeFontSizeOverride("bold_font_size", 13);
        _lore.AddThemeColorOverride("default_color", Ink);
        loreBox.AddChild(_lore);

        // Info : cadre parchemin sous le lore (pane natif = zone).
        var infoBox = new Panel();
        CopyRect(pane.GetParent<Control>(), infoBox);
        infoBox.AddThemeStyleboxOverride("panel", EeSkin.ParchmentBox());
        pane.GetParent().GetParent().AddChild(infoBox);
        pane.GetParent<Control>().Visible = false;
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetLeft = 8; scroll.OffsetTop = 6;
        scroll.OffsetRight = -6; scroll.OffsetBottom = -6;
        infoBox.AddChild(scroll);
        _info = new VBoxContainer
            { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _info.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_info);

        ok.Pressed += OnOk;
        back.Pressed += OnBack;
        nat.Visible = false;
        return true;
    }

    private static void CopyRect(Control src, Control dst)
    {
        dst.AnchorLeft = src.AnchorLeft; dst.AnchorRight = src.AnchorRight;
        dst.AnchorTop = src.AnchorTop; dst.AnchorBottom = src.AnchorBottom;
        dst.OffsetLeft = src.OffsetLeft; dst.OffsetRight = src.OffsetRight;
        dst.OffsetTop = src.OffsetTop; dst.OffsetBottom = src.OffsetBottom;
    }

    public new bool Visible
    {
        get => _win?.Visible ?? false;
        set { if (_win != null) _win.Visible = value; }
    }

    private void OnOk()
    {
        switch (_op)
        {
            case MissionSheetOps.Open:
                Action?.Invoke(MissionListOps.Accept, _mid, _npc); break;
            case MissionSheetOps.Reward:
                Action?.Invoke(MissionListOps.Reward, _mid, _npc); break;
            case MissionSheetOps.Share:
                Action?.Invoke(MissionListOps.ShareAnswer, _mid, 1); break;
            default: break;
        }
        Visible = false;
    }

    private void OnBack()
    {
        if (_op == MissionSheetOps.Share)
            Action?.Invoke(MissionListOps.ShareAnswer, _mid, 0);
        Visible = false;
    }

    /// <summary>Remplit et affiche la fiche (op 1 offre, 2 récompense,
    /// 3 en cours, 7 partage).</summary>
    public void Show(short op, int mid, int npcUid)
    {
        if (_win == null || _info == null || _lore == null) return;
        _op = op; _mid = mid; _npc = npcUid;
        var title = TextResolver.MissionTitle(mid)
            ?? "Quête";
        var m = MissionTable.Get(mid);
        _head!.Text = "◆ " + title;
        var (stText, stCol) = op switch
        {
            MissionSheetOps.Reward => (UiTextEn.Translate("Finished"),
                Color.Color8(205, 225, 60)),
            MissionSheetOps.OnGoing => (UiTextEn.Translate("In progress"),
                Color.Color8(235, 210, 60)),
            _ => ("", Colors.White),
        };
        // Barre de titre native (« Tatle » dans le layout) = « ◆ Titre »
        // + état — le MissionText du layout est masqué sous la barre.
        var tlab = _titleLabel ??= _win.FindChildren("*", "Label", true,
                false).Cast<Label>().FirstOrDefault(
                l => l.Text.Trim() == "Tatle");
        if (tlab != null)
        {
            tlab.AddThemeFontSizeOverride("font_size", 13);
            tlab.Text = stText.Length > 0 ? $"{title}  [{stText}]" : title;
        }
        _status!.Text = stText;
        _status.AddThemeColorOverride("font_color", stCol);
        _lore.Text = TextResolver.MissionDescription(mid) ?? "";

        foreach (var c in _info.GetChildren())
        {
            _info.RemoveChild(c);
            c.QueueFree();
        }
        // « Aller voir : » — PNJ de rendu (ou le PNJ courant à l'offre).
        var see = m is { SubmitNpc: not 0 } ? m.SubmitNpc : npcUid;
        _info.AddChild(Head(UiTextEn.Translate("Report to:")));
        if (see != 0)
            _info.AddChild(Body("   " + NpcLabel(see)));
        _info.AddChild(Head(UiTextEn.Translate("Objectives:")));
        var counted = op != MissionSheetOps.Open &&
                      op != MissionSheetOps.Share;
        foreach (var o in MissionObjectives.ForMission(mid))
        {
            var kind = MissionObjectives.DecodePmsKey(o.PmsKey).Type;
            var cur = MissionCounters.Current(o);
            var line = counted && QuestObjectiveLogic.ShowsCount(kind)
                ? $"{o.Label} {cur}/{o.Req}"
                : o.Req > 1 && QuestObjectiveLogic.ShowsCount(kind)
                    ? $"{o.Label} ×{o.Req}" : o.Label;
            _info.AddChild(Body("   " + line));
        }
        // Récompenses : fixes (RewardItem) puis « disponibles » (choix).
        var fixedItems = new List<(int Id, int Qty)>();
        var choose = new List<(int Id, int Qty)>();
        long gold = 0, exp = 0;
        if (m != null)
            foreach (var r in m.Rewards)
            {
                int A(int i) => i < r.Args.Count &&
                    int.TryParse(r.Args[i], out var v) ? v : 0;
                switch (r.Name)
                {
                    case "RewardItem": fixedItems.Add((A(0), Math.Max(1, A(1)))); break;
                    case "RewardChooseItem": choose.Add((A(0), Math.Max(1, A(1)))); break;
                    case "RewardGold": gold += A(0); break;
                    case "RewardExp": exp += A(0); break;
                }
            }
        _info.AddChild(Head(UiTextEn.Translate("Reward:")));
        _info.AddChild(Field("●", gold.ToString("N0"), Gold));
        _info.AddChild(Field(UiTextEn.Translate("EXP") + " :",
            exp.ToString("N0")));
        // Ref « quest log 3 » : choix = « Sélectionnez une récompense »,
        // récompenses fixes = « Récompenses disponibles ».
        if (choose.Count > 0)
        {
            _info.AddChild(Head(UiTextEn.Translate(
                "Please select a quest reward:")));
            _info.AddChild(IconRow(choose));
        }
        if (fixedItems.Count > 0)
        {
            _info.AddChild(Head(UiTextEn.Translate(
                "Available quest rewards:")));
            _info.AddChild(IconRow(fixedItems));
        }

        // Boutons : Confirmer / Retour (ref) ; hors offre/rendu/partage,
        // Confirmer reste grisé.
        _ok!.Visible = true;
        _ok.Set("disabled", _op is not (MissionSheetOps.Open
            or MissionSheetOps.Reward or MissionSheetOps.Share));
        if (_ok is Button ob) ob.Text = UiTextEn.Translate("Confirm");
        if (_back is Button bb) bb.Text = UiTextEn.Translate("Return");
        Visible = true;
    }

    private static string NpcLabel(int npc)
    {
        var name = TextResolver.NpcName(npc) ?? "PNJ";
        var t = TextResolver.NpcTitle(npc);
        return t != null ? $"<{t}> {name}" : name;
    }

    internal static Label Head(string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 13);
        l.AddThemeColorOverride("font_color", Ink);
        return l;
    }

    internal static Label Body(string text)
    {
        var l = new Label
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        l.AddThemeFontSizeOverride("font_size", 12);
        l.AddThemeColorOverride("font_color", Ink);
        return l;
    }

    /// <summary>Rangée d'icônes d'objets (ref : 6 icônes + « ×15 »).</summary>
    internal static Control IconRow(List<(int Id, int Qty)> items)
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 6);
        foreach (var (id, qty) in items)
        {
            var def = InventoryWindow.LookupDef(id);
            var box = new Control
            {
                CustomMinimumSize = new Vector2(36, 36),
                TooltipText = TextResolver.ItemName(id) ?? "",
            };
            var tex = def is { Code.Length: > 0 }
                ? AssetCatalog.GetTexture($"UI/itemicon/{def.Code}.png")
                : null;
            var icon = new TextureRect
            {
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            box.AddChild(icon);
            if (qty > 1)
            {
                var q = new Label { Text = $"{qty}",
                    MouseFilter = MouseFilterEnum.Ignore };
                EeSkin.Shadowed(q, 11, Colors.White);
                q.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
                q.GrowHorizontal = GrowDirection.Begin;
                q.GrowVertical = GrowDirection.Begin;
                box.AddChild(q);
            }
            row.AddChild(box);
        }
        return row;
    }

    /// <summary>Champ « EXP : [ 800 ]» / or (ref : boîte creuse à droite
    /// du libellé, valeur alignée à droite).</summary>
    internal static Control Field(string label, string value,
        Color? labelColor = null)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var l = Body(label);
        if (labelColor is { } lc)
            l.AddThemeColorOverride("font_color", lc);
        l.CustomMinimumSize = new Vector2(44, 0);
        row.AddChild(l);
        var p = new PanelContainer { CustomMinimumSize = new Vector2(96, 22) };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.Color8(218, 214, 204),
            BorderColor = Color.Color8(120, 116, 106),
            BorderWidthTop = 1, BorderWidthBottom = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 6, ContentMarginRight = 6,
        });
        var v = new Label { Text = value,
            HorizontalAlignment = HorizontalAlignment.Right };
        v.AddThemeFontSizeOverride("font_size", 12);
        v.AddThemeColorOverride("font_color", Ink);
        p.AddChild(v);
        row.AddChild(p);
        return row;
    }
}
