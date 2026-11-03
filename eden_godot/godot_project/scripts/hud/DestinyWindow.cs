using Godot;
using EdenClient.Data;
using EdenClient.Data.Tables;
using EdenClient.Protocol;

namespace EdenClient.Hud;

/// <summary>
/// Fenêtre « chemin d'éveil » (destiny road) — Vague 2.
///
/// Le plateau est le layout natif <c>FateRoadWnd.layout</c>
/// (FrameWindow 272×266 : 39 <c>SlotImage</c> RoadIcon posées en
/// spirale + marqueur <c>StepNow</c> + <c>UseBtn</c>/<c>CloseBtn</c> +
/// <c>Text</c>), complété par un bandeau ajouté sous le plateau :
/// vitalité / DP / dés / avancée, boutons natifs d'action
/// (créer/lancer/avancer/fonctions/choix de dé), compteurs fortune
/// et notification d'événement de case.
///
/// Données : <see cref="DestinyState"/> alimenté par
/// <see cref="DestinyBridge"/> (534 complet, 589 updates, 535
/// événement, 590 refus, 557 fortune) ; les tables
/// <see cref="DestinyTable"/> (icônes fateNN/S81016xx, noms EN,
/// types d'effet, décor/tier de route).
///
/// Actions C→S via les délégués branchés par le bridge :
/// <see cref="DiceSender"/> (203 [op][p2][p3]),
/// <see cref="QuerySender"/> (204), <see cref="FortuneSender"/> (223).
/// </summary>
public partial class DestinyWindow : Control
{
    /// <summary>Dernière instance construite (bridge + --hud-open).</summary>
    public static DestinyWindow? Instance { get; private set; }

    /// <summary>État destin partagé (muté par DestinyBridge).</summary>
    /// <summary>État partagé — alimenté par les binds précoces de
    /// HudExtensions dès le burst d'entrée (avant le montage de la
    /// fenêtre) ; les notifications Changed/Event*/OpFailed sont
    /// ré-émises en file principale.</summary>
    public readonly DestinyState State = DestinyState.Shared;

    /// <summary>NCID 203 [s16 op][s16 p2][s16 p3].</summary>
    public Func<short, short, short, Task>? DiceSender;
    /// <summary>NCID 204 query road (corps vide).</summary>
    public Func<Task>? QuerySender;
    /// <summary>NCID 223 query fortune counters.</summary>
    public Func<Task>? FortuneSender;

    private const int CellCount = 39;

    private Control? _panel;
    private Control? _board;
    private Control? _stepNow;
    private Button? _useBtn;
    private Label? _boardText;
    private readonly TextureRect?[] _cells = new TextureRect?[CellCount];
    private readonly ReferenceRect?[] _marks =
        new ReferenceRect?[CellCount];
    private Vector2[] _cellPos = new Vector2[CellCount];
    private Vector2 _cellSize = new(24, 24);

    private Label? _vit, _dp, _rolls, _move, _fortune, _event;
    private Button? _create, _roll, _go;
    private readonly Button?[] _funcs = new Button?[6];
    private readonly Button?[] _faces = new Button?[6];
    private double _eventTimer;

