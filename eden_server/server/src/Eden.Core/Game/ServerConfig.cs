using System.Globalization;
using Eden.Core.Logging;
namespace Eden.Core.Game;

/// <summary>
/// Config de gameplay serveur « à chaud » (plan 3.3.1) : un fichier texte
/// <c>server.config.txt</c> (clé = valeur, <c>#</c> commentaire) qui permet
/// de changer vite les taux et événements SANS recompiler ni redémarrer —
/// le fichier est relu automatiquement dès que sa date de modification
/// change (vérifiée au plus toutes les 2 s).
///
/// Résolution du fichier : <c>EDEN_SERVER_CONFIG</c> (chemin), sinon
/// <c>server.config.txt</c> cherché en remontant depuis le répertoire
/// courant puis depuis <c>AppContext.BaseDirectory</c>. Absent → défauts
/// (comportement inchangé). Coexiste avec <c>balance.ini</c> (ratios de
/// formules) et <c>EXP_RATE</c>/<c>CLASS_EXP_RATE</c> (env) : une clé
/// présente dans ce fichier est prioritaire sur l'env.
///
/// Clés : ExpRate, ClassExpRate, WakenClassExpRate, DropRate,
/// ClassDropRate, GoldRate, CraftSuccessBonusPct, MoveSpeedBasePct,
/// MultiStrokeChanceMult, MultiStrokeDamageMult, MultiStrokeMaxHits,
/// Event.&lt;nom&gt; (on/off) + Event.&lt;nom&gt;.ExpPct/.DropPct/.GoldPct
/// (bonus en %) + Event.&lt;nom&gt;.DropItem = itemId:chance%[:qté].
/// </summary>
public static class ServerConfig
{
    public sealed record EventDrop(string Event, int ItemId,
        double ChancePct, int Quantity);

    /// <summary>Instantané immuable des valeurs (swap atomique au reload).</summary>
    public sealed class Snapshot
    {
        public double? ExpRate, ClassExpRate, WakenClassExpRate;
        public double DropRate = 1, ClassDropRate = 1, GoldRate = 1;
        public int CraftSuccessBonusPct;
        public double MoveSpeedBasePct = 100;
        /// <summary>Enchant dev de vitesse (0 = aucun) posé à l'entrée ; commande 2051|DevMoveSpeedBuffPct.</summary>
        public int DevMoveSpeedBuffId = 65001;
        public double DevMoveSpeedBuffPct = 200;
        /// <summary>OBSOLÈTE (plan 13) : la jauge BP vient de S_Monster col 13 (patience) ; conservé pour ne pas casser les server.config.txt existants.</summary>
        public double BpGaugePct = 15;
        public double MultiStrokeChanceMult = 1, MultiStrokeDamageMult = 1;
        public int MultiStrokeMaxHits = 2;
        public Dictionary<string, EventDef> Events = new(
            StringComparer.OrdinalIgnoreCase);
    }

    public sealed class EventDef
    {
        public bool Active;
        public double ExpPct, DropPct, GoldPct;
        public List<(int ItemId, double ChancePct, int Qty)> Drops = new();
    }

    private static Snapshot _cur = new();
    private static string? _path;
    private static DateTime _stamp;
    private static long _lastCheckTicks;
    private static readonly object _lock = new();

    public static Snapshot Current { get { Poll(); return _cur; } }
    public static string? Path => _path;

    // ---------- Valeurs effectives ----------
    public static IEnumerable<string> ActiveEvents =>
        Current.Events.Where(e => e.Value.Active).Select(e => e.Key);

    public static bool IsEventActive(string name) =>
        Current.Events.TryGetValue(name, out var e) && e.Active;

    private static double EventPct(Func<EventDef, double> sel) =>
        Current.Events.Values.Where(e => e.Active).Sum(sel);

    /// <summary>Taux d'exp perso effectif : base (fichier, sinon
    /// <paramref name="envRate"/>) × (1 + bonus d'événements actifs).</summary>
    public static double EffectiveExpRate(double envRate) =>
        (Current.ExpRate ?? envRate) * (1 + EventPct(e => e.ExpPct) / 100);

