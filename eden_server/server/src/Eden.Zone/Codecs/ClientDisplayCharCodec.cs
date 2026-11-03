using Eden.Core.Game.Inventory;

namespace Eden.Zone.Codecs;

/// <summary>
/// Builder du corps de <c>CNC_CZ_ClientDisplayChar</c> (NCID 263) —
/// fiche complète d'un personnage envoyée à l'observateur qui
/// l'inspecte. Le natif ne l'émet PAS à l'entrée en vue (le sight
/// pousse le 265 SeeChar caché via <c>CCharacter::OnSeen</c> — voir
/// <c>CNode::OnDisplayEnvironment</c> @0x084b2xx) : il est émis par
/// <c>CCharacter::OnDisplayTo(obj, full=true)</c> @0x084187e0, dont
/// le seul appelant réel est <c>CNE_CZ_ZoneServerWhoIs::Execute</c>
/// @0x082a1340 (NCID 2 — inspect/whois, param_2 = byte IsGm de la
/// requête).
///
/// Preuve : <c>CNC_CZ_ClientDisplayChar::Serialize</c> zone
/// @0x081e03f0 + ctor @0x08671230. Le corps est un record
/// compressé par bitmask (champs optionnels émis seulement s'ils
/// diffèrent de leur sentinelle chaînée) :
/// <code>
/// [u32 charId(char+0x34)]
/// [str nom(char+0x324)]
/// [f32 x][f32 z][f32 dir][f32 moveSpeed(vcall+0xd8)]
/// [u8 atkSpdR×10/15][u8 atkSpdL×10/15]
/// [4B CLifeEntityFlags(char+0x3a)]
/// [u16 level(char+0x470)]
/// [u8 classId(char+0x47c)]
/// [u16 classLevel(char+0x47e)]
/// [0x20 blob apparence(char+0x48e..0x4ae)]
/// [u8 mask] champs conditionnels :
///   bit0 i64 hp(+0x60/0x64)        — si ≠ 0
///   bit1 i64 maxHp(vcall+0xe0)     — si ≠ 0
///   bit2 i32 maxHp32(char+0x70)    — si ≠ 0
///   bit3 i32 maxMp(vcall+0xe4)     — si ≠ maxHp32 (sent. chaînée)
///   bit4 u8 (+0x41, ctor à 0)      — jamais émis
///   bit5 u8 échelle(char+0x290)    — si ≠ 100
///   bit6 u32 rideKey(char+0x7f0)   — si ≠ 0
///   bit7 u32 transformTpl(char+0x28c) — si ≠ 0 (EC_Transform)
/// [u32 mask équipement set0] bit i = slot i (0..10) présent —
///   map char+0x4fc clé 0, gate IsCanSee(slot,0) = slot&lt;11
/// [n× record item complet] — même format que le 571
///   <see cref="NativeItemCodec.WriteEquipViewItem"/>
/// [u32 mask équipement set1] — map clé 1 (seconde couche)
/// [n× record léger] [u16 itemId][i64 due_date(+0x50)]
///   [u16 gold_enchant(+0x6c)][3×s16 magicbox(vec+0x70)]
///   [u16 energy(+0x7c)][u16 star(+0x7e)] — 22 B
/// [u8 modelRev(+0x4dc)][u8 enchantRev(+0x280)][u8 familyRev(+0x4de)]
/// [u8 appellationRev(+0x4df)][u8 doRev(+0x4dd)]
/// [u32 familyId(char+0x5c)]
/// [str familyName(char+0x640 — assigné depuis SFamily+0xc)]
/// [u16 castingSpell][u16 appellationId(char+0x6fc)]
/// [u16 n][n×u32 passagers monture(char+0x7fc)]
/// [str stallTitle(char+0x7ec)]
/// [s16 territoryTeam(char+0xb74)]
/// [i32 mixElfId(char+0xbf8)]
/// [f32 floatHeight(+0x308)][f32 floatRemain(vcall+0x1a4)]
/// [f32 floatDuration(+0x30c)]
/// [s16 floatState(+0x314)]
/// [s16 versus(char+0xd8c)]
/// [i32 versusTeam(char+0xdb0)]
/// </code>
/// </summary>
public static class ClientDisplayCharCodec
{
    /// <summary>Snapshot des valeurs à sérialiser — découplé
    /// d'<c>EntryState</c> pour rester testable hors boucle.</summary>
    public readonly record struct DisplayCharSnapshot(
        uint CharId, string Name,
        float X, float Z, float Facing, float MoveSpeed,
        byte AttackSpeedLevel, byte[] Flags,
        int Level, int ClassId, int ClassLevel,
        byte[] Appearance32,
        long Hp, long MaxHp, int MaxMp,
        byte Scale290, uint RideKey, uint TransformTpl,
        ItemEntity?[] Equips0, ItemEntity?[] Equips1,
        byte ModelRev, byte EnchantRev, byte FamilyRev,
        byte AppellationRev, byte DoRev,
        uint FamilyId, string FamilyName,
        int CastingSpell, int AppellationId,
        IReadOnlyList<uint> Passengers,
        string StallTitle,
        int TerritoryTeam, int MixElfId,
        int Versus, int VersusTeam);

