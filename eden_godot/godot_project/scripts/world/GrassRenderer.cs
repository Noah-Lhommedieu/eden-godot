using Godot;

namespace EdenClient.World;

/// <summary>
/// Construit le Node3D « Grass » d'une map : une
/// <see cref="MultiMeshInstance3D"/> par (typeId GRL × cellule 64 u)
/// pour rester frustum-friendly (AABB par cellule).
///
/// Modèle : le nom GRL (« UE704M.nif ») →
/// <c>models/map/model/object/&lt;stem&gt;.glb</c> — les NIF d'herbe
/// convertis sont des quads croisés texturés (alphaMode MASK,
/// ~0.49). On extrait le premier MeshInstance3D <b>visible et
/// texturé</b> du proto (les helpers « ni_kill »/« miscellaneous »
/// masqués par <see cref="ToonMaterializer"/> sont ignorés) et on le
/// réutilise comme mesh d'instancing. Sans GLB → repli quads croisés
/// billboard-like texturés herbe générique.
///
/// Plafond : <see cref="MaxInstances"/> (défaut 5000 — S025 natif en
/// a ~31 000 ; l'échantillonnage par cellule de
/// <see cref="GrassLayout.Plan"/> garde une répartition uniforme).
///
/// Coords : instances en coords monde Eden (Z-up) sous la racine
/// « World » tournée — position absolue, yaw hashé
/// (<see cref="GrassLayout.Yaw"/>), scale = GRS.Scale.
/// </summary>
public static class GrassRenderer
{
    /// <summary>Plafond d'instances d'herbe par map (perf).</summary>
    public static int MaxInstances { get; set; } = 5000;

    /// <summary>Taille de cellule si le fsm n'a pas de terrain
    /// (carte modèle unique — grass improbable mais toléré).</summary>
    private const float FallbackCellSize = 64f;

    /// <summary>Construit le nœud herbe (null si aucune instance —
    /// l'appelant n'ajoute rien).</summary>
    public static Node3D? Build(FsmFile fsm)
    {
        if (fsm.GrassInstances.Count == 0 || fsm.GrassTypes.Count == 0)
            return null;
        var cellSize = fsm.TileWorldSize > 0f
            ? fsm.TileWorldSize : FallbackCellSize;
        var cells = GrassLayout.Plan(fsm, cellSize, MaxInstances);

        var root = new Node3D { Name = "Grass" };
        // Cache des sources de mesh par typeId (GLB ou repli).
        var sources = new Dictionary<uint, GrassSource?>();
        var total = 0;
        var meshes = 0;
        foreach (var cell in cells)
        {
            if (!sources.TryGetValue(cell.TypeId, out var src))
            {
                src = ResolveSource(fsm, cell.TypeId);
                sources[cell.TypeId] = src;
            }
            if (src == null) continue;
            root.AddChild(BuildCell(src, cell));
            total += cell.Instances.Length;
            meshes++;
        }
        GD.Print($"[grass] {total} instances, {meshes} multimeshes, " +
                 $"{sources.Count} types");
        return total > 0 ? root : null;
    }

    // ------------------------------------------------------------------

    /// <summary>Mesh + transform interne + AABB d'un type d'herbe.</summary>
    private sealed class GrassSource
    {
        public required Mesh Mesh;
        /// <summary>Matériau posé en override sur le MeshInstance source (proto natif : le
        /// matériau n'est pas sur le Mesh) ; null = celui du Mesh.</summary>
        public Material? Override;
        /// <summary>Transform du nœud mesh dans le GLB (pré-multiplié
        /// dans chaque instance).</summary>
        public Transform3D Inner;
        /// <summary>Demi-étendue du mesh (pour le CustomAabb).</summary>
        public Vector3 HalfExtent;
    }

    /// <summary>Résout la source d'instancing d'un typeId : GLB
    /// converti si présent, sinon le quad croisé de repli.</summary>
    private static GrassSource? ResolveSource(FsmFile fsm, uint typeId)
    {
        var stem = GrassLayout.ModelStem(fsm, typeId);
        if (stem != null &&
            AssetCatalog.TryGetGlb($"map/model/object/{stem}",
                out var glbPath))
        {
            var proto = AssetCatalog.LoadGlbNode(glbPath);
            var mi = proto == null ? null : FirstVisibleMesh(proto);
            if (mi?.Mesh != null)
            {
                return new GrassSource
                {
                    Mesh = mi.Mesh,
                    Override = mi.GetSurfaceOverrideMaterial(0),
                    Inner = RelTransform(proto!, mi),
                    HalfExtent = mi.Mesh.GetAabb().Size * 0.5f,
                };
            }
        }
        return FallbackSource();
    }

