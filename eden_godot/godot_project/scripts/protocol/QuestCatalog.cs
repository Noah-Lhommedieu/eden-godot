using EdenClient.Data.Tables;

namespace EdenClient.Protocol;

/// <summary>Spécialisation d'une quête (étiquette « Quête principale »,
/// « Quête quotidienne »… du journal, ref « quest log 1 » : « Quête
/// spéciale »).</summary>
public enum QuestKind
{
    Other, Main, Single, Repeat, Daily, Special, Guild, Awakening, Class,
}

/// <summary>Logique pure (testée NetTests) du catalogue de quêtes côté
/// client : spécialisation, niveau, et « quêtes manquées / oubliées »
/// = quêtes que le joueur pourrait prendre selon son niveau et ses
/// prérequis (mêmes règles que le serveur : <c>ConditionsPass</c> +
/// <c>offerable</c> de QuestEngine).</summary>
public static class QuestCatalog
{
    /// <summary>Type depuis les colonnes 10 et 23 (mots-clés Big5 du
    /// client : 每日 quotidienne, 重複 répétable, 主線 principale, 單次
    /// unique, 特殊 spéciale, 公會 guilde, 覺醒 éveil, 職業 classe).</summary>
    public static QuestKind KindOf(string group, string category, int flags)
    {
        var t = group + "|" + category;
        if (t.Contains("每日") || (flags & 0x10) != 0 && t.Contains("日"))
            return QuestKind.Daily;
        if (t.Contains("重複")) return QuestKind.Repeat;
        if (t.Contains("主線")) return QuestKind.Main;
        if (t.Contains("覺醒")) return QuestKind.Awakening;
        if (t.Contains("公會")) return QuestKind.Guild;
        if (t.Contains("特殊")) return QuestKind.Special;
        if (t.Contains("職業")) return QuestKind.Class;
        if (t.Contains("單次")) return QuestKind.Single;
        return (flags & 0x10) != 0 ? QuestKind.Daily : QuestKind.Other;
    }

    /// <summary>Clé de traduction EN de l'étiquette d'un type.</summary>
    public static string LabelKey(QuestKind k) => k switch
    {
        QuestKind.Main => "Main Quest",
        QuestKind.Single => "Side Quest",
        QuestKind.Repeat => "Repeat Quest",
        QuestKind.Daily => "Daily Quest",
        QuestKind.Special => "Special Quest",
        QuestKind.Guild => "Guild Quest",
        QuestKind.Awakening => "Awakening Quest",
        QuestKind.Class => "Class Quest",
        _ => "Quest",
    };

    /// <summary>Niveau d'une quête : <c>If_CharLevel &gt;= n</c> /
    /// <c>PlayerLevel &gt;= n</c> de col13, sinon col8, sinon le nombre
    /// du titre « Nv. 12 … » / « L12. … ».</summary>
    public static int LevelOf(MissionTable.MissionDef m, string? title)
    {
        var lv = 0;
        foreach (var c in m.Conditions)
            if (c.Name is "If_CharLevel" or "PlayerLevel" &&
                c.Args.Count >= 2 && c.Args[0] == ">=" &&
                int.TryParse(c.Args[1], out var n))
                lv = Math.Max(lv, n);
        if (lv == 0) lv = m.ReqLevel;
        if (lv == 0 && title != null)
        {
            var i = 0;
            while (i < title.Length && !char.IsDigit(title[i])) i++;
            var j = i;
            while (j < title.Length && char.IsDigit(title[j])) j++;
            if (j > i && int.TryParse(title[i..j], out var t)) lv = t;
        }
        return lv;
    }

    /// <summary>Conditions d'offre (même sémantique que le serveur : les
    /// conditions inconnues passent).</summary>
    public static bool ConditionsPass(MissionTable.MissionDef m,
        int level, Func<int, bool> isDone)
    {
        foreach (var c in m.Conditions)
        {
            var a = c.Args;
            switch (c.Name)
            {
                case "If_FMS":
                    if (a.Count >= 2 && int.TryParse(a[0], out var id) &&
                        isDone(id) != (a[1] == "done"))
                        return false;
                    break;
                case "If_CharLevel":
                case "PlayerLevel":
                    if (a.Count >= 2 && a[0] == ">=" &&
                        int.TryParse(a[1], out var min) && level < min)
                        return false;
                    break;
            }
        }
        return true;
    }

    /// <summary>Une quête est « prenable » : ni en cours, niveau atteint,
    /// prérequis faits, et — pour une quête unique (limite 0) — pas déjà
    /// terminée (FMS 1/5 ; les quêtes à limite/quotidiennes restent
    /// reprenables).</summary>
    public static bool IsAvailable(MissionTable.MissionDef m, int level,
        Func<int, int> fmsOf)
    {
        var st = fmsOf(m.Id);
        if (st is 3 or 4) return false;
        if (m.CompleteLimit <= 0 && st is 1 or 5) return false;
        if (m.ReqLevel > level) return false;
        return ConditionsPass(m, level, id => fmsOf(id) is 1 or 5);
    }

    /// <summary>Titre de forme « Nv. 12 … » / « L12. … » (un nombre dans
    /// les 5 premiers caractères).</summary>
    public static bool HasLevelPrefix(string title)
    {
        var n = Math.Min(5, title.Length);
        for (var i = 0; i < n; i++)
            if (char.IsDigit(title[i])) return true;
        return false;
    }

    /// <summary>Quête répétable (limite &gt; 0, quotidienne ou répétable) :
    /// jamais rangée parmi les quêtes prises/terminées de l'arbre, elle
    /// reste dans « Quêtes oubliées » tant qu'elle est reprenable.</summary>
    public static bool IsRepeatable(MissionTable.MissionDef m) =>
        m.CompleteLimit > 0 ||
        KindOf(m.Group, m.Category, m.Flags) is QuestKind.Repeat
            or QuestKind.Daily;

    /// <summary>Quêtes manquées/oubliées : prenables, triées par niveau
    /// croissant puis id ; <paramref name="minLevel"/> écarte les trop
    /// basses (≥ level − fenêtre) et <paramref name="max"/> plafonne.
    /// </summary>
    public static List<(MissionTable.MissionDef Def, int Level, QuestKind Kind)>
        Missed(IEnumerable<MissionTable.MissionDef> all, int level,
            Func<int, int> fmsOf, Func<int, string?> titleOf,
            int minLevel = 1, int max = 200, int maxAbove = 2)
    {
        var res = new List<(MissionTable.MissionDef, int, QuestKind)>();
        foreach (var m in all)
        {
            var title = titleOf(m.Id);
            if (title == null) continue;       // pas de texte = pas une quête jouable
            // Quêtes jouables seulement : un PNJ donneur et un titre
            // « Nv. N … » (écarte tests, certificats, événements).
            if (m.GiverNpc == 0 || !HasLevelPrefix(title)) continue;
            if (!IsAvailable(m, level + maxAbove, fmsOf)) continue;
            var lv = LevelOf(m, title);
            if (lv < minLevel || lv > level + maxAbove) continue;
            res.Add((m, lv, KindOf(m.Group, m.Category, m.Flags)));
        }
        res.Sort((a, b) => a.Item2 != b.Item2
            ? a.Item2.CompareTo(b.Item2) : a.Item1.Id.CompareTo(b.Item1.Id));
        if (res.Count > max) res.RemoveRange(max, res.Count - max);
        return res;
    }
}
