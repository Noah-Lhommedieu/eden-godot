using System.Collections;

namespace EdenClient;

/// <summary>
/// Configuration client simple — <c>gamedata/client.cfg</c>, format
/// <c>key=value</c> (une entrée par ligne, <c>#</c>/<c>;</c> =
/// commentaires, espaces tolérés, insensible à la casse).
///
/// C'est l'équivalent du <c>connect.ini</c> du client natif — cf.
/// <c>$EDEN/tools/start-local.sh</c> (le client Wine pointe vers
/// 127.0.0.1:6543). Clés reconnues :
///   host, login_port, ticket_port, world_port, zone_port,
///   account, client_version, machine_id,
///   transport, tls_thumbprint, tls_insecure,
///   version_check, gamedata_dir.
/// </summary>
public sealed class ClientConfig : IEnumerable<KeyValuePair<string, string>>
{
    private readonly Dictionary<string, string> _values =
        new(StringComparer.OrdinalIgnoreCase);

    public string Host { get; set; } = "127.0.0.1";
    public int LoginPort { get; set; } = 6543;
    public int TicketPort { get; set; } = 2067;
    public int WorldPort { get; set; } = 5200;
    public int ZonePort { get; set; } = 5201;
    /// <summary>Compte pré-rempli à l'écran de login.</summary>
    public string Account { get; set; } = "";
    /// <summary>Version envoyée dans LoginAccount (capture native).</summary>
    public string ClientVersion { get; set; } = "002.003.05.67";
    /// <summary>Chaîne machine envoyée au World (capture native).</summary>
    public string MachineId { get; set; } = "BC-EC-A0-43-F3-80";
    /// <summary>Mode transport : « modern » (TLS 1.3, défaut) ou
    /// « legacy » (handshake RSA/RC4 lapis — repli quand le serveur
    /// tourne en <c>EDEN_TRANSPORT=legacy</c>).</summary>
    public string Transport { get; set; } = "modern";
    /// <summary>Empreinte SHA-256 hex du certificat serveur (pinning
    /// dev — miroir du <c>[TLS] thumbprint=</c> logué au boot serveur ;
    /// séparateurs « : »/espaces tolérés).</summary>
    public string? TlsThumbprint { get; set; }
    /// <summary>Flag dev : accepte n'importe quel certificat serveur
    /// (<c>tls_insecure=1</c> — warning à la connexion).</summary>
    public bool TlsInsecure { get; set; }
    /// <summary>Handshake de version post-TLS (phase 4d — extension
    /// Godot-only, jamais émis en transport legacy) : le client envoie
    /// VersionHello et attend Ack/Reject. Défaut actif ;
    /// <c>version_check=0</c> le coupe (serveur
    /// <c>EDEN_VERSION_CHECK=0</c>).</summary>
    public bool VersionCheck { get; set; } = true;
    /// <summary>Répertoire du corpus <c>*.ini</c> gamedata client dont
    /// le manifest SHA-256 est envoyé dans le VersionHello
    /// (<c>gamedata_dir=</c> — chemin absolu ou relatif). Absent →
    /// résolution automatique (<c>data/db</c> de la racine gamedata).</summary>
    public string? GamedataDir { get; set; }

    /// <summary>Clé <c>ui.show_ids</c> : affiche <c>#id</c> en pied des
    /// tooltips item/skill/classe. Valeur lue dans client.cfg.</summary>
    public bool ShowIds { get; set; }

    private static bool? _showIdsRuntime;
    /// <summary>Flag global runtime (toggle Preferences : l'assigner).
    /// Non assigné → valeur de <c>ui.show_ids</c> de client.cfg
    /// (chargé une fois, paresseusement).</summary>
    public static bool ShowIdsEnabled
    {
        get => _showIdsRuntime ??= Load().ShowIds;
        set => _showIdsRuntime = value;
    }

    /// <summary>Accès brut à une clé (null si absente).</summary>
    public string? Get(string key) =>
        _values.TryGetValue(key, out var v) ? v : null;

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() =>
        _values.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Parse le contenu d'un fichier <c>key=value</c>.</summary>
    public static ClientConfig Parse(string text)
    {
        var cfg = new ClientConfig();
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
                continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            cfg._values[key] = value;
            cfg.ApplyKnown(key, value);
        }
        return cfg;
    }

    private void ApplyKnown(string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "host": Host = value; break;
            case "login_port":
                if (int.TryParse(value, out var lp)) LoginPort = lp;
                break;
            case "ticket_port":
                if (int.TryParse(value, out var tp)) TicketPort = tp;
                break;
            case "world_port":
                if (int.TryParse(value, out var wp)) WorldPort = wp;
                break;
            case "zone_port":
                if (int.TryParse(value, out var zp)) ZonePort = zp;
                break;
            case "account": Account = value; break;
            case "client_version": ClientVersion = value; break;
            case "machine_id": MachineId = value; break;
            case "transport": Transport = value; break;
            case "tls_thumbprint": TlsThumbprint = value; break;
            case "tls_insecure":
                TlsInsecure = value is "1" or "true" or "TRUE" or "yes";
                break;
            case "version_check":
                VersionCheck = value is "1" or "true" or "TRUE" or "yes";
                break;
            case "gamedata_dir": GamedataDir = value; break;
            case "ui.show_ids":
                ShowIds = value is "1" or "true" or "TRUE" or "yes";
                break;
        }
    }

    /// <summary>Chemin du fichier de config (res://gamedata/).</summary>
    public const string DefaultPath = "res://gamedata/client.cfg";

    /// <summary>Charge <paramref name="resPath"/> (défaut
    /// <see cref="DefaultPath"/>) — retourne les défauts si le fichier
    /// est absent. Hors contexte Godot, <paramref name="resPath"/> est
    /// déjà un chemin fichier.
    ///
    /// Pour <see cref="DefaultPath"/>, la recherche passe par
    /// <see cref="DataPaths"/> : <c>$EDEN_DATA_DIR/client.cfg</c> →
    /// <c>&lt;exe&gt;/gamedata/client.cfg</c> (package) →
    /// <c>raw_assets/</c> dev → <c>res://gamedata/</c>. En export, le
    /// client.cfg disque à côté de l'exécutable (éditable par
    /// l'utilisateur) l'emporte sur la copie embarquée dans le pck,
    /// qui reste le dernier repli via <c>FileAccess</c>.</summary>
    public static ClientConfig Load(string resPath = DefaultPath)
    {
        var fs = resPath;
        try
        {
            // Loose ou « epk://client.cfg » (DataPaths rend les deux).
            if (Vfs.EdenVfs.Exists(fs))
                return Parse(Vfs.EdenVfs.ReadAllText(fs)!);
            return new ClientConfig();
        }
        catch (Exception)
        {
            return new ClientConfig();
        }
    }
}
