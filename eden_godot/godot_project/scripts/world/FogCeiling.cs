using Godot;
using EdenClient.Data.Tables;

namespace EdenClient.World;

/// <summary>
/// Plafond FogMax natif — le fog linéaire de Godot monte à 100 % à
/// <c>FogDepthEnd</c> alors que le natif sature à FogMax (~8-36 %).
/// Ce nœud installe un quad plein-écran post-process
/// (<c>fog_ceiling.gdshader</c> — spatial : <c>hint_depth_texture</c>
/// n'existe pas en canvas_item sur Forward+) qui voile l'image d'un
/// fog linéaire <b>plafonné à FogMax</b> à partir de FogSightRang, et
/// neutralise le fog builtin du WorldEnvironment « Environment » pour
/// éviter le double voile.
///
/// Auto-configuration : le nœud remonte ses ancêtres à la recherche
/// de la racine « Map_Sxxx » (nommée par MapLoader) → charge
/// <c>map/model/Sxxx/Sxxx.ini</c> phase Day. Aucun paramètre trouvé →
/// le nœud se désactive (fog builtin inchangé).
///
/// Approximation documentée : fog par distance-caméra en profondeur
/// linéaire (le natif est probablement en profondeur vue aussi —
/// différence négligeable à ces opacités) ; dôme de ciel teinté au
/// plafond (cohérent avec l'horizon natif).
/// </summary>
public partial class FogCeiling : Node3D
{
    private ShaderMaterial? _mat;
    private bool _configured;
    private bool _envDone;
    private double _retryAt;

    /// <summary>Crée le plafond de fog pour une map (null si la map
    /// n'a pas de paramètres fog exploitables).</summary>
    public static FogCeiling? Create()
    {
        return new FogCeiling { Name = "FogCeiling" };
    }

    public override void _Ready()
    {
        _mat = new ShaderMaterial
        {
            Shader = GD.Load<Shader>(
                "res://shaders/fog_ceiling.gdshader"),
        };
        // Quad 2×2 plaqué plein-écran par le shader (clip-space) —
        // position du nœud sans importance ; rendu en passe
        // transparent (ALPHA>0 → après l'opaque, depth_tex stable).
        AddChild(new MeshInstance3D
        {
            Name = "FogQuad",
            Mesh = new QuadMesh { Size = new Vector2(2f, 2f) },
            MaterialOverride = _mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384f, // le quad couvre l'écran quelle
                                      // que soit la caméra — jamais
                                      // de culling frustum
        });
        Visible = false; // jusqu'à configuration réussie
    }

    public override void _Process(double delta)
    {
        if (_configured) return;
        if (Time.GetTicksMsec() < _retryAt) return;
        TryConfigure();
    }

    /// <summary>Trouve la racine Map_Sxxx ancêtre, charge l'env ini
    /// et règle le shader ; neutralise le fog builtin (une fois).</summary>
    private void TryConfigure()
    {
        _retryAt = Time.GetTicksMsec() + 500;
        for (var n = GetParent(); n != null; n = n.GetParent())
        {
            var name = n.Name.ToString();
            if (!name.StartsWith("Map_", StringComparison.Ordinal))
                continue;
            var code = name[4..];
            var envPath = AssetCatalog.ResolveRawAsset(
                $"map/model/{code}/{code}.ini");
            var ph = n.HasMeta("env_phase")
                ? (MapEnvIni.Phase)(int)n.GetMeta("env_phase")
                : MapEnvIni.Phase.Day;
            var phase = envPath != null
                ? MapEnvIni.Load(envPath).GetPhase(ph)
                : null;
            if (phase is not { FogSightRange: > 0f, FogMax: > 0f } p)
            {
                _configured = true; // pas de fog réglé → inerte
                return;
            }
            var (r, g, b) = p.FogColor;
            _mat!.SetShaderParameter("fog_color",
                new Vector3(r / 255f, g / 255f, b / 255f));
            _mat.SetShaderParameter("fog_begin", 0f);
            _mat.SetShaderParameter("fog_full", p.FogSightRange);
            _mat.SetShaderParameter("fog_max",
                Mathf.Clamp(p.FogMax, 0f, 1f));
            _configured = true;
            // Respecte le réglage « Fog » (SettingsApplier peut
            // appliquer avant cette configuration différée —
            // SettingsApplier.cs:Visible = S.Fog).
            Visible = Settings.SettingsApplier.Instance?.S.Fog ?? true;
            GD.Print($"[fog] {code}: plafond FogMax={p.FogMax} " +
                     $"à {p.FogSightRange} u (post-process)");
            DisableBuiltinFog(n);
            return;
        }
    }

    /// <summary>Désactive le fog de profondeur du WorldEnvironment
    /// « Environment » posé par MapLoader sur la même racine map —
    /// le post-process le remplace (le ciel/l'ambiant restent).</summary>
    private void DisableBuiltinFog(Node mapRoot)
    {
        if (_envDone) return;
        _envDone = true;
        if (mapRoot.FindChild("Environment", false, false) is
            WorldEnvironment we && we.Environment != null)
        {
            we.Environment.FogEnabled = false;
            we.SetMeta("fog_ceiling_env", true);
        }
    }
}
