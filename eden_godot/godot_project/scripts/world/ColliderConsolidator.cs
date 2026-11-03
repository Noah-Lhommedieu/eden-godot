using Godot;

namespace EdenClient.World;

/// <summary>
/// Consolidation des colliders statiques d'un sous-arbre : les
/// ~4 k <see cref="StaticBody3D"/> + ~6 k <see cref="CollisionShape3D"/>
/// individuels des objets/props (un body wall + un body floor par
/// prop batché, un body par helper pour le reste) sont fusionnés en
/// DEUX bodies partagés — un par layer physique (wall bloqueur /
/// floor marchable) — portant chacun UNE
/// <see cref="ConcavePolygonShape3D"/> fusionnée.
///
/// Pourquoi : chaque nœud collision coûte ~0,6 µs/frame côté moteur
/// (mesuré --perf : S001 21 ms → 15 ms sans colliders ; 21 → 17,8 ms
/// après consolidation bodies seule). La sémantique physique est
/// identique — un corps statique portant une trimesh niveau est le
/// pattern standard (BVH interne de la shape), les layers sont
/// préservées (WallLayer / FloorLayer).
///
/// Shapes triangulables : <see cref="ConcavePolygonShape3D"/> (toutes
/// les trimesh de CollisionBuilder) et <see cref="BoxShape3D"/>
/// (repli AABB) → faces fusionnées ; les autres shapes (convex,
/// capsule…) sont reparentées telles quelles sous le body partagé.
/// Ne touche QUE les StaticBody3D — Area3D (picking, zones) et
/// colliders d'entités restent hors périmètre (appeler sur Objects,
/// pas Entities).
/// </summary>
public static class ColliderConsolidator
{
    /// <summary>Fusionne les colliders statiques sous
    /// <paramref name="container"/> en deux bodies (wall/floor).
    /// Retourne (bodies supprimés, shapes fusionnées).</summary>
    public static (int Bodies, int Shapes) Consolidate(Node3D container)
    {
        var wall = new List<Vector3>();
        var floor = new List<Vector3>();
        var exotic = new List<(CollisionShape3D Shape,
            Transform3D Rel, uint Layer)>();
        var bodies = new List<StaticBody3D>();
        var merged = 0;
        var stack = new Stack<Node>();
        stack.Push(container);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is StaticBody3D b)
            {
                foreach (var c in b.GetChildren())
                {
                    if (c is not CollisionShape3D cs ||
                        cs.Shape == null) continue;
                    // GlobalTransform est interdit hors-arbre (map
                    // construite sur thread worker) : la chaîne de
                    // parents donne le même rel sans erreur.
                    var rel = Relative(b, container) * cs.Transform;
                    var faces = Faces(cs.Shape, rel);
                    if (faces != null)
                    {
                        (b.CollisionLayer == WalkableGround.WallLayer
                            ? wall : floor).AddRange(faces);
                        merged++;
                        // GetChildren() est un snapshot — retrait
                        // immédiat sûr ; la shape fusionnée disparaît
                        // avec le corps.
                        b.RemoveChild(cs);
                        cs.Free();
                    }
                    else
                    {
                        exotic.Add((cs, rel, b.CollisionLayer));
                    }
                }
                bodies.Add(b);
                continue;
            }
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        AddBody(container, "ColW_shared",
            WalkableGround.WallLayer, wall, exotic);
        AddBody(container, "ColF_shared",
            WalkableGround.FloorLayer, floor, exotic);
        var removed = 0;
        foreach (var b in bodies)
        {
            // Corps vidé de ses shapes → supprimé ; un corps porteur
            // d'enfants non-shape (rare) est laissé en place.
            if (b.GetChildCount() > 0) continue;
            b.GetParent()?.RemoveChild(b);
            b.Free();
            removed++;
        }
        return (removed, merged);
    }

    /// <summary>Crée le body partagé d'une layer : trimesh fusionnée
    /// + shapes exotiques reparentées ; rien si vide.</summary>
    private static void AddBody(Node3D container, string name,
        uint layer, List<Vector3> faces,
        List<(CollisionShape3D Shape, Transform3D Rel, uint Layer)>
            exotic)
    {
        var body = new StaticBody3D
        {
            Name = name,
            CollisionLayer = layer,
            CollisionMask = 0,
        };
        if (faces.Count > 0)
        {
            var shape = new ConcavePolygonShape3D();
            shape.SetFaces(faces.ToArray());
            body.AddChild(new CollisionShape3D { Shape = shape });
        }
        var own = 0;
        foreach (var (cs, rel, l) in exotic)
        {
            if (l != layer) continue;
            cs.GetParent().RemoveChild(cs);
            body.AddChild(cs);
            cs.Transform = rel;
            own++;
        }
        if (body.GetChildCount() > 0) container.AddChild(body);
    }

    /// <summary>Transform de <paramref name="node"/> relatif à
    /// <paramref name="container"/> en composant les Transform
    /// locaux de la chaîne de parents — sûr hors-arbre.</summary>
    private static Transform3D Relative(Node3D node, Node3D container)
    {
        var t = Transform3D.Identity;
        for (Node? n = node; n != null && n != container;
             n = n.GetParent())
            if (n is Node3D n3)
                t = n3.Transform * t;
        return t;
    }

    /// <summary>Faces espace-conteneur d'une shape triangulable ;
    /// null pour les types exotiques (convex/capsule/sphere…).</summary>
    private static Vector3[]? Faces(Shape3D shape, Transform3D rel)
    {
        switch (shape)
        {
            case ConcavePolygonShape3D cps:
                var f = cps.GetFaces();
                for (var i = 0; i < f.Length; i++) f[i] = rel * f[i];
                return f;
            case BoxShape3D box:
                return BoxFaces(box.Size, rel);
            default:
                return null;
        }
    }

    /// <summary>12 triangles d'une boîte <paramref name="size"/>
    /// transformée — utilisé pour les repli-AABB des gros murs.</summary>
    private static Vector3[] BoxFaces(Vector3 size, Transform3D rel)
    {
        var (hx, hy, hz) = (size.X * 0.5f, size.Y * 0.5f,
            size.Z * 0.5f);
        Vector3[] c =
        {
            new(-hx, -hy, -hz), new(hx, -hy, -hz),
            new(hx, -hy, hz), new(-hx, -hy, hz),
            new(-hx, hy, -hz), new(hx, hy, -hz),
            new(hx, hy, hz), new(-hx, hy, hz),
        };
        int[] t =
        {
            // Faces CCW vues de l'extérieur (concave : le winding
            // décide de la face qui bloque, backface=false).
            0, 1, 2, 0, 2, 3,   // −Y
            4, 7, 6, 4, 6, 5,   // +Y
            0, 4, 5, 0, 5, 1,   // −Z
            2, 6, 7, 2, 7, 3,   // +Z
            1, 5, 6, 1, 6, 2,   // +X
            0, 3, 7, 0, 7, 4,   // −X
        };
        var faces = new Vector3[t.Length];
        for (var i = 0; i < t.Length; i++) faces[i] = rel * c[t[i]];
        return faces;
    }
}
