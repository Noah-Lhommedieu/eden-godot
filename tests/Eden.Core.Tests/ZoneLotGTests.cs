using EdenClient.Net;
using EdenClient.Protocol;
using EdenClient.World;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Couverture ajoutée au lot G3 : messages shop/banque/transport
/// (ZoneShopMessages), cas limites vitaux/équipement, encodes C→S
/// du burst zone non encore testés, cas limites <see cref="FsmFile"/>
/// (buffers synthétiques — pas besoin de raw_assets) et smoke headless.
/// </summary>
[Xunit.Collection("SharedCoreState")]
public class ZoneLotGTests
{
    private static byte[] Hex(string h) =>
        Convert.FromHexString(h.Replace(" ", ""));

    private static byte[] RoundTrip(NetMessage m)
    {
        var w = new BinWriter();
        m.Serialize(w);
        var bytes = w.ToArray();
        return bytes;
    }

    private static void RoundTripSame(NetMessage m)
    {
        var bytes = RoundTrip(m);
        var clone = (NetMessage)Activator.CreateInstance(m.GetType())!;
        clone.Deserialize(new BinReader(bytes));
        Assert.Equal(bytes, RoundTrip(clone));
    }

    // ================= 304 EnterShop / 393 Transport / 357 Storage ====

    [Fact]
    public void EnterShop_Wire()
    {
        var m = new ZoneEnterShop();
        m.Deserialize(new BinReader(Hex("0d93c988 0500 01 0200")));
        Assert.Equal(unchecked((int)0x88c9930d), m.NpcUid);
        Assert.Equal(5, m.StoreId);
        Assert.Equal(1, m.Flag);
        Assert.Equal(2, m.Field4);
        // Corps court natif (i32+s16) toléré.
        var short_ = new ZoneEnterShop();
        short_.Deserialize(new BinReader(Hex("0d93c988 0500")));
        Assert.Equal(5, short_.StoreId);
        Assert.Equal(0, short_.Flag);
        RoundTripSame(new ZoneEnterShop
        { NpcUid = 7, StoreId = 42, Flag = 1, Field4 = -3 });
    }

    [Fact]
    public void EnterTransport_Wire()
    {
        var m = new ZoneEnterTransport();
        m.Deserialize(new BinReader(Hex(
            "0300 0a00 1400 1e00 0d93c988")));
        Assert.Equal(new short[] { 10, 20, 30 }, m.Routes);
        Assert.Equal(unchecked((int)0x88c9930d), m.NpcUid);
        RoundTripSame(new ZoneEnterTransport { NpcUid = 5 });
    }

    [Fact]
    public void EnterStorage_AskVendor_Wire()
    {
        var st = new ZoneEnterStorage();
        st.Deserialize(new BinReader(Hex("0d93c988")));
        Assert.Equal(unchecked((int)0x88c9930d), st.NpcUid);
        Assert.Equal(Hex("0d93c988"), RoundTrip(st));
        RoundTripSame(new ZoneAskVendor { Value = 0x1234 });
        RoundTripSame(new ZoneAskVendorContainer { Value = -1 });
    }

    [Fact]
    public void BuyUp_CorpsVide()
    {
        var m = new ZoneBuyUp();
        m.Deserialize(new BinReader([]));
        Assert.Equal([], RoundTrip(m));
    }

    [Fact]
    public void StorageDepositWithdraw_Wire()
    {
        // [i32][u16][s16][s16] — 12 octets.
        var d = new ZoneStorageDeposit
        { Field1 = 0x11223344, Field2 = 5, Field3 = 7, Field4 = 1 };
        var bytes = RoundTrip(d);
        Assert.Equal(10, bytes.Length);   // i32+u16+s16+s16
        var w = new ZoneStorageWithdraw();
        w.Deserialize(new BinReader(bytes));
        Assert.Equal(0x11223344, w.Field1);
        Assert.Equal(5, w.Field2);
        Assert.Equal(7, w.Field3);
        Assert.Equal(1, w.Field4);
        RoundTripSame(new ZoneReviveRequest { ReviveType = 3 });
    }

