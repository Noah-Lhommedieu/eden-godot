namespace EdenClient.Game;

/// <summary>Budgets de performance (PLAN §9.6), figés ici et mesurés par
/// <see cref="FrameStats"/> à chaque jalon.</summary>
public sealed record PerfBudget(double FrameP95Ms, long AllocBytesPerFrame,
    double MapLoadMs, long WorkingSetMb)
{
    /// <summary>Budgets M8 : 60 fps cible (P95 ≤ 16,7 ms), zéro alloc en
    /// boucle stable (≤ 2 Kio tolérés : chaînes de log), carte S002 ≤ 3 s,
    /// RAM ≤ 2 Gio.</summary>
    public static readonly PerfBudget M8 = new(16.7, 2048, 3000, 2048);
}

/// <summary>Fenêtre glissante de temps de frame + allocations par frame
/// (pur, sans moteur) — percentiles et verdict vs <see cref="PerfBudget"/>.</summary>
public sealed class FrameStats
{
    private readonly double[] _ms;
    private readonly long[] _alloc;
    private int _n, _head;

    public FrameStats(int window = 600)
    {
        _ms = new double[window];
        _alloc = new long[window];
    }

    public int Count => _n;

    public void Add(double frameMs, long allocBytes)
    {
        _ms[_head] = frameMs;
        _alloc[_head] = allocBytes;
        _head = (_head + 1) % _ms.Length;
        if (_n < _ms.Length) _n++;
    }

    private static double Pct(double[] a, int n, double p)
    {
        if (n == 0) return 0;
        var c = new double[n];
        Array.Copy(a, c, n);
        Array.Sort(c);
        var idx = (int)Math.Ceiling(p * n) - 1;
        return c[Math.Clamp(idx, 0, n - 1)];
    }

    private double[] Live()
    {
        // ordre indifférent pour les percentiles
        return _ms;
    }

    public double P50 => Pct(Live(), _n, 0.50);
    public double P95 => Pct(Live(), _n, 0.95);
    public double P99 => Pct(Live(), _n, 0.99);
    public double Max { get { var m = 0.0; for (var i = 0; i < _n; i++) m = Math.Max(m, _ms[i]); return m; } }
    public double Mean { get { if (_n == 0) return 0; var s = 0.0; for (var i = 0; i < _n; i++) s += _ms[i]; return s / _n; } }

    /// <summary>Allocation moyenne par frame (octets).</summary>
    public double AllocMean
    {
        get { if (_n == 0) return 0; long s = 0; for (var i = 0; i < _n; i++) s += _alloc[i]; return (double)s / _n; }
    }

    /// <summary>Violations du budget (liste vide = conforme).</summary>
    public List<string> Check(PerfBudget b, double? mapLoadMs = null, long? workingSetMb = null)
    {
        var v = new List<string>();
        if (_n >= 30 && P95 > b.FrameP95Ms)
            v.Add($"frame P95 {P95:0.0} ms > {b.FrameP95Ms:0.0} ms");
        if (_n >= 30 && AllocMean > b.AllocBytesPerFrame)
            v.Add($"alloc {AllocMean:0} o/frame > {b.AllocBytesPerFrame} o");
        if (mapLoadMs is { } m && m > b.MapLoadMs)
            v.Add($"chargement carte {m:0} ms > {b.MapLoadMs:0} ms");
        if (workingSetMb is { } w && w > b.WorkingSetMb)
            v.Add($"RAM {w} Mio > {b.WorkingSetMb} Mio");
        return v;
    }

    public string Summary() =>
        $"frames={_n} P50={P50:0.0} P95={P95:0.0} P99={P99:0.0} max={Max:0.0} ms, " +
        $"alloc moy={AllocMean:0} o/frame";
}

/// <summary>Temps par phase de chargement (map, tables, textures…).</summary>
public sealed class PhaseTimes
{
    private readonly List<(string Name, double Ms)> _p = new();
    public void Add(string name, double ms) => _p.Add((name, ms));
    public double Total => _p.Sum(x => x.Ms);
    public IReadOnlyList<(string Name, double Ms)> Phases => _p;
    public string Summary() =>
        string.Join(", ", _p.Select(x => $"{x.Name}={x.Ms:0}ms")) + $" (total {Total:0} ms)";
}
