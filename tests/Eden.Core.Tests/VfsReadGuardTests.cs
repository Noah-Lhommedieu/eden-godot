using System.Text.RegularExpressions;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// P3 : les données de jeu passent par EdenVfs (pack-aware). Les appels
/// File./Directory./FileAccess./DirAccess. sont plafonnés par fichier :
/// ceux listés ci-dessous sont l'état utilisateur, la détection d'environnement,
/// l'implémentation du VFS, les outils de bake/diag et les replis dev/tests
/// (loose) ; tout nouvel appel, ou un fichier absent de la liste, fait échouer.
/// </summary>
public class VfsReadGuardTests
{
    private static readonly Regex Rx = new(
        @"\b(File\.(Exists|ReadAll[A-Za-z]*|OpenRead|ReadLines|Open)|Directory\.[A-Za-z]+|FileAccess\.[A-Za-z]+|DirAccess\.[A-Za-z]+|Image\.LoadFromFile)\(",
        RegexOptions.Compiled);

    private static readonly Dictionary<string, int> Allowed = new()
    {

        ["scripts/AssetCatalog.cs"] = 9,
        ["scripts/ClientConfig.cs"] = 2,
        ["scripts/DataPaths.cs"] = 5,
        ["scripts/data/ClientUiSettings.cs"] = 3,
        ["scripts/data/TextResolver.cs"] = 3,
        ["scripts/data/UiTextEn.cs"] = 2,
        ["scripts/data/tables/StoreTable.cs"] = 3,
        ["scripts/debug/NativeFxDemo.cs"] = 1,
        ["scripts/debug/NativeParity.cs"] = 4,
        ["scripts/debug/RenderAudit.cs"] = 1, // Directory.CreateDirectory du TSV de sortie, aucune lecture de donnée
        ["scripts/debug/PocScn.cs"] = 4,
        ["scripts/debug/ScnBake.cs"] = 4,
        ["scripts/debug/TexBake.cs"] = 5,
        ["scripts/hud/ChatPanel.cs"] = 1,
        ["scripts/hud/DragHelper.cs"] = 1,
        ["scripts/hud/HotbarPanel.cs"] = 2,
        ["scripts/hud/HudWindows.cs"] = 1,
        ["scripts/hud/MinimapPanel.cs"] = 1,
        ["scripts/hud/QuestWindow.cs"] = 1,
        ["scripts/hud/RideProbe.cs"] = 4,
        ["scripts/hud/TopLeftVitals.cs"] = 1,
        ["scripts/native/NifMaterial.cs"] = 3,
        ["scripts/perf/PerfProbe.cs"] = 1,
        ["scripts/player/PlayerController.cs"] = 1,
        ["scripts/screens/CharSelectScreen.cs"] = 2,
        ["scripts/session/EdenSession.cs"] = 2,
        ["scripts/settings/ClientSettings.cs"] = 6,
        ["scripts/settings/SettingsApplier.cs"] = 1,
        ["scripts/ui/CeguiToGodot.cs"] = 4,
        ["scripts/vfs/EdenVfs.Godot.cs"] = 9,
        ["scripts/vfs/EdenVfs.cs"] = 20,
        ["scripts/vfs/EpkCrypto.cs"] = 5,
        ["scripts/vfs/EpkManifest.cs"] = 2,
        ["src/Eden.Core/Data/ClientSettings.cs"] = 2,
        ["src/Eden.Core/Data/ClientUiSettings.cs"] = 3,
        ["src/Eden.Core/Data/TextResolver.cs"] = 3,
        ["src/Eden.Core/Data/UiTextEn.cs"] = 2,
        ["src/Eden.Core/Data/tables/StoreTable.cs"] = 3,
        ["src/Eden.Core/Pkg/EdenfrManifest.cs"] = 1,
        ["src/Eden.Core/Pkg/PkgArchive.cs"] = 2,
        ["src/Eden.Core/Session/EdenSession.cs"] = 2,
        ["src/Eden.Core/Vfs/EdenVfs.cs"] = 24,
        ["src/Eden.Core/Vfs/EpkCrypto.cs"] = 5,
        ["src/Eden.Core/Vfs/EpkManifest.cs"] = 2,
    };

    private static string Root()
    {
        var d = AppContext.BaseDirectory;
        while (d != null && !Directory.Exists(Path.Combine(d, "src", "Eden.Core")))
            d = Path.GetDirectoryName(d);
        return d ?? throw new DirectoryNotFoundException("racine du dépôt");
    }

    [Fact]
    public void AppelsFichiersPlafonnes()
    {
        var root = Root();
        var dirs = new[]
        {
            (Path.Combine(root, "eden_godot", "godot_project", "scripts"), "scripts/"),
            (Path.Combine(root, "src", "Eden.Core"), "src/Eden.Core/"),
        };
        var bad = new List<string>();
        foreach (var (dir, prefix) in dirs)
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                var n = Rx.Matches(File.ReadAllText(f)).Count;
                if (n == 0) continue;
                var key = prefix + Path.GetRelativePath(dir, f).Replace('\\', '/');
                if (!Allowed.TryGetValue(key, out var max) || n > max)
                    bad.Add($"{key} : {n} appel(s), plafond {(Allowed.TryGetValue(key, out var m) ? m : 0)}");
            }
        Assert.True(bad.Count == 0,
            "Lecture hors EdenVfs (utiliser EdenVfs.ReadAllBytes/OpenRead ou ajouter à la liste avec justification) :\n"
            + string.Join("\n", bad));
    }

    /// <summary>Phase G : plus aucun chargement de GLB ni de contenu dérivé dans le client.</summary>
    [Fact]
    public void AucunChargementGltf()
    {
        var root = Root();
        var hits = new List<string>();
        foreach (var f in Directory.EnumerateFiles(Path.Combine(root, "eden_godot", "godot_project", "scripts"), "*.cs", SearchOption.AllDirectories))
        {
            var t = File.ReadAllText(f);
            foreach (var needle in new[] { "new GltfDocument", "AppendFromFile", "AppendFromBuffer", "GltfDocumentExtension" })
                if (t.Contains(needle)) hits.Add($"{Path.GetFileName(f)} : {needle}");
        }
        Assert.True(hits.Count == 0, "chemin glTF réintroduit :\n" + string.Join("\n", hits));
        Assert.False(Directory.Exists(Path.Combine(root, "eden_godot", "godot_project", "assets")),
            "godot_project/assets/ (dérivés) ne doit plus exister");
    }
}
