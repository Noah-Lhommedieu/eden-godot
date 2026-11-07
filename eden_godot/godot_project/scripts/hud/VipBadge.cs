using Godot;
using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;

namespace EdenClient.Hud;

/// <summary>
/// Badge « VIP » du HUD (paquet custom Reborn 9065, voir <c>Eden.Zone/Features/Modifiers/VipStatusHandler.cs</c>).
/// C→S sans corps ; S→C <c>[u8 vip][i64 finUnix]</c> (0 = permanent). Purement cosmétique : les bonus
/// sont appliqués par le serveur. La requête est renvoyée toutes les 5 s sans réponse, puis toutes les 15 s (octroi/retrait en cours de partie).
/// </summary>
public partial class VipBadge : Control
{
    public const int Ncid = 9065;
    private const double RetrySeconds = 5, RefreshSeconds = 15;

    private static volatile int _state = -1;      // -1 inconnu, 0 non VIP, 1 VIP
    private static long _until;
    private double _wait;
    private EdenSession? _bound;
    private readonly Label _label = new() { Text = "VIP", Visible = false, MouseFilter = MouseFilterEnum.Stop };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;   // le conteneur plein écran ne capte rien ; seul le badge a un tooltip
        _label.AddThemeColorOverride("font_color", new Color(1f, 0.84f, 0.2f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0.25f, 0.1f, 0f));
        _label.AddThemeConstantOverride("outline_size", 4);
        _label.AddThemeFontSizeOverride("font_size", 18);
        _label.Position = new Vector2(128, 154);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        var s = SessionCarry.Session;
        if (s?.Zone?.IsConnected != true) return;
        if (!ReferenceEquals(_bound, s))
        {
            _bound = s; _state = -1;
            s.OnZone(Ncid, r => { var vip = r.ReadByte(); _until = r.ReadLong(); _state = vip; });
        }
        _wait -= delta;
        if (_wait <= 0) { _wait = _state < 0 ? RetrySeconds : RefreshSeconds; _ = s.SendZoneAsync(new Query(), CancellationToken.None); }
        if (_state < 0) return;
        _label.Visible = _state == 1;
        _label.TooltipText = _until == 0 ? "Statut VIP permanent"
            : $"Statut VIP jusqu'au {DateTimeOffset.FromUnixTimeSeconds(_until).ToLocalTime():dd/MM/yyyy HH:mm}";
    }

    private sealed class Query : NetMessage
    {
        public override int Opcode => Ncid;
        public override string Name => "Zone.VipStatus";
        public override void Serialize(BinWriter s) { }
        public override void Deserialize(BinReader s) { }
    }
}
