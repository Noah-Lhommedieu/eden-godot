using Godot;
using EdenClient.Data;

namespace EdenClient.Hud;

/// <summary>Instance unique de <see cref="ClientUiSettings"/>
/// (<c>user://clientSettings.json</c>), chargée au premier accès.</summary>
public static class ClientUiStore
{
    private static ClientUiSettings? _cur;
    private static string FilePath =>
        ProjectSettings.GlobalizePath("user://" + ClientUiSettings.FileName);

    public static ClientUiSettings Current => _cur ??= ClientUiSettings.Load(FilePath);

    public static void Save()
    {
        try { Current.Save(FilePath); }
        catch (System.Exception e) { GD.PrintErr($"[hud] clientSettings : {e.Message}"); }
    }
}
