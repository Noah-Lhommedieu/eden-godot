using Godot;

namespace EdenClient.World;

/// <summary>
/// Requêtes de hauteur « sol marchable » : les meshes helper classés
/// <see cref="HelperClass.Floor"/> par <see cref="ToonMaterializer"/>/
/// <see cref="CollisionBuilder"/> (dalles, ponts, rampes — pont S002
/// UB107/UB108) deviennent des <see cref="StaticBody3D"/> sur la layer
/// physique dédiée <see cref="FloorLayer"/> : ils ne bloquent JAMAIS
/// le déplacement horizontal (le joueur mask=1 les traverse), et la
/// hauteur de sol retenue est
/// <c>max(heightfield, raycastDown(FloorMask))</c>.
///
/// Le raycast descend depuis <c>référence + <see cref="HeadClearance"/>
/// </c> jusqu'à <c>référence − <see cref="MaxDrop"/></c> :
///   - assez haut pour qu'une rampe/marche monte sous les pieds (la
///     hauteur suit la rampe au fil des frames) ;
///   - assez bas pour passer SOUS un pont (un tablier à +8 u n'est pas
///     dans la bande → on reste au terrain) ;
///   - le compromis connu : un sol à moins de ~HeadClearance au-dessus
///     du terrain ne peut pas être passé dessous (la capsule 1,7 u ne
///     rentrerait de toute façon pas).
///
/// « Référence » = la hauteur précédente quand la requête est continue
/// (état sticky interne — un appelant qui marche interroge presque la
/// même colonne à chaque frame : son sol courant est la meilleure base
/// du rayon suivant) ; une requête discontinue (téléport, spawn)
/// repart du terrain — jamais de pop sur un toit/tablier au-dessus.
/// </summary>
public sealed class WalkableGround
{
    /// <summary>Layer des bloqueurs (murs) — inchangée.</summary>
    public const uint WallLayer = 1u;
    /// <summary>Layer des sols marchables helpers (bit dédié ; la
    /// layer picking entité est 1&lt;&lt;4 — pas de conflit).</summary>
    public const uint FloorLayer = 1u << 1;
    /// <summary>Layer des coquilles de salle (helpers
    /// <see cref="HelperClass.Both"/> — « block »/« Roomblock* » des
    /// NIF-portail) : elles bloquent horizontalement via
    /// <see cref="WallLayer"/> ET servent de sol de repli quand la
    /// salle n'a pas de « floor »/« terrain » — jamais dans
    /// <see cref="FloorMask"/>, sinon le dessous extérieur d'une
    /// coquille devient un faux sol (spawn S228 sous la salle).</summary>
    public const uint ShellLayer = 1u << 2;
    /// <summary>Mask de la requête raycast — floors uniquement.</summary>
    public const uint FloorMask = FloorLayer;
    /// <summary>Départ du rayon au-dessus de la référence (~tête).</summary>
    public const float HeadClearance = 2.3f;
    /// <summary>Chute max sondée sous la référence (descendre d'un
    /// pont haut doit retrouver le terrain).</summary>
    public const float MaxDrop = 24f;
    /// <summary>Distance xz max entre deux requêtes « continues »
    /// (sticky) — le joueur sonde ≤ ~0,2 u/frame.</summary>
    public const float Continuity = 3f;

    private Vector2 _pos;
    private float _y;
    private bool _valid;

    /// <summary>Oublie le sol courant (changement de map).</summary>
    public void Reset() => _valid = false;

    /// <summary>Hauteur marchable en (x, z) Godot : max(terrain,
    /// hit layer <see cref="FloorMask"/>) avec continuité sticky.
    /// <paramref name="worldCtx"/> = nœud DANS l'arbre (WorldRoot —
    /// son GetWorld3D donne l'espace physique ; null/hors arbre →
    /// repli terrain).</summary>
    public bool Sample(Node3D? worldCtx, float x, float z,
        bool hasTerrain, float terrainY, out float ground)
    {
        var baseY = hasTerrain ? terrainY : (_valid ? _y : 0f);
        var refY = baseY;
        if (_valid &&
            new Vector2(x - _pos.X, z - _pos.Y).Length() <= Continuity)
            refY = MathF.Max(refY, _y);
        var hasGround = Raycast(worldCtx, x, z, refY, hasTerrain,
            terrainY, out ground);
        _valid = hasGround;
        if (hasGround)
        {
            _pos = new Vector2(x, z);
            _y = ground;
        }
        return hasGround;
    }

