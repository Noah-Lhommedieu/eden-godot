using Eden.Core.Data;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Incrément (d) phase 4 : <see cref="GameDataManifest"/> — empreinte
/// SHA-256 déterministe du corpus <c>*.ini</c> d'un répertoire
/// gamedata. Vérifié sur répertoires temporaires synthétiques.
/// </summary>
public class GameDataManifestTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(),
        $"eden-manifest-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, recursive: true);
    }

    private string Write(string rel, string content)
    {
        var path = Path.Combine(_tmpDir,
            rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Deux runs sur le même corpus → même empreinte, quel que
    /// soit l'ordre d'énumération (chemins triés Ordinal).</summary>
    [Fact]
    public void Compute_Deterministic_SameCorpusSameHash()
    {
        Write("S_Item.ini", "1|sword|10|");
        Write("S_Spell.ini", "5|fireball|");
        Write("sub/T_Text.ini", "9|hello|");

        var m1 = GameDataManifest.Compute(_tmpDir)!;
        var m2 = GameDataManifest.Compute(_tmpDir)!;
        Assert.NotNull(m1);
        Assert.Equal(m1.Hex, m2.Hex);
        Assert.Equal(3, m1.FileCount);
        Assert.Equal(32, m1.Hash.Length);
        Assert.Equal(64, m1.Hex.Length);
    }

    /// <summary>Corpus modifié → empreinte différente (ajout de fichier
    /// OU contenu modifié).</summary>
    [Fact]
    public void Compute_DifferentCorpus_DifferentHash()
    {
        Write("S_Item.ini", "1|sword|10|");
        var base_ = GameDataManifest.Compute(_tmpDir)!.Hex;

        Write("S_Spell.ini", "5|fireball|");
        var added = GameDataManifest.Compute(_tmpDir)!.Hex;
        Assert.NotEqual(base_, added);

        Write("S_Item.ini", "1|sword|11|");
        var edited = GameDataManifest.Compute(_tmpDir)!.Hex;
        Assert.NotEqual(added, edited);
    }

    /// <summary>Le nom de fichier compte autant que le contenu —
    /// renommer un fichier change le manifest.</summary>
    [Fact]
    public void Compute_Rename_ChangesHash()
    {
        var content = "1|sword|10|";
        Write("S_Item.ini", content);
        var a = GameDataManifest.Compute(_tmpDir)!.Hex;
        File.Delete(Path.Combine(_tmpDir, "S_Item.ini"));
        Write("S_Item2.ini", content);
        var b = GameDataManifest.Compute(_tmpDir)!.Hex;
        Assert.NotEqual(a, b);
    }

    /// <summary>Chemin inexistant ou vide de .ini → null (manifest
    /// inconnu — comparaison sautée côté gate).</summary>
    [Fact]
    public void Compute_EmptyOrMissing_ReturnsNull()
    {
        Assert.Null(GameDataManifest.Compute(
            Path.Combine(_tmpDir, "nope")));
        Directory.CreateDirectory(_tmpDir);
        Write("readme.txt", "pas un ini");
        Assert.Null(GameDataManifest.Compute(_tmpDir));
    }

    /// <summary>TryParseHex : 64 hex → 32 octets ; séparateurs et casse
    /// tolérés ; autre longueur → false.</summary>
    [Fact]
    public void TryParseHex_RoundTrip()
    {
        var bytes = Enumerable.Range(0, 32).Select(i => (byte)i)
            .ToArray();
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        Assert.True(GameDataManifest.TryParseHex(hex, out var parsed));
        Assert.Equal(bytes, parsed);
        // Séparateurs « : » + majuscules.
        var spaced = string.Join(':', Convert.ToHexString(bytes)
            .Chunk(2).Select(c => new string(c)));
        Assert.True(GameDataManifest.TryParseHex(spaced, out parsed));
        Assert.Equal(bytes, parsed);
        Assert.False(GameDataManifest.TryParseHex("abcd", out _));
        Assert.False(GameDataManifest.TryParseHex("", out _));
    }
}
