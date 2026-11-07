using EdenClient.World;
using Xunit;
using Xunit.Abstractions;

namespace EdenClient.NetTests;

public class ScratchSpawnHeight
{
    private readonly ITestOutputHelper _o;
    public ScratchSpawnHeight(ITestOutputHelper o) => _o = o;

    private static string? FindRawAssets()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_RAW_ASSETS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "raw_assets");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    [Fact]
    public void S002_SpawnHeights()
    {
        var root = FindRawAssets();
        var p = Path.Combine(root!, "map", "model", "s002", "s002.fsm");
        var f = FsmFile.Load(p);
        Assert.NotNull(f);
        foreach (var (x, y) in new[] { (332f, 298f), (340f, 300f), (320f, 330f), (245f, 288f), (300f, 300f), (280f, 290f) })
        {
            var ok = TerrainSampler.TryGetHeight(f, x, y, out var z);
            _o.WriteLine($"({x},{y}) terrain={ok} z={z:F2}");
        }
    }
}
