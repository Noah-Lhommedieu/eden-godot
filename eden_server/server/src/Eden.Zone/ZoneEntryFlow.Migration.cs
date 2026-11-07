using System.Collections.Concurrent;
using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Database.Repositories;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Core.Networking.Session;
using Eden.Zone.Links;
using Eden.Core.Logging;

namespace Eden.Zone;

public sealed partial class ZoneEntryFlow
{
    /// <summary>D15 CheckNodeExit <c>[s16 keyId][s16 dstNode]</c> — le
    /// joueur entre dans une event_area GATEWAY. Sémantique prouvée par
    /// désasm de <c>CNE_CZ_ZoneServerCheckNodeExit::Execute</c>
    /// @0x8241960 + <c>LoadGatewayTargetID</c> @0x834fa40 + ctor
    /// <c>CGatewayArea</c> @0x8463c30 :
    ///   1. clé = <c>keyId | (nodeId&lt;&lt;16)</c> cherchée dans la map
    ///      des CGatewayArea — keyId = col4 de la ligne [gateway_area]
    ///      (le client envoie col4, PAS l'eventAreaId : wire observé
    ///      S002 51/100/108 = col4 des lignes 7/3/12) ;
    ///   2. introuvable → log "not a valid event area" + 1499 + bail ;
    ///   3. pas une aire GATEWAY → 1499 + bail ;
    ///   4. TestIn(pos, rayon+4) — sinon 1499 + bail ;
    ///   5. dstNode == 0 → résolution via la table de transitions
    ///      (event script attaché : ScriptEventId ≠ -1 ? col2 : col4) ;
    ///   6. dstNode ≠ 0 → check niveau CNodeData (sinon 5259) puis
    ///      CheckNodeTimes (sinon 5228) ;
    ///   7. keyId ∈ [0x33,0x3B] (51-59) → forward Mission ListDialogue
    ///      (sélecteur d'instance héro, param node*100+dstNode) ;
    ///   8. sinon → TriggerArea(eventKey, dstNode) = exécuter le script
    ///      [event] : Event_If_Level (sinon 5259), Event_Transport
    ///      (migration), Event_ScreenMessage (texte).
    /// Arrivée : la gateway pairée sur la scène cible (même LinkKey ≠ -1,
    /// sinon même ScriptEventId) donne la position d'entrée ; à défaut
    /// le premier point [position]/born de la scène.</summary>
    private async Task HandleNodeExitAsync(NetSession session, EntryState st,
        byte[] payload, CancellationToken token)
    {
        if (payload.Length < 2 || st.CharId == 0) return;
        var key = BitConverter.ToInt16(payload, 0);          // s16 : keyId (col4)
        var extra = payload.Length >= 4                      // s16 : node cible
            ? BitConverter.ToInt16(payload, 2) : (short)0;   //       explicite (0=résoudre)

        var scene = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        var gw = scene?.Gateways.FirstOrDefault(g => g.KeyId == key);
        if (gw == null)
        {
            // Le client envoie la clé du portail où IL SE TROUVE — si
            // notre suivi de node est désynchronisé, l'aire n'existe pas
            // dans la scène suivie. Fallback global désambiguïsé par la
            // position réelle du joueur (TestIn) — seule la scène où il
            // est physiquement peut contenir son point.
            var hits = _services.Scenes.FindGatewaysByKeyId(key).ToList();
            var pick = hits.FirstOrDefault(h =>
                AreaContains(h.Scene, h.Gateway.EventAreaId, st));
            if (pick.Scene == null && hits.Count == 1) pick = hits[0];
            if (pick.Scene == null)
            {
                // Rejet natif : TextOutputIndex 0x5db (=1499 "aire
                // d'événement invalide") — désasm @0x8241aec.
                EdenLog.Write($"[Zone-Entry] CheckNodeExit char=" +
                    $"{st.CharId} key={key} node={st.NodeId} : " +
                    $"{hits.Count} gateway(s) — rejet");
                await SendTextIndexAsync(session, 1499, token);
                return;
            }
            scene = pick.Scene;
            gw = pick.Gateway;
            var realNode = MapCodeToNodeId(scene.MapCode);
            if (realNode != 0 && realNode != st.NodeId)
            {
                EdenLog.Write($"[Zone-Entry] CheckNodeExit char=" +
                    $"{st.CharId} : resync node {st.NodeId} -> " +
                    $"{realNode} (client sur {scene.MapCode})");
                st.NodeId = (short)realNode;
            }
        }

        // CGatewayArea::TestIn(pos, 6.0f) — le joueur doit être
        // réellement dans l'event_area GATEWAY (désasm : distance ≤
        // rayon+6.0 sinon TextOutputIndex(1499) + bail ; la constante
        // passée est 0x40800000). Sans ce test, un client qui spamme
        // des clés provoquait des TP chaînés vers des maps arbitraires
        // — bug live : Aven -> instance S630.
        var area = scene!.EventAreas.FirstOrDefault(a => a.Id == gw.EventAreaId);
        if (area != null && !Inside(area, st))
        {
            EdenLog.Write($"[Zone-Entry] CheckNodeExit char={st.CharId} " +
                $"key={key} node={st.NodeId} : hors aire " +
                $"({st.PosX:F0},{st.PosZ:F0} vs {area.X:F0},{area.Y:F0} " +
                $"r={area.Radius:F0}) — rejet");
            await SendTextIndexAsync(session, 1499, token);
            return;
        }

        // Villes de guilde (501/502) : keyId 60-64 = aires de récolte
        // (pioche/gants/cristal), pas des portails — aucun node cible,
        // l'outil s'utilise par clic droit dans l'aire (voir
        // ZoneEntryFlow.FamilyCity.cs). Pas de rejet 1499 bruyant.
        if (st.NodeId is 501 or 502
            && Eden.Core.Game.Family.FamilyCityRules.IsGatherKey(key))
        {
            EdenLog.Write($"[VDG-Gather] char={st.CharId} entre dans " +
                $"l'aire de récolte key={key} node={st.NodeId} " +
                $"({st.PosX:F0},{st.PosZ:F0})");
            return;
        }

        // Clé de trigger native (CheckNodeExit @0x82419f0) :
        // uVar18 = node<<16 | (col2 == -1 ? keyId(col4) : col2).
        // col2 = -1 → le script d'événement vient de la keyId (col4) ;
        // col2 = 0 → la keyId porte l'eventId à exécuter (les deux cas
        // dégénèrent en « col2 absent → col4 = eventId »).
        var eventLow = (ushort)(gw.ScriptEventId == -1 || gw.ScriptEventId == 0
            ? gw.KeyId : gw.ScriptEventId);
        // Anti-retrigger natif : `if (char+0x7a4 == uVar18) -> 0x665
        // (1637)`. Sans ça le client réémettait le 15 tant que la
        // TriggerAreaWnd restait ouverte → double migration.
        var trigKey = (uint)eventLow | (uint)st.NodeId << 16;
        if (st.LastGatewayKey == trigKey)
        {
            await SendTextIndexAsync(session, 1637, token);
            return;
        }
        st.LastGatewayKey = trigKey;

        // CheckTrigger natif : si la clé porte un événement de scène
        // enregistré, la zone notifie la mission (CSceneEventCmds::
        // ExecuteCommand → CDBAgent::Command "CheckTrigger ServerID=
        // %d NodeEventID= %d SelectID= %hd") — la mission crédite les
        // objectifs liés et répond ClearTriggerAreaID pour débloquer.
        // ServerID = notre node (id du serveur de zone dans le modèle
        // unifié).
        if (scene.Events.ContainsKey(eventLow) && _mission.Connected)
            _ = _mission.SendAgentDbaCommandAsync(st.CharId,
                $"CheckTrigger ServerID={st.NodeId} NodeEventID=" +
                $"{trigKey} SelectID={extra}");

        // keyId ∈ [0x32,0x3B] (50-59) : plage native des déclencheurs
        // AreaTravel — le natif N'exécute PAS le script mais forward
        // un ListDialogue à la mission (sélecteur de donjon) avec
        // dialogueId = node*100 + dstNode et flag u8 = 5 (désasm
        // @0x8241a00 : puVar17[3] = node*100 + sVar15 ; la map
        // Ins+0x5c4 est indexée node*10 + (key−0x32)).
        if (key >= 0x32 && key <= 0x3B)
        {
            if (_mission.Connected)
            {
                // 147 : l'aire 51-59 déclenchée crédite les objectifs
                // AreaTravel (TriggerArea natif @0x8241960 — le trigger
                // id composite node<<16|key est passé à la mission).
                _ = _mission.SendAreaTravelAsync(st.CharId, st.NodeId,
                    key);
                await _mission.SendAsync(127, Concat(
                    BitConverter.GetBytes(st.CharId),        // i32 : uid perso
                    BitConverter.GetBytes(st.NodeId),        // s16 : node courant
                    new byte[] { 5 },                        // u8 : flag 5 (trigger héro)
                    BitConverter.GetBytes(st.NodeId * 100 + extra), // i32 : dlgId
                    BitConverter.GetBytes(0),
                    BitConverter.GetBytes(0)), token);
            }
            EdenLog.Write($"[Zone-Entry] CheckNodeExit char={st.CharId} " +
                $"key={key} : forward mission ListDialogue " +
                $"(dlgId={st.NodeId * 100 + extra})");
            return;
        }

        // Résolution native du node destination : le 2e s16 du wire est
        // la cible explicite ; sinon on exécute le script [event] de la
        // gateway (ScriptEventId ≠ -1 ? col2 : col4 = KeyId) dont la
        // directive Event_Transport porte la destination. Si ni l'un ni
        // l'autre ne donne de cible, KeyId valide comme node = transport
        // direct (cas 東青鳳 : S001 row 92,100,0,-1,311 → node 311).
        int target = 0;
        SceneParser.SceneEvent? ev = null;
        var eventId = eventLow;
        scene.Events.TryGetValue(eventId, out ev);

        if (extra != 0)
        {
            target = extra;
        }
        else if (ev != null)
        {
            // Exécution complète du script [event] — équivalent du
            // TriggerArea natif (CSceneEventCmds::ExecuteCommand →
            // ScriptEntry type 8 côté mission). Chaque branche
            // {conds→actions} est évaluée ; Event_Transport migre.
            // Échec d'une condition Event_If_Level → texte 5259 comme
            // le natif (check de niveau de la destination).
            var anyLevelCond = ev.Branches.Any(b => b.Conds
                .Any(c => c.Name == "Event_If_Level"));
            var levelBlocked = ev.Branches.All(b =>
                b.Conds.Any(c => c.Name == "Event_If_Level" &&
                    !EvalSceneCondSync(st, c)));
            if (await TryOfferDungeonAsync(session, st, scene, ev,
                    gw.EventAreaId, token))
                return;
            var moved = await RunSceneEventAsync(session, st, scene,
                eventId, token);
            if (moved) return;
            if (anyLevelCond && levelBlocked)
            {
                EdenLog.Write($"[Zone-Entry] CheckNodeExit char=" +
                    $"{st.CharId} key={key} : Event_If_Level bloque " +
                    $"lv{st.Level} — 5259");
                await SendTextIndexAsync(session, 5259, token);
                return;
            }
            // Script exécuté sans transport (ScreenMessage, RebornMonster,
            // cinematics…) — trigger non-bloquant comme le natif.
            EdenLog.Write($"[Zone-Entry] CheckNodeExit char={st.CharId} " +
                $"key={key} : event {eventId} exécuté (pas de transport)");
            return;
        }
        else if (gw.ScriptEventId > 0)
        {
            // Event script référencé mais absent de notre parse — le
            // trigger est non-bloquant côté natif (ExecuteCommand no-op
            // si la clé n'a pas de CEventCmd). On log et on sort propre.
            EdenLog.Write($"[Zone-Entry] CheckNodeExit char={st.CharId} " +
                $"key={key} : event {gw.ScriptEventId} sans script — " +
                $"déclenchement ignoré");
            return;
        }
        else if (NodeExists(gw.KeyId))
        {
            target = gw.KeyId;
        }

        if (target <= 0)
        {
            EdenLog.Write($"[Zone-Entry] CheckNodeExit char={st.CharId} " +
                $"key={key} node={st.NodeId} : pas de destination — rejet");
            await SendTextIndexAsync(session, 1499, token);
            return;
        }

        // Check niveau destination (natif : CNodeData+0x78 → 5259).
        var dest = _services.Scenes.GetScene($"S{target:D3}");
        var minLv = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == target)?.MinLevel ?? 0;
        if (minLv > 0 && st.Level < minLv)
        {
            EdenLog.Write($"[Zone-Entry] CheckNodeExit char={st.CharId} " +
                $"key={key} -> node {target} : lv{st.Level} < {minLv} — 5259");
            await SendTextIndexAsync(session, 5259, token);
            return;
        }
        if (dest == null)
        {
            EdenLog.Write($"[Zone-Entry] CheckNodeExit char={st.CharId} " +
                $"key={key} node={st.NodeId} -> S{target:D3} sans scène — rejet");
            await SendTextIndexAsync(session, 1499, token);
            return;
        }