    public void Build(HudRoot hud)
    {
        // La FrameWindow native FateRoadWnd EST la fenêtre : sa
        // titlebar (enfant direct) sert au drag via EnsureTitleBar —
        // emboîter le layout dans un Panel intermédiaire ferait
        // échouer son MoveChild (recherche profonde).
        _board = hud.Build("FateRoadWnd.layout");
        if (_board != null)
        {
            _panel = _board;
            _panel.Name = "DestinyPanel";
            // Élargie : plateau natif 272×266 + bandeau ajouté
            // (~170 px sous le plateau).
            HudRoot.Pin(_panel, 0.5f, 0.5f, 0.5f, 0.5f,
                -150, -240, 150, 200);
            CollectBoardNodes();
        }
        else
        {
            var p = new Panel { Name = "DestinyPanel" };
            HudRoot.Pin(p, 0.5f, 0.5f, 0.5f, 0.5f,
                -150, -240, 150, 200);
            p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.083f, 0.08f, 0.071f, 0.92f),
                BorderColor = new Color(0.5f, 0.42f, 0.2f),
                BorderWidthBottom = 1, BorderWidthTop = 1,
                BorderWidthLeft = 1, BorderWidthRight = 1,
            });
            var host = new Control { Position = new Vector2(14, 28) };
            BuildFallbackBoard(host);
            p.AddChild(host);
            _panel = p;
        }

        BuildInfoStrip();
        _panel.Visible = false;
        AddChild(_panel);

        DragHelper.EnsureTitleBar(_panel, UiTextEn.Translate("Destiny Road"));
        DragHelper.Persist(_panel, "destiny");

        Instance = this;
        State.Changed += Refresh;
        State.EventReceived += OnEventReceived;
        State.OpFailed += op => ShowBoardText(
            $"Action {op} denied.");
        Refresh();
    }

    /// <summary>Récupère les nœuds du layout natif (noms aplatis
    /// « Root_FateRoadWnd_Background_Border_RoadIcon3 »).</summary>
    private void CollectBoardNodes()
    {
        if (_board == null) return;
        for (var i = 0; i < CellCount; i++)
        {
            var c = TopLeftVitals.Find<Control>(_board,
                $"RoadIcon{i + 1}");
            if (c == null) continue;
            var tr = c as TextureRect;
            if (tr == null)
            {
                tr = new TextureRect
                {
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum
                        .KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Ignore,
                };
                tr.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                c.AddChild(tr);
            }
            _cells[i] = tr;
            _cellPos[i] = c.Position;
            if (c.Size.X > 0) _cellSize = c.Size;
            var mark = new ReferenceRect
            {
                BorderColor = new Color(1f, 0.85f, 0.25f),
                BorderWidth = 2f,
                EditorOnly = false,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
            };
            mark.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            c.AddChild(mark);
            _marks[i] = mark;
        }
        _stepNow = TopLeftVitals.Find<Control>(_board, "StepNow");
        if (_stepNow != null) _stepNow.Visible = false;
        _useBtn = TopLeftVitals.Find<Button>(_board, "UseBtn");
        if (_useBtn != null)
        {
            _useBtn.Text = UiTextEn.Translate("Use");
            _useBtn.Pressed += () => Dice(1);
        }
        var close = TopLeftVitals.Find<Button>(_board, "CloseBtn");
        if (close != null)
        {
            close.Text = UiTextEn.Translate("Close");
            close.Pressed += Toggle;
        }
        _boardText = TopLeftVitals.Find<Label>(_board, "Text");
        if (_boardText != null) _boardText.Text = "";
    }

    /// <summary>Bandeau ajouté sous le plateau : stats + boutons +
    /// fortune + notifications (ajouts client, pas de contrepartie
    /// native dans ce layout — le natif met ces infos dans
    /// FateRoadInfo/FateRoadTipWnd).</summary>
    private void BuildInfoStrip()
    {
        // Fond sombre sous le bandeau ajouté (le décor natif de la
        // FrameWindow ne descend que jusqu'à ~266 px).
        var back = new Panel { Name = "StripBack" };
        back.Position = new Vector2(4, 296);
        back.Size = new Vector2(292, 140);
        back.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.083f, 0.08f, 0.071f, 0.9f),
            BorderColor = new Color(0.5f, 0.42f, 0.2f, 0.7f),
            BorderWidthTop = 1,
        });
        back.MouseFilter = MouseFilterEnum.Stop;
        _panel?.AddChild(back);
        var v = new VBoxContainer { Name = "InfoStrip" };
        v.Position = new Vector2(10, 302);
        v.Size = new Vector2(280, 132);
        v.AddThemeConstantOverride("separation", 4);

        var stats = new HBoxContainer();
        stats.AddThemeConstantOverride("separation", 10);
        _vit = Stat(stats, UiTextEn.Translate("Vit ?"));
        _dp = Stat(stats, UiTextEn.Translate("DP 0"));
        _rolls = Stat(stats, UiTextEn.Translate("Dice 0"));
        _move = Stat(stats, UiTextEn.Translate("Move -"));
        v.AddChild(stats);

        var row1 = new HBoxContainer();
        row1.AddThemeConstantOverride("separation", 4);
        _create = new Button { Text = UiTextEn.Translate("Create road"),
            TooltipText = UiTextEn.Translate("Create the destiny road (needs 100 vitality)") };
        _create.Pressed += () => Dice(0);
        row1.AddChild(_create);
        _roll = new Button { Text = UiTextEn.Translate("Roll"),
            TooltipText = UiTextEn.Translate("Roll the dice (costs DP)") };
        _roll.Pressed += () => Dice(1);
        row1.AddChild(_roll);
        _go = new Button { Text = UiTextEn.Translate("Go!"),
            TooltipText = UiTextEn.Translate("Move forward by the rolled points") };
        _go.Pressed += () => Dice(4);
        row1.AddChild(_go);
        v.AddChild(row1);

        // Fonctions f0-f5 — ApplyDestinyFunction natif : f0 ±1,
        // f1 ×2, f2 +3, f3 −4, f4 =5, f5 = rand 1-6 (borné 1-6).
        var row2 = new HBoxContainer();
        row2.AddThemeConstantOverride("separation", 3);
        var fnames = new[] { "±1", "×2", "+3", "−4", "=5", "1-6" };
        for (var i = 0; i < 6; i++)
        {
            var idx = i;
            var b = new Button
            {
                Text = fnames[i],
                TooltipText = UiTextEn.Translate($"Destiny function f{i}"),
                CustomMinimumSize = new Vector2(38, 0),
            };
            b.Pressed += () => Dice(3, (short)idx);
            row2.AddChild(b);
            _funcs[i] = b;
        }
        v.AddChild(row2);

        // Choix de face (item « choisir le dé » armé — RerollFlag).
        var row3 = new HBoxContainer { Visible = false,
            Name = "FaceRow" };
        row3.AddThemeConstantOverride("separation", 3);
        row3.AddChild(new Label { Text = UiTextEn.Translate("Pick:") });
        for (var i = 0; i < 6; i++)
        {
            var idx = i;
            var b = new Button
            {
                Text = $"{i + 1}",
                CustomMinimumSize = new Vector2(28, 0),
            };
            // p2 = face−1 (le serveur pose move = p2+1).
            b.Pressed += () => Dice(3, (short)idx);
            row3.AddChild(b);
            _faces[i] = b;
        }
        v.AddChild(row3);

        var frow = new HBoxContainer();
        frow.AddThemeConstantOverride("separation", 6);
        _fortune = new Label { Text = UiTextEn.Translate("Fortune bags: —") };
        _fortune.AddThemeFontSizeOverride("font_size", 10);
        frow.AddChild(_fortune);
        var fr = new Button
        {
            Text = "⟳", TooltipText = UiTextEn.Translate("Query fortune bag counters (223)"),
            CustomMinimumSize = new Vector2(30, 0),
        };
        fr.Pressed += () => Send(FortuneSender, "223");
        frow.AddChild(fr);
        v.AddChild(frow);

        _event = new Label { Name = "EventLabel", Text = "" };
        _event.AddThemeFontSizeOverride("font_size", 10);
        _event.AddThemeColorOverride("font_color",
            new Color(1f, 0.85f, 0.4f));
        v.AddChild(_event);

        _panel?.AddChild(v);
    }

    private static Label Stat(BoxContainer parent, string text)
    {
        var l = new Label { Text = text };
        l.AddThemeFontSizeOverride("font_size", 11);
        l.AddThemeColorOverride("font_color",
            new Color(0.85f, 0.9f, 1f));
        parent.AddChild(l);
        return l;
    }

    // ------------------------------------------------------------------
    // Rafraîchissement
    // ------------------------------------------------------------------

    /// <summary>Repeint tout depuis <see cref="State"/>.</summary>
    public void Refresh()
    {
        var r = State.Current;
        var has = r != null;
        if (_create != null) _create.Visible = !has;
        if (_roll != null)
        {
            _roll.Visible = has;
            _roll.Disabled = r == null || r.RollsLeft <= 0 ||
                r.MovePoints != 0;
        }
        if (_go != null)
        {
            _go.Visible = has;
            _go.Disabled = r == null || r.MovePoints <= 0;
        }
        for (var i = 0; i < _funcs.Length; i++)
        {
            var b = _funcs[i];
            if (b == null) continue;
            b.Visible = has && !(r?.Reroll ?? false);
            b.Disabled = r == null || r.MovePoints <= 0 ||
                ((r.FunctionState >> i) & 1) != 0;
        }
        var faceRow = _panel?.FindChild("FaceRow", true, false)
            as HBoxContainer;
        if (faceRow != null) faceRow.Visible = r?.Reroll == true;

        if (_vit != null)
            _vit.Text = State.DestinyVit < 0
                ? UiTextEn.Translate("Vit ?") : UiTextEn.Translate($"Vit {State.DestinyVit}");
        if (_dp != null) _dp.Text = UiTextEn.Translate($"DP {r?.DpPool ?? 0}");
        if (_rolls != null) _rolls.Text = UiTextEn.Translate($"Dice {r?.RollsLeft ?? 0}");
        if (_move != null)
            _move.Text = r is { MovePoints: > 0 }
                ? UiTextEn.Translate($"Move {r.MovePoints}") : UiTextEn.Translate("Move -");

        RefreshCells(r);
        RefreshFortune();
    }

    /// <summary>Icônes + marqueur de case du plateau.</summary>
    private void RefreshCells(DestinyState.Route? r)
    {
        for (var i = 0; i < CellCount; i++)
        {
            var cell = _cells[i];
            if (cell == null) continue;
            var eid = r != null && i < r.Slots.Length ? r.Slots[i] : 0;
            cell.Texture = eid > 0 ? EventIcon(eid) : null;
            cell.TooltipText = eid > 0 ? CellTooltip(eid) : "";
            // Cases déjà passées estompées ; case courante dorée.
            cell.Modulate = r != null && i < r.Step
                ? new Color(1, 1, 1, 0.35f)
                : Colors.White;
            if (_marks[i] != null)
                _marks[i]!.Visible = r != null && i == r.Step;
        }
        if (_stepNow != null)
        {
            if (r != null && r.Step is >= 0 and < CellCount &&
                _cells[r.Step] != null)
            {
                _stepNow.Visible = true;
                _stepNow.Position = _cellPos[r.Step] +
                    new Vector2(-2, -2);
            }
            else _stepNow.Visible = false;
        }
        // Le « Use » natif sert au lancer (même état que le bouton
        // Roll ajouté).
        if (_useBtn != null)
            _useBtn.Disabled = r == null || r.RollsLeft <= 0 ||
                r.MovePoints != 0;
        RefreshRoadText();
    }

    /// <summary>Compteurs fortune du dernier 557.</summary>
    private void RefreshFortune()
    {
        if (_fortune == null) return;
        if (State.FortuneBags.Count == 0)
        {
            _fortune.Text = UiTextEn.Translate("Fortune bags: —");
            return;
        }
        var parts = new List<string>();
        foreach (var (bagId, n) in State.FortuneBags)
            parts.Add($"{bagId}×{n}");
        _fortune.Text = UiTextEn.Translate("Bags: ") + string.Join(" ", parts);
    }

    /// <summary>535 : bandeau événement quelques secondes + texte du
    /// plateau.</summary>
    private void OnEventReceived(int eventId)
    {
        _eventTimer = 6.0;
        var t = DestinyTable.EventText(eventId);
        if (_event != null) _event.Text = $"➤ {t}";
        ShowBoardText(t);
    }

    /// <summary>Texte de statut dans le label natif du plateau.</summary>
    private void ShowBoardText(string s)
    {
        if (_boardText != null) _boardText.Text = s;
    }

    /// <summary>Texte par défaut du plateau : route + tier + décor
    /// (ré-affiché quand le bandeau événement expire).</summary>
    private void RefreshRoadText()
    {
        if (_eventTimer > 0) return;
        var r = State.Current;
        var road = r != null ? DestinyTable.Road(r.RoadId) : null;
        ShowBoardText(road != null
            ? $"Road {r!.RoadId} · tier {road.Tier} · {road.Backdrop}"
            : r != null ? $"Road {r.RoadId}" : "No destiny road.");
    }

    public override void _Process(double delta)
    {
        if (_eventTimer <= 0) return;
        _eventTimer -= delta;
        if (_eventTimer <= 0)
        {
            if (_event != null) _event.Text = "";
            RefreshRoadText();
        }
    }

    // ------------------------------------------------------------------
    // Interactions
    // ------------------------------------------------------------------

    /// <summary>Ouvre/ferme la fenêtre ; à l'ouverture une requête
    /// 204 resynchronise la route (le serveur n'émet le 534 que si
    /// une route existe).</summary>
    public void Toggle()
    {
        if (_panel == null) return;
        _panel.Visible = !_panel.Visible;
        if (_panel.Visible) Send(QuerySender, "204");
    }

    /// <summary>Ouvre explicitement.</summary>
    public void Open()
    {
        if (_panel != null && !_panel.Visible)
        {
            _panel.Visible = true;
            Send(QuerySender, "204");
        }
    }

    private void Dice(short op, short p2 = 0, short p3 = 0)
    {
        var send = DiceSender?.Invoke(op, p2, p3);
        if (send != null)
            _ = send.ContinueWith(t => GD.PrintErr(
                    $"[destiny] op{op} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    private void Send(Func<Task>? sender, string tag)
    {
        var send = sender?.Invoke();
        if (send != null)
            _ = send.ContinueWith(t => GD.PrintErr(
                    $"[destiny] {tag} : {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
    }

    // ------------------------- icônes / tooltips -------------------

    /// <summary>Icône d'un eventId — code C_DestinyEvent col2
    /// (fateNN → uiicon, S81016xx → itemicon).</summary>
    private static Texture2D? EventIcon(int eventId)
    {
        var code = DestinyTable.Event(eventId)?.Code;
        if (code == null || code.Length == 0) return null;
        return AssetCatalog.GetTexture($"UI/uiicon/{code}.png")
               ?? AssetCatalog.GetTexture($"UI/itemicon/{code}.png")
               ?? AssetCatalog.GetTexture($"UI/skillicon/{code}.png");
    }

    /// <summary>Tooltip d'une case : nom EN + type d'effet +
    /// paramètres.</summary>
    private static string CellTooltip(int eventId)
    {
        var name = DestinyTable.EventName(eventId);
        var def = DestinyTable.Event(eventId);
        if (def == null) return name;
        var type = DestinyTable.TypeName(def.Type);
        var extra = def.Type switch
        {
            1 => $" {def.Value}g",
            2 => $" {def.Value} exp",
            3 => $" NP+CP {def.Value}",
            4 or 6 => $" mob #{def.Value}×{def.Param2}",
            5 => $" enchant #{def.Value} {def.Param2}t",
            7 => $" +{def.Value} dice",
            _ => "",
        };
        return $"{name}\n{type}{extra}  (#{eventId})";
    }

    // ------------------------- repli / démo ------------------------

    /// <summary>Plateau de repli sans uidata : grille 39 cases.</summary>
    private void BuildFallbackBoard(Control host)
    {
        var grid = new GridContainer { Columns = 8 };
        grid.Position = Vector2.Zero;
        for (var i = 0; i < CellCount; i++)
        {
            var slot = new TextureRect
            {
                CustomMinimumSize = new Vector2(32, 32),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum
                    .KeepAspectCentered,
            };
            var mark = new ReferenceRect
            {
                BorderColor = new Color(1f, 0.85f, 0.25f),
                BorderWidth = 2f,
                EditorOnly = false,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
            };
            mark.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            slot.AddChild(mark);
            grid.AddChild(slot);
            _cells[i] = slot;
            _marks[i] = mark;
            _cellPos[i] = slot.Position;
        }
        host.AddChild(grid);
    }

    /// <summary>Données de démonstration pour --hud-open=destiny hors
    /// session : une route factice à cases connues + état milieu de
    /// partie.</summary>
    public void EnsureDemo()
    {
        var road = new ZonePlayerDestinyRoad
        {
            RoadId = 4, RoadType = 1, Step = 7, RollsLeft = 3,
            DpPool = 8400, MovePoints = 4, FunctionState = 0b001010,
            RerollFlag = 0,
        };
        // Cases variées : fate + events à paramètres.
        short[] ids = { 1, 2, 10, 11, 20, 30, 40, 50, 60, 70, 80, 90,
            100, 110, 120, 130, 140, 150, 160, 170, 180, 190, 200, 210 };
        for (var i = 0; i < 39; i++)
            road.Slots.Add(ids[i % ids.Length]);
        State.Apply(road);
        State.Apply(new ZoneUpdateDestinyRoad
        { Type = 7, V1 = 620 });
        State.Apply(new ZonePlayerDestinyRoadEvent { EventId = 2 });
        var m = new ZoneUpdateFortuneBagCounter();
        m.Entries.Add((1001, 2));
        m.Entries.Add((2045, 1));
        State.Apply(m);
        Open();
    }
}
