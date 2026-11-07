using Eden.Core.Data;
using System.Text;

namespace Eden.Core.Data.Parsers;

/// <summary>
/// Parser des fichiers <c>Data/scene/Sxxx.ini</c> — données de scène par map :
/// spawns de monstres, PNJ, zones d'événements, portes, patrouilles.
/// Format : sections <c>[nom],</c> puis lignes CSV.
/// </summary>
public static class SceneParser
{
    static SceneParser()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Spawn de monstre depuis la section [reborn_monsters].</summary>
    public sealed class MonsterSpawn
    {
        public int AreaId { get; init; }
        public int MonsterId { get; init; }
        public int PatrolId { get; init; }
        /// <summary>Temps de respawn en secondes.</summary>
        public int RespawnSeconds { get; init; }
        /// <summary>Nombre de monstres créés par ce point de reborn (col 3).</summary>
        public int Count { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        /// <summary>Hauteur col7 — quand le fichier donne 0 elle est
        /// remplacée post-parse par <see cref="Scene.FloorZ"/> (sol
        /// intérieur sinon invisible sous la map).</summary>
        public float Z { get; set; }
        /// <summary>Col8 — lue %d par le natif : ce n'est PAS un
        /// facing mais la clef d'événement à la mort (+0x28 du
        /// CRebornMonsterInfo zone → CRebornMonster+0x34, évaluée par
        /// OneDie → ExecuteCommand). Conservée float pour les
        /// structures épinglées ; sans effet visible (symétriques).</summary>
        public float Facing { get; init; }
        /// <summary>Clef d'événement à la mort (col8, int natif) —
        /// cristaux de place forte : S005 bleu=3 / rouge=4 (swap
        /// cristal via Event_RebornMonster).</summary>
        public int DeathEventId { get; init; }
        /// <summary>Clef d'événement à la naissance (col9, lignes
        /// ≥19 champs → +0x2c zone → CRebornMonster+0x38, ScriptEntry
        /// '\b' au Born) — timers d'invasion : S005 118→1, 119→2.</summary>
        public int BornEventId { get; init; }
        /// <summary>Durée de vie du reborn en secondes (col11, lignes
        /// ≥20 champs → +0x34 zone) — le groupe meurt de vieillesse à
        /// born+TTL et sa clef col8 s'évalue : 119 vit 420 s → event 2
        /// (« 7分後關閉 »), 117 vit 1200 s → event 4 (retour bleu).</summary>
        public int LifetimeSeconds { get; init; }
        public string ModelCode { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        /// <summary>Drapeau d'annonce — short +0x48 du CRebornMonster
        /// (<c>GetAllRebornMonsterInfo</c> @0x083688a0, col 18 des lignes
        /// à 24+ champs ; 1 ou 2 sur les lignes d'événement qui annoncent
        /// (70-S « Collectionneur d'Énergie », invasions de pirates…),
        /// 0 sur les world boss fixes — sémantique 1 vs 2 non prouvée).
        /// Ancien nom erroné : DropTableId.</summary>
        public int AnnounceFlag { get; init; }
        /// <summary>Index <c>t_textindex</c> de l'annonce d'apparition —
        /// int +0x4c du CRebornMonster (col 19 : 6044-6053 sur les 70-S,
        /// 0 = pas d'annonce). Diffusé à tous les clients au spawn
        /// (<c>CZoneServer::BroadCastMessageToAllCharacter(short,…)</c>
        /// @0x08177b50 → CNC_CZ_ClientTextOutputIndex 316).</summary>
        public int AnnounceTextId { get; init; }
        /// <summary>Shorts non nuls des colonnes suivantes (vector
        /// +0x50 du CRebornMonster, 4 max) — sémantique inconnue,
        /// toujours 0 dans les données US.</summary>
        public short[] ExtraShorts { get; init; } = Array.Empty<short>();
        /// <summary>Ligne « née au boot » — octet +0x38 du CRebornMonster
        /// natif, lu dans <c>GetAllRebornMonsterInfo</c> @0x083688a0 à
        /// l'index qui dépend du nombre de champs : col10 si &lt;19,
        /// col11 si 19, col12 si ≥20 (les lignes à 24 champs des
        /// données US). false = groupe dormant, n'apparaît que via
        /// Event_RebornMonster (ex. cristaux rouges / escouades légion
        /// des « 據點水晶 » — bleus=1, rouges/élites=0).</summary>
        public bool InitiallyActive { get; init; }
        public int[] Raw { get; init; } = Array.Empty<int>();
    }

    /// <summary>Queue d'une ligne reborn_monsters (champs après le
    /// flag « né au boot »).</summary>
    internal readonly record struct RebornTail(int Flag, int TextId,
        short[] Extra);

    /// <summary>Relecture fidèle de la fin de
    /// <c>GetAllRebornMonsterInfo</c> @0x083688a0 : après le modèle
    /// (colonne d'index 13/14/15 selon le nombre de champs), lit
    /// (si assez de champs) un int +0x3c, deux ints +0x40/+0x44, puis
    /// le couple short +0x48 (drapeau d'annonce) / int +0x4c (index
    /// textindex d'annonce), puis ≤4 shorts non nuls (vector +0x50).
    /// Chaque palier est conditionné par le nombre de champs (&gt;13,
    /// &gt;15, &gt;17, &gt;20).</summary>
    internal static RebornTail ParseRebornTail(string[] f)
    {
        var n = f.Length;
        var i = n < 19 ? 13 : n == 19 ? 14 : 15;
        if (n >= 14) i++;                       // int +0x3c
        if (n >= 16) i += 2;                    // ints +0x40/+0x44
        var flag = 0; var text = 0;
        if (n >= 18 && i + 1 < n)
        {
            flag = ParseInt(f[i]);              // short +0x48
            text = ParseInt(f[i + 1]);          // int +0x4c
            i += 2;
        }
        var extra = new List<short>();
        if (n > 20)
            for (var k = 0; k < 4 && i + k < n; k++)
                if (ParseInt(f[i + k]) is var v && v != 0)
                    extra.Add((short)v);
        return new RebornTail(flag, text, extra.ToArray());
    }

    /// <summary>Spawn de PNJ depuis la section [npc] — format natif
    /// <c>localId,tid,x,y,facing,motion,param,...</c> (prouvé par
    /// <c>CStatueDBO::GetStatueInThisServer</c> @0x8370ca0 qui lit
    /// tok2=x, tok3=y, tok4=facing, tok6=param ; il n'y a PAS de z).</summary>
    public sealed class NpcSpawn
    {
        /// <summary>Id local négatif utilisé par la scène.</summary>
        public int LocalId { get; init; }
        public int NpcId { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        /// <summary>Pas de colonne z dans le format natif — résolue
        /// post-parse par <see cref="Scene.FloorZ"/> (point porteur
        /// de z le plus proche : sols intérieurs, ex. S100 ~16).</summary>
        public float Z { get; set; }
        public float Facing { get; init; }
        public string ModelCode { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        /// <summary>Token 6 brut — nom pour les NPCs ordinaires,
        /// paramètre numérique (4000) pour les slots statue.</summary>
        public int Param { get; init; }
    }

    /// <summary>Slot de statue de famille — record [npc] dont le
    /// template ∈ [0xd675, 0xd679] (54901-54905). Jamais spawné comme
    /// NPC (<c>LoadStatueFromDB</c> crée un slot vide ; la statue ne
    /// naît que si <c>statues_scenes</c> la peuple).</summary>
    public sealed class StatueSlot
    {
        public int TemplateId { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Facing { get; init; }
        /// <summary>Col6 du record (4000 dans S501/S502) — champ
        /// +0x14 du CStatueDB natif, 6e valeur du CSV du 298.</summary>
        public int Param { get; init; }
    }

    /// <summary>Porte vers une autre map depuis [gateway_area].
    /// Sémantique prouvée par désassemblage de
    /// <c>CEventAreaDBO::LoadGatewayTargetID</c> (@0x834fa40, sscanf
    /// "%hd" sur tokens 1,2,4) + <c>CZoneServer</c> load @0x8187b4f +
    /// <c>CNE_CZ_ZoneServerCheckNodeExit::Execute</c> @0x8241960 :
    ///   col0 = rowId, col1 = <b>eventAreaId</b> (jointure [event_area]
    ///   pour la géométrie TestIn — map native SGatewaydata indexée par
    ///   <c>(nodeId&lt;&lt;16)|eventAreaId</c>),
    ///   col2 = <b>scriptEventId</b> (id du script [event] à déclencher ;
    ///   -1 → c'est col4 qui sert d'event id ; 0 → pas de script),
    ///   col3 = linkKey/condition (90112 = groupe de liaison, -1/0),
    ///   col4 = <b>keyId</b> — la clé s16 que le client ENVOIE dans
    ///   CheckNodeExit : le ctor CGatewayArea écrit
    ///   <c>*(int*)db = node|col4</c> comme clé de la map +0x25c, et le
    ///   wire observé matche col4 (S002 : 51/100/108 = col4 des lignes
    ///   — jamais col1 eventAreaId ni col2).</summary>
    public sealed class Gateway
    {
        /// <summary>Id de ligne dans la table (col0).</summary>
        public int RowId { get; init; }
        /// <summary>Id de l'event_area GATEWAY déclencheur (col1) —
        /// jointure vers [event_area] pour la position/rayon TestIn.</summary>
        public int EventAreaId { get; init; }
        /// <summary>Id du script [event] déclenché (col2, natif "col3"
        /// → CGatewayArea+0x28) ; -1 = l'event id est KeyId ; 0 = pas de
        /// script (transport direct vers KeyId-as-node).</summary>
        public int ScriptEventId { get; init; }
        /// <summary>Clé de liaison/condition (col3, natif "col4" :
        /// 90112 = groupe pairé, -1/0 = aucun) — CGatewayArea+0x3c.</summary>
        public int LinkKey { get; init; }
        /// <summary>Clé s16 envoyée par le client dans NCID 15
        /// (col4, natif "col5" → clé basse de la map CGatewayArea).</summary>
        public int KeyId { get; init; }
        public string Description { get; init; } = string.Empty;
    }

    /// <summary>Script de la section [event] d'une scène :
    /// <c>id,type,?,"#:Event_If_*…:#:Event_*…:",desc</c>. Preuve :
    /// désasm <c>lapis::Event_If_Level</c> @0x80ddcf0 (mission) —
    /// <c>Event_If_Level &lt;op&gt; &lt;val&gt; &lt;0&gt;</c> avec
    /// op 1 = level≥val, 2 = level≤val, 3 = level==val, 4 = level≠val ;
    /// <c>Event_Transport &lt;node&gt; &lt;?&gt; &lt;0&gt;</c> =
    /// migration vers le node cible (table m_mNodeToInstances) ;
    /// <c>Event_ScreenMessage &lt;textId&gt;</c> = message d'écran.</summary>
    public sealed class SceneEvent
    {
        /// <summary>Id de l'event (col0 — clé composite (node&lt;&lt;16)|id).</summary>
        public int Id { get; init; }
        /// <summary>Script brut (segments "#:Event_X a b c:").</summary>
        public string Script { get; init; } = string.Empty;
        /// <summary>Une commande de script de scène (verbe + args
        /// entiers). Les scripts [event] sont des segments
        /// "#:Event_X a b c:Event_Y d:" — le natif les exécute via
        /// CSceneEventCmds::ExecuteCommand côté mission
        /// (ScriptEntry type 8, désasm mission/decompiled.c).</summary>
        public sealed class Cmd
        {
            /// <summary>Verbe (Event_Transport, Event_If_Level…).</summary>
            public required string Name { get; init; }
            /// <summary>Arguments entiers du verbe.</summary>
            public required int[] Args { get; init; }
            /// <summary>Jetons bruts après le verbe (les verbes à
            /// argument texte — <c>Event_PlaySound s99111 168 0</c> —
            /// perdent leur nom dans <see cref="Args"/> : ParseInt → 0).</summary>
            public string[] Words { get; init; } = Array.Empty<string>();
        }

        /// <summary>Paire condition→actions. Chaque champ virgule du
        /// script est "#:cond1:cond2:#:act1:act2:" : le premier groupe
        /// '#'-séparé porte les conditions, les suivants les actions.
        /// Natif ExecuteCommand @0x80c3620 : l'événement = une LISTE
        /// de paires ; les conds d'une paire doivent toutes passer
        /// pour que ses actions jouent — sinon on teste la paire
        /// suivante (les paires sont indépendantes : ex. S011 ev24 =
        /// RanDomVariable puis un embranchement par valeur).</summary>
        public sealed class Branch
        {
            /// <summary>Conditions Event_If_* (toutes doivent passer).</summary>
            public List<Cmd> Conds { get; } = new();
            /// <summary>Actions Event_* exécutées si Conds passent.</summary>
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

        /// <summary>Évalue Event_If_Level comme le natif (op 1=≥,2=≤,3==,4=≠ ;
        /// op 0 ou absent → toujours vrai).</summary>
        public bool LevelOk(int level) => IfLevelOp switch
        {
            1 => level >= IfLevelValue,
            2 => level <= IfLevelValue,
            3 => level == IfLevelValue,
            4 => level != IfLevelValue,
            _ => true,
        };
    }

    /// <summary>Zone d'événement (spawn, revive, trigger) depuis [event_area].</summary>
    public sealed class EventArea
    {
        public int Id { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Z { get; init; }
        public float Radius { get; init; }
        public int Shape { get; init; }
        public string Type { get; init; } = string.Empty;
        public float Facing { get; init; }
        public string Description { get; init; } = string.Empty;
    }

    /// <summary>Route de patrouille depuis [mob_patrol_mode].</summary>
    public sealed class PatrolRoute
    {
        public int Id { get; init; }
        public List<(float X, float Z)> Waypoints { get; } = new();
        /// <summary>Colonne « fixe » de chaque point (4e valeur du
        /// groupe de 5). Observé sur les donjons (~85 % de mobs
        /// immobiles) : tous les points à 1 = emplacements de
        /// formation (un par mob, le mob y reste) ; un point à 0 après
        /// le premier = vrai chemin à parcourir.</summary>
        public List<bool> Fixed { get; } = new();
        /// <summary>Le groupe reste sur place (emplacements fixes ou
        /// point unique).</summary>
        public bool IsStationary =>
            Waypoints.Count <= 1 || Fixed.All(f => f);
    }

    /// <summary>Scène complète d'une map.</summary>
    public sealed class Scene
    {
        public required string MapCode { get; init; }
        public int MapWidth { get; set; }
        public int MapHeight { get; set; }
        /// <summary>Géométrie déclarée par [globe_data] col0 (« S101.nif »
        /// → « S101 »), en majuscules ; vide si absente. Les scènes
        /// solo/héroïque/event (S701, S603, S801…) partagent la géométrie
        /// — et donc la grille .smp — d'un donjon multi (S101, S201…) :
        /// la grille se cherche sous ce code, pas sous celui du node.</summary>
        public string GlobeCode { get; set; } = string.Empty;
        public List<MonsterSpawn> Monsters { get; } = new();
        public List<NpcSpawn> Npcs { get; } = new();
        /// <summary>Slots statue de famille (templates 54901-54905)
        /// — exclus de <see cref="Npcs"/>.</summary>
        public List<StatueSlot> StatueSlots { get; } = new();
        public List<Gateway> Gateways { get; } = new();
        public List<EventArea> EventAreas { get; } = new();
        /// <summary>Scripts [event] indexés par leur id (col0).</summary>
        public Dictionary<int, SceneEvent> Events { get; } = new();
        public Dictionary<int, PatrolRoute> Patrols { get; } = new();
        /// <summary>`[revive_area]` — clé de revive (col1) → event-area
        /// de réapparition (col2). Inverse de la map native
        /// `LoadReviveArea` (ea→clé) : le `revive_area_id` persisté
        /// résout directement la position de respawn.</summary>
        public Dictionary<int, int> ReviveLinks { get; } = new();
        /// <summary>`[normal_area]` — clé d'aire normale (col1,
        /// persistée dans `last_normal_area_id`) → event-area (col2).
        /// Sert à convertir `last_normal_area_id` en position.</summary>
        public Dictionary<int, int> NormalLinks { get; } = new();
        /// <summary>Index (col3 de `[born_area]`) vers `Positions` —
        /// point de spawn des nouveaux personnages.</summary>
        public int BornPositionIndex { get; set; }
        /// <summary>Points de spawn joueur depuis [position] (x,y,z).</summary>
        public List<(float X, float Y, float Z)> Positions { get; } = new();
        /// <summary>Lignes [position] indexées par leur id de groupe de
        /// spawn (col7) — cible des Event_BornNpc/Event_RebornMonster :
        /// `Event_BornNpc npcId group` fait apparaître le NPC aux
        /// positions dont col7 == group (désasm mission : BornNpc
        /// utilise l'aire/position du groupe, ex. S601 "裝飾用NPC" pos
        /// col7=165 → Event_BornNpc 52062 165 0).</summary>
        public Dictionary<int, List<(float X, float Y, float Z, float Facing)>>
            PositionGroups { get; } = new();
        /// <summary>Régions d'étal joueur — lignes [position] dont
        /// col7 ∈ [600,609] (désasm <c>CheckStallRegion</c> : la map
        /// native CPositionData est indexée par (node&lt;&lt;16)|type,
        /// dist² X/Z testée contre le rayon col4). Type → (X,Y,rayon).
        /// S001 en définit trois (600/601/602).</summary>
        public Dictionary<int, (float X, float Y, float Radius)>
            StallRegions { get; } = new();

        /// <summary>Hauteur de sol la plus proche de (x, y) parmi les
        /// points porteurs de z de la scène ([position], [event_area],
        /// [reborn_monsters] à z≠0). Le format [npc] n'a pas de
        /// hauteur : sans ce secours les entités/téléportations à
        /// z=0 finissent sous les maps intérieures sans heightfield
        /// (S100 « Andrew V. Mine » : sol ~16, position/event_area à
        /// z≈16). 0 s'il n'y a aucun point porteur.</summary>
        /// <summary>Distance max (unités) entre un monstre et le point de
        /// référence z le plus proche pour que ce z soit crédible.</summary>
        private const float MobZMaxDist = 40f;

        /// <summary>Z d'un monstre de reborn : <see cref="FloorZ"/> seulement
        /// s'il est crédible. Les donjons n'ont que 3-9 points porteurs de z
        /// pour des dizaines de mobs répartis sur plusieurs étages (S105 :
        /// z 63..168, S221 : 321..330) — le point le plus proche pouvait
        /// être à 100 u et 50-100 u trop haut/bas, et le client (qui ne
        /// recale le z serveur que s'il est à ≤ 4 u du sol) posait alors
        /// le mob en l'air ou sur un autre étage. Scène plate (écart de z
        /// ≤ 6) ou point à ≤ 40 u : on garde ; sinon 0 = « inconnu », le
        /// client snappe alors le sol sous le mob.</summary>
        public float MobFloorZ(float x, float y)
        {
            float zMin = float.MaxValue, zMax = float.MinValue;
            var bestD = float.MaxValue;
            void Offer(float px, float py, float pz)
            {
                zMin = Math.Min(zMin, pz); zMax = Math.Max(zMax, pz);
                var dx = px - x; var dy = py - y;
                bestD = Math.Min(bestD, dx * dx + dy * dy);
            }
            foreach (var (px, py, pz) in Positions) Offer(px, py, pz);
            foreach (var a in EventAreas) Offer(a.X, a.Y, a.Z);
            if (bestD == float.MaxValue) return 0f;
            if (zMax - zMin <= 6f) return FloorZ(x, y);
            return bestD <= MobZMaxDist * MobZMaxDist ? FloorZ(x, y) : 0f;
        }

        public float FloorZ(float x, float y)
        {
            var best = 0f;
            var bestD = float.MaxValue;
            void Offer(float px, float py, float pz)
            {
                var dx = px - x; var dy = py - y;
                var d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = pz; }
            }
            foreach (var (px, py, pz) in Positions) Offer(px, py, pz);
            foreach (var a in EventAreas) Offer(a.X, a.Y, a.Z);
            foreach (var m in Monsters) if (m.Z != 0) Offer(m.X, m.Y, m.Z);
            return best;
        }
    }

    /// <summary>Parse un fichier de scène (ex: S001.ini).</summary>
    public static Scene Parse(string path)
    {
        var big5 = EdenEncoding.Big5;
        var lines = File.ReadAllLines(path, big5);
        var scene = new Scene { MapCode = Path.GetFileNameWithoutExtension(path) };

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
                    if (fields.Length >= 3)
                    {
                        scene.MapWidth = int.TryParse(fields[1], out var w) ? w : 0;
                        scene.MapHeight = int.TryParse(fields[2], out var h) ? h : 0;
                        scene.GlobeCode = Path.GetFileNameWithoutExtension(
                            fields[0].Trim()).ToUpperInvariant();
                    }
                    break;

                case "revive_area":
                    // rowId,reviveKey,eventAreaId (désasm LoadReviveArea
                    // @0x83504b0 : sscanf %hd sur champs 1/2 → map native
                    // [(node<<16)|col2] = (node<<16)|col1 — event-area
                    // traversée → clé revive persistée). On stocke le
                    // même sens : triggerEa → reviveKey ; le spawn
                    // résout reviveKey → event-area BORN/GATEWAY du node.
                    if (fields.Length >= 3)
                    {
                        var key = ParseInt(fields[1]);
                        var ea = ParseInt(fields[2]);
                        if (key != 0 && ea != 0)
                            scene.ReviveLinks[ea] = key;
                    }
                    break;

                case "normal_area":
                    // rowId,normalKey,eventAreaId (désasm LoadNormalArea
                    // @0x8350930 — même forme : map[(node<<16)|col2] =
                    // (node<<16)|col1 — traverser l'event-area col2
                    // enregistre la clé NORMALE col1 dans
                    // last_normal_area_id).
                    if (fields.Length >= 3)
                    {
                        var key = ParseInt(fields[1]);
                        var ea = ParseInt(fields[2]);
                        if (key != 0 && ea != 0)
                            scene.NormalLinks[ea] = key;
                    }
                    break;

                case "born_area":
                    // rowId,?,spawnIndex,desc — aire de naissance des
                    // nouveaux persos (désasm InitEventArea type 1=BORN).
                    // Le format est celui d'event_area si ≥9 champs,
                    // sinon born_rowId → index dans [position].
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
                        // born_area compacte : rowId,?,?,desc — le 3e
                        // champ indexe la section [position] (point de
                        // spawn initial de la map).
                        var posIdx = ParseInt(fields[2]);
                        if (posIdx > 0)
                            scene.BornPositionIndex = posIdx;
                    }
                    break;

                case "gateway_area":
                    // rowId,eventAreaId,scriptEventId,linkKey,keyId,desc
                    // (natif : sscanf %hd sur tokens 1,2,4 —
                    // LoadGatewayTargetID @0x834fcd0-0x834fd44 ; col4
                    // = clé wire CheckNodeExit, col2 = event script.)
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
                    // id,type,?,"#:conds:#:acts:","#:conds:#:acts:",...
                    // ,desc — chaque champ qui contient '#' = une
                    // branche indépendante {conditions → actions}
                    // (natif : liste de CEventCmd par événement).
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
                            // Le groupe 0 n'est une condition que s'il
                            // contient un verbe Event_If_* — sinon tout
                            // le champ est des actions (branche
                            // toujours vraie).
                            var firstIsCond = groups[0]
                                .Contains("Event_If_",
                                    StringComparison.Ordinal);
                            for (var gi = 0; gi < groups.Length; gi++)
                            {
                                var dest = gi == 0 && firstIsCond
                                    ? br.Conds : br.Actions;
                                foreach (var seg in groups[gi]
                                    .Split(':', StringSplitOptions
                                        .RemoveEmptyEntries))
                                {
                                    var t = seg.Split(' ',
                                        StringSplitOptions
                                            .RemoveEmptyEntries);
                                    if (t.Length == 0) continue;
                                    var cmd = new SceneEvent.Cmd
                                    {
                                        Name = t[0],
                                        Args = t.Skip(1)
                                            .Select(ParseInt).ToArray(),
                                        Words = t.Skip(1).ToArray(),
                                    };
                                    dest.Add(cmd);
                                    switch (cmd.Name)
                                    {
                                        case "Event_If_Level":
                                            ev.IfLevelOp =
                                                cmd.Args.ElementAtOrDefault(0);
                                            ev.IfLevelValue =
                                                cmd.Args.ElementAtOrDefault(1);
                                            break;
                                        case "Event_Transport"
                                            when ev.TransportNode == 0:
                                            ev.TransportNode =
                                                cmd.Args.ElementAtOrDefault(0);
                                            break;
                                        case "Event_ScreenMessage"
                                            when ev.ScreenMessageId == 0:
                                            ev.ScreenMessageId =
                                                cmd.Args.ElementAtOrDefault(0);
                                            break;
                                    }
                                }
                            }
                            // Une branche sans premier groupe de
                            // conditions (champ = "#:act1:act2:") est
                            // conservée avec Conds vide = toujours vrai.
                            ev.Branches.Add(br);
                        }
                        scene.Events[ev.Id] = ev;
                    }
                    break;

                case "event_area":
                    // id,x,y,z,radius,shape,type,facing,description
                    // — desc optionnelle : les lignes s28x finissent
                    // en « ,, » que le TrimEnd réduit à 8 champs.
                    if (fields.Length >= 8)
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
                            Description = fields.Length > 8
                                ? fields[8] : "",
                        });
                    break;

                case "position":
                    // id,x,y,z,?,?,facing,groupId,desc — point
                    // d'arrivée joueur + groupe de spawn des events
                    if (fields.Length >= 4)
                    {
                        scene.Positions.Add((ParseFloat(fields[1]),
                            ParseFloat(fields[2]), ParseFloat(fields[3])));
                        var grp = fields.Length > 7
                            ? ParseInt(fields[7]) : 0;
                        if (grp > 0)
                        {
                            if (!scene.PositionGroups.TryGetValue(grp,
                                    out var pl))
                                scene.PositionGroups[grp] =
                                    pl = new List<(float, float, float,
                                        float)>();
                            pl.Add((ParseFloat(fields[1]),
                                ParseFloat(fields[2]),
                                ParseFloat(fields[3]),
                                fields.Length > 6
                                    ? ParseFloat(fields[6]) : 0));
                            // Région d'étal natif : col7 = type
                            // 600-609, col4 = rayon (CheckStallRegion
                            // zone/decompiled.c — w de CPositionData).
                            if (grp >= 600 && grp <= 609 &&
                                fields.Length > 4)
                                scene.StallRegions[grp] = (
                                    ParseFloat(fields[1]),
                                    ParseFloat(fields[2]),
                                    ParseFloat(fields[4]));
                        }
                    }
                    break;

                case "mob_patrol_mode":
                    // id, x,z,0,1,0, x,z,0,1,0, ... (waypoints par groupes de 5)
                    if (fields.Length >= 6)
                    {
                        var route = new PatrolRoute { Id = ParseInt(fields[0]) };
                        for (int i = 1; i + 1 < fields.Length; i += 5)
                        {
                            var x = ParseFloat(fields[i]);
                            var z = ParseFloat(fields[i + 1]);
                            if (x != 0 || z != 0)
                            {
                                route.Waypoints.Add((x, z));
                                route.Fixed.Add(i + 3 < fields.Length &&
                                    ParseInt(fields[i + 3]) == 1);
                            }
                        }
                        if (route.Waypoints.Count > 0)
                            scene.Patrols[route.Id] = route;
                    }
                    break;

                case "reborn_monsters":
                    // Format natif (GetAllRebornMonsterInfo @0x083688a0) :
                    // col0 id, col1 monsterId, col2 patrol, col3 count,
                    // col4 respawn, col5-7 xyz, col8 = clef d'event à la
                    // mort (int, PAS facing), col9 = clef d'event à la
                    // naissance (n≥19), col11 = délai (n≥20) ; le flag
                    // « né au boot » (reborn+0x38) = col10 si <19 champs,
                    // col11 si 19, col12 si ≥20 ; col13 = modèle,
                    // col14 = nom d'affichage, col18 = table de drop.
                    if (fields.Length >= 13)
                    {
                        var bornIdx = fields.Length < 19 ? 10
                            : fields.Length == 19 ? 11 : 12;
                        var announce = ParseRebornTail(fields);
                        scene.Monsters.Add(new MonsterSpawn
                        {
                            AreaId = ParseInt(fields[0]),
                            MonsterId = ParseInt(fields[1]),
                            PatrolId = ParseInt(fields[2]),
                            Count = ParseInt(fields[3]),
                            RespawnSeconds = ParseInt(fields[4]),
                            X = ParseFloat(fields[5]),
                            Y = ParseFloat(fields[6]),
                            // col7 = cap en radians sur les 20 833 lignes des scènes
                            // (jamais une hauteur) : z résolu post-parse (FloorZ).
                            Z = 0f,
                            Facing = ParseFloat(fields[7]),
                            DeathEventId = ParseInt(fields[8]),
                            BornEventId = fields.Length >= 19
                                ? ParseInt(fields[9]) : 0,
                            LifetimeSeconds = fields.Length >= 20
                                ? ParseInt(fields[11]) : 0,
                            InitiallyActive =
                                fields.Length > bornIdx &&
                                ParseInt(fields[bornIdx]) != 0,
                            ModelCode = fields.Length > 13 ? fields[13] : string.Empty,
                            Name = fields.Length > 14 ? fields[14] : string.Empty,
                            AnnounceFlag = announce.Flag,
                            AnnounceTextId = announce.TextId,
                            ExtraShorts = announce.Extra,
                        });
                    }
                    break;

                case "npc":
                    // Format natif : localId,tid,x,y,facing,motion,
                    // param/name,... — pas de colonne z (désasm
                    // GetStatueInThisServer @0x8370ca0). Les templates
                    // 54901-54905 sont des SLOTS DE STATUE, jamais des
                    // NPCs — routés vers StatueSlots.
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
                            Z = 0,
                            Facing = ParseFloat(fields[4]),
                            ModelCode = fields.Length > 5 ? fields[5] : string.Empty,
                            Name = fields.Length > 6 ? fields[6] : string.Empty,
                            Param = ParseInt(fields[6]),
                        });
                    }
                    break;
            }
        }
        // Les enregistrements sans hauteur ([npc] n'en a jamais,
        // [reborn_monsters] met parfois 0) reçoivent le z du point de
        // scène le plus proche — sinon ils spawnent sous les maps
        // intérieures (sol S100 ≈ 16). Mobs d'abord : leurs z résolus
        // enrichissent ensuite les candidats des NPC.
        foreach (var m in scene.Monsters)
            if (m.Z == 0) m.Z = scene.MobFloorZ(m.X, m.Y);
        foreach (var n in scene.Npcs)
            if (n.Z == 0) n.Z = scene.FloorZ(n.X, n.Y);
        return scene;
    }

    private static int ParseInt(string s) =>
        int.TryParse(s.Trim(), out var v) ? v : 0;

    private static float ParseFloat(string s) =>
        float.TryParse(s.Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}
