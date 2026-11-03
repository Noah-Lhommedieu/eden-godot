using EdenClient.Protocol;

namespace EdenClient.Windows;

/// <summary>Actions du menu contextuel d'un membre de groupe.</summary>
public enum PartyMenu { Whisper, Promote, Kick, InviteTarget, Leave }

/// <summary>Logique pure des cadres de groupe (port <c>PartyHud</c>) :
/// barre de vie relative au max observé, menu contextuel selon le
/// rôle, ordre d'affichage, infobulle.</summary>
public static class PartyView
{
    /// <summary>Fraction HP 0..1 relative au max observé (0 si inconnu).</summary>
    public static float HpRatio(int hp, int maxSeen) =>
        maxSeen > 0 ? Math.Clamp((float)hp / maxSeen, 0f, 1f) : 0f;

    /// <summary>« hp/max » ; vide si max inconnu.</summary>
    public static string HpText(int hp, int maxSeen) =>
        maxSeen > 0 ? $"{hp}/{maxSeen}" : "";

    /// <summary>Entrées du menu : soi → Inviter la cible / Quitter ;
    /// autre → Chuchoter, + Promouvoir/Exclure si on est chef.</summary>
    public static List<PartyMenu> MenuFor(uint selfId, uint memberId,
        uint leaderId)
    {
        var l = new List<PartyMenu>();
        if (memberId == selfId)
        {
            l.Add(PartyMenu.InviteTarget);
            l.Add(PartyMenu.Leave);
            return l;
        }
        l.Add(PartyMenu.Whisper);
        if (selfId != 0 && leaderId == selfId)
        {
            l.Add(PartyMenu.Promote);
            l.Add(PartyMenu.Kick);
        }
        return l;
    }

    /// <summary>Membres triés par slot du roster (stable).</summary>
    public static List<TeamMember> Ordered(IEnumerable<TeamMember> ms) =>
        ms.OrderBy(m => m.Slot).ThenBy(m => m.CharId).ToList();

    /// <summary>Infobulle d'un cadre.</summary>
    public static string Tooltip(TeamMember m) =>
        (m.GuildName.Length > 0
            ? $"{m.CharName}  <{m.GuildName}>" : m.CharName)
        + $"  map S{m.MapId:D3}";

    /// <summary>Le membre est chef (flag bit0).</summary>
    public static bool IsLeader(TeamMember m) => (m.Flags & 1) != 0;
}