    // ---------- états agrégés shop/transport/banque ----------

    [Fact]
    public void ShopState_OpenClose()
    {
        var fired = 0;
        void On() => fired++;
        ShopState.Changed += On;
        try
        {
            var m = new ZoneEnterShop();
            m.Deserialize(new BinReader(Hex("0d93c988 0500 00 0000")));
            ShopState.OnEnterShop(m);
            Assert.True(ShopState.IsOpen);
            Assert.Equal(5, ShopState.StoreId);
            Assert.Equal(unchecked((int)0x88c9930d), ShopState.NpcUid);
            Assert.True(fired >= 1);
            // Store inconnu des tables → liste vide mais ouvert.
            ShopState.Close();
            Assert.False(ShopState.IsOpen);
            Assert.Empty(ShopState.Entries);
        }
        finally { ShopState.Changed -= On; ShopState.Close(); }
    }

    [Fact]
    public void TransportState_OpenClose()
    {
        var m = new ZoneEnterTransport();
        m.Deserialize(new BinReader(Hex("0200 0a00 1400 0d93c988")));
        TransportState.OnEnterTransport(m);
        try
        {
            Assert.True(TransportState.IsOpen);
            Assert.Equal(2, TransportState.Routes.Count);
            Assert.Equal(10, TransportState.Routes[0].RouteId);
            Assert.False(string.IsNullOrEmpty(
                TransportState.Routes[0].Destination));
            TransportState.Close();
            Assert.False(TransportState.IsOpen);
            Assert.Empty(TransportState.Routes);
        }
        finally { TransportState.Close(); }
    }

    [Fact]
    public void StorageState_EnterThenData()
    {
        var st = new ZoneEnterStorage();
        st.Deserialize(new BinReader(Hex("0d93c988")));
        StorageState.OnEnterStorage(st);
        try
        {
            Assert.True(StorageState.IsOpen);
            var data = new ZoneClientGetStorageData();
            data.Deserialize(new BinReader(Hex("ffff01200000040000000000")));
            StorageState.OnStorageData(data);
            Assert.Single(StorageState.Containers);
            Assert.Equal(0, StorageState.TotalItems);
            StorageState.Close();
            Assert.False(StorageState.IsOpen);
        }
        finally { StorageState.Close(); }
    }

    // ================= équipement : cas limites =====================

    [Fact]
    public void UpdateEquipmentView_Mask0()
    {
        // charId + mask 0 → aucun item, queue tolérée.
        var w = new BinWriter();
        w.WriteInt(0x11223344);
        w.WriteInt(0);
        w.WriteInt(0);          // nHidden
        w.WriteShort(0);        // queue s16
        w.WriteByte(0);         // queue s8
        var m = new ZoneUpdateEquipmentView();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Equal(0x11223344, m.CharId);
        Assert.Equal(0u, m.LocMask);
        Assert.Empty(m.Items);
        Assert.Empty(m.Hidden);
    }

    [Fact]
    public void UpdateEquipmentView_Tronque_AvantHidden()
    {
        // Un item loc0 puis corps coupé : on garde l'item, Hidden vide.
        var w = new BinWriter();
        w.WriteInt(0x11223344);
        w.WriteInt(1 << 0);
        w.WriteShort(31939); w.WriteByte(0x48); w.WriteShort(0);
        w.WriteBytes(stackalloc byte[3]); w.WriteBytes(stackalloc byte[16]);
        var m = new ZoneUpdateEquipmentView();
        m.Deserialize(new BinReader(w.ToArray()));
        Assert.Single(m.Items);
        Assert.Equal(31939, m.Items[0].ItemId);
        Assert.Empty(m.Hidden);
    }

