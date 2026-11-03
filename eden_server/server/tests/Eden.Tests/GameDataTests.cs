using System.Text;
using Xunit;
using Eden.Core.Game.Services;

namespace Eden.Tests;

public class GameDataServiceTests
{
    private static readonly string DbDir = TestPaths.RepoRoot + "/server_us/Data/db";

    static GameDataServiceTests()
        => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    [Fact]
    public void LoadAll_LoadsEveryCatalog()
    {
        var gd = new GameDataService(DbDir);
        gd.LoadAll();
        Assert.Equal(305, gd.Classes.Count);
        Assert.True(gd.Items.Count > 20000);
        Assert.True(gd.Monsters.Count > 12000);
        Assert.True(gd.Spells.Count > 4000);
        Assert.True(gd.Missions.Count > 2800);
        Assert.True(gd.Enchants.Count > 19000);
        Assert.True(gd.ElfRecipes.Count > 900);
        Assert.True(gd.FamilyRecipes.Count > 3000);
        Assert.True(gd.Elfs.Count > 200);
        Assert.True(gd.Battlefields.Count > 0);
        Assert.True(gd.DropTables.Count > 4000);
        Assert.True(gd.MonsterSpeeds.Count > 300);
        Assert.True(gd.Talents.Count > 900);
        Assert.True(gd.Titles.Count > 300);
        Assert.True(gd.Achievements.Count > 3800);
        Assert.True(gd.Nodes.Count > 300);
        Assert.True(gd.ClassRankBuffs.Count > 50);
    }

    /// <summary>S_ClassRankBuff — 6 champs/palier, enchant = 6ᵉ
    /// (désasm Deserialize @0x85c44xx + RefreshJPAward @0x8417c10 :
    /// node+0x28). Rang natif = floor(min(classLv, S_Parameter
    /// 578=100) × 0.2) → class1 lv5 → palier1 → enchant 45492
    /// (« Warrior Points Bonus Tier 1 »).</summary>
    [Fact]
    public void ClassRankBuff_TierMapsToEnchant()
    {
        var gd = new GameDataService(DbDir);
        gd.LoadAll();
        var warrior = gd.ClassRankBuffs.First(d => d.Id == 1);
        Assert.Equal((1, 45492), warrior.Tiers[0]);
        Assert.Equal((2, 45493), warrior.Tiers[1]);
        Assert.Equal(20, warrior.Tiers.Count);
        var priest = gd.ClassRankBuffs.First(d => d.Id == 4);
        Assert.Equal((1, 45657), priest.Tiers[0]);
        // rang = floor(classLv×0.2) : lv5→1, lv7→1, lv10→2, lv100→20.
        Assert.Equal(1, (int)(Math.Min(7, 100f) * 0.2f));
        Assert.Equal(2, (int)(Math.Min(10, 100f) * 0.2f));
        Assert.Equal(20, (int)(Math.Min(120, 100f) * 0.2f));
    }

