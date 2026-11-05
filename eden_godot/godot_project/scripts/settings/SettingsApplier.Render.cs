using Godot;
using EdenClient.Hud;
using EdenClient.Player;
using EdenClient.Protocol;
using EdenClient.World;

namespace EdenClient.Settings;

/// <summary>
/// Réglages vidéo « natifs » (Models, Background, Char Draw, Max FX,
/// Outline, Special FX) et effets de préférences qui exigent un pilote
/// périodique (titre/guilde flottants, armes rangées). Un tick à 2 Hz
/// parcourt le registre d'entités — aucune écriture à chaque frame.
/// </summary>
public partial class SettingsApplier
{
    private double _drvT, _shakeT;
    private float _shakeAmp;
    private EntityRegistry? _drvReg;
    private PlayerController? _drvPlayer;
    private bool _unfocused, _wasDead;
    private double _lookupT;

    // ------------------------------------------------------------------
    // Application (appelée par ApplyGraphics)

    /// <summary>Models / Textures / Water / grade couleur (hot).</summary>
    private void ApplyRender(Node root)
    {
        var vp = GetViewport();
        // Models Low/High : seuil de LOD des meshes (plus haut = LOD
        // plus agressifs) + distance d'animation des squelettes.
        vp.MeshLodThreshold = S.ModelQuality == 0 ? 4f : 1f;
        Game.EntityAnimator.AnimLodScale = S.ModelQuality == 0 ? 0.4f : 1f;
        // Textures Low/Mid/High : filtrage anisotrope du viewport (les
        // mipmaps/resolutions des assets ne sont pas rechargeables à chaud).
        vp.AnisotropicFilteringLevel = S.TextureQuality switch
        {
            0 => Viewport.AnisotropicFiltering.Disabled,
            1 => Viewport.AnisotropicFiltering.Anisotropy4X,
            _ => Viewport.AnisotropicFiltering.Anisotropy16X,
        };
        WaterAnimator.SetWaves(S.FxWater);
        ApplyColorGrade(root);
        DriveEntities();
    }

    /// <summary>Glow (Bloom/Glare) + étalonnage (Photo, Death) sur
    /// les Environment de la scène de jeu.</summary>
    private void ApplyColorGrade(Node root)
    {
        var dead = PlayerVitals.IsDead && S.FxDeath;
        foreach (var we in root.FindChildren("*", "WorldEnvironment",
                     true, false).Cast<WorldEnvironment>())
        {
            // fog_ceiling_env NON exclu : ce grade ne touche pas le
            // fog — l'exclure privait les maps foguées (quasi toutes)
            // de glow/adjust même dans les bancs.
            if (we.HasMeta("pregame_env") ||
                we.Environment is not { } env) continue;
            if (!env.HasMeta("glow_i0"))
            {
                env.SetMeta("glow_i0", env.GlowIntensity);
                env.SetMeta("glow_b0", env.GlowBloom);
            }
            env.GlowEnabled = S.Bloom || S.FxGlare;
            env.GlowIntensity = S.FxGlare
                ? Math.Max(1.2f, env.GetMeta("glow_i0").AsSingle() * 1.5f)
                : env.GetMeta("glow_i0").AsSingle();
            env.GlowBloom = S.FxGlare
                ? Math.Max(0.12f, env.GetMeta("glow_b0").AsSingle())
                : env.GetMeta("glow_b0").AsSingle();
            env.AdjustmentEnabled = S.FxPhoto || dead;
            env.AdjustmentContrast = S.FxPhoto ? 1.08f : 1f;
            env.AdjustmentSaturation =
                (dead ? 0.2f : 1f) * (S.FxPhoto ? 1.15f : 1f);
        }
    }

    // ------------------------------------------------------------------
    // Focus : « Background » FPS

    private int EffectiveMaxFps() => !_unfocused ? S.MaxFps
        : S.MaxFps > 0 ? Math.Min(S.MaxFps, S.BackgroundFps)
        : S.BackgroundFps;

    private void PollFocus()
    {
        var unf = !DisplayServer.WindowIsFocused();
        if (unf == _unfocused) return;
        _unfocused = unf;
        Engine.MaxFps = EffectiveMaxFps();
    }