    [Fact]
    public void EquipAndSpellParser_VideEtTronque()
    {
        var (e0, s0) = EquipAndSpellParser.Parse([]);
        Assert.Empty(e0);
        Assert.Empty(s0);
        // 1 conteneur mais corps coupé dans l'en-tête conteneur.
        var (e1, s1) = EquipAndSpellParser.Parse(Hex("0105"));
        Assert.Empty(e1);
        Assert.Empty(s1);
        // 0 conteneur + nSpells tronqué.
        var (e2, s2) = EquipAndSpellParser.Parse(Hex("00 02"));
        Assert.Empty(e2);
        Assert.Empty(s2);
    }

    [Fact]
    public void PlayerSkills_And_Equipment_EnterNode()
    {
        PlayerSkills.ApplyEnterNode(new List<KnownSpell>
        {
            new(0xc351, 1), new(0xe6dd, 2),
        });
        Assert.Equal(2, PlayerSkills.Spells.Count);
        Assert.Equal(0xe6dd, PlayerSkills.Spells[1].SpellMaster);
        Assert.Equal(2, PlayerSkills.Spells[1].Level);
        PlayerSkills.ApplyEnterNode(new List<KnownSpell>());
        Assert.Empty(PlayerSkills.Spells);

        PlayerEquipment.ApplyEnterNode(new List<EquippedItem>
        {
            new(2, 0, 31939, 0x48, 0, 0),
            new(3, 1, 777, 0, 0, 0),   // conteneur 1 : pas « porté »
        });
        Assert.Single(PlayerEquipment.Worn);
        Assert.Equal(31939, PlayerEquipment.Worn[2]);
        // 282 self : remplace l'état porté.
        var m = new ZoneUpdateEquipmentView
        { CharId = unchecked((int)0x0bebc204) };
        PlayerEquipment.ApplyUpdateEquipment(m, 0x0bebc204);
        Assert.Empty(PlayerEquipment.Worn);
        // 282 d'un autre joueur ignoré.
        PlayerEquipment.ApplyUpdateEquipment(
            new ZoneUpdateEquipmentView { CharId = 1 }, 0x0bebc204);
        Assert.Empty(PlayerEquipment.Worn);
    }

    // ================= vitaux : mort/respawn/xp =====================

    [Fact]
    public void PlayerVitals_DieRevive_SelfOnly()
    {
        var died = 0u; var revived = 0;
        void OnDied(uint killer) => died = killer;
        void OnRev() => revived++;
        PlayerVitals.Died += OnDied;
        PlayerVitals.Revived += OnRev;
        try
        {
            // Mort d'une AUTRE entité : ignorée.
            PlayerVitals.ApplyDie(new ZoneDie
            { Handle = 0xfffff4fa, Other = 1 }, selfCharId: 42);
            Assert.False(PlayerVitals.IsDead);
            Assert.Equal(0u, died);
            // Mort du self.
            PlayerVitals.ApplyDie(new ZoneDie
            { Handle = 42, Other = 0xffff0001 }, 42);
            Assert.True(PlayerVitals.IsDead);
            Assert.Equal(0xffff0001u, died);
            // Revive d'un autre : ignoré.
            PlayerVitals.ApplyRevive(new ZoneRevive { Handle = 7 }, 42);
            Assert.True(PlayerVitals.IsDead);
            // Revive du self.
            PlayerVitals.ApplyRevive(new ZoneRevive { Handle = 42 }, 42);
            Assert.False(PlayerVitals.IsDead);
            Assert.Equal(1, revived);
        }
        finally
        {
            PlayerVitals.Died -= OnDied;
            PlayerVitals.Revived -= OnRev;
        }
    }

