namespace Eden.Zone.Codecs;

/// <summary>
/// Paquets trésors au sol (329/330/386) — formats prouvés par les
/// <c>Serialize</c> natifs de zone/decompiled.c :
///   <c>CNC_CZ_ClientSeeTreasure::Serialize</c> @0x81e9ab0 (34 octets),
///   <c>CNC_CZ_ClientTreasureDisappear::Serialize</c> @0x81e7910 (4 o),
///   <c>CNC_CZ_ClientTreasurePickupOK::Serialize</c> @0x80c3f90 (10 o).
/// Mapping des champs SeeTreasure d'après
/// <c>CTreasure::OnSeen</c> @0x84ea090 et <c>PublicTreasure</c>
/// @0x84ea450 (quels champs du CTreasure remplissent chaque offset)
/// et le site de pickup <c>CItemFactory::PlayerPickUpTreasure</c>
/// @0x80b3130 (valeurs des champs PickupOK).
/// </summary>
public static class TreasureCodec
{
    /// <summary>329 ClientSeeTreasure — annonce un trésor posé au sol.
    /// Corps natif 34 octets (Serialize @0x81e9ab0) :
    ///   [u32] id du trésor (CTreasure+0x50)
    ///   [f32] champ +0x64 (drop-source/rot — 0 en pratique :
    ///         CreateTreasure est appelé avec p7=p8=0)
    ///   [f32] champ +0x68 (idem — 0)
    ///   [f32] x du trésor (CTreasure+0x28)
    ///   [f32] z du trésor (CTreasure+0x2c)
    ///   [u16] itemId (clone→itemdata+4 — template S_Item)
    ///   [s16] quantité (CCloneItem+0xc)
    ///   [s16] quantité (CCloneItem+0x1c — le pickupOK émet les deux)
    ///   [s16] CCloneItem+0x2c (0 pour un trésor posé)
    ///   [u8]  flag bind (clone+0x24&gt;&gt;3&amp;1 — 0 ici)
    ///   [i32] charId propriétaire (CTreasure+0x60 ; 0 = public)
    ///   [u8]  0 = vue propriétaire (OnSeen) / 1 = public
    ///         (PublicTreasure).</summary>
    public static byte[] BuildSeeTreasure(
        GroundTreasureService.Treasure t)
    {
        var b = new byte[34];
        BitConverter.GetBytes(t.Id).CopyTo(b, 0);                 // u32 id
        // f32 +0x64 / +0x68 — 0.0 (paramètres 7/8 de CreateTreasure,
        // toujours appelé avec 0).
        BitConverter.GetBytes(t.X).CopyTo(b, 12);                 // f32 x
        BitConverter.GetBytes(t.Z).CopyTo(b, 16);                 // f32 z
        BitConverter.GetBytes((ushort)t.ItemId).CopyTo(b, 20);    // u16 itemId
        BitConverter.GetBytes((short)t.Count).CopyTo(b, 22);      // s16 count
        BitConverter.GetBytes((short)t.Count).CopyTo(b, 24);      // s16 count
        BitConverter.GetBytes((short)0).CopyTo(b, 26);            // s16 (+0x2c)
        // u8 bind (index 28) — 0.
        BitConverter.GetBytes(t.OwnerCharId).CopyTo(b, 29);       // i32 owner
        b[33] = (byte)(t.IsPublic ? 1 : 0);                       // u8 public
        return b;
    }

    /// <summary>330 ClientTreasureDisappear — le trésor disparaît
    /// (ramassé ou expiré). Corps natif : [u32 id].</summary>
    public static byte[] BuildTreasureDisappear(uint treasureId) =>
        BitConverter.GetBytes(treasureId);

    /// <summary>386 ClientTreasurePickupOK — confirmation de ramassage.
    /// Corps natif (Serialize @0x80c3f90 + site d'émission
    /// <c>PlayerPickUpTreasure</c> @0x80b3a5e) :
    ///   [u32] charId du ramasseur (param+0x34 — le paquet est
    ///         NotifyClient au ramasseur lui-même)
    ///   [u16] itemId (clone→itemdata+4)
    ///   [s16] CCloneItem+0x1c (quantité)
    ///   [s16] CCloneItem+0x2c.</summary>
    public static byte[] BuildTreasurePickupOk(int charId,
        int itemId, int count)
    {
        var b = new byte[10];
        BitConverter.GetBytes(charId).CopyTo(b, 0);               // u32 charId
        BitConverter.GetBytes((ushort)itemId).CopyTo(b, 4);       // u16 itemId
        BitConverter.GetBytes((short)count).CopyTo(b, 6);         // s16 count
        BitConverter.GetBytes((short)0).CopyTo(b, 8);             // s16 (+0x2c)
        return b;
    }
}
