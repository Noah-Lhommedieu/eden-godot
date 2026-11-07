using Godot;
using EdenClient.Net;
using EdenClient.Player;
using EdenClient.Protocol;
using EdenClient.Screens;
using EdenClient.Session;
using EdenClient.World;

namespace EdenClient.Combat;

/// <summary>
/// Banc de mesure « freeze après un kill » (<c>--kill-loop=N[,mobId]</c>) : N fois, clone un mob serveur
/// (//clone_monster), l'engage en auto-attaque, attend sa mort réelle (305 → <see cref="RemoteEntity.Dying"/>),
/// puis enregistre le temps de chaque image : fenêtre [mort, mort + 2 s] contre le reste (régime établi).
/// Sortie : <c>[killloop] RESULT …</c> (p50/p95/p99/max en ms) et une ligne par kill. Pur outil de QA,
/// sans effet sans l'argument.
/// </summary>
public partial class KillLoopProbe : Node
{
    private const double WindowSec = 2.0;

    private int _target = 3;
    private int _mobId = 20678;
    private int[] _mobIds = { 20678 };     // rotation : --kill-loop=12,56928;60565;60566
    private int _pack = 1;                 // --kill-pack=K : K mobs clonés par itération, tués en vraie mêlée
    private int _iter;
    private double _t;              // temps depuis le début de l'itération courante
    private int _state;             // 0 attente entrée, 1 clone envoyé, 2 engagé, 3 mort vue → fenêtre, 4 repos
    private RemoteEntity? _mob;
    private RemoteEntity? _engaged;
    private readonly List<RemoteEntity> _packAlive = new();
    private EntityRegistry? _registry;
    private PlayerController? _player;
    private bool _searched;
    private double _windowLeft;
    private double _warm;           // secondes depuis l'entrée en jeu (échauffement)
    private readonly List<double> _win = new();
    private readonly List<double> _base = new();
    private readonly List<(int Iter, double Max)> _perKill = new();
    private double _curMax;
    private ulong _winT0;
    private bool _done;

    public static bool Requested(out int n, out int mobId)
    {
        n = 0; mobId = 20678;
        foreach (var a in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
            if (a.StartsWith("--kill-loop="))
            {
                var p = a[12..].Split(',');
                int.TryParse(p[0], out n);
                if (p.Length > 1) int.TryParse(p[1].Split(';')[0], out mobId);
                return n > 0;
            }
        return false;
    }

    public override void _Ready()
    {
        Requested(out _target, out _mobId);
        foreach (var a in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--kill-loop=") && a.Contains(','))
                _mobIds = a[12..].Split(',')[1].Split(';')
                    .Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).ToArray();
            else if (a.StartsWith("--kill-pack=") &&
                     int.TryParse(a[12..], out var pk) && pk > 0)
                _pack = pk;
        }
        if (_mobIds.Length == 0) _mobIds = new[] { _mobId };
    }