        // Position d'arrivée : l'event_area de la gateway pairée sur la
        // carte cible (même LinkKey, ou même ScriptEventId) — sinon born.
        float dx = 100, dy = 100, dz = 0;
        var pair = dest.Gateways.FirstOrDefault(g =>
            g.LinkKey != -1 && g.LinkKey == gw.LinkKey)
            ?? dest.Gateways.FirstOrDefault(g =>
                gw.ScriptEventId != 0 && g.ScriptEventId == gw.ScriptEventId);
        var arriveArea = pair != null
            ? dest.EventAreas.FirstOrDefault(a => a.Id == pair.EventAreaId)
            : null;
        if (arriveArea != null)
            (dx, dy, dz) = (arriveArea.X, arriveArea.Y, arriveArea.Z);
        else if (dest.Positions.Count > 0)
            (dx, dy, dz) = dest.Positions[0];
        else if (dest.EventAreas.FirstOrDefault(
            a => a.Type == "GATEWAY") is { } ga)
            (dx, dy, dz) = (ga.X, ga.Y, ga.Z);

        EdenLog.Write($"[Zone-Entry] NodeExit char#{st.CharId} " +
            $"node {st.NodeId} key={key} -> {target} ({dx:F1},{dy:F1})" +
            $"{(extra != 0 ? $" extra={extra}" : "")}");

