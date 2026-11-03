using System.Text;

namespace EdenClient.Data.Tables;

/// <summary>
/// <c>data/scene/Sxxx.ini</c> — données de scène d'une map : spawns de
/// monstres, PNJ, zones d'événements, portes, patrouilles.
/// Format : sections <c>[nom],</c> puis lignes CSV (Big5).
///
/// Port de <c>SceneParser</c>
/// (<c>$EDEN/server/.../Parsers/SceneParser.cs</c>) — sémantique des
/// colonnes prouvée par désasm (LoadGatewayTargetID @0x834fa40,
/// GetStatueInThisServer @0x8370ca0, CSceneEventCmds côté mission).
///
/// Sections client supplémentaires non gérées par le serveur
/// (<c>environment_sound</c>, <c>color_correction</c>,
/// <c>dynamic_block</c>) sont conservées brutes dans
/// <see cref="Scene.ExtraSections"/>.
/// </summary>
public static class SceneTable
{
    /// <summary>Spawn de monstre depuis [reborn_monsters].</summary>
    public sealed class MonsterSpawn
    {
        public int AreaId { get; init; }
        public int MonsterId { get; init; }
        public int PatrolId { get; init; }
        /// <summary>Temps de respawn en secondes.</summary>
        public int RespawnSeconds { get; init; }
        /// <summary>Nombre de monstres créés par ce point (col 3).</summary>
        public int Count { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        public float Facing { get; init; }
        /// <summary>Col 12 — code modèle (M008…).</summary>
        public string ModelCode { get; init; } = string.Empty;
        /// <summary>Col 13 — nom Big5.</summary>
        public string Name { get; init; } = string.Empty;
        /// <summary>Col 18 — table de drop associée.</summary>
        public int DropTableId { get; init; }
    }

    /// <summary>Spawn de PNJ depuis [npc] — format natif
    /// <c>localId,tid,x,y,facing,motion,param,...</c> (prouvé par
    /// <c>CStatueDBO::GetStatueInThisServer</c> @0x8370ca0 ; il n'y a
    /// PAS de z).</summary>
    public sealed class NpcSpawn
    {
        /// <summary>Id local négatif utilisé par la scène.</summary>
        public int LocalId { get; init; }
        public int NpcId { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Facing { get; init; }
        /// <summary>Col 5 — code modèle.</summary>
        public string ModelCode { get; init; } = string.Empty;
        /// <summary>Col 6 — nom Big5 pour les NPCs ordinaires.</summary>
        public string Name { get; init; } = string.Empty;
        /// <summary>Col 6 lu en int — paramètre numérique (4000) pour
        /// les slots statue.</summary>
        public int Param { get; init; }
    }

    /// <summary>Slot de statue de famille — record [npc] dont le
    /// template ∈ [0xD675,0xD679] (54901-54905). Jamais spawné comme
    /// NPC (le natif crée un slot vide). Routage identique au serveur.</summary>
    public sealed class StatueSlot
    {
        public int TemplateId { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Facing { get; init; }
        /// <summary>Col 6 du record (4000 dans S501/S502) — champ +0x14
        /// du CStatueDB natif.</summary>
        public int Param { get; init; }
    }

    /// <summary>Porte vers une autre map depuis [gateway_area].
    /// Sémantique prouvée par désassemblage de
    /// <c>CEventAreaDBO::LoadGatewayTargetID</c> (@0x834fa40) :
    ///   col0 = rowId, col1 = eventAreaId (jointure [event_area] pour
    ///        la géométrie TestIn),
    ///   col2 = scriptEventId (-1 → c'est col4 qui sert d'event id),
    ///   col3 = linkKey (90112 = groupe de liaison, -1/0 = aucun),
    ///   col4 = keyId — clé s16 que le client envoie dans CheckNodeExit.</summary>
    public sealed class Gateway
    {
        public int RowId { get; init; }
        /// <summary>Id de l'event_area GATEWAY déclencheur (col1).</summary>
        public int EventAreaId { get; init; }
        /// <summary>Id du script [event] déclenché (col2).</summary>
        public int ScriptEventId { get; init; }
        /// <summary>Clé de liaison/condition (col3).</summary>
        public int LinkKey { get; init; }
        /// <summary>Clé s16 envoyée par le client (col4).</summary>
        public int KeyId { get; init; }
        public string Description { get; init; } = string.Empty;
    }

