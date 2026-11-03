using Godot;

namespace EdenClient.Hud;

/// <summary>
/// A4.5 — trait héroïque du perso (create_flag DB → paquet custom
/// Reborn S→C 9058 [u8 trait], 1..8 = enchant 52000+n). Icône
/// <c>UI/uiicon/NatrueIamge0n</c>. Reçu sur le thread réseau :
/// <see cref="Changed"/> est émis hors thread principal, les
/// abonnés re-postent (CallDeferred).
/// </summary>
public static class HeroTrait
{
    public const int Ncid = EdenClient.Protocol.HeroTraitWire.Ncid;
    private static int _trait;

    /// <summary>0 = aucun, 1..8.</summary>
    public static int Current => _trait;

    public static event Action? Changed;

    public static void Set(int trait)
    {
        // --hero-trait=N : force le trait (capture/diagnostic sans compte à trait).
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--hero-trait=") && int.TryParse(a[13..], out var f)) trait = f;
        trait = trait is >= 1 and <= 8 ? trait : 0;
        if (trait == _trait) return;
        _trait = trait;
        Changed?.Invoke();
    }

    /// <summary>Texture de la bannière (null si aucun trait / absente).</summary>
    public static Texture2D? Icon() =>
        _trait == 0 ? null
            : AssetCatalog.GetTexture($"UI/uiicon/NatrueIamge{_trait:00}.png");
}
