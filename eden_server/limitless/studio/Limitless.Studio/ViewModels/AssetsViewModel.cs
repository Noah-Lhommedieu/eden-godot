using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Limitless.Studio.Services;

namespace Limitless.Studio.ViewModels;

/// <summary>Page Client assets — remplacement d'asset pkg (modding
/// client : icônes/DDS custom via PkgWriter) + génération du
/// manifest de mise à jour (launcher remote-update). Affiche aussi
/// les chemins résolus pour vérifier que le bon client est visé.</summary>
public partial class AssetsViewModel : PageViewModel
{
    public AssetsViewModel(StudioContext ctx) : base(ctx) { }

    public override string Title => "Client assets";
    public override string Subtitle =>
        "pkg patching + update manifest — Reforged client";

    [ObservableProperty] private string _replaceAssetName = "";
    [ObservableProperty] private string _replaceSourcePath = "";
    [ObservableProperty] private bool _isBusy;

    public string ClientPath => Ctx.ClientDir;
    public string PkgPath => Ctx.PkgDir;
    public string ClientStatus => Ctx.ClientDirExists
        ? "Client folder found" : "Client folder NOT found";
    public string PkgStatus =>
        Directory.Exists(Ctx.PkgDir)
            ? $"pkg.idx entries: {Ctx.Icons.PkgCount}"
            : "pkg folder missing";

    /// <summary>Remplace un asset du client par un fichier local —
    /// zlib → append pkg999 + patch pkg.idx en place.</summary>
    [RelayCommand]
    private void ReplaceAsset()
    {
        if (string.IsNullOrWhiteSpace(ReplaceAssetName) ||
            string.IsNullOrWhiteSpace(ReplaceSourcePath))
        { Status("Asset name and source file are required"); return; }
        if (!File.Exists(ReplaceSourcePath))
        { Status($"Source file missing: {ReplaceSourcePath}"); return; }
        var data = File.ReadAllBytes(ReplaceSourcePath);
        var r = new PkgWriter(Ctx.PkgDir)
            .Replace(ReplaceAssetName, data);
        Status(r is { } x
            ? $"{ReplaceAssetName} → pkg{x.Pkg:D3}@0x{x.Offset:x} " +
              "(pkg.idx updated in place)"
            : $"Asset {ReplaceAssetName} not found in pkg.idx");
    }

    /// <summary>Génère `manifest.json` (sha256 par fichier) à la
    /// racine du client — base du launcher remote-update.</summary>
    [RelayCommand]
    private async Task BuildManifest()
    {
        var root = Ctx.ClientDir;
        if (!Directory.Exists(root))
        { Status($"Client folder missing: {root}"); return; }
        IsBusy = true;
        Status("Building manifest…");
        try
        {
            var entries = await Task.Run(
                () => ManifestService.Build(root));
            var json = ManifestService.ToJson(entries);
            var outPath = Path.Combine(root, "manifest.json");
            await File.WriteAllTextAsync(outPath, json);
            Status($"Manifest: {entries.Count} files → {outPath}");
        }
        finally { IsBusy = false; }
    }
}
