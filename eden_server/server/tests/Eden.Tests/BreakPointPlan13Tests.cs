using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Game;
using Eden.Core.Game.Services;
using Eden.Core.Networking;
using Eden.Core.Networking.Messages.Generated;
using Eden.Zone;
using Xunit;

namespace Eden.Tests;

/// <summary>Plan futur-feature/13 — Break Point (patience S_Monster),
/// faiblesses (masque col 11), pools S_DropItem normal/break, rupture de
/// cast, flags de mort. Les données viennent des fichiers natifs
/// <c>server_us/Data/db</c> (valeurs vérifiées par reverse :
/// <c>CMonsterQuery::Deserialize</c> @0x0860d790, <c>BornMonster</c>
/// @0x084b02b0, <c>CalculatePatienceAttrDamage</c> @0x083f4260/94e0,
/// <c>GenerateDrop</c> @0x08091720).</summary>
public class BreakPointPlan13Tests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static BreakPointPlan13Tests() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    private static readonly Lazy<Dictionary<int, MonsterParser.MonsterDef>> Monsters =
        new(() => MonsterParser.Parse(DbDir + "/S_Monster.ini")
            .ToDictionary(m => m.Id));

    // ------------------------------------------------ parsing S_Monster

    [Fact]
    public void Herak_MaskIsSlashAndLightning_PatienceFromColumn()
    {
        var m = Monsters.Value[58471];       // Herak (lv 8) — wiki : Slash + Lightning
        Assert.Equal(0b100001, m.BpWeakMask); // bits 0 (Slash) et 5 (Lightning)
        Assert.Equal(821, m.Patience);
        Assert.Equal(15, m.BpGate);
        Assert.True(m.CanBreak);
        Assert.True(BreakPointRules.IsWeakness(m, 1));   // Slash
        Assert.True(BreakPointRules.IsWeakness(m, 6));   // Lightning
        Assert.False(BreakPointRules.IsWeakness(m, 4));  // Fire
        Assert.False(BreakPointRules.IsWeakness(m, 0));
        Assert.False(BreakPointRules.IsWeakness(m, 10));
    }

    [Fact]
    public void ElementOrder_MatchesWikiBreakPointsForBosses()
    {
        // Ordre prouvé : Slash, Strike, Pierce, Fire, Ice, Lightning,
        // Nature, Light, Dark (bit attr-1). Échantillon wiki-en bosses.md.
        var e = BreakPointRules.ElementNames;
        Assert.Equal(new[] { "Slash", "Strike", "Pierce", "Fire", "Ice",
            "Lightning", "Nature", "Light", "Dark" }, e);
        // Angela (lv19) = Pierce + (autre) ; Garrot = Slash Ice Nature ;
        // Bayard = Strike + Lightning ; Charles = Strike Fire Ice.
        static int Mask(params string[] names) => names.Aggregate(0,
            (a, n) => a | 1 << Array.IndexOf(BreakPointRules.ElementNames, n));
        var m = Monsters.Value;
        Assert.Equal(Mask("Strike", "Lightning"), m[58495].BpWeakMask & Mask("Strike", "Lightning"));
        Assert.Equal(Mask("Strike", "Lightning"), m[58495].BpWeakMask);   // Bayard exact
        Assert.Equal(Mask("Slash", "Ice", "Nature"), m[58543].BpWeakMask & Mask("Slash", "Ice", "Nature"));
        Assert.Equal(Mask("Strike", "Fire", "Ice"), m[58532].BpWeakMask); // Charles exact
        Assert.Equal(Mask("Pierce", "Lightning"), m[58492].BpWeakMask);   // Dana exact
    }

    [Fact]
    public void PlainMob_NoMask_DefaultPatience100_NeverBreakable()
    {
        var m = Monsters.Value[56001];        // champignon : masque 0, patience vide
        Assert.Equal(0, m.BpWeakMask);
        Assert.Equal(100, m.Patience);        // Deserialize @0x0860d790 l.101-102
        Assert.True(m.HasBpBar);              // mob+0x7d = patience != 0
        Assert.False(m.CanBreak);
        var r = BreakPointRules.Drain(m, 100, 1, 500, requireBar: true, 0);
        Assert.False(r.Drained);
        Assert.Equal(100, r.Patience);
    }

    [Fact]
    public void ParsePatience_DefaultCapAndDecimal()
    {
        static string[] Row(string mask, string pat)
        {
            var r = new string[20];
            Array.Fill(r, "");
            r[10] = mask; r[12] = pat;
            return r;
        }
        Assert.Equal(100, MonsterParser.ParsePatience(Row("0", "")));   // mask 0 + pat 0 → 100
        Assert.Equal(100, MonsterParser.ParsePatience(Row("", "")));
        Assert.Equal(0, MonsterParser.ParsePatience(Row("5", "")));     // mask ≠ 0, pat 0 → pas de barre
        Assert.Equal(54023, MonsterParser.ParsePatience(Row("5", "54023.74166")));
        Assert.Equal(9_999_999, MonsterParser.ParsePatience(Row("5", "99999999999")));
        Assert.Equal(1906236, MonsterParser.ParsePatience(Row("5", "1906236")));
    }

    [Fact]
    public void RealData_BreakableMobsAreNumerous_AndAllGatesAreOpen()
    {
        var breakable = Monsters.Value.Values.Where(m => m.CanBreak).ToList();
        Assert.True(breakable.Count > 1000);
        // Les gates réels sont soit 0xF (tout passe) soit 0.
        Assert.All(breakable.Where(m => m.BpWeakMask != 0),
            m => Assert.True(m.BpGate is 15 or 0));
    }

    // ------------------------------------------------ règles pures

    [Fact]
    public void AttackerAttr_FollowsNativeFourArgFunction()
    {
        // Sort : l'enchant d'arme l'emporte, sinon l'attribut du sort.
        Assert.Equal(4, BreakPointRules.AttackerAttr(0, 4, true, 1, true));
        Assert.Equal(6, BreakPointRules.AttackerAttr(6, 4, true, 1, true));
        Assert.Equal(0, BreakPointRules.AttackerAttr(0, 0, true, 1, true));
        // Mêlée : arme.
        Assert.Equal(3, BreakPointRules.AttackerAttr(0, 0, false, 3, true));
        // Sans arme : Strike.
        Assert.Equal(BreakPointRules.AttrStrike,
            BreakPointRules.AttackerAttr(0, 0, false, 0, false));
        // Arme sans attribut et sans enchant : aucun drain.
        Assert.Equal(0, BreakPointRules.AttackerAttr(0, 0, false, 0, true));
        Assert.Equal(5, BreakPointRules.AttackerAttr(5, 0, false, 0, true));
    }

    [Fact]
    public void Drain_SubtractsDamage_AndBreaksBelowOne()
    {
        var def = new MonsterParser.MonsterDef
        { BpWeakMask = 0b1, Patience = 100, BpGate = 15 };
        var r1 = BreakPointRules.Drain(def, 100, 1, 30, true, 0);
        Assert.True(r1.Drained); Assert.False(r1.Broke); Assert.Equal(70, r1.Patience);
        var r2 = BreakPointRules.Drain(def, 70, 1, 70, true, 0);
        Assert.True(r2.Broke); Assert.Equal(0, r2.Patience);        // < 1 → rupture
        var r3 = BreakPointRules.Drain(def, 0, 1, 50, true, 0);
        Assert.Equal(-50, r3.Patience);                              // pas de plancher
        // Mauvais élément / dégâts nuls : rien.
        Assert.False(BreakPointRules.Drain(def, 100, 2, 30, true, 0).Drained);
        Assert.False(BreakPointRules.Drain(def, 100, 1, 0, true, 0).Drained);
    }

    [Fact]
    public void Drain_MeleeRequiresBar_SpellsDoNot()
    {
        // Patience 0 + masque ≠ 0 : mob+0x7d = 0 → la mêlée ne draine pas
        // (AttackResult), un sort si (HitMagicSkill sans test 0x7d).
        var def = new MonsterParser.MonsterDef
        { BpWeakMask = 0b1, Patience = 0, BpGate = 15 };
        Assert.False(def.HasBpBar);
        Assert.False(BreakPointRules.Drain(def, 5, 1, 3, requireBar: true, 0).Drained);
        Assert.True(BreakPointRules.Drain(def, 5, 1, 3, requireBar: false, 0).Drained);
    }

    [Fact]
    public void Gate_AllOpenWhenF_ElseTierBit()
    {
        Assert.True(BreakPointRules.GateAllows(0xF, 4));
        Assert.True(BreakPointRules.GateAllows(0b00100, 2));
        Assert.False(BreakPointRules.GateAllows(0b00100, 1));
        Assert.False(BreakPointRules.GateAllows(0, 0));
        var def = new MonsterParser.MonsterDef
        { BpWeakMask = 1, Patience = 50, BpGate = 0b10 };
        Assert.False(BreakPointRules.Drain(def, 50, 1, 5, true, tier: 0).Drained);
        Assert.True(BreakPointRules.Drain(def, 50, 1, 5, true, tier: 1).Drained);
    }

    [Fact]
    public void SectorTier_IsSymmetricAndUnknownDirIsTier4()
    {
        Assert.Equal(4, BreakPointRules.SectorTier(0f, 0f));
        Assert.Equal(0, BreakPointRules.SectorTier(1f, 1.1f));
        Assert.NotEqual(BreakPointRules.SectorTier(2f, 1f),
            BreakPointRules.SectorTier(1f, 2f));
    }

    [Fact]
    public void SpellBreak_RollIsAboutTwoPercent_AndResistanceBlocks()
    {
        var rng = new Random(1234);
        var hits = 0;
        const int n = 200_000;
        for (var i = 0; i < n; i++)
            if (BreakPointRules.RollSpellBreak(rng)) hits++;
        var rate = hits / (double)n;
        Assert.InRange(rate, 0.017, 0.023);
        // Résistance 100 : jamais.
        Assert.DoesNotContain(true, Enumerable.Range(0, 5000)
            .Select(_ => BreakPointRules.RollSpellBreak(rng, 100)));
    }

    [Fact]
    public void Stagger_FallbackDurations()
    {
        Assert.Equal(4, BreakPointRules.FallbackStaggerSeconds(true));
        Assert.Equal(3, BreakPointRules.FallbackStaggerSeconds(false));
    }

    // ------------------------------------------------ LiveMob / Zone

    private static ZoneLiveWorld.LiveMob Mob(MonsterParser.MonsterDef def) =>
        new()
        {
            Handle = 0xFFFF8001, MobId = def.Id, NodeId = 6,
            Hp = 100, MaxHp = 100, Def = def,
        };

    [Fact]
    public void LiveMob_PatienceInitFromDef_ResetOnRespawn()
    {
        var mob = Mob(Monsters.Value[58471]);
        Assert.Equal(821, mob.MaxPatience);
        Assert.Equal(821, mob.Patience);
        mob.Patience = -20;
        mob.BpBroken = true;
        Assert.True(ZoneEntryFlow.BreakPoolDrops(mob));
        Assert.Equal(8, ZoneEntryFlow.DieFlags(mob));
        mob.ResetCombatForRespawn();                       // respawn
        Assert.Equal(821, mob.Patience);
        Assert.False(mob.BpBroken);
        Assert.False(ZoneEntryFlow.BreakPoolDrops(mob));
        Assert.Equal(0, ZoneEntryFlow.DieFlags(mob));
    }

    [Fact]
    public void LiveMob_WithoutDef_Patience100()
    {
        var mob = new ZoneLiveWorld.LiveMob
        { Handle = 1, MobId = 1, NodeId = 1 };
        Assert.Equal(100, mob.MaxPatience);
        Assert.Equal(100, mob.Patience);
        Assert.False(ZoneEntryFlow.BreakPoolDrops(mob));
    }

    [Fact]
    public void ResetCombat_InvalidatesPendingMobCast()
    {
        var mob = Mob(Monsters.Value[58471]);
        var serial = ++mob.CastSerial;
        mob.ResetCombatForRespawn();
        Assert.NotEqual(serial, mob.CastSerial);
    }

    // ------------------------------------------------ paquets

    [Fact]
    public void SeeMob_CarriesMaxPatienceInField4C_DefaultStaysCapture100()
    {
        var def = BuildSee(58471, 821);
        Assert.Equal(821, def.Unk4C);
        var plain = BuildSee(56001, 100);
        Assert.Equal(100, plain.Unk4C);
        // Aller-retour : le champ +0x4c (bit4) survit à la sérialisation.
        var o = new OutBinStream();
        def.Serialize(o);
        var back = new CNC_CZ_ClientSeeMob();
        back.Deserialize(new InBinStream(o.WrittenSpan.ToArray()));
        Assert.Equal(821, back.Unk4C);
    }

    private static CNC_CZ_ClientSeeMob BuildSee(int id, int maxPatience) =>
        ZoneEntryFlow.BuildSeeMob(0xFFFF8001, id, true, 10, 20, 0, 1f, 500,
            maxPatience: maxPatience);

    [Fact]
    public void UpdateAttr_PatienceAttr13RoundTrips_NegativeAllowed()
    {
        var msg = new CNC_CZ_ClientUpdateAttr
        { Field1 = 0x1234, Field2 = 0x13, Field3 = -50, Field4 = 0 };
        var o = new OutBinStream();
        msg.Serialize(o);
        Assert.Equal(18, o.WrittenSpan.Length);
        var back = new CNC_CZ_ClientUpdateAttr();
        back.Deserialize(new InBinStream(o.WrittenSpan.ToArray()));
        Assert.Equal(0x13, back.Field2);
        Assert.Equal(-50, back.Field3);
    }

    [Fact]
    public void ClientDie_BrokenFlagBit8()
    {
        var mob = Mob(Monsters.Value[58471]);
        mob.BpBroken = true;
        var die = new CNC_CZ_ClientDie
        { Field1 = mob.Handle, Field2 = 7, Field3 = ZoneEntryFlow.DieFlags(mob) };
        var o = new OutBinStream();
        die.Serialize(o);
        Assert.Equal(9, o.WrittenSpan.Length);
        Assert.Equal(8, o.WrittenSpan[8]);
    }

    // ------------------------------------------------ S_DropItem : 2 pools

    private static readonly Lazy<List<DropAndSpeedParsers.DropTable>> Drops =
        new(() => DropAndSpeedParsers.ParseDropItem(DbDir + "/S_DropItem.ini"));

    [Fact]
    public void HerakDrops_TrophyIsInBreakPoolOnly()
    {
        var t = Drops.Value.First(d => d.Id == 58471);
        Assert.Equal(1, t.RollCount);
        Assert.Equal(1, t.BreakRollCount);
        Assert.DoesNotContain(t.Entries, e => e.ItemId == 34013);   // « Cœur de Herak »
        var heart = Assert.Single(t.BreakEntries, e => e.ItemId == 34013);
        Assert.Equal(30f, heart.ChancePercent);
        Assert.Contains(t.Entries, e => e.ItemId == 32631);          // potion
        Assert.DoesNotContain(t.BreakEntries, e => e.ItemId == 32631);
    }

    [Fact]
    public void ChestDrops_RollCountIsFour()
    {
        var t = Drops.Value.First(d => d.Id == 63237);
        Assert.Equal(4, t.RollCount);
        Assert.NotEmpty(t.Entries);
    }

    [Fact]
    public void RollDropPool_BreakPoolOnlyOnRequest_AndCountRolls()
    {
        var svc = new LootService(Drops.Value,
            Array.Empty<GameDataParsers.ClassDropTable>());
        var rng = new Random(7);
        // Pool break de Herak : 30 % pour le cœur par tirage, 1 tirage.
        var hearts = 0;
        const int n = 20_000;
        for (var i = 0; i < n; i++)
            hearts += svc.RollDropPool(58471, true, 1.0, rng)
                .Count(l => l.ItemId == 34013);
        Assert.InRange(hearts / (double)n, 0.27, 0.33);
        // Le pool normal ne lâche jamais le trophée.
        for (var i = 0; i < 2000; i++)
            Assert.DoesNotContain(svc.RollDropPool(58471, false, 1.0, rng),
                l => l.ItemId == 34013);
        // Un tirage = au plus un objet ; 4 tirages = au plus 4.
        for (var i = 0; i < 500; i++)
            Assert.InRange(svc.RollDropPool(63237, false, 1.0, rng).Count, 0, 4);
    }

    [Fact]
    public void RollDropPool_EmptyShareOfTheRange_IsNothing()
    {
        // 56001 : somme des chances ≈ 18 % → ~82 % de tirages vides
        // (col 7 de la ligne = 82 : part « rien » native).
        var svc = new LootService(Drops.Value,
            Array.Empty<GameDataParsers.ClassDropTable>());
        var rng = new Random(99);
        var t = Drops.Value.First(d => d.Id == 56001);
        var sum = t.Entries.Sum(e => e.ChancePercent);
        Assert.InRange(sum, 10, 40);
        var got = 0;
        const int n = 20_000;
        for (var i = 0; i < n; i++)
            got += svc.RollDropPool(56001, false, 1.0, rng).Count;
        Assert.InRange(got / (double)n, sum / 100.0 - 0.03, sum / 100.0 + 0.03);
    }

    [Fact]
    public void RollDropPool_UnknownTableOrZeroCount_NoDrops()
    {
        var svc = new LootService(Drops.Value,
            Array.Empty<GameDataParsers.ClassDropTable>());
        Assert.Empty(svc.RollDropPool(-5, false));
        var t = new DropAndSpeedParsers.DropTable { Id = 1, RollCount = 0 };
        t.Entries.Add(new DropAndSpeedParsers.DropEntry
        { ItemId = 5, ChancePercent = 100, Quantity = 1 });
        var s2 = new LootService(new[] { t },
            Array.Empty<GameDataParsers.ClassDropTable>());
        Assert.Empty(s2.RollDropPool(1, false));   // count < 1 → retour immédiat
    }

    // ------------------------------------------------ world boss (plan 12 étape 3)

    [Fact]
    public void WorldBosses_UseTheSameBreakPointMechanism_MostAreBreakable()
    {
        var m = Monsters.Value;
        var ids = Eden.Core.Game.WorldBoss.WorldBossCatalog.FixedBosses.Select(b => b.MobId)
            .Where(m.ContainsKey).ToList();
        Assert.NotEmpty(ids);
        var breakable = ids.Count(i => m[i].CanBreak);
        // Les 26 boss fixes portent masque + patience dans S_Monster
        // (aucun mécanisme spécifique « world boss » : même drain/rupture/
        // pool break que les boss de donjon).
        Assert.True(breakable == ids.Count,
            $"{breakable}/{ids.Count} cassables");
    }

    [Fact]
    public void DropPools_ChancesSumToAbout100_ForMostRows()
    {
        // Preuve du layout itemId|nom|qté|chance (col 11/96, pas de 4) :
        // l'ancien parseur (rate|item|nom|qté dès la col 10) décalait les
        // chances d'une entrée et ne retombait pas sur 100.
        var sums = Drops.Value
            .Where(t => t.Entries.Count >= 3)
            .Select(t => t.Entries.Sum(e => e.ChancePercent)).ToList();
        // Un pool natif somme au plus 100 % (le reste = tirage vide) :
        // plus de 95 % des pools respectent la borne, et > 1 000 pools
        // tombent exactement sur 100 (entrées exclusives).
        Assert.True(sums.Count(s => s <= 100.6) > sums.Count * 0.95,
            $"{sums.Count(s => s > 100.6)}/{sums.Count} pools > 100 %");
        Assert.True(sums.Count(s => Math.Abs(s - 100) < 0.6) > 1000);
        var brk = Drops.Value.Where(t => t.BreakEntries.Count >= 3)
            .Select(t => t.BreakEntries.Sum(e => e.ChancePercent)).ToList();
        Assert.True(brk.Count > 100);
        Assert.True(brk.Count(s => s <= 100.6) > brk.Count * 0.8);  // qq pools > 100 : plage = max(100, somme) natif
    }
}
