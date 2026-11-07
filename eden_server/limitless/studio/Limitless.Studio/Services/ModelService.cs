using System.Collections.Generic;
using System.Linq;

namespace Limitless.Studio.Services;

/// <summary>Résolution Modèle/animations/textures — un code
/// (S_Monster col1 `M312040`, S_Item col3 `G00005`) est un code de
/// BASE + variante : `M312` → `M312.nif` (modèle NetImmerse),
/// `M312.kf` (animation), `M312.dds`/`M31201..03.dds` (textures) ;
/// `G00005` → `G00005.nif` + `G0000501.dds`. On strippe les chiffres
/// de variante jusqu'à trouver la famille dans pkg.idx.</summary>
public sealed class ModelService
{
    private readonly PkgIndexService _idx;
    public ModelService(PkgIndexService idx) => _idx = idx;

    public record ModelInfo(string Base, string? Nif,
        IReadOnlyList<string> Kf, IReadOnlyList<string> Dds);

    /// <summary>Résout le code vers la famille d'assets — retire un
    /// à un les chiffres de fin jusqu'à trouver `{base}.nif`.</summary>
    public ModelInfo? Resolve(string code)
    {
        _idx.Load();
        var c = code;
        for (var cut = 0; cut < 4 && c.Length > 1; cut++)
        {
            if (_idx.TryGetEntry(c + ".nif", out _))
                return new ModelInfo(c,
                    Nif: c + ".nif",
                    Kf: Siblings(c, ".kf"),
                    Dds: Siblings(c, ".dds"));
            if (!char.IsDigit(c[^1])) break;
            c = c[..^1];
        }
        return null;
    }


    /// <summary>Fichiers `{base}*.{ext}` dans l'index
    /// (M312.kf, M31201.dds…).</summary>
    public IReadOnlyList<string> Siblings(string baseCode, string ext)
    {
        _idx.Load();
        return _idx.Names()
            .Where(n => n.StartsWith(baseCode,
                    System.StringComparison.OrdinalIgnoreCase)
                && n.EndsWith(ext,
                    System.StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n).ToList();
    }
}
