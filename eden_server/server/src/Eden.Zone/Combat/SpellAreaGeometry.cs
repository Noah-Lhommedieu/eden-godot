using Eden.Core.Data.Parsers;

namespace Eden.Zone.Combat;

/// <summary>Géométrie exacte des HitTest natifs des sorts de zone
/// (décompilé <c>re/ghidra/zone/decompiled.c</c>, zone = binaire
/// <c>server_us/ZoneServer/ZoneServer</c>) :
/// <list type="bullet">
/// <item><b>1 cercle</b> — <c>CSpellToLocation::HitTest</c> @0x084f9300 :
/// centre = le point visé, rayon = <c>R(+0x48) + trunc(AttrSpellRadius/2)</c>,
/// énumération seulement si la spec +0x1a != -91 et rayon > 0.</item>
/// <item><b>6 secteur</b> — <c>CSpellToSector::HitTest</c> @0x084fb410 :
/// cône de sommet le lanceur, axe = lanceur→point, longueur
/// <c>L = SSpellData+0x40</c>, demi-angle <c>atan2(R', √(L²−R'²))</c> avec
/// <c>R' = min(R, L)</c>. Aucun mod rayon.</item>
/// <item><b>2 direction</b> — <c>CSpellToDirection::HitTest</c> @0x084f6ad0 +
/// <c>NotifyBlastToSelf</c> @0x084f6070 : onde de rectangles
/// <c>len(+0xb0) × 2R</c> qui avance du lanceur vers le point ; l'union
/// temporelle = bande de largeur 2R partant du lanceur, longueur
/// <c>L + trunc(mod/2)</c> (le natif la plafonne au premier obstacle via
/// <c>CheckLineOfMoveUntilBlock</c> — non modélisé ici).</item>
/// <item><b>5 croix</b> — <c>CSpellToCross::HitTest</c> @0x084f5020 +
/// <c>NotifyBlastToSelf</c> @0x084f40c0 : 4 bras = l'axe tourné de
/// 0/90/180/270° (<c>DAT_08896ac8</c> = π/2), centrés sur le LANCEUR,
/// largeur R, longueur <c>L + trunc(mod/2)</c> (plafonnés par obstacle en
/// natif, caps +0x88..0x94 — non modélisé).</item>
/// </list>
/// Constantes rodata vérifiées (ZoneServer, .rodata) :
/// <c>_DAT_0887b5b0</c> = double 0.5 (col28→L), <c>DAT_08894104</c> = -0.5f
/// (centre du rect d'onde), <c>_DAT_087b2754</c> = 1.0f (onde/tick),
/// <c>DAT_088774d0</c> = 0.1f (+0xac par défaut),
/// <c>DefaultFlySpeed</c> = 40, <c>MovingHitUpdateTime</c> = 1
/// (<c>MovingHitUpdateFlySpeed</c> = 4.0).</summary>
public static class SpellAreaGeometry
{
    /// <summary>Rayon de base <c>SSpellData+0x48</c> =
    /// <c>(short)(u16 col30 × 0.5f)</c> = S_Spell col30 × 0.5, TRONQUÉ
    /// (<c>LoadAllSpellData</c> @0x08380130, <c>DAT_087cc688</c> = 0.5f).</summary>
    public static float AreaRadius(SpellParser.SpellDef def) =>
        def.Duration > 0 ? (int)(def.Duration * 0.5f) : 0f;

    /// <summary>Longueur <c>SSpellData+0x40</c> =
    /// <c>(float)(u16 col28 × _DAT_0887b5b0)</c> avec
    /// <c>_DAT_0887b5b0</c> = double <b>0.5</b> vérifié en .rodata — soit
    /// S_Spell col28 × 0.5 (<see cref="SpellParser.SpellDef.Range"/>).</summary>
    public static float AreaLength(SpellParser.SpellDef def) =>
        def.Range > 0 ? def.Range * 0.5f : 0f;

    /// <summary>Demi-mod rayon du lanceur pour l'attribut 1
    /// (<c>AttrSpellRadius</c>) : le natif calcule
    /// <c>(short)(((ushort)(mod &gt;&gt; 15) &amp; 1) + (short)mod) &gt;&gt; 1</c>
    /// — l'ajout du bit de signe rend la division exactement
    /// <c>trunc(mod/2)</c>, identique à la division entière C# (vérifié
    /// pour valeurs positives et négatives).</summary>
    public static int RadiusModHalf(int attrModify) => attrModify / 2;

    /// <summary>Vrai si la spec +0x1a autorise l'énumération de victimes
    /// d'une forme de zone. Gate natif de
    /// <c>CSpellToLocation::HitTest</c> : <c>+0x1a == -0x5b (-91)</c>
    /// (« toute entité ») saute complètement le GridRangeSearch → zéro
    /// victime d'AoE. Utilisé aussi pour les autres formes : leur appel
    /// à <c>CanCastTarget(+0x1a)</c> par victime est déjà appliqué plus
    /// haut dans le pipeline, mais le -91 reste « toujours valide ».</summary>
    public static bool AreaEnumerationEnabled(int targetSpec) =>
        targetSpec != -91;

