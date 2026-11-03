using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du lecteur <see cref="NifFile"/> sur les vraies données
/// EDENFR : nifs stripés (<c>corpus/fr-full</c> — parse sans exception,
/// mesh explicitement vide) vs nifs complets (<c>~/Téléchargements/
/// EDENFR/extracted</c> — vrais triangles). Skippés si les arbres
/// sont absents.
/// </summary>
public class NifFileTests
{
    /// <summary>Racine corpus (client_assets → corpus/fr-full).</summary>
    private static string? FindCorpus()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_CORPUS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "client_assets");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    /// <summary>Racine des nifs complets EDENFR (hors dépôt).</summary>
    private static string? FindExtracted()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_NIF_ROOT");
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        var def = Path.Combine(home,
            "Téléchargements", "EDENFR", "extracted");
        foreach (var p in new[] { env, def })
            if (!string.IsNullOrEmpty(p) && Directory.Exists(p))
                return p;
        return null;
    }

    private static NifFile? ParseInsensitive(string root, string rel)
    {
        var p = EdenClient.Vfs.EdenVfs.ResolveInsensitive(root, rel);
        return p != null ? NifFile.Parse(File.ReadAllBytes(p)) : null;
    }

    private static string? FindDegradedPak()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10; i++)
        {
            var p = Path.Combine(dir, "corpus", "fr-full-pak-2026-10");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    [Fact]
    public void StrippedUe013ParsesWithoutException()
    {
        // Copie dégradée de l'ancien extracteur (conservée en lecture seule) :
        // le lecteur doit la tolérer. Le corpus courant est authentique.
        var root = FindDegradedPak();
        if (root == null) return;               // skip
        var nif = ParseInsensitive(root, "map/model/object/UE013.nif");
        Assert.NotNull(nif);
        Assert.Equal(0, nif.TriangleCount);     // stripé → 0 triangle
        Assert.DoesNotContain(nif.Shapes, s => s.Indices.Length > 0);
    }

    [Fact]
    public void ExtractedUe013HasRealTriangles()
    {
        var root = FindExtracted();
        if (root == null) return;
        var nif = ParseInsensitive(root, "map/model/object/UE013.nif");
        Assert.NotNull(nif);
        Assert.True(nif.TriangleCount >= 900,   // 824 + 132 attendus
            $"UE013 : {nif.TriangleCount} tris");
        Assert.Contains(nif.Shapes, s =>
            s.TextureFile?.EndsWith("UE01301.dds",
                StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void ExtractedUb107HasRealTriangles()
    {
        var root = FindExtracted();
        if (root == null) return;
        var nif = ParseInsensitive(root, "map/model/object/UB107.nif");
        Assert.NotNull(nif);
        Assert.True(nif.TriangleCount >= 60,    // 54 + 6 attendus
            $"UB107 : {nif.TriangleCount} tris");
    }

    [Fact]
    public void ExtractedPlayerModelParses()
    {
        var root = FindExtracted();
        if (root == null) return;
        var nif = ParseInsensitive(root, "char/model/b.nif");
        Assert.NotNull(nif);
        Assert.True(nif.TriangleCount > 0,      // bind pose réelle
            $"b.nif : {nif.TriangleCount} tris");
        Assert.Contains(nif.Shapes, s =>
            s.Name.StartsWith("S", StringComparison.OrdinalIgnoreCase) ||
            s.Name.Contains("FACE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CaseInsensitiveObjectPathResolves()
    {
        var root = FindExtracted();
        if (root == null) return;
        var up = EdenClient.Vfs.EdenVfs.ResolveInsensitive(
            root, "MAP/MODEL/OBJECT/UE013.NIF");
        var low = EdenClient.Vfs.EdenVfs.ResolveInsensitive(
            root, "map/model/object/ue013.nif");
        Assert.NotNull(up);
        Assert.NotNull(low);
        Assert.Equal(low, up);
    }

    [Fact]
    public void HelperNamesAreDetected()
    {
        Assert.True(NifFile.IsCollisionHelper("true wall01"));
        Assert.True(NifFile.IsCollisionHelper("blo"));
        Assert.True(NifFile.IsCollisionHelper("line3"));
        Assert.False(NifFile.IsCollisionHelper("Object06"));
    }

    [Fact]
    public void S009ObjectCountIsKnown()
    {
        // Garde-fou : S009 doit exposer les 2321 OBJ framés — le même
        // comptage pilote le spawn des props M4.
        var corpus = FindCorpus();
        if (corpus == null) return;
        var p = EdenClient.Vfs.EdenVfs.ResolveInsensitive(
            corpus, "map/model/s009/s009.fsm");
        if (p == null) return;
        var fsm = FsmFile.Load(p);
        Assert.NotNull(fsm);
        Assert.Equal(2321, fsm.SceneObjects.Count);
        Assert.Equal(3292, fsm.GrassInstances.Count);
        Assert.Equal(2, fsm.GrassTypes.Count);
    }
}
