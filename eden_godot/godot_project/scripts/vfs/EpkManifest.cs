using System.Text.Json;

namespace EdenClient.Vfs;

/// <summary>
/// Manifest « current.json » — pointeur de set de packs installé par
/// <c>edenpack install</c> et lu par <see cref="EdenVfs.Mount"/>.
/// Format JSON (spec docs/packaging/EPK-FORMAT.md § Multi-packs) :
///   {"version":int, "packs":[{"name","file","sha256"}],
///    "generated":"UTC ISO 8601",
///    ["sig":hex-128, "keyid":int]}   // signé = + sig/keyid
/// La signature porte sur la forme canonique <see cref="Serialize"/>
/// (ordre des champs fixe, sans sig/keyid) — ECDSA P-256, même
/// primitive que les index de packs. Un manifest dont la signature
/// est présente mais invalide doit être rejeté (anti-rollback) ;
/// non signé = accepté (dev), à durcir en release.
/// Pur .NET — parse JsonDocument (zéro codegen, export-friendly).
/// </summary>
public sealed class EpkManifest
{
    public sealed class PackRef
    {
        public string Name = "";
        public string File = "";
        public string Sha256 = "";
    }

    public int Version;
    public List<PackRef> Packs = new();
    public string Generated = "";
    /// <summary>Signature ECDSA sur <see cref="Serialize"/> (null =
    /// manifest non signé).</summary>
    public byte[]? Sig;
    public uint KeyId;

    /// <summary>Parse current.json — null si absent ou illisible
    /// (le caller retombe alors sur le scan *.epk).</summary>
    public static EpkManifest? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = doc.RootElement;
            var m = new EpkManifest();
            if (root.TryGetProperty("version", out var v)
                && v.ValueKind == JsonValueKind.Number)
                m.Version = v.GetInt32();
            if (root.TryGetProperty("generated", out var g))
                m.Generated = g.GetString() ?? "";
            if (root.TryGetProperty("packs", out var packs)
                && packs.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in packs.EnumerateArray())
                {
                    var r = new PackRef();
                    if (p.TryGetProperty("name", out var n))
                        r.Name = n.GetString() ?? "";
                    if (p.TryGetProperty("file", out var f))
                        r.File = f.GetString() ?? "";
                    if (p.TryGetProperty("sha256", out var s))
                        r.Sha256 = s.GetString() ?? "";
                    // Entrée sans fichier = invalide → manifest rejeté.
                    if (r.File.Length == 0) return null;
                    m.Packs.Add(r);
                }
            }
            if (root.TryGetProperty("sig", out var sig)
                && sig.ValueKind == JsonValueKind.String)
                m.Sig = Convert.FromHexString(sig.GetString() ?? "");
            if (root.TryGetProperty("keyid", out var kid)
                && kid.ValueKind == JsonValueKind.Number)
                m.KeyId = kid.GetUInt32();
            return m;
        }
        catch (Exception) { return null; }
    }

    /// <summary>Sérialise au format manifest (ordre des champs
    /// fixe : version, packs, generated).</summary>
    public static byte[] Serialize(EpkManifest m)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms,
                   new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", m.Version);
            w.WritePropertyName("packs");
            w.WriteStartArray();
            foreach (var p in m.Packs)
            {
                w.WriteStartObject();
                w.WriteString("name", p.Name);
                w.WriteString("file", p.File);
                w.WriteString("sha256", p.Sha256);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("generated", m.Generated);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    /// <summary>Forme canonique signée + champs sig/keyid en fin
    /// (après generated — l'ordre des champs reste fixe).</summary>
    public static byte[] SerializeSigned(EpkManifest m)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms,
                   new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", m.Version);
            w.WritePropertyName("packs");
            w.WriteStartArray();
            foreach (var p in m.Packs)
            {
                w.WriteStartObject();
                w.WriteString("name", p.Name);
                w.WriteString("file", p.File);
                w.WriteString("sha256", p.Sha256);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("generated", m.Generated);
            if (m.Sig != null)
            {
                w.WriteString("sig", Convert.ToHexString(m.Sig));
                w.WriteNumber("keyid", m.KeyId);
            }
            w.WriteEndObject();
        }
        return ms.ToArray();
    }
}
