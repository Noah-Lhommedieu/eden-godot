using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Limitless.Studio.Services;

/// <summary>Détecte les sources .ini en conflit — le même fichier
/// présent à plusieurs endroits du client (data/ décodé, pkg,
/// T_*.ini UI) : si le serveur lit l'un et le client l'autre, les
/// éditions du Studio peuvent ne pas être vues. Rapporte les
/// fichiers dupliqués par nom avec tailles/hash pour comparer.</summary>
public static class IniSourceService
{
    /// <summary>Un groupe de doublons — nom de fichier + tous les
    /// chemins où il apparaît (taille + sha256 pour détecter si les
    /// copies divergent vraiment ou sont identiques).</summary>
    public record Conflict(string Name,
        List<(string Path, long Size, string Sha256)> Copies);

    /// <summary>Scanne `roots` pour les .ini et groupe par nom —
    /// retourne ceux présents dans PLUS d'un emplacement.</summary>
    public static IReadOnlyList<Conflict> FindConflicts(
        params string[] roots)
    {
        var byName = new Dictionary<string,
            List<(string, long, string)>>(System.StringComparer
            .OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
            foreach (var f in Directory.GetFiles(root, "*.ini",
                SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(f);
                if (!byName.TryGetValue(name, out var list))
                    byName[name] = list = new();
                var hash = System.Security.Cryptography.SHA256
                    .HashData(File.ReadAllBytes(f));
                list.Add((f, new FileInfo(f).Length,
                    System.Convert.ToHexString(hash).ToLower()));
            }
        return byName.Where(kv => kv.Value.Count > 1)
            .Select(kv => new Conflict(kv.Key, kv.Value))
            .OrderBy(c => c.Name).ToList();
    }
}