    [Fact]
    public void PlayerVitals_Exp_EtVitaux_NeNotifientQueSurChangement()
    {
        var vit = 0; var exp = 0;
        void OnVit() => vit++;
        void OnExp() => exp++;
        PlayerVitals.VitalsChanged += OnVit;
        PlayerVitals.ExpChanged += OnExp;
        try
        {
            PlayerVitals.SetVitals(50, 60, 40, 50);
            var v0 = vit;
            PlayerVitals.SetVitals(50, 60, 40, 50);   // inchangé
            Assert.Equal(v0, vit);
            PlayerVitals.SetVitals(51, 60, 40, 50);
            Assert.Equal(v0 + 1, vit);
            PlayerVitals.ApplyExp(1234);
            Assert.Equal(1234, PlayerVitals.Exp);
            Assert.Equal(1, exp);
            PlayerVitals.ApplyExp(1234);              // inchangé
            Assert.Equal(1, exp);
        }
        finally
        {
            PlayerVitals.VitalsChanged -= OnVit;
            PlayerVitals.ExpChanged -= OnExp;
        }
    }

    // ================= encodes C→S du burst zone =====================

    [Fact]
    public void ZoneBursts_Encodes()
    {
        // zsniff_cs_entry_success : tickets 22B bruts, moves, actions.
        Assert.Equal(22, RoundTrip(new ZoneUseTicket
        { Ticket = new byte[22] }).Length);

        // MoveSelf : [s16 n][n×2f][5f][s16 type] — zsniff 0100...0700.
        var mv = new ZoneMoveSelf();
        mv.Segments.Add((100f, 0.5f));
        for (var i = 0; i < 4; i++) mv.Tail[i] = i + 1;
        mv.Tail[4] = 0.5f;
        var mb = RoundTrip(mv);
        Assert.Equal(2 + 8 + 20 + 2, mb.Length);
        Assert.Equal(0x0001, BitConverter.ToUInt16(mb, 0));
        Assert.Equal(7, BitConverter.ToInt16(mb, mb.Length - 2));

        Assert.Equal(Hex("08 ffffffff 3b030000 00000000"),
            RoundTrip(new ZonePlayerAction
            { Action = 8, Param1 = -1, Param2 = 827, Param3 = 0 }));
        RoundTripSame(new ZoneMissionListOp
        { Field1 = 2, Field2 = 1009, Field3 = 0xffff });
        RoundTripSame(new ZoneSearchOpt { Field1 = 0, Field2 = 0 });
        RoundTripSame(new ZoneTurnSelf { Value = 0.904f });
        RoundTripSame(new ZoneTargetUpdate { Value = -2001745528 });
        RoundTripSame(new ZoneUpdateBotData { Field1 = 0, Field2 = 0 });
        var tr = new ZoneSetMissionTrace();
        for (var i = 0; i < 10; i++) tr.MissionIds.Add(i == 0 ? 1009 : 0);
        Assert.Equal(2 + 40, RoundTrip(tr).Length);
        RoundTripSame(new ZoneNewPlayerGuide { Field1 = 0, Field2 = 3 });
        RoundTripSame(new ZoneDailyAwardOp { Field1 = 0, Field2 = 2 });
        RoundTripSame(new ZoneQueryRecommendActivities
        { Field1 = 0, Field2 = 0 });

        // Requêtes vides : corps = 0 octet, opcode correct.
        Assert.Equal(63, ZoneEmptyRequest.LeaveShop.Opcode);
        Assert.Equal(25, ZoneEmptyRequest.EnterNodeInventory.Opcode);
        Assert.Equal([], RoundTrip(ZoneEmptyRequest.Echo));
        Assert.Equal(4, new ZoneMigrationDone().Opcode);
    }

    // ================= S→C divers =====================

    [Fact]
    public void ClientHello_FlagModify_NodeData_MailReceive()
    {
        var hello = new ZoneClientHello();
        hello.Deserialize(new BinReader(Hex("52782043")));
        Assert.Equal(160.47f, hello.Value, 1e-3f);

        var fm = new ZoneFlagModify();
        fm.Deserialize(new BinReader(Hex("04c2eb0b 00 00010100")));
        Assert.Equal(0x0bebc204, fm.Field1);
        Assert.Equal(5, fm.Rest.Length);   // i32 lu, 5 o de queue

        var nd = new ZoneEnterNodeNodeData();
        nd.Deserialize(new BinReader(Hex("0200 01 00004842 0000c842 0000f642")));
        Assert.Equal(2, nd.Field1);
        Assert.Equal(1, nd.Field2);
        Assert.Equal(50f, nd.X);
        Assert.Equal(100f, nd.Y);
        Assert.Equal(123f, nd.Z);

        var mail = new ZoneMailReceive();
        mail.Deserialize(new BinReader(Hex("0000 0000")));
        Assert.Equal(0, mail.Field1);
    }

