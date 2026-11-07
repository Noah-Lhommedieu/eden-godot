using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

public static class ProgressionParsers
{
    // ---------- S_Level.ini (|V.8|27|) ----------

    /// <summary>
    /// S_Level.ini — courbe d'XP et stats par niveau (1-101).
    /// Mapping prouvé par CLevelQuery::Deserialize @0x85fed60
    /// (lecture séquentielle des tokens) :
    ///   col0→level, col1→+4(exp), col2→+0xc(HP base),
    ///   col3→+0x10(MP), col4→+0x14, col5→+0x18(Str),
    ///   col6→+0x1c(Dex/Agi), col7→+0x20(Int), col8→+0x24(Spr/Wis),
    ///   col9→+0x28(Luk/Lck). Les 5 stats sont égales par niveau
    ///   (lv1=10, lv7=17) — la différenciation vient des % classe.
    /// </summary>
    public sealed class LevelRow
    {
        public int Level { get; set; }
        public long ExpNeeded { get; set; }
        public int MaxHp { get; set; }
        public int MaxMp { get; set; }
        /// <summary>Défense naturelle du niveau (col 4 → CLevelData
        /// +0x14 → char+0x370, ajoutée à l'équipement dans
        /// `CCharFormula::PhysicalDefence` @0x83f55c0). 359@lv1,
        /// 834@lv100 — vérifié données.</summary>
        public int NaturalDef { get; set; }
        public int Str { get; set; }
        public int Con { get; set; }
        public int Int { get; set; }
        public int Wis { get; set; }
        public int Agi { get; set; }
        /// <summary>LUK natif (CLevelData+0x28, col9) — même valeur
        /// que les autres stats par niveau.</summary>
        public int Lck { get; set; }
        public int AttackMin { get; set; }
        public int AttackMax { get; set; }
        public int DefenseMin { get; set; }
        public int DefenseMax { get; set; }
        public int Tier { get; set; }
        /// <summary>col 10 (0-idx) — XP de classe requise pour passer
        /// au niveau de classe suivant. Preuve : log natif
        /// `LoadClassLevelExp` (System1011 : « class level [1] exp
        /// [76] [2]=78 [3]=82 ») = champ $11 du fichier = index 10 ;
        /// l'index 11 (2000/7000/24000…) est une AUTRE courbe.</summary>
        public int ClassExpNeeded { get; set; }
        /// <summary>Seuil d'EXP du familier par niveau (col 24
        /// 0-indexée → CLevelData+0x64) — vecteur `CLevelDBO::
        /// LoadPetExp` natif (Ins+0x3b4), consommé par
        /// `CKFElf::GainExp` @0x8456ed0.</summary>
        public int PetExp { get; set; }
        /// <summary>Seuil d'EXP de famille pour passer au niveau
        /// suivant (col 11 0-indexée → CLevelData+0x30) — courbe
        /// 2000/7000/24000… lue par `CFamily::GainExp` mission
        /// @0x81beec0 (level-up seulement pour level &lt; 3, sinon
        /// cap à la valeur).</summary>
        public int FamilyExpNeeded { get; set; }
        /// <summary>Fonds de guilde requis pour atteindre ce niveau (col 18
        /// → CLevelData+0x4c) : 3 400 000 au niv. 4 … 560 000 000 au 12.</summary>
        public int FamilyFundNeeded { get; set; }
        /// <summary>Seuils d'EXP des bâtiments de production par type
        /// de station (cols 12-17 → CLevelData+0x34..+0x48), indexés
        /// par station-0x18 (ferme 24→[0], mine 25→[1], autel 26→[2],
        /// alchimie 27→[3], fonderie 28→[4], mare 29→[5]). Consommés
        /// par `CFamily::UpdateBuildingExp`/`CheckBuildingLevelUp`
        /// (mission) — identiques pour les 6 types en EDENFR.</summary>
        public int[] BuildingExps { get; set; } = new int[6];
        public string[] Raw { get; set; } = Array.Empty<string>();
    }

