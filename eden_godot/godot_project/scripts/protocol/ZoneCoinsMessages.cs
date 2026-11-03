using System;
using EdenClient.Net;

namespace EdenClient.Protocol;

/// <summary>Monnaies secondaires du joueur — alimentées par les
/// paquets « complétude » (zone 576/580/555/554, monde 78 déjà
/// couvert par <see cref="PlayerProfileState"/>). Mutation thread-safe
/// + notification postée sur le thread principal.</summary>
public static class PlayerCoins
{
    public static event Action? Changed;

    private static readonly object _gate = new();

    /// <summary>NCID 576 — WarCoin (PvP/battlefield).</summary>
    public static int WarCoin { get; set; }
    /// <summary>NCID 580 — AlpacaCoin (monnaie alpaga/event).</summary>
    public static int AlpacaCoin { get; set; }
    /// <summary>NCID 555 — Lifelong gold (points cumulatifs).</summary>
    public static int LifelongGold { get; set; }
    /// <summary>NCID 554 — Lifelong point.</summary>
    public static int LifelongPoint { get; set; }

    /// <summary>Applique une monnaie unitaire (Value) — appelable
    /// depuis le thread réseau.</summary>
    public static void ApplyValue(int coin, int value)
    {
        lock (_gate)
            switch (coin)
            {
                case 576: WarCoin = value; break;
                case 580: AlpacaCoin = value; break;
                case 555: LifelongGold = value; break;
                case 554: LifelongPoint = value; break;
            }
    }

    /// <summary>Émet <see cref="Changed"/> — thread principal.</summary>
    public static void RaiseChanged() => Changed?.Invoke();
}
