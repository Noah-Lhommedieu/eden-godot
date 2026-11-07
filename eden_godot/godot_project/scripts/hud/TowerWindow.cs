using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>Tour du Ciel — fenêtre native <c>towerwnd.layout</c> ouverte
/// par le service NPC type 55 (<c>Service 7130 55</c>, NPC 53175 à S001,
/// cond. niveau ≥70) via 579 <see cref="ZoneClientOpenAreaDialog"/>
/// <c>[s16 0xe][s16 0]</c> (dispatch natif zone — le refId est vide dans
/// le record c_npc). « Entrer » émet
/// <see cref="ZoneServerTeamOperation"/> <c>[4][2][0]</c> — handler
/// client natif <c>FUN_009f18b0</c> @0x9f18b0 :
/// <c>FUN_006cbd00(4,2,-1)</c> et le 3e champ tombe à 0 quand p3&lt;1
/// (<c>FUN_006b99b0</c>). L'entrée effective (étages, nœuds 280-283,
/// transport) reste côté serveur — sous-système tour absent, consigné
/// dans EDENFR-GAPS ; le bouton reproduit le wire natif pour la suite.</summary>
public partial class TowerWindow : Control
{
    private Control? _window;
    private bool _native, _building;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        EnsureWindow();
        Visible = false;
        MiscPackets.Received += OnPacket;
    }

    /// <summary>Construction paresseuse — le ctx uidata n'est pas prêt
    /// au _Ready du game.tscn (même motif que CombineWindow).</summary>
    private void EnsureWindow()
    {
        if (_native || _building) return;
        _building = true;
        var w = HudExtensions.FindHud()?.Build("towerwnd.layout");
        if (w == null) { _building = false; return; }
        _window?.QueueFree();
        _window = w;
        _native = true;
        AddChild(_window);
        HudExtensions.CenterNative(_window, new Vector2(330, 320));
        BindNative(_window);
        _window.Visible = false;
        _building = false;
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
            case ZoneClientOpenAreaDialog a when a.Field1 == 0xe:
                EnsureWindow();
                if (_window == null) return;
                Fill();
                _window.Visible = true;
                Visible = true;
                break;
            case ZoneClientTeamCheckNotify n when n.Field1 is 2 or 4:
                // Consentement / fin de check : la boîte de consent
                // (EvaluateWindow) remplace la fenêtre d'entrée.
                if (_window != null) _window.Visible = false;
                Visible = false;
                break;
        }
    }

    /// <summary>Champs Info/* du layout natif : le nom est celui du
    /// perso ; les compteurs d'étage (Hisfloor/floor/Teamfloor/Date)
    /// dépendent de la progression tour — absente côté serveur, on
    /// laisse le libellé natif « 0 » plutôt qu'inventer une donnée.</summary>
    private void Fill()
    {
        var name = SessionCarry.Character?.Name ?? "";
        SetText("Info/Name", name);
    }

    private void BindNative(Control w)
    {
        var enter = FindLeaf<BaseButton>(w, "EnterBtn");
        if (enter != null) enter.Pressed += OnEnter;
        var close = FindLeaf<BaseButton>(w, "CloseBtn");
        if (close != null) close.Pressed += () => w.Visible = false;
    }

    /// <summary>TeamOperation [4][2][0] — wire natif du bouton Entrer
    /// (TowerWnd.cpp → FUN_006cbd00(4,2,-1), p3&lt;1 → p2=0).</summary>
    private void OnEnter()
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        _ = s.SendZoneAsync(new ZoneServerTeamOperation
            { Op = 4, Param1 = 2, Param2 = 0 },
            System.Threading.CancellationToken.None)
            .ContinueWith(t => GD.PrintErr(
                $"[tower] enter : {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions
                    .OnlyOnFaulted);
    }

    private void SetText(string leaf, string text)
    {
        if (_window == null) return;
        if (FindLeaf<Label>(_window, leaf) is { } l) l.Text = text;
        else if (FindLeaf<RichTextLabel>(_window, leaf) is { } r)
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
}
