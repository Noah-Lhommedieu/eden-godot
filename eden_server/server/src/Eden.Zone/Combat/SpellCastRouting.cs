using Eden.Core.Data.Parsers;

namespace Eden.Zone.Combat;

/// <summary>Règles natives d'acceptation d'un cast par message —
/// quelle forme de sort (<c>SSpellData+0x18</c>) peut passer par quel
/// NCID client, et quels refus le serveur natif émet. Source : décompilé
/// <c>re/ghidra/zone/decompiled.c</c> (binaire
/// <c>server_us/ZoneServer/ZoneServer</c>) :
/// <list type="bullet">
/// <item><b>9 = CastToLife</b> @0x081545a0 : formes {0,2,5,6}
/// seulement — 0 = cible unique, 2/5/6 = AoE axées sur la position de la
/// cible. Autre forme → <c>TextOutputIndex</c> 0x5aa = textindex
/// <b>1450</b> « Inutilisable sur les gens », puis 410
/// <c>ClientCommandFailReturn [ncid][spell]</c>. Handle 0 →
/// <c>GetLifeEntity(0)</c> NULL → <c>CastTest</c> 0x5af =
/// <b>1455</b> « La cible n'existe pas ».</item>
/// <item><b>16 = CastToLocation</b> @0x08154210 : formes {1,2,5,6} —
/// 1 = cercle sur le point, 2/5/6 = axe lanceur→point normalisé.
/// Sinon 0x5ab = <b>1451</b> « Inutilisable au sol ».</item>
/// <item><b>17 = CastToNowhere</b> @0x08153990 : forme 3 seulement
/// (centré sur le lanceur). Sinon 0x5ac = <b>1452</b> « Inutilisable
/// sur vous-même ».</item>
/// <item><b>59 = CastToNearby</b> @0x081536d0 : forme 4 seulement
/// (jamais produite par <c>LoadAllSpellData</c>).</item>
/// <item><b>Sort inconnu</b> — <c>CheckCanCast</c> @0x081526e0 : la
/// recherche dans la table des sorts échoue → 0x5a9 = <b>1449</b>
/// « La Compétence (%s1) n'existe pas. », arg = id décimal.</item>
/// <item><b>Cible joueur</b> — <c>CSpellToLife::CastTest</c>
/// @0x084f04d0 : <c>CanCastTarget(+0x1a)</c> est appelé avec
/// <c>param_5=0</c> → échec = retour 0 <b>silencieux</b> (410 seul,
/// pas de message). Portée : <c>HowClose(dist, +0x44, rayons +
/// +0x40 + attrModify(0) + 2.5)</c> → hors limite = 0x333 =
/// <b>819</b> « trop éloignée » ; sous +0x44 = 0x334 = <b>820</b>
/// « trop près » (+0x44 reste 0 dans LoadAllSpellData → branche morte).
/// Constante <c>DAT_08896c68</c> = double <b>2.5</b> (.rodata).</item>
/// </list>
/// Le refus complet natif = 316 <c>CNC_CZ_ClientTextOutputIndex</c>
/// <c>[s16 index][str arg]</c> suivi du 410 — voir
/// <c>vfunc TextOutputIndex</c> @0x08429b20 et les <c>Execute</c> des
/// commandes 9/16/17 qui émettent le 410 quand <c>CastTo*</c> retourne 0.</summary>
public static class SpellCastRouting
{
    /// <summary>CSpellToLife — cible unique.</summary>
    public const int ShapeSingle = 0;
    /// <summary>CSpellToLocation — cercle au sol.</summary>
    public const int ShapeGround = 1;
    /// <summary>CSpellToDirection — onde orientée.</summary>
    public const int ShapeDirection = 2;
    /// <summary>CSpellToNowhere — centré sur le lanceur.</summary>
    public const int ShapeSelf = 3;
    /// <summary>CSpellToNearby — jamais produit par la table.</summary>
    public const int ShapeNearby = 4;
    /// <summary>CSpellToCross — croix centrée lanceur, axe vers le point.</summary>
    public const int ShapeCross = 5;
    /// <summary>CSpellToSector — cône lanceur→point.</summary>
    public const int ShapeSector = 6;

    /// <summary>NCID du cast ciblé (<c>SpellCastToLife</c>).</summary>
    public const int NcidToLife = 9;
    /// <summary>NCID du cast au sol (<c>SpellCastToLocation</c>).</summary>
    public const int NcidToLocation = 16;
    /// <summary>NCID du cast sans cible (<c>SpellCastToNowhere</c>).</summary>
    public const int NcidToNowhere = 17;
    /// <summary>NCID du cast « alentour » (<c>SpellCastToNearby</c>).</summary>
    public const int NcidToNearby = 59;