    public static List<LevelRow> ParseLevels(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<LevelRow>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 26 || !int.TryParse(row[0], out var lv)) continue;
            list.Add(new LevelRow
            {
                Level = lv,
                ExpNeeded = IniTable.GetLong(row, 1),
                MaxHp = IniTable.GetInt(row, 2),
                MaxMp = IniTable.GetInt(row, 3),
                NaturalDef = IniTable.GetInt(row, 4),
                Str = IniTable.GetInt(row, 5),
                Con = IniTable.GetInt(row, 6),   // natif: col6 = DEX/AGI
                Int = IniTable.GetInt(row, 7),
                Wis = IniTable.GetInt(row, 8),   // natif: col8 = SPR/WIS
                Agi = IniTable.GetInt(row, 9),   // natif: col9 = LUK
                Lck = IniTable.GetInt(row, 9),
                AttackMin = IniTable.GetInt(row, 19),
                AttackMax = IniTable.GetInt(row, 20),
                DefenseMin = IniTable.GetInt(row, 21),
                DefenseMax = IniTable.GetInt(row, 22),
                Tier = IniTable.GetInt(row, 25),
                ClassExpNeeded = IniTable.GetInt(row, 10),
                PetExp = IniTable.GetInt(row, 24),
                FamilyExpNeeded = IniTable.GetInt(row, 11),
                FamilyFundNeeded = IniTable.GetInt(row, 18),
                BuildingExps = new[]
                {
                    IniTable.GetInt(row, 12), IniTable.GetInt(row, 13),
                    IniTable.GetInt(row, 14), IniTable.GetInt(row, 15),
                    IniTable.GetInt(row, 16), IniTable.GetInt(row, 17),
                },
                Raw = row,
            });
        }
        return list;
    }

    // ---------- S_Races.ini (V.1,4,) ----------

    /// <summary>S_Races.ini — races jouables (comma format).</summary>
    public sealed class RaceDef
    {
        public int Id { get; set; }
        public string Code { get; set; } = string.Empty;  // 人, 侏 etc.
        public int Flag { get; set; }
        public string Name { get; set; } = string.Empty;  // 人類
    }

    public static List<RaceDef> ParseRaces(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<RaceDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 4 || !int.TryParse(row[0], out var id)) continue;
            list.Add(new RaceDef
            {
                Id = id,
                Code = IniTable.GetStr(row, 1),
                Flag = IniTable.GetInt(row, 2),
                Name = IniTable.GetStr(row, 3),
            });
        }
        return list;
    }

    // ---------- S_RootCmds.ini / S_GMToolCmds.ini (V.1,4,) ----------

    /// <summary>S_RootCmds.ini — 332 commandes GM (format comma).</summary>
    public sealed class GmCommand
    {
        public int Id { get; set; }
        public string Signature { get; set; } = string.Empty; // "ban <number> <number> <word>"
        public int AccessLevel { get; set; }
        public string[] Params { get; } = Array.Empty<string>() as string[];

        public string Name => Signature.Split(' ')[0];
        public IReadOnlyList<string> ParamSpec =>
            Signature.Split(' ').Skip(1).ToList();
    }

    public static List<GmCommand> ParseGmCommands(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<GmCommand>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 3 || !int.TryParse(row[0], out var id)) continue;
            list.Add(new GmCommand
            {
                Id = id,
                Signature = IniTable.GetStr(row, 1),
                AccessLevel = IniTable.GetInt(row, 2),
            });
        }
        return list;
    }

    // ---------- S_EquipSet.ini (|V.1|18|) ----------

    /// <summary>S_EquipSet.ini — bonus de sets d'equipement.</summary>
    public sealed class EquipSetDef
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<int> ItemIds { get; } = new();       // cols 3-7
        public List<int> BonusEnchantIds { get; } = new(); // cols 12-17
    }

    public static List<EquipSetDef> ParseEquipSets(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<EquipSetDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 10 || !int.TryParse(row[0], out var id)) continue;
            var set = new EquipSetDef { Id = id, Name = IniTable.GetStr(row, 1) };
            for (var i = 3; i <= 7 && i < row.Length; i++)
            {
                var itemId = IniTable.GetInt(row, i);
                if (itemId > 0) set.ItemIds.Add(itemId);
            }
            for (var i = 12; i < row.Length; i++)
            {
                var encId = IniTable.GetInt(row, i);
                if (encId > 0) set.BonusEnchantIds.Add(encId);
            }
            list.Add(set);
        }
        return list;
    }

    // ---------- S_Ai.ini (|V.2|25|) ----------

    /// <summary>
    /// S_Ai.ini — profils IA des monstres (aggro, skill usage).
    /// </summary>
    /// <summary>Layout CAiData prouvé par `GameData::CAiQuery::
    /// Deserialize` @0x85b60xx (le struct est byte-packed) :
    /// +0x00 u32 id | +0x04 u8 col1 | +0x05 u8 col2 | +0x06/0x07 chars
    /// | +0x08 u8 col5… Seul +0x05 est lu par `CAI::DoSeekEnemy`
    /// (probabilité de détection passive).</summary>
    public sealed class AiProfile
    {
        public int Id { get; set; }
        /// <summary>S_Ai col1 → CAiData+0x04 — paramètre brut (80 sur
        /// les AI de combat ; rôle exact non décodé — le rayon d'aggro
        /// vient de S_Monster col37, PAS de cette colonne).</summary>
        public int Param1 { get; set; }
        /// <summary>S_Ai col2 → CAiData+0x05 — probabilité d'aggro
        /// passive : `CAI::DoSeekEnemy` @0x84d5xxx roll `rand(100)
        /// &lt;= col2` toutes les ~2 s ; 0 = passif, 100 = toujours.</summary>
        public int SeekChancePercent { get; set; }
        public int SkillChance { get; set; }
        public int[] Values { get; set; } = Array.Empty<int>();
    }

    public static List<AiProfile> ParseAiProfiles(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<AiProfile>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 10 || !int.TryParse(row[0], out var id)) continue;
            list.Add(new AiProfile
            {
                Id = id,
                Param1 = IniTable.GetInt(row, 1),
                SeekChancePercent = IniTable.GetInt(row, 2),
                SkillChance = IniTable.GetInt(row, 4),
                Values = row.Select(s =>
                    int.TryParse(s, out var v) ? v : 0).ToArray(),
            });
        }
        return list;
    }

    /// <summary>Une entrée S_RecommendActivity — activité recommandée
    /// hebdomadaire (UI « RecommendActivities »). Mapping colonnes→struct
    /// prouvé par GameData Deserialize @0x8620880 (22 colonnes lues
    /// séquentiellement) :
    /// c1 id | c2 group (lu, ignoré par le natif) | c3 +0x04 max_lv |
    /// c4 +0x05 min_lv | c5 +0x06 type | c6 +0x08 flag (0 = éligible
    /// dans ResetRecommendActivitiesByWeek @0x8108e60) | c7 +0x0c |
    /// c8 +0x10 titre | c9 +0x14 | c10 +0x18 libellé | c11 +0x1c desc |
    /// c12 +0x20 enchant | c13 +0x24 durée enchant | c14 +0x28 item |
    /// c15 +0x2c qté | c16 +0x30 exp_mult | c17 +0x34 cp_mult |
    /// c18 +0x38 lifelong | c19 +0x3c gold | c20 +0x40 | c21 +0x44 |
    /// c22 +0x48 target. Récompenses : GiveRecommendActivitiesReward
    /// zone @0x810ba80 (item/enchant/exp×lv_data+0x58/cp×+0x5c/
    /// lifelong/gold).</summary>
    public sealed class RecommendActivityDef
    {
        public int Id { get; set; }
        /// <summary>Col 3 → +0x04 : niveau max (0 = pas de borne haute —
        /// ResetByWeek : « max==0 || level &lt;= max »).</summary>
        public int MaxLevel { get; set; }
        /// <summary>Col 4 → +0x05 : niveau min (« min &lt;= level »).</summary>
        public int MinLevel { get; set; }
        /// <summary>Col 5 → +0x06 : type d'activité (97 etc.).</summary>
        public int Kind { get; set; }
        /// <summary>Col 6 → +0x08 : le natif n'insère que les defs où
        /// ce byte vaut 0.</summary>
        public int Flag { get; set; }
        /// <summary>Col 8 → +0x10 : titre affiché.</summary>
        public string Title { get; set; } = "";
        /// <summary>Col 10 → +0x18 : libellé secondaire (court).</summary>
        public string SubTitle { get; set; } = "";
        /// <summary>Col 11 → +0x1c : description longue.</summary>
        public string Description { get; set; } = "";
        /// <summary>Col 12 → +0x20 : enchant offert (0 = aucun).</summary>
        public int EnchantId { get; set; }
        /// <summary>Col 13 → +0x24 : durée enchant (×10 ticks natifs).</summary>
        public int EnchantDuration { get; set; }
        /// <summary>Col 14 → +0x28 : item de récompense.</summary>
        public int RewardItemId { get; set; }
        /// <summary>Col 15 → +0x2c : quantité.</summary>
        public int RewardItemCount { get; set; }
        /// <summary>Col 16 → +0x30 : multiplicateur d'exp appliqué à la
        /// base du level_data monstre (GainExp).</summary>
        public int ExpMultiplier { get; set; }
        /// <summary>Col 17 → +0x34 : multiplicateur de points de classe.</summary>
        public int ClassPointMultiplier { get; set; }
        /// <summary>Col 18 → +0x38 : lifelong gold (« GMGainLifelong Money »).</summary>
        public int LifelongGold { get; set; }
        /// <summary>Col 19 → +0x3c : or.</summary>
        public int Gold { get; set; }
        /// <summary>Col 20 → +0x40 : catégorie d'événement progressif
        /// (0xe = marquee — incrémentée par
        /// <c>CCharacter::HandleRAMarquee</c> ex. lot de sac chanceux,
        /// @0x082aa4f0).</summary>
        public int EventKind { get; set; }
        /// <summary>Col 21 → +0x44 : paramètre d'événement — comparé à
        /// l'argument de HandleRAMarquee (1 = lot de sac chanceux
        /// récupéré).</summary>
        public int EventParam { get; set; }
        /// <summary>Col 22 → +0x48 : compteur cible à atteindre.</summary>
        public int Target { get; set; }
    }

    /// <summary>Parse S_RecommendActivity.ini.</summary>
    public static List<RecommendActivityDef> ParseRecommendActivities(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var list = new List<RecommendActivityDef>();
        foreach (var row in table.Rows)
        {
            if (row.Length < 22 || !int.TryParse(row[0], out var id)) continue;
            list.Add(new RecommendActivityDef
            {
                Id = id,
                MaxLevel = IniTable.GetInt(row, 2),
                MinLevel = IniTable.GetInt(row, 3),
                Kind = IniTable.GetInt(row, 4),
                Flag = IniTable.GetInt(row, 5),
                Title = IniTable.GetStr(row, 7),
                SubTitle = IniTable.GetStr(row, 9),
                Description = IniTable.GetStr(row, 10),
                EnchantId = IniTable.GetInt(row, 11),
                EnchantDuration = IniTable.GetInt(row, 12),
                RewardItemId = IniTable.GetInt(row, 13),
                RewardItemCount = IniTable.GetInt(row, 14),
                ExpMultiplier = IniTable.GetInt(row, 15),
                ClassPointMultiplier = IniTable.GetInt(row, 16),
                LifelongGold = IniTable.GetInt(row, 17),
                Gold = IniTable.GetInt(row, 18),
                EventKind = IniTable.GetInt(row, 19),
                EventParam = IniTable.GetInt(row, 20),
                Target = IniTable.GetInt(row, 21),
            });
        }
        return list;
    }
}