    // ------------------------------------------------------------------
    // Secousse caméra (Critical Shock)

    /// <summary>Secoue la caméra active ~0,25 s (amplitude en
    /// unités monde).</summary>
    public void Shake(float amp)
    {
        _shakeAmp = Math.Max(_shakeAmp, amp);
        _shakeT = 0.25;
    }

    private void TickShake(double delta)
    {
        if (_shakeT <= 0 && _shakeAmp <= 0) return;
        var cam = GetViewport().GetCamera3D();
        if (cam == null) { _shakeT = 0; _shakeAmp = 0; return; }
        _shakeT -= delta;
        if (_shakeT <= 0)
        {
            _shakeT = 0; _shakeAmp = 0;
            cam.HOffset = 0f; cam.VOffset = 0f;
            return;
        }
        var k = (float)(_shakeT / 0.25) * _shakeAmp;
        cam.HOffset = (GD.Randf() - 0.5f) * 2f * k;
        cam.VOffset = (GD.Randf() - 0.5f) * 2f * k;
    }

    // ------------------------------------------------------------------
    // Tick 2 Hz

    private void TickRender(double delta)
    {
        TickShake(delta);
        if ((_lookupT -= delta) <= 0) { _lookupT = 0.25; PollFocus(); }
        if ((_drvT += delta) < 0.5) return;
        _drvT = 0;
        DriveEntities();
        var dead = PlayerVitals.IsDead;
        if (dead != _wasDead)
        {
            _wasDead = dead;
            if (GetTree().CurrentScene is { } r) ApplyColorGrade(r);
        }
    }

    private void FindDriven()
    {
        var scene = GetTree().CurrentScene;
        if (scene == null) return;
        if (_drvReg == null || !GodotObject.IsInstanceValid(_drvReg))
            _drvReg = scene.FindChild("NetEntities", true, false)
                as EntityRegistry;
        if (_drvPlayer == null || !GodotObject.IsInstanceValid(_drvPlayer))
            _drvPlayer = scene.FindChild("Player", true, false)
                as PlayerController;
    }

    private void DriveEntities()
    {
        FindDriven();
        var cam = GetViewport().GetCamera3D();
        var reg = _drvReg;
        var ents = new List<RemoteEntity>();
        if (reg != null)
            foreach (var e in reg.Entities.Values)
                if (GodotObject.IsInstanceValid(e) && e.IsInsideTree())
                    ents.Add(e);

        // --- Char Draw : plafond de joueurs distants dessinés ---
        var limit = S.CharDraw >= 100 ? int.MaxValue : S.CharDraw;
        var camPos = cam?.GlobalPosition ?? Vector3.Zero;
        var hiddenSet = new HashSet<ulong>();
        if (limit != int.MaxValue && cam != null)
        {
            var players = ents.Where(e => e.Kind == 0 && !e.Dying)
                .OrderBy(e => e.GlobalPosition.DistanceSquaredTo(camPos))
                .ToList();
            for (var i = limit; i < players.Count; i++)
                hiddenSet.Add(players[i].GetInstanceId());
        }
        foreach (var e in ents)
        {
            var hide = hiddenSet.Contains(e.GetInstanceId());
            var was = e.HasMeta("cd_hidden");
            if (hide == was) continue;
            e.Visible = !hide;
            if (!e.Dying)
                e.GetNodeOrNull<Area3D>("PickArea")?.SetDeferred(
                    Area3D.PropertyName.Monitorable, !hide);
            if (hide) e.SetMeta("cd_hidden", true);
            else e.RemoveMeta("cd_hidden");
        }

        // --- Outline ---
        foreach (var e in ents)
        {
            var want = S.Outline == OutlineMode.All ||
                       (S.Outline == OutlineMode.Characters && e.Kind == 0);
            if (want && cam != null &&
                e.GlobalPosition.DistanceSquaredTo(camPos) > 3600f)
                want = false;       // > 60 u : inutile et coûteux
            if (want || e.HasMeta("outlined"))
                SetOutline(e.GetNodeOrNull<Node3D>("Visual"), e, want);
        }
        if (_drvPlayer != null)
        {
            var on = S.Outline != OutlineMode.Off;
            if (on || _drvPlayer.HasMeta("outlined"))
                SetOutline(_drvPlayer.Visual, _drvPlayer, on);
        }

        // --- Armes rangées (Toggle WPN) : les équipements s'attachent
        //     après SetVisual → réappliquer ---
        if (_drvPlayer is { WeaponsHidden: true })
            _drvPlayer.ApplyWeaponVisibility();

        // --- Titre / guilde flottants ---
        foreach (var e in ents)
        {
            if (e.Kind != 0 || e.Dying) continue;
            PlayerTitles.Others.TryGetValue((int)e.Handle, out var t);
            SetTag(e, "__title", HudPrefs.ShowTitle ? TitleText(t.TitleId) : "", new Color(1f, 0.85f, 0.4f));
            SetTag(e, "__guild", "", new Color(0.6f, 1f, 0.6f));
            // Pile nom/guilde/titre/barre recalée sur le modèle réel
            // (équipement, monture, échelle) pour les entités proches.
            if (cam == null ||
                e.GlobalPosition.DistanceSquaredTo(camPos) < 2500f)
                e.RelayoutOverhead();
        }
        foreach (var e in ents)
            if (e.Kind != 0 && !e.Dying && (cam == null ||
                e.GlobalPosition.DistanceSquaredTo(camPos) < 2500f))
                e.RelayoutOverhead();
        if (_drvPlayer != null)
        {
            SetTag(_drvPlayer, "__title",
                HudPrefs.ShowOwnTitle ? TitleText(PlayerTitles.Active) : "", new Color(1f, 0.85f, 0.4f));
            SetTag(_drvPlayer, "__guild",
                HudPrefs.ShowGuild && SocialState.GuildName.Length > 0
                    ? $"<{SocialState.GuildName}>" : "", new Color(0.6f, 1f, 0.6f));
            _drvPlayer.RelayoutOverhead();
        }
    }

