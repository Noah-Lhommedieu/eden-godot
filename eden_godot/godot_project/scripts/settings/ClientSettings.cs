#if GODOT
using Godot;
#endif

namespace EdenClient.Settings;

/// <summary>Mode fenêtrage (onglet Affichage).</summary>
public enum EdenWindowMode
{
    /// <summary>Fenêtré classique.</summary>
    Windowed,
    /// <summary>Plein écran exclusif.</summary>
    Fullscreen,
    /// <summary>Fenêtré-plein-écran (borderless).</summary>
    Borderless,
}

/// <summary>Preset graphique global (D5).</summary>
public enum GraphicsPreset { Low, Medium, High, Ultra, Custom }

/// <summary>Anti-aliasing (MSAA/FXAA du Viewport).</summary>
public enum AntialiasMode { None, Fxaa, Msaa2, Msaa4, Msaa8 }

/// <summary>Contour des modèles (ref « Outline Off / Character / All »).</summary>
public enum OutlineMode { Off, Characters, All }

/// <summary>
/// Paramètres client persistants — <c>gamedata/settings.cfg</c>
/// (format INI <c>[section]</c> + <c>key=value</c>, identique en
/// esprit à <see cref="ClientConfig"/>). Classe <b>pure</b> :
/// aucune dépendance Godot — l'application runtime passe par
/// <see cref="SettingsApplier"/>/<see cref="InputBindings"/>.
///
/// Résolution des chemins (hors tests) : chargement =
/// <c>&lt;exe&gt;/gamedata/settings.cfg</c> puis
/// <c>res://gamedata/settings.cfg</c> (template embarqué) via
/// <see cref="DataPaths"/> ; sauvegarde = <c>&lt;exe&gt;/gamedata/</c>
/// en export, <c>res://gamedata/</c> du projet en éditeur.
/// </summary>
public sealed class ClientSettings
{
    public const string FileName = "settings.cfg";
    public const string ResPath = "res://gamedata/settings.cfg";

    // ------------------------------------------------ display ------
    public int ResolutionWidth = 1280;
    public int ResolutionHeight = 720;
    public EdenWindowMode WindowMode = EdenWindowMode.Windowed;
    public bool VSync = true;
    /// <summary>0 = illimité.</summary>
    public int MaxFps;

    // ----------------------------------------------- graphics ------
    public GraphicsPreset Preset = GraphicsPreset.Medium;
    public bool Shadows = true;
    public int ShadowMapSize = 1024;
    /// <summary>Distance de vue caméra (Camera3D.Far) — plancher
    /// haut : un far trop petit découpe le SkyDome (trou noir).</summary>
    public float ViewDistance = 2000f;
    public AntialiasMode Antialiasing = AntialiasMode.Fxaa;
    public bool Bloom;
    public bool Dof;
    public bool Ssao;
    public bool Ssil;
    public bool Fog = true;
    public bool Grass = true;
    public bool Particles = true;
    /// <summary>0=basse 1=moyenne 2=haute — stub : application non
    /// immédiate (rechargement des assets requis).</summary>
    public int TextureQuality = 2;
    /// <summary>Models Low/High (0/1) : LOD des meshes + distance
    /// d'animation des squelettes.</summary>
    public int ModelQuality = 1;
    /// <summary>« Background » : FPS max quand la fenêtre perd le
    /// focus (5..60).</summary>
    public int BackgroundFps = 30;
    /// <summary>« Char Draw » : nombre max de personnages distants
    /// dessinés (5..100, 100 = illimité).</summary>
    public int CharDraw = 100;
    /// <summary>« Max FX » : nombre max d'effets de sort d'autrui
    /// simultanés (0..60, 60 = illimité).</summary>
    public int MaxFx = 60;
    /// <summary>« Outline » : contour (inverted hull) des modèles.</summary>
    public OutlineMode Outline = OutlineMode.Off;
    /// <summary>Special FX : Death (écran désaturé à la mort).</summary>
    public bool FxDeath = true;
    /// <summary>Special FX : Water Wave (ondulation + frames de
    /// l'eau).</summary>
    public bool FxWater = true;
    /// <summary>Special FX : 3D Effect (modèles d'effets de sorts).</summary>
    public bool Fx3D = true;
    /// <summary>Special FX : Photo (étalonnage contraste/saturation).</summary>
    public bool FxPhoto;
    /// <summary>Special FX : Glare (éblouissement : glow renforcé).</summary>
    public bool FxGlare;

