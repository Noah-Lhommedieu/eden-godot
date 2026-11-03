using EdenClient.Data;
using Xunit;

namespace NetTests;

/// <summary>CharColorTable — convention d'indexation prouvée par
/// désasm client_game (FUN_0073e970/8d0/a60) : sélecteur blob +
/// base de section par code corps → id de ligne C_CharColor.ini.
/// La table réelle est lue via raw_assets/ (skip si absent).</summary>
public class CharColorTests
{
    private static string? FindRawAssets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName,
                    "raw_assets", "data")))
                return Path.Combine(dir.FullName, "raw_assets");
            dir = dir.Parent;
        }
        return null;
    }

    private static void BindResolver()
    {
        var root = FindRawAssets();
        if (root == null) return;
        // Casse-insensible — même sémantique que AssetCatalog (le corpus
        // nomme le fichier c_charcolor.ini, le code demande C_CharColor).
        TextResolver.ResolveFile =
            EdenClient.NetTests.IniTableTests.CaseInsensitiveResolver(root);
    }

    private static void AssertRgb(CharColorTable.Rgb c,
        int r, int g, int b)
    {
        Assert.Equal(r / 255f, c.R, 3);
        Assert.Equal(g / 255f, c.G, 3);
        Assert.Equal(b / 255f, c.B, 3);
    }

    [Fact]
    public void RealFile_SkinBase_PerBodyCode()
    {
        var root = FindRawAssets();
        if (root == null) return;
        BindResolver();

        // Bases natives (FUN_0073e970) : Human/Halfkin 201,
        // Zumi 401, Anuran 501, Ursun 601.
        Assert.Equal(201, CharColorTable.SkinBase(0));
        Assert.Equal(201, CharColorTable.SkinBase(1));
        Assert.Equal(401, CharColorTable.SkinBase(2));
        Assert.Equal(501, CharColorTable.SkinBase(4));
        Assert.Equal(601, CharColorTable.SkinBase(6));
        Assert.Equal(201, CharColorTable.SkinBase(8));
        Assert.Equal(201, CharColorTable.SkinBase(9));

        // Valeurs réelles C_CharColor.ini : sel=0 → première teinte
        // de la section (SKIN_TONE_1 = 207,153,114 ; ZUMI_1 =
        // 77,95,179 ; ANURAN_1 et URSUN_1 lus dans la table).
        AssertRgb(CharColorTable.Skin(1, 0), 207, 153, 114);
        AssertRgb(CharColorTable.Skin(2, 0), 77, 95, 179);
        // skin=7 humain → 208 = SKIN_TONE_8 (149,132,100) — PAS le
        // dye rouge (ligne positionnelle 7 = DYE_RED) — régression
        // du bug « peau rouge ».
        var s7 = CharColorTable.Skin(0, 7);
        AssertRgb(s7, 149, 132, 100);
        Assert.NotEqual(181 / 255f, s7.R);   // ≠ DYE_RED
    }

    [Fact]
    public void RealFile_HairAndEyes_Bases()
    {
        var root = FindRawAssets();
        if (root == null) return;
        BindResolver();

        // Cheveux : 101 générique, 701 Ursun (FUN_0073e8d0).
        Assert.Equal(101, CharColorTable.HairBase(0));
        Assert.Equal(101, CharColorTable.HairBase(2));
        Assert.Equal(701, CharColorTable.HairBase(6));
        // 101 = HAIR_LIGHT_BROWN (220,180,150 dans le ini) ;
        // 701 = HAIR_COLOR_URSUN_1 (220,180,150).
        AssertRgb(CharColorTable.Hair(0, 0), 220, 180, 150);
        AssertRgb(CharColorTable.Hair(6, 0), 220, 180, 150);
        // Yeux : 301 partout (FUN_0073ea60) — sel 0 = EYES_SLATE_GRAY.
        AssertRgb(CharColorTable.Eyes(0), 75, 90, 84);
        // Teinture : palette 64 = lignes id 1..64 (FUN_006e6620).
        AssertRgb(CharColorTable.Dye(0), 241, 239, 194); // DYE_IVORY
        AssertRgb(CharColorTable.Dye(7), 181, 32, 32);   // DYE_RED
    }

    [Fact]
    public void Fallback_WhenIdMissing()
    {
        BindResolver();
        var root = FindRawAssets();
        // Table absente OU id absent → repli base section / carnation.
        var skin = CharColorTable.Skin(1, 60);   // id 261 inexistant
        if (root != null)
            AssertRgb(skin, 207, 153, 114);      // repli 201
        else
            AssertRgb(skin, 255, 209, 168);      // carnation
    }
}
