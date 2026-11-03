namespace EdenClient.Player;

/// <summary>
/// Buffer d'interpolation réseau — math pure, testable hors Godot.
///
/// Retient les derniers états (position, yaw) horodatés poussés par le
/// réseau et échantillonne l'état interpolé à un « render time » =
/// now − <see cref="RenderDelay"/> (MMO classique ~100-200 ms de retard
/// d'affichage). Entre deux updates : lerp position + lerp angulaire
/// du yaw (plus court chemin). Au-delà du plus récent : extrapolation
/// bornée à <see cref="MaxExtrapolation"/> puis clamp.
/// </summary>
public sealed class InterpBuffer
{
    /// <summary>Un état horodaté.</summary>
    public readonly struct State
    {
        public readonly double Time;
        public readonly float X, Y, Z;
        /// <summary>Cap autour de Y (radians).</summary>
        public readonly float Yaw;
        /// <summary>Destination prédite (extrémité d'un 326) : clamp à
        /// l'arrivée, JAMAIS d'extrapolation au-delà — le serveur peut
        /// avoir déjà dépassé ce point mais le client n'a pas
        /// d'autorité pour dériver plus loin (rubber-band visible à
        /// chaque fin de trajet sinon).</summary>
        public readonly bool Terminal;

        public State(double time, float x, float y, float z, float yaw,
            bool terminal = false)
        {
            Time = time;
            X = x; Y = y; Z = z; Yaw = yaw;
            Terminal = terminal;
        }
    }

    /// <summary>Retard d'affichage en secondes (0.15 par défaut).</summary>
    public double RenderDelay = 0.15;
    /// <summary>Extrapolation max au-delà du dernier état (s).</summary>
    public double MaxExtrapolation = 0.25;
    /// <summary>Fenêtre de rétention (s) — les états plus vieux que
    /// renderTime − window sont purgés.</summary>
    public double Retention = 2.0;

    private readonly List<State> _samples = new(16);

    /// <summary>Nombre d'états retenus.</summary>
    public int Count => _samples.Count;

    /// <summary>Pousse un état réseau (horodatage monotone attendu ;
    /// les états désordonnés sont insérés à leur place).</summary>
    public void Push(double time, float x, float y, float z, float yaw)
    {
        var s = new State(time, x, y, z, yaw);
        // Insertion triée (souvent à la fin).
        var i = _samples.Count;
        while (i > 0 && _samples[i - 1].Time > time) i--;
        _samples.Insert(i, s);
        // Purge.
        var cutoff = time - RenderDelay - Retention;
        var drop = 0;
        while (drop < _samples.Count && _samples[drop].Time < cutoff)
            drop++;
        if (drop > 0) _samples.RemoveRange(0, drop);
    }

    /// <summary>Écart au carré (u²) toléré entre le « cur » d'un 326
    /// et la position prédite locale : en dessous le segment repart
    /// de la position prédite, au-delà le cur serveur est honoré
    /// (vraie téléportation). 20 u — couvre toute dérive normale
    /// (trajet wander ≤ ~11 u + retard d'affichage).</summary>
    public float MaxCurCorrectionSq = 400f;

    /// <summary>Mouvement prédit natif (326 : cur→dst — le client
    /// marche lui-même de cur vers dst). Remplace toute prédiction en
    /// vol : les samples plus récents que <paramref name="now"/> sont
    /// jetés (destination précédente), puis cur@now est poussé et
    /// dst@dstTime inséré sans purge — un horodatage futur passerait
    /// la fenêtre de rétention et effacerait l'historique utile.
    /// Le cur serveur peut devancer la position prédite (le serveur
    /// C# pose dst dans sa position logique dès l'émission —
    /// MobWander) : si l'écart reste sous <see
    /// cref="MaxCurCorrectionSq"/> on repart de la position prédite à
    /// <paramref name="now"/> — sinon le modèle encore en marche
    /// « téléportait » à l'ancienne destination avant de repartir.</summary>
    public void PushMovement(double now, float cx, float cy, float cz,
        float yaw, double dstTime, float dx, float dy, float dz)
    {
        // Prédiction à now AVANT de jeter l'ancienne destination.
        if (SampleAt(now, out var px, out var py, out var pz, out _))
        {
            var ex = cx - px; var ez = cz - pz;
            if (ex * ex + ez * ez <= MaxCurCorrectionSq)
            { cx = px; cy = py; cz = pz; }
        }
        _samples.RemoveAll(s => s.Time > now);
        Push(now, cx, cy, cz, yaw);
        if (dstTime > now)
            _samples.Add(new State(dstTime, dx, dy, dz, yaw,
                terminal: true));
    }

    /// <summary>Échantillonne à renderTime = <paramref name="now"/> −
    /// <see cref="RenderDelay"/>. False si le buffer est vide.</summary>
    public bool Sample(double now, out float x, out float y,
        out float z, out float yaw)
    {
        x = y = z = yaw = 0f;
        if (_samples.Count == 0) return false;
        var rt = now - RenderDelay;
        return SampleAt(rt, out x, out y, out z, out yaw);
    }

    /// <summary>Échantillonne à un temps absolu (utile pour les tests).</summary>
    public bool SampleAt(double renderTime, out float x, out float y,
        out float z, out float yaw)
    {
        x = y = z = yaw = 0f;
        var n = _samples.Count;
        if (n == 0) return false;

        // Avant le plus vieux : clamp sur lui.
        if (renderTime <= _samples[0].Time)
            return Out(_samples[0], out x, out y, out z, out yaw);

        // Après le plus récent : extrapolation bornée depuis les 2
        // derniers états, sinon clamp.
        var last = _samples[n - 1];
        if (renderTime >= last.Time)
        {
            if (n < 2 || last.Terminal ||
                renderTime - last.Time > MaxExtrapolation)
                return Out(last, out x, out y, out z, out yaw);
            var prev = _samples[n - 2];
            var dt = last.Time - prev.Time;
            if (dt <= 0) return Out(last, out x, out y, out z, out yaw);
            var over = (float)((renderTime - last.Time) / dt);
            x = last.X + (last.X - prev.X) * over;
            y = last.Y + (last.Y - prev.Y) * over;
            z = last.Z + (last.Z - prev.Z) * over;
            yaw = last.Yaw + DeltaYaw(prev.Yaw, last.Yaw) * over;
            return true;
        }

        // Recherche de l'intervalle [i, i+1] encadrant renderTime.
        var hi = 1;
        while (hi < n - 1 && _samples[hi].Time < renderTime) hi++;
        var a = _samples[hi - 1];
        var b = _samples[hi];
        var span = b.Time - a.Time;
        var t = span > 0 ? (float)((renderTime - a.Time) / span) : 0f;
        x = a.X + (b.X - a.X) * t;
        y = a.Y + (b.Y - a.Y) * t;
        z = a.Z + (b.Z - a.Z) * t;
        yaw = a.Yaw + DeltaYaw(a.Yaw, b.Yaw) * t;
        return true;
    }

    private static bool Out(State s, out float x, out float y,
        out float z, out float yaw)
    {
        x = s.X; y = s.Y; z = s.Z; yaw = s.Yaw;
        return true;
    }

    /// <summary>Différence angulaire signée la plus courte
    /// (−π, π] de a vers b.</summary>
    public static float DeltaYaw(float a, float b)
    {
        var d = (b - a) % MathF.Tau;
        if (d > MathF.PI) d -= MathF.Tau;
        else if (d < -MathF.PI) d += MathF.Tau;
        return d;
    }
}
