using System.Runtime.CompilerServices;
using Eden.Core.Game.Calendar;
using Eden.Core.Game.Foundation;
using Eden.Core.Game.Modifiers;

namespace Eden.Zone.Features.Modifiers;

/// <summary>
/// Accès au moteur de modificateurs et à l'horloge de jeu depuis <see cref="ZoneServices"/>, sans
/// grossir ce record : un résolveur par instance de services (sources : config serveur, VIP, charmes).
/// </summary>
internal static class ZoneModifierExtensions
{
    private static readonly ConditionalWeakTable<ZoneServices, ModifierResolver> Resolvers = new();

    /// <summary>Horloge unique du gameplay ; les tests y posent une <see cref="FakeGameClock"/>.</summary>
    public static IGameClock Clock { get => GameTime.Clock; set => GameTime.Clock = value; }

    public static ModifierResolver Modifiers(this ZoneServices s) => Resolvers.GetValue(s, x =>
        new ModifierResolver(new IModifierSource[]
        {
            new ServerConfigModifierSource(x.ExpRate, x.ClassExpRate, x.ClassExpRate),
            new AlmanaxModifierSource(AlmanaxProvider.Calendar),
            new VipModifierSource(VipModifierSource.DefaultRules),
            new CharmModifierSource(),
        }));
}
