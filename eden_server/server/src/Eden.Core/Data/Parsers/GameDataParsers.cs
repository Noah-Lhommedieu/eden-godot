using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parsers pour les tables de drops et de progression.
///
/// S_ClassDropItem.ini (|V.3|85|): tables de loot par classe de monstre.
///   Colonnes repetees par groupe: [chance][itemId][itemName][qty]...
///   ex: 59013|100|33747|nom|1|...|100|33819|nom|1|...
///
/// S_DropItem.ini (|V.3|176|): tables de loot detaillees par monstre.
///
/// S_Talent.ini (|V.2|10|): arbres de talents de classe.
///   [0] Talent ID, [1] classe (Big5), [2..9] slots "spellId val1 v2 v3 v4 v5"
///
/// S_Title.ini (|V.3|6|): titres avec bonus de stats.
///   [0] ID, [1] nom, [2] type, [3] spell associe, [4] description bonus,
///   [5] condition d'obtention
///
/// S_Achievement.ini (|V.4|15|): 3844 succes.
///   [0] ID, [2] type, [3] code, [4] nom, [5-8] params, [14] description
///
/// S_Node.ini (|V.9|78|): 379 maps/zones.
///   [0] Node ID, [1] nom, [4-6] codes map (S001/S001M/S001S), [7-10] dims,
///   [11-18] limites zone, [19-22] palettes couleur, [23-25] sons BGM,
///   [31-34] codes zones voisines (N102, N201...), [35-42] timers/ratios,
///   [48] description, [49-66] POIs (coords + noms), [67] sous-titre
///
/// S_Transport.ini (|V.10|59|): 40 lignes de transport/fast travel.
///   [0] ID, [1] nom, [2-16] couts par niveau/palier, [18-19] flags,
///   [21-58] destinations/couts
/// </summary>
public static class GameDataParsers
{
    // ---------- Drops ----------

    public sealed class ClassDropEntry
    {
        public int Chance { get; set; }
        public int ItemId { get; set; }
        public string ItemName { get; set; } = string.Empty;
        public int Quantity { get; set; }
    }

    public sealed class ClassDropTable
    {
        public int Id { get; set; }                    // monstre/groupe id
        public List<ClassDropEntry> Entries { get; } = new();
    }

    /// <summary>S_ClassDropItem.ini — triplets/quadruplets repetes.</summary>
    public static List<ClassDropTable> ParseClassDropItem(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var tables = new List<ClassDropTable>();

        foreach (var row in table.Rows)
        {
            if (row.Length < 5 || !int.TryParse(row[0], out var id)) continue;
            var t = new ClassDropTable { Id = id };

            // Pattern: chance|itemId|name|qty repete tous les ~6-8 cols
            // col1=chance(100), col2=itemId, col3=name, col4=qty → puis col13=chance...
            // On scanne: chaque groupe commence par un chance (<=100 suivi d'un itemId)
            for (var i = 1; i + 3 < row.Length; i++)
            {
                if (!int.TryParse(row[i], out var chance) || chance <= 0 || chance > 100000) continue;
                if (!int.TryParse(row[i + 1], out var itemId) || itemId <= 0) continue;
                var name = row[i + 2];
                var qty = int.TryParse(row[i + 3], out var q) ? q : 1;
                // heuristique: un nom contient des lettres non-numeriques
                if (int.TryParse(name, out _)) continue;
                t.Entries.Add(new ClassDropEntry { Chance = chance, ItemId = itemId, ItemName = name, Quantity = qty });
                i += 3;
            }
            tables.Add(t);
        }
        return tables;
    }

    // ---------- Talents ----------

    public sealed class TalentSlot
    {
        public int SpellId { get; set; }
        public int[] Params { get; set; } = new int[5];
    }

    public sealed class TalentTree
    {
        public int Id { get; set; }
        public string ClassName { get; set; } = string.Empty; // Big5
        public List<TalentSlot> Slots { get; } = new();         // cols 2-9
    }