    // ---------------------------------------------- interface ------
    private float _uiScale = 1f;
    /// <summary>Échelle globale UI, bornée [0.5 ; 2.0] (D3).</summary>
    public float UiScale
    {
        get => _uiScale;
        set => _uiScale = ClampUiScale(value);
    }

    /// <summary>"Irongray" = atlas de base (« Noble Irongray Style »,
    /// thème des ref-shots).</summary>
    public string Theme = "Irongray";
    /// <summary>Nom de police packagée (ex. FONT01) — "" = défaut.</summary>
    public string Font = "FONT01";
    private int _fontSize = 14;
    public int FontSize
    {
        get => _fontSize;
        set => _fontSize = Math.Clamp(value, 10, 24);
    }
    /// <summary>"en" / "cn" — placeholder (E1/E2 en cours).</summary>
    public string Language = "en";

    // --------------------------------------------------- keys ------
    /// <summary>Action → noms de touches (enum <c>Key</c> Godot ou
    /// bouton souris MouseLeft/MouseRight/MouseMiddle/MouseXButton1/2,
    /// alias Mouse4/Mouse5 — la molette n'est pas bindable).</summary>
    public readonly SortedDictionary<string, List<string>> Bindings =
        new(StringComparer.OrdinalIgnoreCase);

    // -------------------------------------------------- audio ------
    /// <summary>Volumes linéaires 0..1 (buses Master/Music/Effects).</summary>
    public bool Mute;
    public float MasterVolume = 1f;
    public float MusicVolume = 0.8f;
    public float EffectsVolume = 0.8f;

    // ==================================================================
    // Tables & helpers purs

    /// <summary>Borne officielle du scale UI (0.5–2.0, D3).</summary>
    public static float ClampUiScale(float v) =>
        Math.Clamp(v, 0.5f, 2.0f);

    /// <summary>Thèmes couleur du client natif
    /// (<c>raw_assets/themes/&lt;Theme&gt;/</c>).</summary>
    public static readonly string[] ThemeNames =
        { "Irongray", "Blue", "Coffee", "Green", "Purple", "Red" };

    /// <summary>Résolutions proposées (1024×768 → 3840×2160, D1).</summary>
    public static readonly (int W, int H)[] Resolutions =
    {
        (1024, 768), (1280, 720), (1366, 768), (1600, 900),
        (1920, 1080), (2560, 1440), (3840, 2160),
    };

    /// <summary>Limites FPS proposées (0 = illimité).</summary>
    public static readonly int[] FpsLimits = { 30, 60, 120, 0 };

