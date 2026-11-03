using System.Globalization;
using Godot;

namespace EdenClient.Perf;

/// <summary>
/// Sonde de performance en jeu — activée par l'argument
/// <c>--perf</c>. Échantillonne chaque seconde (fps réel, RSS process,
/// moniteurs Godot : draw calls/objets/primitifs de la frame, mémoire
/// statique, VRAM/textures, compte de nœuds par type) et imprime une
/// ligne <c>[perf] …</c> lisible dans le log. Le rapport final
/// <c>[perf-done] …</c> résume la fenêtre (fps min/moy/max, pics).
///
/// Args complémentaires (utilisateurs ou moteur) :
///   --perf-seconds=N   — quitte le jeu après N s (défaut : jamais,
///                        la sonde continue de logguer) ;
///   --perf-out=&lt;fic&gt; — recopie les lignes [perf] dans un fichier ;
///   --perf-nostats     — saute le comptage des nœuds (coûteux sur
///                        une très grosse scène).
///
/// Résolution du problème « mesure sans toucher au flux » : la sonde
/// est ajoutée par <c>GameScene._Ready</c> après la construction du
/// monde ; elle ne modifie ni la scène ni le rendu.
///
/// RSS : lu dans <c>/proc/self/status</c> (Linux) — 0 ailleurs.
/// </summary>
public partial class PerfProbe : Node
{
    private static bool? _enabled;
    /// <summary>True si <c>--perf</c> figure dans les args (moteur ou
    /// utilisateur).</summary>
    public static bool Enabled => _enabled ??= HasArg("--perf");

    private double _quitAfter;
    private string? _outPath;
    private bool _noStats;
    private StreamWriter? _writer;

    private int _frames;
    private double _t0;
    // Fenêtre d'une seconde glissante.
    private double _winAcc;
    private int _winN;
    private double _winMin = double.MaxValue;
    private double _winMax;
    // Globale.
    private int _n;
    private double _sumDt;
    private double _minDt = double.MaxValue;
    private double _maxDt;
    private long _peakRss;

    public override void _Ready()
    {
        _t0 = Time.GetTicksMsec() / 1000.0;
        foreach (var a in Args())
        {
            if (a.StartsWith("--perf-seconds=") &&
                double.TryParse(a[15..], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var s))
                _quitAfter = s;
            else if (a.StartsWith("--perf-out="))
                _outPath = a[11..];
            else if (a == "--perf-nostats")
                _noStats = true;
        }
        if (_outPath != null)
        {
            try { _writer = new StreamWriter(_outPath, append: false); }
            catch (Exception ex)
            {
                GD.PrintErr($"[perf] --perf-out {_outPath} : {ex.Message}");
            }
        }
        Emit($"start t=0 rssMB={RssMb():F0}");
    }

    public override void _Process(double delta)
    {
        _frames++;
        _n++;
        _sumDt += delta;
        if (delta < _minDt) _minDt = delta;
        if (delta > _maxDt) _maxDt = delta;
        _winAcc += delta;
        _winN++;
        if (delta < _winMin) _winMin = delta;
        if (delta > _winMax) _winMax = delta;

        if (_winAcc >= 1.0)
        {
            var line = BuildLine();
            Emit(line);
            _winAcc = 0; _winN = 0;
            _winMin = double.MaxValue; _winMax = 0;
        }

        var t = Now();
        if (_quitAfter > 0 && t >= _quitAfter)
        {
            DumpSummary();
            _writer?.Flush();
            GetTree().Quit();
        }
    }

    public override void _ExitTree()
    {
        // Sortie autre que --perf-seconds (F4, --shot…) : on loggue
        // quand même le résumé des mesures accumulées.
        if (_n > 0) DumpSummary();
        _writer?.Dispose();
        base._ExitTree();
    }

    // ------------------------------------------------------------------

    private double Now() => Time.GetTicksMsec() / 1000.0 - _t0;

