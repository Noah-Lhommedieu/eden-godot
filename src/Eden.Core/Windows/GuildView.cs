using EdenClient.Protocol;

namespace EdenClient.Windows;

/// <summary>Logique pure de la fenêtre de guilde (port
/// <c>GuildWindow</c>) : rang affiché, tri/filtre des membres, lignes
/// de liste, journal, saisies de création/don/emblème.</summary>
public static class GuildView
{
    /// <summary>Rang affiché : chef via leader_id ; self via le 346
    /// (rang 0-7 → Titles[rank+2]) ; sinon titre 0 « Member » — le wire
    /// ne porte pas le rang des autres membres.</summary>
    public static string RankLabel(GuildMember m, int selfId,
        int leaderId, int selfRank, IReadOnlyList<string> titles)
    {
        string T(int i, string dflt) =>
            i >= 0 && i < titles.Count && titles[i].Length > 0
                ? titles[i] : dflt;
        if (m.CharId == leaderId) return T(1, "Leader");
        if (m.CharId == selfId && selfRank >= 0)
            return T(selfRank + 2, $"Rank {selfRank}");
        return T(0, "Member");
    }

    /// <summary>Membres triés : en ligne d'abord, chef, puis nom ;
    /// <paramref name="onlineOnly"/> masque les hors-ligne.</summary>
    public static List<GuildMember> Sorted(IEnumerable<GuildMember> ms,
        int leaderId, bool onlineOnly) =>
        ms.Where(m => !onlineOnly || m.Online != 0)
            .OrderByDescending(m => m.Online)
            .ThenByDescending(m => m.CharId == leaderId)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>« ● Nom — Lv12 Classe — Rang (+contrib) ».</summary>
    public static string MemberLine(GuildMember m, string className,
        string rank) =>
        $"{(m.Online != 0 ? "●" : "○")} {m.Name} — Lv{m.Level} {className}"
        + $" — {rank}" + (m.Contribution > 0 ? $" (+{m.Contribution})" : "");

    /// <summary>Infobulle d'un membre.</summary>
    public static string MemberTip(GuildMember m, string className) =>
        $"{m.Name} — node {m.NodeId}, {className} lv{m.ClassLevel}"
        + (m.SelfComment.Length > 0 ? $"\n«{m.SelfComment}»" : "")
        + (m.FamilyComment.Length > 0 ? $"\nnote: {m.FamilyComment}" : "");

    /// <summary>« [MM-dd] texte1 texte2 (tN #id) ».</summary>
    public static string LogLine(GuildLogEntry l)
    {
        var date = l.Date > 0
            ? DateTimeOffset.FromUnixTimeSeconds(l.Date).UtcDateTime
                .ToString("MM-dd") : "??";
        return $"[{date}] {l.Str1} {l.Str2} (t{l.Type} #{l.LogId})";
    }

    /// <summary>« [MM-dd] titre — auteur ».</summary>
    public static string BoardLine(int createDate, string title, string who)
    {
        var date = createDate > 0
            ? DateTimeOffset.FromUnixTimeSeconds(createDate).UtcDateTime
                .ToString("MM-dd") : "??";
        return $"[{date}] {title} — {who}";
    }

    /// <summary>« &lt;Nom&gt; Lv3 — 12 members — leader X ».</summary>
    public static string RecruitLine(GuildRecruitEntry e) =>
        $"<{e.Name}> Lv{e.Level} — {e.MemberCount} members — leader {e.LeaderName}";

    /// <summary>Nom de guilde valide pour la création (non vide).</summary>
    public static string? ValidName(string? name)
    {
        var n = (name ?? "").Trim();
        return n.Length == 0 ? null : n;
    }

    /// <summary>Don en or : entier &gt; 0 ou null.</summary>
    public static int? ParseDonation(string? t) =>
        int.TryParse((t ?? "").Trim(), out var v) && v > 0 ? v : null;

    /// <summary>Id d'emblème short ou null.</summary>
    public static short? ParseEmblem(string? t) =>
        short.TryParse((t ?? "").Trim(), out var v) ? v : null;

    /// <summary>« Treasury: 1 234 gold ».</summary>
    public static string Created(int unix) => unix > 0
        ? DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime
            .ToString("yyyy-MM-dd") : "—";
}
