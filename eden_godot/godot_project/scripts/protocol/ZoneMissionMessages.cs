using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages mission du ZoneServer (:5201) — ops de quêtes C→S,
// marqueurs NPC (345) et objectifs PMS décodés depuis c_mission.ini.
//
// Sources de preuve (eden_server — lecture seule) :
//   - $EDEN/server/src/Eden.Zone/Handlers/SocialHandlers.cs:425-466 —
//     C→S 87 MissionListOp `[s16 op][i32 mid][u16 npcUid]` : op<6 est
//     relayé à la mission (« MissionServiceOp mid op npcUid »),
//     op==9 = clic « Available quests » du HUD.
//   - $EDEN/server/src/Eden.Mission/Services/QuestEngine.Events.cs —
//     OnServiceOpAsync : op 1 = accept, 2 = reward(claim), 3 = on-going,
//     4 = finish ; OnGiveUpAsync : 143 mission-interne GiveUp.
//   - $EDEN/server/src/Eden.Zone/Handlers/TeamHandlers.cs:118-130 —
//     C→S 51 GiveUpMission `[s16 mid]` relayé tel quel à la mission.
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.Mission.cs:215-236 —
//     S→C 345 UpdateNPCSymbols `[s16 n][n×(i32 npcUid, s16 sym,
//     s16 sym2)]` (la zone ajoute le 2e s16, passthru mission 65).
//   - $EDEN/server/src/Eden.Mission/Services/QuestEngine.Objectives.cs
//     :136-207 — clé PMS = mid×1 000 000 + type×10 000 + (objId−base),
//     format natif GetMonsterKilledPMS & co (@0x80e1b30-0x80e2310).

/// <summary>Sous-opérations du C→S 87 MissionListOp — valeurs natives
/// prouvées (QuestEngine.OnServiceOpAsync + capture
/// « 0200 f4010000 ffff » = op 2, mid 500, npcUid 0xffff).</summary>
public static class MissionListOps
{
    /// <summary>Accepter la quête proposée (mission-open → accept).</summary>
    public const short Accept = 1;
    /// <summary>Réclamer la récompense (mission-reward → claim).</summary>
    public const short Reward = 2;
    /// <summary>Rafraîchir la quête en cours (mission-on-going).</summary>
    public const short OnGoing = 3;
    /// <summary>Rendre/terminer (mission-finish).</summary>
    public const short Finish = 4;
    /// <summary>Partage en équipe : le partageur propose la quête aux
    /// membres locaux (zone CMissionListOp::DoOperation @0x0856e7f0,
    /// relayé « MissionServiceOp mid 6 membre »). Field3 = 0xFFFF.</summary>
    public const short ShareStart = 6;
    /// <summary>Réponse du destinataire d'un partage : Field3 = 1
    /// accepte (= accept natif), 0 refuse (texte 0xa05 au partageur).</summary>
    public const short ShareAnswer = 7;
    /// <summary>Clic sur une quête « dispo » du panneau HUD — le serveur
    /// renvoie 388 op=1 pour ouvrir la fiche (capture native).</summary>
    public const short PickAvailable = 9;
}

/// <summary>Ops du S→C 388 EnterMissionList (<see
/// cref="ZoneEnterMissionList"/>) — la mission pilote l'UI quête via
/// « shop &lt;verb&gt; » (ZoneEntryFlow.Mission.Commands.cs:225-252).</summary>
public static class MissionSheetOps
{
    /// <summary>Fiche d'offre — bouton « Accept » → 87 op=1.</summary>
    public const short Open = 1;
    /// <summary>Fiche récompense — bouton « Complete » → 87 op=2.</summary>
    public const short Reward = 2;
    /// <summary>Fiche quête en cours (suivi, pas d'action serveur).</summary>
    public const short OnGoing = 3;
    /// <summary>Quête rendue — le serveur ferme la fiche.</summary>
    public const short Finish = 4;
    /// <summary>Fiche de partage reçue (« shop mission-share ») : le
    /// destinataire accepte (87 op 7, flag 1) ou refuse (flag 0).</summary>
    public const short Share = 7;
}

