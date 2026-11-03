namespace Eden.Core.Game.Services;

/// <summary>
/// Règles pures du partage de quête en équipe (C→S 87 MissionListOp
/// op 6 « Share » / op 7 « réponse du destinataire »). Preuves reverse :
/// <list type="bullet">
/// <item>zone <c>CMissionListOp::DoOperation</c> @0x0856e7f0 : op 6 →
/// refus 0xa07 si l'équipe partage déjà ; pour chaque membre local ≠ soi
/// la zone relaie <c>MissionServiceOp &lt;mid&gt; 6 &lt;charUid membre&gt;</c> à
/// la mission et l'inscrit comme destinataire en attente ; les membres
/// non locaux reçoivent le texte 0xbb0 côté partageur. Op 7 (envoyé par le
/// destinataire, <c>param_4</c> = accepte) : 0 → texte 0xa05 au partageur,
/// sinon relais <c>MissionServiceOp &lt;mid&gt; 7 &lt;flag&gt;</c> (= accept,
/// <c>TC_MissionServiceOp</c> cas 1/7 → SetFMS(3)) + texte 0xa06 ; quand la
/// liste d'attente est vide le partage est clos. Ops &lt; 6 sur la quête
/// en cours de partage → refus 0xb8a.</item>
/// <item>mission <c>CGlobalInfo::ShareMission</c> @0x0807bfd0 : quête non
/// partageable (flag mission+0x24 bit1 = col6 &amp; 2) → 0x61d ; plus de
/// 24 quêtes actives chez le destinataire → 0xa03 ; FMS 3/4 → 0x61a ;
/// FMS 1 → 0x61b ; <c>FMSCheckApproval</c> ≠ 6 → 0x619 ; sinon commande
/// zone du destinataire <c>shop mission-share 0 &lt;mid&gt;</c> + textes
/// 0x61c (destinataire) / 0xac (partageur, « titre,nom »).</item>
/// </list>
/// </summary>
public static class QuestShareRules
{
    public const short TextNotApproved = 0x619, TextOnQuest = 0x61a,
        TextCompleted = 0x61b, TextOffered = 0x61c, TextNonShareable = 0x61d,
        TextSharedTo = 0xac, TextFull = 0xa03, TextAlreadySharing = 0xa07,
        TextDeclined = 0xa05, TextAccepted = 0xa06, TextBusyOp = 0xb8a,
        TextNotLocal = 0xbb0;

    /// <summary>Seuil natif : <c>activeCount &gt; 0x18</c> → refus.</summary>
    public const int MaxActiveBeforeRefusal = 0x18;

    public enum Verdict { Ok, NonShareable, Full, OnQuest, Completed,
        NotApproved }

    /// <summary>Texte natif de refus d'un verdict (0 = accepté).</summary>
    public static short TextOf(Verdict v) => v switch
    {
        Verdict.NonShareable => TextNonShareable,
        Verdict.Full => TextFull,
        Verdict.OnQuest => TextOnQuest,
        Verdict.Completed => TextCompleted,
        Verdict.NotApproved => TextNotApproved,
        _ => 0,
    };

    /// <summary>Quête partageable ? flag +0x24 bit1 = col6 &amp; 2.</summary>
    public static bool IsShareable(int col6Flags) =>
        (col6Flags & 2) == 0;

    /// <summary>Verdict de <c>ShareMission</c> dans l'ordre natif.</summary>
    public static Verdict Check(int col6Flags, int targetActiveCount,
        int targetFms, bool approved)
    {
        if (!IsShareable(col6Flags)) return Verdict.NonShareable;
        if (targetActiveCount > MaxActiveBeforeRefusal) return Verdict.Full;
        if (targetFms is 3 or 4) return Verdict.OnQuest;
        if (targetFms == 1) return Verdict.Completed;
        return approved ? Verdict.Ok : Verdict.NotApproved;
    }
}
