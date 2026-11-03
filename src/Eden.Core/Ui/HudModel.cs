using System.Globalization;

namespace EdenClient.Ui;

/// <summary>Constantes vitales du joueur affichées par le HUD.</summary>
public sealed record HudVitals(string Name, int Level, int JobLevel,
    long Hp, long HpMax, long Mp, long MpMax, long Exp, long ExpMax,
    long Soul = 0, long SoulMax = 0);

/// <summary>Cible sélectionnée (monstre ou joueur).</summary>
public sealed record HudTarget(string Name, int Level, long Hp, long HpMax,
    long Mp, long MpMax, bool IsMonster, int Stars = 0);

/// <summary>Case de barre rapide (icône = nom de région d'atlas).</summary>
public sealed record HudSlot(int Bar, int Index, string? Icon, int Count,
    float Cooldown01);

/// <summary>Buff (<paramref name="Debuff"/> = nerf) avec durée restante.</summary>
public sealed record HudBuff(string Name, string? Icon, int SecondsLeft,
    bool Debuff);

/// <summary>Ligne de suivi de quête.</summary>
public sealed record HudQuestLine(string Title, string Objective);

/// <summary>Ligne de chat.</summary>
public sealed record HudChatLine(string Channel, string Sender, string Text);

/// <summary>
/// Source de données du HUD — interface neutre : le réseau
/// (<c>EdenSession</c>) l'implémentera plus tard ; <see cref="MockHudData"/>
/// sert aux vérifs visuelles/tests. Aucune dépendance moteur.
/// </summary>
public interface IHudData
{
    HudVitals? Vitals { get; }
    HudTarget? Target { get; }
    string ZoneName { get; }
    /// <summary>Position joueur (coords Eden x, y) pour la minimap.</summary>
    (float X, float Y) Position { get; }
    IReadOnlyList<HudSlot> Hotbar { get; }
    IReadOnlyList<HudBuff> Buffs { get; }
    IReadOnlyList<HudQuestLine> Quests { get; }
    IReadOnlyList<HudChatLine> Chat { get; }
    /// <summary>Texte du bandeau de zone (null = masqué).</summary>
    string? Banner { get; }
    /// <summary>Incrémenté à chaque changement (le moteur repolle).</summary>
    int Version { get; }
}

/// <summary>Données factices pour tests et captures.</summary>
public sealed class MockHudData : IHudData
{
    public HudVitals? Vitals { get; set; } = new("Noah", 100, 13,
        14607, 14690, 8200, 9000, 5_400_000, 8_000_000, 120, 500);
    public HudTarget? Target { get; set; } = new("Slime", 12, 350, 500,
        0, 0, true, 2);
    public string ZoneName { get; set; } = "Aven";
    public (float X, float Y) Position { get; set; } = (1234f, 987f);
    public IReadOnlyList<HudSlot> Hotbar { get; set; } = new[]
    {
        new HudSlot(1, 1, null, 0, 0f), new HudSlot(1, 2, null, 5, 0.5f),
    };
    public IReadOnlyList<HudBuff> Buffs { get; set; } = new[]
    {
        new HudBuff("Bénédiction", null, 90, false),
        new HudBuff("Poison", null, 12, true),
    };
    public IReadOnlyList<HudQuestLine> Quests { get; set; } = new[]
    {
        new HudQuestLine("Les slimes d'Aven", "Tuer des Slimes (3/10)"),
    };
    public IReadOnlyList<HudChatLine> Chat { get; set; } = new[]
    {
        new HudChatLine("Système", "", "Bienvenue sur Eden Eternal !"),
        new HudChatLine("Monde", "Alice", "Salut tout le monde"),
    };
    public string? Banner { get; set; } = "Aven";
    public int Version { get; set; } = 1;
}

/// <summary>Une valeur à écrire dans un nœud nommé d'un layout.</summary>
public readonly record struct HudNodeValue(string Node, string? Text = null,
    float? Progress = null, bool? Visible = null);

/// <summary>Panneaux HUD P0 → layout CEGUI + parent par défaut.</summary>
public sealed record HudPanelDef(string Id, string Layout, float AnchorX,
    float AnchorY, float OffX, float OffY);

