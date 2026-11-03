namespace EdenClient.Scene;

/// <summary>
/// Chronologie PURE du mode cinéma client (plan futur-feature/02,
/// étape 2) : bandes noires (letterbox) qui entrent/sortent, verrou
/// d'entrée tant que la scène est active, saut possible. Pas de
/// dépendance Godot. Le rendu (<c>CinemaMode</c>) lit
/// <see cref="BarFraction"/> et <see cref="Active"/>.
/// Aucun pont réseau natif n'est prouvé (NCID 499 inconnu) : le
/// déclenchement est local (<c>--cinema-test</c>, API
/// <see cref="Begin"/>).
/// </summary>
public sealed class CinemaTimeline
{
    /// <summary>Durée d'entrée / sortie des bandes (s).</summary>
    public const double SlideSeconds = 0.5;

    private double _length;
    private double _t;
    private bool _skipped;

    /// <summary>Instance partagée lue par les verrous d'entrée.</summary>
    public static CinemaTimeline Shared { get; } = new();

    /// <summary>Vrai tant que la scène (bandes comprises) tourne :
    /// l'entrée joueur est verrouillée.</summary>
    public bool Active { get; private set; }

    /// <summary>Déclenché quand <see cref="Active"/> change.</summary>
    public event Action? Changed;

    /// <summary>Temps écoulé (s).</summary>
    public double Elapsed => _t;

    /// <summary>Démarre une scène de <paramref name="seconds"/> s
    /// (bornée à [1, 600]).</summary>
    public void Begin(double seconds)
    {
        _length = Math.Clamp(seconds, 1, 600);
        _t = 0;
        _skipped = false;
        if (!Active) { Active = true; Changed?.Invoke(); }
    }

    /// <summary>Saute la scène : les bandes sortent aussitôt.</summary>
    public void Skip()
    {
        if (!Active || _skipped) return;
        _skipped = true;
        // Repositionne le temps au début de la sortie des bandes.
        var exitStart = _length;
        if (_t < exitStart) _t = exitStart;
    }

    /// <summary>Avance l'horloge ; retourne true quand la scène vient
    /// de se terminer.</summary>
    public bool Tick(double dt)
    {
        if (!Active) return false;
        _t += Math.Max(0, dt);
        if (_t >= _length + SlideSeconds)
        {
            Active = false;
            _t = 0;
            Changed?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>Fraction d'ouverture des bandes ∈ [0,1] (lissée) :
    /// 0 = absentes, 1 = pleines.</summary>
    public double BarFraction
    {
        get
        {
            if (!Active) return 0;
            double f;
            if (_t < SlideSeconds) f = _t / SlideSeconds;
            else if (_t <= _length) f = 1;
            else f = 1 - (_t - _length) / SlideSeconds;
            f = Math.Clamp(f, 0, 1);
            return f * f * (3 - 2 * f);   // smoothstep
        }
    }

    /// <summary>Remise à zéro (changement de node / déconnexion).</summary>
    public void Reset()
    {
        var was = Active;
        Active = false; _t = 0; _skipped = false;
        if (was) Changed?.Invoke();
    }
}
