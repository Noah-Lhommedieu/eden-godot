using Eden.Core.Networking.Handlers;
using Eden.Zone.Features.Modifiers;
using Eden.Zone.Features.TownDetail;

namespace Eden.Zone.Features;

/// <summary>
/// Module de fonctionnalité de zone (PLAN-ACTION-3 Q8) : un fichier par fonctionnalité, enregistré en un seul endroit.
/// Tout nouveau code de gameplay est un module ; <c>ZoneEntryFlow</c> n'est plus agrandi (test d'architecture).
/// </summary>
public interface IZoneFeature
{
    /// <summary>Nom court unique (journaux, administration).</summary>
    string Name { get; }

    /// <summary>Enregistre les paquets et abonnements du module.</summary>
    void Register(PacketDispatcher dispatcher, ZoneServices services);
}

/// <summary>Registre des modules de zone : <see cref="RegisterAll"/> est appelé une fois au démarrage.</summary>
public static class ZoneFeatures
{
    public static IReadOnlyList<IZoneFeature> All { get; } = new IZoneFeature[]
    {
        new TownDetailFeature(),
        new VipStatusFeature(),
        new AlmanaxFeature(),
        new RulesFeature(),
        new Admin.AdminRpcFeature(),
    };

    public static void RegisterAll(PacketDispatcher dispatcher, ZoneServices services)
    {
        foreach (var f in All) f.Register(dispatcher, services);
    }
}

internal sealed class TownDetailFeature : IZoneFeature
{
    public string Name => "town-detail";
    public void Register(PacketDispatcher d, ZoneServices s) => TownDetailHandler.Register(d, s);
}

internal sealed class AlmanaxFeature : IZoneFeature
{
    public string Name => "almanax";
    public void Register(PacketDispatcher d, ZoneServices s) => AlmanaxHandler.Register(d, s);
}

internal sealed class VipStatusFeature : IZoneFeature
{
    public string Name => "vip-status";
    public void Register(PacketDispatcher d, ZoneServices s) => VipStatusHandler.Register(d, s);
}
