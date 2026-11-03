using System.Linq;
using Godot;

namespace EdenClient.Hud;

/// <summary>Sonde <c>--ride-test</c> : monte la monture 20051 (double-clic
/// inventaire) puis laisse le jeu ouvert pour captures Flameshot
/// (idle/marche/course/saut, touches XTEST externes).</summary>
public sealed partial class RideProbe : Node
{
    private readonly HudRoot _hud;
    private int _frame;
    private ulong _t0;
    private bool _second;

    public RideProbe(HudRoot hud)
    {
        _hud = hud;
        Name = "__ride_probe__";
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_hud.Inventory is not InventoryWindow inv) return;
        if (_frame % 60 == 0 && GetTree().Root
                .FindChildren("*", "CharacterBody3D", true, false)
                .OfType<EdenClient.Player.PlayerController>()
                .FirstOrDefault() is { } pc && pc.MountAnimator != null)
            GD.Print($"[ride-test] cavalier={pc.Animator?.Current} " +
                $"monture={pc.MountAnimator.Current}");
        // Le serveur bascule monter/descendre : le perso peut déjà être
        // monté à la connexion → 2e usage `--ride-test=2` après 20 s.
        var again = System.Environment.GetEnvironmentVariable("RIDE_TWICE") == "1";
        if (_frame == 300) _t0 = Time.GetTicksMsec();
        else if (!(again && _t0 > 0 && !_second &&
                   Time.GetTicksMsec() - _t0 > 20000)) return;
        else _second = true;
        var it = inv.KnownSlots.Values.FirstOrDefault(i => i.ItemId == 20051);
        GD.Print($"[ride-test] monter item=20051 -> {(it == null ? "ABSENT" : $"sac{it.Bag} slot{it.Slot}")}");
        if (it != null) inv.OnCellDoubleClick(it.Bag, it.Slot);
    }
}

/// <summary>Debug placement monture : <c>RIDE_KEY=14010</c> (env) ou
/// <c>--ride-key=14010</c> force <c>SetRideModel</c> sur ce rideKey sans
/// serveur de ride ; ensuite le fichier <c>/tmp/ride_key.txt</c>
/// (« key [yawDeg pitchDeg dist hauteur] ») est relu à chaque
/// changement : un seul client live pour parcourir toutes les montures.
/// Caméra : yaw 90 = vue de côté.</summary>
public static class RideDebug
{
    public readonly record struct Req(int Key, float Yaw, float Pitch,
        float Dist, float Height, float Drop);

    /// <summary>Traces [ride-seat] (env RIDE_VERBOSE=1 ou fichier /tmp/ride_verbose).</summary>
    public static bool Verbose =>
        System.Environment.GetEnvironmentVariable("RIDE_VERBOSE") == "1" ||
        System.IO.File.Exists("/tmp/ride_verbose");

    private const string FilePath = "/tmp/ride_key.txt";
    private static bool _init, _on;
    private static Req? _pending;
    private static long _stamp;
    private static int _tick;
    private static bool _fileOn;

    public static Req? Poll()
    {
        if (!_init)
        {
            _init = true;
            var v = System.Environment.GetEnvironmentVariable("RIDE_KEY");
            foreach (var a in OS.GetCmdlineUserArgs())
                if (a.StartsWith("--ride-key=")) v = a[11..];
            if (!string.IsNullOrEmpty(v)) { _on = true; _pending = Parse(v); }
        }
        // Sans env : le fichier suffit (clé -1 = caméra seule, la monture
        // posée par le serveur reste).
        if (!_on && ++_tick % 30 != 0 && !_fileOn) return null;
        try
        {
            _fileOn = System.IO.File.Exists(FilePath);
            if (!_on && !_fileOn) return null;
            var st = System.IO.File.GetLastWriteTimeUtc(FilePath).Ticks;
            if (st != _stamp && System.IO.File.Exists(FilePath))
            {
                _stamp = st;
                // Fichier vide (touch oublié) : pas de caméra debug,
                // sinon le yaw 90 par défaut fait tourner le perso sans fin.
                var txt = System.IO.File.ReadAllText(FilePath);
                if (string.IsNullOrWhiteSpace(txt)) return _pending;
                _pending = Parse(txt);
                GD.Print($"[ride-debug] {_pending}");
            }
        }
        catch { /* fichier en cours d'écriture */ }
        return _pending;
    }

    public static void Ack() => _pending = null;

    private static Req Parse(string txt)
    {
        var t = txt.Split(new[] { ' ', ',', '\n', '\t' },
            System.StringSplitOptions.RemoveEmptyEntries);
        float F(int i, float d) => i < t.Length &&
            float.TryParse(t[i], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var f) ? f : d;
        return new Req((int)F(0, 0), F(1, 90), F(2, 10), F(3, 7), F(4, 1.6f),
            F(5, 0f));
    }
}
