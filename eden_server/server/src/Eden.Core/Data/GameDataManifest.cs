using System.Security.Cryptography;
using System.Text;

namespace Eden.Core.Data;

/// <summary>
/// Empreinte SHA-256 déterministe du corpus gamedata d'un processus
/// (phase 4 incrément d — handshake de version,
/// <c>docs/temp-plans/2026-10-26-phase4-transport-protocole.md</c> §d).
///
/// Le manifest couvre <b>tous les <c>*.ini</c> du répertoire gamedata,
/// récursivement</b> — superset déterministe des fichiers effectivement
/// ouverts par les chargeurs (GameDataService, *Service.LoadFromDataDir,
/// balance.ini…) : un .ini livré mais jamais chargé compte quand même
/// dans l'empreinte, ce qui détecte la dérive du corpus plutôt que le
/// seul sous-ensemble lu. Chemins relatifs triés Ordinal, séparateurs
/// normalisés « / » : deux arbres identiques donnent le même hash quels
/// que soient l'OS et l'ordre d'énumération.
///
/// Algorithme : <c>SHA-256( Σ utf8(relpath) + 0x00 + sha256(fichier) )</c>
/// — le coût est borné à un parcours + un hash par fichier, calculé une
/// fois par répertoire et mis en cache
/// (<see cref="ForDirectory"/>/<see cref="Current"/>).
/// </summary>
public static class GameDataManifest
{
    /// <summary>Résultat du calcul : empreinte + nombre de fichiers
    /// couverts.</summary>
    /// <param name="Hash">32 octets SHA-256 du manifest.</param>
    /// <param name="FileCount">Nombre de <c>*.ini</c> inclus.</param>
    /// <param name="Directory">Répertoire racine hashé.</param>
    public sealed record Manifest(byte[] Hash, int FileCount,
        string Directory)
    {
        /// <summary>Empreinte en hex minuscule (format véhiculé dans la
        /// trame de version).</summary>
        public string Hex => Convert.ToHexString(Hash).ToLowerInvariant();
    }

    /// <summary>Calcule le manifest d'un répertoire gamedata.
    /// Retourne null si le répertoire est absent ou vide de tout
    /// <c>*.ini</c> — l'appelant traite alors le manifest comme
    /// inconnu (la vérification de version continue sans lui).</summary>
    public static Manifest? Compute(string directory)
    {
        if (string.IsNullOrEmpty(directory) ||
            !System.IO.Directory.Exists(directory))
            return null;
        var root = Path.GetFullPath(directory);
        var files = System.IO.Directory
            .EnumerateFiles(root, "*.ini", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f)
                .Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(rel => rel, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0) return null;

        using var sha = SHA256.Create();
        var buffer = new byte[64 * 1024];
        foreach (var rel in files)
        {
            var pathBytes = Encoding.UTF8.GetBytes(rel);
            sha.TransformBlock(pathBytes, 0, pathBytes.Length, null, 0);
            sha.TransformBlock([0], 0, 1, null, 0);
            using var fs = new FileStream(Path.Combine(root, rel),
                FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 20, FileOptions.SequentialScan);
            // Empreinte interne du fichier — hachée dans le flux global
            // plutôt que de concaténer 53 Mio de contenu.
            var fileHash = SHA256.HashData(fs);
            sha.TransformBlock(fileHash, 0, fileHash.Length, null, 0);
        }
        sha.TransformFinalBlock([], 0, 0);
        return new Manifest(sha.Hash!, files.Length, root);
    }

    // ------------------------------------------------------------------
    // Manifest du processus — résolu depuis la config, calculé une fois.
    // ------------------------------------------------------------------

    private static readonly Lock _cacheLock = new();
    private static Manifest? _cached;
    private static bool _resolved;

    /// <summary>Manifest du corpus de ce processus :
    /// <c>EDEN_GAMEDATA_HASH</c> (hex) en priorité, sinon le calcul de
    /// <see cref="Compute"/> sur <c>EDEN_GAMEDATA_DIR</c> (repli
    /// <c>DATA_DIR</c>/<c>EDEN_DATA</c>). Null quand ni l'un ni l'autre
    /// n'est disponible — évalué et mis en cache à la première demande
    /// (le résultat inclut l'état « résolu sans manifest »).</summary>
    public static Manifest? Current
    {
        get
        {
            lock (_cacheLock)
            {
                if (_resolved) return _cached;
                _resolved = true;
                _cached = Resolve();
                return _cached;
            }
        }
    }

    /// <summary>Résolution initiale — séparée de la propriété pour
    /// rester lisible.</summary>
    private static Manifest? Resolve()
    {
        if (EdenConfig.GameDataHash is { } hex &&
            TryParseHex(hex, out var overrideHash))
            return new Manifest(overrideHash, FileCount: -1, "<EDEN_GAMEDATA_HASH>");
        var dir = EdenConfig.GameDataDir;
        return dir == null ? null : Compute(dir);
    }

    /// <summary>Amorce le cache du manifest du processus depuis un
    /// répertoire déjà résolu par l'appelant (ex. la zone qui a son
    /// propre repli <c>AppContext/data</c> quand DATA_DIR est absent).
    /// No-op si <c>EDEN_GAMEDATA_HASH</c> est posé (l'override gagne)
    /// ou si le cache est déjà résolu.</summary>
    public static void PrimeDirectory(string directory)
    {
        lock (_cacheLock)
        {
            if (_resolved || EdenConfig.GameDataHash != null) return;
            _cached = Compute(directory);
            _resolved = true;
        }
    }

    /// <summary>Invalide le cache (tests).</summary>
    public static void ResetForTests()
    {
        lock (_cacheLock)
        {
            _cached = null;
            _resolved = false;
        }
    }

    /// <summary>Parse une empreinte hex (espaces/« : » tolérés).
    /// Exige exactement 32 octets.</summary>
    public static bool TryParseHex(string hex, out byte[] hash)
    {
        var chars = new char[hex.Length];
        var n = 0;
        foreach (var c in hex)
            if (Uri.IsHexDigit(c)) chars[n++] = c;
        if (n != 64)
        {
            hash = [];
            return false;
        }
        hash = Convert.FromHexString(new string(chars, 0, n));
        return true;
    }
}