    public override void _Process(double delta)
    {
        if (_done) return;
        if (!_searched)
        {
            _registry = GetTree().Root.FindChild("NetEntities", true, false) as EntityRegistry;
            _player = GetTree().Root.FindChild("Player", true, false) as PlayerController;
            _searched = _registry != null && _player != null;
            return;
        }
        if (SessionCarry.Session?.Zone?.IsConnected != true || PlayerSkills.Spells.Count == 0) return;
        _warm += delta;
        if (_warm < 12) return;                       // échauffement (chargement carte, HUD, FX warmup)

        // Mesure : chaque image va dans « fenêtre » (juste après une mort) ou « base » (hors fenêtre, après
        // le premier kill pour exclure le chargement).
        var ms = delta * 1000.0;
        if (_windowLeft > 0)
        {
            _win.Add(ms);
            _curMax = Math.Max(_curMax, ms);
            _windowLeft -= delta;
            if (_windowLeft <= 0)
            {
                _perKill.Add((_iter, _curMax));
                GD.Print($"[killloop] kill #{_iter} : pire image dans les {WindowSec:0.#} s = {_curMax:F0} ms ; " +
                         $"{Snapshot()} ; " +
                         $"évènements : {EdenClient.Perf.PerfTrace.EventsSince(_winT0)}");
                _curMax = 0;
                _state = 4; _t = 0;
            }
        }
        else if (_iter > 0 || _state != 0) _base.Add(ms);

        _t += delta;
        switch (_state)
        {
            case 0:
                if (_iter >= _target) { Finish(); return; }
                // Pack : K mobs clonés d'un coup, tués en vraie mêlée —
                // les morts se succèdent en ~2-4 s (rafale réelle).
                // PAS de « //kill » : un vrai kill passe par les dégâts
                // réels (loot, exp, attrs, floaters complets).
                _packAlive.Clear();
                _engaged = null;
                for (var k = 0; k < _pack; k++)
                    SendText($"//clone_monster {_mobIds[(_iter * _pack + k) % _mobIds.Length]}");
                PerfEvent("clone");
                _state = 1; _t = 0;
                break;
            case 1 when _t > 2.0:
                // Tous les mobs du pack, vivants, par distance.
                var spawn = _registry!.Entities.Values
                    .Where(e => e.Kind == 12 && _mobIds.Contains(e.EntityId) &&
                                !e.Dying && !_packAlive.Contains(e))
                    .OrderBy(e => (e.GlobalPosition - _player!.GlobalPosition)
                        .LengthSquared())
                    .Take(_pack - _packAlive.Count).ToList();
                _packAlive.AddRange(spawn);
                _mob = _packAlive.FirstOrDefault(e =>
                    IsInstanceValid(e) && !e.Dying);
                if (_mob == null)
                {
                    if (_t > 10) { _iter++; _state = 0; _t = 0; }
                    break;
                }
                _player!.AutoAttacker?.Engage(_mob);
                _state = 2; _t = 0;
                break;
            case 2:
                // Fenêtre ouverte dès la 1re mort (couvre toute la
                // rafale jusqu'à 2 s après la dernière).
                if (_packAlive.Any(e => !IsInstanceValid(e) || e.Dying) &&
                    _windowLeft <= 0)
                {
                    PerfEvent("KILL");
                    _windowLeft = WindowSec; _curMax = 0;
                    _winT0 = Time.GetTicksMsec();
                }
                _packAlive.RemoveAll(e => !IsInstanceValid(e) || e.Dying);
                _mob = _packAlive.FirstOrDefault();
                if (_mob == null)
                {
                    _iter++;
                    _state = 3; _t = 0;
                }
                else if (_mob != _engaged || _t > 1.0)
                {
                    _player!.AutoAttacker?.Engage(_mob);
                    _engaged = _mob;
                    _t = 0;
                }
                break;
            case 4 when _t > 4.0:
                _state = 0; _t = 0;
                break;
        }
    }

    private static void PerfEvent(string what) => EdenClient.Perf.PerfTrace.Event(what);

    /// <summary>Instantané des accumulations suspectes après un kill :
    /// nœuds arbre, hôtes d'effets, arriérés de libération étalée,
    /// cadavres en grâce, entités. Une croissance monotone kill après
    /// kill trahit une fuite (corps/FX non libérés).</summary>
    private string Snapshot()
    {
        int fx = 0;
        foreach (var n in GetTree().Root
                     .FindChildren("*", "Node", true, false)
                     .OfType<Native.NativeFxHost>())
            fx++;
        return $"nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount):F0} " +
               $"ents={_registry?.Entities.Count ?? -1} " +
               $"corpses={_registry?.DebugCorpseCount ?? -1} " +
               $"slowq={(_registry?.DebugSlowFreeQueued ?? -1) + World.SlowFree.Queued} " +
               $"fxhosts={fx}";
    }

    private void SendText(string text)
    {
        _ = SessionCarry.Session?.SendZoneAsync(new ZoneTextCommand { Text = text });
        GD.Print($"[killloop] {text}");
    }

    private static string Stats(List<double> v)
    {
        if (v.Count == 0) return "n=0";
        var s = v.OrderBy(x => x).ToArray();
        double P(double q) => s[Math.Min(s.Length - 1, (int)(q * s.Length))];
        return $"n={s.Length} p50={P(0.5):F1} p95={P(0.95):F1} p99={P(0.99):F1} max={s[^1]:F1}";
    }

    private void Finish()
    {
        _done = true;
        GD.Print($"[killloop] RESULT fenêtre post-kill (ms) : {Stats(_win)}");
        GD.Print($"[killloop] RESULT hors kill (ms)         : {Stats(_base)}");
        GD.Print($"[killloop] RESULT pires images par kill  : {string.Join(", ", _perKill.Select(k => $"#{k.Iter}={k.Max:F0}"))}");
        GetTree().Quit();
    }
}