    // ================= FsmFile : cas limites synthétiques ============

    /// <summary>En-tête FSM minimal valide (magic + version).</summary>
    private static byte[] FsmHeader()
    {
        var b = new byte[FsmFile.HeaderSize];
        var magic = System.Text.Encoding.ASCII.GetBytes("FSM V20990101");
        magic.CopyTo(b, 0);
        // formatRevision @0x54
        BitConverter.GetBytes(3u).CopyTo(b, 0x54);
        return b;
    }

    private static void W32(byte[] b, int off, uint v) =>
        BitConverter.GetBytes(v).CopyTo(b, off);

    [Fact]
    public void Fsm_Parse_RefuseMagie()
    {
        Assert.Throws<InvalidDataException>(() =>
            FsmFile.Parse(new byte[8]));
        var b = FsmHeader();
        b[0] = (byte)'X';
        Assert.Throws<InvalidDataException>(() => FsmFile.Parse(b));
    }

    [Fact]
    public void Fsm_Parse_EnTeteSeul_MapVide()
    {
        var f = FsmFile.Parse(FsmHeader());
        Assert.Equal("V20990101", f.Version);
        Assert.Equal(3u, f.FormatRevision);
        Assert.Empty(f.Tiles);
        Assert.Empty(f.SceneObjects);
        Assert.Empty(f.GrassInstances);
        Assert.Null(f.GetTerrainTile(0, 0));
        Assert.Equal(0, f.TilesPerRow);
    }

    [Fact]
    public void Fsm_Parse_ChunkInconnu_Skippe()
    {
        // Chunk « XXXX » de 8+4 o : ignoré, parse continue.
        var b = new byte[FsmFile.HeaderSize + 12];
        FsmHeader().CopyTo(b, 0);
        W32(b, FsmFile.HeaderSize, 0x58585858);
        W32(b, FsmFile.HeaderSize + 4, 12);
        var f = FsmFile.Parse(b);
        Assert.Empty(f.Tiles);
    }

    [Fact]
    public void Fsm_Parse_ChunkTropGrand_Refuse()
    {
        var b = new byte[FsmFile.HeaderSize + 8];
        FsmHeader().CopyTo(b, 0);
        W32(b, FsmFile.HeaderSize, 0x204c5247);   // "GRL "
        W32(b, FsmFile.HeaderSize + 4, 0xFFFF);   // size > fichier
        Assert.Throws<InvalidDataException>(() => FsmFile.Parse(b));
    }

    [Fact]
    public void Fsm_Parse_ChunkTailleTropPetite_Refuse()
    {
        var b = new byte[FsmFile.HeaderSize + 8];
        FsmHeader().CopyTo(b, 0);
        W32(b, FsmFile.HeaderSize, 0x204c5247);
        W32(b, FsmFile.HeaderSize + 4, 4);        // < en-tête chunk
        Assert.Throws<InvalidDataException>(() => FsmFile.Parse(b));
    }

    [Fact]
    public void Fsm_Parse_CountGrlIncoherent_Refuse()
    {
        // GRL count=2 mais taille pour 0 record → incohérence refusée.
        var size = FsmFile.ChunkHeaderSize + 4 + 0;
        var b = new byte[FsmFile.HeaderSize + size];
        FsmHeader().CopyTo(b, 0);
        W32(b, FsmFile.HeaderSize, 0x204c5247);
        W32(b, FsmFile.HeaderSize + 4, (uint)size);
        W32(b, FsmFile.HeaderSize + 8, 2);        // count=2 ≠ size
        Assert.Throws<InvalidDataException>(() => FsmFile.Parse(b));
    }

