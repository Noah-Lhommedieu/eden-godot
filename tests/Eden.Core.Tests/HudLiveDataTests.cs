using EdenClient.Protocol;
using EdenClient.Ui;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>
/// Modèle live du HUD (<see cref="LiveHudData"/>) : identité/vitaux
/// reconstruits depuis <see cref="PlayerVitals"/>, file de chat
/// bornée, cycle bandeau, suivi de quêtes (348/391), snapshot buffs
/// self — chaque mutation effective incrémente <see cref="IHudData.Version"/>.
/// Les tables texte (t_*.ini) sont absentes du contexte test : les
/// replis « Quest #id » / « enchant #id » sont les valeurs attendues.
/// </summary>
[Xunit.Collection("SharedCoreState")]
public class HudLiveDataTests : IDisposable
{
    public HudLiveDataTests()
    {
        PlayerVitals.SetVitals(0, 0, 0, 0);
        PlayerVitals.ApplyExp(0);
        EntityBuffs.ClearAll();
    }

    public void Dispose()
    {
        EntityBuffs.ClearAll();
        PlayerVitals.SetVitals(0, 0, 0, 0);
    }

    [Fact]
    public void Self_And_Vitals_Bump_Version_On_Effective_Change()
    {
        var d = new LiveHudData();
        var v0 = d.Version;
        d.SetSelf(42, "Noah", 10, 5);
        Assert.True(d.Version > v0);
        Assert.Equal("Noah", d.Vitals!.Name);
        Assert.Equal(10, d.Vitals.Level);
        Assert.Equal(5, d.Vitals.JobLevel);
        Assert.Equal(42u, d.SelfCharId);

        // Idempotent : même identité → pas de bump.
        var v1 = d.Version;
        d.SetSelf(42, "Noah", 10, 5);
        Assert.Equal(v1, d.Version);

        // Vitaux via PlayerVitals (muté par les 280 côté bridge).
        PlayerVitals.SetVitals(120, 200, 30, 50);
        d.RefreshVitals();
        Assert.Equal(120, d.Vitals!.Hp);
        Assert.Equal(200, d.Vitals.HpMax);
        Assert.Equal(30, d.Vitals.Mp);
        Assert.Equal(50, d.Vitals.MpMax);
        Assert.True(d.Version > v1);

        // Refresh sans changement → stable.
        var v2 = d.Version;
        d.RefreshVitals();
        Assert.Equal(v2, d.Version);
    }

    [Fact]
    public void PlayerData310_Sets_Identity_Vitals_JobLevel()
    {
        var d = new LiveHudData();
        var pd = new PlayerHudData(1f, 1, 0, 10, 1500, 300, 1600, 320,
            5, new byte[32], 7, 0, 0);
        d.ApplyPlayerData(0x00EBC204, "Tester", pd);
        Assert.Equal("Tester", d.Vitals!.Name);
        Assert.Equal(10, d.Vitals.Level);
        Assert.Equal(7, d.Vitals.JobLevel);
        Assert.Equal(1500, d.Vitals.Hp);
        Assert.Equal(1600, d.Vitals.HpMax);
        Assert.Equal(300, d.Vitals.Mp);
        Assert.Equal(320, d.Vitals.MpMax);
        Assert.Equal(0x00EBC204u, d.SelfCharId);
    }

    [Fact]
    public void Attr280_Maps_Vitals_Like_Reference()
    {
        var d = new LiveHudData();
        d.SetSelf(7, "n", 1, 1);
        PlayerVitals.SetVitals(100, 200, 40, 80);
        // HP courant (0), MaxHP (0x11 → champ Max), MP courant (1),
        // MaxMP (0x12 → champ Current — asymétrie prouvée serveur).
        PlayerVitals.ApplyUpdateAttr(new ZoneUpdateAttr
            { Handle = 7, AttrId = 0x11, Current = 1, Max = 260 });
        PlayerVitals.ApplyUpdateAttr(new ZoneUpdateAttr
            { Handle = 7, AttrId = 0x12, Current = 99, Max = 0 });
        PlayerVitals.ApplyUpdateAttr(new ZoneUpdateAttr
            { Handle = 7, AttrId = 0x00, Current = 42, Max = 0 });
        PlayerVitals.ApplyUpdateAttr(new ZoneUpdateAttr
            { Handle = 7, AttrId = 0x01, Current = 11, Max = 0 });
        d.RefreshVitals();
        Assert.Equal(42, d.Vitals!.Hp);
        Assert.Equal(260, d.Vitals.HpMax);
        Assert.Equal(11, d.Vitals.Mp);
        Assert.Equal(99, d.Vitals.MpMax);
    }

    [Fact]
    public void Exp600_Refreshes_Bar()
    {
        var d = new LiveHudData();
        d.SetSelf(1, "n", 5, 1);
        PlayerVitals.ApplyExp(12345);
        d.RefreshVitals();
        Assert.Equal(12345, d.Vitals!.Exp);
        // ExpMax = palier LevelTable (0 si la table ne résout pas).
        Assert.Equal(EdenClient.Data.LevelTable.ExpForNext(5) ?? 0,
            d.Vitals.ExpMax);
    }