/// <summary>C→S NCID 51 — GiveUpMission [s16 mid] : abandonner une
/// quête en cours (bouton « Give Up » du journal). Relayé tel quel à
/// la mission (TeamHandlers.cs:118 → CNC_ZMis_MissionServerGiveUpMission,
/// wire [i32 charUid][s16 nodeId][s16 mid]).</summary>
public sealed class ZoneGiveUpMission : NetMessage
{
    public override int Opcode => 51;
    public override string Name => "Zone.ZoneServerGiveUpMission";
    /// <summary>s16 — id de mission abandonnée.</summary>
    public short MissionId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(MissionId);
    public override void Deserialize(BinReader s) => MissionId = s.ReadShort();
}

/// <summary>S→C NCID 345 — UpdateNPCSymbols : marqueurs de quête
/// au-dessus des NPC (« ! » / « ? »). Wire prouvé par le passthru
/// zone (ZoneEntryFlow.Mission.cs:215 — le serveur réécrit le
/// mission→zone `[i32 char][s16 node][u16 n][n×(i32 uid, s16 sym)]`
/// en client `[s16 n][n×(i32 uid, s16 sym1, s16 sym2)]`, le 2e s16
/// étant un état local mis à 0).</summary>
public sealed class ZoneUpdateNpcSymbols : NetMessage
{
    public override int Opcode => 345;
    public override string Name => "Zone.ClientUpdateNPCSymbols";

    /// <summary>Un marqueur sur un NPC.</summary>
    public readonly record struct NpcSymbol(int NpcUid, short Sym1,
        short Sym2);

    /// <summary>Entrées (npcUid = template/star id du NPC).</summary>
    public List<NpcSymbol> Entries { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries)
        {
            s.WriteInt(e.NpcUid);
            s.WriteShort(e.Sym1);
            s.WriteShort(e.Sym2);
        }
    }
    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 8; i++)
            Entries.Add(new NpcSymbol(s.ReadInt(), s.ReadShort(),
                s.ReadShort()));
    }
}

/// <summary>
/// Objectifs de quête décodés de <c>data/db/c_mission.ini</c> (même
/// format que S_Mission.ini serveur — script col16 « #:Cmd args:… »
/// parsé par <see cref="MissionParser"/> côté serveur).
///
/// Clé PMS native (désasm GetMonsterKilledPMS/GetMonsterTreasureGotPMS/
/// GetItemGotPMS/GetItemGivePMS/GetNPCTalkPMS/GetNPCMultiTalkPMS/
/// GetAreaTravelPMS @0x80e1b30-0x80e2310 — port de
/// <c>QuestEngine.Objectives.PmsObjectives</c>) :
///   <c>mid×1 000 000 + type×10 000 + (objId − base)</c> avec
///   type 1=kill(55000), 2=treasure(55000), 3=itemgot(50000),
///   4=itemgiven(50000), 5=npctalk(50000), 6=npcitem(submit−50000),
///   7=areatravel, 9=multitalk(50000), 10=itemcollect(clé partagée).
/// Le client affiche « n/requis » du tracker sur ces clés via le
/// 391 kind=1 (`[u8 1][u32 pmsKey][u16 count][s16 0]`).
/// </summary>
public static class MissionObjectives
{
    /// <summary>Un objectif affichable d'une quête.</summary>
    /// <param name="ItemId">Item de quête lié à l'objectif (collecte,
    /// loot de mob, remise) — 0 si l'objectif n'en a pas ; sert au
    /// bouton « Use » du tracker.</param>
    public sealed record Objective(uint PmsKey, string Label, int Req,
        int ItemId = 0);