    /// <summary>Taux d'exp de classe : classe éveil (21-60) =
    /// WakenClassExpRate si défini, sinon ClassExpRate.</summary>
    public static double EffectiveClassExpRate(double envRate,
        bool awakened)
    {
        var s = Current;
        var b = awakened && s.WakenClassExpRate is { } w ? w
            : s.ClassExpRate ?? envRate;
        return b * (1 + EventPct(e => e.ExpPct) / 100);
    }

    /// <summary>Multiplicateur des chances de drop normal.</summary>
    public static double EffectiveDropRate =>
        Current.DropRate * (1 + EventPct(e => e.DropPct) / 100);
    public static double EffectiveClassDropRate =>
        Current.ClassDropRate * (1 + EventPct(e => e.DropPct) / 100);
    public static double EffectiveGoldRate =>
        Current.GoldRate * (1 + EventPct(e => e.GoldPct) / 100);

    /// <summary>Drops supplémentaires des événements actifs.</summary>
    public static IEnumerable<EventDrop> ActiveEventDrops() =>
        Current.Events.Where(e => e.Value.Active).SelectMany(e =>
            e.Value.Drops.Select(d =>
                new EventDrop(e.Key, d.ItemId, d.ChancePct, d.Qty)));

    public static int CraftSuccessBonusPct => Current.CraftSuccessBonusPct;

    /// <summary>Multiplicateur de vitesse de base joueur (1.0 = 100 %).</summary>
    public static double BpGaugePct => Current.BpGaugePct;

    public static int DevMoveSpeedBuffId => Current.DevMoveSpeedBuffId;
    public static double DevMoveSpeedBuffPct => Current.DevMoveSpeedBuffPct;

    public static float MoveSpeedBaseMul =>
        (float)(Current.MoveSpeedBasePct / 100.0);

    // ---------- Chargement ----------
    /// <summary>Premier chargement (idempotent). Appelé au démarrage de
    /// la zone ; ensuite <see cref="Poll"/> recharge sur changement.</summary>
    public static void Init()
    {
        _path = Resolve();
        _stamp = default;
        Reload(force: true);
        _timer ??= new Timer(_ => Poll(), null, 2000, 2000);
    }
    private static Timer? _timer;

    private static string? Resolve()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_SERVER_CONFIG");
        if (!string.IsNullOrWhiteSpace(env)) return env;
        foreach (var start in new[] { Directory.GetCurrentDirectory(),
                     AppContext.BaseDirectory })
        {
            var d = new DirectoryInfo(start);
            for (var i = 0; d != null && i < 12; i++, d = d.Parent)
            {
                var p = System.IO.Path.Combine(d.FullName,
                    "server.config.txt");
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    private static void Poll()
    {
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastCheckTicks) < 2000) return;
        Interlocked.Exchange(ref _lastCheckTicks, now);
        Reload(force: false);
    }

    private static void Reload(bool force)
    {
        lock (_lock)
        {
            try
            {
                if (_path == null && force) { Log("aucun fichier — défauts"); return; }
                if (_path == null || !File.Exists(_path))
                {
                    if (_stamp != default) { _cur = new(); _stamp = default; Log("fichier retiré — défauts"); }
                    return;
                }
                var t = File.GetLastWriteTimeUtc(_path);
                if (!force && t == _stamp) return;
                _stamp = t;
                _cur = Parse(File.ReadAllLines(_path));
                Log($"chargé {_path}");
            }
            catch (Exception ex)
            {
                EdenLog.Write($"[ServerConfig] erreur lecture : {ex.Message} — valeurs précédentes conservées");
            }
        }
    }

