using Eden.Core.Networking;

namespace Eden.Core.Game.Classes;

/// <summary>
/// Corps du 592 `CNC_CZ_ClientPlayerClassCustomData` — wire natif
/// vérifié (`Serialize` zone @0x081fed00 + les deux sites de
/// construction : `PlayerCCDataSave::Execute` @0x08272bc0 flag=1 et
/// `QueryClassList::Execute` @0x082767d0 flag=0) :
/// <code>[u8 flag][u8 slot_actif]
/// [s16 n][u8 ×n equip_type par carte]
/// [s16 n][u8 ×n weapon_type par carte]
/// [u32 nslots][par carte : s16 len][i32 ×len sorts]</code>
/// flag=1 → la carte fraîchement sauvegardée (n=1) ; flag=0 → toutes
/// les cartes dans l'ordre (réponse QueryClassList 166).
/// </summary>
public static class CustomClassPackets
{
    /// <summary>Body flag=1 natif : la carte `slot` seule —
    /// émis à la fin de `PlayerCCDataSave::Execute`.</summary>
    public static byte[] BuildSavedSlot(byte slot, CustomClassSlot card)
        => Build(1, slot, new[] { card });

    /// <summary>Body flag=0 natif : toutes les cartes —
    /// suffixe de la réponse `QueryClassList` (166).</summary>
    public static byte[] BuildAllSlots(byte activeSlot,
        IReadOnlyList<CustomClassSlot> cards)
        => Build(0, activeSlot, cards);

    private static byte[] Build(byte flag, byte activeSlot,
        IReadOnlyList<CustomClassSlot> cards)
    {
        var s = new OutBinStream();
        s.WriteByte(flag);                    // u8 : 1 = carte sauvée / 0 = query
        s.WriteByte(activeSlot);              // u8 : slot actif (char+0xd98)
        s.WriteShort((short)cards.Count);     // s16 : n equip_type
        foreach (var c in cards)
            s.WriteByte(c.EquipType);         // u8 : EClassEquipType
        s.WriteShort((short)cards.Count);     // s16 : n weapon_type
        foreach (var c in cards)
            s.WriteByte(c.WeaponType);        // u8 : type d'arme
        // Compteur externe vec&lt;vec&lt;u32&gt;&gt; écrit en u32 par le
        // natif (`operator&lt;&lt;(uint)` @0x081fed00) — u16 ici
        // désynchronise le reader client (underflow → crash).
        s.WriteInt(cards.Count);              // u32 : nslots vec&lt;vec&gt;
        foreach (var c in cards)
        {
            s.WriteShort((short)c.Spells.Count);   // s16 : n sorts
            foreach (var sp in c.Spells)
                s.WriteInt(sp);                    // i32 : spell_id
        }
        return s.ToArray();
    }
}