    [Fact]
    public void Zone_Sets_Name_And_Banner()
    {
        var d = new LiveHudData();
        var v0 = d.Version;
        d.SetZone(2);
        // Nom via t_node.ini (résolue ou repli « S002 »).
        var expected = EdenClient.Data.TextResolver.NodeName(2) ?? "S002";
        Assert.Equal(expected, d.ZoneName);
        Assert.Equal(expected, d.Banner);
        Assert.True(d.Version > v0);
        d.ClearBanner();
        Assert.Null(d.Banner);
        // ClearBanner idempotent.
        var v1 = d.Version;
        d.ClearBanner();
        Assert.Equal(v1, d.Version);
    }

    [Fact]
    public void Chat_Is_Capped_And_Labelled()
    {
        var d = new LiveHudData();
        for (var i = 0; i < LiveHudData.ChatCap + 10; i++)
            d.AddChat(0, "a", $"m{i}");
        Assert.Equal(LiveHudData.ChatCap, d.Chat.Count);
        Assert.Equal("m109", d.Chat[^1].Text);
        var line = d.Chat[^1];
        Assert.Equal("a", line.Sender);
        Assert.True(line.Channel.Length > 0);
        // Canal système ≥7 : libellé non vide aussi (« System »).
        d.AddChat(7, "", "boot");
        Assert.Equal(LiveHudData.ChannelLabel(7), d.Chat[^1].Channel);
        Assert.NotEqual(LiveHudData.ChannelLabel(0),
            LiveHudData.ChannelLabel(5));
    }

    [Fact]
    public void Quests_Trace_Then_Update_Lifecycle()
    {
        var d = new LiveHudData();
        var m = new ZoneMissionStates();
        m.Fms.Add((1012, 3));
        m.Fms.Add((2000, 1));            // terminée → jamais affichée
        m.Trace.Add(1012);
        m.Trace.Add(2000);               // slot traînant ignoré
        d.SetMissions(m);
        Assert.Single(d.Quests);
        // Titre via t_mission.ini (résolue ou repli « Quest #id »).
        var expectedTitle = EdenClient.Data.TextResolver.MissionTitle(1012)
            ?? EdenClient.Data.UiTextEn.Translate("Quest #1012");
        Assert.Equal(expectedTitle, d.Quests[0].Title);

        // 391 done (fms=1) → retirée du tracker.
        d.UpdateMission(new ZoneUpdateMissionState
            { Op = 0, Key = 1012, MissionId = 1 });
        Assert.Empty(d.Quests);

        // 391 en cours (fms=3) → ajoutée.
        d.UpdateMission(new ZoneUpdateMissionState
            { Op = 0, Key = 3333, MissionId = 3 });
        Assert.Single(d.Quests);
        var t3 = EdenClient.Data.TextResolver.MissionTitle(3333)
            ?? EdenClient.Data.UiTextEn.Translate("Quest #3333");
        Assert.Equal(t3, d.Quests[0].Title);
    }

    [Fact]
    public void Buffs_Snapshot_Self()
    {
        var d = new LiveHudData();
        d.SetSelf(42, "n", 1, 1);
        EntityBuffs.ApplyAdd(new ZoneEnchantAdd
        {
            Handle = 42, EffectId = 42, RestSeconds = 90,
            StackCount = 1, Level = 1,
        });
        EntityBuffs.ApplyAdd(new ZoneEnchantAdd
        {
            Handle = 99, EffectId = 42, RestSeconds = 30,
            StackCount = 1, Level = 1,
        });
        d.RefreshBuffs(42);
        Assert.Single(d.Buffs);                    // self seulement
        // Nom/classification via c_enchant.ini (repli « enchant #id »
        // / buff si la table ne résout pas dans ce contexte).
        var def = EdenClient.Data.Tables.EnchantTable.Get(42);
        Assert.Equal(EdenClient.Data.Tables.EnchantTable.NameOf(42),
            d.Buffs[0].Name);
        Assert.Equal(!(def?.IsBuff ?? true), d.Buffs[0].Debuff);
        Assert.True(d.Buffs[0].SecondsLeft is > 0 and <= 90);
    }

    [Fact]
    public void Position_And_Target_Are_Settable()
    {
        var d = new LiveHudData();
        var v0 = d.Version;
        d.SetPosition(10f, 20f);
        Assert.Equal((10f, 20f), d.Position);
        Assert.True(d.Version > v0);
        var v1 = d.Version;
        d.SetPosition(10f, 20f);
        Assert.Equal(v1, d.Version);
        // Cible : vide tant que le lot combat ne branche pas, mais
        // settable et versionnée.
        Assert.Null(d.Target);
        d.Target = new HudTarget("Slime", 2, 10, 10, 0, 0, true);
        Assert.True(d.Version > v1);
        Assert.Equal("Slime", d.Target!.Name);
        d.Target = null;
        Assert.Null(d.Target);
    }
}