    private static void Log(string what)
    {
        var s = _cur;
        EdenLog.Write($"[ServerConfig] {what} exp={(s.ExpRate?.ToString(CultureInfo.InvariantCulture) ?? "env")} " +
            $"classExp={(s.ClassExpRate?.ToString(CultureInfo.InvariantCulture) ?? "env")} " +
            $"wakenExp={(s.WakenClassExpRate?.ToString(CultureInfo.InvariantCulture) ?? "=class")} " +
            $"drop={s.DropRate} classDrop={s.ClassDropRate} gold={s.GoldRate} " +
            $"craft+{s.CraftSuccessBonusPct}% moveBase={s.MoveSpeedBasePct}% " +
            $"multiStroke x{s.MultiStrokeChanceMult}/x{s.MultiStrokeDamageMult}/max{s.MultiStrokeMaxHits} " +
            $"events=[{string.Join(",", s.Events.Where(e => e.Value.Active).Select(e => e.Key))}]");
    }

    /// <summary>Parse pur (testable) — lignes de fichier → instantané.
    /// Valeurs invalides ignorées (défaut conservé).</summary>
    public static Snapshot Parse(IEnumerable<string> lines)
    {
        var s = new Snapshot();
        static bool D(string v, out double d) => double.TryParse(v,
            NumberStyles.Float, CultureInfo.InvariantCulture, out d);
        static bool On(string v) => v.Trim().ToLowerInvariant()
            is "1" or "on" or "true" or "yes" or "oui";
        EventDef Ev(string n)
        {
            if (!s.Events.TryGetValue(n, out var e))
                s.Events[n] = e = new EventDef();
            return e;
        }
        foreach (var raw in lines)
        {
            var line = raw;
            var h = line.IndexOf('#');
            if (h >= 0) line = line[..h];
            line = line.Trim();
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var k = line[..eq].Trim();
            var v = line[(eq + 1)..].Trim();
            if (k.StartsWith("Event.", StringComparison.OrdinalIgnoreCase))
            {
                var rest = k[6..];
                var dot = rest.IndexOf('.');
                if (dot < 0) { Ev(rest).Active = On(v); continue; }
                var ev = Ev(rest[..dot]);
                var sub = rest[(dot + 1)..].ToLowerInvariant();
                if (sub == "dropitem")
                {
                    var p = v.Split(':');
                    if (p.Length >= 2 && int.TryParse(p[0], out var id) &&
                        D(p[1], out var ch))
                        ev.Drops.Add((id, ch,
                            p.Length > 2 && int.TryParse(p[2], out var q)
                                ? Math.Max(1, q) : 1));
                }
                else if (D(v, out var n))
                    switch (sub)
                    {
                        case "exppct": ev.ExpPct = n; break;
                        case "droppct": ev.DropPct = n; break;
                        case "goldpct": ev.GoldPct = n; break;
                    }
                continue;
            }
            if (!D(v, out var x)) continue;
            switch (k.ToLowerInvariant())
            {
                case "exprate": if (x >= 0) s.ExpRate = x; break;
                case "classexprate": if (x >= 0) s.ClassExpRate = x; break;
                case "wakenclassexprate": if (x >= 0) s.WakenClassExpRate = x; break;
                case "droprate": if (x >= 0) s.DropRate = x; break;
                case "classdroprate": if (x >= 0) s.ClassDropRate = x; break;
                case "goldrate": if (x >= 0) s.GoldRate = x; break;
                case "craftsuccessbonuspct": s.CraftSuccessBonusPct = (int)x; break;
                case "movespeedbasepct": if (x > 0) s.MoveSpeedBasePct = x; break;
                case "devmovespeedbuffid": s.DevMoveSpeedBuffId = Math.Clamp((int)x, 0, 65535); break;
                case "devmovespeedbuffpct": if (x >= 0) s.DevMoveSpeedBuffPct = x; break;
                case "bpgaugepct": if (x > 0) s.BpGaugePct = x; break;
                case "multistrokechancemult": if (x >= 0) s.MultiStrokeChanceMult = x; break;
                case "multistrokedamagemult": if (x >= 0) s.MultiStrokeDamageMult = x; break;
                case "multistrokemaxhits": s.MultiStrokeMaxHits = Math.Clamp((int)x, 0, 2); break;
            }
        }
        return s;
    }
}