    /// <summary>0x5a9 — « La Compétence (%s1) n'existe pas. »</summary>
    public const int TextUnknownSpell = 1449;
    /// <summary>0x5aa — refus CastToLife sur forme non ciblée.</summary>
    public const int TextCantCastToLife = 1450;
    /// <summary>0x5ab — refus CastToLocation sur forme non zonée.</summary>
    public const int TextCantCastToLocation = 1451;
    /// <summary>0x5ac — refus CastToNowhere/Nearby (forme non auto-centrée).</summary>
    public const int TextCantCastToNowhere = 1452;
    /// <summary>0x5af — « La cible n'existe pas. »</summary>
    public const int TextTargetNotFound = 1455;
    /// <summary>0x333 — « La cible est trop éloignée. » (portée CastTest)</summary>
    public const int TextTooFar = 819;

    /// <summary>Constante additive native de la limite extérieure de
    /// portée — <c>DAT_08896c68</c> = double 2.5 (.rodata ZoneServer).</summary>
    public const float OuterRangeBonus = 2.5f;

    /// <summary>Shape dérivée <c>SSpellData+0x18</c> — switch natif de
    /// <c>LoadAllSpellData</c> @0x08380130 sur <c>CSpellData+0x44</c>
    /// (S_Spell col21 = <see cref="SpellParser.SpellDef.Param1"/>)
    /// combiné à <c>+0xcc</c> (col31 =
    /// <see cref="SpellParser.SpellDef.ShapeSub"/>) :
    /// 0→{3,5}, 1-3→3, 4→{0,2,5,6}, 5→{1,2,5,6}, autre→0.
    /// Valeurs = clés de générateurs de <c>CSpellFactory::InitSpellTable</c>.</summary>
    public static int ShapeOf(SpellParser.SpellDef def) =>
        def.Param1 switch
        {
            0 => def.ShapeSub == 3 ? ShapeCross : ShapeSelf,
            >= 1 and <= 3 => ShapeSelf,
            4 => def.ShapeSub == 2 ? ShapeSector : def.ShapeSub == 3
                ? ShapeCross : def.ShapeSub == 1 ? ShapeDirection
                : ShapeSingle,
            // case 5 natif : sub==1→2, sub==2→6, sub==3→5, sinon 1.
            5 => def.ShapeSub == 2 ? ShapeSector : def.ShapeSub == 3
                ? ShapeCross : def.ShapeSub == 1 ? ShapeDirection
                : ShapeGround,
            _ => ShapeSingle,   // case 6 et hors table → mono-cible
        };

    /// <summary>Vrai si la forme peut passer par NCID 9
    /// (<c>CastToLife</c> @0x081545a0 : 0 ciblée ; 2/5/6 = AoE à la
    /// position de la cible). La forme 1 y est refusée (0x5aa).</summary>
    public static bool AcceptsToLife(int shape) =>
        shape is ShapeSingle or ShapeDirection or ShapeCross or ShapeSector;

    /// <summary>Vrai si la forme peut passer par NCID 16
    /// (<c>CastToLocation</c> @0x08154210 : 1 cercle ; 2/5/6 axe).</summary>
    public static bool AcceptsToLocation(int shape) =>
        shape is ShapeGround or ShapeDirection or ShapeCross or ShapeSector;

    /// <summary>Vrai si la forme peut passer par NCID 17
    /// (<c>CastToNowhere</c> @0x08153990 : forme 3 seulement).</summary>
    public static bool AcceptsToNowhere(int shape) => shape == ShapeSelf;

    /// <summary>Vrai si la forme peut passer par NCID 59
    /// (<c>CastToNearby</c> @0x081536d0 : forme 4 seulement).</summary>
    public static bool AcceptsToNearby(int shape) => shape == ShapeNearby;

    /// <summary>Vrai si <paramref name="ncid"/> est un canal valide
    /// pour la forme <paramref name="shape"/> (table de routage
    /// <c>CSpellFactory</c> ci-dessus).</summary>
    public static bool Accepts(int ncid, int shape) => ncid switch
    {
        NcidToLife => AcceptsToLife(shape),
        NcidToLocation => AcceptsToLocation(shape),
        NcidToNowhere => AcceptsToNowhere(shape),
        NcidToNearby => AcceptsToNearby(shape),
        _ => false,
    };

