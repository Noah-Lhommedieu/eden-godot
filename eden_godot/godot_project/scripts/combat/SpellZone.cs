using EdenClient.Game;

namespace EdenClient.Combat;

/// <summary>Forme de lancement d'un sort = <c>SSpellData+0x18</c> natif (clé du générateur de
/// <c>CSpellFactory::InitSpellTable</c> @0x08154a10).</summary>
public enum CastShape
{
    /// <summary>CSpellToLife — cible unique (NCID 9).</summary>
    Single = 0,
    /// <summary>CSpellToLocation — cercle au sol autour du point visé (NCID 16).</summary>
    Ground = 1,
    /// <summary>CSpellToDirection — rectangle balayé depuis le lanceur vers le point.</summary>
    Direction = 2,
    /// <summary>CSpellToNowhere — centré sur le lanceur, sans cible (NCID 17).</summary>
    Self = 3,
    /// <summary>CSpellToNearby (jamais produit par LoadAllSpellData).</summary>
    Nearby = 4,
    /// <summary>CSpellToCross — croix orientée lanceur → point.</summary>
    Cross = 5,
    /// <summary>CSpellToSector — secteur orienté lanceur → point.</summary>
    Sector = 6,
}

/// <summary>Quel message le client natif émet (<c>CastToLife/Location/Nowhere</c>) —
/// ou <c>Refused</c> : le sort exige une cible et le client natif n'a AUCUN
/// message « dans le vide » pour cette forme → le cast ne doit pas partir.</summary>
public enum CastRoute { ToLife, ToLocation, ToNowhere, Refused }

/// <summary>Décision de lancement : message + point visé (coords sol Eden x,y).</summary>
public readonly record struct CastPlan(CastRoute Route, float X, float Y);

/// <summary>
/// Géométrie et routage PURS (sans Godot, testable) des sorts de zone. Source : décompilé zone.
/// <list type="bullet">
/// <item><c>CSpellDBO::LoadAllSpellData</c> @0x08380130 : forme dérivée de C_Spell col 21 (index 20) et
/// col 31 (index 30) ; rayon <c>+0x48 = (short)(u16 col30 × 0.5f)</c> (index 29) — TRONQUÉ.</item>
/// <item><c>CSpellFactory::CastToLocation</c> @0x08154210 : seules les formes 1/2/5/6 acceptent un point
/// (sinon « can't cast to location » 0x5ab) ; 1 = cercle centré SUR le point ; 2/5/6 = axe
/// lanceur → point (<c>CastToDirection/Cross/Sector</c> normalisent <c>(px−cx, py−cy)</c>).</item>
/// <item><c>CastToLife</c> @0x081545a0 : formes 0, 2, 5, 6 seulement (2/5/6 utilisent la position de la
/// cible comme point) ; la forme 1 est refusée (« can't cast to life » 0x5aa).</item>
/// <item><c>CastToNowhere</c> @0x08153990 : forme 3 seulement ; centre = position du lanceur.</item>
/// </list>
/// </summary>
public static class SpellZone
{
    /// <summary>Distance du point visé devant le lanceur quand aucune cible n'est sélectionnée (le
    /// natif vise au curseur ; sans curseur on vise devant soi), plafonnée par la portée du sort.</summary>
    public const float GroundFallbackMax = 8f;

    /// <summary>Port exact du switch de <c>LoadAllSpellData</c> : <paramref name="p1"/> = C_Spell index 20,
    /// <paramref name="sub"/> = index 30.</summary>
    public static CastShape ShapeOf(int p1, int sub) => p1 switch
    {
        0 => sub == 3 ? CastShape.Cross : CastShape.Self,
        >= 1 and <= 3 => CastShape.Self,
        4 => sub == 2 ? CastShape.Sector : sub == 3 ? CastShape.Cross
            : sub == 1 ? CastShape.Direction : CastShape.Single,
        5 => sub == 2 ? CastShape.Sector : sub == 3 ? CastShape.Cross
            : sub == 1 ? CastShape.Direction : CastShape.Ground,
        _ => CastShape.Single,
    };

    /// <summary>Rayon de zone en unités monde : <c>(short)(col29 × 0.5f)</c>, tronqué ; 0 = pas de zone.</summary>
    public static float AreaRadius(int col29) => col29 > 0 ? (int)(col29 * 0.5f) : 0f;

    /// <summary>Portée du sort en unités : <c>+0x40 = (float)(u16 col28 × _DAT_0887b5b0)</c>
    /// (LoadAllSpellData @0x08380130). La constante n'est lue nulle part en clair ; ses autres usages
    /// (<c>x × c + c'</c> arrondi, <c>d ≤ r + c</c> tolérance) et le float jumeau du rayon
    /// (<c>DAT_087cc688 = 0.5f</c>) indiquent 0,5 — **hypothèse**. Utilisé pour borner la visée au sol ;
    /// la poursuite de cast ciblé garde son facteur historique (BeyondReach, col28 × 1,5).
    /// col28 = 0 → 4 u.</summary>
    public static float Reach(int col28) => col28 > 0 ? col28 * 0.5f : 4f;

    /// <summary>Vrai si le natif accepte un lancer vers un point (<c>CastToLocation</c>).</summary>
    public static bool AcceptsLocation(CastShape s) =>
        s is CastShape.Ground or CastShape.Direction or CastShape.Cross or CastShape.Sector;