/// <summary>
/// Table des panneaux P0 et calcul pur des valeurs de nœuds
/// (<see cref="Compute"/>) à partir d'un <see cref="IHudData"/>.
/// Les noms de nœuds sont ceux des layouts natifs (suffixe après
/// <c>Root/&lt;Fenêtre&gt;/</c>).
/// </summary>
public static class HudBinder
{
    public static readonly HudPanelDef[] Panels =
    {
        new("Vitals", "MiniInfoWnd", 0f, 0f, 8, 8),
        new("Target", "TargetInfoWnd", 0.5f, 0f, -100, 8),
        new("Minimap", "RadarUI_Ver2", 1f, 0f, -190, 8),
        new("Quests", "QuestTraceWnd", 1f, 0.35f, -230, 0),
        new("Chat", "ChannelWndV2", 0f, 1f, 8, -250),
        new("Hotbar", "FunctionWnd", 0.5f, 1f, -240, -110),
        new("ExpBar", "FucntionBarWnd", 0.5f, 1f, -240, -50),
        new("Buffs", "BuffWnd", 1f, 0f, -300, 200),
    };

    /// <summary>Nom complet d'un nœud dans l'arbre <see cref="UiNodeSpec"/>
    /// (le builder remplace « / » par « _ ») ; <paramref name="node"/> vide
    /// = la racine.</summary>
    public static string FullName(string rootName, string node) =>
        node.Length == 0 ? rootName
            : rootName + "_" + node.Replace('/', '_');

    /// <summary>Format CEGUI des barres : « cur/max ».</summary>
    public static string Bar(long cur, long max) =>
        string.Create(CultureInfo.InvariantCulture, $"{cur}/{max}");

    /// <summary>Fraction 0..1 bornée (max 0 → 0).</summary>
    public static float Fraction(long cur, long max) =>
        max <= 0 ? 0f : Math.Clamp((float)cur / max, 0f, 1f);

    /// <summary>Durée « 1:30 » / « 12s ».</summary>
    public static string Duration(int sec) =>
        sec >= 60 ? $"{sec / 60}:{sec % 60:00}" : $"{sec}s";

    public static IEnumerable<HudNodeValue> Compute(string panelId,
        IHudData d)
    {
        switch (panelId)
        {
            case "Vitals" when d.Vitals is { } v:
                yield return new("backgrand/textBorder/NameText", v.Name);
                yield return new("backgrand/textBorder/LVText",
                    "Lv " + v.Level);
                yield return new("backgrand/textBorder/JobLvText",
                    v.JobLevel.ToString(CultureInfo.InvariantCulture));
                yield return new("backgrand/Hpborder/HpBar", null,
                    Fraction(v.Hp, v.HpMax));
                yield return new("backgrand/Hpborder/HpBar/text",
                    Bar(v.Hp, v.HpMax));
                yield return new("backgrand/Hpborder/MpBar", null,
                    Fraction(v.Mp, v.MpMax));
                yield return new("backgrand/Hpborder/MpBar/text",
                    Bar(v.Mp, v.MpMax));
                break;
            case "Target":
                if (d.Target is not { } t)
                {
                    yield return new("", null, null, false);
                    break;
                }
                yield return new("", null, null, true);
                yield return new("backgrand/textBorder/NameText", t.Name);
                yield return new("backgrand/textBorder/LVText",
                    "Lv " + t.Level);
                yield return new("backgrand/hpBorder/HpBar", null,
                    Fraction(t.Hp, t.HpMax));
                yield return new("backgrand/hpBorder/HpBar/text",
                    Bar(t.Hp, t.HpMax));
                yield return new("backgrand/hpBorder/MpBar", null,
                    Fraction(t.Mp, t.MpMax));
                for (var i = 1; i <= 5; i++)
                    yield return new(
                        $"backgrand/textBorder/MonsterStartBorder/star{i}",
                        null, null, i <= t.Stars);
                break;
            case "Minimap":
                yield return new("BackGrand/TitleText", d.ZoneName);
                yield return new("BackGrand/CoordText",
                    string.Create(CultureInfo.InvariantCulture,
                        $"X:{(int)d.Position.X},Y:{(int)d.Position.Y}"));
                break;
            case "ExpBar" when d.Vitals is { } e:
                yield return new("expBackGrand/bar", null,
                    Fraction(e.Exp, e.ExpMax));
                yield return new("expBackGrand/bar/text",
                    string.Create(CultureInfo.InvariantCulture,
                        $"{Fraction(e.Exp, e.ExpMax) * 100:0.00}%"));
                yield return new("SoulBackGrand/bar", null,
                    Fraction(e.Soul, e.SoulMax));
                yield return new("SoulBackGrand/bar/text",
                    Bar(e.Soul, e.SoulMax));
                break;
            case "Buffs":
                var normal = d.Buffs.Where(b => !b.Debuff).ToList();
                for (var i = 0; i < 15; i++)
                {
                    var on = i < normal.Count;
                    yield return new($"Buff{i + 1}", null, null, on);
                    if (on)
                        yield return new($"Buff{i + 1}/Text",
                            Duration(normal[i].SecondsLeft));
                }
                break;
        }
    }
}