    /// <summary>Script de la section [event] :
    /// <c>id,type,?,"#:Event_If_*…:#:Event_*…:",desc</c>. Preuve : désasm
    /// <c>lapis::Event_If_Level</c> @0x80ddcf0 (mission) —
    /// <c>Event_Transport &lt;node&gt;</c> = migration,
    /// <c>Event_ScreenMessage &lt;textId&gt;</c> = message d'écran.</summary>
    public sealed class SceneEvent
    {
        public int Id { get; init; }
        /// <summary>Script brut (segments "#:Event_X a b c:").</summary>
        public string Script { get; init; } = string.Empty;

        /// <summary>Une commande de script (verbe + args entiers).</summary>
        public sealed class Cmd
        {
            public required string Name { get; init; }
            public required int[] Args { get; init; }
        }

        /// <summary>Paire condition→actions : le premier groupe
        /// '#'-séparé porte les conditions s'il contient un verbe
        /// Event_If_*, les suivants les actions.</summary>
        public sealed class Branch
        {
            public List<Cmd> Conds { get; } = new();
            public List<Cmd> Actions { get; } = new();
        }

        /// <summary>Branches du script, dans l'ordre du fichier.</summary>
        public List<Branch> Branches { get; } = new();
        /// <summary>Opérateur Event_If_Level (0 = pas de condition).</summary>
        public int IfLevelOp { get; set; }
        /// <summary>Valeur du check Event_If_Level.</summary>
        public int IfLevelValue { get; set; }
        /// <summary>Cible du premier Event_Transport (0 = aucun).</summary>
        public int TransportNode { get; set; }
        /// <summary>Premier Event_ScreenMessage (0 = aucun).</summary>
        public int ScreenMessageId { get; set; }

        /// <summary>Évalue Event_If_Level comme le natif
        /// (op 1=≥, 2=≤, 3==, 4=≠ ; op 0 ou absent → vrai).</summary>
        public bool LevelOk(int level) => IfLevelOp switch
        {
            1 => level >= IfLevelValue,
            2 => level <= IfLevelValue,
            3 => level == IfLevelValue,
            4 => level != IfLevelValue,
            _ => true,
        };
    }

    /// <summary>Zone d'événement (spawn, revive, trigger) depuis
    /// [event_area] : <c>id,x,y,z,radius,shape,type,facing,desc</c>.</summary>
    public sealed class EventArea
    {
        public int Id { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        public float Radius { get; init; }
        public int Shape { get; init; }
        /// <summary>Type natif : BORN, REVIVE, GATEWAY, NORMAL…</summary>
        public string Type { get; init; } = string.Empty;
        public float Facing { get; init; }
        public string Description { get; init; } = string.Empty;
    }

    /// <summary>Route de patrouille depuis [mob_patrol_mode] —
    /// waypoints (x,z) par groupes de 5 champs.</summary>
    public sealed class PatrolRoute
    {
        public int Id { get; init; }
        public List<(float X, float Z)> Waypoints { get; } = new();
    }

