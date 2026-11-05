using Godot;

namespace EdenClient.Hud;

/// <summary>Préférences d'UI (catégorie « Préférences » des Paramètres)
/// stockées dans <c>clientSettings.json</c> → <c>options</c>
/// (indépendant de <c>settings.cfg</c>).</summary>
public static class HudPrefs
{
    public enum BarRole { Mob, Self, Other }

    public static event Action? Changed;
    /// <summary>PV du joueur local (alimenté par HudRoot) — lu par la barre au-dessus de sa tête.</summary>
    public static long SelfHp, SelfMaxHp;
    /// <summary>PM du joueur local (alimenté par TopLeftVitals) — seul MP
    /// connu côté client (le serveur ne diffuse pas celui des autres).</summary>
    public static long SelfMp, SelfMaxMp;
    /// <summary>Levée quand SelfMp/SelfMaxMp changent (cadre de cible = soi).</summary>
    public static event Action? SelfMpChanged;
    public static void SetSelfMp(long mp, long maxMp)
    {
        if (mp == SelfMp && maxMp == SelfMaxMp) return;
        SelfMp = mp; SelfMaxMp = maxMp;
        SelfMpChanged?.Invoke();
    }

    /// <summary>Quelles valeurs numériques afficher : rien / PV / PM / PV+PM.</summary>
    public enum ValuesMode { None = 0, Hp = 1, Mp = 2, Both = 3 }

    private static ValuesMode GetMode(string k, ValuesMode d) =>
        (ValuesMode)Math.Clamp((int)GetF(k, (int)d), 0, 3);

    /// <summary>Valeurs PV/PM dans le cadre de cible du HUD
    /// (<c>pref.vals.target</c>, défaut PV).</summary>
    public static ValuesMode TargetValues
    {
        get => GetMode("pref.vals.target", ValuesMode.Hp);
        set => Set("pref.vals.target", ((int)value).ToString());
    }
    /// <summary>Valeurs PV/PM au-dessus des entités
    /// (<c>pref.vals.overhead</c>, défaut rien).</summary>
    public static ValuesMode OverheadValues
    {
        get => GetMode("pref.vals.overhead", ValuesMode.None);
        set => Set("pref.vals.overhead", ((int)value).ToString());
    }

    /// <summary>« actuel/max » (true, défaut) ou « actuel seul » (false)
    /// pour les valeurs PV/PM (<c>pref.vals.max</c>) — cadre de cible et
    /// valeurs au-dessus des entités.</summary>
    public static bool ValuesShowMax
    {
        get => GetB("pref.vals.max", true);
        set => Set("pref.vals.max", value ? "1" : "0");
    }

    /// <summary>Afficher le libellé « PV »/« PM » devant les valeurs
    /// (<c>pref.vals.labels</c>, défaut true).</summary>
    public static bool ValuesLabels
    {
        get => GetB("pref.vals.labels", true);
        set => Set("pref.vals.labels", value ? "1" : "0");
    }

    /// <summary>Formate une valeur vitale selon les prefs :
    /// « PV 123/456 », « PV 123 », « 123/456 » ou « 123 ».</summary>
    public static string FormatValue(string label, long cur, long max)
    {
        var s = ValuesShowMax ? $"{cur}/{max}" : $"{cur}";
        return ValuesLabels ? $"{label} {s}" : s;
    }

    private static string Get(string k, string d) =>
        ClientUiStore.Current.Options.TryGetValue(k, out var v) ? v : d;

    private static bool GetB(string k, bool d) =>
        Get(k, d ? "1" : "0") == "1";

    private static float GetF(string k, float d) =>
        float.TryParse(Get(k, ""), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var f)
            ? f : d;

    private static void Set(string k, string v)
    {
        ClientUiStore.Current.Options[k] = v;
        ClientUiStore.Save();
        Changed?.Invoke();
    }

