using EdenClient.Data.Tables;
using EdenClient.Game;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Règles montures : clic droit retrait, paliers de vitesse
/// affichés, routage natif des actions de combat, vitesse observée.</summary>
public class MountRulesTests
{
    private static string? CorpusFile(string name)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, "corpus", "fr-full", "data", "db", name);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    private static Dictionary<int, EnchantTable.EnchantDef> Defs()
    {
        var p = CorpusFile("c_enchant.ini");
        Assert.NotNull(p);
        return EnchantTable.Load(p!);
    }

    // ---- clic droit (retrait de monture) --------------------------------

    [Fact]
    public void RightClick_RideAlwaysCancellable_EvenNoAbandon()
    {
        var ride = new EnchantTable.EnchantDef
        {
            IsBuff = false, NoAbandon = true,
            Raw = new[] { "1", "x", "2134", "253020", "", "1", "2" },
        };
        Assert.True(EnchantTable.CanCancelByRightClick(ride));
    }

    [Theory]
    [InlineData(true, false, true)]    // buff normal
    [InlineData(false, false, false)]  // débuff
    [InlineData(true, true, false)]    // can't abandon
    public void RightClick_NonRide(bool isBuff, bool noAbandon, bool expected)
    {
        var d = new EnchantTable.EnchantDef
        {
            IsBuff = isBuff, NoAbandon = noAbandon,
            Raw = new[] { "1", "x", "2051", "10" },
        };
        Assert.Equal(expected, EnchantTable.CanCancelByRightClick(d));
    }

    [Fact]
    public void RightClick_UnknownDef_IsCancellable() =>
        Assert.True(EnchantTable.CanCancelByRightClick(null));

    /// <summary>Données réelles (corpus FR) : TOUS les enchants de
    /// monture sont retirables au clic droit, et au moins une monture
    /// porte un drapeau qui l'aurait bloquée sans la règle.</summary>
    [Fact]
    public void RightClick_AllRealRides_Cancellable()
    {
        var defs = Defs();
        var rides = defs.Values.Where(d => EnchantTable.RideKeyOfRaw(d.Raw) > 0).ToList();
        Assert.True(rides.Count > 40);
        Assert.All(rides, d => Assert.True(EnchantTable.CanCancelByRightClick(d), $"enchant {d.Id}"));
        Assert.Contains(rides, d => !d.IsBuff || d.NoAbandon);
        // 11354 = Kitsune de combat : monture reconnue (rideKey 253020).
        Assert.Equal(253020, EnchantTable.RideKeyOfRaw(defs[11354].Raw));
    }

    /// <summary>Chaîne de donnée réelle 11354 « Kitsune de combat »
    /// (corpus FR, après NormalizeRow KB-01) : 2134 type 2 → badge
    /// épée + survie à l'engage (même règle que
    /// <see cref="MountCombatRules.IsCombatRide"/>) ; 2051|90 → palier
    /// or 90 affiché ; blocs stats que le stage « ride » du
    /// StatPipeline applique ensuite (EnchantService.GetUseCommands +
    /// LicenseService.ApplyEnchantCommands — 2002 = AttrMaxHPRate,
    /// 2083 = AttrPhysicoDamageRate/DefenceRate, cf. EnchantCommands).
    /// </summary>
    [Fact]
    public void CombatMount_11354_DataChain_RideTypeAndStats()
    {
        var raw = Defs()[11354].Raw;
        Assert.Equal(2, MountCombatRules.RideTypeOf(raw));
        var i2051 = Array.IndexOf(raw, "2051");
        Assert.True(i2051 > 0 && raw[i2051 + 1] == "90",
            "2051|90 attendu (vitesse native → palier or 90)");
        var i2002 = Array.IndexOf(raw, "2002");
        Assert.True(i2002 > 0 && raw[i2002 + 1] == "7",
            "2002|7 attendu (PV max +7 %)");
        var i2083 = Array.IndexOf(raw, "2083");
        Assert.True(i2083 > 0 && raw[i2083 + 1] == "2" &&
            raw[i2083 + 2] == "1",
            "2083|2|1 attendu (ATQ +2 %, DÉF +1 %)");
    }

    /// <summary>Montures à sorts (2134 <b>type 1</b>) : le natif ne
    /// teste que le type <c>≠ 0</c> (<c>CEC_Ride::SetState</c> zone
    /// @0x0854f330 stocke var3 en char+0x824, relu par
    /// <c>ISpell::CastTest</c> @0x084f08c3 — 0 =&gt; dismount à
    /// l'engage ; client <c>FUN_00778660</c> route sur l'octet ≠0).
    /// Le badge « épée » (<see cref="MountCombatRules.IsCombatRide"/>)
    /// doit donc couvrir les montures à sorts, pas seulement le type 2
    /// — corpus : type 1 = 10 enchants, TOUS avec un bloc 2140
    /// (RideSpell). Cas réels : 50114 « canon », 51369/58997/59033.
    /// </summary>
    [Theory]
    [InlineData(58997)] [InlineData(50114)]
    [InlineData(51369)] [InlineData(59033)]
    public void CombatBadge_Type1SpellMount_IsCombat(int eid)
    {
        var raw = Defs()[eid].Raw;
        Assert.Equal(1, MountCombatRules.RideTypeOf(raw));
        // Même prédicat que le badge inventaire : type ≠ 0 → combat.
        Assert.True(MountCombatRules.RideTypeOf(raw) != 0,
            $"enchant {eid} : type 1 = monture de combat (badge épée)");
        Assert.Contains("2140", raw);   // RideSpell — marqueur type 1
    }

    /// <summary>Invariant corpus : <b>tout</b> enchant 2134 type 1
    /// porte 2140 (RideSpell) — le type 1 n'existe que pour les
    /// montures à sorts. Et l'inverse du badge : type 0/absent, ou
    /// pas de 2134 du tout, n'est pas combat.</summary>
    [Fact]
    public void CombatBadge_Type1Invariants_AndNonCombat()
    {
        var type1 = Defs().Values
            .Where(d => MountCombatRules.RideTypeOf(d.Raw) == 1).ToList();
        Assert.True(type1.Count >= 4, $"type-1 réels : {type1.Count}");
        Assert.All(type1, d => Assert.Contains("2140", d.Raw));
        // Raws synthétiques : pas de 2134, ou 2134 sans type (var3 vide).
        Assert.Equal(0, MountCombatRules.RideTypeOf(
            new[] { "1", "x", "2051", "10" }));
        Assert.Equal(0, MountCombatRules.RideTypeOf(
            new[] { "1", "x", "2134", "999999", "", "", "", "" }));
        // Court-circuit déterministe du badge (id invalide → non combat).
        Assert.False(MountCombatRules.IsCombatRide(0));
    }

    /// <summary>Aucun enchant de monture FR ne porte de bonus PM
    /// direct (2013/1014 absents) : la montée de PM vient de 2085
    /// (toutes stats +N % → Sag). Invariant à maintenir si la donnée
    /// évolue.</summary>
    [Fact]
    public void RealRides_NoDirectMpBonus()
    {
        var rides = Defs().Values
            .Where(d => EnchantTable.RideKeyOfRaw(d.Raw) > 0);
        // Blocs de commande en cols 9+7k (schéma S_ après NormalizeRow).
        Assert.DoesNotContain(rides, d => Enumerable.Range(0, 4)
            .Select(k => 9 + k * 7).Where(i => i < d.Raw.Length)
            .Any(i => d.Raw[i] is "2013" or "1014"));
    }

    // ---- paliers de vitesse (infobulles) ---------------------------------

    [Theory]
    [InlineData(0, 0)] [InlineData(-75, -75)]
    [InlineData(15, 35)] [InlineData(25, 35)]
    [InlineData(26, 55)] [InlineData(35, 55)] [InlineData(42, 55)]
    [InlineData(50, 70)] [InlineData(65, 70)]
    [InlineData(66, 90)] [InlineData(75, 90)] [InlineData(90, 90)]
    public void Tier_ByNativeBonus(int native, int tier) =>
        Assert.Equal(tier, MountCombatRules.TierPct(native));

    [Theory]
    [InlineData("VIT DÉP +75 %.", 75, "VIT DÉP +90 %.")]
    [InlineData("Quand monté, VIT DÉP +35 %.", 35, "Quand monté, VIT DÉP +55 %.")]
    [InlineData("VIT DÉP +80 % si utilisée.", 90, "VIT DÉP +90 % si utilisée.")]
    [InlineData("VIT DÉP +20 % et PV +10 %", 20, "VIT DÉP +35 % et PV +10 %")]
    [InlineData("Augmente votre vitesse de 15 % en monture.", 15, "Augmente votre vitesse de 35 % en monture.")]
    [InlineData("Augmente la vitesse de déplacement de 50 %. ", 50, "Augmente la vitesse de déplacement de 70 %. ")]
    [InlineData("$15$VIT DÉP -75 %, PV Max +5 000 000.", -75, "$15$VIT DÉP -75 %, PV Max +5 000 000.")]
    [InlineData("Transformation en canon.", 0, "Transformation en canon.")]
    public void TierText_Rewrites(string src, int native, string expected) =>
        Assert.Equal(expected, MountCombatRules.TierText(src, native));

    /// <summary>Même table que le serveur (RideSpeedRules) : couvre
    /// chaque borne de palier du corpus réel.</summary>
    [Fact]
    public void Tier_RealRides_OnlyFourTiers()
    {
        var tiers = Defs().Values
            .Where(d => EnchantTable.RideKeyOfRaw(d.Raw) > 0)
            .Select(d => { var r = d.Raw; for (var i = 0; i + 1 < r.Length; i++)
                if (r[i] == "2051" && int.TryParse(r[i + 1], out var p)) return p; return 0; })
            .Where(p => p > 0).Select(MountCombatRules.TierPct).Distinct().OrderBy(x => x);
        Assert.Equal(new[] { 35, 55, 70, 90 }, tiers.ToArray());
    }

    // ---- combat monté (natif FUN_00778660 / FUN_007785d0) -----------------

    [Theory]
    [InlineData("magic01", true)] [InlineData("magic09", true)]
    [InlineData("magic19", true)] [InlineData("magic20", true)]
    [InlineData("magic03", false)] [InlineData("magic02", false)] [InlineData("magic05", false)]
    [InlineData("attack01", true)] [InlineData("attack04", true)] [InlineData("attack05", false)]
    [InlineData("shoot01", true)] [InlineData("shoot23", true)]
    [InlineData("hurt01", false)] [InlineData("stand01", false)]
    public void Routed_Actions(string anim, bool routed) =>
        Assert.Equal(routed, MountCombatRules.RoutedAction(anim));

    // ---- vitesse d'anim des autres joueurs --------------------------------

    [Fact]
    public void Observed_MountedGold_Gives90PctBonus()
    {
        // Même repère que le joueur local : bonus = (v/6 − 1) × 100.
        var mul = MoveAnimSpeed.MulFromObserved(6f * 1.9f);
        Assert.Equal(90f, MoveAnimSpeed.BonusPct(mul), 1);
        Assert.Equal(1f, MoveAnimSpeed.Scale(MoveAnimSpeed.BonusPct(mul), true));
    }

    [Fact]
    public void Observed_Smoothing_ConvergesAndSeeds()
    {
        Assert.Equal(7f, MoveAnimSpeed.Smooth(0f, 7f));
        var v = 6f;
        for (var i = 0; i < 12; i++) v = MoveAnimSpeed.Smooth(v, 12f);
        Assert.InRange(v, 11.9f, 12f);
    }
}
