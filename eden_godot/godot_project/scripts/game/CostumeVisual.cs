namespace EdenClient.Game;

/// <summary>
/// A6.6 — fusion « tenue portée + pages costume » pour le rendu du
/// corps. Les pages costume (conteneurs d'équipement 2/3/4, locs 1-5 =
/// mêmes locs que les armures) sont choisies par poids d'armure
/// (<c>kind % 3</c> : 0→cont2, 2→cont3, 1→cont4 — serveur
/// <c>ResolveEquipLocAsync</c>). Une pièce costume remplace
/// VISUELLEMENT la pièce portée du même loc ; armes/off-hand (locs ≥ 6)
/// ne sont jamais touchées. Les stats ne passent pas ici (zéro stat
/// propre côté costume).
/// ⚠️ Inférence : le poids de la page appliquée = poids de la pièce
/// portée au loc (à défaut, celui du torse loc 2) — non prouvé au
/// désasm natif.
/// </summary>
public static class CostumeVisual
{
    /// <summary>Locs de pièces d'armure remplaçables par un costume.</summary>
    public const int FirstLoc = 1, LastLoc = 5;

    /// <summary>Conteneur de page pour un kind d'armure (kind % 3).</summary>
    public static int ContainerForKind(int kind) => (kind % 3) switch
    {
        0 => 2,
        2 => 3,
        _ => 4,
    };

    /// <summary>Rangée du profil natif pour l'archétype c_class d'une
    /// classe (cellule certsBase−10 — mesuré sur fr-full : 1 = 鎧甲/
    /// heavy pour 戰士/騎士/聖騎士, 2 = 皮甲/light pour
    /// 獵人/盜賊/吟遊詩人/工程師/武鬥家/舞者/遊俠, 3 = 布衣/cloth pour
    /// 魔術師/神官/幻術師/巫醫). La licence de la classe détermine la
    /// rangée « Current Gear » du EquipmentWnd ; les autres affichent
    /// leur page d'ensemble sauvegardée. Mapping rangée ↔ conteneur
    /// identique à <see cref="ContainerForKind"/> (heavy→4, light→3,
    /// cloth→2). Archétype inconnu (0) → repli Cloth (1re rangée
    /// native).</summary>
    public static (string Row, int Container) RowForArchetype(
        int archetype) => archetype switch
    {
        1 => ("Armor", 4),
        2 => ("Leather", 3),
        _ => ("Cloth", 2),
    };

    /// <param name="worn">loc → itemId tenu (conteneur 0).</param>
    /// <param name="costumeAt">(conteneur, loc) → itemId costume (0 = vide).</param>
    /// <param name="kindOf">itemId → kind c_item (-1 si inconnu).</param>
    public static IReadOnlyDictionary<int, int> Merge(
        IReadOnlyDictionary<int, int> worn,
        Func<int, int, int> costumeAt, Func<int, int> kindOf)
    {
        var result = new Dictionary<int, int>(worn);
        var bodyKind = worn.TryGetValue(2, out var body) ? kindOf(body) : -1;
        for (var loc = FirstLoc; loc <= LastLoc; loc++)
        {
            var kind = worn.TryGetValue(loc, out var w) ? kindOf(w) : -1;
            if (kind < 0) kind = bodyKind;
            if (kind < 0) continue;
            var cost = costumeAt(ContainerForKind(kind), loc);
            if (cost != 0) result[loc] = cost;
        }
        return result;
    }
}