    /// <summary>Vrai si le natif accepte un lancer sans cible (<c>CastToNowhere</c>).</summary>
    public static bool AcceptsNowhere(CastShape s) => s == CastShape.Self;

    /// <summary>Vrai si le blast est centré SUR le lanceur (Self/Nearby —
    /// CSpellToNowhere @0x08153990, centre = position du lanceur) : le
    /// visuel AoE (cyclone…) entoure le lanceur et non chaque victime.</summary>
    public static bool SelfAnchored(CastShape s) =>
        s is CastShape.Self or CastShape.Nearby;

    /// <summary>Vrai si la forme EXIGE une entité ciblée — la forme 0
    /// (<c>CSpellToLife</c>) est la seule sans canal de repli : ni
    /// CastToLocation ni CastToNowhere ne l'acceptent (serveur natif
    /// 0x5aa/0x5ac → 410). Un client natif n'émet rien sans cible pour
    /// elle — c'est la règle « pas de cast sans cible ».</summary>
    public static bool RequiresTarget(CastShape s) => s == CastShape.Single;

    /// <summary>Vecteur unitaire (x,y sol Eden) du cap Eden <paramref name="dir"/>.</summary>
    public static (float X, float Y) Axis(float dir)
    {
        var a = EdenDir.HeadingOf(dir);
        return (MathF.Cos(a), MathF.Sin(a));
    }

    /// <summary>Axe NORMALISÉ lanceur → point (ce que calculent CastToDirection/Cross/Sector) ; (0,0) si
    /// le point est confondu avec le lanceur (le natif divise par 0 — on se rabat sur le cap).</summary>
    public static (float X, float Y) AxisTo(float casterX, float casterY, float px, float py, float fallbackDir)
    {
        var dx = px - casterX; var dy = py - casterY;
        var len = MathF.Sqrt(dx * dx + dy * dy);
        return len < 1e-4f ? Axis(fallbackDir) : (dx / len, dy / len);
    }

    /// <summary>Un entité (ex,ey) est-elle dans le cercle (centre cx,cy, rayon r) ? Test natif :
    /// <c>distance ≤ rayon</c>.</summary>
    public static bool InCircle(float cx, float cy, float r, float ex, float ey)
    {
        var dx = ex - cx; var dy = ey - cy;
        return dx * dx + dy * dy <= r * r;
    }

    /// <summary>
    /// Routage natif d'un sort : forme + cible → message et point.
    /// <para><c>Self</c> : toujours NCID 17, même avec une cible sélectionnée.
    /// <c>Single</c> : NCID 9 avec cible ; SANS cible → <see cref="CastRoute.Refused"/>
    /// — le client natif n'a pas de message « dans le vide » pour la forme 0
    /// (CastToNowhere la refuse, 0x5ac), il bloque en local avec le prompt
    /// textindex 2195.
    /// <c>Ground</c> : NCID 16 centré sur la cible, ou devant le lanceur sans cible.
    /// <c>Direction/Cross/Sector</c> : NCID 9 avec cible (le serveur redirige vers la position de la
    /// cible, <c>CastToLife</c>), sinon NCID 16 vers un point devant le lanceur (l'axe est
    /// normalisé côté serveur, la distance n'importe pas).</para>
    /// </summary>
    public static CastPlan Plan(CastShape shape, bool hasTarget, float targetX, float targetY,
        float casterX, float casterY, float facingDir, float reach)
    {
        switch (shape)
        {
            case CastShape.Self:
            case CastShape.Nearby:
                return new CastPlan(CastRoute.ToNowhere, casterX, casterY);
            case CastShape.Ground:
                if (hasTarget) return new CastPlan(CastRoute.ToLocation, targetX, targetY);
                return Ahead(casterX, casterY, facingDir, MathF.Min(reach, GroundFallbackMax));
            case CastShape.Direction:
            case CastShape.Cross:
            case CastShape.Sector:
                if (hasTarget) return new CastPlan(CastRoute.ToLife, targetX, targetY);
                return Ahead(casterX, casterY, facingDir, 5f);
            default:
                // Forme 0 (cible unique) : le natif n'a aucun message
                // « dans le vide » — CastToNowhere refuse la forme 0
                // (0x5ac) et CastToLocation aussi (0x5ab). Sans cible
                // le cast est REFUSÉ en local (textindex 2195 côté UI).
                return hasTarget
                    ? new CastPlan(CastRoute.ToLife, targetX, targetY)
                    : new CastPlan(CastRoute.Refused, casterX, casterY);
        }
    }

    /// <summary>Borne (x,y) à une distance maximale <paramref name="reach"/> du lanceur (ox,oy) — le centre
    /// du cercle visé ne peut pas sortir de la portée du sort.</summary>
    public static (float X, float Y, bool Clamped) ClampToReach(float ox, float oy, float x, float y, float reach)
    {
        var dx = x - ox; var dy = y - oy;
        var d = MathF.Sqrt(dx * dx + dy * dy);
        if (d <= reach || d < 1e-4f) return (x, y, false);
        var k = reach / d;
        return (ox + dx * k, oy + dy * k, true);
    }

    private static CastPlan Ahead(float cx, float cy, float dir, float dist)
    {
        var (ax, ay) = Axis(dir);
        return new CastPlan(CastRoute.ToLocation, cx + ax * dist, cy + ay * dist);
    }
}
