using Eden.Core.Game.Family;
using Eden.Core.Networking;
using Eden.Core.Networking.Handlers;
using Eden.Core.Networking.Session;
using Eden.Zone.Handlers;

namespace Eden.Zone.Features.TownDetail;

/// <summary>Opcode 9064 C→S (custom Reborn) : « détail de la ville de guilde <c>[i32 familyId]</c> ».</summary>
public sealed class CNE_CZ_ZoneServerTownDetail : NetCommand
{
    public override int Opcode => 9064;
    public override string Name => "CNE_CZ_ZoneServerTownDetail";
    public int FamilyId { get; set; }
    public override void Serialize(OutBinStream stream) => stream.WriteInt(FamilyId);
    public override void Deserialize(InBinStream stream) => FamilyId = stream.ReadInt();
}

/// <summary>Rôle d'un PNJ dans la liste de la ville (affichage uniquement).</summary>
public enum TownNpcRole : byte
{
    /// <summary>Housekeeper : PNJ d'assignation de rang 1 du bâtiment.</summary>
    Housekeeper = 1,
    /// <summary>PNJ de service posé par contrat (plans, composants, marché…).</summary>
    Service = 2,
    /// <summary>Vendeur d'outil de récolte posé près d'une aire (pioche, gants, boule).</summary>
    ToolVendor = 3,
}

/// <summary>
/// Détail PUBLIC d'une ville de guilde pour le panneau « Villes de guilde » (PLAN-ACTION-3 A4) :
/// les PNJ réellement posés (contrats du conteneur 0), que le paquet natif 502 ne transporte pas.
/// Aucune donnée privée (trésorerie, membres nominatifs) : uniquement ce qu'un visiteur voit en ville.
/// S→C 9064 : <c>[i32 familyId][u16 n]</c> puis n × <c>[i32 npcTid][s16 loc][u8 role]</c>.
/// </summary>
public static class TownDetailHandler
{
    public const ushort ResponseNcid = 9064;

    public static void Register(PacketDispatcher d, ZoneServices s) =>
        d.RegisterChar(() => new CNE_CZ_ZoneServerTownDetail(),
            async (session, msg, charId, token) =>
            {
                var rows = await s.Social.GetFamilyBuildingsAsync(msg.FamilyId, 0);
                await session.SendPacketAsync(ResponseNcid, Build(msg.FamilyId, rows), token);
            });

    /// <summary>Corps de la réponse (pur, testé) : PNJ posés + vendeurs d'outils de la ville.</summary>
    public static byte[] Build(int familyId, IEnumerable<FamilyBuildingItem> container0)
    {
        var npcs = container0.Where(r => r.ItemType is 0x2a or 0x52 && r.Tid > 0)
            .OrderBy(r => r.Loc)
            .Select(r => (r.Tid, r.Loc, Role: r.Loc % 10 == 1 ? TownNpcRole.Housekeeper : TownNpcRole.Service))
            .ToList();
        // Vendeurs d'outils : la zone les pose près des aires de récolte de toute ville ouverte.
        if (container0.Any(r => r.ItemType == 0x29))
            npcs.AddRange(FamilyCityRules.GatherAreas.Select(a => (a.VendorNpcTid, (short)0, TownNpcRole.ToolVendor)));
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(familyId);
        w.Write((ushort)npcs.Count);
        foreach (var (tid, loc, role) in npcs) { w.Write(tid); w.Write(loc); w.Write((byte)role); }
        return ms.ToArray();
    }
}