    /// <summary>L'enchant 45492 (« Warrior Points Bonus Tier 1 »)
    /// décode en [2084 arg0=33 → +P-ATK flat] + [2003 arg0=41 →
    /// +41 MaxHP post] — la contribution du stage « classrank » pour
    /// un guerrier classLv≥5.</summary>
    [Fact]
    public void ClassRankBuff_EnchantCommandsResolve()
    {
        var gd = new GameDataService(DbDir);
        gd.LoadAll();
        var enchants = new EnchantService(gd.Enchants);
        var c = new Eden.Core.Game.Stats.StatContribution();
        Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
            c, enchants.GetUseCommands(45492));
        Assert.Equal(41, c.Post(Eden.Core.Game.Stats.StatIndex.Hp));
        Assert.Equal(33, c.PhysAttackAdd);
        // Magicien tier 1 (45717) : +M-ATK via arg2 du 2084, +MP.
        var m = new Eden.Core.Game.Stats.StatContribution();
        Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
            m, enchants.GetUseCommands(45717));
        Assert.Equal(47, m.MagicAttackAdd);
        Assert.Equal(29, m.Post(Eden.Core.Game.Stats.StatIndex.Mp));
    }

    /// <summary>Enchants de titres S_Title — commande 2086
    /// « AttrBase » mode 0 (flat 5 stats → canal <b>post</b> natif
    /// kinddata+0x540, ordre vars Str,Agi,Int,Wis,Lck vérifié sur
    /// 53090-53094), 2085 « AttrBaseRate » mode 2 (mêmes vars en %
    /// → canal pct +0x178, titre 336 « 全能力3% »), 2002
    /// « AttrMaxHPRate » (titre « HP上限3% »).</summary>
    [Fact]
    public void TitleEnchants_AttrBaseCommandsResolve()
    {
        var gd = new GameDataService(DbDir);
        gd.LoadAll();
        var enchants = new EnchantService(gd.Enchants);
        static Eden.Core.Game.Stats.StatContribution Apply(
            EnchantService es, int id)
        {
            var c = new Eden.Core.Game.Stats.StatContribution();
            Eden.Core.Game.Classes.LicenseService.ApplyEnchantCommands(
                c, es.GetUseCommands(id));
            return c;
        }
        // 53090 : 2086 arg1=5 → Agi flat +5 (« 敏捷屬性提高 5 點 »).
        // Canal POST natif : CEC_AttrBase::Execute mode 0 → accum
        // kinddata+0x540, ajouté APRÈS le % (CCharFormula::Str =
        // ROUND((eq+base)×(0x178+classPct)×0.01) + 0x540).
        var agi = Apply(enchants, 53090);
        Assert.Equal(5, agi.Post(Eden.Core.Game.Stats.StatIndex.Agi));
        Assert.Equal(0, agi.Post(Eden.Core.Game.Stats.StatIndex.Str));
        // 53091 : arg0=5 → Str (« 力量屬性提高 5 點 »).
        var str = Apply(enchants, 53091);
        Assert.Equal(5, str.Post(Eden.Core.Game.Stats.StatIndex.Str));
        // 53094 : arg4=5 → Lck (« 運氣屬性提高 5 點 »).
        var lck = Apply(enchants, 53094);
        Assert.Equal(5, lck.Post(Eden.Core.Game.Stats.StatIndex.Lck));
        // 51075 (titre 336 « 全能力 3% ») : 2085 vars 0-4 = 3 → pct.
        var all = Apply(enchants, 51075);
        Assert.Equal(3, all.Pct(Eden.Core.Game.Stats.StatIndex.Str));
        Assert.Equal(3, all.Pct(Eden.Core.Game.Stats.StatIndex.Lck));
        // 59465 (« HP和MP上限3% ») : 2002|3 + 2013|3 → pct Hp + Mp.
        var hp = Apply(enchants, 59465);
        Assert.Equal(3, hp.Pct(Eden.Core.Game.Stats.StatIndex.Hp));
        Assert.Equal(3, hp.Pct(Eden.Core.Game.Stats.StatIndex.Mp));
    }

    /// <summary>MultiStroke passifs — `GetAttackCommands` retourne les
    /// commandes 1031/2031/2032 des blocs NON déclenchés (col40 vide),
    /// avec args `[chance1, facteur1, chance2, facteur2]` à arg0..3
    /// (corrélé tooltips). Datové sur vraies lignes S_Enchant.</summary>
    [Fact]
    public void GetAttackCommands_PassiveMultiStrokesResolve()
    {
        var gd = new GameDataService(DbDir);
        gd.LoadAll();
        var enchants = new EnchantService(gd.Enchants);
        // 52196 « Triple Slash » : 2031|6|25|100|10 + 2032|6|25|100|10,
        // col40/41 vides → 2 commandes passives [6,25,100,10].
        var triple = enchants.GetAttackCommands(52196);
        Assert.Equal(2, triple.Count);
        Assert.Equal(new[] { 2031, 2032 },
            triple.Select(c => c.CommandId).ToArray());
        Assert.Equal(new double[] { 6, 25, 100, 10 },
            triple[0].Args.Take(4).ToArray());
        // 52018 « Double Hit » : 2031|15|50 + 2032|15|50 → [15,50].
        var dbl = enchants.GetAttackCommands(52018);
        Assert.Equal(2, dbl.Count);
        Assert.Equal(new double[] { 15, 50 },
            dbl[0].Args.Take(2).ToArray());
        // 53225 : seul bloc = 2033 (DamageAddPlus, hors filtre) et
        // slot1 déclenché (col40=1) → aucune commande passive.
        Assert.Empty(enchants.GetAttackCommands(53225));
        // Son GetProc reste valide (evt1 attaque, 5 %).
        Assert.NotNull(enchants.GetProc(53225));
    }

    [Fact]
    public void LoadAll_MissingDirDoesNotThrow()
    {
        var gd = new GameDataService("/nonexistent/path");
        gd.LoadAll(); // ne doit pas lever
        Assert.Empty(gd.Items);
    }
}

public class ItemExtraMergeTests
{
    /// <summary>S_ItemExtra est prioritaire : le placeholder US 48813
    /// (« 測試用編號 ») est remplacé par la def EDENFR ; un id inconnu
    /// est ajouté.</summary>
    [Fact]
    public void MergeItemExtra_ReplacesCollidingAndAddsNew()
    {
        var items = new List<Eden.Core.Data.Parsers.ItemParser.ItemDef>
        {
            new() { Id = 48813, Name = "placeholder US" },
            new() { Id = 1, Name = "conserve" },
        };
        GameDataService.MergeItemExtra(items, new[]
        {
            new Eden.Core.Data.Parsers.ItemParser.ItemDef
                { Id = 48813, Name = "Renard Blanc" },
            new Eden.Core.Data.Parsers.ItemParser.ItemDef
                { Id = 1290, Name = "Kitsune" },
        });
        Assert.Equal(3, items.Count);
        Assert.Equal("Renard Blanc", items.Single(i => i.Id == 48813).Name);
        Assert.Equal("Kitsune", items.Single(i => i.Id == 1290).Name);
        Assert.Single(items, i => i.Id == 1);
    }
}
