using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>Trial gladiateur « 毀滅幻境 » (nœuds type 18) —
/// consentement d'équipe et HUD d'évaluation :
/// <list type="bullet">
/// <item><b>577</b> <see cref="ZoneClientTeamCheckNotify"/> : type 3 →
/// dialogue « Rejoindre le trial ? » (accept → C→S 79
/// TeamOperation [5][2], refus → [5][1]) ; type 4 → ferme.</item>
/// <item><b>607</b> <see cref="ZoneClientEvaluateInfo"/> : statut,
/// compteurs kills/morts, résultat + grade, boîtes loterie.</item>
/// <item><b>525</b> gladiator info et <b>608</b> record → chat.</item>
/// <item><b>579</b> <see cref="ZoneClientOpenAreaDialog"/> type 0x12
/// (service NPC « evaluate », refId = nodeTid) → fenêtre native
/// <c>evaluatewnd.layout</c> ; EnterBtn → TeamOperation [4][3][nodeTid]
/// (handler client natif <c>FUN_009f2ac0</c> @0x9f2ac0 :
/// <c>FUN_006cbd00(4,3,dlg+0x110)</c>, +0x110 = Field2 du 579).</item>
/// </list></summary>
public partial class EvaluateWindow : Control
{
    private Panel? _consent;
    private Label? _consentText;
    private Panel? _hud;
    private Label? _hudText;
    private int _consentNode;
    private Control? _trial;
    private bool _trialNative, _buildingTrial;
    private int _trialNode;