    /// <summary>Table des actions bindables — libellé affiché +
    /// touches par défaut (noms d'enum Key / Mouse*). Les
    /// <c>move_*</c> sont les actions lues par PlayerController
    /// (Input.GetVector) ; les <c>eden_*</c> sont dispatchées par
    /// <see cref="SettingsApplier"/>.</summary>
    public static readonly (string Action, string Label,
        string[] Defaults)[] BindingDefs =
    {
        ("move_forward",     "Move forward",            new[] { "W", "Up" }),
        ("move_back",        "Move back",            new[] { "S", "Down" }),
        ("move_left",        "Move left",             new[] { "A", "Left" }),
        ("move_right",       "Move right",             new[] { "D", "Right" }),
        ("move_run",         "Run",             new[] { "Shift" }),
        ("eden_jump",        "Jump",             new[] { "Space" }),
        ("eden_cam_zoom_in", "Camera: zoom in",    new[] { "Pageup" }),
        ("eden_cam_zoom_out","Camera: zoom out",    new[] { "Pagedown" }),
        ("eden_inventory",   "Inventory",         new[] { "I" }),
        ("eden_profile",     "Profile",             new[] { "C" }),
        ("eden_skills",      "Skills",        new[] { "K" }),
        ("eden_quests",      "Quests",             new[] { "J" }),
        ("eden_map",         "Map",              new[] { "M" }),
        ("eden_pet",         "Pet",                new[] { "N" }),
        ("eden_sit",         "Sit / Stand",        new[] { "X" }),
        ("eden_weapon",      "Toggle WPN",         new[] { "Z" }),
        ("eden_attack",      "Attack",             new[] { "R" }),
        // Annuler le cast en vol (NCID 13) — natif : Shift+S casse
        // l'incantation. « Mod+Touche » supporté par InputBindings.
        ("eden_cancel_cast", "Cancel cast",        new[] { "Shift+S" }),
        ("eden_achievements","Achievements",       new[] { "L" }),
        ("eden_social",      "Community",          new[] { "O" }),
        ("eden_party_01",   "Party Member 1",     new[] { "F1" }),
        ("eden_party_02",   "Party Member 2",     new[] { "F2" }),
        ("eden_party_03",   "Party Member 3",     new[] { "F3" }),
        ("eden_party_04",   "Party Member 4",     new[] { "F4" }),
        ("eden_party_05",   "Party Member 5",     new[] { "F5" }),
        ("eden_party_06",   "Party Member 6",     new[] { "F6" }),
        ("eden_party_07",   "Party Member 7",     new[] { "F7" }),
        ("eden_party_08",   "Party Member 8",     new[] { "F8" }),
        ("eden_party_09",   "Party Member 9",     new[] { "F9" }),
        ("eden_party_10",   "Party Member 10",     new[] { "F10" }),
        ("eden_chat",        "Chat",               new[] { "Enter" }),
        ("eden_system",      "System menu",       new[] { "Escape" }),
        // Noms paddés (_01..) : SortedDictionary les trie en ordre.
        ("eden_slot_01",     "Hotbar slot 1",      new[] { "Key1" }),
        ("eden_slot_02",     "Hotbar slot 2",      new[] { "Key2" }),
        ("eden_slot_03",     "Hotbar slot 3",      new[] { "Key3" }),
        ("eden_slot_04",     "Hotbar slot 4",      new[] { "Key4" }),
        ("eden_slot_05",     "Hotbar slot 5",      new[] { "Key5" }),
        ("eden_slot_06",     "Hotbar slot 6",      new[] { "Key6" }),
        ("eden_slot_07",     "Hotbar slot 7",      new[] { "Key7" }),
        ("eden_slot_08",     "Hotbar slot 8",      new[] { "Key8" }),
        ("eden_slot_09",     "Hotbar slot 9",      new[] { "Key9" }),
        ("eden_slot_10",     "Hotbar slot 10",     new[] { "Key0" }),
        ("eden_slot_11",     "Hotbar slot 11",     new[] { "Minus" }),
        ("eden_slot_12",     "Hotbar slot 12",     new[] { "Equal" }),
    };

