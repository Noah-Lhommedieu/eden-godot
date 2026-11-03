using Godot;

namespace EdenClient.World;

/// <summary>
/// Construit les colliders d'un objet de map instancié à partir de
/// ses meshes « helper » masqués par <see cref="ToonMaterializer"/>
/// (meta <c>collision_helper</c> — nœuds « true wall », « blo »,
/// « Box01 », « floor1 », « UB107 »… présents dans les NIF natifs
/// exactement pour délimiter le joueur).
///
/// Politique — DEUX classes de helpers
/// (<see cref="ToonMaterializer.ClassifyHelper"/>, affinée par la
/// géométrie) :
///   - <see cref="HelperClass.Wall"/> (noms « true wall », « blo »,
///     « knock », « nocamera », « camera ») → <see cref="StaticBody3D"/>
///     sur la layer 1 : bloqueur horizontal du
///     <c>MoveAndCollide</c> joueur (comportement historique) ;
///   - <see cref="HelperClass.Floor"/> (dalles « floor/plane/object/
///     box/cylinder/light », codes « U[A-Z]nn », variantes « -s/-n »,
///     « Editable * » génériques) → body sur la layer
///     <see cref="WalkableGround.FloorLayer"/> : JAMAIS de blocage
///     horizontal (le joueur la traverse) — elle sert de sol marchable
///     via le raycast <see cref="WalkableGround.Sample"/> dans
///     <see cref="MapLoader.TryGetGodotHeight"/> ;
///   - promotion géométrique : un helper « wall » dont l'AABB locale
///     est une DALLE PLATE (cas réel : le pont S002 UB107 porte son
///     sol de marche sous le nom « true wall ») est reclassé floor —
///     avec warning « class=wall? » ; symétriquement un helper floor
///     à l'AABB verticale (palissade « -s ») est logué « class=wall? »
///     mais reste floor (un mur manquant est moins grave qu'un mur
///     invisible géant).
///
/// Shapes : trimesh exacte ≤ <see cref="MaxTris"/> tris ; au-delà —
/// wall : repli <see cref="BoxShape3D"/> AABB + log (un gros mur reste
/// bloquant) ; floor : trimesh FORCÉE (une boîte géante = mur
/// invisible — pont UB107/UB108) + log, skip si la trimesh échoue.
/// Cap par objet <see cref="MaxHelpersPerObject"/> : on garde les plus
/// gros. Les props « non-structure » (herbe/plantes UE7xxM…) sont
/// exclus dans <see cref="ShouldCollide"/>.
///
/// Diag : <c>EDEN_COLLISION_DIAG=1</c> dump chaque helper classé ;
/// <c>--collision-dump=ex,ey[,r]</c> (coords Eden, r=30 défaut)
/// restreint le dump aux objets proches du point — sert à prouver la
/// classification d'une zone (ex. le pont S002 :
/// <c>--collision-dump=295,305,40</c>).
/// </summary>
public static class CollisionBuilder
{
    /// <summary>Triangles max par mesh avant repli AABB-box (walls
    /// uniquement — les floors font la trimesh quoi qu'il arrive).</summary>
    public const int MaxTris = 10_000;
    /// <summary>Colliders max par objet.</summary>
    public const int MaxHelpersPerObject = 8;