    /// <summary>Axe unitaire 2D lanceur→point, comme le natif qui normalise
    /// <c>(px−cx, py−cy)</c>. Point identique au lanceur (norme 0) →
    /// <c>false</c> : le natif n'a pas d'axe valide non plus.</summary>
    public static bool TryNormalizeAxis(float fromX, float fromY,
        float toX, float toY, out float ax, out float ay)
    {
        var dx = toX - fromX; var dy = toY - fromY;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len <= 0f) { ax = 0; ay = 0; return false; }
        ax = dx / len; ay = dy / len;
        return true;
    }

    /// <summary>Forme 1 — cercle : <c>distance(mob, centre) ≤ rayon</c>
    /// (<c>GridRangeSearch</c> natif).</summary>
    public static bool ContainsCircle(float cx, float cy, float radius,
        float x, float y)
    {
        if (radius <= 0f) return false;
        var dx = x - cx; var dy = y - cy;
        return dx * dx + dy * dy <= radius * radius;
    }

    /// <summary>Forme 6 — secteur : cône de sommet le lanceur
    /// (<c>ox,oy</c>), d'axe unitaire <c>(ax,ay)</c>, de longueur
    /// <paramref name="length"/> et de demi-angle
    /// <c>asin(min(R,L)/L)</c> (<c>SectorSearch</c> natif :
    /// <c>atan2(R', √(L²−R'²))</c>). Un point est touché si sa distance
    /// au sommet ≤ L et si son angle à l'axe ≤ le demi-angle — testé par
    /// produit scalaire <c>cos(demiAngle)</c>.</summary>
    public static bool ContainsSector(float ox, float oy, float ax,
        float ay, float length, float radius, float x, float y)
    {
        if (length <= 0f) return false;
        var dx = x - ox; var dy = y - oy;
        var d2 = dx * dx + dy * dy;
        if (d2 > length * length) return false;
        if (d2 <= 0f) return true;            // le sommet lui-même
        var r = MathF.Min(radius, length);    // R' = min(R, L)
        // demi-angle θ : sin θ = R'/L → cos θ = √(1 − (R'/L)²)
        var sinT = r / length;
        var cosT = MathF.Sqrt(MathF.Max(0f, 1f - sinT * sinT));
        // cos(angle au sommet) = (dx·ax + dy·ay) / dist — signe requis :
        // un point derrière le lanceur a cos négatif < cosT.
        var d = MathF.Sqrt(d2);
        return (dx * ax + dy * ay) / d >= cosT;
    }

    /// <summary>Forme 2 — direction : union de l'onde balayée = bande de
    /// demi-largeur <paramref name="radius"/> (largeur 2R en natif :
    /// <c>R + R</c>) partant du LANCEUR sur l'axe, de longueur
    /// <paramref name="length"/> (= distance lanceur→point plafonnée par
    /// <c>L + trunc(mod/2)</c> ; le natif la raccourcit aussi au mur).
    /// Point testé : projection sur l'axe dans [0, len] et distance
    /// perpendiculaire ≤ R.</summary>
    public static bool ContainsDirection(float ox, float oy, float ax,
        float ay, float length, float radius, float x, float y)
    {
        if (radius <= 0f || length <= 0f) return false;
        var dx = x - ox; var dy = y - oy;
        var along = dx * ax + dy * ay;
        if (along < 0f || along > length) return false;
        var px = dx - along * ax; var py = dy - along * ay;
        return px * px + py * py <= radius * radius;
    }

    /// <summary>Forme 5 — croix : deux bandes perpendiculaires de largeur
    /// totale <paramref name="radius"/> (demi-largeur R/2 en natif :
    /// <c>CrossSearch(width=R)</c>) et de demi-longueur
    /// <paramref name="length"/> par bras, centrées sur le LANCEUR
    /// (<c>this+0x4c</c> — le point ne sert qu'à l'orientation).
    /// Point testé : dans l'une des deux bandes — |proj. axe| ≤ L et
    /// |proj. perp| ≤ R/2, ou l'inverse.</summary>
    public static bool ContainsCross(float ox, float oy, float ax,
        float ay, float length, float radius, float x, float y)
    {
        if (radius <= 0f || length <= 0f) return false;
        var half = radius * 0.5f;
        var dx = x - ox; var dy = y - oy;
        var along = dx * ax + dy * ay;             // projection sur l'axe
        var across = dx * -ay + dy * ax;           // projection sur la perp
        var a = MathF.Abs(along); var p = MathF.Abs(across);
        return (a <= length && p <= half) ||
               (p <= length && a <= half);
    }
}
