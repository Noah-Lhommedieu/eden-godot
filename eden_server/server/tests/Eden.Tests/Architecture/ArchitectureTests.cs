using System.Reflection;

namespace Eden.Tests.Architecture;

/// <summary>
/// Garde-fous mécaniques : la qualité devient une règle exécutable.
/// Les anciens manquements sont gelés dans la baseline (qui ne peut que
/// baisser) ; tout NOUVEAU manquement fait échouer le test.
/// </summary>
public class ArchitectureTests
{
    private static readonly bool Regenerate =
        Environment.GetEnvironmentVariable("EDEN_ARCH_REGENERATE") == "1";

    private static List<(string Rule, string Path, int Value)> Measure()
    {
        var rows = new List<(string, string, int)>();
        var zLines = 0; var zFiles = 0;
        foreach (var (rel, text) in ArchitectureScanner.SourceFiles())
        {
            var lines = ArchitectureScanner.LineCount(text);
            if (lines > ArchitectureScanner.MaxLines) rows.Add(("lines", rel, lines));
            var ec = ArchitectureScanner.CountEmptyCatch(text);
            if (ec > 0) rows.Add(("empty_catch", rel, ec));
            if (!ArchitectureScanner.IsFoundation(rel))
            {
                var r = ArchitectureScanner.CountDirectRandom(text);
                if (r > 0) rows.Add(("direct_random", rel, r));
                var n = ArchitectureScanner.CountDirectNow(text);
                if (n > 0) rows.Add(("direct_now", rel, n));
            }
            if (ArchitectureScanner.IsZoneEntryFlow(rel)) { zLines += lines; zFiles++; }
        }
        rows.Add(("zone_entry_flow_lines", "ZoneEntryFlow*", zLines));
        rows.Add(("zone_entry_flow_files", "ZoneEntryFlow*", zFiles));
        return rows;
    }

    private static void Check(string rule, string message)
    {
        var baseline = ArchitectureBaseline.Load();
        var bad = Measure().Where(r => r.Rule == rule && r.Value > baseline.Allowed(rule, r.Path))
            .Select(r => $"{r.Path}: {r.Value} > toléré {baseline.Allowed(rule, r.Path)}").ToList();
        Assert.True(bad.Count == 0, message + "\n" + string.Join("\n", bad));
    }

    [Fact]
    public void RegenerateBaseline_OnlyWhenAsked()
    {
        if (!Regenerate) return;
        ArchitectureBaseline.Write(Measure());
    }

    [Fact]
    public void NewFiles_StayUnder400Lines() =>
        Check("lines", "Fichier > 400 lignes (nouveau ou agrandi). Découper en modules.");

    [Fact]
    public void NoNewEmptyCatch() =>
        Check("empty_catch", "catch vide interdit : journaliser ou laisser remonter.");

    [Fact]
    public void NoNewDirectRandom() =>
        Check("direct_random", "Random.Shared / new Random interdit : injecter IRandom (Foundation).");

    [Fact]
    public void NoNewDirectClock() =>
        Check("direct_now", "DateTime.Now / UtcNow interdit : injecter IGameClock (Foundation).");

    [Fact]
    public void ZoneEntryFlow_MustNotGrow()
    {
        var baseline = ArchitectureBaseline.Load();
        var m = Measure();
        var lines = m.Single(r => r.Rule == "zone_entry_flow_lines").Value;
        var files = m.Single(r => r.Rule == "zone_entry_flow_files").Value;
        Assert.True(lines <= baseline.Allowed("zone_entry_flow_lines", "ZoneEntryFlow*"),
            $"ZoneEntryFlow a grossi : {lines} lignes. Tout nouveau code = module (IZoneFeature).");
        Assert.True(files <= baseline.Allowed("zone_entry_flow_files", "ZoneEntryFlow*"),
            $"ZoneEntryFlow a plus de fichiers partiels : {files}.");
    }

    [Fact]
    public void Core_DoesNotReferenceGodot()
    {
        var bad = ArchitectureScanner.SourceFiles()
            .Where(f => f.Rel.StartsWith("src/Eden.Core/")
                && (f.Text.Contains("using Godot") || f.Text.Contains("GodotSharp")))
            .Select(f => f.Rel).ToList();
        Assert.Empty(bad);
    }

    // --- Les règles elles-mêmes (non-régression des détecteurs) ---

    [Fact]
    public void Detector_FlagsFileOver400Lines() =>
        Assert.True(ArchitectureScanner.LineCount(string.Join("\n", Enumerable.Repeat("x", 401)))
            > ArchitectureScanner.MaxLines);

    [Fact]
    public void Detector_FlagsEmptyCatch()
    {
        Assert.Equal(1, ArchitectureScanner.CountEmptyCatch("try { a(); } catch { }"));
        Assert.Equal(1, ArchitectureScanner.CountEmptyCatch("try { a(); } catch (Exception) {}"));
        Assert.Equal(0, ArchitectureScanner.CountEmptyCatch("try { a(); } catch (Exception e) { Log(e); }"));
    }

    [Fact]
    public void Detector_FlagsDirectRandomAndClock()
    {
        Assert.Equal(2, ArchitectureScanner.CountDirectRandom("Random.Shared.Next(); new Random(3);"));
        Assert.Equal(2, ArchitectureScanner.CountDirectNow("DateTime.Now; DateTimeOffset.UtcNow;"));
        Assert.Equal(0, ArchitectureScanner.CountDirectNow("clock.Now"));
    }
}
