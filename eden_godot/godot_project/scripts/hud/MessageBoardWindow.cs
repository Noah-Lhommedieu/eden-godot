using Godot;
using EdenClient.Data;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using System.Collections.Generic;

namespace EdenClient.Hud;

/// <summary>Panneau d'affichage (message board) — 455 ouvre +
/// requête liste (C→S 138), 456 alimente la liste, 457/459
/// résultats d'écriture/suppression → re-requête, 458 affiche le
/// corps. Écrire (139) et supprimer (141) câblés. Fenêtre
/// déplaçable façon HUD standard.</summary>
public partial class MessageBoardWindow : Control
{
    private Control? _panel;
    private ItemList? _list;
    private Label? _body;
    private LineEdit? _title, _text;
    private readonly List<int> _ids = new();
    private bool _open;

    public override void _Ready()
    {
        // Layout natif MessageBoardUI : Root plein écran contenant
        // MsgBorardFWin (liste) + ReadFWin/WriteFWin (popups masqués)
        // — le frame interne fait office de fenêtre ; on n'y épingle
        // pas le conteneur.
        var built = (HudExtensions.FindHud()
            ?? GetParent() as HudRoot)?.Build("MessageBoardUI.layout");
        var sz = new Vector2(520, 380);
        if (built != null)
        {
            _panel = TopLeftVitals.Find<Control>(built, "MsgBorardFWin");
            if (_panel == null) built.QueueFree();
            else
            {
                AddChild(built);
                built.SetAnchorsAndOffsetsPreset(
                    LayoutPreset.FullRect);
                built.MouseFilter = MouseFilterEnum.Ignore;
                foreach (var leaf in new[] { "ReadFWin", "WriteFWin" })
                    if (TopLeftVitals.Find<Control>(built, leaf)
                            is { } w) w.Visible = false;
                DragHelper.Persist(_panel, "msgboard");
                sz = _panel.Size;
            }
        }
        if (_panel == null)
        {
        var p = new Panel { Name = "__msgboard" };
        _panel = p;
        p.SetAnchorsAndOffsetsPreset(LayoutPreset.Center);
        p.Position = new Vector2(-260, -190);
        p.Size = sz;
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.06f, 0.09f, 0.97f),
            BorderColor = new Color(0.5f, 0.45f, 0.3f),
            BorderWidthBottom = 1, BorderWidthTop = 1,
            BorderWidthLeft = 1, BorderWidthRight = 1,
        });
        AddChild(p);
        DragHelper.EnsureTitleBar(p, "Panneau d'affichage");
        DragHelper.Persist(p, "msgboard");
        }

        _list = new ItemList();
        _list.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _list.Position = new Vector2(12, 40);
        _list.Size = new Vector2(sz.X - 24, sz.Y - 250);
        _list.ItemSelected += OnSelect;
        _panel.AddChild(_list);

        _body = new Label { Text = "", AutowrapMode = TextServer
            .AutowrapMode.Word };
        // Zone de lecture native claire → encre sombre.
        _body.AddThemeColorOverride("font_color",
            new Color(0.15f, 0.12f, 0.2f));
        _body.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _body.Position = new Vector2(12, sz.Y - 202);
        _body.Size = new Vector2(sz.X - 24, 110);
        _panel.AddChild(_body);

        _title = new LineEdit { PlaceholderText = UiTextEn.Translate("Title") };
        _title.Position = new Vector2(12, sz.Y - 84);
        _title.Size = new Vector2(sz.X - 320, 28);
        _panel.AddChild(_title);
        _text = new LineEdit { PlaceholderText = UiTextEn.Translate("Message") };
        _text.Position = new Vector2(sz.X - 300, sz.Y - 84);
        _text.Size = new Vector2(228, 28);
        _panel.AddChild(_text);

        var write = new Button { Text = UiTextEn.Translate("Write") };
        write.Position = new Vector2(sz.X - 72, sz.Y - 84);
        write.Size = new Vector2(60, 28);
        write.Pressed += OnWrite;
        _panel.AddChild(write);

        var del = new Button { Text = UiTextEn.Translate("Del.") };
        del.Position = new Vector2(sz.X - 72, sz.Y - 40);
        del.Size = new Vector2(60, 28);
        del.Pressed += OnDelete;
        _panel.AddChild(del);

        var close = new Button { Text = UiTextEn.Translate("Close") };
        close.Position = new Vector2(12, sz.Y - 40);
        close.Size = new Vector2(80, 28);
        close.Pressed += () =>
        {
            Send(new ZoneMessageExit());
            Visible = false; _open = false;
        };
        _panel.AddChild(close);

        Visible = false;
        MiscPackets.Received += OnPacket;
    }

    /// <summary>Contenu factice pour capture (--hud-open hors
    /// session).</summary>
    public void EnsureDemo()
    {
        _ids.Clear(); _ids.AddRange(new[] { 31, 32 });
        _list?.Clear();
        _list?.AddItem(UiTextEn.Translate("Looking for guild — Admin"));
        _list?.AddItem(UiTextEn.Translate("Selling bear paws — Trader"));
        if (_body != null)
            _body.Text = UiTextEn.Translate("Welcome to the message board. ") +
                UiTextEn.Translate("Select a message to read it.");
        Visible = true; _open = true;
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
            case ZoneClientEnterMessageBoard:
                Visible = true; _open = true;
                _list?.Clear(); _ids.Clear();
                Send(new ZoneMessageListQuery { Value = 0 });
                break;
            case ZoneClientMessageList e:
                // [u16 flags][i32 id][str titre][str auteur][i32][s16]
                _ids.Add(e.Field2);
                _list?.AddItem($"{e.Field3} — {e.Field4}");
                break;
            case ZoneClientMessageRead rd:
                if (_body != null) _body.Text = rd.Field2;
                break;
            case ZoneClientMessageWriteResult:
            case ZoneClientMessageDeleteResult:
                _list?.Clear(); _ids.Clear();
                Send(new ZoneMessageListQuery { Value = 0 });
                break;
        }
    }

    private void OnSelect(long index)
    {
        if (index >= 0 && index < _ids.Count)
            Send(new ZoneMessageReadRequest { Value = _ids[(int)index] });
    }

    private void OnWrite()
    {
        if (string.IsNullOrWhiteSpace(_title?.Text)) return;
        Send(new ZoneMessageWrite
            { Title = _title!.Text, Text = _text?.Text ?? "" });
        _title.Text = ""; if (_text != null) _text.Text = "";
    }

    private void OnDelete()
    {
        var sel = _list?.GetSelectedItems();
        if (sel != null && sel.Length > 0 && sel[0] < _ids.Count)
            Send(new ZoneMessageDelete { Value = _ids[sel[0]] });
    }

    private static void Send(NetMessage m)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true)
        {
            var task = s.SendZoneAsync(m,
                System.Threading.CancellationToken.None);
            _ = task.ContinueWith(t =>
                GD.PrintErr($"[msgboard] send : {t.Exception}"),
                System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
