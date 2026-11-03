using System.Text;
using EdenClient.Data;
using EdenClient.Data.Tables;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests des parseurs de tables .ini (scripts/data) : formats synthétiques
/// (pipe/comma/Count), MyCrypt sur vecteur natif, puis vrais fichiers du
/// corpus officiel via le symlink <c>raw_assets/</c> (→ corpus/official).
/// </summary>
[Xunit.Collection("SharedCoreState")]
public class IniTableTests
{
    // ---------- localisation raw_assets ----------

    /// <summary>Chemin absolu de raw_assets/ remonté depuis
    /// bin/Debug/net8.0 → tools/NetTests → eden_godot. null si
    /// le symlink n'existe pas (environnement sans assets).</summary>
    private static string? FindRawAssets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "client_assets");
            if (!Directory.Exists(candidate))
                candidate = Path.Combine(dir.FullName, "raw_assets");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    // ---------- IniTable : 3 formats ----------

    [Fact]
    public void Parse_PipeFormat()
    {
        // Format "|V.x|N|" — comme c_item.ini / c_monster.ini / C_Class.ini
        var data = EdenEncoding.Big5.GetBytes(
            "|V.2|4|\r\n1|戰士|J009|desc|\r\n2|法師|J033|d2|\r\n");
        var t = IniTable.Parse(data, EdenEncoding.Big5);
        Assert.Equal("V.2", t.Version);
        Assert.Equal(4, t.ColumnCount);
        Assert.Equal('|', t.Separator);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal("戰士", t.Rows[0][1]);
        Assert.Equal("J033", t.Rows[1][2]);
        Assert.Same(t.Rows[1], t.FindById(2));
    }

    [Fact]
    public void Parse_PipeFormat_MultiLineField()
    {
        // Champs contenant des CRLF embarqués (C_Class.ini : descriptions) —
        // le record n'est complet qu'à cols+1 cellules.
        var data = Encoding.ASCII.GetBytes(
            "|V.6|65|\r\n1|name|1|J009|line1\r\nline2|trait|td|req|0||1|1|1|100|100|123|69|125|114|100|100|104|33272|33273|33274|34170|34171|34172|||50201|50202|50231|50269|50036|50037|50038|50039|50040|50041|50042|50151|50152|60053||||||52581|52582|52583|52584|52585|52586|1 A01101 A01201 0|\r\n");
        var t = IniTable.Parse(data, EdenEncoding.Big5);
        Assert.Single(t.Rows);
        // la description a absorbé le CRLF
        Assert.Equal("line1\r\nline2", t.Rows[0][4]);
        Assert.Equal(52581, IniTable.GetInt(t.Rows[0], 49));
    }

    [Fact]
    public void Parse_CommaFormat()
    {
        // Format "V.x,N," — comme C_Classes.ini / C_EquipType.ini
        var data = EdenEncoding.Big5.GetBytes(
            "V.2,5,\r\n0,初心者,1,60,,\r\n1,神武門,1,60,,\r\n");
        var t = IniTable.Parse(data, EdenEncoding.Big5);
        Assert.Equal("V.2", t.Version);
        Assert.Equal(5, t.ColumnCount);
        Assert.Equal(',', t.Separator);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal("初心者", t.Rows[0][1]);
        Assert.Equal(60, IniTable.GetInt(t.Rows[1], 3));
    }

    [Fact]
    public void Parse_CountFormat()
    {
        // Format Count des T_* — "N|" + index colonnes + rows.
        var data = Encoding.ASCII.GetBytes(
            "3|\r\n1|3|21|\r\n51973|<Apprentice Magician>\r\nMichaela|2014 Christmas Event|\r\n");
        var t = IniTable.Parse(data, EdenEncoding.Big5);
        Assert.Equal(3, t.ColumnCount);
        Assert.Single(t.Rows);
        // le champ 1 contenait un CRLF — accumulé jusqu'à N+1 cellules
        Assert.Equal("<Apprentice Magician>\r\nMichaela", t.Rows[0][1]);
        Assert.Equal("2014 Christmas Event", t.Rows[0][2]);
    }

    [Fact]
    public void Parse_CountFormat_MultiLineDescription()
    {
        // t_item.ini : description multi-lignes avec codes $N$.
        var data = Encoding.ASCII.GetBytes(
            "3|\r\n1|8|35|\r\n" +
            "1|Dagger Secret Book|$7$\"A thick layer.\"\r\n\r\n" +
            "$7$No one knows.\r\n$12$Note: one time.|\r\n");
        var t = IniTable.Parse(data, EdenEncoding.Big5);
        Assert.Single(t.Rows);
        Assert.Equal("Dagger Secret Book", t.Rows[0][1]);
        Assert.Contains("$12$Note", t.Rows[0][2]);
    }

    // ---------- MyCrypt ----------

    /// <summary>Vecteur construit sur les clés natives
    /// {0x12345678,0x23456789,0x34567890} + password "ghfdsjdl"
    /// (MyCrypt::init_keys @0x08594d70) : après key-scheduling du mot de
    /// passe les clés valent 0x134C834F/0xC0A4931C/0x39068034 et le
    /// ciphertext du plaintext ci-dessous est la constante attendue —
    /// calculée avec l'algorithme PKZIP du natif (indépendamment du port
    /// C#).</summary>
    [Fact]
    public void MyCrypt_KnownVector()
    {
        var plain = Encoding.Latin1.GetBytes("V.2,5,\r\n0,Test,1,60,,\r\n");
        var expectedCt = Convert.FromHexString(
            "DDC7EE71E3E74D57B0A036D772DB989725F6BEAF3A1295");
        Assert.Equal(expectedCt, MyCrypt.CryptEncode(plain, MyCrypt.Password));
        Assert.Equal(plain, MyCrypt.CryptDecode(expectedCt, MyCrypt.Password));
    }

    [Fact]
    public void MyCrypt_DecodeFile_EncodeHeader()
    {
        // Fichier chiffré = "<encode>" + ciphertext ; en clair = inchangé.
        var plain = Encoding.Latin1.GetBytes("V.1,4,\r\n1,abc,1,x,\r\n");
        var ct = MyCrypt.CryptEncode(plain, MyCrypt.Password);
        var file = Encoding.Latin1.GetBytes("<encode>").Concat(ct).ToArray();
        Assert.Equal(plain, MyCrypt.DecodeFile(file));
        Assert.Equal(plain, MyCrypt.DecodeFile(plain)); // pas de header → brut
    }

    // ---------- frontiere de records (bug records avales) ----------

    [Fact]
    public void Parse_PipeFormat_ShortRows_NotSwallowed()
    {
        // c_item.ini natif : 78 colonnes declarees mais des records
        // physiques de ~35 cellules (colonnes vides de fin omises).
        // Regression : la ligne suivante etait absorbee dans le record
        // en cours → ~10 000 items perdus (Montures archive 8 au
        // lieu de 20).
        var data = Encoding.ASCII.GetBytes(
            "|V.11|78|\r\n" +
            "1|Q00134||G00005|UI012|desc|$7$text\r\n" +
            "2|Q00135||G00006|UI013|desc|$7$more\r\n" +
            "3|Q00136||G00007|UI014|d|$7$end|\r\n");
        var t = IniTable.Parse(data, EdenEncoding.Big5);
        Assert.Equal(3, t.Rows.Count);
        Assert.Equal("Q00134", t.Rows[0][1]);
        Assert.Equal("Q00136", t.Rows[2][1]);
    }

    [Fact]
    public void Parse_PipeFormat_MultiLine_StillJoined()
    {
        // Un vrai record multi-lignes : la suite ne commence PAS par
        // « id| » → accumulation conservee.
        var data = Encoding.ASCII.GetBytes(
            "|V.2|4|\r\n" +
            "1|nom|desc ligne1\r\nsuite desc|fin|\r\n" +
            "2|n2|d2|x|\r\n");
        var t = IniTable.Parse(data, EdenEncoding.Big5);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal("desc ligne1\r\nsuite desc", t.Rows[0][2]);
        Assert.Equal("n2", t.Rows[1][1]);
    }

    [Fact]
    public void Parse_CountFormat_ShortRows_NotSwallowed()
    {
        // t_npc.ini natif : lignes a 2 cellules (id|nom) alors que
        // l'index declare 4 colonnes → chaque ligne est un record.
        var data = Encoding.ASCII.GetBytes(
            "4|\r\n1|2|4|5|\r\n" +
            "50002|Chef du Village\r\n" +
            "50003|Marchand\r\n" +
            "50004|<Titre>\nAndrew|func|\r\n");
        var t = IniTable.Parse(data, EdenEncoding.Big5);
        Assert.Equal(3, t.Rows.Count);
        Assert.Equal("Chef du Village", t.Rows[0][1]);
        Assert.Equal("Marchand", t.Rows[1][1]);
    }

    // ---------- vrais fichiers corpus officiel ----------

    [Fact]
    public void RealFile_CClasses_Loads()
    {
        var root = FindRawAssets();
        if (root == null) return; // assets absents — test ignoré
        var races = RaceTable.Load(Path.Combine(root, "data/db/C_Classes.ini"));
        Assert.Equal(7, races.Count);
        Assert.Equal("初心者", races[0].Name);   // Big5 décodé
        Assert.Equal(60, races[1].Param);
    }

    [Fact]
    public void RealFile_SceneS001_Loads()
    {
        var root = FindRawAssets();
        if (root == null) return;
        var scene = SceneTable.Load(Path.Combine(root, "data/scene/s001.ini"));
        Assert.Equal(640, scene.MapWidth);       // S001.fsm,640,640,V7
        Assert.Equal(640, scene.MapHeight);
        Assert.Equal("S001.fsm", scene.FsmFile);
        Assert.NotEmpty(scene.Monsters);         // [reborn_monsters]
        Assert.NotEmpty(scene.Npcs);             // [npc]
        Assert.NotEmpty(scene.Gateways);         // [gateway_area]
        Assert.NotEmpty(scene.EventAreas);
        // Sections client inconnues du serveur conservées
        Assert.True(scene.ExtraSections.ContainsKey("environment_sound"));
        // Spawn natif : MonsterId + code modèle renseignés
        Assert.All(scene.Monsters, m => Assert.True(m.MonsterId > 0));
    }

    [Fact]
    public void RealFile_Tables_Load()
    {
        var root = FindRawAssets();
        if (root == null) return;
        var db = Path.Combine(root, "data/db");

        var classes = ClassTable.Load(Path.Combine(db, "c_class.ini"));
        Assert.True(classes.Count >= 100,
            $"classes={classes.Count}");   // 105 classes jouables avec
            // stats ; les 200 autres ids sont des stubs id|nom|code|desc
            // (8-12 cellules) que le filtre row>=22 écarte — format
            // natif identique US/Reforged/FR.
        Assert.Equal("戰士", classes[1].Name);
        Assert.Equal("J009", classes[1].ClassCode);
        Assert.Equal(33272, classes[1].CertificateIds[0]);
        Assert.Contains(50201, classes[1].ClassSpellIds);
        Assert.Equal(52581, classes[1].TalentIds[0]);
        Assert.Equal(9, classes[1].LevelProgression.Count);  // paliers 1-80

        var texts = ClassTextTable.Load(Path.Combine(db, "t_class.ini"));
        Assert.Equal("Guerrier", texts[1].Name);       // base corpus = FR
        Assert.Contains("Condition requise", texts[1].RequirementText);

        var types = EquipTypeTable.Load(
            Vfs.EdenVfs.ResolveInsensitive(
                root, "data/db/C_EquipType.ini")!);
        Assert.Equal(7, types[7].EquipLoc);          // 單手劍 → loc 7
        Assert.True(types[7].IsEquipable);
        Assert.False(types[-1].IsEquipable);         // dummy −1 conservé

        var monsters = MonsterTable.Load(Path.Combine(db, "c_monster.ini"));
        Assert.True(monsters.Count >= 10900);      // 10923 records EDENFR
        Assert.Equal("M312040", monsters[20001].ModelCode);
        Assert.Equal(80, monsters[20001].Level);
        Assert.True(monsters[20001].IsEliteOrBoss);  // type 3

        var items = ItemTable.Load(Path.Combine(db, "c_item.ini"));
        Assert.True(items.Count >= 18700);         // 18794 records EDENFR
        Assert.Equal("Q00134", items[1].Code);
        Assert.Equal("UI012", items[1].IconCode);
        // Montures : les ids chevauchables EDENFR doivent tous survivre
        // (regression : 12 etaient avales avant le fix de frontiere).
        // 29284/29782 exclus — montures Reforged absentes du corpus FR.
        foreach (var mountId in new[] { 20051, 20052, 20330, 20331,
            20332, 20333, 20334, 20697, 20925, 21252, 21331, 21345,
            23217, 23766, 26495, 29153, 36973, 37019 })
            Assert.True(items.ContainsKey(mountId), $"monture {mountId}");

        var names = TextTable.LoadNames(Path.Combine(db, "t_monster.ini"));
        Assert.Equal("Chevalier de la Rose", names[20001]); // base FR

        var env = MapEnvIni.Load(
            Path.Combine(root, "map/model/S001/S001.ini"));
        Assert.Equal("20100531", env.Version);
        var day = env.GetPhase(MapEnvIni.Phase.Day);
        Assert.Equal(8, day.Hour);
        Assert.Equal((byte)0xE4, day.LitColor.R);    // 15005944 = 0xE4FFF8
        Assert.Equal("N201", day.Sky);
        var night = env.GetPhase(MapEnvIni.Phase.Night);
        Assert.Equal(20, night.Hour);

        var area = MapAreaFile.Load(
            Path.Combine(root, "map/model/S001/S001.area"));
        Assert.Equal(10, area.GridWidth);
        Assert.Equal(960f, area.WorldWidth);         // ×96.0 natif
    }

    /// <summary>Résolution casse-insensible segment par segment —
    /// même sémantique que <c>AssetCatalog.ResolveFile</c> (le corpus
    /// mélange t_class.ini minuscule et T_* majuscule historique).</summary>
    internal static Func<string, string?> CaseInsensitiveResolver(string root) =>
        rel =>
        {
            var abs = Path.Combine(root,
                rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(abs)) return abs;
            var dir = root;
            foreach (var seg in rel.Split('/'))
            {
                var direct = Path.Combine(dir, seg);
                if (File.Exists(direct)) { dir = direct; continue; }
                if (!Directory.Exists(dir)) return null;
                var hit = Directory.GetFileSystemEntries(dir)
                    .FirstOrDefault(e => string.Equals(
                        Path.GetFileName(e), seg,
                        StringComparison.OrdinalIgnoreCase));
                if (hit == null) return null;
                dir = hit;
            }
            return File.Exists(dir) ? dir : null;
        };

    [Fact]
    public void RealFile_TextResolver_En()
    {
        var root = FindRawAssets();
        if (root == null) return;
        TextResolver.ResolveFile = CaseInsensitiveResolver(root);
        TextResolver.SetLanguage("en");

        // Corpus EDENFR pur : pas d'overlay lang/en — le mode « en »
        // replie sur la base data/db native FR (comportement attendu
        // tant que l'anglais n'est pas réintroduit proprement).
        Assert.Equal("Champignoïde rouge", TextResolver.MonsterName(56001));
        Assert.Equal("Chevalier de la Rose", TextResolver.MonsterName(20001));
        Assert.Null(TextResolver.MonsterName(-1));

        Assert.Equal("Andrew", TextResolver.NpcName(50002));
        Assert.Equal("Chef du Village", TextResolver.NpcTitle(50002));
        Assert.Equal("Carrack", TextResolver.NpcName(50174));

        Assert.Equal("Guerrier", TextResolver.ClassName(1));
        Assert.Equal("Guerrier", TextResolver.ClassName(401)); // race-composite

        // t_textindex (NCID 316) : %s1 = paramètre du paquet.
        Assert.Equal("Vous avez appris la compétence \"Ambush\".",
            TextResolver.TextIndex(1524, "Ambush"));
        Assert.Equal("Passer à la Classe Thief",
            TextResolver.TextIndex(2013, "Thief"));
        Assert.Equal("Vous avez déjà appris la compétence Ambush.",
            TextResolver.TextIndex(2014, "Ambush"));
        Assert.Null(TextResolver.TextIndex(-5));
        Assert.Equal("Monts Calcaires", TextResolver.NodeName(2));
        Assert.Equal("Aven", TextResolver.NodeName(1));

        Assert.Equal("Nv. 1 Votre première Quête I",
            TextResolver.MissionTitle(1009));
        Assert.Equal("Livre Secret : Dague", TextResolver.ItemName(1));
        Assert.Equal("Chef", TextResolver.SpellName(50001));

        // Clean : codes $N$ retirés.
        Assert.Equal("\"note\"", TextResolver.Clean("  $7$\"note\"  "));
    }

    [Fact]
    public void RealFile_TextResolver_Fr()
    {
        var root = FindRawAssets();
        if (root == null) return;
        TextResolver.ResolveFile = CaseInsensitiveResolver(root);
        TextResolver.SetLanguage("fr");

        // Base corpus = tables FR Aeria (le repli EN couvre les trous).
        Assert.Equal("Champigno", TextResolver.MonsterName(56001)![..9]);
        Assert.Equal("Chevalier de la Rose", TextResolver.MonsterName(20001));
        Assert.Equal("Andrew", TextResolver.NpcName(50002));   // nom identique
        Assert.Equal("Chef du Village", TextResolver.NpcTitle(50002));
        Assert.Equal("Carrack", TextResolver.NpcName(50174));  // FR ≠ EN (« Gina »)
        Assert.Equal("Guerrier", TextResolver.ClassName(1));
        Assert.Equal("Monts Calcaires", TextResolver.NodeName(2));
        Assert.StartsWith("Nv. 1 Votre premi",
            TextResolver.MissionTitle(1009));

        // Retour EN pour ne pas polluer les autres tests.
        TextResolver.SetLanguage("en");
    }
}