    /// <summary>Vrai si l'objet de scène est un candidat « structure »
    /// pour la collision — les modèles herbe/plante (UE7xxM/UE7xxMN)
    /// et les très petits props sont exclus : marcher dans l'herbe ne
    /// doit pas bloquer, et les helpers miniatures sont du bruit.</summary>
    public static bool ShouldCollide(string objectName)
    {
        // UE7xx = végétation (GRL/GRS : « UE704M.nif »…) — jamais de
        // collision, même si un helper traîne dans le GLB.
        if (objectName.StartsWith("UE7", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>Spécification d'un collider : mesh helper, transform
    /// relatif à l'objet et classe physique retenue — partagée avec
    /// <see cref="PropBatcher"/> (même policy par instance).</summary>
    public readonly struct HelperSpec
    {
        public readonly Mesh Mesh;
        public readonly Transform3D Rel;
        public readonly HelperClass Cls;
        public readonly string Name;
        /// <summary>Demi-diagonale AABB (tri du cap).</summary>
        public readonly float HalfDiag;
        /// <summary>AABB local — réutilisé par la promotion
        /// géométrique et le diag.</summary>
        public readonly Aabb Aabb;
        /// <summary>Annotation de classification (« wall », « floor »,
        /// « wall? » ambigu, « floor(flat-wall) » promu).</summary>
        public readonly string Note;
        /// <summary>Classe fixée par le groupe sémantique du NIF
        /// (meta <c>helper_class</c> de NifScene) — collision écrite
        /// par les auteurs : exempte du cap
        /// <see cref="MaxHelpersPerObject"/>.</summary>
        public readonly bool Tagged;
        public HelperSpec(Mesh mesh, Transform3D rel, HelperClass cls,
            string name, Aabb aabb, string note, bool tagged = false)
        {
            Mesh = mesh; Rel = rel; Cls = cls; Name = name;
            Aabb = aabb; Note = note; Tagged = tagged;
            HalfDiag = aabb.Size.Length() * 0.5f;
        }
    }

    /// <summary>Classe effective d'un helper : nom d'abord
    /// (<see cref="ToonMaterializer.ClassifyHelper"/>) puis promotion
    /// géométrique — une dalle « true wall » est un sol (pont UB107) ;
    /// les ambiguïtés restantes sont signalées « class=wall? ».</summary>
    public static HelperClass Classify(MeshInstance3D mi,
        out string note)
    {
        // Meta « helper_class » de NifScene : le groupe sémantique du
        // NIF (« block »/« nocamerablock »/« camerablock »/« floor »/
        // « terrain », cf. rf_game FUN_006ebc70) fixe la classe — pas
        // de devinette nom+géométrie, les AABB locales sont en Z-up.
        if (mi.HasMeta("helper_class"))
        {
            var g = mi.GetMeta("helper_class").AsString();
            var cls = g == "wall" ? HelperClass.Wall
                : g == "both" ? HelperClass.Both
                : HelperClass.Floor;
            note = $"{cls.ToString().ToLowerInvariant()}(nif)";
            return cls;
        }
        var cls2 = ToonMaterializer.ClassifyHelper(mi.Name.ToString());
        var aabb = mi.Mesh!.GetAabb();
        note = cls2 == HelperClass.Wall ? "wall" : "floor";
        var flat = IsFlatSlab(aabb);
        if (cls2 == HelperClass.Wall && flat)
        {
            note = "floor(flat-wall)";
            return HelperClass.Floor;
        }
        if (cls2 == HelperClass.Floor && IsVerticalSlab(aabb))
            note = "wall?";
        return cls2;
    }

    /// <summary>AABB locale horizontale et mince — dalle de marche
    /// (pont, toit bas) : hauteur ≤ 60 % du plus grand côté
    /// horizontal.</summary>
    private static bool IsFlatSlab(Aabb aabb)
    {
        var s = aabb.Size;
        return s.Y <= MathF.Max(s.X, s.Z) * 0.6f;
    }

    /// <summary>AABB locale dominée par la hauteur (>2× les côtés et
    /// >1,6 u) — mur/palissade déguisé en floor.</summary>
    private static bool IsVerticalSlab(Aabb aabb)
    {
        var s = aabb.Size;
        return s.Y > 1.6f &&
               s.Y > MathF.Max(s.X, s.Z) * 2f;
    }

    /// <summary>Ajoute les StaticBody3D de collision sous
    /// <paramref name="objectNode"/> (déjà transformé à sa position
    /// monde) pour chaque mesh helper trouvé dans le sous-arbre.
    /// Retourne le nombre de colliders créés.</summary>
    public static int Apply(Node3D objectNode)
    {
        // Collecte (helper, transform relatif à l'objet) — on compose
        // les transforms locaux jusqu'à objectNode (les helpers sont
        // souvent sous des nœuds intermédiaires du GLB).
        var helpers = new List<HelperSpec>();
        // On commence par les enfants : inclure le transform de
        // objectNode mettrait les colliders à objT² (le corps étant
        // déjà posé sous l'objet).
        foreach (var rootChild in objectNode.GetChildren())
            Collect(rootChild, Transform3D.Identity, helpers);
        if (helpers.Count == 0) return 0;

        // Diag — dump AVANT le cap (montre aussi les helpers perdus).
        MaybeDump(objectNode.Name.ToString(),
            objectNode.Transform.Origin, helpers);

        // Trop de helpers → garder les plus gros (surface estimée par
        // la diagonale de l'AABB — les petits « blo » sous les pieds
        // ne changent rien au ressenti). Les helpers taggés par les
        // groupes sémantiques du NIF (block/floor/terrain) ne sont
        // JAMAIS cappés : c'est la collision écrite par les auteurs —
        // une map modèle unique en porte ~70.
        helpers.Sort((a, b) => b.Tagged.CompareTo(a.Tagged) != 0
            ? b.Tagged.CompareTo(a.Tagged)
            : b.HalfDiag.CompareTo(a.HalfDiag));

        var created = 0;
        var untagged = 0;
        foreach (var spec in helpers)
        {
            if (!spec.Tagged && untagged >= MaxHelpersPerObject) break;
            var shape = BuildShape(spec.Mesh, spec.Name, spec.Cls,
                out var center);
            if (shape == null) continue;
            var body = new StaticBody3D
            {
                Name = $"Col_{spec.Name}",
                Transform = spec.Rel,
                CollisionLayer = spec.Cls == HelperClass.Wall
                    ? WalkableGround.WallLayer
                    : spec.Cls == HelperClass.Both
                        ? WalkableGround.WallLayer | WalkableGround.ShellLayer
                        : WalkableGround.FloorLayer,
                // Corps statique : il ne sonde rien.
                CollisionMask = 0,
            };
            // center = décalage AABB pour la box de repli (0 pour les
            // trimesh — géométrie exacte déjà en place).
            body.AddChild(new CollisionShape3D
            {
                Shape = shape, Position = center,
            });
            objectNode.AddChild(body);
            if (!spec.Tagged) untagged++;
            created++;
        }
        return created;
    }

    /// <summary>Shape de collision pour un helper : trimesh exacte
    /// ≤ <see cref="MaxTris"/> ; au-delà, un WALL retombe sur la
    /// BoxShape3D de l'AABB (<paramref name="center"/> = centre AABB)
    /// avec log, tandis qu'un FLOOR fait la trimesh quoi qu'il arrive
    /// (une boîte englobante géante serait un mur invisible — le pont
    /// S002 dépassait le seuil) ou est skipée avec warning.</summary>
    internal static Shape3D? BuildShape(Mesh mesh, string name,
        HelperClass cls, out Vector3 center)
    {
        center = Vector3.Zero;
        var tris = CountTris(mesh);
        if (tris <= MaxTris)
        {
            var shape = mesh.CreateTrimeshShape();
            if (shape != null) return shape;
        }
        // Floor ET Both : jamais de boîte de repli — une AABB solide
        // d'une coquille Roomblock engloberait le joueur (trimesh
        // forcée, ou collider skippé avec warning).
        if (cls != HelperClass.Wall)
        {
            var forced = mesh.CreateTrimeshShape();
            if (forced != null)
            {
                WarnOnce($"floor-big:{name}",
                    $"[collision] floor '{name}' {tris} tris > " +
                    $"{MaxTris} → trimesh forcée (pas de boîte " +
                    "invisible)");
                return forced;
            }
            WarnOnce($"floor-null:{name}",
                $"[collision] floor '{name}' {tris} tris — trimesh " +
                "impossible, collider SKIPPÉ");
            return null;
        }
        if (tris > MaxTris)
            WarnOnce($"wall-box:{name}",
                $"[collision] wall '{name}' {tris} tris > {MaxTris} " +
                "→ box AABB (approximation)");
        // Repli : boîte de l'AABB (approximation documentée, bornée
        // par MaxTris — ou trimesh impossible sur un mur).
        var aabb = mesh.GetAabb();
        if (aabb.Size.LengthSquared() < 1e-6f) return null;
        center = aabb.GetCenter();
        return new BoxShape3D { Size = aabb.Size };
    }

    /// <summary>Triangles du mesh, toutes surfaces confondues.</summary>
    internal static int CountTris(Mesh mesh)
    {
        var tris = 0;
        for (var s = 0; s < mesh.GetSurfaceCount(); s++)
        {
            var arrays = mesh.SurfaceGetArrays(s);
            var idx = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            tris += idx.Length > 0
                ? idx.Length / 3
                : arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array()
                    .Length / 3;
        }
        return tris;
    }

    // ------------------------------------------------------------------

    /// <summary>Collecte récursive des meshes marqués
    /// <c>collision_helper</c> (ou fils de tels nœuds — le helper peut
    /// être un Node3D porteur), classés par
    /// <see cref="Classify"/>.</summary>
    private static void Collect(Node node, Transform3D parentRel,
        List<HelperSpec> dest)
    {
        var rel = parentRel;
        if (node is Node3D n3) rel = parentRel * n3.Transform;
        if (node is MeshInstance3D mi && mi.Mesh != null &&
            !mi.HasMeta("no_collision") &&
            (mi.HasMeta("collision_helper") ||
             ToonMaterializer.IsHelperByName(mi.Name.ToString())))
        {
            dest.Add(MakeSpec(mi, rel));
        }
        foreach (var child in node.GetChildren())
            Collect(child, rel, dest);
    }

    /// <summary>Spec d'un mesh helper (classification nom+géométrie +
    /// warnings « class=wall? » une fois par nom) — point d'entrée
    /// commun avec <see cref="PropBatcher"/>.</summary>
    internal static HelperSpec MakeSpec(MeshInstance3D mi,
        Transform3D rel)
    {
        var cls = Classify(mi, out var note);
        var aabb = mi.Mesh!.GetAabb();
        if (note == "wall?")
            WarnOnce($"ambigu:{mi.Name}",
                $"[collision] class=wall? '{mi.Name}' classé floor " +
                $"mais AABB verticale {aabb.Size} (rampe/mur " +
                "ambigu — floor privilégié)");
        else if (note == "floor(flat-wall)")
            WarnOnce($"flat:{mi.Name}",
                $"[collision] class=wall? '{mi.Name}' nommé wall " +
                $"mais dalle plate {aabb.Size} → floor (pont " +
                "marchable)");
        return new HelperSpec(mi.Mesh, rel, cls,
            mi.Name.ToString(), aabb, note,
            tagged: mi.HasMeta("helper_class"));
    }

    // ------------------------------------------------------------------
    // Diag : EDEN_COLLISION_DIAG=1 (tout) et/ou
    // --collision-dump=ex,ey[,r] (autour d'un point Eden).

    private static bool _diagParsed;
    private static bool _diagAll;
    private static bool _dumpOn;
    private static Vector2 _dumpPos;
    private static float _dumpR = 30f;

    private static void ParseDiagArgs()
    {
        if (_diagParsed) return;
        _diagParsed = true;
        _diagAll = System.Environment.GetEnvironmentVariable(
            "EDEN_COLLISION_DIAG") == "1";
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var a in OS.GetCmdlineArgs()
                     .Concat(OS.GetCmdlineUserArgs()))
        {
            if (!a.StartsWith("--collision-dump=",
                    StringComparison.Ordinal)) continue;
            var p = a["--collision-dump=".Length..].Split(',');
            if (p.Length < 2 ||
                !float.TryParse(p[0], System.Globalization
                        .NumberStyles.Float, ci, out var dx) ||
                !float.TryParse(p[1], System.Globalization
                        .NumberStyles.Float, ci, out var dy))
            {
                GD.PrintErr("[collision] --collision-dump=ex,ey[,r] " +
                            $"attendu, reçu « {a} »");
                continue;
            }
            _dumpPos = new Vector2(dx, dy);
            if (p.Length >= 3 &&
                float.TryParse(p[2], System.Globalization
                        .NumberStyles.Float, ci, out var r) && r > 0f)
                _dumpR = r;
            _dumpOn = true;
        }
        if (_diagAll || _dumpOn)
            GD.Print("[collision] diag actif" +
                (_dumpOn ? $" — dump ≤{_dumpR:F0} u de " +
                    $"({_dumpPos.X:F0},{_dumpPos.Y:F0})" : "") +
                (_diagAll ? " — EDEN_COLLISION_DIAG=1 (tout)" : ""));
    }

    /// <summary>Vrai si l'objet à la position Eden
    /// <paramref name="edenPos"/> doit être dumpé (flag global ou
    /// dans le rayon du --collision-dump).</summary>
    public static bool DumpMatches(Vector3 edenPos)
    {
        ParseDiagArgs();
        if (_diagAll) return true;
        return _dumpOn &&
               new Vector2(edenPos.X - _dumpPos.X,
                   edenPos.Y - _dumpPos.Y).Length() <= _dumpR;
    }

    /// <summary>Dump « nom / classe / tris » des helpers d'un objet —
    /// appelé par <see cref="Apply"/> (chemin instancié) et par
    /// <see cref="PropBatcher"/> (chemin batché).</summary>
    public static void MaybeDump(string owner, Vector3 edenPos,
        IEnumerable<HelperSpec> helpers)
    {
        if (!DumpMatches(edenPos)) return;
        foreach (var h in helpers)
        {
            var layer = h.Cls == HelperClass.Wall
                ? WalkableGround.WallLayer
                : h.Cls == HelperClass.Both
                    ? WalkableGround.WallLayer | WalkableGround.ShellLayer
                    : WalkableGround.FloorLayer;
            GD.Print($"[collision] {owner} helper '{h.Name}' " +
                     $"class={h.Cls} tris={CountTris(h.Mesh)} " +
                     $"layer={layer} aabb={h.Aabb.Size} " +
                     $"note={h.Note} " +
                     $"@({edenPos.X:F1},{edenPos.Y:F1})");
        }
    }

    /// <summary>Warning unique par clé (les modèles se répètent —
    /// un seul log par cas). Verrou : le chargement map peut tourner
    /// en tâche de fond.</summary>
    private static readonly HashSet<string> _warned = new();

    private static void WarnOnce(string key, string message)
    {
        lock (_warned)
        {
            if (!_warned.Add(key)) return;
        }
        GD.PrintErr(message);
    }
}
