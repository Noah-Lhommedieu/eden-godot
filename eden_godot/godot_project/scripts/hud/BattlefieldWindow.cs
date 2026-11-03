using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>Champ de bataille — 462 liste, 463 proposition d'entrée
/// (réponse C→S 145 [bfId][1]), 464 résultat join, 465 score/résultat,
/// 466 état d'autel, 547/548/549 position/membres. Saisie bfId + camp
/// → C→S 144 <see cref="ZoneBattlefieldJoin"/> (relais zone→mission
/// op149 prouvé dans InstanceHandlers).</summary>
public partial class BattlefieldWindow : Control
{
    private Control? _panel;
    private Label? _list, _prompt;
    private LineEdit? _bfId, _side;
    private short _proposedBf = -1;

    public override void _Ready()
    {
        // Layout natif BattlefieldListUI (liste war-planes +
        // WarJoinBtn + LeaveBtn) ; repli panneau plat sinon.
        var built = HudExtensions.BuildNativeWindow(this,
            "BattlefieldListUI.layout", "battlefield",
            new Vector2(440, 300));
        if (built != null)
        {
            _panel = built;
            built.Name = "__battlefield";
            var sz = built.CustomMinimumSize;
            _list = new Label
            {
                Text = "",
                AutowrapMode = TextServer.AutowrapMode.Word,
            };
            _list.Position = new Vector2(16, 44);
            _list.Size = new Vector2(sz.X - 32, sz.Y - 130);
            built.AddChild(_list);
            _prompt = new Label { Text = "" };
            _prompt.SetAnchorsAndOffsetsPreset(
                LayoutPreset.BottomWide);
            _prompt.HorizontalAlignment = HorizontalAlignment.Center;
            _prompt.OffsetTop = -64;
            built.AddChild(_prompt);
            if (TopLeftVitals.Find<BaseButton>(built, "WarJoinBtn")
                    is { } wj)
                wj.Pressed += () => Answer(1);
            if (TopLeftVitals.Find<BaseButton>(built, "LeaveBtn")
                    is { } lv)
                lv.Pressed += () =>
                    Send(new ZoneBattlefieldLeave());
            Visible = false;
            MiscPackets.Received += OnPacket;
            return;
        }
        var p = new Panel { Name = "__battlefield" };
        _panel = p;
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-220, -150);
        p.Size = new Vector2(440, 300);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.06f, 0.05f, 0.97f),
            BorderColor = new Color(0.65f, 0.4f, 0.3f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(_panel);
        DragHelper.EnsureTitleBar(_panel, "Champ de bataille");
        DragHelper.Persist(_panel, "battlefield");

        _list = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _list.Position = new Vector2(16, 44);
        _list.Size = new Vector2(408, 96);
        _panel.AddChild(_list);

        _prompt = new Label { Text = "" };
        _prompt.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _prompt.HorizontalAlignment = HorizontalAlignment.Center;
        _prompt.OffsetTop = 146;
        _panel.AddChild(_prompt);

        var joinPrompt = new Button { Text = UiTextEn.Translate("Join queue") };
        joinPrompt.Position = new Vector2(150, 172);
        joinPrompt.Size = new Vector2(140, 30);
        joinPrompt.Pressed += () => Answer(1);
        _panel.AddChild(joinPrompt);

        _bfId = new LineEdit { PlaceholderText = "bfId" };
        _bfId.Position = new Vector2(16, 216);
        _bfId.Size = new Vector2(100, 28);
        _panel.AddChild(_bfId);
        _side = new LineEdit { PlaceholderText = UiTextEn.Translate("camp") };
        _side.Position = new Vector2(124, 216);
        _side.Size = new Vector2(100, 28);
        _panel.AddChild(_side);
        var join = new Button { Text = UiTextEn.Translate("Join") };
        join.Position = new Vector2(232, 216);
        join.Size = new Vector2(100, 28);
        join.Pressed += Join;
        _panel.AddChild(join);
        var leave = new Button { Text = UiTextEn.Translate("Leave") };
        leave.Position = new Vector2(340, 216);
        leave.Size = new Vector2(84, 28);
        leave.Pressed += () => Send(new ZoneBattlefieldLeave());
        _panel.AddChild(leave);

        var close = new Button { Text = UiTextEn.Translate("Close") };
        close.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        close.Position = new Vector2(-45, -34);
        close.Size = new Vector2(90, 26);
        close.Pressed += () => Visible = false;
        _panel.AddChild(close);

        Visible = false;
        MiscPackets.Received += OnPacket;
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        base._ExitTree();
    }

    /// <summary>Contenu factice pour capture (--hud-open hors
    /// session).</summary>
    public void EnsureDemo()
    {
        _proposedBf = 3;
        if (_list != null) _list.Text =
            UiTextEn.Translate("War plains:\n  Aurea War  —  12/20  open\n") +
            UiTextEn.Translate("  Crystal Field  —  8/20  open");
        if (_prompt != null)
            _prompt.Text = UiTextEn.Translate("Entry offered (bf 3)");
        Visible = true;
    }

    private void OnPacket(NetMessage m)
    {
        switch (m)
        {
            case ZoneClientBattlefieldList b:
                Visible = true;
                if (_list != null) _list.Text =
                    $"Champs : {b.Field1} {b.Field2} {b.Field3} " +
                    $"{b.Field4} {b.Field5} {b.Field6}";
                break;
            case ZoneClientEnterBattlefield e:
                Visible = true;
                _proposedBf = e.Value;
                if (_prompt != null)
                    _prompt.Text = UiTextEn.Translate($"Entry offered (bf {e.Value})");
                break;
            case ZoneClientBattlefieldJoinResult j:
                Chat(UiTextEn.Translate($"File : {j.Field1}/{j.Field2}/{j.Field3} ") +
                    UiTextEn.Translate($"inst={j.Field4}"));
                break;
            case ZoneClientBattlefieldResult r:
                Chat(UiTextEn.Translate($"Result: {r.Field4} ({r.Field1})"));
                break;
            case ZoneClientBattlefieldUpdateScore s:
                if (_list != null) _list.Text =
                    UiTextEn.Translate($"Score : {s.Field1} {s.Field2} {s.Field3} {s.Field4}");
                break;
        }
    }

    private void Join()
    {
        short.TryParse(_bfId?.Text, out var bf);
        short.TryParse(_side?.Text, out var side);
        Send(new ZoneBattlefieldJoin
            { Field1 = bf, Field2 = side, Field3 = 0 });
    }

    private void Answer(short accept)
    {
        Send(new ZoneEnterBattlefieldAnswer
            { Field1 = _proposedBf, Field2 = accept });
    }

    private void Chat(string t) =>
        (GetParent() as HudRoot)?.AddChat(7, "", t);

    private static void Send(NetMessage m)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true)
        {
            var task = s.SendZoneAsync(m,
                System.Threading.CancellationToken.None);
            _ = task.ContinueWith(t =>
                GD.PrintErr($"[battlefield] send : {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