    /// <summary>Sérialise le corps complet du 263 dans l'ordre exact
    /// du Serialize natif (mask u8 après le blob, records d'items
    /// réutilisant <see cref="NativeItemCodec"/>).</summary>
    public static byte[] Build(in DisplayCharSnapshot s)
    {
        var ms = new MemoryStream(256);
        var w = new BinaryWriter(ms);
        w.Write(s.CharId);                     // u32 — charId (char+0x34)
        WriteStr(w, s.Name);                   // str — nom (char+0x324)
        w.Write(s.X);                          // f32 — x (char+0x28)
        w.Write(s.Z);                          // f32 — z (char+0x2c)
        w.Write(s.Facing);                     // f32 — dir (char+0x58)
        w.Write(s.MoveSpeed);                  // f32 — vcall+0xd8
        w.Write(s.AttackSpeedLevel);           // u8 — GetAttackSpeed(0)×10/15
        w.Write(s.AttackSpeedLevel);           // u8 — GetAttackSpeed(1)×10/15
        w.Write(s.Flags);                      // 4B — CLifeEntityFlags
        w.Write((ushort)s.Level);              // u16 — char+0x470
        w.Write((byte)s.ClassId);              // u8 — char+0x47c classId
        w.Write((ushort)s.ClassLevel);         // u16 — char+0x47e
        var app = s.Appearance32;
        w.Write(app.AsSpan(0,
            Math.Min(0x20, app.Length)));      // 0x20 — blob char+0x48e
        for (var i = app.Length; i < 0x20; i++)
            w.Write((byte)0);                  // pad si blob court
        // --- u8 mask + champs conditionnels (sentinelles chaînées) ---
        var mask = (byte)(
            (s.Hp != 0 ? 0x01 : 0) |
            (s.MaxHp != 0 ? 0x02 : 0) |
            ((int)s.MaxHp != 0 ? 0x04 : 0) |
            (s.MaxMp != (int)s.MaxHp ? 0x08 : 0) |
            (s.Scale290 != 100 ? 0x20 : 0) |
            (s.RideKey != 0 ? 0x40 : 0) |
            (s.TransformTpl != 0 ? 0x80 : 0));
        w.Write(mask);
        if (s.Hp != 0) w.Write(s.Hp);          // bit0 — i64 hp
        if (s.MaxHp != 0) w.Write(s.MaxHp);    // bit1 — i64 maxHp
        if ((int)s.MaxHp != 0)
            w.Write((int)s.MaxHp);             // bit2 — i32 maxHp32
        if (s.MaxMp != (int)s.MaxHp)
            w.Write(s.MaxMp);                  // bit3 — i32 maxMp
        // bit4 — u8 +0x41 : toujours 0 dans le ctor → jamais émis.
        if (s.Scale290 != 100)
            w.Write(s.Scale290);               // bit5 — u8 échelle
        if (s.RideKey != 0)
            w.Write(s.RideKey);                // bit6 — u32 rideKey
        if (s.TransformTpl != 0)
            w.Write(s.TransformTpl);           // bit7 — u32 transform
        // --- équipement set0 : mask u32 + records complets ---
        WriteEquipMaskAndItems(w, s.Equips0);
        // --- équipement set1 : mask u32 + records légers 22 B ---
        var mask1 = 0u;
        var e1 = s.Equips1;
        for (var i = 0; i < e1.Length && i < 11; i++)
            if (e1[i] != null) mask1 |= 1u << i;
        w.Write(mask1);
        for (var i = 0; i < e1.Length && i < 11; i++)
            if (e1[i] is { } it) WriteLightEquipItem(w, it);
        // --- révisions + bloc social ---
        w.Write(s.ModelRev);                   // u8 — char+0x4dc
        w.Write(s.EnchantRev);                 // u8 — char+0x280
        w.Write(s.FamilyRev);                  // u8 — char+0x4de
        w.Write(s.AppellationRev);             // u8 — char+0x4df
        w.Write(s.DoRev);                      // u8 — char+0x4dd
        w.Write(s.FamilyId);                   // u32 — char+0x5c
        WriteStr(w, s.FamilyName);             // str — char+0x640
        w.Write((ushort)s.CastingSpell);       // u16 — sort en cours
        w.Write((ushort)s.AppellationId);      // u16 — char+0x6fc
        w.Write((ushort)s.Passengers.Count);   // u16 — nb passagers
        foreach (var p in s.Passengers)
            w.Write(p);                        // u32 — handle passager
        WriteStr(w, s.StallTitle);             // str — char+0x7ec
        w.Write((short)s.TerritoryTeam);       // s16 — char+0xb74
        w.Write(s.MixElfId);                   // i32 — char+0xbf8
        w.Write(0f);                           // f32 — char+0x308
        w.Write(0f);                           // f32 — vcall+0x1a4
        w.Write(0f);                           // f32 — char+0x30c
        w.Write((short)0);                     // s16 — char+0x314
        w.Write((short)s.Versus);              // s16 — char+0xd8c
        w.Write(s.VersusTeam);                 // i32 — char+0xdb0
        return ms.ToArray();
    }

