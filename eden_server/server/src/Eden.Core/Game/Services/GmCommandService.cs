using Eden.Core.Data.Parsers;
using Eden.Core.Networking.Session;

namespace Eden.Core.Game.Services;

/// <summary>
/// Commandes GM — charge S_RootCmds.ini (332 cmds) + S_GMToolCmds.ini.
/// Dispatch textuel vers des handlers enregistres par les services de jeu.
/// </summary>
public sealed class GmCommandService
{
    /// <summary>Contexte passé aux handlers async de zone : le perso
    /// exécutant, sa session (pour les paquets d'effet — 274, 280…) et
    /// le token d'annulation. <paramref name="State"/> transporte
    /// l'état runtime du perso (EntryState côté zone — l'assemblage
    /// Core ne le connaît pas ; les handlers casteur-side).</summary>
    public sealed record Context(int CharId, NetSession Session,
        CancellationToken Token, object? State = null);

    private readonly Dictionary<string, ProgressionParsers.GmCommand> _commands =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,
        Func<int, string[], string>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,
        Func<Context, string[], Task<string?>>> _asyncHandlers =
        new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Commandes implémentées hors registre (switch local de
    /// <c>ZoneEntryFlow.Admin.cs</c> — elles ont besoin d'
    /// <c>EntryState</c> ou du pipeline de boucle) : déclarées via
    /// <see cref="MarkImplemented"/> pour que <c>ListCommands()</c> et
    /// le <c>//help</c> reflètent la réalité.</summary>
    private readonly HashSet<string> _implementedElsewhere =
        new(StringComparer.OrdinalIgnoreCase);

    public GmCommandService(
        IEnumerable<ProgressionParsers.GmCommand> commands)
    {
        foreach (var c in commands)
            _commands[c.Name] = c;
    }

    public static GmCommandService LoadFromDataDir(string decodedDir)
        => new(ProgressionParsers.ParseGmCommands(
            Path.Combine(decodedDir, "S_RootCmds.ini"))
            .Concat(ProgressionParsers.ParseGmCommands(
                Path.Combine(decodedDir, "S_GMToolCmds.ini"))));

    public int CommandCount => _commands.Count;

    /// <summary>Toutes les commandes connues (ini + handlers maison)
    /// triées par nom — source du <c>//help</c> : signature S_RootCmds
    /// quand elle existe, sinon le nom du handler.</summary>
    public IReadOnlyList<(string Name, string Signature, int Access,
        bool Implemented)> ListCommands()
    {
        var names = _commands.Keys
            .Concat(_handlers.Keys).Concat(_asyncHandlers.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var list = new List<(string, string, int, bool)>();
        foreach (var n in names)
        {
            var sig = _commands.TryGetValue(n, out var c)
                ? c.Signature : n;
            var access = _commands.TryGetValue(n, out var c2)
                ? c2.AccessLevel : 4;
            var impl = _handlers.ContainsKey(n) ||
                _asyncHandlers.ContainsKey(n) ||
                _implementedElsewhere.Contains(n);
            list.Add((n, sig, access, impl));
        }
        return list.OrderBy(x => x.Item1).ToList();
    }

    /// <summary>Verifie qu'une commande existe et le niveau d'acces.</summary>
    public int RequiredAccess(string name)
        => _commands.TryGetValue(name, out var c) ? c.AccessLevel : -1;

    /// <summary>Enregistre un handler pour une commande.</summary>
    public void Register(string name,
        Func<int, string[], string> handler)
        => _handlers[name] = handler;

    /// <summary>Marque une commande implémentée ailleurs que dans le
    /// registre (ex. switch local de la boucle de zone) —
    /// <c>ListCommands()</c> la rapporte alors comme implémentée.</summary>
    public void MarkImplemented(string name)
        => _implementedElsewhere.Add(name);

    /// <summary>Enregistre un handler async avec contexte zone. Les
    /// handlers enregistrés ainsi priment sur la liste ini (permet les
    /// commandes d'admin maison absentes de RootCmds).</summary>
    public void RegisterAsync(string name,
        Func<Context, string[], Task<string?>> handler)
        => _asyncHandlers[name] = handler;

    /// <summary>
    /// Execute "/commande arg1 arg2" — retourne le resultat textuel
    /// ou null si commande inconnue / acces insuffisant.
    /// </summary>
    public string? Execute(string commandLine, int gmLevel)
    {
        var parts = commandLine.TrimStart('/')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var name = parts[0];
        if (!_commands.TryGetValue(name, out var cmd)) return null;
        if (gmLevel < cmd.AccessLevel) return null;
        if (!_handlers.TryGetValue(name, out var handler))
            return $"[GM] {name} non supporté (commande native " +
                "connue, pas encore portée)";
        return handler(gmLevel, parts.Skip(1).ToArray());
    }

    /// <summary>Version async avec contexte : les handlers
    /// enregistrés via <see cref="RegisterAsync"/> sont consultés en
    /// premier, puis la table ini + handlers sync classiques.</summary>
    public async Task<string?> ExecuteAsync(string commandLine,
        int gmLevel, Context ctx)
    {
        var parts = commandLine.TrimStart('/')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var name = parts[0];
        if (_asyncHandlers.TryGetValue(name, out var ah))
            return await ah(ctx, parts.Skip(1).ToArray());
        return Execute(commandLine, gmLevel);
    }
}
