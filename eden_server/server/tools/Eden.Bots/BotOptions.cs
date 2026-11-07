namespace Eden.Bots;

/// <summary>Options CLI du banc d'essai — aucune valeur réseau n'est
/// devinée : les défauts reprennent les ports de dev locaux du serveur.</summary>
public sealed class BotOptions
{
    public string Host { get; private set; } = "127.0.0.1";
    public int TicketPort { get; private set; } = 2067;
    public int ZonePort { get; private set; } = 5201;
    public string DbConnection { get; private set; } =
        Environment.GetEnvironmentVariable("DB_CONN") ??
        "Host=localhost;Port=5433;Database=eden_live;SearchPath=character,inventory,market,guild,social,progress,ranking,lottery,system,admin,public;Username=eden;Password=postgres";
    public string CapturePath { get; private set; } =
        ResolveCapturePath();
    public string NamePrefix { get; private set; } = "botchar";
    public int Sessions { get; private set; } = 1;
    public string Scenario { get; private set; } = "mixed";
    public int DurationSeconds { get; private set; } = 30;
    public int ActionsPerSecond { get; private set; } = 1;
    public int SpawnWaitSeconds { get; private set; } = 8;
    public int StaggerMs { get; private set; } = 25;
    public string? JsonPath { get; private set; }
    public bool NoEntry { get; private set; }
    /// <summary>« modern » (TLS 1.3, défaut — requis par le serveur en
    /// <c>EDEN_TRANSPORT=modern</c>) ou « legacy » (RSA/RC4 historique).</summary>
    public string Transport { get; private set; } = "modern";
    /// <summary>Empreinte SHA-256 du cert serveur attendu (pinning) —
    /// <c>--tls-thumbprint=</c>, repli env <c>EDEN_TLS_THUMBPRINT</c>.</summary>
    public string? TlsThumbprint { get; private set; } =
        Environment.GetEnvironmentVariable("EDEN_TLS_THUMBPRINT");
    /// <summary><c>--tls-insecure</c> — accepte n'importe quel
    /// certificat (dev ; warning logué à la connexion).</summary>
    public bool TlsInsecure { get; private set; }

    /// <summary>Version applicative annoncée dans le VersionHello
    /// (phase 4d) — <c>--client-version=</c>, défaut la version du
    /// client.cfg (« 002.003.05.67 »).</summary>
    public string ClientVersion { get; private set; } = "002.003.05.67";
    /// <summary>Empreinte manifest gamedata annoncée (hex,
    /// <c>--manifest-hash=</c>) — null = calculée depuis
    /// <c>EDEN_GAMEDATA_DIR</c>/<c>DATA_DIR</c>/<c>EDEN_DATA</c>
    /// s'il est résolu, sinon hash vide.</summary>
    public string? ManifestHashHex { get; private set; }
    /// <summary><c>--no-version</c> — simule un client pré-version :
    /// le bot n'envoie pas de VersionHello et attend le rejet
    /// <c>0xFFFD UpdateRequired</c> du gate (test du filtre).</summary>
    public bool NoVersion { get; private set; }

    /// <summary>Parse simple `--key=value` ; les options inconnues
    /// échouent explicitement pour éviter un run silencieusement faux.</summary>
    public static BotOptions Parse(string[] args)
    {
        var o = new BotOptions();
        foreach (var arg in args)
        {
            var parts = arg.TrimStart('-').Split('=', 2);
            var k = parts[0].ToLowerInvariant();
            var v = parts.Length == 2 ? parts[1] : "true";
            switch (k)
            {
                case "host": o.Host = v; break;
                case "ticket-port": o.TicketPort = int.Parse(v); break;
                case "zone-port": o.ZonePort = int.Parse(v); break;
                case "db": o.DbConnection = v; break;
                case "capture": o.CapturePath = Path.GetFullPath(v); break;
                case "name-prefix": o.NamePrefix = v; break;
                case "sessions": o.Sessions = int.Parse(v); break;
                case "scenario": o.Scenario = v.ToLowerInvariant(); break;
                case "duration": o.DurationSeconds = int.Parse(v); break;
                case "aps": o.ActionsPerSecond = int.Parse(v); break;
                case "spawn-wait": o.SpawnWaitSeconds = int.Parse(v); break;
                case "stagger-ms": o.StaggerMs = int.Parse(v); break;
                case "json": o.JsonPath = Path.GetFullPath(v); break;
                case "no-entry": o.NoEntry = v is "1" or "true"; break;
                case "transport": o.Transport = v.ToLowerInvariant(); break;
                case "tls-thumbprint": o.TlsThumbprint = v; break;
                case "tls-insecure": o.TlsInsecure = v is "1" or "true"; break;
                case "client-version": o.ClientVersion = v; break;
                case "manifest-hash": o.ManifestHashHex = v; break;
                case "no-version": o.NoVersion = v is "1" or "true"; break;
                case "help": throw new ArgumentException(Usage);
                default: throw new ArgumentException($"Option inconnue: {arg}\n{Usage}");
            }
        }
        if (o.Sessions <= 0 || o.DurationSeconds <= 0 || o.ActionsPerSecond <= 0)
            throw new ArgumentException("sessions/duration/aps doivent être > 0");
        return o;
    }

    /// <summary>Cherche la capture d'entrée depuis le cwd puis en
    /// remontant — permet de lancer l'outil depuis `server/tools` ou repo.</summary>
    private static string ResolveCapturePath()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName,
                "captures/zsniff_cs_entry_success.bin");
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        return "captures/zsniff_cs_entry_success.bin";
    }

    public const string Usage = """
Usage : eden-bots [options]
  --scenario=entry|move|combat|spell|loot|mixed
  --sessions=N --duration=S --aps=N --spawn-wait=S
  --host=127.0.0.1 --ticket-port=2067 --zone-port=5201
  --db='Host=...;Database=eden_live;SearchPath=character,inventory,market,guild,social,progress,ranking,lottery,system,admin,public;Username=eden;Password=...'
  --name-prefix=botchar --capture=... --stagger-ms=25 --no-entry
  --transport=modern|legacy  (TLS 1.3 par défaut ; legacy = RSA/RC4)
  --tls-thumbprint=<sha256>  (pinning ; repli EDEN_TLS_THUMBPRINT)
  --tls-insecure             (accepte tout cert — dev)
  --client-version=<ver>     (version annoncée dans VersionHello)
  --manifest-hash=<hex>      (empreinte gamedata annoncée ; défaut =
                             manifest de EDEN_GAMEDATA_DIR/DATA_DIR)
  --no-version               (simule un client pré-version — attendu :
                             rejet 0xFFFD update_required du gate)
""";
}
