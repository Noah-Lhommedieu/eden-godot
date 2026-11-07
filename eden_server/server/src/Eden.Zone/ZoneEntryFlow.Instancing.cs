using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Eden.Core.Logging;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Instancing des nœuds de donjon — portage du <c>CNodeGroup</c>
/// natif : chaque équipe (et chaque perso solo) possède sa copie des
/// nœuds de type instance (<c>IsAnyInstance</c> zone @0x0812b430 —
  /// la liste est dans <see cref="InstanceNodeTypes"/>). La clé
/// <see cref="EntryState.InstanceGroup"/> vaut
/// <c>team.Id</c> en équipe, <c>-charId</c> en solo, 0 hors nœud
/// instancié (scène partagée).
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Clé de groupe pour un perso sur un nœud donné —
    /// <c>ILifeEntity::GetTeamNodeGroup</c> natif : le groupe de
    /// l'équipe si membre, sinon le groupe personnel.</summary>
    private int ResolveInstanceGroup(EntryState st, int nodeId)
    {
        var type = _services.GameData.Nodes
            .FirstOrDefault(n => n.Id == nodeId)?.Type ?? 0;
        if (!InstanceNodeTypes.Contains(type)) return 0;
        // Type 4 = hall de famille : l'instance est par FAMILLE
        // visitée (char+0xaac natif), pas par équipe — deux membres de
        // la même guilde sans équipe partagent le même hall, et deux
        // visiteurs de familles différentes sur le même node 501/502
        // ne se voient pas. Repli -charId si le perso est là sans
        // famille visitée (cas anormal).
        var g = type == 4
            ? (st.VisitedFamilyInstanceId != 0
                ? st.VisitedFamilyInstanceId : -(int)st.CharId)
            : _services.Teams.GetTeamOf((int)st.CharId)?.Id
                ?? -(int)st.CharId;
        EdenLog.Write($"[Instance] resolve char={st.CharId} " +
            $"node={nodeId} type={type} -> groupe {g}");
        return g;
    }

    /// <summary>Recalcule l'instance du perso après un changement
    /// d'appartenance (join/leave/disband d'équipe) — à appeler
    /// depuis les chemins qui mutent la composition. La vue se
    /// répare toute seule : la purge d'orphelins d'UpdateSightAsync
    /// éradique les handles de l'ancienne instance et ré-annonce
    /// celles de la nouvelle au prochain tick. L'ancien groupe est
    /// détruit s'il ne reste plus aucun occupant dessus (ex. perso
    /// solo qui rejoint une équipe : son instance personnelle
    /// devient orpheline).</summary>
    private void RefreshInstanceMembership(EntryState st)
    {
        var want = ResolveInstanceGroup(st, st.NodeId);
        if (want == st.InstanceGroup) return;
        var old = st.InstanceGroup;
        EdenLog.Write($"[Instance] char={st.CharId} " +
            $"groupe {old} → {want} " +
            $"(node={st.NodeId})");
        st.InstanceGroup = want;
        if (old != 0 &&
            !_states.Values.Any(o => o.InstanceGroup == old))
            DestroyGroupInstances(old);
    }

    /// <summary>Appelé par <see cref="TeamService.MembershipChanged"/>
    /// à chaque changement d'appartenance (join/leave/kick/disband —
    /// le <c>char+0x328</c> natif suit le <c>CNodeGroup</c> de
    /// l'équipe). Recalcule la clé d'instance du perso puis détruit
    /// les instances de l'équipe quittée si plus aucun membre ne
    /// s'y trouve. <b>Le hook part du thread lecteur</b> (handlers du
    /// dispatcher) — tout le travail est reposté sur la boucle.</summary>
    private void OnTeamMembershipChanged(int charId, int leftTeamId)
    {
        // Un perso qui quitte/rejoint une équipe ne peut plus piloter un
        // partage de quête (CForceTeam natif détruit avec l'équipe).
        ScheduleOnLoop(TimeSpan.Zero, _ =>
        {
            _shares.ClearChar(charId);
            return Task.CompletedTask;
        });
        ScheduleOnLoop(TimeSpan.Zero, async ct =>
        {
            if (_services.Sessions.FindByCharacter(charId) is { } s &&
                _states.TryGetValue(s.SessionId, out var st))
            {
                var before = st.InstanceGroup;
                RefreshInstanceMembership(st);
                if (st.InstanceGroup != before)
                {
                    // Le perso bascule dans la copie de sa nouvelle
                    // équipe : on la peuple (mobs/npcs propres au
                    // groupe, comme l'ouverture du CNodeGroup natif)
                    // puis on reconstruit sa vue — sinon un perso
                    // immobile gardait indéfiniment la vue de
                    // l'ancienne instance (265/269 jamais rejoués).
                    var scene = _services.Scenes.GetScene(
                        $"S{st.NodeId:D3}");
                    _services.Live.EnsureNodePopulated(st.NodeId,
                        st.InstanceGroup, scene);
                    await UpdateSightAsync(s, st, ct, force: true);
                }
            }
            if (leftTeamId != 0 &&
                !_states.Values.Any(o => o.InstanceGroup == leftTeamId))
                DestroyGroupInstances(leftTeamId);
        });
    }

    /// <summary>Recalcule l'instance de tous les membres d'une
    /// équipe (création par matchmaking, disband) — no-op pour ceux
    /// hors ligne ou hors nœud instancié.</summary>
    private void RefreshTeamInstances(
        IEnumerable<int> memberCharIds)
    {
        foreach (var cid in memberCharIds)
            if (_services.Sessions.FindByCharacter(cid) is { } s &&
                _states.TryGetValue(s.SessionId, out var mst))
                RefreshInstanceMembership(mst);
    }

    /// <summary>Redirection de spawn quand le <c>node_id</c> persisté
    /// est un type instance — <c>CNE_DBA_ClientPlayerData::Execute</c>
    /// zone @0x082f4ea0 : un nœud instancié n'est pas une position de
    /// login stable (son <c>CNodeGroup</c> est mort avec la session).
    /// Ordre natif :
    /// <list type="number">
    ///   <item>types à groupe partagé (5/7 donjons, 18 trial) :
    ///   réintégrer l'instance de l'équipe si elle est encore vivante
    ///   (<c>ILifeEntity::GetTeamNodeGroup</c> + IsOfflineReset) ;</item>
    ///   <item><c>cross_zone_node/x/y</c> (exec+0xf0/+0xf4/+0xf8 —
    ///   point de retour posé à l'entrée de l'instance) ;</item>
    ///   <item><c>last_normal_area_id</c> (exec+0x34, node&lt;&lt;16|clé
    ///   d'aire normale → event-area de la scène) ;</item>
    ///   <item><c>revive_area_id</c> (même encodage, clé de revive).</item>
    /// </list>
    /// Sans ça le client charge la scène d'instance sans contexte de
    /// groupe → barre de chargement infinie puis déconnexion
    /// (observé live : perso persisté en node 309 type 15).</summary>
    private void ApplyInstanceLoginRedirect(EntryState st,
        Eden.Core.Database.Entities.PlayerCharacterEntity chr,
        int nodeType)
    {
        // Réintégration d'instance d'équipe vivante (le natif rejoint
        // le CNodeGroup de l'équipe si le leader est en ligne, le
        // nœud pas plein et la team pas en reset-offline).
        if (nodeType is 5 or 7 or 18)
        {
            var team = _services.Teams.GetTeamOf((int)st.CharId);
            if (team != null &&
                _services.Live.InstanceNodes(team.Id)
                    .Contains(st.NodeId))
            {
                st.InstanceGroup = team.Id;
                return;
            }
        }
        // Point de retour posé à l'entrée de l'instance (exec+0xf0).
        if (chr.CrossZoneNode > 0)
        {
            EdenLog.Write($"[Zone-Entry] login redirect " +
                $"char=#{st.CharId} node {st.NodeId}(type {nodeType}) " +
                $"→ cross_zone {chr.CrossZoneNode}");
            st.NodeId = (short)chr.CrossZoneNode;
            st.PosX = (float)chr.CrossZoneX;
            st.PosZ = (float)chr.CrossZoneY;
            return;
        }
        // Dernière aire normale traversée, sinon aire de revive.
        if (TryAreaLoginRedirect(st, chr.LastNormalAreaId,
                sc => sc.NormalLinks, "last_normal") ||
            TryAreaLoginRedirect(st, chr.ReviveAreaId,
                sc => sc.ReviveLinks, "revive"))
            return;
        // Aucun point de retour exploitable : born du node par défaut
        // (le natif refuse le login — « Character not create » — mais
        // un perso bloqué hors du monde est pire qu'un respawn).
        var home = _services.GameData.Nodes
            .FirstOrDefault(n => n.Type == 1)?.Id ?? 2;
        EdenLog.Write($"[Zone-Entry] login redirect char=#{st.CharId} " +
            $"node {st.NodeId}(type {nodeType}) → node {home} " +
            $"(aucun point de retour)");
        st.NodeId = (short)home;
        var scene = _services.Scenes.GetScene($"S{home:D3}");
        var born = scene?.EventAreas.FirstOrDefault(
            a => a.Type == "BORN");
        if (born != null)
        {
            st.PosX = born.X; st.PosZ = born.Y; st.PosY = born.Z;
            st.Facing = born.Facing;
        }
        else if (scene?.Positions.Count > 0)
        {
            st.PosX = scene.Positions[0].X;
            st.PosZ = scene.Positions[0].Y;
            st.PosY = scene.Positions[0].Z;
        }
    }

    /// <summary>Résout un id d'aire packé <c>node&lt;&lt;16|clé</c> en
    /// position de spawn : la clé pointe vers l'event-area via les
    /// liens de la scène (<see cref="SceneParser.Scene.NormalLinks"/>
    /// ou <see cref="SceneParser.Scene.ReviveLinks"/>), puis point
    /// aléatoire dans l'aire (<c>CEventArea::GetRandomPoint</c>
    /// natif). Null si la clé/l'aire n'existe pas.</summary>
    private (short Node, float X, float Z, float H, float Facing)?
        ResolvePackedAreaPoint(int packedId,
            Func<SceneParser.Scene, Dictionary<int, int>> links)
    {
        if (packedId == 0) return null;
        var node = packedId >> 16;
        var scene = _services.Scenes.GetScene($"S{node:D3}");
        if (scene == null ||
            !links(scene).TryGetValue(packedId & 0xFFFF,
                out var areaId))
            return null;
        var area = scene.EventAreas.FirstOrDefault(a => a.Id == areaId);
        if (area == null) return null;
        // GetRandomPoint natif : point uniforme dans le disque de
        // l'aire (shape 1 = cercle ; carré pour les autres).
        var r = area.Radius;
        float dx, dy;
        if (area.Shape == 1)
        {
            var ang = Random.Shared.NextDouble() * Math.PI * 2;
            var rad = r * Math.Sqrt(Random.Shared.NextDouble());
            dx = (float)(Math.Cos(ang) * rad);
            dy = (float)(Math.Sin(ang) * rad);
        }
        else
        {
            dx = (float)((Random.Shared.NextDouble() * 2 - 1) * r);
            dy = (float)((Random.Shared.NextDouble() * 2 - 1) * r);
        }
        return ((short)node, area.X + dx, area.Y + dy, area.Z,
            area.Facing);
    }

    /// <summary>Applique la redirection d'aire résolue à l'état —
    /// wrapper loggé de <see cref="ResolvePackedAreaPoint"/> pour le
    /// chemin de login.</summary>
    private bool TryAreaLoginRedirect(EntryState st, int packedId,
        Func<SceneParser.Scene, Dictionary<int, int>> links,
        string tag)
    {
        if (ResolvePackedAreaPoint(packedId, links) is not { } p)
            return false;
        EdenLog.Write($"[Zone-Entry] login redirect char=#{st.CharId} " +
            $"node {st.NodeId} → {tag} node {p.Node}");
        st.NodeId = p.Node;
        st.PosX = p.X;
        st.PosZ = p.Z;
        st.PosY = p.H;
        st.Facing = p.Facing;
        return true;
    }

    /// <summary>Ménage de fin de groupe — <c>CNodeGroup</c> détruit
    /// avec l'équipe / à la déconnexion du perso solo : les instances
    /// du groupe sont vidées (mobs, npcs, entités). Les occupants
    /// éventuels rebasculent d'abord sur leur nouvelle clé.</summary>
    private void DestroyGroupInstances(int inst)
    {
        if (inst == 0) return;
        var nodes = _services.Live.InstanceNodes(inst);
        if (nodes.Count > 0)
            EdenLog.Write($"[Instance] destruction groupe {inst} " +
                $"({nodes.Count} node(s) : " +
                $"{string.Join(',', nodes)})");
        _services.Live.RemoveInstances(inst);
        _services.GroundTreasure.ClearInstance(inst);
    }
}