    public static bool BarMobs { get => GetB("pref.bar.mobs", true); set => Set("pref.bar.mobs", value ? "1" : "0"); }
    public static bool BarSelf { get => GetB("pref.bar.self", true); set => Set("pref.bar.self", value ? "1" : "0"); }
    public static bool BarOthers { get => GetB("pref.bar.others", true); set => Set("pref.bar.others", value ? "1" : "0"); }
    /// <summary>A4.4 : overlay FPS + ping (clé <c>ui.show_perf</c>,
    /// toggle Preferences).</summary>
    public static bool ShowPerf { get => GetB("ui.show_perf", false); set => Set("ui.show_perf", value ? "1" : "0"); }
    /// <summary>Noms flottants des joueurs distants (<c>pref.name.players</c>).</summary>
    public static bool ShowNames { get => GetB("pref.name.players", true); set => Set("pref.name.players", value ? "1" : "0"); }
    /// <summary>Son propre nom au-dessus du perso (<c>pref.name.self</c>,
    /// défaut off — le ref le montre, 3.3.3 le masquait).</summary>
    public static bool ShowOwnName { get => GetB("pref.name.self", false); set => Set("pref.name.self", value ? "1" : "0"); }
    /// <summary>Noms flottants des PNJ et monstres (<c>pref.name.npc</c>).</summary>
    public static bool ShowNpcNames { get => GetB("pref.name.npc", true); set => Set("pref.name.npc", value ? "1" : "0"); }
    /// <summary>Nombres de dégâts flottants (<c>pref.damage</c>).</summary>
    public static bool ShowDamage { get => GetB("pref.damage", true); set => Set("pref.damage", value ? "1" : "0"); }
    /// <summary>Caméra inversée sur l'axe vertical (<c>pref.cam.invert</c>).</summary>
    public static bool InvertCamera { get => GetB("pref.cam.invert", false); set => Set("pref.cam.invert", value ? "1" : "0"); }
    /// <summary>Refuse automatiquement les invitations d'échange.</summary>
    public static bool RefuseTrade { get => GetB("pref.refuse.trade", false); set => Set("pref.refuse.trade", value ? "1" : "0"); }
    /// <summary>Refuse automatiquement les duels.</summary>
    public static bool NoDuel { get => GetB("pref.refuse.duel", false); set => Set("pref.refuse.duel", value ? "1" : "0"); }
    /// <summary>Refuse automatiquement les invitations de groupe.</summary>
    public static bool NoParty { get => GetB("pref.refuse.party", false); set => Set("pref.refuse.party", value ? "1" : "0"); }
    /// <summary>#id en pied des tooltips (<c>ui.show_ids</c>) ; le défaut
    /// vient de client.cfg, l'écriture met aussi à jour
    /// <see cref="ClientConfig.ShowIdsEnabled"/>.</summary>
    public static bool ShowIds
    {
        get => GetB("ui.show_ids", ClientConfig.ShowIdsEnabled);
        set { ClientConfig.ShowIdsEnabled = value; Set("ui.show_ids", value ? "1" : "0"); }
    }
    /// <summary>Taille de police du journal de chat (10..20, défaut 13).</summary>
    public static int ChatFontSize
    {
        get => Math.Clamp((int)GetF("ui.chat_font_size", 13f), 10, 20);
        set => Set("ui.chat_font_size", Math.Clamp(value, 10, 20).ToString());
    }
    /// <summary>Titre de son propre perso (<c>pref.title.self</c>).</summary>
    public static bool ShowOwnTitle { get => GetB("pref.title.self", true); set => Set("pref.title.self", value ? "1" : "0"); }
    /// <summary>Guilde des autres joueurs (<c>pref.guild.others</c>) ;
    /// ignorée tant que le serveur n'envoie pas leur guilde.</summary>
    public static bool ShowOthersGuild { get => GetB("pref.guild.others", true); set => Set("pref.guild.others", value ? "1" : "0"); }
    /// <summary>Titre (appellation) flottant des autres joueurs.</summary>
    public static bool ShowTitle { get => GetB("pref.show.title", true); set => Set("pref.show.title", value ? "1" : "0"); }
    /// <summary>Nom de guilde flottant au-dessus des têtes (soi seulement :
    /// le 265/263 ne porte pas la guilde des autres joueurs).</summary>
    public static bool ShowGuild { get => GetB("pref.show.guild", true); set => Set("pref.show.guild", value ? "1" : "0"); }
    /// <summary>Valeurs numériques HP/MP sur les barres du HUD.</summary>
    public static bool ShowVitals { get => GetB("pref.show.vitals", true); set => Set("pref.show.vitals", value ? "1" : "0"); }
    /// <summary>Sort allié sans cible amie → lancé sur soi.</summary>
    public static bool SelfCast { get => GetB("pref.selfcast", true); set => Set("pref.selfcast", value ? "1" : "0"); }
    /// <summary>Secousse caméra sur coup critique.</summary>
    public static bool CritShock { get => GetB("pref.crit.shock", true); set => Set("pref.crit.shock", value ? "1" : "0"); }
    /// <summary>Vibration manette sur critique (si manette connectée).</summary>
    public static bool Vibration { get => GetB("pref.vibration", true); set => Set("pref.vibration", value ? "1" : "0"); }
    /// <summary>Animation « gelée » (rebond élastique) à l'ouverture des fenêtres.</summary>
    public static bool JellyEffect { get => GetB("pref.jelly", false); set => Set("pref.jelly", value ? "1" : "0"); }
    /// <summary>La caméra se recale derrière le perso quand il avance.</summary>
    public static bool CameraFollow { get => GetB("pref.cam.follow", false); set => Set("pref.cam.follow", value ? "1" : "0"); }

