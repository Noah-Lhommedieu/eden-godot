using Godot;

namespace EdenClient.World;

/// <summary>
/// Regroupe les props statiques de map (<c>map/model/object/*</c>) en
/// <see cref="MultiMeshInstance3D"/> par (modèle GLB × cellule 64 u ×
/// nœud-mesh du proto) — même logique que <see cref="GrassRenderer"/>
/// mais pour les objets du chunk OBJ du .fsm.
///
/// Pourquoi : S001 instancie ~3 769 objets = ~10 k MeshInstance3D →
/// ~2 300 draw calls/frame. La majorité sont des props statiques
/// répétés (lampadaires, pots, barriques) dont le seul changement
/// est le Transform3D — cas d'école du MultiMesh.
///
/// Éligibilité (scannée UNE FOIS sur le proto GLB partagé — lecture
/// seule, jamais modifié). Un modèle est NON batchable si :
///   - il contient un <see cref="AnimationPlayer"/>,
///     <see cref="Skeleton3D"/>, <see cref="GpuParticles3D"/>,
///     <see cref="Light3D"/> ou <see cref="Camera3D"/> (props animés
///     / émetteurs — un MultiMesh est figé) ;
///   - un mesh a un matériau alpha <b>blend</b>
///     (<c>StandardMaterial3D.Transparency != Disabled</c>) : le tri
///     transparence par instance est incorrect sous MultiMesh ;
///     l'alpha-scissor (MASK — feuillage) reste éligible ;
///   - un mesh a plusieurs surfaces avec des matériaux effectifs
///     différents (<see cref="MultiMeshInstance3D"/> n'a qu'un
///     <c>MaterialOverride</c> global — nos GLB émettent 1 prim /
///     nœud donc cas rare).
///
/// Colliders PRÉSERVÉS : les meshes « helper » (meta
/// <c>collision_helper</c> posée par <see cref="ToonMaterializer"/>,
/// ou nom reconnu par <see cref="ToonMaterializer.IsHelperByName"/> —
/// même prédicat que <see cref="CollisionBuilder"/>) ne
/// disqualifient PAS le modèle : chaque instance reçoit à la place un
/// ou deux <see cref="StaticBody3D"/> porteurs — un body layer
/// <see cref="WalkableGround.WallLayer"/> pour les helpers classés
/// wall, un body layer <see cref="WalkableGround.FloorLayer"/> pour
/// les sols marchables (même classification nom+géométrie, mêmes
/// seuils MaxTris/MaxHelpersPerObject et même policy de repli que
/// <see cref="CollisionBuilder"/> — shapes partagées par
/// (mesh, classe)). Les helpers invisibles ne sont pas rendus (comme
/// avant), les helpers « nom seul » restent rendus via le MultiMesh —
/// parité stricte.
///
/// Préservé aussi : <see cref="Transform3D"/> complet par instance,
/// <see cref="GeometryInstance3D.VisibilityRangeEnd"/> (recopié du
/// mesh source — le culling distance s'applique à la cellule entière
/// via son <c>CustomAabb</c>, approximation 64 u), ombres off pour
/// les petits props (politique <c>MapLoader.DisableSmallShadows</c>,
/// rayon &lt; 1,5 u).
///
/// Flag : <c>--props-batch</c> / <c>--props-batch=off</c>
/// (<c>--no-props-batch</c>) — parsé ici même, indépendant de
/// MapLoader.
/// </summary>
public sealed class PropBatcher
{
    /// <summary>Côté de cellule de regroupement (unités Eden).</summary>
    public const float CellSize = 64f;

    private static bool _argsParsed;
    private static bool _enabled = true;

    /// <summary>Batching actif — défaut on, override CLI
    /// (<c>--props-batch</c>, <c>--props-batch=off</c>,
    /// <c>--no-props-batch</c>).</summary>
    public static bool Enabled
    {
        get
        {
            if (_argsParsed) return _enabled;
            _argsParsed = true;
            foreach (var a in OS.GetCmdlineArgs()
                         .Concat(OS.GetCmdlineUserArgs()))
            {
                if (a == "--props-batch" || a == "--props-batch=on")
                    _enabled = true;
                else if (a == "--props-batch=off" ||
                         a == "--no-props-batch")
                    _enabled = false;
            }
            return _enabled;
        }
        set => _enabled = value;
    }

