using System.Text;
using Eden.Core.Data.Parsers;
using Eden.Core.Game.Services;
using Xunit;

namespace Eden.Tests;

/// <summary>KB-01 (garde-fou schéma c_enchant) et KB-02 (rankbuff par
/// classId) — docs/knowledge/known-bugs.md.</summary>
public class ClassBugfixTests
{
    private static readonly string FrDb = Path.GetFullPath(Path.Combine(
        TestPaths.RepoRoot, "..", "corpus", "fr-full", "data", "db"));

    static ClassBugfixTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void NormalizeRow_FrRowGetsElfColumn_UsRowUntouched()
    {
        var fr = new string[61];
        Array.Fill(fr, "");
        fr[6] = "1"; fr[8] = "2084";
        var n = EnchantParser.NormalizeRow(fr, 60);
        Assert.Equal(62, n.Length);
        Assert.Equal("", n[6]);
        Assert.Equal("1", n[7]);
        Assert.Equal("2084", n[9]);
        // FR tronqué (56) seulement avec l'en-tête 60.
        Assert.Equal(57, EnchantParser.NormalizeRow(new string[56], 60).Length);
        Assert.Equal(56, EnchantParser.NormalizeRow(new string[56], 61).Length);
        // Lignes S_ (62) et anomalie 46131 (55) inchangées.
        var us = new string[62];
        Assert.Same(us, EnchantParser.NormalizeRow(us, 61));
        var an = new string[55];
        Assert.Same(an, EnchantParser.NormalizeRow(an, 60));
    }

    [Fact]
    public void FrEnchants_DecodeRealCommandIds()
    {
        var path = Path.Combine(FrDb, "c_enchant.ini");
        if (!File.Exists(path)) return;
        var defs = EnchantParser.Parse(path);
        Assert.True(defs.Count > 15000);
        // Aucune def FR ne doit garder une cellule 6 non vide (= décalage).
        var shifted = defs.Count(d => d.Raw.Length == 62 && d.Raw[6] != "");
        Assert.Equal(0, shifted);
        var svc = new EnchantService(defs);
        // 45747 « Nécromancien points bonus 1 » : 2084|… puis 2047|13.
        var cmds = svc.GetUseCommands(45747).Select(c => c.CommandId).ToList();
        Assert.Contains(2084, cmds);
        Assert.Contains(2047, cmds);
        var d45747 = defs.Single(d => d.Id == 45747);
        Assert.True(d45747.Enabled);
        Assert.Equal(48, d45747.TriggerType);
        Assert.Contains("#12#", d45747.Description);
    }

    [Fact]
    public void FrEnchants_ChanceStones_UseNativeParamLayout()
    {
        // Kind 8002 : param[i] @ col(9+i) sur la ligne normalisée —
        // p1=bonus, p5=safe-level, p7=bind, p9/p11=fenêtre
        // (CGameData::Integration @0x85995b0).
        var path = Path.Combine(FrDb, "c_enchant.ini");
        if (!File.Exists(path)) return;
        var defs = EnchantParser.Parse(path);
        var byId = defs.Where(d => d.TriggerParams[0] == 0x1f42)
            .ToDictionary(d => d.Id);
        Assert.True(byId.Count >= 6);
        // 59170-72 « pierre bénie +3~6/+6~8/+8~10 » : bonus 100, seuil.
        Assert.Equal(100, byId[59170].ChanceBonus);
        Assert.Equal(6, byId[59170].ChanceSafeLevel);
        Assert.Equal(8, byId[59171].ChanceSafeLevel);
        Assert.Equal(10, byId[59172].ChanceSafeLevel);
        // 57538/57539 « pierre de chance » : faible bonus, seuil 1.
        Assert.Equal(2, byId[57538].ChanceBonus);
        Assert.Equal(1, byId[57538].ChanceSafeLevel);
        Assert.Equal(7, byId[57539].ChanceBonus);
        // 59901 « 必成強化 » dev : +100 %, aucune borne → libre partout.
        Assert.Equal(100, byId[59901].ChanceBonus);
        Assert.Equal(0, byId[59901].ChanceSafeLevel);
        Assert.Equal(0, byId[59901].ChanceUpper);
    }

