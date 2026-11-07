using Godot;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Éphéméride d'Eden : bouton du HUD + frise des 21 prochains jours (paquet custom Reborn 9066, voir
/// <c>Eden.Zone/Features/Modifiers/AlmanaxHandler.cs</c>). Aucune logique : le serveur envoie, par jour, le nom et l'effet chiffré.
/// S→C <c>[i32 jourDeResetAujourdhui][u8 n]</c> puis n × <c>[i32 jour][i32 modèle][u8 forcé][str nom][str détail]</c> (str = u16 len + UTF-8).
/// </summary>
public partial class AlmanaxPanel : Control
{
    public const int Ncid = 9066;

    private sealed record Day(int ResetDay, int TemplateId, bool Forced, string Name, string Detail);

    private static volatile List<Day>? _days;
    private static int _today;
    private readonly Button _button = new() { Text = "Éphéméride", Position = new Vector2(128, 178), Visible = false };
    private readonly PanelContainer _panel = new() { Visible = false, Position = new Vector2(128, 210) };
    private readonly VBoxContainer _list = new();
    private EdenSession? _bound;
    private List<Day>? _shown;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _button.Pressed += () => { _panel.Visible = !_panel.Visible; Request(); };
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(430, 330) };
        scroll.AddChild(_list);
        _panel.AddChild(scroll);
        AddChild(_button); AddChild(_panel);
    }

    public override void _Process(double delta)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        if (!ReferenceEquals(_bound, s))
        {
            _bound = s; _days = null;
            s.OnZone(Ncid, r =>
            {
                var today = r.ReadInt(); var n = r.ReadByte(); var list = new List<Day>(n);
                for (var i = 0; i < n; i++)
                    list.Add(new Day(r.ReadInt(), r.ReadInt(), r.ReadByte() == 1, ReadStr(r), ReadStr(r)));
                _today = today; _days = list;
            });
            Request();
        }
        _button.Visible = true;
        var days = _days;
        if (days != null && !ReferenceEquals(days, _shown)) { _shown = days; Rebuild(days); }
    }

    private static string ReadStr(BinReader r)
    {
        var len = r.ReadUShort(); var b = new byte[len];
        for (var i = 0; i < len; i++) b[i] = r.ReadByte();
        return System.Text.Encoding.UTF8.GetString(b);
    }

    private void Rebuild(List<Day> days)
    {
        foreach (var c in _list.GetChildren()) c.QueueFree();
        foreach (var d in days)
        {
            var offset = d.ResetDay - _today;
            var title = offset == 0 ? "Aujourd'hui" : offset == 1 ? "Demain" : $"Dans {offset} jours";
            var l = new Label { Text = $"{title} — {d.Name}{(d.Forced ? " (verrouillé)" : "")}\n    {d.Detail}", CustomMinimumSize = new Vector2(410, 0) };
            l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            l.AddThemeColorOverride("font_color", offset == 0 ? new Color(1f, 0.84f, 0.2f) : new Color(0.9f, 0.9f, 0.9f));
            _list.AddChild(l);
        }
    }

    private static void Request()
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected == true) _ = s.SendZoneAsync(new Query(), CancellationToken.None);
    }

    private sealed class Query : NetMessage
    {
        public override int Opcode => Ncid;
        public override string Name => "Zone.Almanax";
        public override void Serialize(BinWriter s) { }
        public override void Deserialize(BinReader s) { }
    }
}