    private string BuildLine()
    {
        var fps = _winN / Math.Max(_winAcc, 1e-6);
        var rss = RssMb();
        if (rss > 0 && rss * 1024 * 1024 > _peakRss)
            _peakRss = (long)(rss * 1024 * 1024);
        // Moniteurs moteur (0 si non supportés par le renderer).
        var draws = Mon(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var objs = Mon(Performance.Monitor.RenderTotalObjectsInFrame);
        var prims = Mon(Performance.Monitor.RenderTotalPrimitivesInFrame);
        var nodes = Mon(Performance.Monitor.ObjectNodeCount);
        var res = Mon(Performance.Monitor.ObjectResourceCount);
        var mem = Mon(Performance.Monitor.MemoryStatic) / 1048576.0;
        var vram = Mon(Performance.Monitor.RenderVideoMemUsed) /
                   1048576.0;
        var tex = Mon(Performance.Monitor.RenderTextureMemUsed) /
                  1048576.0;
        var scene = _noStats ? "" : $" scene={CountScene()}";
        var cache = AssetCatalog.CacheStats();
        // Temps process/physique/navigation mesurés par le moteur —
        // discrimine CPU-script vs physique vs rendu quand le frame
        // time dépasse le budget 60 fps.
        var tproc = Mon(Performance.Monitor.TimeProcess) * 1000.0;
        var tphys = Mon(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        var bodies = Mon(Performance.Monitor.Physics3DActiveObjects);
        return $"t={Now():F0}s fps={fps:F0} " +
               $"frame={_winAcc * 1000 / Math.Max(_winN, 1):F1}ms" +
               $"({_winMin * 1000:F1}-{_winMax * 1000:F1}) " +
               $"proc={tproc:F1}ms phys={tphys:F1}ms bodies={bodies:F0} " +
               $"rss={rss:F0}MB static={mem:F0}MB vram={vram:F0}MB " +
               $"tex={tex:F0}MB draws={draws:F0} objs={objs:F0} " +
               $"prims={prims:F0} nodes={nodes:F0} res={res:F0} " +
               $"cache={cache.Textures}tex/{cache.GlbProtos}glb" + scene;
    }

    private void DumpSummary()
    {
        var fpsAvg = _n / Math.Max(_sumDt, 1e-6);
        Emit($"perf-done t={Now():F0}s n={_n} " +
             $"fps avg={fpsAvg:F1} min={1.0 / Math.Max(_maxDt, 1e-6):F1} " +
             $"max={1.0 / Math.Max(_minDt, 1e-6):F1} " +
             $"peak_rss={_peakRss / 1048576.0:F0}MB");
        Emit("proc-dump " + ProcHistogram());
        Emit("node-dump " + NodeHistogram());
        Emit("tex-postes " + AssetCatalog.TopTexPostes(8));
    }

    /// <summary>Histogramme des types de nœuds de la scène (masse —
    /// pour expliquer le coût moteur ~O(nœuds) par frame).</summary>
    private string NodeHistogram()
    {
        var root = GetTree().CurrentScene;
        if (root == null) return "noscene";
        var counts = new Dictionary<string, int>();
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            var key = n.GetType().Name;
            counts[key] = counts.GetValueOrDefault(key) + 1;
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        return string.Join(" ", counts.OrderByDescending(kv => kv.Value)
            .Take(15).Select(kv => $"{kv.Key}×{kv.Value}"));
    }

    /// <summary>Histogramme des nœuds en process/physics/input —
    /// répond à « qui consomme le CPU main-thread ? » en une passe au
    /// moment du résumé.</summary>
    private string ProcHistogram()
    {
        var root = GetTree().CurrentScene;
        if (root == null) return "noscene";
        var counts = new Dictionary<string, int>();
        var (anims, skels) = (0, 0);
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.IsProcessing() || n.IsPhysicsProcessing() ||
                n.IsProcessingInput() || n.IsProcessingUnhandledInput() ||
                n.IsProcessingUnhandledKeyInput())
            {
                var key = n.GetType().Name;
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
            if (n is AnimationPlayer ap && ap.IsPlaying()) anims++;
            else if (n is Skeleton3D) skels++;
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        var top = counts.OrderByDescending(kv => kv.Value)
            .Take(12).Select(kv => $"{kv.Key}×{kv.Value}");
        return string.Join(" ", top) + $" | anims={anims} skels={skels}";
    }

    /// <summary>Comptage par type sous la scène courante (un passage —
    /// toutes les 1 s, tolérable à ~20 k nœuds).</summary>
    private string CountScene()
    {
        var root = GetTree().CurrentScene;
        if (root == null) return "noscene";
        var (mi, mmi, lbl, total) = (0, 0, 0, 0);
        var stack = new Stack<Node>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            total++;
            switch (n)
            {
                case MultiMeshInstance3D: mmi++; break;
                case MeshInstance3D: mi++; break;
                case Label3D: lbl++; break;
            }
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        return $"{total}n/{mi}mi/{mmi}mm/{lbl}lbl";
    }

    private static double Mon(Performance.Monitor m)
    {
        try { return Performance.GetMonitor(m); }
        catch { return 0; }
    }

    private void Emit(string line)
    {
        GD.Print($"[perf] {line}");
        try { _writer?.WriteLine(line); } catch { /* best effort */ }
    }

    private static bool HasArg(string flag)
    {
        foreach (var a in Args())
            if (a == flag) return true;
        return false;
    }

    private static IEnumerable<string> Args() =>
        OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs());

    /// <summary>RSS du process en Mo (/proc Linux ; 0 si indisponible).</summary>
    private static double RssMb()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/self/status"))
                if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
                    return double.Parse(
                        line[6..].Trim().Split(' ')[0],
                        CultureInfo.InvariantCulture) / 1024.0;
        }
        catch (Exception) { /* hors Linux ou /proc absent */ }
        return 0;
    }
}

/// <summary>
/// Chronométrage minimal des phases de chargement — activé par
/// <c>--perf</c> (même flag que <see cref="PerfProbe"/>). Utilisation :
/// <c>var m = PerfTrace.Mark(); … ; PerfTrace.Report("loadmap", m);</c>
/// Ne fait rien (coût ~0) quand la sonde est inactive.
/// </summary>
public static class PerfTrace
{
    /// <summary>True si <c>--perf</c> est passé.</summary>
    public static bool Enabled => PerfProbe.Enabled;

    /// <summary>Référence temps zéro du process (premier accès —
    /// proche du démarrage du jeu, pour mesurer les transitions
    /// écran→écran en temps absolu).</summary>
    private static readonly ulong _boot = Time.GetTicksMsec();

    /// <summary>Début d'une section (ticks ms).</summary>
    public static ulong Mark() => Time.GetTicksMsec();

    /// <summary>Log « [perf] &lt;label&gt; : N ms ».</summary>
    public static void Report(string label, ulong mark)
    {
        if (!Enabled) return;
        GD.Print($"[perf] {label} : {Time.GetTicksMsec() - mark} ms");
    }

    /// <summary>Log « [perf] &lt;label&gt; : t=X.XXs » — temps absolu
    /// depuis le boot du process. Pour mesurer les transitions
    /// utilisateur (clic → écran prêt) sans chaîner les marks.</summary>
    public static void Abs(string label)
    {
        if (!Enabled) return;
        GD.Print($"[perf] {label} : t=" +
            $"{(Time.GetTicksMsec() - _boot) / 1000.0:F2}s");
    }
}
