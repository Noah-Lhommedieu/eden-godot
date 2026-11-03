using System.Collections.Generic;
using System.Globalization;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Tables natives des familiers de combat ("elf pets" / partners) —
/// miroir de <c>GameData::CPartnerQuery::Deserialize</c> et
/// <c>CPartnerGrowQuery::Deserialize</c> (zone/decompiled.c @0x86192c0).
///
/// <b>S_Partner.ini</b> (|V.2|30|) — mapping colonnes → CPartnerData :
/// <list type="bullet">
///   <item>[0] item id (clé — le partner est indexé par l'id de
///         l'item "œuf", ex. 23993 = Combat Bunny Sprite) ;</item>
///   <item>[1] code icône (I00948) ;</item>
///   <item>[2] code modèle "M######" → +0x8 (le natif fait
///         strtol(colonne+1) — on stocke l'entier, le modèle
///         s'écrit "m%06d" dans BornElfPet) ;</item>
///   <item>[3] nom — le natif découpe par '\n' et garde le dernier
///         token (variantes de nom empilées) ;</item>
///   <item>[4] consommé mais non stocké (local de la Deserialize) ;</item>
///   <item>[5] type de croissance → +0x14 : pilote
///         <c>RandomGrowInfo</c> (0→1 pt, 2→3, 3→6, 4→5, 5→7 pts
///         répartis aléatoirement sur les 4 stats, cap 9/stat) ;</item>
///   <item>[6] chaîne optionnelle → flag +0x64 (non vide) ;</item>
///   <item>[7-10] stats de croissance (attack/hit/critical/atkspd)
///         stockées 1-based → le natif soustrait 1 ;</item>
///   <item>[11-12] champs +0x28/+0x2c (réservés natif) ;</item>
///   <item>[13-17] sorts de bataille (vecteur +0x30, 5 slots) ;</item>
///   <item>[18-22] sorts intégrés (vecteur +0x3c, 5 slots) ;</item>
///   <item>[23-28] champs +0x48..+0x5c (loterie/mix — non utilisés
///         pour l'invocation simple) ;</item>
///   <item>[29] HP max de base → +0x60 (CKFElf::GetMaxHp / +0x20).</item>
/// </list>
///
/// <b>S_PartnerGrow.ini</b> (|V.2|41|) — clé = niveau du pet, 40
/// valeurs : 4 groupes de 10 indexées par les stats de croissance
/// (désasm CKFElf @0x8453dxx) :
///   attack → cols 1-10 (+0x4 + grow[0]*4),
///   hit    → cols 11-20 (+0x2c + grow[1]*4),
///   crit   → cols 21-30 (+0x54 + grow[2]*4),
///   atkspd → cols 31-40 (+0x7c + grow[3]*4).
/// </summary>
public static class PartnerParser
{
    /// <summary>Définition d'un partner (CPartnerData natif).</summary>
    public sealed class PartnerDef
    {
        /// <summary>Id de l'item "œuf" (col 0 — clé de la query).</summary>
        public int ItemId { get; set; }
        /// <summary>Code icône I##### (col 1).</summary>
        public string Icon { get; set; } = string.Empty;
        /// <summary>Modèle numérique (col 2 "M279010" → 279010) —
        /// s'écrit "m%06d" côté visuel (BornElfPet natif).</summary>
        public int ModelId { get; set; }
        /// <summary>Code modèle formaté natif "m######".</summary>
        public string ModelCode => $"m{ModelId:D6}";
        /// <summary>Nom affiché — dernier token '\n' comme
        /// lapis::StrToken (col 3).</summary>
        public string Name { get; set; } = string.Empty;
        /// <summary>Type de croissance (col 5 → CPartnerData+0x14) —
        /// argument de RandomGrowInfo quand les stats sont à 0.</summary>
        public int GrowType { get; set; }
        /// <summary>Stats de croissance attack/hit/crit/atkspd
        /// (cols 7-10, -1 natif appliqué — valeurs 0..9 indexant
        /// S_PartnerGrow).</summary>
        public int[] Grow { get; } = new int[4];
        /// <summary>Sorts de bataille (cols 13-17 → CKFElf+0x40).</summary>
        public int[] BattleSpells { get; } = new int[5];
        /// <summary>Sorts intégrés (cols 18-22 → CKFElf+0x4c).</summary>
        public int[] IntegrateSpells { get; } = new int[5];
        /// <summary>HP max (col 29 → CPartnerData+0x60 →
        /// CKFElf+0x20).</summary>
        public int MaxHp { get; set; }
    }

