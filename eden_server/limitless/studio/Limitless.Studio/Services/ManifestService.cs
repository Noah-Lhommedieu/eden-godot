using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;

namespace Limitless.Studio.Services;

/// <summary>Générateur de manifest de mise à jour client — scanne
/// un répertoire client (ou un sous-ensemble : .ini/.pkg/exe) et
/// produit `manifest.json` (chemin relatif → sha256, taille) pour
/// le launcher qui compare et ne télécharge que les fichiers
/// modifiés (voir docs/limitless/LAUNCHER-UPDATE-DESIGN.md).
/// Signature : les manifests sont signés côté serveur (Ed25519
/// recommandé) — le hash seul ne protège pas contre l'altération
/// d'un manifest intercepté.</summary>
public static class ManifestService
{
    /// <summary>Entrée du manifest — chemin relatif, taille,
    /// hash sha256 hex.</summary>
    public record Entry(string Path, long Size, string Sha256);

    /// <summary>Scanne `root` pour les `patterns` (ex.
    /// `*.ini,*.pkg,*.exe`) et retourne les entrées hashées.</summary>
    public static List<Entry> Build(string root,
        params string[] patterns)
    {
        var list = new List<Entry>();
        foreach (var pat in patterns.Length == 0
            ? new[] { "*.ini", "*.pkg", "*.exe", "*.dll" } : patterns)
            foreach (var f in Directory.GetFiles(root, pat,
                SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f)
                    .Replace('\\', '/');
                var hash = Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(f))).ToLower();
                list.Add(new Entry(rel, new FileInfo(f).Length, hash));
            }
        return list;
    }

    /// <summary>Sérialise le manifest en JSON (array de
    /// {path,size,sha256}).</summary>
    public static string ToJson(IReadOnlyList<Entry> entries)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"files\":[");
        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"path\":\"{e.Path}\",\"size\":{e.Size}" +
                $",\"sha256\":\"{e.Sha256}\"}}");
        }
        return sb.Append("]}").ToString();
    }
}