    /// <summary>S_Talent.ini — arbres de talents par classe.</summary>
    public static List<TalentTree> ParseTalents(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var trees = new List<TalentTree>();

        foreach (var row in table.Rows)
        {
            if (row.Length < 3 || !int.TryParse(row[0], out var id)) continue;
            var tree = new TalentTree { Id = id, ClassName = IniTable.GetStr(row, 1) };

            for (var c = 2; c <= 9 && c < row.Length; c++)
            {
                var parts = row[c].Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || !int.TryParse(parts[0], out var spellId)) continue;
                var slot = new TalentSlot { SpellId = spellId };
                for (var i = 0; i < 5 && i + 1 < parts.Length; i++)
                    int.TryParse(parts[i + 1], out slot.Params[i]);
                tree.Slots.Add(slot);
            }
            trees.Add(tree);
        }
        return trees;
    }

    // ---------- Titres ----------

    public sealed class TitleDef
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int SpellId { get; set; }
        public string BonusDescription { get; set; } = string.Empty;
        public string ObtainCondition { get; set; } = string.Empty;
    }

    public static List<TitleDef> ParseTitles(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var titles = new List<TitleDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 5 || !int.TryParse(row[0], out var id)) continue;
            titles.Add(new TitleDef
            {
                Id = id,
                Name = IniTable.GetStr(row, 1),
                Type = IniTable.GetStr(row, 2),
                SpellId = IniTable.GetInt(row, 3),
                BonusDescription = IniTable.GetStr(row, 4),
                ObtainCondition = IniTable.GetStr(row, 5),
            });
        }
        return titles;
    }

    // ---------- Achievements ----------

    /// <summary>Succès — layout <c>CAchievementData</c> natif
    /// (<c>GameData::CAchievementQuery::Deserialize</c> mission
    /// @0x082ae760, lecture positionnelle des 15 colonnes) :
    ///   col0  → +0x00 id
    ///   col1  → +0x04 NeedNum (0/vide → 1)
    ///   col2  → +0x08 index de tri
    ///   col3  → +0x0c code (M025, AC501…)
    ///   col4  → +0x10 nom
    ///   col5  → +0x14 type/groupe-cumul 1 (4 = complétion immédiate)
    ///   col6,7→ +0x18 = col6*100+col7 (groupe cumulatif combiné)
    ///   col8  → +0x1c points accordés à la complétion (s16)
    ///   col9  → +0x20 seuil de points déclenchant les récompenses
    ///   col10 → +0x24 appellation (titre) accordée
    ///   col11 → +0x28 item_id de récompense (mail système)
    ///   col12 → +0x54 align/prestige id, col13 → +0x58 exp
    ///   col13 → liste « ; » des succès-membres (groupes AC5xx)
    ///   col14 → description.
    /// </summary>
    public sealed class AchievementDef
    {
        public int Id { get; set; }
        /// <summary>Nombre de déclenchements requis pour compléter
        /// (col1, vide/0 → 1 natif).</summary>
        public int NeedNum { get; set; } = 1;
        public int SortIndex { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        /// <summary>Col5 : type du succès — aussi clé 1 de
        /// <c>CumulativeAchievement</c> (sVar2 natif) ; 4 = complétion
        /// immédiate dès le premier grant.</summary>
        public int Type { get; set; }
        /// <summary>Col6*100+col7 — clé 2 de CumulativeAchievement
        /// (sVar3 natif ; plages 101-108/201-202 → total type 2,
        /// 501-506 → total type 3 dans le paquet 480).</summary>
        public int GroupKey { get; set; }
        /// <summary>Col8 : points de succès accordés à la complétion
        /// (s16 natif).</summary>
        public int Point { get; set; }
        /// <summary>Col9 : seuil — les récompenses ne partent que si le
        /// total de points du succès franchit cette valeur
        /// (<c>old &lt; seuil &lt;= new</c> natif).</summary>
        public int RewardThreshold { get; set; }
        /// <summary>Col10 : id d'appellation accordée en récompense.</summary>
        public int AppellationId { get; set; }
        /// <summary>Col11 : item_id envoyé par mail de récompense
        /// (<c>CNE_ZMis_ZoneServerAchievementRewardMail</c> natif).</summary>
        public int ItemReward { get; set; }
        /// <summary>Col12 : id de prestige/align crédité.</summary>
        public int AlignId { get; set; }
        /// <summary>Col13 : exp de prestige créditée (le même champ
        /// porte la liste « ; » des membres pour les groupes AC5xx —
        /// <see cref="Members"/>).</summary>
        public int AlignExp { get; set; }
        /// <summary>Ids des succès-membres (col13 liste « ; ») — le
        /// succès-groupe est accordé quand tous sont complétés
        /// (<c>GetReachedAchievementGroupID</c> natif @0x080764a0).</summary>
        public int[] Members { get; set; } = Array.Empty<int>();
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>Parse S_Achievement.ini — mapping positionnel du
    /// deserializer natif (voir <see cref="AchievementDef"/>).</summary>
    public static List<AchievementDef> ParseAchievements(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<AchievementDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 5 || !int.TryParse(row[0], out var id)) continue;
            var a = new AchievementDef
            {
                Id = id,
                NeedNum = Math.Max(1, IniTable.GetInt(row, 1)),
                SortIndex = IniTable.GetInt(row, 2),
                Code = IniTable.GetStr(row, 3),
                Name = IniTable.GetStr(row, 4),
                Type = IniTable.GetInt(row, 5),
                GroupKey = IniTable.GetInt(row, 6) * 100
                    + IniTable.GetInt(row, 7),
                Point = IniTable.GetInt(row, 8),
                RewardThreshold = IniTable.GetInt(row, 9),
                AppellationId = IniTable.GetInt(row, 10),
                ItemReward = IniTable.GetInt(row, 11),
                AlignId = IniTable.GetInt(row, 12),
                AlignExp = IniTable.GetInt(row, 13),
                Description = IniTable.GetStr(row, 14),
            };
            // col13 : liste « ; » quand elle n'est pas un simple entier.
            var m = IniTable.GetStr(row, 13);
            if (m.Contains(';'))
                a.Members = m.Split(';',
                        StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => int.TryParse(s, out var v) ? v : 0)
                    .Where(v => v != 0).ToArray();
            list.Add(a);
        }
        return list;
    }

    // ---------- Nodes / Maps ----------

    public sealed class NodeDef
    {
        public int Id { get; set; }
        public int Type { get; set; }                           // col 3 : 1=map normale, >=4 instance
        public string Name { get; set; } = string.Empty;        // Big5
        public string MapCode { get; set; } = string.Empty;     // S001
        public string MapCodeM { get; set; } = string.Empty;    // S001M
        public string MapCodeS { get; set; } = string.Empty;    // S001S
        public int[] Dimensions { get; set; } = new int[4];     // cols 7-10
        public int[] Bounds { get; set; } = new int[8];         // cols 11-18
        public string[] NeighborCodes { get; set; } = new string[4]; // cols 31-34
        /// <summary>Description longue — <c>CNodeData+0x64</c>, lue
        /// juste après le dernier flag (ancre S + 44).</summary>
        public string Description { get; set; } = string.Empty;
        /// <summary>Sous-titre — <c>CNodeData+0x68</c>, premier token
        /// de la queue (après la liste des portails, ancre S + 45 +
        /// 2×paires).</summary>
        public string Subtitle { get; set; } = string.Empty;
        /// <summary>Niveau minimum d'entrée (col 2 "LV 9~14" → 9 ;
        /// 0 = pas de restriction). Le natif lit le niveau requis dans
        /// CNodeData+0x78 — la seule colonne qui porte un niveau est la
        /// plage "LV min~max" ; les scripts [event] Event_If_Level
        /// restent le check prioritaire (désasm CheckNodeExit).</summary>
        public int MinLevel { get; set; }
        /// <summary>Nombre max d'entrées journalières —
        /// <c>CNodeData+0x8a</c> natif lu par
        /// <c>CCharacter::CheckNodeTimes</c> zone @0x8409fe0, en queue
        /// de ligne (ancre S + 45 + 2×paires + 7 ; <c>Deserialize</c>
        /// le lit en s16 après +0x88, version ≥ 5). 0 partout dans les
        /// données V.9 → pas de limite.</summary>
        public int MaxEntryTimes { get; set; }
        /// <summary>Taille d'équipe exigée par le tableau de
        /// recrutement d'instance — <c>CNodeData+0x74</c> natif lu par
        /// <c>CTeamManager::CheckMakeTeam</c> mission @0x080c8490 et
        /// <c>CCharacter::CheckNodeLimit</c> zone @0x0840c720 (0 = pas
        /// de limite), en queue de ligne (queue+1).</summary>
        public int CharLimit { get; set; }
        /// <summary>Niveau requis pour la file de recrutement —
        /// <c>CNodeData+0x78</c> natif (<c>RecruitInstanceAdd</c>
        /// mission @0x080ce5a0 : <c>level &lt; node+0x78</c> → refus
        /// 0x1169), en queue de ligne (queue+2, lu en s16 par
        /// <c>strtol</c> direct sur le curseur).</summary>
        public int RecruitMinLevel { get; set; }
        /// <summary>Flag familiers — col 46 partie entière. Natif :
        /// <c>CNodesQuery::Deserialize</c> @0x8590490 lit un int et
        /// pose <c>CNodeData+0x60</c> bit4 si ∈{1,3}, bit5 si ∈{2,3} ;
        /// <c>CNodeDBO::GetNodesForServer</c> @0x836bf00 les replie
        /// dans le param flags de <c>CNodeTemplate</c> (cascade
        /// uVar26) → <c>template+0x10</c> bit 0x20 (« invocation/combat
        /// permis ») quand ∈{1,3}, bit 0x40 (« prière/intégration
        /// permise ») quand ∈{2,3}. Lu par
        /// <c>CNE_CZ_ZoneServerElfWork::Execute</c> @0x824b0b0
        /// (refus 8350/8351/8352) et
        /// <c>CKFElfManager::CallElfBackVer2</c> @0x80731c0.
        /// Attention : 4 colonnes int (bytes CNodeData+0x1c..0x1f) sont
        /// consommées par des <c>strtol</c> directs sur le curseur du
        /// flux — sans <c>operator&gt;&gt;</c> — juste après col 6, ce
        /// qui décale de +4 tous les index suivants, ET l'en-tête de
        /// ligne a une longueur variable (colonnes optionnelles avant
        /// le code map) — la position fiable est
        /// <c>index du code Sxxx + 42</c> (vérifié : les 379 lignes
        /// donnent alors des valeurs ∈ {0,1,2,3} uniquement). Gate de
        /// version : la lecture n'a lieu que si l'en-tête du fichier
        /// est V.9 (<c>CheckVersion</c> → this+0x24 &gt; 8).</summary>
        public int PetFlag { get; set; }
    }

    public static List<NodeDef> ParseNodes(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var nodes = new List<NodeDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 10 || !int.TryParse(row[0], out var id)) continue;
            // L'en-tête de ligne a une longueur variable (colonnes
            // optionnelles name2 / « LV x~y » avant le code map) —
            // toutes les positions après le type sont relatives à
            // l'ancre du premier code Sxxx (s). Natif :
            // CNodesQuery::Deserialize @0x8590490 — s+42 = pet flag,
            // s+44 = description (+0x64), s+45.. = ≤9 paires portail
            // (« x;y » + nom, arrêt au premier token vide), puis la
            // queue : t+0=+0x68 sous-titre, t+1=+0x74 char_limit,
            // t+2=+0x78 recruit_level, t+7=+0x8a max_times.
            var s = NodeAnchor(row);
            var pairsEnd = s < 0 ? -1 : NodePairsEnd(row, s + 45);
            var n = new NodeDef
            {
                Id = id,
                // Le type (CNodeData+0xc) précède immédiatement le code
                // map : s-1 ('3'@3 pour S001, '1'@4 pour S019…).
                Type = s > 0 ? IniTable.GetInt(row, s - 1)
                             : IniTable.GetInt(row, 3),
                Name = IniTable.GetStr(row, 1),
                MapCode = s >= 0 ? IniTable.GetStr(row, s) : "",
                MapCodeM = s >= 0 ? IniTable.GetStr(row, s + 1) : "",
                MapCodeS = s >= 0 ? IniTable.GetStr(row, s + 2) : "",
                Description = s >= 0 ? IniTable.GetStr(row, s + 44) : "",
                Subtitle = pairsEnd >= 0
                    ? IniTable.GetStr(row, pairsEnd) : "",
                CharLimit = pairsEnd >= 0
                    ? NodeIntPrefix(row, pairsEnd + 1) : 0,
                RecruitMinLevel = pairsEnd >= 0
                    ? NodeIntPrefix(row, pairsEnd + 2) : 0,
                MaxEntryTimes = pairsEnd >= 0
                    ? NodeIntPrefix(row, pairsEnd + 7) : 0,
                PetFlag = s >= 0 ? NodeIntPrefix(row, s + 42) : 0,
            };
            for (var i = 0; i < 4; i++) n.Dimensions[i] = IniTable.GetInt(row, 7 + i);
            for (var i = 0; i < 8; i++) n.Bounds[i] = IniTable.GetInt(row, 11 + i);
            for (var i = 0; i < 4; i++) n.NeighborCodes[i] = IniTable.GetStr(row, 31 + i);
            // « LV 9~14 » / « LV50 » — niveau min d'entrée du node ;
            // la colonne flotte entre 2 et s-1 selon les lignes.
            var lvLimit = s > 0 ? s - 1 : 4;
            for (var i = 2; i <= lvLimit && i < row.Length; i++)
            {
                var lv = row[i];
                if (!lv.StartsWith("LV", StringComparison
                        .OrdinalIgnoreCase)) continue;
                var lvDigits = new string(lv.SkipWhile(
                    c => !char.IsDigit(c)).TakeWhile(char.IsDigit)
                    .ToArray());
                if (lvDigits.Length > 0) n.MinLevel = int.Parse(lvDigits);
                break;
            }
            nodes.Add(n);
        }
        return nodes;
    }

    /// <summary>Ancre d'une ligne S_Node : index du premier code map
    /// <c>Sxxx</c> (sans suffixe) dans les colonnes 3-9 ; les
    /// sous-nodes qui n'ont que <c>SxxxM</c>/<c>SxxxS</c> reculent
    /// l'ancre d'une colonne. -1 si aucun code trouvé.</summary>
    private static int NodeAnchor(string[] row)
    {
        for (var i = 3; i <= 9 && i < row.Length; i++)
            if (System.Text.RegularExpressions.Regex.IsMatch(row[i],
                    @"^S\d+$"))
                return i;
        for (var i = 3; i <= 9 && i < row.Length; i++)
            if (System.Text.RegularExpressions.Regex.IsMatch(row[i],
                    @"^S\d+[MS]$"))
                return i - 1;
        return -1;
    }

    /// <summary>Fin de la liste des portails — jusqu'à 9 paires de
    /// tokens (« x;y » + nom), arrêt au premier token vide ; retourne
    /// l'index du premier token de queue. Natif : la boucle lit
    /// <c>(str, str)</c> tant que le premier n'est pas vide.</summary>
    private static int NodePairsEnd(string[] row, int start)
    {
        var p = start;
        for (var i = 0; i < 9 && p < row.Length && row[p].Length > 0;
             i++, p += 2) { }
        return p;
    }

    /// <summary>Lit le préfixe entier d'un champ — même sémantique
    /// que l'extraction <c>istream &gt;&gt; u32</c> native : les
    /// chiffres de tête puis arrêt au premier non-digit ('2.35'→2,
    /// '0.59'→0, ''→0).</summary>
    internal static int NodeIntPrefix(string[] row, int col)
    {
        if (col >= row.Length) return 0;
        var s = row[col];
        var i = 0;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return i > 0 && int.TryParse(s.AsSpan(0, i), out var v) ? v : 0;
    }
}