    /// <summary>Type d'objectif décodé d'une clé PMS (key/10 000 % 100).</summary>
    public enum Kind : int
    {
        Unknown = 0, Kill = 1, Treasure = 2, ItemGot = 3,
        ItemGiven = 4, NpcTalk = 5, NpcItem = 6, AreaTravel = 7,
        MultiTalk = 9, ItemCollect = 10,
    }

    /// <summary>Décode une clé PMS → (missionId, type, objId natif
    /// ré-additonné de sa base). Retourne mid=0 si la clé n'est pas
    /// au format mid×1e6+….</summary>
    public static (int Mid, Kind Type, int ObjId) DecodePmsKey(uint key)
    {
        var mid = (int)(key / 1_000_000u);
        if (mid <= 0) return (0, Kind.Unknown, 0);
        var rest = key % 1_000_000u;
        var type = (int)(rest / 10_000u);
        var obj = (int)(rest % 10_000u);
        var kind = type switch
        {
            1 => Kind.Kill, 2 => Kind.Treasure, 3 => Kind.ItemGot,
            4 => Kind.ItemGiven, 5 => Kind.NpcTalk, 6 => Kind.NpcItem,
            7 => Kind.AreaTravel, 9 => Kind.MultiTalk,
            10 => Kind.ItemCollect,
            _ => Kind.Unknown,
        };
        // Restaure l'id natif (base soustraite à l'encodage).
        var objId = kind switch
        {
            Kind.Kill or Kind.Treasure => obj + 55000,
            Kind.ItemGot or Kind.ItemGiven or Kind.NpcTalk
                or Kind.NpcItem or Kind.MultiTalk => obj + 50000,
            Kind.AreaTravel => (obj / 10) * 100 + (obj % 10) + 0x32,
            _ => obj,
        };
        return (mid, kind, objId);
    }

    /// <summary>Objectifs PMS d'une quête — mêmes clés que le serveur
    /// (PmsObjectives) + libellés EN (t_monster/t_item/t_npc/t_node).</summary>
    public static List<Objective> ForMission(int mid)
    {
        var m = MissionTable.Get(mid);
        if (m == null) return new List<Objective>();
        var list = new List<Objective>();
        var midKey = (uint)mid * 1_000_000u;
        foreach (var o in m.Actions)
        {
            int A(int i) => i < o.Args.Count &&
                int.TryParse(o.Args[i], out var v) ? v : 0;
            switch (o.Name)
            {
                case "MonsterKilled" when o.Args.Count >= 2:
                    list.Add(new(midKey + 10000u + (uint)(A(0) - 55000),
                        N("Defeat \"%s1\"", Name(Data.TextResolver.MonsterNameShort(A(0)), A(0))),
                        Math.Max(1, A(1))));
                    break;
                case "MonsterTreasureGot" when o.Args.Count >= 4:
                    list.Add(new(midKey + 20000u + (uint)(A(0) - 55000),
                        N("Collect \"%s2\" from \"%s1.\"",
                            Name(Data.TextResolver.MonsterNameShort(A(0)), A(0)),
                            Name(Data.TextResolver.ItemName(A(2)), A(2))),
                        Math.Max(1, A(3)), A(2)));
                    break;
                case "ItemGot" when o.Args.Count >= 4:
                    list.Add(new(midKey + 30000u + (uint)(A(0) - 50000),
                        N("Collect \"%s1\"", Name(Data.TextResolver.ItemName(A(2)), A(2))),
                        Math.Max(1, A(3)), A(2)));
                    break;
                case "ItemGiven" when o.Args.Count >= 4:
                    list.Add(new(midKey + 40000u + (uint)(A(0) - 50000),
                        N("Deliver \"%s1\" to \"%s2.\"",
                            Name(Data.TextResolver.ItemName(A(2)), A(2)),
                            Name(Data.TextResolver.NpcName(A(0)), A(0))),
                        Math.Max(1, A(3)), A(2)));
                    break;
                case "NPCTalk":
                    if (o.Args.Count >= 1 && A(0) != 0)
                        list.Add(new(midKey + 50000u + (uint)(A(0) - 50000),
                            N("Talk to \"%s1\"", Name(Data.TextResolver.NpcName(A(0)), A(0))), 1));
                    else if (m.SubmitNpc != 0)
                        list.Add(new(midKey + 50000u + (uint)(m.SubmitNpc - 50000),
                            N("Talk to \"%s1\"", Name(Data.TextResolver.NpcName(m.SubmitNpc), m.SubmitNpc)), 1));
                    break;
                case "NPCItem" when m.SubmitNpc != 0:
                    list.Add(new(midKey + 60000u + (uint)(m.SubmitNpc - 50000),
                        N("Talk to \"%s1\"", Name(Data.TextResolver.NpcName(m.SubmitNpc), m.SubmitNpc)), 1));
                    break;
                case "NPCMultiTalk" when o.Args.Count >= 1:
                    list.Add(new(midKey + 90000u + (uint)(A(0) - 50000),
                        N("Talk to \"%s1\"", Name(Data.TextResolver.NpcName(A(0)), A(0))), 1));
                    break;
                case "AreaTravel" when o.Args.Count >= 1 && A(0) > 0:
                    list.Add(new(midKey + 70000u + (uint)(
                        (A(0) % 100 - 0x32) + (A(0) / 100) * 10),
                        F("Travel to {0}", Name(Data.TextResolver.NodeName(A(0) / 100), A(0) / 100)), 1));
                    break;
                case "ItemCollect" when o.Args.Count >= 2:
                    // Clé partagée mid×1e6+100000 (MC_FinishItemCollect)
                    // — comptée sur l'inventaire au rendu, affichée n/req
                    // quand le serveur émet le 391 correspondant.
                    list.Add(new(midKey + 100000u,
                        N("Collect \"%s1\"", Name(Data.TextResolver.ItemName(A(0)), A(0))),
                        Math.Max(1, A(1)), A(0)));
                    break;
            }
        }
        return list;
    }