    /// <summary>Scène complète d'une map.</summary>
    public sealed class Scene
    {
        public required string MapCode { get; init; }
        /// <summary>Largeur/hauteur déclarées dans [globe_data]
        /// (col 1/2 — cellules ; pas des unités monde).</summary>
        public int MapWidth { get; set; }
        public int MapHeight { get; set; }
        /// <summary>Col 0 de [globe_data] — fichier fsm de la map.</summary>
        public string FsmFile { get; set; } = string.Empty;
        /// <summary>Col 3 de [globe_data] — version de la map (V7).</summary>
        public string MapVersion { get; set; } = string.Empty;
        public List<MonsterSpawn> Monsters { get; } = new();
        public List<NpcSpawn> Npcs { get; } = new();
        /// <summary>Slots statue (templates 54901-54905) — exclus de
        /// <see cref="Npcs"/>.</summary>
        public List<StatueSlot> StatueSlots { get; } = new();
        public List<Gateway> Gateways { get; } = new();
        public List<EventArea> EventAreas { get; } = new();
        /// <summary>Scripts [event] indexés par leur id (col0).</summary>
        public Dictionary<int, SceneEvent> Events { get; } = new();
        public Dictionary<int, PatrolRoute> Patrols { get; } = new();
        /// <summary>[revive_area] — event-area (col2) → clé revive
        /// (col1).</summary>
        public Dictionary<int, int> ReviveLinks { get; } = new();
        /// <summary>[normal_area] — event-area (col2) → clé normale
        /// (col1).</summary>
        public Dictionary<int, int> NormalLinks { get; } = new();
        /// <summary>Index (col3 de [born_area]) vers <see cref="Positions"/>
        /// — point de spawn des nouveaux personnages.</summary>
        public int BornPositionIndex { get; set; }
        /// <summary>Points de spawn joueur depuis [position] (x,y,z).</summary>
        public List<(float X, float Y, float Z)> Positions { get; } = new();
        /// <summary>Lignes [position] indexées par leur groupe (col7) —
        /// cible des Event_BornNpc/Event_RebornMonster.</summary>
        public Dictionary<int, List<(float X, float Y, float Z, float Facing)>>
            PositionGroups { get; } = new();
        /// <summary>Régions d'étal joueur — [position] dont col7 ∈
        /// [600,609] (CheckStallRegion natif). Type → (X,Y,rayon col4).</summary>
        public Dictionary<int, (float X, float Y, float Radius)>
            StallRegions { get; } = new();
        /// <summary>Sections non gérées (environment_sound,
        /// color_correction, dynamic_block…) : nom → lignes CSV
        /// éclatées — conservées pour un usage client futur.</summary>
        public Dictionary<string, List<string[]>> ExtraSections { get; } = new();
    }

    /// <summary>Parse un fichier de scène (ex : data/scene/s001.ini).</summary>
    public static Scene Load(string path)
    {
        var lines = Vfs.EdenVfs.ReadAllLines(path, EdenEncoding.Big5);
        var scene = new Scene
        {
            MapCode = Path.GetFileNameWithoutExtension(path)
        };

        string? section = null;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim().TrimEnd(',');
            if (line.Length == 0) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }

