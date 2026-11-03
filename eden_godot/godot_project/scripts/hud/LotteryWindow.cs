using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>Fenêtre loterie/autels — ouverte par 511
/// <see cref="ZoneClientUpdateLottery"/> (et 436/437/438 updates) ;
/// un bouton « Tirer » par autel envoie C→S 129
/// <see cref="ZoneServerGoLottery"/> — wire prouvé
/// <c>ZoneEntryFlow.Lottery</c> : [s16 autel][u16 ticketId][s16 0]
/// [s16 0][s16 slot=-1][u32 bag=0]. Résultats 448/fin 449 et
/// notices 552/607/608 → corps + chat.</summary>
public partial class LotteryWindow : Control
{
    // Configs verbatim LotteryService (TicketRequestId par autel).
    private static readonly (string Nom, int Ticket)[] Altars =
    {
        ("Autel normal", 0x9d31),
        ("Loterie en jeu", 0x66ac),
        ("Loterie diamant", 0x6c91),
    };

    private Panel? _panel;
    private Label? _status;

    public override void _Ready()
    {
        _panel = new Panel { Name = "__lottery" };
        _panel.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        _panel.Position = new Vector2(-200, -140);
        _panel.Size = new Vector2(400, 280);
        _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.05f, 0.10f, 0.97f),
            BorderColor = new Color(0.65f, 0.5f, 0.25f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(_panel);
        DragHelper.EnsureTitleBar(_panel, "Loterie");
        DragHelper.Persist(_panel, "lottery");

        for (var i = 0; i < Altars.Length; i++)
        {
            var idx = i;
            var row = new Label { Text = Altars[i].Nom };
            row.Position = new Vector2(20, 52 + i * 48);
            row.Size = new Vector2(250, 30);
            _panel.AddChild(row);
            var go = new Button { Text = UiTextEn.Translate("Draw") };
            go.Position = new Vector2(280, 48 + i * 48);
            go.Size = new Vector2(100, 32);
            go.Pressed += () => Go(idx);
            _panel.AddChild(go);
        }

        _status = new Label
        {
            Text = "",
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        _status.Position = new Vector2(16, 200);
        _status.Size = new Vector2(368, 64);
        _panel.AddChild(_status);

        Visible = false;
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
            case ZoneClientUpdateLottery u:
                Visible = true;
                if (_status != null) _status.Text =
                    UiTextEn.Translate($"State {u.Field1} — {u.Field2}/{u.Field3}/") +
                    $"{u.Field4} {u.Field5} {u.Field6}";
                break;
            case ZoneClientLotteryResult r:
                Visible = true;
                if (_status != null) _status.Text =
                    UiTextEn.Translate($"Result: {r.Field1} {r.Field2} {r.Field3} ") +
                    $"{r.Field4} {r.Field5} {r.Field6}";
                (GetParent() as HudRoot)?.AddChat(7, "",
                    _status!.Text);
                PlayerBag.RaiseChanged();
                break;
            case ZoneClientLotteryEnd:
                if (_status != null) _status.Text = UiTextEn.Translate("Draw finished.");
                break;
            case ZoneClientInGameLotteryNotify n:
                (GetParent() as HudRoot)?.AddChat(7, "",
                    $"Loterie : {n.Value}");
                break;
            case ZoneClientDiamondLotteryNotify d:
                (GetParent() as HudRoot)?.AddChat(7, "",
                    $"Loterie diamant : {d.Value}");
                break;
        }
    }

    /// <summary>129 GoLottery — [s16 autel][u16 ticket][s16 0]
    /// [s16 0][s16 -1][u32 0] (format client réel, désasm
    /// FUN_006cdbe0 via ZoneEntryFlow.Lottery).</summary>
    private void Go(int altar)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        var task = s.SendZoneAsync(new ZoneServerGoLottery
        {
            Field1 = (short)altar,
            Field2 = (ushort)Altars[altar].Ticket,
            Field3 = 0, Field4 = 0, Field5 = -1, Field6 = 0,
        }, System.Threading.CancellationToken.None);
        _ = task.ContinueWith(t =>
            GD.PrintErr($"[lottery] go : {t.Exception}"),
            System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
    }
}
