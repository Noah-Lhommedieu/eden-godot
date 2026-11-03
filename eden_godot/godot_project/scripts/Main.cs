using Godot;

namespace EdenClient;

/// <summary>
/// Scène racine minimale du client Eden Limitless.
/// Pour l'instant : logue au démarrage et expose le point d'entrée
/// vers la couche réseau (<see cref="Session.EdenSession"/>).
/// </summary>
public partial class Main : Node3D
{
    public override void _Ready()
    {
        GD.Print("[EdenClientReborn] Main._Ready — socle Godot 4 C# OK");
    }
}