    /// <summary>Ligne de progression d'un objectif à partir d'une
    /// update 391 kind=1 — « Defeat Rose Knight 2/3 » ; null si la
    /// clé est inconnue de la table.</summary>
    public static string? ProgressLine(uint pmsKey, int count)
    {
        var (mid, _, _) = DecodePmsKey(pmsKey);
        if (mid <= 0) return null;
        var obj = ForMission(mid)
            .FirstOrDefault(o => o.PmsKey == pmsKey);
        if (obj == null)
        {
            var (_, kind, objId) = DecodePmsKey(pmsKey);
            var label = kind switch
            {
                Kind.Kill or Kind.Treasure =>
                    $"Defeat {Name(Data.TextResolver.MonsterNameShort(objId), objId)}",
                Kind.NpcTalk or Kind.NpcItem or Kind.MultiTalk =>
                    $"Talk to {Name(Data.TextResolver.NpcName(objId), objId)}",
                Kind.ItemGot or Kind.ItemGiven or Kind.ItemCollect =>
                    $"Collect {Name(Data.TextResolver.ItemName(objId), objId)}",
                Kind.AreaTravel =>
                    $"Travel to {Name(Data.TextResolver.NodeName(objId / 100), objId / 100)}",
                _ => $"Objective #{pmsKey}",
            };
            return count > 0 ? $"{label} {count}" : label;
        }
        return $"{obj.Label} {Math.Min(count, obj.Req)}/{obj.Req}";
    }