    /// <summary>Copie des bindings par défaut.</summary>
    public static SortedDictionary<string, List<string>>
        DefaultBindings()
    {
        var d = new SortedDictionary<string, List<string>>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var (a, _, defs) in BindingDefs)
            d[a] = new List<string>(defs);
        return d;
    }

    /// <summary>Touches liées à <paramref name="action"/> (copie,
    /// liste vide si absente).</summary>
    public List<string> GetBinding(string action) =>
        Bindings.TryGetValue(action, out var v)
            ? new List<string>(v) : new List<string>();

    /// <summary>Remplace les touches d'une action (créée si absente).</summary>
    public void SetBinding(string action, params string[] keys) =>
        Bindings[action] = new List<string>(keys);

    /// <summary>Recharge les bindings par défaut.</summary>
    public void ResetBindings()
    {
        Bindings.Clear();
        foreach (var (a, keys) in DefaultBindings())
            Bindings[a] = keys;
    }

    /// <summary>Applique un preset graphique sur les toggles
    /// (D5) — <see cref="Preset"/> prend ensuite la valeur choisie ;
    /// toute modif individuelle repasse <see cref="Preset"/> à
    /// <see cref="GraphicsPreset.Custom"/>.</summary>
    public void ApplyPreset(GraphicsPreset p)
    {
        Preset = p;
        switch (p)
        {
            case GraphicsPreset.Low:
                Shadows = false; ShadowMapSize = 512;
                ViewDistance = 800f;
                Antialiasing = AntialiasMode.None;
                Bloom = false; Dof = false; Ssao = false; Ssil = false;
                Fog = true; Grass = false; Particles = false;
                TextureQuality = 0; ModelQuality = 0;
                CharDraw = 20; MaxFx = 8;
                break;
            case GraphicsPreset.Medium:
                Shadows = true; ShadowMapSize = 1024;
                ViewDistance = 2000f;
                Antialiasing = AntialiasMode.Fxaa;
                Bloom = false; Dof = false; Ssao = false; Ssil = false;
                Fog = true; Grass = true; Particles = true;
                TextureQuality = 1; ModelQuality = 1;
                CharDraw = 50; MaxFx = 30;
                break;
            case GraphicsPreset.High:
                Shadows = true; ShadowMapSize = 2048;
                ViewDistance = 3000f;
                Antialiasing = AntialiasMode.Msaa4;
                Bloom = true; Dof = false; Ssao = true; Ssil = false;
                Fog = true; Grass = true; Particles = true;
                TextureQuality = 2; ModelQuality = 1;
                CharDraw = 100; MaxFx = 60;
                break;
            case GraphicsPreset.Ultra:
                Shadows = true; ShadowMapSize = 4096;
                ViewDistance = 4000f;
                Antialiasing = AntialiasMode.Msaa8;
                Bloom = true; Dof = true; Ssao = true; Ssil = true;
                Fog = true; Grass = true; Particles = true;
                TextureQuality = 2; ModelQuality = 1;
                CharDraw = 100; MaxFx = 60;
                break;
            // Custom : rien — l'état courant fait foi.
        }
    }

    /// <summary>Marque le preset « custom » après une modif manuelle
    /// d'un toggle graphique (appelé par la fenêtre).</summary>
    public void MarkCustom() => Preset = GraphicsPreset.Custom;

    // ==================================================================
    // Sérialisation INI

    /// <summary>Parse le contenu d'un settings.cfg — les clés
    /// inconnues sont ignorées (tolérant aux versions futures).</summary>
    public static ClientSettings Parse(string text)
    {
        var s = new ClientSettings();
        s.ResetBindings();
        var section = "";
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
                continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim().ToLowerInvariant();
            var value = line[(eq + 1)..].Trim();
            s.ApplyKey(section, key, value);
        }
        return s;
    }

    private void ApplyKey(string section, string key, string value)
    {
        bool B() => value.Equals("true", StringComparison.OrdinalIgnoreCase)
                    || value == "1";
        switch (section)
        {
            case "display":
                switch (key)
                {
                    case "resolution":
                        var parts = value.ToLowerInvariant().Split('x');
                        if (parts.Length == 2 &&
                            int.TryParse(parts[0], out var w) &&
                            int.TryParse(parts[1], out var h) &&
                            w > 0 && h > 0)
                        {
                            ResolutionWidth = w; ResolutionHeight = h;
                        }
                        break;
                    case "window_mode":
                        WindowMode = value.ToLowerInvariant() switch
                        {
                            "fullscreen" => EdenWindowMode.Fullscreen,
                            "borderless" => EdenWindowMode.Borderless,
                            _ => EdenWindowMode.Windowed,
                        };
                        break;
                    case "vsync": VSync = B(); break;
                    case "max_fps":
                        if (int.TryParse(value, out var f) && f >= 0)
                            MaxFps = f;
                        break;
                }
                break;
            case "graphics":
                switch (key)
                {
                    case "preset":
                        if (Enum.TryParse<GraphicsPreset>(value, true,
                                out var p))
                            ApplyPreset(p);
                        break;
                    case "shadows": Shadows = B(); break;
                    case "shadow_map_size":
                        if (int.TryParse(value, out var sz) && sz >= 256)
                            ShadowMapSize = sz;
                        break;
                    case "view_distance":
                        if (float.TryParse(value,
                                System.Globalization.CultureInfo
                                    .InvariantCulture, out var vd) &&
                            vd > 0)
                            ViewDistance = vd;
                        break;
                    case "antialiasing":
                        if (Enum.TryParse<AntialiasMode>(value, true,
                                out var aa))
                            Antialiasing = aa;
                        break;
                    case "bloom": Bloom = B(); break;
                    case "dof": Dof = B(); break;
                    case "ssao": Ssao = B(); break;
                    case "ssil": Ssil = B(); break;
                    case "fog": Fog = B(); break;
                    case "grass": Grass = B(); break;
                    case "particles": Particles = B(); break;
                    case "texture_quality":
                        if (int.TryParse(value, out var tq))
                            TextureQuality = Math.Clamp(tq, 0, 2);
                        break;
                    case "model_quality":
                        if (int.TryParse(value, out var mq))
                            ModelQuality = Math.Clamp(mq, 0, 1);
                        break;
                    case "background_fps":
                        if (int.TryParse(value, out var bf))
                            BackgroundFps = Math.Clamp(bf, 5, 60);
                        break;
                    case "char_draw":
                        if (int.TryParse(value, out var cd))
                            CharDraw = Math.Clamp(cd, 5, 100);
                        break;
                    case "max_fx":
                        if (int.TryParse(value, out var mf))
                            MaxFx = Math.Clamp(mf, 0, 60);
                        break;
                    case "outline":
                        if (Enum.TryParse<OutlineMode>(value, true,
                                out var ol))
                            Outline = ol;
                        break;
                    case "fx_death": FxDeath = B(); break;
                    case "fx_water": FxWater = B(); break;
                    case "fx_3d": Fx3D = B(); break;
                    case "fx_photo": FxPhoto = B(); break;
                    case "fx_glare": FxGlare = B(); break;
                }
                break;
            case "interface":
                switch (key)
                {
                    case "ui_scale":
                        if (float.TryParse(value,
                                System.Globalization.CultureInfo
                                    .InvariantCulture, out var us))
                            UiScale = us;   // setter = clamp 0.5–2.0
                        break;
                    case "theme": Theme = value; break;
                    case "font": Font = value; break;
                    case "font_size":
                        if (int.TryParse(value, out var fs))
                            FontSize = fs;
                        break;
                    case "language": Language = value; break;
                }
                break;
            case "keys":
                // keys.<action>=W,Up — le « key » INI est le nom de
                // l'action (move_forward, eden_inventory…).
                var keys = value.Split(',',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);
                SetBinding(key, keys);
                break;
            case "audio":
                float V() => float.TryParse(value,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var v) ? Math.Clamp(v, 0f, 1f) : -1f;
                if (key == "mute")
                {
                    Mute = value is "1" or "true" or "TRUE" or "yes";
                    break;
                }
                var vol = V();
                if (vol < 0f) break;
                switch (key)
                {
                    case "master": MasterVolume = vol; break;
                    case "music": MusicVolume = vol; break;
                    case "effects": EffectsVolume = vol; break;
                }
                break;
        }
    }

    /// <summary>Sérialise tous les paramètres (INI commenté).</summary>
    public string Serialize()
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var b = new System.Text.StringBuilder();
        b.AppendLine("# Eden Limitless — client settings");
        b.AppendLine("# Regenerated by the Settings window.");
        b.AppendLine();
        b.AppendLine("[display]");
        b.AppendLine(
            $"resolution={ResolutionWidth}x{ResolutionHeight}");
        b.AppendLine($"window_mode={WindowMode switch
        {
            EdenWindowMode.Fullscreen => "fullscreen",
            EdenWindowMode.Borderless => "borderless",
            _ => "windowed",
        }}");
        b.AppendLine($"vsync={(VSync ? "true" : "false")}");
        b.AppendLine($"max_fps={MaxFps}");
        b.AppendLine();
        b.AppendLine("[graphics]");
        b.AppendLine($"preset={Preset.ToString().ToLowerInvariant()}");
        b.AppendLine($"shadows={B(Shadows)}");
        b.AppendLine($"shadow_map_size={ShadowMapSize}");
        b.AppendLine(
            $"view_distance={ViewDistance.ToString("0.##", inv)}");
        b.AppendLine(
            $"antialiasing={Antialiasing.ToString().ToLowerInvariant()}");
        b.AppendLine($"bloom={B(Bloom)}");
        b.AppendLine($"dof={B(Dof)}");
        b.AppendLine($"ssao={B(Ssao)}");
        b.AppendLine($"ssil={B(Ssil)}");
        b.AppendLine($"fog={B(Fog)}");
        b.AppendLine($"grass={B(Grass)}");
        b.AppendLine($"particles={B(Particles)}");
        b.AppendLine($"texture_quality={TextureQuality}");
        b.AppendLine($"model_quality={ModelQuality}");
        b.AppendLine($"background_fps={BackgroundFps}");
        b.AppendLine($"char_draw={CharDraw}");
        b.AppendLine($"max_fx={MaxFx}");
        b.AppendLine($"outline={Outline.ToString().ToLowerInvariant()}");
        b.AppendLine($"fx_death={B(FxDeath)}");
        b.AppendLine($"fx_water={B(FxWater)}");
        b.AppendLine($"fx_3d={B(Fx3D)}");
        b.AppendLine($"fx_photo={B(FxPhoto)}");
        b.AppendLine($"fx_glare={B(FxGlare)}");
        b.AppendLine();
        b.AppendLine("[interface]");
        b.AppendLine($"ui_scale={UiScale.ToString("0.##", inv)}");
        b.AppendLine($"theme={Theme}");
        b.AppendLine($"font={Font}");
        b.AppendLine($"font_size={FontSize}");
        b.AppendLine($"language={Language}");
        b.AppendLine();
        b.AppendLine("[keys]");
        foreach (var (action, keys) in Bindings)
            b.AppendLine($"{action}={string.Join(',', keys)}");
        b.AppendLine();
        b.AppendLine("[audio]");
        b.AppendLine($"mute={B(Mute)}");
        b.AppendLine($"master={MasterVolume.ToString("0.##", inv)}");
        b.AppendLine($"music={MusicVolume.ToString("0.##", inv)}");
        b.AppendLine($"effects={EffectsVolume.ToString("0.##", inv)}");
        return b.ToString();

        static string B(bool v) => v ? "true" : "false";
    }

    // ==================================================================
    // Chemins & IO

    /// <summary>Charge depuis <paramref name="path"/> (défaut :
    /// résolution <see cref="ResolveLoadPath"/>). Fichier absent →
    /// défauts + bindings par défaut.</summary>
    public static ClientSettings Load(string? path = null)
    {
        path ??= ResolveLoadPath();
        try
        {
            // VFS : path peut être une URI « epk:// » (settings.cfg
            // packagé) autant qu'un chemin lâche — ReadAllText gère
            // les deux et rend null si absent.
            if (path != null &&
                Vfs.EdenVfs.ReadAllText(path) is { } text)
                return Parse(text);
        }
        catch (Exception) { /* défauts */ }
#if GODOT
        // Dernier repli : template embarqué dans le pck (export sans
        // gamedata/ à côté de l'exécutable).
        try
        {
            if (Godot.FileAccess.FileExists(ResPath))
                return Parse(Godot.FileAccess.GetFileAsString(ResPath));
        }
        catch (Exception) { /* défauts */ }
#endif
        var s = new ClientSettings();
        s.ResetBindings();
        return s;
    }

    /// <summary>Sauvegarde vers <paramref name="path"/> (défaut :
    /// <see cref="ResolveSavePath"/>) — crée le dossier si besoin.
    /// Retourne le chemin écrit, null en cas d'échec.</summary>
    public string? Save(string? path = null)
    {
        path ??= ResolveSavePath();
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, Serialize());
            return path;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Premier chemin de settings.cfg existant :
    /// <c>&lt;exe&gt;/gamedata/</c> (package) puis
    /// <c>res://gamedata/</c> (dev + template pck) — via
    /// <see cref="DataPaths"/> qui gère aussi <c>$EDEN_DATA_DIR</c>
    /// et <c>raw_assets/</c>. null hors Godot.</summary>
    public static string? ResolveLoadPath()
    {
#if GODOT
        // Ordre imposé : <exe>/gamedata d'abord.
        var exe = Path.Combine(DataPaths.ExeDir, "gamedata", FileName);
        if (File.Exists(exe)) return exe;
        var hit = DataPaths.ResolveFile(FileName);
        if (hit != null) return hit;
        var res = ProjectSettings.GlobalizePath(ResPath);
        return File.Exists(res) ? res : null;
#else
        return File.Exists(FileName) ? FileName : null;
#endif
    }

    /// <summary>Chemin d'écriture : en export, <c>&lt;exe&gt;/gamedata/
    /// </c> (éditable utilisateur) ; en éditeur, <c>res://gamedata/</c>
    /// du projet (persistant en dev).</summary>
    public static string ResolveSavePath()
    {
#if GODOT
        if (!OS.HasFeature("editor"))
            return Path.Combine(DataPaths.ExeDir, "gamedata", FileName);
        return ProjectSettings.GlobalizePath(ResPath);
#else
        return FileName;
#endif
    }
}