        await MigrateToNodeAsync(session, st, target, dx, dy, dz, token);
    }

    /// <summary>TestIn natif : le point (PosX,PosZ) du joueur est dans le
    /// disque de l'event_area élargi de 6.0 (CheckNodeExit @0x82419f0 —
    /// <c>TestIn(char+0x28, 0x40800000)</c> = +6.0f fixe).</summary>
    private static bool Inside(SceneParser.EventArea area, EntryState st)
    {
        var ddx = st.PosX - area.X; var ddz = st.PosZ - area.Y;
        var reach = area.Radius + 6f;
        return ddx * ddx + ddz * ddz <= reach * reach;
    }

    /// <summary>Vrai si le joueur est dans l'aire gateway de la scène —
    /// utilisé par le fallback global pour identifier la scène réelle.</summary>
    private static bool AreaContains(SceneParser.Scene scene, int eventAreaId,
        EntryState st)
    {
        var a = scene.EventAreas.FirstOrDefault(e => e.Id == eventAreaId);
        return a != null && Inside(a, st);
    }

    /// <summary>Sxxx → nodeId numérique (S001 → 1). 0 si le code n'est
    /// pas de la forme attendue.</summary>
    private static int MapCodeToNodeId(string mapCode) =>
        mapCode.Length == 4 && mapCode[0] == 'S' &&
        int.TryParse(mapCode.AsSpan(1), out var n) ? n : 0;

    /// <summary>Le nodeId existe dans S_Node (transport direct quand la
    /// gateway n'a pas de script event — cas col2=0, KeyId=node).</summary>
    private bool NodeExists(int nodeId) =>
        _services.GameData.Nodes.Any(n => n.Id == nodeId);

    /// <summary>Séquence de migration de node (capture portail) :
    /// `293 [u16 0]` → `563 [s16 node][s16 1]` → `302 [char][00 00 01 01
    /// 12]` → `267 [char][f32 x][f32 y][0x65]` → `313 [s16 node][u8 1]
    /// [f32 x][f32 y][f32 z]`. Réutilisée par les portails (15), le
    /// transport 88 et les commandes texte admin.</summary>
    private async Task MigrateToNodeAsync(NetSession session, EntryState st,
        int target, float dx, float dy, float dz, CancellationToken token)
    {
        // Cast en vol : le TP le brise (287 + release du flag casting)
        // sur l'ancien node — sinon CastingSpell survivait au TP et
        // refusait (279 flag=0) les sorts suivants.
        await BreakCastAsync(session, st, token);
        // Pets invoqués : désinvocation sur l'ancien node puis re-born
        // sur le nouveau — l'enchant source persiste pendant le TP.
        await DespawnPetsAsync(st, -1, -1, token);
        // Duel en cours → TransportCharacterTo* natif appelle
        // DuelFinish(char) : le téléporté perd par forfait.
        await FinishDuelAsync(session, st);
        // CEvaluate::CharacterLeave natif : quitter un nœud trial
        // (type 18) avec un EvalId actif = sortie de l'instance —
        // reset 607, auto-claim si la loterie était ouverte, EvalId=0.
        // AVANT le changement de NodeId (la détection porte sur le
        // nœud quitté, pas la destination).
        var oldNodeType = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == st.NodeId)?.Type ?? 0;
        if (st.EvalId != 0 && oldNodeType == 18 && target != st.NodeId)
            await CharacterLeaveEvaluateAsync(session, st, token);
        // Sortie d'un node de siège (type 19) : le char quitte la
        // croisade — reset char+0xdfc et notification à la mission
        // (TC_BattleCrusadeLeave natif), sauf si la migration vient
        // du join/close (géré par les chemins crusade).
        if (oldNodeType == 19 && st.CrusadeId != 0)
        {
            var cid = st.CrusadeId;
            st.CrusadeId = 0;
            st.CrusadeReturnNode = 0;
            if (_services.Mission is { Connected: true })
                await _services.Mission.SendAgentDbaCommandAsync(0,
                    $"CrusadeLeave crusade_id= {cid} " +
                    $"char_id= {st.CharId}", token);
        }
        // CrossZoneData natif (exec+0xf0/+0xf4/+0xf8 →
        // char+0xa98/+0xa9c/+0xaa0) : quitter un node dont le type
        // n'a pas de retour dédié mémorise (node, position) comme
        // point de retour d'instance — consommé par la sortie du
        // hall familial (176 familyId=-1) et les quits cross-zone.
        if (!EntryState.CrossZoneExcludedTypes.Contains(oldNodeType))
        {
            st.CrossZoneNode = st.NodeId;
            st.CrossZoneX = st.PosX;
            st.CrossZoneY = st.PosZ;
        }
        // Instance du CNodeGroup natif — résolue à chaque entrée de
        // nœud (équipe → copie partagée de l'équipe, solo → copie
        // perso, nœud non-instancé → scène partagée 0). L'ancien
        // groupe est détruit s'il devient orphelin (sortie de
        // donjon, saut d'un donjon à l'autre).
        var prevGroup = st.InstanceGroup;
        st.NodeId = (short)target;
        st.InstanceGroup = ResolveInstanceGroup(st, target);
        if (prevGroup != st.InstanceGroup && prevGroup != 0 &&
            !_states.Values.Any(o => o.InstanceGroup == prevGroup))
            DestroyGroupInstances(prevGroup);
        st.PosX = dx; st.PosZ = dy;
        // Secours hauteur : un appelant qui passe dz=0 (repère sans
        // colonne z) posait le joueur sous les maps intérieures. Les
        // données de scène portent déjà le z résolu (SceneParser)
        // — ceci couvre les chemins qui ne passent pas par elles.
        if (dz == 0)
            dz = _services.Scenes.GetScene($"S{target:D3}")
                ?.FloorZ(dx, dy) ?? 0f;
        st.PosY = dz;
        st.Sent.Clear(); st.EntityPos.Clear();
        st.Replayed.Clear(); st.HandleToEntity.Clear();
        st.HallNpcItems.Clear();
        st.WorkOpBuildingItemId = 0;
        st.WorkOpTaxRate = 0;
        st.FamilyNpcsLoaded = false;
        st.Generated.Clear();
        st.NodeChanged = true;
        st.LastGatewayKey = 0;
        st.InsideAreas.Clear();
        st.MissionDialogs.Clear();
        st.DialogBodyIds = Array.Empty<int>();
        st.DialogServices.Clear();
        // Sauvegarde complète avec le NOUVEAU node et sa position —
        // un kill entre deux migrations ne renvoie plus le joueur
        // sur l'ancienne map (constaté live). Attendue : le point de
        // durabilité précède la migration.
        await PersistStateAsync(st);

        await session.SendAsync(new CNC_CZ_ClientNodeExit(), token);
        // 563 ClientUpdateNodeTimes — le vrai compteur si la map
        // player_node_times est chargée (entrées trial 655+ portées
        // par AddNodeTimes), sinon 1 comme l'ancien stub.
        var nodeTimes = st.NodeTimes != null &&
            st.NodeTimes.TryGetValue(target, out var nt) &&
            nt > 0 ? (ushort)nt : (ushort)1;
        await session.SendAsync(new CNC_CZ_ClientUpdateNodeTimes
        {
            Field1 = (short)target,   // s16 : nœud destination
            Field2 = nodeTimes,       // u16 : entrées cumulées
        }, token);
        // 302 post-migration : mot d'état courant du joueur — base
        // 0x01000000 (« vrai perso », sel=0 op=1) + bit9 0x0200 si le
        // perso migre monté (RideStates persistant entre nodes : un
        // cavalier qui prend un portail reste « monté » pour les
        // records de strafe client — NodifyFlagChange natif renvoie
        // toujours char+0x3a entier). sel=1 posait le bit16 — état FSM
        // fantôme jamais purgé par le client → freeze après portail
        // (bug live). seq = compteur char+0x280 courant du perso.
        await session.SendAsync(
            Codecs.StateFlagWord.BuildCommand(st.CharId,
                PlayerStateWord(st), st.NextAction()), token);
        // Le perso migre monté : le client reconstruit sa scène à
        // l'arrivée et repart à pied sans le modèle de monture — on
        // lui renvoie le 283 mask=2 (+ barre de sorts 545) de la
        // monture courante (état serveur inchangé par la migration).
        await ResendRideAfterMigrationAsync(session, st, token);
        await session.SendAsync(new CNC_CZ_ClientGoto
        {
            Field1 = (int)st.CharId, Field2 = dx,
            Field3 = dy, Field4 = 0x65,
        }, token);
        await session.SendAsync(new CNC_CZ_ClientEnterNodeNodeData
        {
            Field1 = (short)target, Field2 = 1,
            Field3 = dx, Field4 = dy, Field5 = dz,
        }, token);
        // Re-invocation sur le nouveau node — les enchants actifs
        // porteurs de commandes summon re-bornent leur pet ici, et le
        // pet elfe invoqué (status=2) re-born aussi (BornElfPet natif).
        await RespawnPetsAfterMigrationAsync(session, st, token);
        await RespawnElfPetAfterMigrationAsync(session, st, token);
        // Auto-leave de la file de recrutement si la destination est
        // un type d'instance hors {5,7,12} (zone @0x08414960 — post
        // EnterNodeNodeData, comme ici).
        await RecruitAutoLeaveOnEnterAsync(session, st, target, token);
    }

    /// <summary>316 ClientTextOutputIndex <c>[s16 index][str fmt]</c> —
    /// message localisé client (T_TextIndex). Preuve : désasm
    /// <c>CCharacter::TextOutputIndex(short, char, const char*, ...)</c>
    /// @0x8429b20 — index à +4, chaîne formatée à +8 ; le param canal
    /// (-105) n'entre pas dans le corps sérialisé (Serializer
    /// @0x80d33c0 : `&lt;&lt;short` puis la string).</summary>
    private static Task SendTextIndexAsync(NetSession session, short index,
        CancellationToken token) =>
        session.SendAsync(new CNC_CZ_ClientTextOutputIndex
        {
            Field1 = index,   // s16 : index dans T_TextIndex
            Field2 = "",      // str : arguments de format (vide)
        }, token);

    /// <summary>Broadcast du 316 TextIndex à tous les persos du node —
    /// équivalent de l'Announce mission→world natif (TC_Announce :
    /// CNC_MW_WorldTextCommand "Announce id" vers le serveur du node
    /// de l'exécutant, rediffusé canal système). args = contenu de
    /// Field2 (chaîne d'arguments du format client, ex. "%s,+%d,%s"
    /// de ReplaceOrderParm).</summary>
    private async Task BroadcastTextIndexAsync(int nodeId, int inst,
        short index, CancellationToken token, string args = "")
    {
        var (op, payload) = PreparePacket(new CNC_CZ_ClientTextOutputIndex
        { Field1 = index, Field2 = args });
        foreach (var s in _services.Sessions.All)
        {
            if (!_states.TryGetValue(s.SessionId, out var ost) ||
                ost.NodeId != nodeId || ost.InstanceGroup != inst ||
                !ost.Migrated) continue;
            // session morte : SendPreparedToAsync avale — le sweep
            // s'en charge.
            await SendPreparedToAsync(s, op, payload, token);
        }
    }
}