    /// <summary>Récompenses d'une quête (script col20 — RewardGold/
    /// RewardExp/RewardItem/RewardClassPoint…) formatées EN pour la
    /// fiche d'offre/récompense.</summary>
    public static List<string> RewardLines(int mid)
    {
        var m = MissionTable.Get(mid);
        var lines = new List<string>();
        if (m == null) return lines;
        foreach (var r in m.Rewards)
        {
            int A(int i) => i < r.Args.Count &&
                int.TryParse(r.Args[i], out var v) ? v : 0;
            switch (r.Name)
            {
                case "RewardGold": lines.Add($"{A(0)} gold"); break;
                case "RewardExp": lines.Add($"{A(0)} EXP"); break;
                case "RewardItem":
                    lines.Add($"{Name(Data.TextResolver.ItemName(A(0)), A(0))}" +
                        (A(1) > 1 ? $" ×{A(1)}" : "")); break;
                case "RewardChooseItem":
                    lines.Add($"Choose: {Name(Data.TextResolver.ItemName(A(0)), A(0))}"); break;
                case "RewardClassPoint": lines.Add($"{A(0)} class points"); break;
                case "RewardReputation": lines.Add("Reputation"); break;
                case "RewardSpecialCoin": lines.Add($"{A(0)} special coins"); break;
            }
        }
        return lines;
    }

    /// <summary>Résumé « objectifs » pour la fiche d'offre (sans
    /// compteurs — « Defeat Rose Knight ×3 »).</summary>
    public static List<string> ObjectiveLines(int mid) =>
        ForMission(mid).Select(o => o.Req > 1
            ? $"{o.Label} ×{o.Req}" : o.Label).ToList();

    private static string Name(string? en, int id) =>
        en ?? $"#{id}";

    /// <summary>Objectif traduit : la clé TSV porte les {0} pour
    /// garder l'ordre des mots libre selon la langue.</summary>
    private static string N(string key, params string[] args)
    {
        // Chaînes natives « Defeat "%s1" » : %sN remplacés après
        // traduction (l'ordre des mots dépend de la langue).
        var t = Data.UiTextEn.Translate(key);
        for (var i = 0; i < args.Length; i++)
            t = t.Replace($"%s{i + 1}", args[i]);
        return t;
    }

    /// <summary>Ligne « Talk to "X" to report your quest progress. »
    /// (natif) — objectif de rendu d'une quête terminée.</summary>
    public static string ReturnLine(int npcUid) => N(
        "Talk to \"%s1\" to report your quest progress.",
        Data.TextResolver.NpcName(npcUid)
            ?? Data.UiTextEn.Translate("the quest giver"));

    private static string F(string key, params object[] args) =>
        string.Format(Data.UiTextEn.Translate(key), args);
}

/// <summary>
/// <c>data/db/c_mission.ini</c> — définitions de quêtes côté client
/// (mêmes colonnes que S_Mission.ini : col11 = NPC donneur, col16 =
/// script d'objectifs, col17 = NPC de rendu, col20 = récompenses —
/// voir <c>$EDEN/server/.../Parsers/MissionParser.cs</c>). Chargement
/// paresseux partagé par le tracker, la fiche quête et le panneau.
/// </summary>
public static class MissionTable
{
    /// <summary>Commande de script c_mission (« MonsterKilled 55001 3 »).</summary>
    public sealed class ScriptCmd
    {
        public required string Name { get; init; }
        public List<string> Args { get; } = new();
    }

    /// <summary>Quête (sous-ensemble utile au client).</summary>
    public sealed class MissionDef
    {
        public int Id { get; init; }
        /// <summary>Col 11 — uid du NPC donneur.</summary>
        public int GiverStepId { get; init; }
        public int SubmitStepId { get; init; }
        /// <summary>Col 17 — uid du NPC de rendu.</summary>
        public int SubmitNpc { get; init; }
        /// <summary>Col 11 — uid du NPC donneur.</summary>
        public int GiverNpc { get; init; }
        /// <summary>Col 4 — limite de complétions (0 = quête unique).</summary>
        public int CompleteLimit { get; init; }
        /// <summary>Col 6 — flags (bit 0x10 = quotidienne).</summary>
        public int Flags { get; init; }
        /// <summary>Col 8 — niveau requis pour proposer la quête.</summary>
        public int ReqLevel { get; init; }
        /// <summary>Col 10 — groupe d'affichage (Big5, ex. 每日任務).</summary>
        public string Group { get; init; } = "";
        /// <summary>Col 23 — type/série (Big5, ex. « S025，主線 »).</summary>
        public string Category { get; init; } = "";
        /// <summary>Col 13 — conditions d'offre (If_FMS / If_CharLevel…).</summary>
        public List<ScriptCmd> Conditions { get; } = new();
        /// <summary>Col 16 — objectifs (« MonsterKilled 55001 3 »).</summary>
        public List<ScriptCmd> Actions { get; } = new();
        /// <summary>Col 20 — récompenses.</summary>
        public List<ScriptCmd> Rewards { get; } = new();
    }

