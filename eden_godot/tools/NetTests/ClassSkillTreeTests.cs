using EdenClient.Data.Tables;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Skills et talents par classe (dont éveil) depuis les tables
/// EDENFR : prouve que chaque classe a sa liste et son arbre, et que les
/// colonnes fr-full (pay col41, base de coût col42) sont bien lues.</summary>
public class ClassSkillTreeTests
{
    private static bool Init()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var db = Path.Combine(dir.FullName, "raw_assets", "data", "db");
            if (Directory.Exists(db))
            {
                ClassSkillTree.Load(Path.Combine(db, "c_spell.ini"),
                    Path.Combine(db, "c_class.ini"),
                    Path.Combine(db, "c_talent.ini"));
                return true;
            }
            dir = dir.Parent;
        }
        return false;
    }

    [Fact]
    public void BaseOf_MapsAwakeningClasses()
    {
        Assert.Equal(1, ClassSkillTree.BaseOf(21));
        Assert.Equal(1, ClassSkillTree.BaseOf(22));
        Assert.Equal(10, ClassSkillTree.BaseOf(40));
        Assert.Equal(19, ClassSkillTree.BaseOf(58));
        Assert.Equal(59, ClassSkillTree.BaseOf(61));
        Assert.Equal(12, ClassSkillTree.BaseOf(12));
    }

    [Fact]
    public void ClassSkills_PerClass_AreDistinct_AndPaidInCp()
    {
        if (!Init()) return;
        var war = ClassSkillTree.ClassSkills(1);
        var mage = ClassSkillTree.ClassSkills(2);
        var awk = ClassSkillTree.ClassSkills(21);
        // vecteur c_class (50036-42, 50151-52) + mono-masque 58345
        Assert.Equal(10, war.Count);
        Assert.Contains(war, s => s.Id == 50036);
        Assert.Contains(war, s => s.Id == 58345);
        Assert.All(war, s => Assert.Equal(9, s.Pay));
        Assert.All(war, s => Assert.True(s.UpgradeBase > 0));
        Assert.DoesNotContain(war, s => mage.Any(m => m.Id == s.Id));
        // éveil 21 : vecteur c_class (50687-94) + mono-masque (59632-34, 59681)
        Assert.Equal(12, awk.Count);
        Assert.All(awk, s => Assert.Equal(80, s.RequiredLevel));
        // éveil 43 : le vecteur c_class répète la classe 12, les skills
        // propres (56081-…) viennent du masque mono-classe
        var awk43 = ClassSkillTree.ClassSkills(43);
        Assert.Contains(awk43, s => s.Id == 56081);
        Assert.Contains(awk43, s => s.Id == 50118);
        Assert.DoesNotContain(ClassSkillTree.ClassSkills(12), s => s.Id == 56081);
        Assert.DoesNotContain(awk, s => war.Any(m => m.Id == s.Id));
        Assert.All(ClassSkillTree.ClassSkills(2),
            s => Assert.NotEqual(7, s.Family));
    }

    [Fact]
    public void RegSkills_AreGold_AwakeningKeepsBaseClassSet()
    {
        if (!Init()) return;
        var war = ClassSkillTree.RegSkills(1, 0);
        Assert.NotEmpty(war);
        Assert.All(war, s => Assert.Equal(1, s.Pay));
        Assert.Equal(war.Select(s => s.Id),
            ClassSkillTree.RegSkills(21, 0).Select(s => s.Id));
        // race inconnue : aucun sort racial
        Assert.DoesNotContain(ClassSkillTree.RegSkills(1, -1),
            s => ClassSkillTree.IsRacial(s.Id, out _));
        // une seule race affichée
        Assert.DoesNotContain(ClassSkillTree.RegSkills(1, 2),
            s => ClassSkillTree.IsRacial(s.Id, out var r) && r != 2);
    }

    [Fact]
    public void Talents_AreDynamic_PerClass_AndAwakening()
    {
        if (!Init()) return;
        string Sig(int cls) => string.Join(";", ClassSkillTree.Talents(cls)!
            .SelectMany(b => b).Select(s => s?.SpellId.ToString() ?? "-"));
        // arbres propres : guerrier, mage, éveil 21
        Assert.NotEqual(Sig(1), Sig(2));
        Assert.NotEqual(Sig(1), Sig(21));
        Assert.NotEqual(Sig(21), Sig(22));
        // classes éveil sans ligne propre → arbre de la classe de base
        Assert.Equal(Sig(10), Sig(40));
        Assert.Equal(Sig(10), Sig(39));
        Assert.Equal(Sig(12), Sig(43));
        // 52582 : 1er talent du guerrier, prérequis 54699 → 52582
        var w = ClassSkillTree.Talents(1)![0];
        Assert.Equal(52582, w[0]!.SpellId);
        Assert.Equal(52582, w[1]!.PreSpell);
        // toute classe 1-61 a un arbre non vide
        for (var c = 1; c <= 61; c++)
            Assert.True(ClassSkillTree.Talents(c)?
                .Any(b => b.Any(s => s != null)) ?? false, $"classe {c}");
    }
}
