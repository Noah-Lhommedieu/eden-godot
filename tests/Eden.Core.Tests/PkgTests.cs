using EdenClient.Pkg;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// CRC32C du manifeste, index pkg et inflate à fenêtre 64 Ko. Les tests sur pkg
/// sont ignorés si le client EDENFR (EDEN_EDENFR_DIR ou ~/Téléchargements/EDENFR) est absent.
/// </summary>
public class PkgTests
{
    static string? ClientDir()
    {
        var d = Environment.GetEnvironmentVariable("EDEN_EDENFR_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Téléchargements", "EDENFR");
        return File.Exists(Path.Combine(d, "pkg", "pkg.idx")) ? d : null;
    }

    static PkgEntry Find(PkgArchive a, string path) =>
        a.Entries.First(e => e.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void ManifestCrc_is_crc32c_check_vector() =>
        Assert.Equal(0xE3069283u, ManifestCrc.Compute("123456789"u8));

    [Fact]
    public void Manifest_parses_lines_and_ignores_case()
    {
        var m = EdenfrManifest.Parse("ACDC.dat:20101106055246:949180:1162122698\r\nChar\\Model\\B.nif:2016:5:7\r\nbad line\r\n");
        Assert.Equal(2, m.Count);
        Assert.True(m.TryGet("acdc.DAT", out var e));
        Assert.Equal((949180L, 1162122698u), (e.Size, e.Crc));
        Assert.True(m.TryGet("char/model/b.nif", out _));
    }

    [Fact]
    public void SafePath_rejects_traversal_and_lowers_uidata()
    {
        Assert.Equal("uidata/layouts/a.layout", PkgArchive.SafePath("UIData\\layouts\\a.layout"));
        Assert.Throws<InvalidDataException>(() => PkgArchive.SafePath("a/../b"));
    }

    [Theory]
    [InlineData("char/model/B1001.nif", 5787, 3722075921u)]                 // zlib standard
    [InlineData("map/model/object/ub124.nif", 0, 0u)]                        // dégradé dans fr-full-pak : sain ici
    [InlineData("effect/texture/S1762108.ifl", 28, 1012737455u)]            // zsize == size mais compressé
    [InlineData("char/animation/b.kf", 24631391, 1566560833u)]               // fenêtre 64 Ko
    public void Pkg_entries_are_conform_to_manifest(string path, int size, uint crc)
    {
        var dir = ClientDir();
        if (dir == null) return;
        var a = new PkgArchive(Path.Combine(dir, "pkg"));
        var e = Find(a, path);
        var data = a.Read(e);                                // lève si le CRC de l'index diffère
        if (size != 0) Assert.Equal((size, crc), (data.Length, e.Crc));
        var man = EdenfrManifest.Load(Path.Combine(dir, "GameDataFileList.txt"));
        Assert.True(man.Matches(path, data));
    }
}
