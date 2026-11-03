using Godot;

namespace EdenClient.World;

/// <summary>
/// Construit les <see cref="MeshInstance3D"/> des tuiles d'un
/// <see cref="FsmFile"/> — un mesh ArrayMesh par tuile (33×33 vertices
/// → 32×32 quads → 2 triangles/quad, indices reconstruits u32).
///
/// Coordonnées : vertices natifs en coords monde Eden Z-up (locaux au
/// node placé à l'origine de la tuile) — à mettre sous un Node3D racine
/// tourné à −90°/X (<see cref="WorldConv.EdenRootEulerDeg"/>).
///
/// Matériau : <c>res://shaders/terrain_splat.gdshader</c> pour les
/// tuiles kind=0 (4 canaux splat → slots 1..4, base implicite) ;
/// <see cref="StandardMaterial3D"/> translucide pour kind=1 (eau).
///
/// Attributs vertex : POSITION, NORMAL (attr #2 natif — unitaire
/// vérifié), COLOR_0 (attr #3 — éclairage baké RGB, réel sur 40+ maps
/// : 0.4-1.0, ombres portées + teinte froide ; alpha ≈ fade d'eau sur
/// les tuiles kind=1), UV = posMonde / tailleTuile (cf. shader).
/// </summary>
public static class TerrainBuilder
{
    private static ShaderMaterial? _splatProto;

    /// <summary>Construit le Node3D « Terrain » (un MeshInstance3D par
    /// record, coords natives Eden). Ajoute aussi l'herbe GRS/GRL
    /// (<see cref="GrassRenderer"/>) et le plafond de fog
    /// (<see cref="FogCeiling"/>, CanvasLayer — il remonte à la racine
    /// « Map_Sxxx » pour lire l'env ini ; inerte sinon). Ces deux
    /// hooks sont le seul point d'insertion du pipeline monde sans
    /// toucher MapLoader.</summary>
    public static Node3D Build(FsmFile fsm, string? geoCode = null)
    {
        var root = new Node3D { Name = "Terrain" };
        foreach (var tile in fsm.Tiles)
            root.AddChild(BuildTile(tile, geoCode));
        if (GrassRenderer.Build(fsm) is { } grass)
            root.AddChild(grass);
        if (FogCeiling.Create() is { } fog)
            root.AddChild(fog);
        return root;
    }

    /// <summary>Construit le MeshInstance3D d'une tuile.</summary>
    public static MeshInstance3D BuildTile(TerrainTile tile,
        string? geoCode = null)
    {
        var mi = new MeshInstance3D
        {
            Name = $"Tile_{tile.TileX}_{tile.TileY}" +
                   (tile.Kind == FsmFile.TileKindWater ? "_water" : ""),
            Mesh = BuildMesh(tile),
            Position = new Vector3(tile.OriginX, tile.OriginY, 0f),
        };
        mi.MaterialOverride = tile.Kind == FsmFile.TileKindWater
            ? BuildWaterMaterial(tile)
            : BuildSplatMaterial(tile, geoCode);
        if (tile.Kind == FsmFile.TileKindWater)
            mi.CastShadow =
                GeometryInstance3D.ShadowCastingSetting.Off;
        return mi;
    }