    /// <summary>Mask u32 des slots 0..10 occupés puis records
    /// <see cref="NativeItemCodec.WriteEquipViewItem"/> dans l'ordre
    /// croissant des slots (vector piVar16[0..10] natif).</summary>
    private static void WriteEquipMaskAndItems(BinaryWriter w,
        ItemEntity?[] equips)
    {
        var mask = 0u;
        for (var i = 0; i < equips.Length && i < 11; i++)
            if (equips[i] != null) mask |= 1u << i;
        w.Write(mask);
        for (var i = 0; i < equips.Length && i < 11; i++)
            if (equips[i] is { } it)
                NativeItemCodec.WriteEquipViewItem(w, it);
    }

    /// <summary>Record léger 22 B du second set (Serialize @0x081e03f0
    /// seconde boucle) : [u16 itemId][i64 due_date][u16 gold_enchant]
    /// [3×s16 magicbox][u16 energy][u16 star].</summary>
    private static void WriteLightEquipItem(BinaryWriter w,
        ItemEntity it)
    {
        w.Write((ushort)it.ItemId);            // u16 — proto+4
        w.Write((long)it.DueDate);             // i64 — clone+0x50
        w.Write((ushort)it.GoldEnchant);       // u16 — clone+0x6c
        w.Write((short)it.MagicBoxEnchant1);   // s16 — vec+0x70[0]
        w.Write((short)it.MagicBoxEnchant2);   // s16 — vec+0x70[1]
        w.Write((short)it.MagicBoxEnchant3);   // s16 — vec+0x70[2]
        w.Write((ushort)it.MagicBoxEnergy);    // u16 — clone+0x7c
        w.Write((ushort)it.MagicBoxStarLevel); // u16 — clone+0x7e
    }

    /// <summary>String lapis : u16 len + octets Big5 — même schéma
    /// que <c>OutBinStream.WriteString</c>.</summary>
    private static void WriteStr(BinaryWriter w, string value)
    {
        var bytes = Eden.Core.Data.EdenEncoding.Big5.GetBytes(
            value ?? string.Empty);
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }
}
