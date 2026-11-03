using Godot;
using EdenClient.Game;

namespace EdenClient.Screens;

/// <summary>
/// Préchauffage des écrans pré-jeu. Le coût dominant de la sélection
/// de personnage est le parse du méga-GLB corps (~5 s à froid par
/// lettre de corps) : on ne connaît la liste des persos qu'après le
/// login, donc on mémorise localement (<c>user://pregame_hint.cfg</c>)
/// les blobs d'apparence vus à la dernière session du compte et on
/// construit leurs protos corps + le temple pendant que l'utilisateur
/// est encore sur l'écran de login. Le cache de protos
/// (<see cref="CharBodyCache"/>, cache GLB d'AssetCatalog) fait que
/// la sélection n'a plus qu'à dupliquer.
/// </summary>
public static class PreGameWarmup
{
    private const string Path = "user://pregame_hint.cfg";
    private static bool _started;

    /// <summary>Lance le préchauffage (une fois par processus) —
    /// thread principal.</summary>
    public static void Start(string account)
    {
        if (_started) return;
        _started = true;
        // Temple : le singleton demande son GLB en tâche de fond dès
        // sa création, même détaché de l'arbre.
        _ = TempleBackdrop.Shared;
        var apps = LoadHint(account);
        if (apps.Count == 0) return;
        Task.Run(() =>
        {
            foreach (var a in apps)
            {
                try { CharBodyCache.Prewarm(a); }
                catch (Exception e)
                { GD.PrintErr($"[warmup] {e.Message}"); }
            }
            EdenClient.Perf.PerfTrace.Abs(
                $"warmup : {apps.Count} corps prêts");
        });
    }

    private static List<byte[]> LoadHint(string account)
    {
        var res = new List<byte[]>();
        try
        {
            var cfg = new ConfigFile();
            if (cfg.Load(Path) != Error.Ok) return res;
            var sec = Section(account);
            if (!cfg.HasSection(sec))
                sec = cfg.GetSections().FirstOrDefault() ?? "";
            if (sec.Length == 0) return res;
            foreach (var k in cfg.GetSectionKeys(sec))
            {
                var hex = (string)cfg.GetValue(sec, k, "");
                if (hex.Length >= 64)
                    res.Add(Convert.FromHexString(hex));
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[warmup] hint illisible : {e.Message}");
        }
        return res;
    }

    /// <summary>Mémorise les apparences de la liste courante.</summary>
    public static void SaveHint(string account,
        IEnumerable<byte[]?> apps)
    {
        try
        {
            var cfg = new ConfigFile();
            cfg.Load(Path);
            var sec = Section(account);
            if (cfg.HasSection(sec)) cfg.EraseSection(sec);
            var i = 0;
            foreach (var a in apps)
                if (a is { Length: > 0 } && i < 8)
                    cfg.SetValue(sec, $"c{i++}", Convert.ToHexString(a));
            cfg.Save(Path);
        }
        catch (Exception e)
        {
            GD.PrintErr($"[warmup] hint non écrit : {e.Message}");
        }
    }

    private static string Section(string account) =>
        "acc_" + (account ?? "").Trim().ToLowerInvariant();
}