    public override void _Ready()
    {
        // Dialogue de consentement — petit panneau centré bas.
        _consent = new Panel { Name = "__evalconsent" };
        _consent.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _consent.Position = new Vector2(-170, 110);
        _consent.Size = new Vector2(340, 110);
        _consent.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.10f, 0.97f),
            BorderColor = new Color(0.55f, 0.5f, 0.3f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(_consent);
        _consentText = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _consentText.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _consentText.OffsetTop = 12;
        _consent.AddChild(_consentText);
        var accept = new Button
        { Name = "EvalAcceptBtn", Text = UiTextEn.Translate("Accept") };
        accept.Position = new Vector2(50, 62);
        accept.Size = new Vector2(100, 32);
        accept.Pressed += () => Answer(0);   // natif : 0 = « Yes » (set[3])
        _consent.AddChild(accept);
        var refuse = new Button
        { Name = "EvalRefuseBtn", Text = UiTextEn.Translate("Refuse") };
        refuse.Position = new Vector2(190, 62);
        refuse.Size = new Vector2(100, 32);
        refuse.Pressed += () => Answer(1);   // natif : ≠0 = « No » (set[1])
        _consent.AddChild(refuse);
        _consent.Visible = false;

        // HUD du run — bandeau haut-centre discret.
        _hud = new Panel { Name = "__evalhud" };
        _hud.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
        _hud.Position = new Vector2(-210, 4);
        _hud.Size = new Vector2(420, 44);
        _hud.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.08f, 0.85f),
        });
        AddChild(_hud);
        _hudText = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _hudText.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _hud.AddChild(_hudText);
        _hud.Visible = false;

        MiscPackets.Received += OnPacket;
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        base._ExitTree();
    }

    private void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientTeamCheckNotify n:
                if (n.Field1 is 2 or 3 or 4)
                {
                    // La boîte de consentement remplace la fenêtre
                    // d'entrée (ouverte par le bouton Entrer).
                    if (_trial != null) _trial.Visible = false;
                }
                if (n.Field1 == 3)
                {
                    _consentNode = n.Field2;
                    if (_consentText != null)
                        _consentText.Text = UiTextEn.Translate(
                            "Your party wants to enter the trial ") +
                            $"(#{_consentNode}).";
                    if (_consent != null) _consent.Visible = true;
                }
                else if (n.Field1 == 2)
                {
                    // Check tour du ciel — 577 [2][étage] : même
                    // DragMsgDlgUI natif que l'épreuve.
                    _consentNode = n.Field2;
                    if (_consentText != null)
                        _consentText.Text = UiTextEn.Translate(
                            "Climb the Sky Tower — floor ") +
                            $"{_consentNode}.";
                    if (_consent != null) _consent.Visible = true;
                }
                else if (_consent != null)
                {
                    _consent.Visible = false;   // type 4 : fin du check
                }
                break;
            case ZoneClientEvaluateInfo e:
                OnEvaluate(e);
                break;
            case ZoneClientGladiatorTeamInfo g:
                (GetParent() as HudRoot)?.AddChat(7, "",
                    UiTextEn.Translate($"Gladiator points {g.Field2} ") +
                    UiTextEn.Translate($"— gold {g.Field3}") +
                    (g.Field1 != 0 ? $" (trial #{g.Field1})" : ""));
                break;
            case ZoneClientUpdateEvaluateInfo r:
                (GetParent() as HudRoot)?.AddChat(7, "",
                    UiTextEn.Translate("New trial record: ") +
                    $"{r.Field3} pts — {r.Field4} " +
                    $"({r.Field2}s)");
                break;
            case ZoneClientOpenAreaDialog a when a.Field1 == 0x12:
                OpenTrial(a.Field2);
                break;
        }
    }

    // ------------------- dialogue d'entrée (579 0x12) -------------------

    /// <summary>579 [0x12][nodeTid] — ouvre <c>evaluatewnd.layout</c>
    /// (construction paresseuse : ctx uidata pas prêt au _Ready).</summary>
    private void OpenTrial(int nodeTid)
    {
        _trialNode = nodeTid;
        EnsureTrialWindow();
        if (_trial == null) return;
        var name = SessionCarry.Character?.Name ?? "";
        SetTrialText("NameInfo", name);
        SetTrialText("DungeonInfo",
            TextResolver.NodeName(nodeTid) ?? $"#{nodeTid}");
        _trial.Visible = true;
    }

    private void EnsureTrialWindow()
    {
        if (_trialNative || _buildingTrial) return;
        _buildingTrial = true;
        var w = HudExtensions.FindHud()?.Build("evaluatewnd.layout");
        if (w == null) { _buildingTrial = false; return; }
        _trial?.QueueFree();
        _trial = w;
        _trialNative = true;
        AddChild(_trial);
        HudExtensions.CenterNative(_trial, new Vector2(330, 320));
        var enter = FindLeaf<BaseButton>(_trial, "EnterBtn");
        if (enter != null) enter.Pressed += OnTrialEnter;
        var close = FindLeaf<BaseButton>(_trial, "CloseBtn");
        if (close != null) close.Pressed += () => _trial.Visible = false;
        _trial.Visible = false;
        _buildingTrial = false;
    }

    /// <summary>EnterBtn → TeamOperation [4][3][nodeTid] — wire natif
    /// du client (FUN_006cbd00(4,3,dlg+0x110)) ; le serveur lance le
    /// consentement d'équipe 577 puis EnterEvaluate.</summary>
    private void OnTrialEnter()
    {
        if (_trial != null) _trial.Visible = false;
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        _ = s.SendZoneAsync(new ZoneServerTeamOperation
            { Op = 4, Param1 = 3, Param2 = (short)_trialNode },
            System.Threading.CancellationToken.None)
            .ContinueWith(t => GD.PrintErr(
                $"[evaluate] enter : {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions
                    .OnlyOnFaulted);
    }

    private void SetTrialText(string leaf, string text)
    {
        if (_trial == null) return;
        if (FindLeaf<Label>(_trial, leaf) is { } l) l.Text = text;
        else if (FindLeaf<RichTextLabel>(_trial, leaf) is { } r)
            r.Text = text;
    }

    private static T? FindLeaf<T>(Node root, string leaf)
        where T : class
    {
        // Pas de filtre « type » : FindChildren ne matche pas les
        // classes script C# — on filtre par cast (motif CombineWindow).
        foreach (var n in root.FindChildren("*", "", true, false))
            if (n is T t && n.Name.ToString().EndsWith(leaf))
                return t;
        return null;
    }

    /// <summary>Réponse au consentement — TeamOperation [5][answer]
    /// (0 = accept « Yes » set[3], ≠0 = refus « No » set[1] —
    /// TeamCheckAnswer/SetCheckState natifs).</summary>
    private void Answer(short answer)
    {
        if (_consent != null) _consent.Visible = false;
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        var task = s.SendZoneAsync(new ZoneServerTeamOperation
        {
            Op = 5, Param1 = answer, Param2 = 0,
        }, System.Threading.CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[evaluate] answer : {t.Exception}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        (GetParent() as HudRoot)?.AddChat(7, "",
            answer == 2
                ? UiTextEn.Translate("Trial accepted.")
                : UiTextEn.Translate("Trial refused."));
    }

    private void OnEvaluate(ZoneClientEvaluateInfo e)
    {
        var v = e.Values;
        switch (e.SubType)
        {
            case 0: // statut [status][sec]
                {
                    var status = v.Length > 0 ? v[0] : -1;
                    var sec = v.Length > 1 ? v[1] : 0;
                    if (_hud != null) _hud.Visible = status >= 0;
                    if (_hudText != null && status >= 0)
                        _hudText.Text = UiTextEn.Translate("Trial — ") +
                            StatusLabel(status) + $" — {Fmt(sec)}";
                    break;
                }
            case 1: // compteurs [raw][deaths][mon][elite][boss][max×4]
                if (_hud != null) _hud.Visible = true;
                if (_hudText != null && v.Length >= 9)
                    _hudText.Text = UiTextEn.Translate("Score ") +
                        $"{v[0]} — " +
                        UiTextEn.Translate($"kills {v[2]}/{v[6]} · ") +
                        $"{v[3]}/{v[7]} · {v[4]}/{v[8]} — " +
                        UiTextEn.Translate($"deaths {v[1]}/{v[5]}");
                break;
            case 2: // résultat
                if (v.Length >= 18)
                {
                    var total = v[15];
                    (GetParent() as HudRoot)?.AddChat(7, "",
                        UiTextEn.Translate("Trial finished: ") +
                        $"{total} pts — {UiTextEn.Translate("grade")} " +
                        $"{e.Grade} ({v[0]}s)");
                }
                break;
            case 3: // loterie [opens][itemId][count][index]
                if (v.Length >= 4 && v[1] > 0)
                    (GetParent() as HudRoot)?.AddChat(7, "",
                        UiTextEn.Translate("Reward box: ") +
                        $"#{v[1]} ×{v[2]}");
                break;
        }
    }

    private static string StatusLabel(int status) => status switch
    {
        1 => UiTextEn.Translate("in progress"),
        2 => UiTextEn.Translate("reward boxes"),
        3 => UiTextEn.Translate("closing"),
        _ => status.ToString(),
    };

    private static string Fmt(int sec) =>
        $"{sec / 3600}:{sec / 60 % 60:D2}:{sec % 60:D2}";

    /// <summary>Contenu factice pour capture (--hud-open).</summary>
    public void EnsureDemo()
    {
        if (_consentText != null)
            _consentText.Text = UiTextEn.Translate(
                "Your party wants to enter the trial (#655).");
        if (_consent != null) _consent.Visible = true;
        if (_hudText != null)
            _hudText.Text = UiTextEn.Translate("Score 1234 — ") +
                "kills 12/32 · 1/2 · 0/2 — deaths 0/1";
        if (_hud != null) _hud.Visible = true;
    }
}