    // Publié une fois rempli (lecteurs concurrents : jamais de dictionnaire à moitié construit).
    private static volatile Dictionary<int, MissionDef>? _missions;
    private static readonly object _missionsLock = new();

    /// <summary>Toutes les quêtes de la table.</summary>
    public static IEnumerable<MissionDef> All => Ensure().Values;

    /// <summary>Définition d'une quête, null si absente de la table.</summary>
    public static MissionDef? Get(int mid) =>
        Ensure().TryGetValue(mid, out var m) ? m : null;

    /// <summary>Parse « #:Cmd args:Cmd2 args:::: » — '#' introduit le
    /// bloc, ':' sépare les commandes (même grammaire que
    /// MissionParser.ParseScript serveur).</summary>
    public static List<ScriptCmd> ParseScript(string script)
    {
        var cmds = new List<ScriptCmd>();
        if (string.IsNullOrWhiteSpace(script)) return cmds;
        var body = script.TrimStart('#', ':');
        foreach (var seg in body.Split(':',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var words = seg.Trim().Split(' ',
                StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;
            var c = new ScriptCmd { Name = words[0] };
            for (var i = 1; i < words.Length; i++)
                c.Args.Add(words[i]);
            cmds.Add(c);
        }
        return cmds;
    }

    private static Dictionary<int, MissionDef> Ensure()
    {
        if (_missions is { } ready) return ready;
        lock (_missionsLock)
            return _missions ??= Load();
    }

    private static Dictionary<int, MissionDef> Load()
    {
        var missions = new Dictionary<int, MissionDef>();
        var path = (Data.TextResolver.ResolveFile
                ?? Data.Tables.StoreTable.ResolveDefault)(
            "data/db/c_mission.ini");
        if (path == null) return missions;
        try
        {
            var table = Data.IniTable.ParseFile(path,
                Data.EdenEncoding.Big5);
            foreach (var row in table.Rows)
            {
                if (row.Length < 21 ||
                    !int.TryParse(row[0], out var id))
                    continue;
                var def = new MissionDef
                {
                    Id = id,
                    // col 14/19 : ids d'étape de mission (10xxx), pas des NPC
                    GiverStepId = Data.IniTable.GetInt(row, 14),
                    SubmitStepId = Data.IniTable.GetInt(row, 19),
                    // col 17 : NPC de remise (50xxx → dialogue, prouvé serveur
                    // QuestEngine.Dialogue : 50003→4003)
                    SubmitNpc = Data.IniTable.GetInt(row, 17),
                    GiverNpc = Data.IniTable.GetInt(row, 11),
                    CompleteLimit = Data.IniTable.GetInt(row, 4),
                    Flags = Data.IniTable.GetInt(row, 6),
                    ReqLevel = Data.IniTable.GetInt(row, 8),
                    Group = Data.IniTable.GetStr(row, 10),
                    Category = Data.IniTable.GetStr(row, 23),
                };
                def.Conditions.AddRange(
                    ParseScript(Data.IniTable.GetStr(row, 13)));
                def.Actions.AddRange(
                    ParseScript(Data.IniTable.GetStr(row, 16)));
                def.Rewards.AddRange(
                    ParseScript(Data.IniTable.GetStr(row, 20)));
                missions[id] = def;
            }
        }
        catch (Exception e)
        {
            Data.TextResolver.Log?.Invoke(
                $"[mission] c_mission.ini : {e.Message}");
        }
        return missions;
    }
}
