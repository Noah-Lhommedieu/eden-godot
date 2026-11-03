using Eden.Core.Game.Services;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>
/// Isolation des instances de donjon (portage <c>CNodeGroup</c>
/// natif — <c>CNode::IsAnyInstance</c> zone @0x0812b430) : chaque
/// équipe possède sa copie du nœud ; les entités, trésors et
/// énumérations sont keyés (nodeId, inst) et ne fuient jamais d'une
/// copie à l'autre. Couvre aussi le hook
/// <see cref="TeamService.MembershipChanged"/> (join/leave/disband
/// → recalcul de la clé d'instance côté zone).
/// </summary>
public class InstancingTests
{
    // ---------------- ZoneLiveWorld (node,inst) ----------------

    /// <summary>Deux groupes sur le même nœud voient chacun leurs
    /// mobs — MobsOf/GroupMobs ne mélangent jamais les copies.</summary>
    [Fact]
    public void LiveWorld_SameNode_DifferentInstances_Isolated()
    {
        var live = new ZoneLiveWorld();
        var a = live.SpawnHostileMob(35, 7, 1001, 0, 10, 0, 10, 0);
        var b = live.SpawnHostileMob(35, 9, 1001, 0, 20, 0, 20, 0);
        live.SpawnHostileMob(35, 0, 1002, 0, 30, 0, 30, 0); // scène partagée

        Assert.Single(live.MobsOf(35, 7));
        Assert.Single(live.MobsOf(35, 9));
        Assert.Single(live.MobsOf(35, 0));
        Assert.Equal(7, a.InstanceKey);
        Assert.Equal(9, b.InstanceKey);
        // L'accès par handle retrouve bien la bonne copie.
        Assert.Equal(20, live.GetMob(b.Handle)!.X);
    }

    /// <summary>RemoveInstances(inst) ne détruit que la copie du
    /// groupe — l'autre instance du même nœud survit
    /// (CNodeGroup::RemoveAllNode natif).</summary>
    [Fact]
    public void LiveWorld_RemoveInstances_OnlyThatGroup()
    {
        var live = new ZoneLiveWorld();
        var a = live.SpawnHostileMob(35, 7, 1001, 0, 10, 0, 10, 0);
        live.SpawnNpc(35, 7, 501, 0, 0, 0, 0);
        var b = live.SpawnHostileMob(35, 9, 1001, 0, 20, 0, 20, 0);

        Assert.Contains(35, live.InstanceNodes(7));
        live.RemoveInstances(7);

        Assert.Empty(live.MobsOf(35, 7));
        Assert.Empty(live.InstanceNodes(7));
        Assert.Null(live.GetMob(a.Handle));
        // La copie du groupe 9 est intacte.
        Assert.Single(live.MobsOf(35, 9));
        Assert.NotNull(live.GetMob(b.Handle));
    }

    /// <summary>SpawnPet/SpawnNpc keyés par instance : deux équipes
    /// dans la même scène ont chacune leurs pets et NPCs.</summary>
    [Fact]
    public void LiveWorld_PetsAndNpcs_PerInstance()
    {
        var live = new ZoneLiveWorld();
        live.SpawnPet(35, 7, 2001, 111, 0, 0, 0, 0, false, 0);
        live.SpawnPet(35, 9, 2001, 222, 0, 0, 0, 0, false, 0);
        live.SpawnNpc(35, 7, 600, 0, 0, 0, 0);

        var despawned7 = live.DespawnNpcs(35, 7, 600);
        Assert.Single(despawned7);
        Assert.Empty(live.DespawnNpcs(35, 9, 600));
    }

    // ---------------- GroundTreasureService ----------------

    /// <summary>Trésors au sol : listing et ramassage filtrés par
    /// instance — un drop d'une copie est invisible dans l'autre.</summary>
    [Fact]
    public void Treasure_ListingFilteredByInstance()
    {
        var svc = new GroundTreasureService();
        var now = DateTime.UtcNow;
        var t7 = svc.Spawn(35, 7, 111, 0, 0, 5001, 1, now);
        svc.Spawn(35, 9, 222, 0, 0, 5002, 1, now);
        svc.Spawn(35, 0, 333, 0, 0, 5003, 1, now); // scène partagée

        var list7 = svc.ListForNode(35, 7);
        Assert.Single(list7);
        Assert.Equal(t7.Id, list7[0].Id);
        Assert.Single(svc.ListForNode(35, 9));
        Assert.Single(svc.ListForNode(35, 0));
    }

    /// <summary>ClearInstance ne retire que les trésors du groupe
    /// détruit (NodeGroupReset / disband / déco solo).</summary>
    [Fact]
    public void Treasure_ClearInstance_OnlyThatGroup()
    {
        var svc = new GroundTreasureService();
        var now = DateTime.UtcNow;
        svc.Spawn(35, 7, 111, 0, 0, 5001, 1, now);
        var keep = svc.Spawn(35, 9, 222, 0, 0, 5002, 1, now);

        svc.ClearInstance(7);
        Assert.Empty(svc.ListForNode(35, 7));
        Assert.Single(svc.ListForNode(35, 9));
        Assert.True(svc.TryGet(keep.Id, out _));
    }

    // ---------------- TeamService.MembershipChanged ----------------

    /// <summary>Le hook MembershipChanged trace chaque transition —
    /// la zone s'en sert pour recalculer la clé d'instance et GC le
    /// groupe orphelin (CNodeGroup natif).</summary>
    [Fact]
    public void TeamService_MembershipChanged_Fires()
    {
        var svc = new TeamService();
        var events = new List<(int CharId, int LeftTeam)>();
        svc.MembershipChanged = (c, t) => events.Add((c, t));

        var team = svc.CreateTeam(100, "leader", 50, 1);
        Assert.Equal((100, 0), events[^1]);

        Assert.True(svc.AddMember(team.Id, 200, "mate", 50, 2));
        Assert.Equal((200, 0), events[^1]);

        Assert.True(svc.LeaveTeam(200));
        Assert.Equal((200, team.Id), events[^1]);

        // Le dernier membre dissout l'équipe — leftTeamId transmis
        // pour la destruction du groupe d'instances.
        Assert.True(svc.LeaveTeam(100));
        Assert.Equal((100, team.Id), events[^1]);
        Assert.Null(svc.GetTeam(team.Id));
    }

    /// <summary>AnswerInvite crée l'équipe puis ajoute la cible :
    /// les deux transitions sont notifiées (join → clé = team.Id).</summary>
    [Fact]
    public void TeamService_InviteFlow_NotifiesBoth()
    {
        var svc = new TeamService();
        var events = new List<(int CharId, int LeftTeam)>();
        svc.MembershipChanged = (c, t) => events.Add((c, t));

        var inv = svc.Invite(100, 200);
        Assert.NotNull(inv);
        var team = svc.AnswerInvite(inv!.Value.InviteId, 200, true,
            "mate", 50, 2);

        Assert.NotNull(team);
        Assert.Contains((100, 0), events);  // leader → nouvelle équipe
        Assert.Contains((200, 0), events);  // cible → join
    }
}
