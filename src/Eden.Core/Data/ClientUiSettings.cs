using System.Text.Json;
using System.Text.Json.Serialization;

namespace EdenClient.Data;

/// <summary>Position relative (fraction du viewport, coin haut-gauche)
/// d'un HUD fixe — indépendante de la résolution.</summary>
public sealed class HudPlacement
{
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
}

/// <summary>Position absolue (pixels) d'une fenêtre HUD.</summary>
public sealed class WindowPlacement
{
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
}

/// <summary>
/// <c>clientSettings.json</c> (dossier de données utilisateur, hors
/// repo) : positions des HUDs fixes (relatives), des fenêtres
/// (absolues, clampées au chargement) et choix d'UI libres
/// (<see cref="Options"/>). Classe pure — le chemin est fourni par
/// l'appelant (Godot : <c>user://clientSettings.json</c>).
/// </summary>
public sealed class ClientUiSettings
{
    public const string FileName = "clientSettings.json";

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("huds")]
    public Dictionary<string, HudPlacement> Huds { get; set; } = new();
    [JsonPropertyName("windows")]
    public Dictionary<string, WindowPlacement> Windows { get; set; } = new();
    [JsonPropertyName("options")]
    public Dictionary<string, string> Options { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    /// <summary>Parse tolérant : JSON invalide/vide → réglages vides.</summary>
    public static ClientUiSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ClientUiSettings();
        try
        {
            var s = JsonSerializer.Deserialize<ClientUiSettings>(json, Json)
                ?? new ClientUiSettings();
            s.Huds ??= new(); s.Windows ??= new(); s.Options ??= new();
            return s;
        }
        catch (JsonException) { return new ClientUiSettings(); }
    }

    public static ClientUiSettings Load(string path)
    {
        try { return File.Exists(path) ? FromJson(File.ReadAllText(path)) : new(); }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
    }

    /// <summary>Écriture atomique (tmp + remplacement).</summary>
    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, ToJson());
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Efface positions HUD + fenêtres (garde les options).</summary>
    public void ResetPositions() { Huds.Clear(); Windows.Clear(); }

    /// <summary>Clamp d'un rectangle (pos, taille) dans le viewport :
    /// entièrement visible si possible, sinon au moins
    /// <paramref name="minVisible"/> px visibles.</summary>
    public static (float X, float Y) Clamp(float x, float y, float w,
        float h, float vpW, float vpH, float minVisible = 24f)
    {
        float cx = w <= vpW ? Math.Clamp(x, 0, vpW - w)
            : Math.Clamp(x, minVisible - w, vpW - minVisible);
        float cy = h <= vpH ? Math.Clamp(y, 0, vpH - h)
            : Math.Clamp(y, minVisible - h, vpH - minVisible);
        return (cx, cy);
    }
}