    // ---- Cases « ShowKuso* » du profil natif (EquipmentWnd_new2) ----
    // Le natif les persiste dans le fichier d'options client
    // (FUN_0069e290 client_game : ShowKusoHelmetb=%d — byte 0xe1/0xe2)
    // et rafraîchit l'avatar en local, SANS paquet réseau : la
    // visibilité des pièces d'équipement est une préférence
    // d'affichage, jamais l'état porté.
    /// <summary>Lit une case ShowKuso&lt;suffix&gt; (défaut visible).</summary>
    public static bool KusoVisible(string suffix) =>
        GetB($"pref.kuso.{suffix}", true);
    /// <summary>Écrit une case ShowKuso&lt;suffix&gt;.</summary>
    public static void SetKusoVisible(string suffix, bool visible) =>
        Set($"pref.kuso.{suffix}", visible ? "1" : "0");
    /// <summary>Le loc d'équipement doit-il être rendu ? (tête=1 →
    /// ShowKusoHelmet, dos=6 → ShowKusoCloak — les autres locs n'ont
    /// pas de case dédiée côté visuel actuel).</summary>
    public static bool AttachVisibleForLoc(int loc) => loc switch
    {
        1 => KusoVisible("helmet"),
        6 => KusoVisible("cloak"),
        _ => true,
    };
    /// <summary>Case « ShowKusoClothes » : meshes armure « #… ».</summary>
    public static bool KusoClothesVisible => KusoVisible("clothes");
    /// <summary>Clic gauche sur le sol = déplacement (point&amp;click).</summary>
    public static bool LeftClickRoute { get => GetB("pref.click.route", true); set => Set("pref.click.route", value ? "1" : "0"); }
    /// <summary>Multiplicateur de sensibilité souris de la caméra (0.25..2.5).</summary>
    public static float MouseSpeed
    {
        get => Math.Clamp(GetF("pref.mouse.speed", 1f), 0.25f, 2.5f);
        set => Set("pref.mouse.speed", Math.Clamp(value, 0.25f, 2.5f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }
    public static bool LossAnim { get => GetB("pref.loss.on", true); set => Set("pref.loss.on", value ? "1" : "0"); }
    /// <summary>Délai (s) avant que la perte orange ne descende.</summary>
    public static float LossDelay
    {
        get => Math.Clamp(GetF("pref.loss.delay", 0.7f), 0.1f, 3f);
        set => Set("pref.loss.delay", Math.Clamp(value, 0.1f, 3f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }
    /// <summary>Durée (s) de la descente de la perte.</summary>
    public static float LossDuration
    {
        get => Math.Clamp(GetF("pref.loss.dur", 0.35f), 0.05f, 2f);
        set => Set("pref.loss.dur", Math.Clamp(value, 0.05f, 2f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
    }

    public static bool BarVisible(BarRole r) => r switch
    {
        BarRole.Mob => BarMobs,
        BarRole.Self => BarSelf,
        _ => BarOthers,
    };

    public static void ResetPrefs()
    {
        foreach (var k in new[] { "pref.bar.mobs", "pref.bar.self",
                     "pref.bar.others", "pref.loss.on", "pref.loss.delay",
                     "pref.loss.dur", "ui.show_perf", "pref.name.players",
                     "pref.name.npc", "pref.name.self", "pref.damage", "pref.cam.invert",
                     "pref.refuse.trade", "pref.refuse.duel",
                     "pref.refuse.party", "ui.show_ids", "ui.chat_font_size",
                     "pref.show.title", "pref.show.guild", "pref.title.self", "pref.guild.others", "pref.show.vitals",
                     "pref.selfcast", "pref.crit.shock", "pref.vibration",
                     "pref.jelly", "pref.cam.follow", "pref.click.route",
                     "pref.mouse.speed", "pref.vals.target",
                     "pref.vals.overhead" })
            ClientUiStore.Current.Options.Remove(k);
        ClientConfig.ShowIdsEnabled = ClientConfig.Load().ShowIds;
        ClientUiStore.Save();
        Changed?.Invoke();
    }
}

/// <summary>
/// Suivi des pertes de PV d'une barre : chaque baisse crée un segment
/// orange (écart avant/après) qui attend <see cref="HudPrefs.LossDelay"/>
/// puis « descend » sur <see cref="HudPrefs.LossDuration"/>. Les segments
/// sont indépendants (2 coups espacés de 0,5 s = 2 descentes
/// animées successives). Un soin vide les segments.
/// </summary>
public sealed class HpLossAnim
{
    private struct Seg { public float Size; public double T0; }
    private readonly List<Seg> _segs = new();
    private float _frac = -1f;

    public float Frac => _frac < 0 ? 1f : _frac;
    public bool Active => _segs.Count > 0;

    private static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>Pose la fraction de PV (0..1). Première valeur =
    /// état initial (aucune animation).</summary>
    public void Set(float frac)
    {
        frac = Mathf.Clamp(frac, 0f, 1f);
        if (_frac < 0f) { _frac = frac; return; }
        if (frac < _frac - 1e-4f)
        {
            if (HudPrefs.LossAnim)
                _segs.Add(new Seg { Size = _frac - frac, T0 = Now });
        }
        else if (frac > _frac + 1e-4f)
            _segs.Clear();
        _frac = frac;
    }

    /// <summary>Bord haut de la zone orange (≥ Frac) à l'instant
    /// courant ; purge les segments terminés.</summary>
    public float GhostTop()
    {
        if (!HudPrefs.LossAnim) { _segs.Clear(); return Frac; }
        var delay = HudPrefs.LossDelay;
        var dur = Math.Max(0.01f, HudPrefs.LossDuration);
        var now = Now;
        var sum = 0f;
        for (var i = _segs.Count - 1; i >= 0; i--)
        {
            var s = _segs[i];
            var t = (float)(now - s.T0) - delay;
            if (t >= dur) { _segs.RemoveAt(i); continue; }
            sum += t <= 0 ? s.Size : s.Size * (1f - EaseOut(t / dur));
        }
        return Mathf.Min(1f, Frac + sum);
    }

    private static float EaseOut(float x) => 1f - (1f - x) * (1f - x);
}

/// <summary>Zone orange « PV perdus » à poser en enfant plein-cadre
/// d'une barre 2D : dessine [Lo..Hi] (fractions) en orange.</summary>
public partial class GhostBar : Control
{
    public float Lo, Hi;
    public Color Tint = new(1f, 0.55f, 0.08f, 0.95f);

    public GhostBar()
    {
        Name = "__ghost__";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }

    public override void _Draw()
    {
        if (Hi <= Lo + 1e-4f) return;
        DrawRect(new Rect2(Size.X * Lo, 0, Size.X * (Hi - Lo), Size.Y), Tint);
    }
}

/// <summary>
/// Barre de PV au-dessus d'une entité : UN seul quad billboard avec
/// shader (fond + orange + remplissage dans le même fragment) — plus
/// de quads coplanaires dont l'ordre de profondeur dépendait du côté
/// d'où l'on regardait (barre « vide » vue du nord, « pleine » vue du
/// sud).
/// </summary>
public partial class HpBar3D : MeshInstance3D
{
    private const string Src = @"
shader_type spatial;
render_mode unshaded, cull_disabled, blend_mix;
uniform float frac = 1.0;
uniform float ghost = 1.0;
uniform vec4 fill_color : source_color = vec4(0.3, 0.9, 0.3, 1.0);
uniform vec4 ghost_color : source_color = vec4(1.0, 0.55, 0.08, 1.0);
uniform vec2 margin = vec2(0.02, 0.16);
void vertex() {
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
}
void fragment() {
    vec2 uv = UV;
    bool inside = uv.x >= margin.x && uv.x <= 1.0 - margin.x && uv.y >= margin.y && uv.y <= 1.0 - margin.y;
    vec4 c = vec4(0.0, 0.0, 0.0, 0.85);
    if (inside) {
        float u = (uv.x - margin.x) / (1.0 - 2.0 * margin.x);
        if (u <= frac) c = fill_color;
        else if (u <= ghost) c = ghost_color;
        else c = vec4(0.12, 0.12, 0.12, 0.75);
    }
    ALBEDO = c.rgb;
    ALPHA = c.a;
}";

    private static Shader? _shader;
    private readonly ShaderMaterial _mat = new();
    private readonly HpLossAnim _anim = new();
    private readonly HudPrefs.BarRole _role;
    private bool _want;
    /// <summary>Couleur fixe (soi/joueurs) ; null = vert→rouge selon PV.</summary>
    public Color? FixedFill;
    /// <summary>Barre du joueur local : rose (ref « juste le jeu »),
    /// prioritaire sur FixedFill.</summary>
    public static readonly Color SelfPink = new(0.95f, 0.4f, 0.55f);

    public HpBar3D(HudPrefs.BarRole role, Vector2 size)
    {
        _role = role;
        Name = "HpBar3D";
        _shader ??= new Shader { Code = Src };
        _mat.Shader = _shader;
        Mesh = new QuadMesh { Size = size };
        MaterialOverride = _mat;
        CastShadow = ShadowCastingSetting.Off;
        VisibilityRangeEnd = 96f;
        SetProcess(false);
    }

    public override void _EnterTree() => HudPrefs.Changed += Refresh;
    public override void _ExitTree() => HudPrefs.Changed -= Refresh;

    private void Refresh() => Visible = _want && HudPrefs.BarVisible(_role);

    /// <summary>Met à jour la barre ; <paramref name="want"/> = false
    /// la cache (ex. MaxHp inconnu).</summary>
    public void SetHp(long hp, long maxHp, bool want = true)
    {
        _want = want && maxHp > 0;
        Refresh();
        if (!_want) return;
        var frac = Mathf.Clamp((float)hp / maxHp, 0f, 1f);
        _anim.Set(frac);
        _mat.SetShaderParameter("frac", frac);
        _mat.SetShaderParameter("ghost", _anim.GhostTop());
        _mat.SetShaderParameter("fill_color",
            (_role == HudPrefs.BarRole.Self ? SelfPink : FixedFill) ?? (frac > 0.5f
            ? new Color(0.3f, 0.9f, 0.3f)
            : frac > 0.25f ? new Color(0.95f, 0.7f, 0.2f)
            : new Color(0.9f, 0.25f, 0.2f)));
        SetProcess(_anim.Active);
    }

    public override void _Process(double delta)
    {
        _mat.SetShaderParameter("ghost", _anim.GhostTop());
        if (!_anim.Active) SetProcess(false);
    }
}
