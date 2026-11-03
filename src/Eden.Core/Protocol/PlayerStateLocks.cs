using System;
using EdenClient.Protocol;

namespace EdenClient.Combat;

/// <summary>
/// Verrous d'action du perso local imposés par les statuts (stun, root,
/// silence...). Le serveur (<c>ZoneEntryFlow.Status.cs</c>,
/// <c>SendStateLockAttrsAsync</c>) réémet après chaque add/remove de
/// statut les 5 compteurs natifs <c>CEC_State*</c> en NCID 280
/// <c>UpdateAttr</c> : 1002 NoMove, 1003 NoAttack, 1004 NoSkill Magic,
/// 1005 NoSkill Kungfu, 1006 NoUseItem — valeur = nombre de statuts
/// actifs qui verrouillent (test natif <c>cnt &gt; 0</c>). Le client
/// grise/bloque donc l'entrée de façon cohérente avec le serveur, qui
/// reste l'autorité (il refuse de toute façon).
/// </summary>
public static class PlayerStateLocks
{
    private static readonly int[] Counts = new int[5];

    /// <summary>Déclenché quand un compteur change.</summary>
    public static event Action? Changed;

    /// <summary>Scène cinématique en cours (plan 02, mode cinéma) :
    /// verrouille déplacement, attaque, sorts et objets côté client.</summary>
    private static bool Cinema => Scene.CinemaTimeline.Shared.Active;

    public static bool NoMove => Counts[0] > 0 || Cinema;
    public static bool NoAttack => Counts[1] > 0 || Cinema;
    public static bool NoMagic => Counts[2] > 0 || Cinema;
    public static bool NoKungfu => Counts[3] > 0 || Cinema;
    public static bool NoUseItem => Counts[4] > 0 || Cinema;
    /// <summary>Aucun sort possible (les deux familles verrouillées) ;
    /// si une seule l'est, le serveur tranche selon le type du sort.</summary>
    public static bool NoAnySkill => NoMagic && NoKungfu;
    public static bool Any => Array.Exists(Counts, c => c > 0);

    /// <summary>Applique un 280 self d'attr 1002..1006 ; ignore les autres.</summary>
    public static void Apply(ZoneUpdateAttr m)
    {
        var i = m.AttrId switch
        {
            1002 => 0, 1003 => 1, 1004 => 2, 1005 => 3, 1006 => 4, _ => -1,
        };
        if (i < 0) return;
        var v = (int)Math.Max(0, m.Current);
        if (Counts[i] == v) return;
        Counts[i] = v;
        Changed?.Invoke();
    }

    /// <summary>État lisible pour les logs de sonde.</summary>
    public static string Describe() =>
        $"move={Counts[0]} atk={Counts[1]} magic={Counts[2]} kungfu={Counts[3]} item={Counts[4]}";

    /// <summary>Remise à zéro (changement de perso / reconnexion).</summary>
    public static void Reset()
    {
        Array.Clear(Counts);
        Changed?.Invoke();
    }
}
