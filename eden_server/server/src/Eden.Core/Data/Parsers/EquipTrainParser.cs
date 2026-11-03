using System.Collections.Generic;
using System.Globalization;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Table de progression « Mana Infusion » (entraînement d'équipement)
/// — miroir de <c>GameData::CEquipTrainQuery::Deserialize</c>
/// (zone/decompiled.c, map remplie par
/// <c>CLevelDBO::LoadEquipTrainExp</c> @0x83626a0).
///
/// <b>S_EquipTrain.ini</b> (|V.1|17|) — mapping colonnes → CEquipTrainExp :
/// <list type="bullet">
///   <item>[0] niveau d'entraînement u8 → +0x00 (clé de la query) ;</item>
///   <item>[1] EXP cumulée requise pour atteindre ce niveau — lue
///         i64 → +0x04 ; dans <c>GainEquipExp</c> @0x841b1d0 le seuil
///         du prochain palier est <c>row[trainLevel+1].exp</c> ;</item>
///   <item>[2-16] 15 coefficients f32 → +0x08..+0x40, un par type
///         d'attribut : 0=HP 1=MP 2=ATK 3=M-ATK 4=ACC 5=ATK SPD
///         6=CRIT 7=Cast SPD 8=M-CRIT 9=DEF 10=EVA 11=Parry
///         12=Block 13=G-Healing 14=P-Healing (ordre du switch de
///         <c>CItemFactory::UpdateCloneItemAddAttr</c> @0x8097d00).</item>
/// </list>
///
/// Subtilité native : la ligne de coefficients est indexée par
/// <c>itemdata+0x3e</c> (niveau requis de l'item — S_Item col14),
/// PAS par le niveau d'entraînement courant ; la même table sert
/// aussi de courbe d'EXP (indexée par train_level+1).
/// </summary>
public static class EquipTrainParser
{
    /// <summary>Nombre de types d'attributs entraînables — les 15
    /// branches du switch natif.</summary>
    public const int AttrTypeCount = 15;

    /// <summary>Une ligne de CEquipTrainExp natif.</summary>
    public sealed class EquipTrainRow
    {
        /// <summary>Niveau d'entraînement (clé, col 0 → +0x00).</summary>
        public int Level { get; set; }
        /// <summary>EXP cumulée requise pour ce niveau (col 1 → +0x04).</summary>
        public long Exp { get; set; }
        /// <summary>Les 15 coefficients par type (cols 2-16 → +0x08).</summary>
        public float[] Coefs { get; set; } = new float[AttrTypeCount];
    }

    /// <summary>Parse S_EquipTrain.ini — lignes indexées par niveau.</summary>
    public static List<EquipTrainRow> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var rows = new List<EquipTrainRow>(table.Rows.Count);
        foreach (var row in table.Rows)
        {
            if (row.Length < 2 + AttrTypeCount ||
                !int.TryParse(row[0], out var level))
                continue;
            var r = new EquipTrainRow
            {
                Level = level,
                // col1 : EXP cumulée du palier — valeur décimale dans
                // le fichier ("33.76"), tronquée comme le strtol natif.
                Exp = (long)GetFloat(row, 1),
            };
            for (var i = 0; i < AttrTypeCount; i++)
                r.Coefs[i] = GetFloat(row, 2 + i);
            rows.Add(r);
        }
        return rows;
    }

    private static float GetFloat(string[] row, int i)
        => i < row.Length && float.TryParse(row[i],
               NumberStyles.Float, CultureInfo.InvariantCulture,
               out var v) ? v : 0f;
}
