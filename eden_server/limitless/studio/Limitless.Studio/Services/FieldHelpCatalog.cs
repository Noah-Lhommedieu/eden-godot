using System.Collections.Generic;

namespace Limitless.Studio.Services;

/// <summary>Catalogue de documentation complète par champ .ini —
/// la source du wiki affiché par la fenêtre « ? » du formulaire.
/// Indexé par (nom de fichier, index de colonne 0-basé). Le contenu
/// vient des commentaires de colonnes des parsers C# serveur
/// (`Eden.Core/Data/Parsers/*Parser.cs`, eux-mêmes prouvés par
/// désasm/captures), de la table native EquipType.ini, du dispatch
/// d'animations de `MotionParser`, des slots natifs de
/// `NativeEquipmentSlots`, des commandes d'effet de
/// `EnchantCommands`, et des valeurs réellement distribuées dans
/// `server_us/Data/db_decoded`. Les colonnes sans entrée dédiée
/// retombent sur le hint du schéma + la section « valeurs
/// observées » calculée en live.</summary>
public static partial class FieldHelpCatalog
{
    static FieldHelpCatalog()
    {
        RegisterItems();
        RegisterMonsters();
    }

    /// <summary>Documentation complète du champ `columnIndex` de
    /// `iniFile`, ou null si la colonne n'a pas d'entrée dédiée
    /// (le panneau affiche alors le hint + les valeurs observées).</summary>
    public static FieldHelpEntry? For(string iniFile, int columnIndex)
    {
        if (Items.TryGetValue(iniFile, out var table)
            && table.TryGetValue(columnIndex, out var entry))
            return entry;
        return null;
    }

    /// <summary>La table a-t-elle au moins une entrée ?</summary>
    public static bool HasCatalog(string iniFile) =>
        Items.ContainsKey(iniFile);

    private static FieldHelpEntry E(string purpose,
        IReadOnlyList<FieldValueCase>? values = null,
        string? valuesNote = null, string? recipe = null,
        params string[] seeAlso) =>
        new(purpose, values, valuesNote, recipe,
            seeAlso.Length > 0 ? seeAlso : null);

    /// <summary>Helper compact pour passer la liste « see also »
    /// en argument nommé (params accepte un tableau).</summary>
    private static string[] SA(params string[] files) => files;

    private static FieldValueCase V(string value, string meaning) =>
        new(value, meaning);

    /// <summary>Les entrées par fichier — chaque membre partiel
    /// porte la doc d'une table (Items.cs = S_Item, Monsters.cs =
    /// S_Monster).</summary>
    private static readonly Dictionary<string,
        Dictionary<int, FieldHelpEntry>> Items = new(
            System.StringComparer.OrdinalIgnoreCase);
}