            var fields = line.Split(',');
            switch (section)
            {
                case "globe_data":
                    // S001.fsm,640,640,V7
                    scene.FsmFile = fields[0];
                    if (fields.Length >= 3)
                    {
                        scene.MapWidth = ParseInt(fields[1]);
                        scene.MapHeight = ParseInt(fields[2]);
                    }
                    if (fields.Length >= 4)
                        scene.MapVersion = fields[3];
                    break;

                case "revive_area":
                    // rowId,reviveKey,eventAreaId (désasm LoadReviveArea
                    // @0x83504b0) — triggerEa → reviveKey.
                    if (fields.Length >= 3)
                    {
                        var key = ParseInt(fields[1]);
                        var ea = ParseInt(fields[2]);
                        if (key != 0 && ea != 0)
                            scene.ReviveLinks[ea] = key;
                    }
                    break;

                case "normal_area":
                    // rowId,normalKey,eventAreaId (LoadNormalArea
                    // @0x8350930 — même forme).
                    if (fields.Length >= 3)
                    {
                        var key = ParseInt(fields[1]);
                        var ea = ParseInt(fields[2]);
                        if (key != 0 && ea != 0)
                            scene.NormalLinks[ea] = key;
                    }
                    break;

                case "born_area":
                    // rowId,?,spawnIndex,desc — aire de naissance.
                    // Format event_area si ≥9 champs avec BORN en col6.
                    if (fields.Length >= 9 &&
                        fields[6].Contains("BORN", StringComparison.OrdinalIgnoreCase))
                    {
                        scene.EventAreas.Add(new EventArea
                        {
                            Id = ParseInt(fields[0]),
                            X = ParseFloat(fields[1]),
                            Y = ParseFloat(fields[2]),
                            Z = ParseFloat(fields[3]),
                            Radius = ParseFloat(fields[4]),
                            Shape = ParseInt(fields[5]),
                            Type = "BORN",
                            Facing = ParseFloat(fields[7]),
                            Description = fields[8],
                        });
                    }
                    else if (fields.Length >= 3)
                    {
                        // born_area compacte : col3 indexe [position].
                        var posIdx = ParseInt(fields[2]);
                        if (posIdx > 0)
                            scene.BornPositionIndex = posIdx;
                    }
                    break;

                case "gateway_area":
                    // rowId,eventAreaId,scriptEventId,linkKey,keyId,desc
                    if (fields.Length >= 6)
                        scene.Gateways.Add(new Gateway
                        {
                            RowId = ParseInt(fields[0]),
                            EventAreaId = ParseInt(fields[1]),
                            ScriptEventId = ParseInt(fields[2]),
                            LinkKey = ParseInt(fields[3]),
                            KeyId = ParseInt(fields[4]),
                            Description = fields[5],
                        });
                    break;

                case "event":
                    // id,type,?,"#:conds:#:acts:",... ,desc — chaque
                    // champ contenant '#' = une branche indépendante
                    // {conditions → actions}.
                    if (fields.Length >= 4)
                    {
                        var ev = new SceneEvent
                        {
                            Id = ParseInt(fields[0]),
                            Script = string.Join(',', fields.Skip(3)),
                        };
                        foreach (var field in fields.Skip(3))
                        {
                            if (!field.Contains('#')) continue;
                            var groups = field.Split('#',
                                StringSplitOptions.RemoveEmptyEntries);
                            if (groups.Length == 0) continue;
                            var br = new SceneEvent.Branch();
                            var firstIsCond = groups[0]
                                .Contains("Event_If_", StringComparison.Ordinal);
                            for (var gi = 0; gi < groups.Length; gi++)
                            {
                                var dest = gi == 0 && firstIsCond
                                    ? br.Conds : br.Actions;
                                foreach (var seg in groups[gi]
                                    .Split(':', StringSplitOptions.RemoveEmptyEntries))
                                {
                                    var t = seg.Split(' ',
                                        StringSplitOptions.RemoveEmptyEntries);
                                    if (t.Length == 0) continue;
                                    var cmd = new SceneEvent.Cmd
                                    {
                                        Name = t[0],
                                        Args = t.Skip(1).Select(ParseInt).ToArray(),
                                    };
                                    dest.Add(cmd);
                                    switch (cmd.Name)
                                    {
                                        case "Event_If_Level":
                                            ev.IfLevelOp = cmd.Args.ElementAtOrDefault(0);
                                            ev.IfLevelValue = cmd.Args.ElementAtOrDefault(1);
                                            break;
                                        case "Event_Transport" when ev.TransportNode == 0:
                                            ev.TransportNode = cmd.Args.ElementAtOrDefault(0);
                                            break;
                                        case "Event_ScreenMessage" when ev.ScreenMessageId == 0:
                                            ev.ScreenMessageId = cmd.Args.ElementAtOrDefault(0);
                                            break;
                                    }
                                }
                            }
                            ev.Branches.Add(br);
                        }
                        scene.Events[ev.Id] = ev;
                    }
                    break;

                case "event_area":
                    // id,x,y,z,radius,shape,type,facing,description
                    if (fields.Length >= 9)
                        scene.EventAreas.Add(new EventArea
                        {
                            Id = ParseInt(fields[0]),
                            X = ParseFloat(fields[1]),
                            Y = ParseFloat(fields[2]),
                            Z = ParseFloat(fields[3]),
                            Radius = ParseFloat(fields[4]),
                            Shape = ParseInt(fields[5]),
                            Type = fields[6],
                            Facing = ParseFloat(fields[7]),
                            Description = fields[8],
                        });
                    break;

                case "position":
                    // id,x,y,z,?,?,facing,groupId,desc
                    if (fields.Length >= 4)
                    {
                        scene.Positions.Add((ParseFloat(fields[1]),
                            ParseFloat(fields[2]), ParseFloat(fields[3])));
                        var grp = fields.Length > 7 ? ParseInt(fields[7]) : 0;
                        if (grp > 0)
                        {
                            if (!scene.PositionGroups.TryGetValue(grp, out var pl))
                                scene.PositionGroups[grp] =
                                    pl = new List<(float, float, float, float)>();
                            pl.Add((ParseFloat(fields[1]),
                                ParseFloat(fields[2]),
                                ParseFloat(fields[3]),
                                fields.Length > 6 ? ParseFloat(fields[6]) : 0));
                            // Région d'étal natif : col7 = type 600-609,
                            // col4 = rayon (CheckStallRegion).
                            if (grp >= 600 && grp <= 609 && fields.Length > 4)
                                scene.StallRegions[grp] = (
                                    ParseFloat(fields[1]),
                                    ParseFloat(fields[2]),
                                    ParseFloat(fields[4]));
                        }
                    }
                    break;

                case "mob_patrol_mode":
                    // id, x,z,0,1,0, x,z,0,1,0, ... (waypoints par 5)
                    if (fields.Length >= 6)
                    {
                        var route = new PatrolRoute { Id = ParseInt(fields[0]) };
                        for (var i = 1; i + 1 < fields.Length; i += 5)
                        {
                            var x = ParseFloat(fields[i]);
                            var z = ParseFloat(fields[i + 1]);
                            if (x != 0 || z != 0)
                                route.Waypoints.Add((x, z));
                        }
                        if (route.Waypoints.Count > 0)
                            scene.Patrols[route.Id] = route;
                    }
                    break;

                case "reborn_monsters":
                    // areaId,monsterId,patrolId,count,respawnSecs,
                    // x,y,z,facing,u9,u10,u11,u12,model,name,...,dropTable,...
                    // (col 13 = code modèle M025 — vérifié sur
                    // s001/s002.ini : col 12 est un flag « 1 »)
                    if (fields.Length >= 14)
                        scene.Monsters.Add(new MonsterSpawn
                        {
                            AreaId = ParseInt(fields[0]),
                            MonsterId = ParseInt(fields[1]),
                            PatrolId = ParseInt(fields[2]),
                            Count = ParseInt(fields[3]),
                            RespawnSeconds = ParseInt(fields[4]),
                            X = ParseFloat(fields[5]),
                            Y = ParseFloat(fields[6]),
                            Z = ParseFloat(fields[7]),
                            Facing = ParseFloat(fields[8]),
                            ModelCode = fields.Length > 13 ? fields[13] : string.Empty,
                            Name = fields.Length > 14 ? fields[14] : string.Empty,
                            DropTableId = fields.Length > 18 ? ParseInt(fields[18]) : 0,
                        });
                    break;

                case "npc":
                    // localId,tid,x,y,facing,motion,param/name,...
                    // Templates 54901-54905 = slots statue (jamais NPCs).
                    if (fields.Length >= 7)
                    {
                        var tid = ParseInt(fields[1]);
                        if (tid is >= 0xD675 and <= 0xD679)
                        {
                            scene.StatueSlots.Add(new StatueSlot
                            {
                                TemplateId = tid,
                                X = ParseFloat(fields[2]),
                                Y = ParseFloat(fields[3]),
                                Facing = ParseFloat(fields[4]),
                                Param = ParseInt(fields[6]),
                            });
                            break;
                        }
                        scene.Npcs.Add(new NpcSpawn
                        {
                            LocalId = ParseInt(fields[0]),
                            NpcId = tid,
                            X = ParseFloat(fields[2]),
                            Y = ParseFloat(fields[3]),
                            Facing = ParseFloat(fields[4]),
                            ModelCode = fields.Length > 5 ? fields[5] : string.Empty,
                            Name = fields.Length > 6 ? fields[6] : string.Empty,
                            Param = ParseInt(fields[6]),
                        });
                    }
                    break;

                default:
                    // Sections client non couvertes par la spec serveur
                    // (environment_sound, color_correction, dynamic_block)
                    // : conservées brutes en lignes CSV éclatées.
                    if (section != null)
                    {
                        if (!scene.ExtraSections.TryGetValue(section, out var list))
                            scene.ExtraSections[section] = list = new List<string[]>();
                        list.Add(fields);
                    }
                    break;
            }
        }
        return scene;
    }

    private static int ParseInt(string s) =>
        int.TryParse(s.Trim(), out var v) ? v : 0;

    private static float ParseFloat(string s) =>
        float.TryParse(s.Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}