    private static string TitleText(int id) =>
        id > 0 ? $"[{AchievementWindow.TitleName(id)}]" : "";

    /// <summary>Label3D discret (titre/guilde) sous le nœud
    /// <paramref name="host"/> ; texte vide = masqué.</summary>
    private static void SetTag(Node3D host, string name, string text,
        Color color)
    {
        var l = host.GetNodeOrNull<Label3D>(name);
        if (text.Length == 0)
        {
            if (l != null) l.Visible = false;
            return;
        }
        if (l == null)
        {
            l = new Label3D
            {
                Name = name,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                FontSize = 32, PixelSize = 0.0065f, OutlineSize = 8,
                OutlineModulate = new Color(0f, 0f, 0f, 0.85f),
                Modulate = color,
                VisibilityRangeEnd = 60f,
            };
            host.AddChild(l);
        }
        if (l.Text != text) l.Text = text;
        l.Visible = true;
    }

    // ------------------------------------------------------------------
    // Outline (inverted hull en MaterialOverlay)

    private const string OutlineShader = @"
shader_type spatial;
render_mode unshaded, cull_front;
uniform vec4 outline_color : source_color = vec4(0.05, 0.04, 0.04, 1.0);
uniform float thickness = 0.022;
void vertex() {
    vec4 wp = MODEL_MATRIX * vec4(VERTEX, 1.0);
    vec3 wn = normalize(mat3(MODEL_MATRIX) * NORMAL);
    wp.xyz += wn * thickness;
    POSITION = PROJECTION_MATRIX * VIEW_MATRIX * wp;
}
void fragment() {
    ALBEDO = outline_color.rgb;
}";

    private static ShaderMaterial? _outlineMat;

    private static ShaderMaterial OutlineMat => _outlineMat ??=
        new ShaderMaterial { Shader = new Shader { Code = OutlineShader } };

    private static void SetOutline(Node3D? visual, Node3D owner, bool on)
    {
        if (visual == null)
        {
            if (!on) owner.RemoveMeta("outlined");
            return;
        }
        foreach (var n in visual.FindChildren("*", "MeshInstance3D",
                     true, false))
        {
            if (n is not MeshInstance3D mi || mi is HpBar3D ||
                mi.Mesh == null) continue;
            OutlineFx.Apply(mi, on, OutlineMat);
        }
        if (on) owner.SetMeta("outlined", true);
        else owner.RemoveMeta("outlined");
    }
}