    // ================= settings : cas limites =====================

    [Fact]
    public void Settings_ValeursCorrompues_RetombentSurDefauts()
    {
        var s = EdenClient.Settings.ClientSettings.Parse(
            "[DISPLAY]\nresolution=abc\nmax_fps=-5\nwindow_mode=weird\n" +
            "[UI]\nui_scale=nan\n[AUDIO]\nmaster=zz\npreset=nonsense\n");
        Assert.True(s.ResolutionWidth > 0);
        Assert.True(s.MaxFps >= 0);
        Assert.Equal(EdenClient.Settings.EdenWindowMode.Windowed,
            s.WindowMode);
    }

    [Fact]
    public void Settings_Resolution_HorsListe_RetombeDefaut()
    {
        // Résolution libre « WxH » acceptée (pas de whitelist), mais
        // les clés hors de leur section [DISPLAY] sont ignorées
        // (retombe sur le défaut 1280×720).
        var s = EdenClient.Settings.ClientSettings.Parse(
            "[DISPLAY]\nresolution=1366x768");
        Assert.Equal(1366, s.ResolutionWidth);
        Assert.Equal(768, s.ResolutionHeight);
        var hors = EdenClient.Settings.ClientSettings.Parse(
            "resolution=1920x1080");
        Assert.Equal(1280, hors.ResolutionWidth);
    }

    // ================= smoke headless game.tscn =====================

    /// <summary>Lance <c>godot --headless res://scenes/game.tscn</c>
    /// quelques secondes : la scène de jeu se charge sans erreur script.
    /// Skip si le binaire godot ou raw_assets est absent.</summary>
    [Fact]
    public async Task Smoke_GameScene_Headless_ChargeSansErreur()
    {
        var godot = Environment.GetEnvironmentVariable("EDEN_GODOT_BIN")
            ?? "godot";
        string? proj = null;
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var p = Path.Combine(dir, "godot_project");
            if (File.Exists(Path.Combine(p, "project.godot")))
            {
                proj = p; break;
            }
            dir = Path.GetDirectoryName(dir) ?? dir;
        }
        if (proj == null) return;                       // projet absent
        var rawAssets = Path.Combine(
            Directory.GetParent(proj)!.FullName, "raw_assets");
        if (!Directory.Exists(Path.Combine(rawAssets, "map")))
            return;                                     // assets absents

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = godot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = proj,
        };
        psi.ArgumentList.Add("--headless");
        psi.ArgumentList.Add("--path");
        psi.ArgumentList.Add(".");
        psi.ArgumentList.Add("res://scenes/game.tscn");
        psi.ArgumentList.Add("--quit-after");
        psi.ArgumentList.Add("15");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--map=S002");
        psi.ArgumentList.Add("--solo");
        psi.ArgumentList.Add("--nohud");

        using var proc = new System.Diagnostics.Process();
        string outLog;
        try
        {
            proc.StartInfo = psi;
            proc.Start();
            // Lecture async avec garde-fou (le process quitte de
            // lui-même après 15 s ; 60 s max).
            var readOut = proc.StandardOutput.ReadToEndAsync();
            var readErr = proc.StandardError.ReadToEndAsync();
            try
            {
                await proc.WaitForExitAsync()
                    .WaitAsync(TimeSpan.FromSeconds(60));
            }
            catch (TimeoutException)
            {
                proc.Kill(entireProcessTree: true);
                Assert.Fail("godot --headless game.tscn n'a pas " +
                            "quitté en 60 s");
            }
            outLog = await readOut + await readErr;
        }
        catch (Exception)
        {
            return;                                     // godot absent
        }
        Assert.DoesNotContain("SCRIPT ERROR", outLog);
        Assert.DoesNotContain("Échec LoadMap", outLog);
        Assert.Contains("[GameScene] map=S002", outLog);
    }
}