    /// <summary>Ligne de croissance par niveau (CPartnerGrowData) —
    /// 40 valeurs : [attack×10][hit×10][crit×10][atkspd×10].</summary>
    public sealed class GrowDef
    {
        /// <summary>Niveau du pet (clé de la table).</summary>
        public int Level { get; set; }
        /// <summary>40 valeurs groupées par stat (10 index chacune).</summary>
        public int[] Values { get; } = new int[40];

        /// <summary>Valeur de la stat <paramref name="statIndex"/>
        /// (0=attack, 1=hit, 2=crit, 3=atkspd) à l'index de
        /// croissance donné — formule native +0x4/+0x2c/+0x54/+0x7c
        /// + grow[n]*4. Index hors borne → 0.</summary>
        public int Stat(int statIndex, int growIndex)
        {
            var baseIdx = statIndex * 10 + growIndex;
            return growIndex >= 0 && baseIdx < Values.Length
                ? Values[baseIdx] : 0;
        }
    }

    /// <summary>Parse S_Partner.ini → partners indexés par item id.</summary>
    public static Dictionary<int, PartnerDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var defs = new Dictionary<int, PartnerDef>();
        foreach (var row in table.Rows)
        {
            var def = new PartnerDef
            {
                ItemId = IniTable.GetInt(row, 0),
                Icon = IniTable.GetStr(row, 1),
                ModelId = ParseModelId(IniTable.GetStr(row, 2)),
                // StrToken('\n') natif → dernier token du nom.
                Name = LastToken(IniTable.GetStr(row, 3)),
                GrowType = IniTable.GetInt(row, 5),
                MaxHp = IniTable.GetInt(row, 29),
            };
            for (var i = 0; i < 4; i++)
            {
                // Stats 1-based → -1 natif (vide → 0 sans -1).
                var raw = IniTable.GetStr(row, 7 + i);
                def.Grow[i] = raw.Length == 0 ? 0
                    : int.Parse(raw, CultureInfo.InvariantCulture) - 1;
            }
            for (var i = 0; i < 5; i++)
            {
                def.BattleSpells[i] = IniTable.GetInt(row, 13 + i);
                def.IntegrateSpells[i] = IniTable.GetInt(row, 18 + i);
            }
            if (def.ItemId != 0) defs[def.ItemId] = def;
        }
        return defs;
    }

    /// <summary>Parse S_PartnerGrow.ini → croissances indexées par
    /// niveau (clé col 0 — AddElf natif utilise
    /// min(niveau item, niveau perso)).</summary>
    public static Dictionary<int, GrowDef> ParseGrow(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var defs = new Dictionary<int, GrowDef>();
        foreach (var row in table.Rows)
        {
            var level = IniTable.GetInt(row, 0);
            var def = new GrowDef { Level = level };
            for (var i = 0; i < 40; i++)
                def.Values[i] = IniTable.GetInt(row, 1 + i);
            if (level != 0) defs[level] = def;
        }
        return defs;
    }

    /// <summary>"M279010" → 279010 (strtol(col+1) natif).</summary>
    private static int ParseModelId(string code)
    {
        if (code.Length > 1 && (code[0] is 'M' or 'm') &&
            int.TryParse(code[1..], NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var v))
            return v;
        return int.TryParse(code, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out v) ? v : 0;
    }

    /// <summary>Dernier token séparé par '\n' — lapis::StrToken
    /// garde le dernier élément du champ nom.</summary>
    private static string LastToken(string name)
    {
        var idx = name.LastIndexOf('\n');
        return idx >= 0 ? name[(idx + 1)..] : name;
    }
}