    [Fact]
    public void ClassRankBuff_LookupIsByClassIdNotRowIndex()
    {
        var gd = new GameDataService(TestPaths.ServerUsData + "/db");
        gd.LoadAll();
        var defs = gd.ClassRankBuffs;
        // Juge (59) = ligne d'index 42 ; l'index 59 est la classe 57.
        var judge = ShopAndMiscParsers.FindClassRankBuff(defs, 59)!;
        Assert.Equal(59, judge.ClassId);
        Assert.Equal(40911, judge.Tiers[0].EnchantId);
        Assert.Equal(57, ShopAndMiscParsers.FindClassRankBuff(defs, 57)!.ClassId);
        // Classes 1-19 : index == classId, inchangé.
        Assert.Equal(45492,
            ShopAndMiscParsers.FindClassRankBuff(defs, 1)!.Tiers[0].EnchantId);
        // Id à préfixe racial ramené au rang local.
        Assert.Equal(59, ShopAndMiscParsers.FindClassRankBuff(defs, 159)!.ClassId);
        // La ligne d'index 59 n'est PAS celle de la classe 59.
        Assert.NotEqual(defs.First(d => d.Id == 59).Tiers[0],
            judge.Tiers[0]);
    }

    [Fact]
    public void ClassFor_UsesRaceBand_NotHumanRow()
    {
        var gd = new GameDataService(TestPaths.ServerUsData + "/db");
        gd.LoadAll();
        var cl = gd.Classes;
        Assert.Equal(1, Eden.Core.Game.Classes.ClassSkillTable.ClassFor(cl, 1, 1)!.Id);
        Assert.Equal(101, Eden.Core.Game.Classes.ClassSkillTable.ClassFor(cl, 2, 1)!.Id);
        Assert.Equal(459, Eden.Core.Game.Classes.ClassSkillTable.ClassFor(cl, 5, 59)!.Id);
        // Race inconnue : repli sur la ligne classId.
        Assert.Equal(1, Eden.Core.Game.Classes.ClassSkillTable.ClassFor(cl, 0, 1)!.Id);
    }

    [Fact]
    public void RacialSpells_FilteredByRaceFlag()
    {
        var gd = new GameDataService(TestPaths.ServerUsData + "/db");
        gd.LoadAll();
        Assert.True(Eden.Core.Game.Classes.ClassSkillTable.RaceAllows(-1, 3));
        Assert.True(Eden.Core.Game.Classes.ClassSkillTable.RaceAllows(3, 1));
        Assert.False(Eden.Core.Game.Classes.ClassSkillTable.RaceAllows(3, 2));
        Assert.True(Eden.Core.Game.Classes.ClassSkillTable.RaceAllows(768, 5));
        Assert.True(Eden.Core.Game.Classes.ClassSkillTable.RaceAllows(855, 4));
        Assert.Equal(3, gd.Spells.Single(x => x.Id == 50202).RaceFlag);
        Assert.Equal(4, gd.Spells.Single(x => x.Id == 50240).RaceFlag);

        var human = Eden.Core.Game.Classes.ClassSkillTable
            .GrantableGeneralSpells(1, 60, gd.Spells, 1);
        var zumi = Eden.Core.Game.Classes.ClassSkillTable
            .GrantableGeneralSpells(1, 60, gd.Spells, 2);
        Assert.Contains(50202, human);
        Assert.DoesNotContain(50240, human);
        Assert.DoesNotContain(50202, zumi);
        Assert.Contains(50240, zumi);

        // Expertise J504 : variante Humaine 50201 / Zumi 50239.
        var hDef = Eden.Core.Game.Classes.ClassSkillTable.ClassFor(gd.Classes, 1, 1);
        var zDef = Eden.Core.Game.Classes.ClassSkillTable.ClassFor(gd.Classes, 2, 1);
        var hs = Eden.Core.Game.Classes.ClassSkillTable.GrantableSpells(1, 10, gd.Spells, hDef, 1);
        var zs = Eden.Core.Game.Classes.ClassSkillTable.GrantableSpells(1, 10, gd.Spells, zDef, 2);
        Assert.Contains(50201, hs);
        Assert.DoesNotContain(50239, hs);
        Assert.Contains(50239, zs);
        Assert.DoesNotContain(50201, zs);
    }