    /// <summary>Sorts à forme directionnelle (2 direction / 5 croix / 6
    /// secteur) dont la DESCRIPTION FR (T_Spell) place l'effet autour/devant
    /// le lanceur : « autour du lanceur / de lui / de l'invocateur », « devant
    /// le lanceur », « Jeteur de sort téléporté », « N m alentour » sans mention
    /// de cible. Seuls ceux-là se lancent sans cible (NCID 16 sans handle) ; les
    /// autres exigent une cible (NCID 9). Règle propriétaire
    /// (docs/knowledge/definitions.md § Règles de lancer), dérivée côté client par
    /// <c>SpellCastRule.CasterCentered</c> — le test NetTests
    /// <c>CasterCenteredIds_MatchServerList</c> garde les deux listes alignées
    /// sur les données fr-full.</summary>
    public static readonly IReadOnlySet<int> CasterCenteredSpellIds =
        new HashSet<int>
        {
            50385, 50398, 51331, 51538, 58236, 58473, 58478, 58481,
            58484, 58485, 59143,
        };

    /// <summary>Un cast SANS cible (NCID 16) est-il admis pour ce sort ?
    /// Cercle au sol (forme 1) : oui — le point visé fait office de cible
    /// (visée au curseur ; ou cercle « autour de l'invocateur » centré
    /// lanceur). Direction/croix/secteur : seulement si la description
    /// les centre sur le lanceur. Autres formes : jamais par le 16.</summary>
    public static bool AcceptsLocationWithoutTarget(int shape, int spellId) =>
        shape == ShapeGround ||
        (shape is ShapeDirection or ShapeCross or ShapeSector &&
         CasterCenteredSpellIds.Contains(spellId));

    /// <summary>Vrai si la forme exige une entité ciblée — la forme 0
    /// (<c>CSpellToLife</c>) est la SEULE sans canal de repli : elle ne
    /// passe ni par 16 ni par 17 ni par 59. Un client qui n'a pas de
    /// cible ne doit donc rien émettre pour un sort de forme 0.</summary>
    public static bool RequiresLifeTarget(int shape) =>
        shape == ShapeSingle;

    /// <summary>Textindex du refus de routage natif pour le NCID
    /// (émission = 316 <c>TextOutputIndex</c> puis 410). 59 partage le
    /// refus de CastToNowhere (0x5ac).</summary>
    public static int RefusalTextIndex(int ncid) => ncid switch
    {
        NcidToLife => TextCantCastToLife,
        NcidToLocation => TextCantCastToLocation,
        _ => TextCantCastToNowhere,
    };

    /// <summary>Gate natif <c>CanCastTarget(+0x1a)</c> quand la cible
    /// du cast est un JOUEUR — appelé avec <c>param_5=0</c> dans
    /// <c>CSpellToLife::CastTest</c> : l'échec refuse SILENCIEUX (le
    /// serveur n'émet que le 410). Specs :
    /// -99 = IsMyEnemy → un joueur n'est jamais la cible d'un sort
    /// hostile (pas de PvP modélisé) ; -95/-94 = ennemi quand
    /// Type==1, ami sinon ; -98/-96/-97/-93/-91 et autres → joueur
    /// admis (l'équipe est contrôlée à part via
    /// <see cref="SpecNeedsTeam"/>).</summary>
    public static bool SpecAllowsPlayerTarget(int spec, int spellType) =>
        spec switch
        {
            -99 => false,
            -95 or -94 => spellType != 1,
            _ => true,
        };

    /// <summary>Vrai si la spec exige la même équipe (-96/-97 →
    /// <c>IsMyTeam</c> ; -96 inclut aussi le lanceur). La cible = le
    /// lanceur lui-même est toujours admise.</summary>
    public static bool SpecNeedsTeam(int spec) => spec is -96 or -97;

    /// <summary>Limite extérieure de portée d'un cast ciblé —
    /// <c>CSpellToLife::CastTest</c> : <c>HowClose(cible, +0x44=0,
    /// r_lanceur + r_cible + SSpellData+0x40 + attrModify(0) + 2.5)</c>.
    /// +0x40 = portée S_Spell col28 × 0,5
    /// (<see cref="SpellAreaGeometry.AreaLength"/>) ;
    /// <paramref name="attrRangeMod"/> = GetSpellAttrModify kind 0
    /// (AttrSpellRange). Les rayons d'entités (+0x18 vfunc) ne sont pas
    /// résolus dans le décompilé → borne additive forfaitaire de 1,0 u
    /// documentée (le client poursuit avant d'émettre, donc les casts
    /// légitimes restent largement en-dessous).</summary>
    public static float LifeCastOuterRange(SpellParser.SpellDef def,
        int attrRangeMod) =>
        SpellAreaGeometry.AreaLength(def) + attrRangeMod +
        OuterRangeBonus + 1.0f;
}
