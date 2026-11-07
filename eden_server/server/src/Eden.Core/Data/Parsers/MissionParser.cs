using Eden.Core.Data;
using System.Collections.Generic;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser pour S_Mission.ini — 2831 quetes (|V.5|28|).
///
/// Colonnes:
///   [0]  Mission ID (1009...)
///   [1]  Nom du script (script01)
///   [2]  Nom de la quete (L01.甦醒之一)
///   [3]  ?
///   [5]  Niveau min
///   [6]  Niveau max
///   [7]  Flag
///   [8]  ?
///   [10] Nom de la map (Big5: 灰岩山)
///   [13] Script de conditions: "#:If_Gender 0 1 9 8:::::::"
///   [14] ID etape suivante / NPC
///   [15] Script intermediaire
///   [16] Script d'action: "#:NPCTalk :::::::"
///   [17] NPC/spell id
///   [19] Etape suivante
///   [20] Script de recompenses: "#:RewardGold 7:RewardExp 38:RewardItem 44025 1::::"
///   [21] Etape suivante
///   [23] Type/serie (S002，主線 = quete principale)
///   [25-26] IDs suivants
///   [27] Description texte
///
/// Langage de script (27 commandes identifiees):
///   Conditions : If_CharLevel, If_FMS, If_Gender, If_CheckBuff,
///                PlayerLevel, CheckBuff
///   Objectifs  : MonsterKilled, ItemCollect, MonsterTreasureGot, NPCTalk,
///                AcceptInventoryItem, ItemGiven, ItemGot, AreaTravel,
///                NPCItem, MC, DoTimes, WinTimes, AcceptCloseMission, AcceptTPA
///   Recompenses: RewardGold, RewardExp, RewardItem, RewardBuff,
///                RewardReputation, RewardSpecialCoin, RewardChooseItem,
///                RewardClassPoint, RewardFamilyExp
///
/// Syntaxe: "#:Cmd arg1 arg2 ...:Cmd2 ...:::" — ':' separe commande/args.
/// </summary>
public static class MissionParser
{
    public sealed class ScriptCommand
    {
        public string Name { get; set; } = string.Empty;
        public List<string> Args { get; set; } = new();
    }

    public sealed class MissionDef
    {
        public int Id { get; set; }
        public string ScriptName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;        // Big5
        public int Field5 { get; set; }
        public int Field6 { get; set; }
        /// <summary>Col4 — limite de complétions (s16). Routage natif
        /// (SetMission @0x080e5250) : flag col6 bit4 SET → compteur
        /// JOURNALIER (mission+0x226) ; bit4 CLEAR → compteur total
        /// de session (mission+0x224). 0 = quête ordinaire
        /// (complétion unique, persistante).</summary>
        public int CompleteLimit { get; set; }
        /// <summary>Col6 bit4 — quête « daily-type » : sa limite
        /// compte par jour (reset au changement de jour, natif
        /// CGlobalInfo::ResetDailyMission @0x080752f0).</summary>
        public bool DailyType => (Field6 & 0x10) != 0;
        /// <summary>Col6 bit2 — pour une quête limitée non-daily, le
        /// natif conserve la row complete_num tant que la limite
        /// n'est pas atteinte (au lieu de la supprimer dès la
        /// complétion).</summary>
        public bool KeepLimitRow => (Field6 & 4) != 0;
        public string MapName { get; set; } = string.Empty;
        public List<ScriptCommand> Conditions { get; set; } = new();  // col 13
        /// <summary>Script d'acceptation (col 15) — exécuté à la prise
        /// de quête : `AcceptInventoryItem id ± n` (remise/retrait
        /// d'item), `RewardBuff e d`, `AcceptTPA p a`, `AcceptSRA`,
        /// `AcceptCloseMission mid…` (abandon des quêtes listées) et
        /// des gates `If_*` supplémentaires.</summary>
        public List<ScriptCommand> AcceptActions { get; set; } = new(); // col 15
        public List<ScriptCommand> Actions { get; set; } = new();     // col 16
        public List<ScriptCommand> Rewards { get; set; } = new();     // col 20
        public string Category { get; set; } = string.Empty;          // col 23
        /// <summary>Groupe de trigger (col 25) — natif
        /// `TriggerGroupMission` : quand une quête complétée porte
        /// `TriggerGroup &lt;gid&gt;` dans sa récompense, TOUTES les
        /// missions dont col25 == gid deviennent éligibles et
        /// s'auto-accordent au joueur (chaînes de quêtes — ex. 1293
        /// "TriggerGroup 426" active 1295-1298).</summary>
        public int TriggerGroup { get; set; }                           // col 25
        public string Description { get; set; } = string.Empty;       // col 27
        public string[] Raw { get; set; } = System.Array.Empty<string>();
    }

    public static List<MissionDef> Parse(string filePath)
    {
        var table = IniTable.ParseFile(filePath, EdenEncoding.Big5);
        var missions = new List<MissionDef>(table.Rows.Count);

        foreach (var row in table.Rows)
        {
            if (row.Length < 15 || !int.TryParse(row[0], out var id)) continue;

            missions.Add(new MissionDef
            {
                Id = id,
                ScriptName = IniTable.GetStr(row, 1),
                Name = IniTable.GetStr(row, 2),
                CompleteLimit = IniTable.GetInt(row, 4),
                Field5 = IniTable.GetInt(row, 5),
                Field6 = IniTable.GetInt(row, 6),
                MapName = IniTable.GetStr(row, 10),
                Conditions = ParseScript(IniTable.GetStr(row, 13)),
                AcceptActions = ParseScript(IniTable.GetStr(row, 15)),
                Actions = ParseScript(IniTable.GetStr(row, 16)),
                Rewards = ParseScript(IniTable.GetStr(row, 20)),
                Category = IniTable.GetStr(row, 23),
                TriggerGroup = IniTable.GetInt(row, 25),
                Description = IniTable.GetStr(row, 27),
                Raw = row,
            });
        }

        return missions;
    }

    /// <summary>
    /// Parse "#:Cmd args:Cmd2 args::::" — '#' introduit le bloc,
    /// ':' separe les commandes, chaque segment = "Nom arg1 arg2 ...".
    /// </summary>
    public static List<ScriptCommand> ParseScript(string script)
    {
        var commands = new List<ScriptCommand>();
        if (string.IsNullOrWhiteSpace(script)) return commands;

        var body = script.TrimStart('#', ':');
        foreach (var segment in body.Split(':',
            System.StringSplitOptions.RemoveEmptyEntries))
        {
            var words = segment.Trim().Split(' ',
                System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;

            var cmd = new ScriptCommand { Name = words[0] };
            for (var i = 1; i < words.Length; i++)
                cmd.Args.Add(words[i]);
            commands.Add(cmd);
        }
        return commands;
    }
}
