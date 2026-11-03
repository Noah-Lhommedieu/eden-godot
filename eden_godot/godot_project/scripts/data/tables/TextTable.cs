namespace EdenClient.Data.Tables;

/// <summary>
/// Chargeur générique des tables de textes <c>data/db/T_*.ini</c> —
/// libellés UI et dialogues localisés du client.
///
/// Format Count (voir <see cref="IniTable"/>) : header <c>N|</c>,
/// ligne 2 = index des colonnes, rows <c>id|champ1|champ2|…|</c> —
/// les champs texte peuvent contenir des CRLF (accumulés par IniTable)
/// et des codes <c>$N$</c> de couleur/format.
///
/// Toutes les T_* partagent la même structure id→cellules ; ce loader
/// unique couvre t_item, T_Spell, t_monster, t_npc, T_Class (cf.
/// <see cref="ClassTextTable"/> pour une vue typée), t_dialogue,
/// t_textindex, T_Achievement, t_mission… Le sens des colonnes varie
/// par fichier (ex. t_item : col1=nom, col2=description ; t_npc :
/// col1=nom, col2=titre).
///
/// Spécification serveur : <c>GameDataService.ParseSpellNames /
/// ParseEnchantDescriptions / ParseTextIndex</c>
/// (<c>$EDEN/server/.../Services/GameDataService.cs</c>).
/// </summary>
public static class TextTable
{
    /// <summary>Une ligne de texte localisé.</summary>
    public sealed class TextRow
    {
        /// <summary>Col 0 — id (jointure vers la table C_/c_ du même
        /// nom : t_item↔c_item, t_monster↔c_monster…).</summary>
        public int Id { get; init; }
        /// <summary>Cellules de la ligne (col0 incluse).</summary>
        public string[] Cells { get; init; } = Array.Empty<string>();
        /// <summary>Cellule i ou vide si hors bornes.</summary>
        public string Field(int i) =>
            i >= 0 && i < Cells.Length ? Cells[i] : string.Empty;
        /// <summary>Col 1 — nom/libellé court (convention T_*).</summary>
        public string Name => Field(1);
        /// <summary>Col 2 — description/titre (convention T_*).</summary>
        public string Description => Field(2);
    }

    /// <summary>Charge un T_*.ini → id → ligne. Les ids dupliqués
    /// conservent la dernière occurrence (comportement dictionnaire
    /// natif — certaines T_ ont des entrées « Testing ID » écrasées par
    /// des tables mall, cf. note T_ItemMall du serveur).</summary>
    public static Dictionary<int, TextRow> Load(string path)
    {
        var table = IniTable.ParseFile(path, EdenEncoding.Big5);
        var dict = new Dictionary<int, TextRow>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 2 || !int.TryParse(row[0], out var id))
                continue;
            dict[id] = new TextRow { Id = id, Cells = row };
        }
        return dict;
    }

    /// <summary>Variante id→nom (col 1 uniquement) — équivalent du
    /// ParseSpellNames serveur pour les cas où seul le libellé court
    /// sert (noms d'items/sorts/monstres).</summary>
    public static Dictionary<int, string> LoadNames(string path)
    {
        var dict = new Dictionary<int, string>();
        foreach (var kv in Load(path))
        {
            var name = kv.Value.Field(1);
            if (name.Length > 0 && !name.StartsWith('$'))
                dict[kv.Key] = name;
        }
        return dict;
    }
}
