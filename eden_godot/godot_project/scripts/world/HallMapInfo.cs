using System.Collections.Generic;

namespace EdenClient.World;

/// <summary>Bâtiments de la ville de guilde courante (nom, niveau,
/// position Eden) — alimentés par <c>FamilyHallBuildings.Rebuild</c>,
/// lus par la minicarte et la carte régionale.</summary>
public static class HallMapInfo
{
    public static readonly List<(string Name, int Level, float X, float Y)>
        Buildings = new();

    /// <summary>Incrémenté à chaque reconstruction.</summary>
    public static int Version { get; private set; }

    public static int NodeId { get; private set; }

    public static void Set(int nodeId,
        IEnumerable<(string Name, int Level, float X, float Y)> items)
    {
        Buildings.Clear();
        Buildings.AddRange(items);
        NodeId = nodeId;
        Version++;
    }

    /// <summary>Nom FR du type de bâtiment (S_Buliding col 3).</summary>
    public static string TypeName(int type) => type switch
    {
        1 => "Manoir", 2 => "Hôtel de brique", 4 => "Ferme",
        5 => "Mine", 6 => "Autel", 7 => "Labo d'alchimie",
        8 => "Fonderie", 9 => "Mare magique", 10 => "Marché",
        _ => "Bâtiment",
    };
}
