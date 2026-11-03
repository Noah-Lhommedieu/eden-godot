using Godot;

namespace EdenClient.Player;

/// <summary>
/// Composant d'interpolation réseau pour une entité distante
/// (<c>RemoteEntity</c>, Node3D) : applique au node parent l'état
/// échantillonné par <see cref="InterpBuffer"/> à now − render_delay.
///
/// API : <see cref="PushState"/> appelé à chaque update réseau ;
/// <see cref="_Process"/> interpole. PAS encore branché sur les
/// paquets (lot suivant) — la classe est prête.
/// </summary>
public partial class NetworkInterpolator : Node
{
    /// <summary>Retard d'affichage (secondes). 0,15 = marge contre
    /// la gigue quand deux trames 326 arrivent espacées ; la latence
    /// d'anim perçue est déjà couverte par le flag « moving » du 326
    /// (EntityRegistry → EntityAnimator.InputMoving, phase5 §4).
    /// Option envisageable : 0,08-0,10 pour rapprocher la position
    /// rendue du serveur — à ne baisser qu'après vérif live de
    /// stabilité (jitter si les trames dépassent ce délai).</summary>
    [Export] public double RenderDelay { get; set; } = 0.15;

    private readonly InterpBuffer _buffer = new();
    private Node3D? _target;

    /// <summary>Buffer sous-jacent (tests / réglages).</summary>
    public InterpBuffer Buffer => _buffer;

    public override void _Ready()
    {
        _target = GetParent<Node3D>();
        _buffer.RenderDelay = RenderDelay;
    }

    /// <summary>Pousse un état réseau (position espace Godot, yaw
    /// autour de Y, temps en secondes — horloge
    /// <see cref="Time.GetTicksUsec"/> ou serveur).</summary>
    public void PushState(Vector3 pos, float yaw, double time)
    {
        _buffer.Push(time, pos.X, pos.Y, pos.Z, yaw);
    }

    /// <summary>Tick autonome (entités hors <c>EntityRegistry</c> —
    /// VisTest). La registry appelle <see cref="Tick"/> elle-même et
    /// désactive notre process : ~260 callbacks natifs→C# séparés par
    /// frame économisés (perf S001).</summary>
    public override void _Process(double delta) => Tick(delta);

    /// <summary>Échantillonne le buffer à now − render_delay et
    /// applique pos/yaw au parent.</summary>
    public void Tick(double delta)
    {
        if (_target == null) return;
        _buffer.RenderDelay = RenderDelay;
        var now = Time.GetTicksUsec() / 1_000_000.0;
        if (!_buffer.Sample(now, out var x, out var y, out var z,
                out var yaw))
            return;
        _target.Position = new Vector3(x, y, z);
        _target.Rotation = new Vector3(0f, yaw, 0f);
    }
}