    /// <summary>Premier MeshInstance3D visible avec une texture (les
    /// helpers « ni_kill »/bloqueurs sont Visible=false après
    /// ToonMaterializer — on les saute explicitement).</summary>
    private static MeshInstance3D? FirstVisibleMesh(Node root)
    {
        if (root is MeshInstance3D mi && mi.Visible && mi.Mesh != null &&
            HasAnyTexture(mi))
            return mi;
        foreach (var c in root.GetChildren())
            if (FirstVisibleMesh(c) is { } hit)
                return hit;
        return null;
    }

    /// <summary>Vrai si au moins une surface a une texture albedo
    /// (distingue le quad herbe du plan « ni_kill » d'ambiance).</summary>
    private static bool HasAnyTexture(MeshInstance3D mi)
    {
        for (var i = 0; i < mi.Mesh!.GetSurfaceCount(); i++)
        {
            var m = mi.GetSurfaceOverrideMaterial(i)
                    ?? mi.Mesh.SurfaceGetMaterial(i);
            if (m is StandardMaterial3D sm && sm.AlbedoTexture != null)
                return true;
            if (m is ShaderMaterial) return true;
        }
        return false;
    }

    /// <summary>Transform relatif du nœud mesh depuis la racine du
    /// GLB (hors arbre — accumulation parentale manuelle).</summary>
    private static Transform3D RelTransform(Node root, Node3D node)
    {
        var t = Transform3D.Identity;
        for (Node? n = node; n != root && n != null; n = n.GetParent())
            if (n is Node3D n3) t = n3.Transform * t;
        return t;
    }

    /// <summary>Une MultiMeshInstance3D pour une cellule : transforms
    /// par instance (pos Eden absolue, yaw hashé, échelle) et
    /// CustomAabb de culling.</summary>
    private static MultiMeshInstance3D BuildCell(GrassSource src,
        GrassLayout.Cell cell)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = src.Mesh,
            InstanceCount = cell.Instances.Length,
        };
        var min = new Vector3(float.MaxValue, float.MaxValue,
            float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue,
            float.MinValue);
        for (var i = 0; i < cell.Instances.Length; i++)
        {
            var g = cell.Instances[i];
            var scale = g.Scale <= 0f ? 1f : g.Scale;
            var basis = new Basis(Vector3.Back, GrassLayout.Yaw(i))
                .Scaled(new Vector3(scale, scale, scale));
            var t = new Transform3D(basis,
                new Vector3(g.Position.X, g.Position.Y, g.Position.Z))
                * src.Inner;
            mm.SetInstanceTransform(i, t);
            var p = t.Origin;
            min = new Vector3(Math.Min(min.X, p.X - src.HalfExtent.X),
                Math.Min(min.Y, p.Y - src.HalfExtent.Y),
                Math.Min(min.Z, p.Z - src.HalfExtent.Z * scale));
            max = new Vector3(Math.Max(max.X, p.X + src.HalfExtent.X),
                Math.Max(max.Y, p.Y + src.HalfExtent.Y),
                Math.Max(max.Z, p.Z + src.HalfExtent.Z * scale));
        }
        var mmi = new MultiMeshInstance3D
        {
            Name = $"Grass_{cell.TypeId}_{cell.CellX}_{cell.CellY}",
            Multimesh = mm,
            MaterialOverride = src.Override,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            // AABB de la cellule → culling frustum par Godot.
            CustomAabb = new Aabb(min, max - min),
        };
        return mmi;
    }

    // ------------------------------------------------------------------
    // Repli : quads croisés texturés (si le GLB du type est absent)

    private static GrassSource? _fallback;

    /// <summary>Source de repli partagée : deux quads croisés 1×1 u
    /// ancrés au sol (+Z hauteur en coords Eden), texture herbe
    /// générique en alpha-scissor.</summary>
    private static GrassSource FallbackSource()
    {
        if (_fallback != null) return _fallback;
        var mesh = new ArrayMesh();
        var verts = new Vector3[]
        {
            // quad X-Z (normal +Y), base à z=0, hauteur 1
            new(-0.5f, 0f, 0f), new(0.5f, 0f, 0f),
            new(0.5f, 0f, 1f), new(-0.5f, 0f, 1f),
            // quad Y-Z (normal +X)
            new(0f, -0.5f, 0f), new(0f, 0.5f, 0f),
            new(0f, 0.5f, 1f), new(0f, -0.5f, 1f),
        };
        var uvs = new Vector2[]
        {
            new(0, 1), new(1, 1), new(1, 0), new(0, 0),
            new(0, 1), new(1, 1), new(1, 0), new(0, 0),
        };
        var idx = new int[] { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        var tex = AssetCatalog.GetTexture("effect/texture/grassx01.dds")
                  ?? AssetCatalog.GetTexture("effect/texture/grass_01.dds")
                  ?? AssetCatalog.White;
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = tex,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.45f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.Disabled,
            Roughness = 1f,
        };
        mesh.SurfaceSetMaterial(0, mat);
        _fallback = new GrassSource
        {
            Mesh = mesh,
            Inner = Transform3D.Identity,
            HalfExtent = new Vector3(0.5f, 0.5f, 0.5f),
        };
        return _fallback;
    }
}
