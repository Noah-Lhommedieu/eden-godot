using Godot;
using EdenClient.World;

namespace EdenClient.Debugging;

/// <summary>Sonde headless : diagnostique le rendu du terrain —
/// résolution des textures de slots, uniforms du shader splat,
/// stats splatmap. Usage : godot --headless --path . scenes/vis_diag.tscn</summary>
public partial class VisDiag : Node3D
{
    public override void _Ready()
    {
        GD.Print($"[diag] AssetsRoot={AssetCatalog.AssetsRoot}");
        GD.Print($"[diag] RawAssetsRoot={AssetCatalog.RawAssetsRoot}");

        var mapCode = "S009";
        var tileIdx = 0;
        var dumpMap = false;
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
        {
            if (a.StartsWith("--map=")) mapCode = a[6..];
            else if (a.StartsWith("--tile=") &&
                     int.TryParse(a[7..], out var ti)) tileIdx = ti;
            else if (a == "--dump-map") dumpMap = true;
        }

        if (dumpMap)
        {
            var loader = new MapLoader();
            var root = loader.LoadMap(mapCode);
            var stats = new Dictionary<string, int>();
            foreach (var meshInst in root.FindChildren("*", "MeshInstance3D",
                         true, false).Cast<MeshInstance3D>())
            {
                var mat = meshInst.MaterialOverride ??
                    (meshInst.Mesh != null && meshInst.Mesh.GetSurfaceCount() > 0
                        ? meshInst.Mesh.SurfaceGetMaterial(0) : null);
                var key = mat is ShaderMaterial smat
                    ? $"shader:{smat.Shader?.ResourcePath}"
                    : mat is StandardMaterial3D bm
                        ? $"std:tex={bm.AlbedoTexture != null} color={bm.AlbedoColor}"
                        : mat?.GetType().Name ?? "null";
                stats[key] = stats.GetValueOrDefault(key) + 1;
                var aabb = meshInst.GetAabb();
                if (aabb.Size.LengthSquared() > 10000f)
                {
                    var chain = new List<string>();
                    for (Node? n = meshInst; n != root && n != null;
                         n = n.GetParent())
                        chain.Insert(0, n.Name.ToString());
                    var texName = "";
                    if (mat is StandardMaterial3D bmat &&
                        bmat.AlbedoTexture?.ResourcePath.Length > 0)
                        texName = bmat.AlbedoTexture.ResourcePath;
                    GD.Print($"[dump] BIG {string.Join('/', chain)} " +
                             $"origin={meshInst.GlobalPosition} " +
                             $"size={aabb.Size} mat={key} tex={texName}");
                }
            }
            foreach (var kv in stats.OrderByDescending(x => x.Value))
                GD.Print($"[dump] {kv.Value,5} × {kv.Key}");
        }

        // Résolution directe des textures S009.
        foreach (var n in new[]
                 { "mount_04", "dust_04", "dust_08", "grass_05",
                   "road_04", "brick_03", "water_env" })
        {
            var t = AssetCatalog.GetTexture($"map/texture/{n}.dds");
            if (t == null)
                GD.Print($"[diag] tex {n}: NULL");
            else
            {
                var img = t.GetImage();
                GD.Print($"[diag] tex {n}: {img?.GetWidth()}x" +
                         $"{img?.GetHeight()}");
            }
        }

        // Charge le fsm et construit une tuile — inspecte le matériau.
        var fsmPath = AssetCatalog.ResolveRawAsset(
            $"map/model/{mapCode}/{mapCode}.fsm");
        if (fsmPath == null)
        {
            GD.PrintErr($"[diag] {mapCode}.fsm introuvable");
            GetTree().Quit();
            return;
        }
        var fsm = FsmFile.Load(fsmPath);
        var kinds = new System.Collections.Generic.Dictionary<uint, int>();
        foreach (var t in fsm.Tiles)
            kinds[t.Kind] = kinds.GetValueOrDefault(t.Kind) + 1;
        GD.Print($"[diag] tuiles={fsm.Tiles.Count} " +
                 $"kinds={string.Join(',', kinds)}");

        var tile = fsm.Tiles[Math.Min(tileIdx, fsm.Tiles.Count - 1)];
        // Résolution effective des textures des slots de CETTE tuile.
        for (var i = 0; i < FsmFile.TexSlotCount; i++)
        {
            var s = tile.GetTexSlot(i);
            if (!s.Used || s.Name.Length == 0) continue;
            var t = AssetCatalog.GetTexture($"map/texture/{s.Name}");
            GD.Print($"[diag] {mapCode} slot{i} «{s.Name}» " +
                     $"uv={s.UvScale} → {(t == null ? "NULL" : "ok")}");
        }
        var mi = TerrainBuilder.BuildTile(tile);
        if (mi.MaterialOverride is ShaderMaterial sm)
        {
            for (var i = 0; i < 5; i++)
            {
                var tex = sm.GetShaderParameter($"layer{i}")
                    .As<ImageTexture>();
                var sc = sm.GetShaderParameter($"uv_scale{i}")
                    .AsSingle();
                var img = tex?.GetImage();
                GD.Print($"[diag] tile0 layer{i}: tex=" +
                         $"{(img == null ? "null" : $"{img.GetWidth()}x{img.GetHeight()}")} " +
                         $"uvScale={sc} slot={tile.GetTexSlot(i).Name}");
            }
            var splat = sm.GetShaderParameter("splat_map")
                .As<ImageTexture>()?.GetImage();
            if (splat != null)
            {
                long r = 0, g = 0, b = 0, a = 0;
                for (var y = 0; y < splat.GetHeight(); y++)
                for (var x = 0; x < splat.GetWidth(); x++)
                {
                    var c = splat.GetPixel(x, y);
                    r += (long)(c.R * 255); g += (long)(c.G * 255);
                    b += (long)(c.B * 255); a += (long)(c.A * 255);
                }
                var n = splat.GetWidth() * splat.GetHeight();
                GD.Print($"[diag] tile0 splat avg RGBA = " +
                         $"{r / n},{g / n},{b / n},{a / n}");
            }
        }
        else
        {
            GD.PrintErr($"[diag] tile0 material = " +
                        $"{mi.MaterialOverride?.GetType().Name ?? "null"}");
        }
        GetTree().Quit();
    }
}
