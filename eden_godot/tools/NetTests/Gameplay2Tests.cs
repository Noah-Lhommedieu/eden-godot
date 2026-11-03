using EdenClient.Combat;
using EdenClient.Net;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Tests du lot « gameplay-2 » : routing 272/305 (hit/mort),
/// messages WhoIs/DisplayChar (apparence remote), parseurs
/// motion.ini et .ifl, plan herbe GRL/GRS (groupage + plafond).
/// </summary>
public class Gameplay2Tests
{
    private static string? FindRawAssets()
    {
        var env = Environment.GetEnvironmentVariable("EDEN_RAW_ASSETS");
        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "raw_assets");
            if (Directory.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        return null;
    }

    // ------------------------------------------------------------------
    // Messages wire : 305 ClientDie, 2 WhoIs, 263 DisplayChar (tête).

    [Fact]
    public void ClientDie_WireLayout()
    {
        // [u16 ncid=305][u32 victime][u32 tueur][u8] — 11 octets.
        var m = new ZoneClientDie
        {
            Victim = 0xFFFFA123, Killer = 200000002, Flags = 0,
        };
        var w = new BinWriter();
        w.WriteUShort((ushort)m.Opcode);
        m.Serialize(w);
        Assert.Equal(new byte[]
        {
            0x31, 0x01, 0x23, 0xA1, 0xFF, 0xFF,
            0x02, 0xC2, 0xEB, 0x0B, 0x00,
        }, w.ToArray());
        var back = new ZoneClientDie();
        back.Deserialize(new BinReader(w.ToArray()[2..]));
        Assert.Equal(0xFFFFA123u, back.Victim);
        Assert.Equal(200000002u, back.Killer);
    }

    [Fact]
    public void WhoIs_WireLayout()
    {
        // [i32 charId][u8 flag] — flag=1 demande la fiche 263.
        var m = new ZoneWhoIs { CharId = 200000002, Flag = 1 };
        var w = new BinWriter();
        w.WriteUShort(2);
        m.Serialize(w);
        Assert.Equal(new byte[]
        {
            0x02, 0x00, 0x02, 0xC2, 0xEB, 0x0B, 0x01,
        }, w.ToArray());
    }

    [Fact]
    public void DisplayChar_HeadParses()
    {
        // Corps synthétique conforme à ClientDisplayCharCodec :
        // head fixe jusqu'à l'apparence puis queue libre (mask…).
        var w = new BinWriter();
        w.WriteInt(200000007);            // charId
        w.WriteString("Alice");           // nom
        for (var i = 0; i < 4; i++) w.WriteFloat(1.5f);
        w.WriteByte(3); w.WriteByte(3);   // atkSpd R/L
        w.WriteBytes(new byte[4]);        // lifeFlags
        w.WriteShort(42);                 // level
        w.WriteByte(7);                   // classId
        w.WriteShort(9);                  // classLevel
        var app = new byte[32];
        app[28] = 0x20;                   // body_code nibble = 2 → « m »
        w.WriteBytes(app);
        w.WriteByte(0x0F);                // mask (queue non lue)
        w.WriteBytes(new byte[40]);       // queue variable
        var m = new ZoneClientDisplayChar();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(200000007u, m.CharId);
        Assert.Equal("Alice", m.CharName);
        Assert.Equal((ushort)42, m.Level);
        Assert.Equal((byte)7, m.ClassId);
        Assert.Equal(32, m.Appearance.Length);
        Assert.Equal(0x20, m.Appearance[28]);
        Assert.Equal(41, m.Rest.Length);  // mask + queue
    }

    // ------------------------------------------------------------------
    // MotionTable — (modèle,kfId)→anim (motion.ini, spec serveur).

    [Fact]
    public void MotionTable_ParsesBothSections()
    {
        var t = MotionTable.Parse(
            "[INFO]\nVersion=103\n" +
            "[KEY_INFO]\n" +
            "DB1=m025,1021,4,attack01\n" +
            "DB2=m025,1011,3,hurt01\n" +
            "[KEY_Table]\n" +
            "DB0=g,1421,0.5,hit,NULL,,attack12\n" +
            "DB1=g,1421,1.2,end,NULL,,attack12\n");
        Assert.Equal("attack01", t.Lookup("m025", 1021));
        Assert.Equal("hurt01", t.Lookup("m025", 1011));
        Assert.Equal("attack12", t.Lookup("g", 1421));
        Assert.Equal("attack01", t.Lookup("M025", 1021)); // casse
        Assert.Null(t.Lookup("m999", 1021));
        Assert.Null(t.Lookup("m025", -1));
    }

