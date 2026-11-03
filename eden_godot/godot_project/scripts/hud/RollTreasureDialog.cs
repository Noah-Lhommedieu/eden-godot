using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>Dés de butin — 520 <see cref="ZoneClientRollTreasure"/>
/// affiche Need/Greed/Pass ; réponse C→S 193
/// <see cref="ZoneServerRollTreasure"/> [u32 treasureId][s16 choice
/// 0=pass/1=need/2=greed] (CheckRollItem @0x84bc220 natif).</summary>
public partial class RollTreasureDialog : Control
{
    private Control? _panel;
    private Label? _itemName;
    private uint _treasureId;

    public override void _Ready()
    {
        // Layout natif RollUI : ItemIcon/ItemName/NeedBtn/GreedBtn/
        // TimeBar — le « pass » natif = fermer la fenêtre (choix 0
        // envoyé quand même pour libérer le jet côté serveur).
        _panel = HudExtensions.BuildNativeWindow(this,
            "RollUI.layout", "rolltreasure", new Vector2(280, 140));
        if (_panel != null)
        {
            _panel.Name = "__roll";
            _itemName = TopLeftVitals.Find<Label>(
                _panel, "ItemName");
            if (TopLeftVitals.Find<BaseButton>(_panel, "NeedBtn")
                    is { } need)
                need.Pressed += () => Roll(1);
            if (TopLeftVitals.Find<BaseButton>(_panel, "GreedBtn")
                    is { } greed)
                greed.Pressed += () => Roll(2);
            var pass = new Button { Text = UiTextEn.Translate("Pass") };
            pass.SetAnchorsAndOffsetsPreset(
                LayoutPreset.CenterBottom);
            pass.Position = new Vector2(-40, -36);
            pass.Size = new Vector2(80, 26);
            pass.Pressed += () => Roll(0);
            _panel.AddChild(pass);
            Visible = false;
            MiscPackets.Received += OnPacket;
            return;
        }
        var p = new Panel { Name = "__roll" };
        _panel = p;
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-140, -50);
        p.Size = new Vector2(280, 100);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.06f, 0.07f, 0.05f, 0.96f),
            BorderColor = new Color(0.5f, 0.6f, 0.35f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        DragHelper.EnsureTitleBar(p, "Loot — roll the dice");
        DragHelper.Persist(p, "rolltreasure");

        AddBtn(UiTextEn.Translate("Need"), -130, 1);
        AddBtn(UiTextEn.Translate("Greed"), -45, 2);
        AddBtn(UiTextEn.Translate("Pass"), 40, 0);

        Visible = false;
        MiscPackets.Received += OnPacket;
    }

    private void AddBtn(string label, int x, short choice)
    {
        var b = new Button { Text = label };
        b.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom);
        b.Position = new Vector2(x, -38);
        b.Size = new Vector2(80, 28);
        b.Pressed += () => Roll(choice);
        _panel!.AddChild(b);
    }

    public override void _ExitTree()
    {
        MiscPackets.Received -= OnPacket;
        base._ExitTree();
    }

    /// <summary>État factice pour capture (--hud-open hors
    /// session).</summary>
    public void EnsureDemo()
    {
        _treasureId = 7;
        if (_itemName != null)
            _itemName.Text = UiTextEn.Translate("Rose Knight's Blade");
        Visible = true;
    }

    private void OnPacket(NetMessage m)
    {
        if (m is ZoneClientRollTreasure r)
        {
            _treasureId = r.Field1;
            Visible = true;
        }
    }

    private void Roll(short choice)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true)
        {
            var task = s.SendZoneAsync(new ZoneServerRollTreasure
                { Field1 = _treasureId, Field2 = choice },
                System.Threading.CancellationToken.None);
            _ = task.ContinueWith(t =>
                GD.PrintErr($"[roll] : {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }
        Visible = false;
    }
}