    /// <summary>Mesh ArrayMesh : positions LOCALES à la tuile
    /// (origine soustraite — positions natives absolues sinon).</summary>
    public static ArrayMesh BuildMesh(TerrainTile tile)
    {
        var w = tile.GridW;
        var h = tile.GridH;
        var verts = new Vector3[w * h];
        var normals = new Vector3[w * h];
        var colors = new Godot.Color[w * h];
        var uvs = new Vector2[w * h];
        var tileSize = (w - 1) * tile.CellSize;
        var needNormals = false;

        for (var iy = 0; iy < h; iy++)
        {
            for (var ix = 0; ix < w; ix++)
            {
                var v = tile.GetVertex(ix, iy);
                var i = iy * w + ix;
                // Position locale (monde absolu − origine tuile).
                verts[i] = new Vector3(v.Position.X - tile.OriginX,
                    v.Position.Y - tile.OriginY, v.Position.Z);
                var n = v.Normal;
                if (n.LengthSquared() is > 0.9f and < 1.1f)
                {
                    normals[i] = new Vector3(n.X, n.Y, n.Z);
                }
                else
                {
                    needNormals = true;
                }
                var c = v.Color; // HYPOTHESIS: RGBA color
                colors[i] = new Godot.Color(c.X, c.Y, c.Z, c.W);
                // UV = posMonde / tailleTuile → fract() dans le shader.
                uvs[i] = new Vector2(v.Position.X / tileSize,
                    v.Position.Y / tileSize);
            }
        }

        // Normales non unitaires/absentes → calcul depuis les hauteurs.
        if (needNormals)
            ComputeNormals(verts, normals, w, h);

        // Indices : 2 triangles/quad, winding CCW vu depuis +z.
        var indices = new int[(w - 1) * (h - 1) * 6];
        var k = 0;
        for (var iy = 0; iy < h - 1; iy++)
        {
            for (var ix = 0; ix < w - 1; ix++)
            {
                var v = iy * w + ix;
                indices[k++] = v;
                indices[k++] = v + w;
                indices[k++] = v + 1;
                indices[k++] = v + 1;
                indices[k++] = v + w;
                indices[k++] = v + w + 1;
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    /// <summary>Matériau splat pour une tuile terrain : splatmap +
    /// jusqu'à 5 textures (manquantes → blanc).</summary>
    public static ShaderMaterial BuildSplatMaterial(TerrainTile tile,
        string? geoCode = null)
    {
        var mat = SplatProto.Duplicate() as ShaderMaterial;
        if (mat == null)
            return new ShaderMaterial { Shader = LoadSplatShader() };
        if (DebugLayer >= 0)
            mat.SetShaderParameter("debug_layer", DebugLayer);

        var splatImg = Image.CreateFromData(128, 128, false,
            Image.Format.Rgba8, tile.SplatBytes.ToArray());
        mat.SetShaderParameter("splat_map",
            ImageTexture.CreateFromImage(splatImg));

        // DarkMap natif de la tuile (lightmap multiplicative, UV 0..1).
        var dark = geoCode != null && tile.DarkMapName.Length > 0
            ? AssetCatalog.GetTexture(
                $"map/model/{geoCode}/dark/{tile.DarkMapName}.dds")
            : null;
        mat.SetShaderParameter("dark_map", dark ?? AssetCatalog.White);

        // Convention uvScale native (rf_game FUN_00b02100 @0x00b02100) :
        // l'UV vertex est tilée par la texture de base (slot0) et le
        // transform de chaque stage N = uvScale0/uvScaleN — la couche N
        // est donc échantillonnée à wuv × uvScale0²/uvScaleN, PAS
        // wuv × uvScaleN (notre ancienne convention : ~2-3× trop
        // grossier → sol « flou »).
        var scale0 = tile.GetTexSlot(0).UvScale;
        if (scale0 <= 0f) scale0 = 1f;
        // Textures des 5 slots : manquante → log + repli sur un slot
        // voisin utilisé de la même tuile (jamais blanc : un slot sans
        // texture rendait la zone aplati-gris — symptôme H3).
        var texs = new ImageTexture?[FsmFile.TexSlotCount];
        var firstUsed = -1;
        for (var i = 0; i < FsmFile.TexSlotCount; i++)
        {
            var slot = tile.GetTexSlot(i);
            if (!slot.Used || slot.Name.Length == 0) continue;
            var t = AssetCatalog.GetTexture($"map/texture/{slot.Name}");
            if (t == null)
            {
                GD.PrintErr($"[terrain] tile {tile.TileX},{tile.TileY} " +
                    $"slot{i} : texture «{slot.Name}» absente " +
                    "(repli voisin)");
            }
            else
            {
                texs[i] = t;
                if (firstUsed < 0) firstUsed = i;
            }
        }
        // Repli : slot le plus proche ayant une texture (la base
        // slot0 en priorité — teinte la plus représentative du sol).
        for (var i = 0; i < FsmFile.TexSlotCount; i++)
        {
            if (texs[i] != null) continue;
            var slot = tile.GetTexSlot(i);
            if (!slot.Used || slot.Name.Length == 0 || firstUsed < 0)
                continue;
            var alt = texs[0] ?? texs[firstUsed];
            if (alt != null) texs[i] = alt;
        }
        for (var i = 0; i < FsmFile.TexSlotCount; i++)
        {
            var slot = tile.GetTexSlot(i);
            mat.SetShaderParameter($"layer{i}",
                texs[i] ?? AssetCatalog.White);
            var sc = slot.UvScale > 0f ? slot.UvScale : 1f;
            mat.SetShaderParameter($"uv_scale{i}",
                i == 0 ? sc : scale0 * scale0 / sc);
        }
        return mat;
    }

    /// <summary>Eau animée : délégué à <see cref="WaterAnimator"/>
    /// — shader « water.gdshader » cyclant les frames du
    /// « waterani_*.ifl » du slot texture (sampler2DArray + TIME ;
    /// repli défilement UV si l'.ifl est introuvable). UV vertex =
    /// posMonde/64 → uv_scale ~6 pour un motif ~10 u.</summary>
    public static Material BuildWaterMaterial(TerrainTile tile) =>
        WaterAnimator.BuildMaterial(tile);

    /// <summary>Diagnostic splat : <c>--splat-layer=N</c> n'affiche
    /// que la couche N (5 = splatmap brute) — vis_test/debug.</summary>
    public static int DebugLayer { get; } = ParseDebugLayer();

    private static int ParseDebugLayer()
    {
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
            if (a.StartsWith("--splat-layer=") &&
                int.TryParse(a[14..], out var n))
                return n;
        return -1;
    }

    private static ShaderMaterial SplatProto => _splatProto ??=
        new ShaderMaterial { Shader = LoadSplatShader() };

    private static Shader LoadSplatShader() =>
        GD.Load<Shader>("res://shaders/terrain_splat.gdshader");

    /// <summary>Normales par produits croisés de quad (moyenne par
    /// vertex) — fallback si l'attr #2 natif n'est pas unitaire.</summary>
    private static void ComputeNormals(Vector3[] verts,
        Vector3[] normals, int w, int h)
    {
        for (var iy = 0; iy < h; iy++)
        for (var ix = 0; ix < w; ix++)
        {
            var i = iy * w + ix;
            var xm = verts[iy * w + Math.Max(ix - 1, 0)];
            var xp = verts[iy * w + Math.Min(ix + 1, w - 1)];
            var ym = verts[Math.Max(iy - 1, 0) * w + ix];
            var yp = verts[Math.Min(iy + 1, h - 1) * w + ix];
            normals[i] = (xp - xm).Cross(yp - ym).Normalized();
        }
    }
}
