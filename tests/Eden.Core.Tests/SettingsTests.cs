using EdenClient.Settings;
using Xunit;

namespace EdenClient.NetTests;

/// <summary>Tests du modèle de paramètres (lot D) — noyau pur :
/// roundtrip load/save, clamp scale, presets, plan de binding.</summary>
public class SettingsTests
{
    private static string Tmp()
    {
        var p = Path.Combine(Path.GetTempPath(),
            $"eden_settings_{Guid.NewGuid():N}.cfg");
        return p;
    }

    [Fact]
    public void SaveLoad_Roundtrip_PreservesAllFields()
    {
        var s = new ClientSettings();
        s.ResetBindings();
        s.ResolutionWidth = 1920; s.ResolutionHeight = 1080;
        s.WindowMode = EdenWindowMode.Borderless;
        s.VSync = false; s.MaxFps = 120;
        s.Shadows = false; s.ShadowMapSize = 4096;
        s.ViewDistance = 1500f;
        s.Antialiasing = AntialiasMode.Msaa8;
        s.Bloom = true; s.Ssao = true;
        s.Fog = false; s.Grass = false; s.Particles = false;
        s.UiScale = 1.5f; s.Theme = "Purple";
        s.Font = "FONT01"; s.FontSize = 20; s.Language = "cn";
        s.SetBinding("eden_inventory", "F5");
        s.MasterVolume = 0.5f; s.MusicVolume = 0.3f;
        s.EffectsVolume = 0.7f;

        var path = Tmp();
        try
        {
            Assert.Equal(path, s.Save(path));
            var r = ClientSettings.Load(path);

            Assert.Equal(1920, r.ResolutionWidth);
            Assert.Equal(1080, r.ResolutionHeight);
            Assert.Equal(EdenWindowMode.Borderless, r.WindowMode);
            Assert.False(r.VSync);
            Assert.Equal(120, r.MaxFps);
            Assert.False(r.Shadows);
            Assert.Equal(4096, r.ShadowMapSize);
            Assert.Equal(1500f, r.ViewDistance);
            Assert.Equal(AntialiasMode.Msaa8, r.Antialiasing);
            Assert.True(r.Bloom);
            Assert.True(r.Ssao);
            Assert.False(r.Fog);
            Assert.False(r.Grass);
            Assert.False(r.Particles);
            Assert.Equal(1.5f, r.UiScale, 3);
            Assert.Equal("Purple", r.Theme);
            Assert.Equal(20, r.FontSize);
            Assert.Equal("cn", r.Language);
            Assert.Equal(new[] { "F5" }, r.GetBinding("eden_inventory"));
            Assert.Equal(0.5f, r.MasterVolume, 3);
            Assert.Equal(0.3f, r.MusicVolume, 3);
            Assert.Equal(0.7f, r.EffectsVolume, 3);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Parse_ToleratesCommentsSectionsAndCase()
    {
        var s = ClientSettings.Parse(
            "# commentaire\n; autre\n[DISPLAY]\n" +
            "RESOLUTION = 1600x900\nwindow_mode = fullscreen\n" +
            "vsync = false\nmax_fps = 60\n" +
            "[Audio]\nmaster = 0.25\n");
        Assert.Equal(1600, s.ResolutionWidth);
        Assert.Equal(900, s.ResolutionHeight);
        Assert.Equal(EdenWindowMode.Fullscreen, s.WindowMode);
        Assert.False(s.VSync);
        Assert.Equal(60, s.MaxFps);
        Assert.Equal(0.25f, s.MasterVolume, 3);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var s = ClientSettings.Load(
            Path.Combine(Path.GetTempPath(), "eden_absent_xyz.cfg"));
        Assert.Equal(EdenWindowMode.Windowed, s.WindowMode);
        Assert.True(s.VSync);
        Assert.NotEmpty(s.Bindings);
    }

    [Theory]
    [InlineData(0.1f, 0.5f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(1.0f, 1.0f)]
    [InlineData(1.5f, 1.5f)]
    [InlineData(2.0f, 2.0f)]
    [InlineData(9.9f, 2.0f)]
    public void UiScale_ClampedTo_0_5_2_0(float input, float expected)
    {
        var s = new ClientSettings { UiScale = input };
        Assert.Equal(expected, s.UiScale, 3);
        Assert.Equal(expected, ClientSettings.ClampUiScale(input), 3);
    }

    [Fact]
    public void ApplyPreset_SetsToggleBundle()
    {
        var s = new ClientSettings();
        s.ApplyPreset(GraphicsPreset.Low);
        Assert.False(s.Shadows);
        Assert.False(s.Grass);
        Assert.False(s.Particles);
        Assert.Equal(AntialiasMode.None, s.Antialiasing);
        Assert.Equal(GraphicsPreset.Low, s.Preset);

        s.ApplyPreset(GraphicsPreset.Ultra);
        Assert.True(s.Shadows);
        Assert.True(s.Bloom);
        Assert.True(s.Ssao);
        Assert.True(s.Ssil);
        Assert.Equal(4096, s.ShadowMapSize);
        Assert.Equal(AntialiasMode.Msaa8, s.Antialiasing);
    }

    [Fact]
    public void Preset_SerializedAsLowercase_AndReparsed()
    {
        var s = new ClientSettings();
        s.ApplyPreset(GraphicsPreset.High);
        var text = s.Serialize();
        Assert.Contains("preset=high", text);
        var r = ClientSettings.Parse(text);
        Assert.Equal(2048, r.ShadowMapSize);
        Assert.True(r.Ssao);
    }

    [Fact]
    public void DefaultBindings_CoverMoveAndEdenActions()
    {
        var defs = ClientSettings.DefaultBindings();
        Assert.Equal(new[] { "W", "Up" }, defs["move_forward"]);
        Assert.Equal(new[] { "S", "Down" }, defs["move_back"]);
        Assert.Equal(new[] { "I" }, defs["eden_inventory"]);
        Assert.Equal(new[] { "C" }, defs["eden_profile"]);
        Assert.Equal(new[] { "M" }, defs["eden_map"]);
        Assert.Equal(new[] { "N" }, defs["eden_pet"]);
        Assert.Equal(new[] { "L" }, defs["eden_achievements"]);
        Assert.Equal(new[] { "O" }, defs["eden_social"]);
        Assert.Equal(new[] { "Enter" }, defs["eden_chat"]);
        Assert.Equal(new[] { "Key1" }, defs["eden_slot_01"]);
        Assert.Equal(new[] { "Key0" }, defs["eden_slot_10"]);
        Assert.Equal(new[] { "Minus" }, defs["eden_slot_11"]);
        Assert.Equal(new[] { "Equal" }, defs["eden_slot_12"]);
        // Toutes les actions du plan existent dans BindingDefs.
        foreach (var (a, _, _) in ClientSettings.BindingDefs)
            Assert.True(defs.ContainsKey(a), $"binding manquant : {a}");
    }

    [Fact]
    public void BuildPlan_ReflectsSavedBindings()
    {
        var s = new ClientSettings();
        s.ResetBindings();
        s.SetBinding("eden_inventory", "F5", "F6");
        var plan = InputBindings.BuildPlan(s);
        Assert.Contains(("move_forward", "W"), plan);
        Assert.Contains(("eden_inventory", "F5"), plan);
        Assert.Contains(("eden_inventory", "F6"), plan);
        Assert.DoesNotContain(("eden_inventory", "I"), plan);
    }

    [Fact]
    public void Bindings_Survive_Roundtrip()
    {
        var s = new ClientSettings();
        s.ResetBindings();
        s.SetBinding("eden_map", "Tab");
        var r = ClientSettings.Parse(s.Serialize());
        Assert.Equal(new[] { "Tab" }, r.GetBinding("eden_map"));
        // Les autres bindings par défaut restent présents.
        Assert.Equal(new[] { "W", "Up" }, r.GetBinding("move_forward"));
    }

    [Fact]
    public void IsValidEventName_AcceptsKeysAndMouseButtons()
    {
        Assert.True(InputBindings.IsValidEventName("W"));
        Assert.True(InputBindings.IsValidEventName("MouseLeft"));
        Assert.True(InputBindings.IsValidEventName("MouseXButton1"));
        Assert.True(InputBindings.IsValidEventName("Mouse5"));
        // « pas les scroll » : la molette n'est pas bindable.
        Assert.False(InputBindings.IsValidEventName("WheelUp"));
        Assert.False(InputBindings.IsValidEventName("MouseBizarre"));
    }

    [Fact]
    public void Resolutions_AndFpsLimits_MatchSpec()
    {
        Assert.Equal((1024, 768), ClientSettings.Resolutions[0]);
        Assert.Equal((3840, 2160),
            ClientSettings.Resolutions[^1]);
        Assert.Equal(new[] { 30, 60, 120, 0 },
            ClientSettings.FpsLimits);
    }

    [Fact]
    public void VideoNativeSettings_RoundTrip_AndClamp()
    {
        var s = new ClientSettings
        {
            ModelQuality = 0, BackgroundFps = 15, CharDraw = 25,
            MaxFx = 7, Outline = OutlineMode.All, FxDeath = false,
            FxWater = false, Fx3D = false, FxPhoto = true,
            FxGlare = true,
        };
        var r = ClientSettings.Parse(s.Serialize());
        Assert.Equal(0, r.ModelQuality);
        Assert.Equal(15, r.BackgroundFps);
        Assert.Equal(25, r.CharDraw);
        Assert.Equal(7, r.MaxFx);
        Assert.Equal(OutlineMode.All, r.Outline);
        Assert.False(r.FxDeath);
        Assert.False(r.FxWater);
        Assert.False(r.Fx3D);
        Assert.True(r.FxPhoto);
        Assert.True(r.FxGlare);
        var c = ClientSettings.Parse(
            "[graphics]\nchar_draw=1\nmax_fx=999\nbackground_fps=1\n");
        Assert.Equal(5, c.CharDraw);
        Assert.Equal(60, c.MaxFx);
        Assert.Equal(5, c.BackgroundFps);
    }

    [Fact]
    public void Hotkeys_PartyMembersAndActions_HaveDefaults()
    {
        var d = ClientSettings.DefaultBindings();
        for (var i = 1; i <= 10; i++)
            Assert.Equal($"F{i}", d[$"eden_party_{i:D2}"][0]);
        Assert.Equal("X", d["eden_sit"][0]);
        Assert.Equal("Z", d["eden_weapon"][0]);
        Assert.Equal("R", d["eden_attack"][0]);
        Assert.Equal("O", d["eden_social"][0]);
        Assert.Equal("N", d["eden_pet"][0]);
    }
}
