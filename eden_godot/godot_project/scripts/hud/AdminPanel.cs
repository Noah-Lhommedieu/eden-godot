using System.Text;
using System.Text.Json;
using Godot;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Panneau d'administration en jeu (PLAN-ACTION-3 D6). Aucune logique : chaque bouton envoie une requête JSON à la couche d'administration
/// partagée du serveur (paquet custom Reborn 9067 → réponse 9068, voir <c>Eden.Zone/Features/Admin/AdminBridge.cs</c>) et affiche la réponse.
/// Le bouton « Admin » n'apparaît que si le serveur répond à <c>whoami</c> avec succès : un compte sans niveau ne voit rien.
/// </summary>
public partial class AdminPanel : Control
{
    public const int RequestNcid = 9067, ResponseNcid = 9068;

    private static readonly Queue<string> Replies = new();
    private static readonly object Lock = new();
    private readonly Button _open = new() { Text = "Admin", Position = new Vector2(128, 202), Visible = false };
    private readonly PanelContainer _win = new() { Visible = false, Position = new Vector2(128, 236) };
    private readonly LineEdit _target = new() { PlaceholderText = "cible : nom, me ou all", Text = "me" };
    private readonly LineEdit _item = new() { PlaceholderText = "id objet" };
    private readonly LineEdit _qty = new() { PlaceholderText = "quantité", Text = "1" };
    private readonly LineEdit _reason = new() { PlaceholderText = "motif (obligatoire, journalisé)" };
    private readonly LineEdit _totp = new() { PlaceholderText = "code 2FA (si configuré)" };
    private readonly RichTextLabel _out = new() { CustomMinimumSize = new Vector2(460, 170), ScrollFollowing = true, BbcodeEnabled = false };
    private EdenSession? _bound;
    private bool _isAdmin, _probed;
    private double _wait;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(470, 0) };
        box.AddChild(new Label { Text = "Administration" });
        foreach (var f in new Control[] { _target, _item, _qty, _reason, _totp }) box.AddChild(f);
        var row = new HBoxContainer();
        foreach (var (label, cmd, dry) in new[] { ("Donner", "give", false), ("Retirer", "remove", false), ("Simuler don", "give", true) })
        {
            var b = new Button { Text = label };
            b.Pressed += () => Send(cmd, new() { ["target"] = _target.Text, ["item"] = _item.Text, ["qty"] = _qty.Text, ["reason"] = _reason.Text }, dry);
            row.AddChild(b);
        }
        box.AddChild(row);
        var row2 = new HBoxContainer();
        foreach (var (label, cmd) in new[] { ("Joueurs", "players"), ("Audit", "audit"), ("Aide", "help"), ("Activer 2FA", "2fa") })
        {
            var b = new Button { Text = label };
            b.Pressed += () => Send(cmd, new(), false);
            row2.AddChild(b);
        }
        box.AddChild(row2); box.AddChild(_out);
        _win.AddChild(box);
        _open.Pressed += () => _win.Visible = !_win.Visible;
        AddChild(_open); AddChild(_win);
    }

    public override void _Process(double delta)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        if (!ReferenceEquals(_bound, s))
        {
            _bound = s; _isAdmin = _probed = false;
            s.OnZone(ResponseNcid, r =>
            {
                var len = r.ReadUShort(); var json = Encoding.UTF8.GetString(r.ReadBytes(len));
                lock (Lock) Replies.Enqueue(json);
            });
        }
        if (!_probed) { _wait -= delta; if (_wait <= 0) { _wait = 5; Send("whoami", new(), false, silent: true); } }
        while (true)
        {
            string? json; lock (Lock) json = Replies.Count > 0 ? Replies.Dequeue() : null;
            if (json == null) break;
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
            var ok = root.GetProperty("ok").GetBoolean(); var msg = root.GetProperty("message").GetString() ?? "";
            var code = root.GetProperty("code").GetString() ?? "";
            if (!_probed) { _probed = true; _isAdmin = ok; _open.Visible = ok; if (ok) _out.Text = msg + "\n"; continue; }
            _out.Text += (ok ? "" : $"[{code}] ") + msg + "\n";
        }
    }

    private void Send(string command, Dictionary<string, string> args, bool dry, bool silent = false)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        var payload = JsonSerializer.Serialize(new { requestId = silent ? "" : Guid.NewGuid().ToString("N"), command, args, dryRun = dry, totp = _totp.Text });
        _ = s.SendZoneAsync(new Request { Json = payload }, CancellationToken.None);
    }

    private sealed class Request : NetMessage
    {
        public string Json { get; set; } = "";
        public override int Opcode => RequestNcid;
        public override string Name => "Zone.AdminRpc";
        public override void Serialize(BinWriter s) { var b = Encoding.UTF8.GetBytes(Json); s.WriteUShort((ushort)b.Length); s.WriteBytes(b); }
        public override void Deserialize(BinReader s) { }
    }
}