    /// <summary>KB-05 : cmd 2047 (HealPlus modes 4/5) alimente les canaux
    /// flat — enchant de palier 45747 (`2047|13`).</summary>
    [Fact]
    public void Cmd2047_HealPlusFlat_FeedsHealChannels()
    {
        var path = Path.Combine(FrDb, "c_enchant.ini");
        if (!File.Exists(path)) return;
        var svc = new EnchantService(EnchantParser.Parse(path));
        var c = new Eden.Core.Game.Stats.StatContribution();
        Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
            c, svc.GetUseCommands(45747));
        Assert.Equal(13, c.HealGivenFlat);
        Assert.Equal(0, c.HealTakenFlat);
        // arg1 → soins reçus flat (même dispatch que 2038).
        var c2 = new Eden.Core.Game.Stats.StatContribution();
        Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(c2,
            new[] { new EnchantService.UseCommand(2047, new double[] { 5, 7, 0, 0, 0, 0 }) });
        Assert.Equal(5, c2.HealGivenFlat);
        Assert.Equal(7, c2.HealTakenFlat);
    }

    // ── KB-04 / KB-03 ──────────────────────────────────────────────

    [Fact]
    public void ClassRules_AwakenedAndBase()
    {
        foreach (var c in new[] { 21, 22, 40, 58, 60, 61, 321 })
            Assert.True(Eden.Core.Game.Classes.ClassRules.IsAwakened(c), $"{c}");
        foreach (var c in new[] { 1, 19, 20, 59, 159 })
            Assert.False(Eden.Core.Game.Classes.ClassRules.IsAwakened(c), $"{c}");
        Assert.Equal(1, Eden.Core.Game.Classes.ClassRules.BaseOf(21));
        Assert.Equal(19, Eden.Core.Game.Classes.ClassRules.BaseOf(58));
        Assert.Equal(59, Eden.Core.Game.Classes.ClassRules.BaseOf(61));
        Assert.Equal(201, Eden.Core.Game.Classes.ClassRules.BaseOf(222));
    }

    [Fact]
    public void ClassPrereqColumns_ParsedAndUnlockRule()
    {
        var gd = new GameDataService(TestPaths.ServerUsData + "/db");
        gd.LoadAll();
        var k = gd.Classes.Single(c => c.Id == 7);      // Chevalier
        Assert.Equal(1UL, k.RequiredClassMask);          // Guerrier
        Assert.Equal(10, k.RequiredClassLevel);
        Assert.Equal(25, k.RequiredCharLevel);
        Assert.Equal(72UL, gd.Classes.Single(c => c.Id == 13).RequiredClassMask);
        Assert.Equal(0, gd.Classes.Single(c => c.Id == 21).RequiredCharLevel);

        var U = Eden.Core.Game.Classes.ClassUnlockRules.Unlockable;
        // Guerrier lv10 + perso 25 : Chevalier OK ; Paladin (60 + deux
        // classes) non ; éveillées jamais.
        var ok = U(gd.Classes, 1, 25, new[] { (1, 10) });
        Assert.Contains(7, ok);
        Assert.DoesNotContain(13, ok);
        Assert.DoesNotContain(21, ok);
        Assert.DoesNotContain(1, ok);                    // déjà possédée
        // Guerrier lv9 : pas de Chevalier.
        Assert.DoesNotContain(7, U(gd.Classes, 1, 25, new[] { (1, 9) }));
        // Perso 24 : pas de Chevalier.
        Assert.DoesNotContain(7, U(gd.Classes, 1, 24, new[] { (1, 10) }));
        // Paladin : Chevalier 45 + Prêtre 45 + perso 60.
        Assert.Contains(13, U(gd.Classes, 1, 60,
            new[] { (1, 50), (7, 45), (4, 45) }));
        Assert.DoesNotContain(13, U(gd.Classes, 1, 60,
            new[] { (1, 50), (7, 45), (4, 44) }));
    }

    [Fact]
    public async Task AddClassAsync_AwakenedStartsAt80_BaseAt1_AndMigration()
    {
        var cs = TestDb.ConnStr("edenfr_db");
        const int Player = 999000222;
        Npgsql.NpgsqlDataSource? ds = null;
        try { ds = Npgsql.NpgsqlDataSource.Create(cs); await using var c0 = await ds.OpenConnectionAsync(); }
        catch { return; }                                 // DB absente : skip
        using (ds)
        {
            var repo = new Eden.Core.Database.Repositories.ClassRepository(ds);
            await using var c = await ds.OpenConnectionAsync();
            async Task Clean() => await Dapper.SqlMapper.ExecuteAsync(c,
                "DELETE FROM player_classlist WHERE player_id=@P", new { P = Player });
            try
            {
                await Clean();
                await repo.AddClassAsync(Player, 21);
                await repo.AddClassAsync(Player, 7);
                await repo.AddClassAsync(Player, 59);
                var rows = (await repo.GetClassesByCharacterIdAsync(Player))
                    .ToDictionary(r => r.ClassId);
                Assert.Equal(80, rows[21].ClassLevel);
                Assert.Equal(80, rows[21].AaPoint);
                Assert.Equal(1, rows[7].ClassLevel);
                Assert.Equal(1, rows[59].ClassLevel);      // Juge = base
                // Migration : éveillée historique lv1/aa1 → 80/80.
                await Dapper.SqlMapper.ExecuteAsync(c,
                    "UPDATE player_classlist SET level=1, aa_point=1 WHERE player_id=@P AND class=21",
                    new { P = Player });
                Assert.True(await repo.PromoteAwakenedAsync() >= 1);
                rows = (await repo.GetClassesByCharacterIdAsync(Player))
                    .ToDictionary(r => r.ClassId);
                Assert.Equal(80, rows[21].ClassLevel);
                Assert.Equal(1, rows[7].ClassLevel);       // base inchangée
            }
            finally { await Clean(); }
        }
    }
}