    /// <summary>Variante « y-aware » : la référence du rayon est
    /// fournie par l'appelant (position courante du corps — résout
    /// dessus/dessous sans heuristique sticky). Sans état : n'altère
    /// pas le suivi interne de <see cref="Sample"/>.</summary>
    public bool SampleAt(Node3D? worldCtx, float x, float z,
        float refY, bool hasTerrain, float terrainY, out float ground) =>
        Raycast(worldCtx, x, z,
            hasTerrain ? MathF.Max(refY, terrainY) : refY,
            hasTerrain, terrainY, out ground);

    /// Requête « montante » pour les maps SANS heightfield (donjons à
    /// modèle unique — le sol est à une hauteur inconnue, la référence
    /// 0 tombe sous la map) : le rayon balaie la colonne de bas en
    /// haut, la première face rencontrée est le sol le plus bas =
    /// surface marchable. Ne sert que de replis (spawn local.ini).
    public bool SampleUp(Node3D? worldCtx, float x, float z,
        out float ground)
    {
        var space = worldCtx?.GetWorld3D()?.DirectSpaceState;
        ground = 0f;
        if (space == null) return false;
        // Sols « floor »/« terrain » d'abord, coquilles de salle
        // ensuite (un donjon sans terrain marche sur le fond de ses
        // coquilles « block »).
        if (ScanFloorsAbove(space, x, z, -4096f, FloorMask,
                out ground, float.MaxValue) ||
            ScanFloorsAbove(space, x, z, -4096f, ShellLayer,
                out ground, float.MaxValue))
        {
            _pos = new Vector2(x, z);
            _y = ground;
            _valid = true;
            return true;
        }
        return false;
    }

    // ------------------------------------------------------------------

    /// <summary>Rayon vertical dans la colonne (x, z) depuis
    /// ref+HeadClearance jusqu'à ref−MaxDrop sur <see cref="FloorMask"/>
    /// ; retourne max(terrain, hit). Sans espace physique (hors arbre)
    /// retombe sur le terrain pur.</summary>
    private static bool Raycast(Node3D? worldCtx, float x, float z,
        float refY, bool hasTerrain, float terrainY, out float ground)
    {
        ground = terrainY;
        var hasGround = hasTerrain;
        var space = worldCtx?.GetWorld3D()?.DirectSpaceState;
        if (space == null) return hasGround;
        var hit = RaycastDown(space, x, z, refY, FloorMask);
        if (hit.Count > 0)
        {
            var hy = hit["position"].AsVector3().Y;
            if (!hasGround || hy > ground)
            {
                ground = hy;
                hasGround = true;
            }
        }
        if (hasGround || hasTerrain) return hasGround;
        // Sans heightfield (donjon modèle unique) : un sol peut être
        // AU-DESSUS de la référence (spawn local.ini à y=0 sous la
        // salle). Un raycast ne voit que les faces tournées vers
        // l'origine — d'abord la face « dessus » juste SOUS la
        // référence (le joueur debout sur un sol), sinon la plus
        // basse AU-DESSUS (plancher de la salle). Sols
        // « floor »/« terrain » d'abord, coquilles « block » en
        // repli (salles sans terrain).
        // Repli « sol au-dessus » borné à HeadClearance : un dessus de
        // dalle bien au-dessus de la tête est un plafond/pont haut, pas
        // un sol — le prendre téléportait le marcheur sur le toit des
        // plateformes sous lesquelles il passait (S198 : +195 u).
        return ScanDown(space, x, z, refY, FloorMask, out ground) ||
               ScanFloorsAbove(space, x, z, refY, FloorMask,
                   out ground, HeadClearance) ||
               ScanDown(space, x, z, refY, ShellLayer, out ground) ||
               ScanFloorsAbove(space, x, z, refY, ShellLayer,
                   out ground, HeadClearance);
    }

