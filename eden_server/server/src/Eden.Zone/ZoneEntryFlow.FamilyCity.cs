using Eden.Core.Data.Parsers;
using Eden.Core.Game.Family;
using Eden.Core.Logging;

namespace Eden.Zone;

/// <summary>
/// Ville de guilde — aires de récolte physiques (pioche / gants /
/// boule de cristal). Voir docs/temp-plans/2026-11-06-vdg-ressources.md.
///
/// Le natif n'a pas de « nœud » : l'outil (S_Item kind 44, col32
/// « 501 502;60|61|62 ») s'utilise en clic droit dans une event_area
/// GATEWAY dont la keyId (col4 de [gateway_area]) vaut 60/61/62 —
/// <c>CEC_AttrGainCollectItem::CanExecute</c> @0x085056b0, déjà
/// porté dans ZoneEntryFlow.Items.cs. Les vendeurs d'outils natifs
/// (S_Npc 53199/53200/53201) ne sont posés qu'à Aven (S001 [npc]
/// -143/-131/-132) : déviation EDENFR assumée, ils sont injectés près
/// de chaque aire pour que l'outil soit achetable sur place.
/// </summary>
public sealed partial class ZoneEntryFlow
{
    /// <summary>Injecte les vendeurs d'outils au centre de chaque aire
    /// de récolte de la scène du hall.</summary>
    private void InjectHallToolVendors(EntryState st,
        SceneParser.Scene scene)
    {
        foreach (var area in FamilyCityRules.GatherAreas)
        {
            var gw = scene.Gateways.FirstOrDefault(
                g => g.KeyId == area.KeyId);
            var ev = gw == null ? null : scene.EventAreas
                .FirstOrDefault(e => e.Id == gw.EventAreaId);
            if (ev == null)
            {
                EdenLog.Write($"[VDG-Gather] node={st.NodeId} aire " +
                    $"key={area.KeyId} absente de la scène");
                continue;
            }
            if (!_services.GameData.Npcs.Any(n => n.Id == area.VendorNpcTid))
            {
                EdenLog.Write($"[VDG-Gather] vendeur {area.VendorNpcTid} " +
                    "absent de S_Npc");
                continue;
            }
            var h = InjectHallNpc(st, area.VendorNpcTid, ev.X, ev.Y, ev.Z,
                0);
            EdenLog.Write($"[VDG-Gather] node={st.NodeId} {area.Label} : " +
                $"aire key={area.KeyId} centre=({ev.X:F0},{ev.Y:F0}) " +
                $"r={ev.Radius:F0} ; vendeur npc={area.VendorNpcTid} " +
                $"handle=0x{h:x8}");
        }
    }

    /// <summary>Outil de récolte utilisable ici : node de la colonne 32
    /// de S_Item et joueur dans l'event_area GATEWAY de keyId
    /// <c>UseAreaSubType</c> (rayon + 2). Test serveur approximatif :
    /// le natif ne teste aucune position dans l'effet.</summary>
    private bool IsInCollectArea(EntryState st, int toolId)
    {
        var toolDef = _services.GameData.Items
            .FirstOrDefault(i => i.Id == toolId);
        if (toolDef == null) return true;
        if (toolDef.UseAreaNodeIds.Length > 0 &&
            !toolDef.UseAreaNodeIds.Contains(st.NodeId)) return false;
        if (toolDef.UseAreaSubType <= 0) return true;
        var sc = _services.Scenes.GetScene($"S{st.NodeId:D3}");
        return sc != null && sc.Gateways.Any(g =>
            g.KeyId == toolDef.UseAreaSubType &&
            sc.EventAreas.Any(e => e.Id == g.EventAreaId &&
                MathF.Sqrt((e.X - st.PosX) * (e.X - st.PosX)
                    + (e.Y - st.PosZ) * (e.Y - st.PosZ))
                    <= e.Radius + 2f));
    }
}