    /// <summary>Stats du dernier <see cref="Flush"/> (log/perf).</summary>
    public int Batched { get; private set; }
    public int MultiMeshes { get; private set; }
    public int Models { get; private set; }
    public int ColliderBodies { get; private set; }

    // Diagnostic : motifs de rejet du batching par modèle unique —
    // sert à décider où investir (ex. alpha-blend → scissor rend
    // batchable).
    private readonly Dictionary<string, int> _rejectReasons = new();

    // Instances rejetées par motif (pondéré — un modèle ×500
    // instances pèse plus qu'un modèle ×1).
    private readonly Dictionary<string, int> _rejectInstances = new();

    private void Reject(string reason) =>
        _rejectReasons[reason] = _rejectReasons
            .GetValueOrDefault(reason) + 1;

    /// <summary>Ligne récap des rejets (appelée après Flush).</summary>
    public string RejectSummary() =>
        _rejectReasons.Count == 0 ? "" :
        " — rejets: " + string.Join(", ", _rejectInstances
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key}×{kv.Value}obj")) + " (" +
        string.Join(", ", _rejectReasons
            .OrderByDescending(kv => kv.Value)
            .Select(kv => $"{kv.Key}×{kv.Value}mod")) + ")";

    // ------------------------------------------------------------------

    /// <summary>Un nœud-mesh éligible du proto : mesh partagé,
    /// transform relatif à la racine GLB, matériau d'override éventuel,
    /// portée de visibilité et demi-diagonale pour l'AABB/ombres.</summary>
    private sealed class Part
    {
        public required Mesh Mesh;
        public Transform3D Rel;
        public Material? Override;
        public float VisRange;
        public float VisMargin;
        public float HalfDiag;
    }

    /// <summary>Modèle résolu : parties visuelles + specs de colliders
    /// (<see cref="CollisionBuilder.HelperSpec"/> — mesh, transform
    /// relatif, classe wall/floor, AABB ; null = modèle non batchable
    /// → chemin instancié classique).</summary>
    private sealed class ModelInfo
    {
        public required List<Part> Parts;
        public required List<CollisionBuilder.HelperSpec> Colliders;
    }

    /// <summary>Entrée d'instance dans une cellule.</summary>
    private readonly struct Entry
    {
        public readonly Transform3D T;
        public readonly bool Collide;
        public Entry(Transform3D t, bool collide) { T = t; Collide = collide; }
    }

    private readonly Dictionary<string, ModelInfo?> _models = new();
    // Motif de rejet par modèle (pour la pondération par instances).
    private readonly Dictionary<string, string> _modelReasons = new();
    private readonly Dictionary<(string Path, int Cx, int Cy),
        List<Entry>> _groups = new();
    // Shapes partagées par (mesh, classe) — la classe change la
    // policy de repli (floor >MaxTris = trimesh forcée, jamais de
    // boîte géante ; wall >MaxTris = box AABB + log).
    private readonly Dictionary<(Mesh, HelperClass), Shape3D?>
        _shapeCache = new();
    private readonly Dictionary<(Mesh, HelperClass), Vector3>
        _shapeCenters = new();

    /// <summary>Classe l'objet de scène pour un batch — true si le
    /// modèle est éligible. L'appelant ne crée alors AUCUN nœud propre
    /// à l'instance (visuels → MultiMesh, colliders → StaticBody3D
    /// porteur émis au <see cref="Flush"/>).</summary>
    public bool TryCollect(string objectName, Transform3D objTransform)
    {
        var rel = $"map/model/object/{objectName}";
        if (!AssetCatalog.TryGetGlb(rel, out var glbPath))
            return false;
        if (!_models.TryGetValue(glbPath, out var model))
        {
            model = ResolveModel(glbPath);
            _models[glbPath] = model;
        }
        if (model == null ||
            (model.Parts.Count == 0 && model.Colliders.Count == 0))
        {
            var r = _modelReasons.GetValueOrDefault(glbPath, "?");
            _rejectInstances[r] = _rejectInstances
                .GetValueOrDefault(r) + 1;
            return false;
        }
        var key = (glbPath,
            (int)MathF.Floor(objTransform.Origin.X / CellSize),
            (int)MathF.Floor(objTransform.Origin.Y / CellSize));
        if (!_groups.TryGetValue(key, out var list))
            _groups[key] = list = new List<Entry>();
        list.Add(new Entry(objTransform,
            model.Colliders.Count > 0 &&
            CollisionBuilder.ShouldCollide(objectName)));
        return true;
    }

    /// <summary>Émet les MultiMeshInstance3D + StaticBody3D porteurs
    /// sous <paramref name="parent"/> (la racine « Objects », coords
    /// Eden — les transforms sont locaux au parent).</summary>
    public void Flush(Node3D parent)
    {
        foreach (var (key, entries) in _groups)
        {
            var model = _models[key.Path];
            if (model == null) continue;
            for (var pi = 0; pi < model.Parts.Count; pi++)
                parent.AddChild(
                    BuildMultiMesh(key, pi, model.Parts[pi], entries));
            MultiMeshes += model.Parts.Count;
            if (model.Colliders.Count > 0)
                ColliderBodies += BuildColliders(parent, key,
                    model.Colliders, entries);
            Batched += entries.Count;
        }
        Models = _models.Count;
        _groups.Clear();
    }

    // ------------------------------------------------------------------
    // Éligibilité : scan du proto partagé (lecture seule)

    /// <summary>Parties visuelles + colliders du proto ; null si le
    /// modèle ne peut pas être batché (animé, alpha-blend,
    /// multi-matériaux).</summary>
    private string _reject = "?";

    private ModelInfo? ResolveModel(string glbPath)
    {
        var proto = AssetCatalog.GetProto(glbPath);
        if (proto == null)
        {
            Reject("noproto");
            _modelReasons[glbPath] = "noproto";
            return null;
        }
        var parts = new List<Part>();
        var colliders = new List<CollisionBuilder.HelperSpec>();
        _reject = "?";
        if (!Collect(proto, proto, Transform3D.Identity, parts,
                colliders))
        {
            Reject(_reject);
            _modelReasons[glbPath] = _reject;
            return null;
        }
        // Cap MaxHelpersPerObject — mêmes critère/ordre que
        // CollisionBuilder.Apply (les plus gros helpers gardés).
        if (colliders.Count > CollisionBuilder.MaxHelpersPerObject)
        {
            colliders.Sort((a, b) =>
                b.HalfDiag.CompareTo(a.HalfDiag));
            colliders.RemoveRange(CollisionBuilder.MaxHelpersPerObject,
                colliders.Count - CollisionBuilder.MaxHelpersPerObject);
        }
        return new ModelInfo { Parts = parts, Colliders = colliders };
    }

    /// <summary>Accumule récursivement mesh éligibles + specs de
    /// colliders ; retourne false dès qu'un nœud rend le modèle non
    /// batchable.</summary>
    private bool Collect(Node root, Node node, Transform3D parentRel,
        List<Part> parts, List<CollisionBuilder.HelperSpec> colliders)
    {
        var rel = parentRel;
        if (node is Node3D n3 && !ReferenceEquals(node, root))
            rel = parentRel * n3.Transform;
        switch (node)
        {
            case AnimationPlayer or Skeleton3D or GpuParticles3D
                or CpuParticles3D or Light3D or Camera3D:
                _reject = "anime";
                return false;   // animé / émetteur / lumière → figé impossible
            case MeshInstance3D mi when mi.Mesh != null:
            {
                var isColliderMesh =
                    !mi.HasMeta("no_collision") &&
                    (mi.HasMeta("collision_helper") ||
                     ToonMaterializer.IsHelperByName(mi.Name.ToString()));
                if (isColliderMesh)
                    colliders.Add(CollisionBuilder.MakeSpec(mi, rel));
                // Helper invisible (meta ToonMaterializer) : jamais
                // rendu — son collider suffit.
                if (!mi.Visible) return true;
                var mat = UniformMaterial(mi);
                if (mat == s_invalidMaterial)
                {
                    _reject = "multimat";
                    return false;
                }
                // Vrai blend / depth-pre-pass : tri par instance
                // incorrect sous MultiMesh. AlphaHash/scissor
                // (dither/discard — feuillage) tolérés : pas de tri
                // nécessaire.
                if (mat is StandardMaterial3D sm &&
                    sm.Transparency is BaseMaterial3D.TransparencyEnum
                        .Alpha or BaseMaterial3D.TransparencyEnum
                        .AlphaDepthPrePass)
                {
                    _reject = "alphablend";
                    return false;
                }
                var isOverride = mat != null && HasOverride(mi);
                parts.Add(new Part
                {
                    Mesh = mi.Mesh,
                    Rel = rel,
                    Override = isOverride ? mat : null,
                    VisRange = mi.VisibilityRangeEnd,
                    VisMargin = mi.VisibilityRangeEndMargin,
                    HalfDiag = mi.Mesh.GetAabb().Size.Length() * 0.5f,
                });
                return true;
            }
            case MeshInstance3D:      // mesh null — inerte
            case Node3D:
                break;
            default:
                _reject = node.GetType().Name;
                return false;         // type de nœud inconnu → prudent
        }
        foreach (var child in node.GetChildren())
            if (!Collect(root, child, rel, parts, colliders))
                return false;
        return true;
    }

    private static readonly Material s_invalidMaterial =
        new StandardMaterial3D();

    /// <summary>Matériau si UNIFORME sur toutes les surfaces du mesh
    /// (override &gt; matériau mesh), <see cref="s_invalidMaterial"/>
    /// si les surfaces diffèrent, null si aucune n'a de matériau.</summary>
    private static Material? UniformMaterial(MeshInstance3D mi)
    {
        Material? first = s_invalidMaterial;
        for (var i = 0; i < mi.Mesh!.GetSurfaceCount(); i++)
        {
            var m = mi.GetSurfaceOverrideMaterial(i)
                    ?? mi.Mesh.SurfaceGetMaterial(i);
            if (ReferenceEquals(first, s_invalidMaterial))
                first = m;
            else if (!ReferenceEquals(first, m))
                return s_invalidMaterial;
        }
        return ReferenceEquals(first, s_invalidMaterial) ? null : first;
    }

    /// <summary>Vrai si une surface au moins porte un override
    /// (sinon le matériau vient du mesh partagé — pas besoin de
    /// <c>MaterialOverride</c>).</summary>
    private static bool HasOverride(MeshInstance3D mi)
    {
        for (var i = 0; i < mi.Mesh!.GetSurfaceCount(); i++)
            if (mi.GetSurfaceOverrideMaterial(i) != null)
                return true;
        return false;
    }

    // ------------------------------------------------------------------
    // Colliders — même policy que CollisionBuilder.BuildShape (trimesh
    // ≤ MaxTris ; floor >MaxTris = trimesh forcée jamais de boîte
    // géante ; wall >MaxTris = box AABB + log). Les shapes sont
    // partagées entre toutes les instances du modèle (resource
    // immutable après création).

    private Shape3D? ColliderShape(Mesh mesh, HelperClass cls,
        string name, out Vector3 center)
    {
        var key = (mesh, cls);
        if (_shapeCache.TryGetValue(key, out var cached))
        {
            center = cached == null ? Vector3.Zero
                : _shapeCenters.GetValueOrDefault(key);
            return cached;
        }
        var shape = CollisionBuilder.BuildShape(mesh, name, cls,
            out center);
        _shapeCache[key] = shape;
        _shapeCenters[key] = center;
        return shape;
    }

    /// <summary>StaticBody3D porteurs par instance « collidable » du
    /// groupe : un body layer wall + un body layer floor selon les
    /// classes des helpers (transform relatif + offset box) —
    /// équivalent exact de <see cref="CollisionBuilder.Apply"/> en
    /// bodies séparés par layer.</summary>
    private int BuildColliders(Node3D parent,
        (string Path, int Cx, int Cy) key,
        List<CollisionBuilder.HelperSpec> specs, List<Entry> entries)
    {
        var created = 0;
        var n = 0;
        foreach (var e in entries)
        {
            if (!e.Collide) continue;
            CollisionBuilder.MaybeDump(key.Path, e.T.Origin, specs);
            var wall = new StaticBody3D
            {
                Name = $"ColW_{key.Cx}_{key.Cy}_{n}",
                Transform = e.T,
                CollisionLayer = WalkableGround.WallLayer,
                CollisionMask = 0,
            };
            var floor = new StaticBody3D
            {
                Name = $"ColF_{key.Cx}_{key.Cy}_{n}",
                Transform = e.T,
                CollisionLayer = WalkableGround.FloorLayer,
                CollisionMask = 0,
            };
            // Les coquilles « Both » portent via la layer Shell
            // dédiée — le dessous extérieur d'une enveloppe de salle
            // ne doit jamais être un sol marchable au même titre
            // qu'un « floor »/« terrain ».
            var shell = new StaticBody3D
            {
                Name = $"ColS_{key.Cx}_{key.Cy}_{n}",
                Transform = e.T,
                CollisionLayer = WalkableGround.ShellLayer,
                CollisionMask = 0,
            };
            n++;
            foreach (var spec in specs)
            {
                var shape = ColliderShape(spec.Mesh, spec.Cls,
                    spec.Name, out var center);
                if (shape == null) continue;
                // Both = coquille de blocage (groupe NIF « block ») :
                // les faces bloquent (body wall) ET portent (body
                // shell) — shape partagée.
                if (spec.Cls != HelperClass.Floor)
                    wall.AddChild(new CollisionShape3D
                    {
                        Shape = shape,
                        Transform = spec.Rel *
                            new Transform3D(Basis.Identity, center),
                    });
                if (spec.Cls == HelperClass.Floor)
                    floor.AddChild(new CollisionShape3D
                    {
                        Shape = shape,
                        Transform = spec.Rel *
                            new Transform3D(Basis.Identity, center),
                    });
                else if (spec.Cls == HelperClass.Both)
                    shell.AddChild(new CollisionShape3D
                    {
                        Shape = shape,
                        Transform = spec.Rel *
                            new Transform3D(Basis.Identity, center),
                    });
            }
            if (wall.GetChildCount() > 0)
            {
                parent.AddChild(wall);
                created++;
            }
            if (floor.GetChildCount() > 0)
            {
                parent.AddChild(floor);
                created++;
            }
            if (shell.GetChildCount() > 0)
            {
                parent.AddChild(shell);
                created++;
            }
        }
        return created;
    }

    // ------------------------------------------------------------------
    // MultiMesh

    /// <summary>Une MultiMeshInstance3D pour (modèle × cellule ×
    /// nœud-mesh) : transforms pré-multipliés par la pose interne du
    /// nœud dans le GLB, AABB conservative (sphère englobante × scale
    /// max), culling distance recopié, ombres off si petit.</summary>
    private static MultiMeshInstance3D BuildMultiMesh(
        (string Path, int Cx, int Cy) key, int partIdx, Part part,
        List<Entry> entries)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = part.Mesh,
            InstanceCount = entries.Count,
        };
        var min = new Vector3(float.MaxValue, float.MaxValue,
            float.MaxValue);
        var max = new Vector3(float.MinValue, float.MinValue,
            float.MinValue);
        var maxScale = 0f;
        for (var i = 0; i < entries.Count; i++)
        {
            var t = entries[i].T * part.Rel;
            mm.SetInstanceTransform(i, t);
            var s = t.Basis.Scale;
            var sc = MathF.Max(MathF.Abs(s.X),
                MathF.Max(MathF.Abs(s.Y), MathF.Abs(s.Z)));
            maxScale = MathF.Max(maxScale, sc);
            var r = part.HalfDiag * sc;
            var p = t.Origin;
            min = new Vector3(Math.Min(min.X, p.X - r),
                Math.Min(min.Y, p.Y - r), Math.Min(min.Z, p.Z - r));
            max = new Vector3(Math.Max(max.X, p.X + r),
                Math.Max(max.Y, p.Y + r), Math.Max(max.Z, p.Z + r));
        }
        var mmi = new MultiMeshInstance3D
        {
            Name = $"Props_{partIdx}_{key.Cx}_{key.Cy}",
            Multimesh = mm,
            // AABB de la cellule → culling frustum + distance par Godot.
            CustomAabb = new Aabb(min, max - min),
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
        if (part.Override != null)
            mmi.MaterialOverride = part.Override;
        if (part.VisRange > 0f)
        {
            mmi.VisibilityRangeEnd = part.VisRange;
            mmi.VisibilityRangeEndMargin = part.VisMargin;
            mmi.VisibilityRangeFadeMode =
                GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
        }
        // Ombres : même politique que MapLoader.DisableSmallShadows —
        // rayon < 1,5 u post-scale → caster inutile (4 splits sun).
        if (part.HalfDiag * maxScale < 1.5f)
            mmi.CastShadow =
                GeometryInstance3D.ShadowCastingSetting.Off;
        return mmi;
    }
}