    /// <summary>Première face « dessus » (normale vers le haut)
    /// sous la référence — l'équivalent orienté du rayon descendant
    /// : le dessous extérieur d'une coquille (normale bas) ne peut
    /// pas y faire office de sol.</summary>
    private static bool ScanDown(PhysicsDirectSpaceState3D space,
        float x, float z, float refY, uint mask, out float ground)
    {
        ground = 0f;
        var fromY = refY + HeadClearance;
        var stopY = refY - MaxDrop;
        for (var i = 0; i < 8; i++)
        {
            var q = PhysicsRayQueryParameters3D.Create(
                new Vector3(x, fromY, z), new Vector3(x, stopY, z),
                mask);
            q.HitFromInside = true;
            var hit = space.IntersectRay(q);
            if (hit.Count == 0) return false;
            var pos = hit["position"].AsVector3();
            if (hit["normal"].AsVector3().Y > 0.35f)
            {
                ground = pos.Y;
                return true;
            }
            fromY = pos.Y - 0.05f;
            if (fromY <= stopY) return false;
        }
        return false;
    }

    /// <summary>Rayon vertical descendant de ref+HeadClearance à
    /// ref−MaxDrop sur <paramref name="mask"/>.</summary>
    private static Godot.Collections.Dictionary RaycastDown(
        PhysicsDirectSpaceState3D space, float x, float z, float refY,
        uint mask)
    {
        var q = PhysicsRayQueryParameters3D.Create(
            new Vector3(x, refY + HeadClearance, z),
            new Vector3(x, refY - MaxDrop, z), mask);
        // L'origine peut toucher l'intérieur d'une dalle fine.
        q.HitFromInside = true;
        return space.IntersectRay(q);
    }

    /// <summary>Colonne balayée de haut en bas sur
    /// <paramref name="mask"/> : retient la face dont la normale
    /// regarde vers le haut (dessus d'un sol) la plus BASSE au-dessus
    /// de <paramref name="refY"/> — le plancher intérieur de la salle
    /// qui couvre (x, z). <paramref name="maxRise"/> borne la
    /// remontée (mouvement : un dessus au-delà de la tête est un
    /// plafond) ; <see cref="float.MaxValue"/> = colonne entière
    /// (spawn). L'origine descend juste sous chaque hit
    /// (pas d'exclusion de RID : le dessus et le dessous d'une dalle
    /// partagent le même corps).</summary>
    private static bool ScanFloorsAbove(PhysicsDirectSpaceState3D space,
        float x, float z, float refY, uint mask, out float ground,
        float maxRise)
    {
        ground = 0f;
        var diag = System.Environment.GetEnvironmentVariable(
            "EDEN_COLLISION_DIAG") == "1";
        var topY = 4096f;
        var best = float.MaxValue;
        for (var i = 0; i < 32; i++)
        {
            var q = PhysicsRayQueryParameters3D.Create(
                new Vector3(x, topY, z), new Vector3(x, -4096f, z),
                mask);
            q.HitFromInside = true;
            var hit = space.IntersectRay(q);
            if (hit.Count == 0) break;
            var pos = hit["position"].AsVector3();
            var n = hit["normal"].AsVector3();
            if (diag)
                GD.Print($"[scanflr] ({x:F0},{z:F0}) mask={mask} " +
                         $"hit y={pos.Y:F1} n=({n.X:F2},{n.Y:F2}," +
                         $"{n.Z:F2}) {hit["collider"].AsGodotObject()}");
            if (n.Y > 0.35f && pos.Y > refY + 0.1f &&
                pos.Y < refY + maxRise && pos.Y < best)
                best = pos.Y;
            topY = pos.Y - 0.05f;
            if (pos.Y <= refY) break;
        }
        if (best == float.MaxValue) return false;
        ground = best;
        return true;
    }
}
