using Eden.Zone;

namespace Eden.Tests;

/// <summary>Classification ami/ennemi joueur↔joueur — port du noyau
/// char→char de <c>CCharacter::IsMyEnemy</c> @0x0840e490 : hors
/// contexte PvP partagé, tout joueur est « ami ».</summary>
public sealed class EntityTypingTests
{
    private static ZoneEntryFlow.EntryState P(uint id) => new()
    { CharId = id, Migrated = true, NodeId = 5 };

    /// <summary>Deux joueurs lambda sur la même carte ne sont PAS
    /// ennemis — la mêlée PvP ne doit pas partir.</summary>
    [Fact]
    public void IsPlayerEnemy_NoSharedContext_False()
    {
        var a = P(200000001);
        var b = P(200000002);
        Assert.False(ZoneEntryFlow.IsPlayerEnemy(a, b));
        Assert.False(ZoneEntryFlow.IsPlayerEnemy(b, a));
    }

    /// <summary>Duel accepté des deux côtés → ennemis ; duel unilatéral
    /// (l'autre n'a pas confirmé) → pas ennemi.</summary>
    [Fact]
    public void IsPlayerEnemy_Duel()
    {
        var a = P(200000001);
        var b = P(200000002);
        a.DuelActive = true; a.DuelOpponentId = b.CharId;
        Assert.False(ZoneEntryFlow.IsPlayerEnemy(a, b));
        b.DuelActive = true; b.DuelOpponentId = a.CharId;
        Assert.True(ZoneEntryFlow.IsPlayerEnemy(a, b));
    }

    /// <summary>Versus : même instance + camps différents → ennemis ;
    /// même camp → alliés.</summary>
    [Fact]
    public void IsPlayerEnemy_VersusCamps()
    {
        var a = P(200000001);
        var b = P(200000002);
        var c = P(200000003);
        a.VersusInstanceId = b.VersusInstanceId =
            c.VersusInstanceId = 7;
        a.VersusCamp = c.VersusCamp = 0;
        b.VersusCamp = 1;
        Assert.True(ZoneEntryFlow.IsPlayerEnemy(a, b));
        Assert.False(ZoneEntryFlow.IsPlayerEnemy(a, c));
    }

    /// <summary>Territoire : même bataille + camps différents →
    /// ennemis ; même camp → alliés (la mêlée ne part pas).</summary>
    [Fact]
    public void IsPlayerEnemy_TerritoryCamps()
    {
        var a = P(200000001);
        var b = P(200000002);
        var c = P(200000003);
        a.TerritoryId = b.TerritoryId = c.TerritoryId = 3;
        a.TerritoryCamp = 1; b.TerritoryCamp = 2; c.TerritoryCamp = 1;
        Assert.True(ZoneEntryFlow.IsPlayerEnemy(a, b));
        Assert.False(ZoneEntryFlow.IsPlayerEnemy(a, c));
    }

    /// <summary>Battlefield / croisade / BR : même match → ennemis
    /// (arène = hostilité globale, cf. le lethal check existant).</summary>
    [Fact]
    public void IsPlayerEnemy_Arenas()
    {
        var a = P(200000001);
        var b = P(200000002);
        a.BattlefieldId = b.BattlefieldId = 42;
        Assert.True(ZoneEntryFlow.IsPlayerEnemy(a, b));
        a.BattlefieldId = b.BattlefieldId = 0;
        a.CrusadeId = b.CrusadeId = 9;
        Assert.True(ZoneEntryFlow.IsPlayerEnemy(a, b));
        a.CrusadeId = b.CrusadeId = 0;
        a.BattleRoyaleId = b.BattleRoyaleId = 1;
        Assert.True(ZoneEntryFlow.IsPlayerEnemy(a, b));
        // Matchs différents / hors match → pas ennemis.
        a.BattleRoyaleId = 2;
        Assert.False(ZoneEntryFlow.IsPlayerEnemy(a, b));
    }
}
