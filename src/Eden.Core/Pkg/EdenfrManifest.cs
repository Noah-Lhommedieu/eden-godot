using System.Text;

namespace EdenClient.Pkg;

/// <summary>Une ligne de GameDataFileList.txt : <c>chemin:date:taille:CRC32C</c>.</summary>
public readonly record struct ManifestEntry(string Path, string Date, long Size, uint Crc);

/// <summary>Manifeste officiel EDENFR (47 601 fichiers). Recherche insensible à la casse.</summary>
public sealed class EdenfrManifest
{
    readonly Dictionary<string, ManifestEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<ManifestEntry> Entries => _byPath.Values;
    public int Count => _byPath.Count;

    public static EdenfrManifest Parse(string text)
    {
        var m = new EdenfrManifest();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var p = line.Split(':');
            if (p.Length < 4 || !long.TryParse(p[2], out var size) || !uint.TryParse(p[3], out var crc)) continue;
            var path = p[0].Replace('\\', '/');
            m._byPath[path] = new ManifestEntry(path, p[1], size, crc);
        }
        return m;
    }

    public static EdenfrManifest Load(string file) =>
        Parse(Encoding.Latin1.GetString(File.ReadAllBytes(file)));

    public bool TryGet(string path, out ManifestEntry e) => _byPath.TryGetValue(path.Replace('\\', '/'), out e);

    /// <summary>Vrai si les octets ont la taille et le CRC32C du manifeste.</summary>
    public bool Matches(string path, ReadOnlySpan<byte> data) =>
        TryGet(path, out var e) && e.Size == data.Length && e.Crc == ManifestCrc.Compute(data);
}