    [Fact]
    public void MotionTable_RealFile()
    {
        var root = FindRawAssets();
        if (root == null) return;
        var p = Path.Combine(root, "data", "db", "motion.ini");
        if (!File.Exists(p)) return;
        var t = MotionTable.LoadFile(p);
        Assert.True(t.Count > 1000);
        // Valeurs natives connues (serveur MotionParser) : m025 kfId
        // 1021 = attack01, kfId joueur 1421 = attack12.
        Assert.Equal("attack01", t.Lookup("m025", 1021));
        Assert.Equal("attack12", t.Lookup("g", 1421));
    }

    // ------------------------------------------------------------------
    // CombatFx — mapping hitType/kfId → anims (règles serveur).

    [Fact]
    public void CombatFx_AnimChoices()
    {
        var t = MotionTable.Parse("DB0=m025,1021,4,attack01\n" +
                                  "DB1=m025,1022,4,attack02\n");
        Assert.Equal("attack02", CombatFx.AttackAnim(t, "m025", 1022));
        Assert.Equal("attack01", CombatFx.AttackAnim(t, "m999", 5));
        Assert.Null(CombatFx.AttackAnim(t, "m025", -1)); // twin/fatal
        Assert.Equal("hurt01", CombatFx.HitAnim(1));
        Assert.Equal("hurt01", CombatFx.HitAnim(3));
        Assert.Equal("death01", CombatFx.HitAnim(5));
        Assert.Null(CombatFx.HitAnim(8));
        Assert.True(CombatFx.IsFatal(5));
        Assert.True(CombatFx.IsMiss(8));
    }

    // ------------------------------------------------------------------
    // IflFile — liste de frames (CRLF, durées, chemins Windows).

    [Fact]
    public void Ifl_Parses()
    {
        var f = IflFile.Parse(
            "caust00.dds\r\ncaust01.dds\r\nX666.dds 3\r\n" +
            "C:\\tmp\\WaterairAni-11.dds\r\n\r\n");
        Assert.Equal(new[]
        {
            "caust00.dds", "caust01.dds", "X666.dds", "X666.dds",
            "X666.dds", "WaterairAni-11.dds",
        }, f.Frames);
    }

    [Fact]
    public void Ifl_RealWaterAni()
    {
        var root = FindRawAssets();
        if (root == null) return;
        var p = Path.Combine(root, "map", "texture", "WaterAni_normal.ifl");
        if (!File.Exists(p)) return;
        var f = IflFile.Load(p);
        Assert.True(f.Frames.Count >= 30);
        Assert.Equal("caust00.dds", f.Frames[0]);
    }

    // ------------------------------------------------------------------
    // GrassLayout — groupage type×cellule + plafond uniforme.

    [Fact]
    public void GrassLayout_GroupsAndCaps()
    {
        var root = FindRawAssets();
        if (root == null) return;
        var p = Path.Combine(root, "map", "model", "S009", "S009.fsm");
        if (!File.Exists(p)) return;
        var fsm = FsmFile.Load(p);
        var cells = GrassLayout.Plan(fsm, fsm.TileWorldSize, 5000);
        Assert.NotEmpty(cells);
        var total = cells.Sum(c => c.Instances.Length);
        Assert.True(total <= 5000 && total > 0);
        Assert.Equal("UE704M", GrassLayout.ModelStem(fsm, 0));
        Assert.Equal("UE704MN", GrassLayout.ModelStem(fsm, 1));
        // Toutes les instances d'une cellule partagent typeId+cellule.
        Assert.All(cells, c => Assert.All(c.Instances, g =>
        {
            Assert.Equal(c.TypeId, g.TypeId);
            Assert.Equal(c.CellX,
                (int)Math.Floor(g.Position.X / fsm.TileWorldSize));
        }));
    }

    [Fact]
    public void GrassLayout_CapThins()
    {
        var root = FindRawAssets();
        if (root == null) return;
        var p = Path.Combine(root, "map", "model", "S002", "S002.fsm");
        if (!File.Exists(p)) return;
        var fsm = FsmFile.Load(p);
        var cells = GrassLayout.Plan(fsm, fsm.TileWorldSize, 5000);
        var total = cells.Sum(c => c.Instances.Length);
        // S002 a ~21 597 instances natives → plafond appliqué.
        Assert.InRange(total, 1, 5000);
        Assert.True(total < fsm.GrassInstances.Count);
    }
}
