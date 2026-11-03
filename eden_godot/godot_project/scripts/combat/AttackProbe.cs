using Godot;

namespace EdenClient.Combat;

/// <summary>Sonde de latence du swing (--combat-test) : envoi du NCID 5, début d'animation, réception du 272.
/// Journal <c>[atkprobe]</c> en ms d'horloge moteur.</summary>
public static class AttackProbe
{
    static int s_swing;
    static ulong s_lastSend;

    public static void Swing(ulong sendMs, ulong animDoneMs, string anim)
    {
        s_lastSend = sendMs;
        GD.Print($"[atkprobe] swing#{++s_swing} t={sendMs} anim='{anim}' début_anim=+{animDoneMs - sendMs}ms (envoi+Play/décodage)");
    }

    /// <summary>EDEN_ATTACK_LEGACY=1 : comportement d'avant (pas de préchauffage, deux nombres par coup) — mesure A/B.</summary>
    public static readonly bool Legacy = System.Environment.GetEnvironmentVariable("EDEN_ATTACK_LEGACY") == "1";

    public static void Damage(int damage, int kf)
    {
        var now = Time.GetTicksMsec();
        GD.Print($"[atkprobe] 272 dmg={damage} kf={kf} t={now} depuis_envoi=+{(long)now - (long)s_lastSend}ms");
    }

    public static void Floater(int damage)
    {
        var now = Time.GetTicksMsec();
        GD.Print($"[atkprobe] FLOATER dmg={damage} t={now} depuis_envoi=+{(long)now - (long)s_lastSend}ms");
    }

    /// <summary>Décodage paresseux d'une séquence sur le thread principal (cuisson + construction Animation).</summary>
    public static void Ensure(string kf, string anim, double bakeMs, double buildMs) =>
        GD.Print($"[atkprobe] Ensure {kf}:{anim} cuisson={bakeMs:F1}ms build={buildMs:F1}ms (thread principal={(OS.GetThreadCallerId() == OS.GetMainThreadId())})");
}
