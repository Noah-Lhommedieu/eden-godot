using Xunit;
using Eden.Core.Game.Services;
using Eden.Core.Game.Party;
using Eden.Zone.Combat;

namespace Eden.Tests;

public class TeamServiceTests
{
    [Fact]
    public void CreateTeam_LeaderIsMember()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "Leader", 50, 1);
        Assert.Single(team.Members);
        Assert.True(team.Members[0].IsLeader);
        Assert.False(team.IsRaid);
    }

    [Fact]
    public void AddMember_UpToPartyLimit()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        for (var i = 2; i <= 5; i++)
            Assert.True(svc.AddMember(team.Id, i, $"M{i}", 40, 2));
        Assert.True(team.IsFull);
        Assert.False(svc.AddMember(team.Id, 99, "X", 40, 2)); // plein
    }

    [Fact]
    public void Raid_Allows25Members()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 80, 7, isRaid: true);
        for (var i = 2; i <= 25; i++)
            Assert.True(svc.AddMember(team.Id, i, $"M{i}", 70, 2));
        Assert.True(team.IsFull);
        Assert.False(svc.AddMember(team.Id, 99, "X", 40, 2));
    }

    [Fact]
    public void LeaderQuit_PromotesNextMember()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);
        svc.LeaveTeam(1);
        Assert.Equal(2, team.LeaderCharacterId);
        Assert.True(team.Members[0].IsLeader);
    }

    [Fact]
    public void Kick_RequiresLeader()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);
        svc.AddMember(team.Id, 3, "M3", 40, 2);
        Assert.False(svc.KickMember(2, 3)); // pas leader
        Assert.True(svc.KickMember(1, 3));
        Assert.False(svc.IsInTeam(3));
    }

    /// <summary>Partage d'exp natif `CCharFormula::ExpForTeam`
    /// @0x083f4470 : `ROUND(base×0.01×lv×(n×para330+100)/Σlv)` capé à
    /// base — les paramètres viennent de S_Parameter (330 = coef
    /// partagé, 329 = déclin par écart de niveau).</summary>
    [Fact]
    public void ExpForTeam_NativeFormula()
    {
        var p = new NativeCombatFormulas.Params(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0,
            ExpLevelDiff: 9f, ExpLevelDec: 9f,
            TeamExpShareCoef: 25f, TeamExpNoShareCoef: 5f,
            EquipExpRate: 0.2f,
            DesDeclineUp: 17f, DesDeclineDown: 20f,
            DpPerKill: 25f, DpRollCost: 1250f,
            DpVitCapMul: 10f, DpPoolCapMul: 10f,
            SummonMobLifespan: 30f,
            LuckyBagFailStep: 30f, LuckyBagFailCap: 90f);
        // Duo à même niveau : base×0.01×lv×(2×25+100)/(2lv)
        // = base×0.75 → chacun 75 % (bonus de groupe +50 % cumulés).
        var duo = NativeCombatFormulas.ExpForTeam(1000, 2, 40, 80, p);
        Assert.Equal(750, duo);
        // Cinq membres : chacun touche moins qu'en duo mais plus
        // que base/5 (la bonus team compense).
        var full = NativeCombatFormulas.ExpForTeam(1000, 5, 40, 200, p);
        Assert.Equal(450, full);
        // La part est toujours bornée par la base.
        var capped = NativeCombatFormulas.ExpForTeam(1000, 5, 100, 200, p);
        Assert.Equal(1000, capped);
        // Déclin natif : identique niveau → 1.0 ; écart > para328
        // applique le malus par palier de 9 niveaux.
        Assert.Equal(1f,
            NativeCombatFormulas.ExpDeclineRate(40, 40, p));
        Assert.Equal(0.91f,
            NativeCombatFormulas.ExpDeclineRate(50, 40, p), 2);
        Assert.Equal(0f,
            NativeCombatFormulas.ExpDeclineRate(70, 40, p));
    }

    /// <summary>`GetTreasureLooter` @0x0808c1f0 : règle 3
    /// (Taunt-Exclusive) ou TeamType 3 (raid 25) → pas de
    /// redistribution, le tueur garde le drop.</summary>
    [Fact]
    public void PickLootReceiver_Rule3AndRaid25_KillerKeeps()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);
        team.LootRuleValue = 3;
        for (var i = 0; i < 20; i++)
            Assert.Equal(1, svc.PickLootReceiver(team.Id, 1));
        // Raid 25 → solo quel que soit la règle de loot.
        team.LootRuleValue = 2;
        team.IsRaid = true; team.RaidCap = 25;
        Assert.Equal(3, team.TeamType);
        Assert.Equal(1, svc.PickLootReceiver(team.Id, 1));
    }

    /// <summary>`GetLooter` @0x0846dfe0 : règles 1-2 → tirage
    /// UNIFORME `random() % n` parmi les éligibles locaux — ratio
    /// exact 1/n (2 membres → 50/50, 3 → 33/33/33…). Pas de paliers
    /// pondérés en natif. Le curseur +0x52 suit le dernier servi.</summary>
    [Fact]
    public void PickLootReceiver_Random_UniformAmongEligible()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);
        svc.AddMember(team.Id, 3, "M3", 40, 2);
        team.LootRuleValue = 2;

        var tally = new Dictionary<int, int>();
        for (var i = 0; i < 300; i++)
        {
            var cid = svc.PickLootReceiver(team.Id, 1);
            Assert.NotNull(cid);
            tally[cid!.Value] = tally.GetValueOrDefault(cid.Value) + 1;
            // Le curseur natif pointe sur le membre servi.
            Assert.Equal(
                team.Members.FindIndex(m => m.CharacterId == cid),
                team.LootCursor);
        }
        // Les trois sont servis (~100 chacun, uniforme).
        Assert.Equal(3, tally.Count);
        Assert.All(tally.Values, c => Assert.InRange(c, 50, 150));
    }

    /// <summary>Règles 1-2 : un inéligible (hors node) est exclu du
    /// tirage ; un seul éligible → le tueur garde (natif renvoie le
    /// cueilleur) mais le curseur se cale sur l'éligible.</summary>
    [Fact]
    public void PickLootReceiver_Random_SkipsIneligible()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);
        svc.AddMember(team.Id, 3, "M3", 40, 2);
        team.LootRuleValue = 1;

        // Membre 2 inéligible → tirage uniforme entre 1 et 3.
        for (var i = 0; i < 50; i++)
            Assert.Contains(svc.PickLootReceiver(team.Id, 1,
                cid => cid != 2), new int?[] { 1, 3 });
        // Un seul éligible → le tueur garde (natif : count<2 →
        // retour cueilleur) ; le curseur se cale sur lui.
        team.LootCursor = -1;
        Assert.Equal(1, svc.PickLootReceiver(team.Id, 1,
            cid => cid == 2));
        Assert.Equal(1, team.LootCursor); // index du membre 2
    }

    /// <summary>Valeur de règle hors {1,2,3} (chemin `else` natif) →
    /// premier éligible après le curseur : distribution séquentielle.</summary>
    [Fact]
    public void PickLootReceiver_OtherRule_SequentialAfterCursor()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);
        svc.AddMember(team.Id, 3, "M3", 40, 2);
        team.LootRuleValue = 4;

        Assert.Equal(1, svc.PickLootReceiver(team.Id, 1));
        Assert.Equal(2, svc.PickLootReceiver(team.Id, 1));
        Assert.Equal(3, svc.PickLootReceiver(team.Id, 1));
        Assert.Equal(1, svc.PickLootReceiver(team.Id, 1));
        // Membre 1 inéligible → la rotation reprend à 2.
        Assert.Equal(2, svc.PickLootReceiver(team.Id, 1,
            cid => cid != 1));
    }

    /// <summary>`CForceTeam::SetExpRule` @0x846cdb0 : 1-2 → partage
    /// (+0x56=0) ; >2 → no-share ; les autres valeurs ne touchent
    /// pas le flag. Leader uniquement.</summary>
    [Fact]
    public void SetExpRule_NativeSemantics()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);

        Assert.False(team.ExpNoShare);
        Assert.Same(team, svc.SetExpRule(1, 3));
        Assert.True(team.ExpNoShare);
        Assert.Same(team, svc.SetExpRule(1, 1));
        Assert.False(team.ExpNoShare);
        // rule ≤ 0 : le natif retourne sans écrire — flag conservé.
        svc.SetExpRule(1, 3);
        Assert.Same(team, svc.SetExpRule(1, 0));
        Assert.True(team.ExpNoShare);
        // Pas leader → null, flag inchangé.
        Assert.Null(svc.SetExpRule(2, 1));
        Assert.True(team.ExpNoShare);
    }

    /// <summary>Annonces de recrutement — `ChangeRecruitDataV2` :
    /// solo → post perso ; en équipe → leader uniquement ; le mot-clé
    /// filtre sur le message ; enabled=false retire l'annonce.</summary>
    [Fact]
    public void Recruit_RegisterSearchClear()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);

        // Non-leader en équipe → refus natif.
        Assert.Null(svc.RegisterRecruit(2, true, 1, 0, 40, 0, "x"));
        // Leader → post d'équipe.
        var post = svc.RegisterRecruit(1, true, 10, 0, 40, 0,
            "farm donjon");
        Assert.NotNull(post);
        Assert.Equal(team.Id, post!.TeamId);
        // Solo → post personnel (TeamId 0).
        Assert.NotNull(svc.RegisterRecruit(9, true, 5, 0, 20, 0,
            "quetes"));
        var all = svc.SearchRecruits(0, 0, "");
        Assert.Equal(2, all.Count);
        // Filtre mot-clé (insensible casse).
        Assert.Single(svc.SearchRecruits(0, 0, "DONJON"));
        // Retrait.
        svc.RegisterRecruit(1, false, 0, 0, 0, 0, "");
        Assert.Single(svc.SearchRecruits(0, 0, ""));
        svc.ClearRecruit(9);
        Assert.Empty(svc.SearchRecruits(0, 0, ""));
    }

    /// <summary>TeamType dérivé de la capacité comme
    /// `SetMaxTeamSize` natif : party→1, raid10→2, raid25→3.</summary>
    [Fact]
    public void TeamType_DerivedFromCapacity()
    {
        var svc = new TeamService();
        Assert.Equal(1, svc.CreateTeam(1, "L", 50, 1).TeamType);
        var raid = svc.CreateTeam(2, "R", 50, 1);
        raid.IsRaid = true; raid.RaidCap = 10;
        Assert.Equal(2, raid.TeamType);
        raid.RaidCap = 25;
        Assert.Equal(3, raid.TeamType);
    }

    /// <summary>Règle 3 : le tueur reçoit ; un tueur non éligible
    /// rebascule sur un membre joignable ; aucun éligible → null.</summary>
    [Fact]
    public void PickLootReceiver_TauntExclusive_FallsBackToEligible()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);
        team.LootRuleValue = 3;

        Assert.Equal(2, svc.PickLootReceiver(team.Id, 2));
        Assert.Equal(1, svc.PickLootReceiver(team.Id, 2,
            cid => cid == 1));
        Assert.Null(svc.PickLootReceiver(team.Id, 2, _ => false));
    }

    /// <summary>ChageLootRule natif (op 0) : leader uniquement,
    /// valeurs 1-3 (hors plage = ignoré). Couplage +0x56 : règle 3
    /// coupe le partage d'exp, 1-2 le rétablissent.</summary>
    [Fact]
    public void ChangeLootRule_LeaderOnly_NativeRange()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);

        Assert.Null(svc.ChangeLootRule(2, 2)); // pas leader
        Assert.Null(svc.ChangeLootRule(1, 0)); // hors plage native
        Assert.Null(svc.ChangeLootRule(1, 4));
        Assert.Equal(1, team.LootRuleValue);   // défaut natif
        Assert.False(team.ExpNoShare);

        Assert.NotNull(svc.ChangeLootRule(1, 2));
        Assert.Equal(2, team.LootRuleValue);
        Assert.False(team.ExpNoShare);

        // Règle 3 (Taunt) → no-share exp, comme +0x56 natif.
        Assert.NotNull(svc.ChangeLootRule(1, 3));
        Assert.Equal(3, team.LootRuleValue);
        Assert.True(team.ExpNoShare);

        Assert.NotNull(svc.ChangeLootRule(1, 1));
        Assert.Equal(1, team.LootRuleValue);
        Assert.False(team.ExpNoShare);
    }

    /// <summary>ChageRollRule natif (op 3) : seuil 0-2, leader
    /// uniquement, stocké tel quel (pas de borne côté natif).</summary>
    [Fact]
    public void ChangeRollRule_LeaderOnly()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);

        Assert.Null(svc.ChangeRollRule(2, 1));
        Assert.NotNull(svc.ChangeRollRule(1, 2));
        Assert.Equal(2, team.RollRule);
    }

    /// <summary>ChangeToRaid (op 1, cap=10) / CreateRaid25Team (op 6,
    /// cap=25) — leader uniquement, refuse si l'équipe déborde.</summary>
    [Fact]
    public void ConvertToRaid_SetsRaidCap()
    {
        var svc = new TeamService();
        var team = svc.CreateTeam(1, "L", 50, 1);
        svc.AddMember(team.Id, 2, "M2", 40, 2);

        Assert.Null(svc.ConvertToRaid(2, 10)); // pas leader
        Assert.NotNull(svc.ConvertToRaid(1, 10));
        Assert.True(team.IsRaid);
        Assert.Equal(10, team.MaxSize);

        Assert.NotNull(svc.ConvertToRaid(1, 25));
        Assert.Equal(25, team.MaxSize);

        // Raid trop petit pour les membres actuels → refus.
        Assert.Null(svc.ConvertToRaid(1, 1));
    }
}
