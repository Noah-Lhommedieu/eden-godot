namespace Eden.Zone.Codecs;

/// <summary>
/// Builders des paquets boîte magique — corps écrits à la main parce
/// que les modèles générés sont aplatis (vecteurs perdus).
///
/// Preuves byte-par-byte :
/// - `CNC_CZ_ClientMagicBoxResult::Serialize` @0x80cxxxx (zone,
///   NCID **613**) : `[s16 type][u16 n][s16 combos×n][u16 energy]
///   [u16 star][u16 n][s16 idx×n][u16 n][s16 loc×n][s16 mallBag]
///   [s16 mallSlot]`.
/// - `CNC_CZ_ClientModifyItemInfo::Serialize` @0x80cxxxx (zone,
///   NCID **369**) : `[s16 mode]` puis, si mode ≠ 8, l'enregistrement
///   item complet (ctor @0x80c8540 — mapping args→offsets vérifié) ;
///   la magic box utilise le mode -1 (0xffff) = record complet, puis
///   2 octets de queue (+0x6b,+0x6a = 0).
/// </summary>
public static class MagicBoxPackets
{
    /// <summary>Corps 613 — `CNC_CZ_ClientMagicBoxResult` natif.
    /// `combos` = vecteur magicbox_enchant1-3 du clone (s16) ;
    /// `idx`/`loc` = positions des sacrifices VALIDÉS ; `mallBag`/
    /// `mallSlot` = position du catalyseur consommé (-1 sinon).</summary>
    public static byte[] BuildMagicBoxResult(short type, short[] combos,
        ushort energy, ushort starLevel, short[] successIdx,
        short[] successLoc, short mallBag, short mallSlot)
    {
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes(type));         // s16 : type d'op
        body.AddRange(BitConverter.GetBytes((ushort)combos.Length));
        foreach (var c in combos)                           // s16 ×n : combos de l'item
            body.AddRange(BitConverter.GetBytes(c));
        body.AddRange(BitConverter.GetBytes(energy));       // u16 : énergie courante
        body.AddRange(BitConverter.GetBytes(starLevel));    // u16 : niveau d'étoile
        body.AddRange(BitConverter.GetBytes((ushort)successIdx.Length));
        foreach (var i in successIdx)                       // s16 ×n : sacs consommés
            body.AddRange(BitConverter.GetBytes(i));
        body.AddRange(BitConverter.GetBytes((ushort)successLoc.Length));
        foreach (var l in successLoc)                       // s16 ×n : slots consommés
            body.AddRange(BitConverter.GetBytes(l));
        body.AddRange(BitConverter.GetBytes(mallBag));      // s16 : sac item mall (-1)
        body.AddRange(BitConverter.GetBytes(mallSlot));     // s16 : slot item mall (-1)
        return body.ToArray();
    }

    /// <summary>Corps 369 mode -1 — `CNC_CZ_ClientModifyItemInfo`
    /// natif record complet, rempli depuis la ligne `inventory*` (tous
    /// les champs de l'item, DoMagicBox @0x80a8b90 appelle le ctor
    /// avec les champs du clone).</summary>
    /// <param name="row">Ligne SQL complète (GetItemFullRowAsync —
    /// inclut `embedded_amount` et `embedded_id1-6`).</param>
    /// <param name="containerType">GetType() natif du conteneur portant
    /// l'item — u16 du header (ctor 369 : `container-&gt;vfunc+8` ;
    /// 0 = CInventory sacs, 1 = CEquipment équipé, 4 = stockage).
    /// Ce n'est PAS `container_index` — un sac n°2 reste de type 0.
    /// </param>
    /// <param name="bag">Conteneur/slot du paquet (param_5/param_6 du
    /// handler natif — clé de conteneur + loc).</param>
    /// <param name="socketCapable">Flag « perceable » = bit 3 de
    /// clone+0x24 natif (`CItem::Clone` : `lapis+0xf6 != 0`, posé
    /// dans CWeapon/CArmor — `InlayService.IsSocketCapable(def)`).
    /// </param>
    public static byte[] BuildModifyItemInfo(
        IDictionary<string, object?> row, short containerType,
        short bag, short slot, byte socketCapable)
    {
        int I(string n) => row.TryGetValue(n, out var v) && v != null
            ? Convert.ToInt32(v) : 0;
        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes((short)-1));    // s16 : mode -1 (record complet)
        body.AddRange(BitConverter.GetBytes(
            (ushort)containerType));                        // u16 : GetType() du conteneur
        body.AddRange(BitConverter.GetBytes(bag));          // s16 : clé conteneur (param_5)
        body.AddRange(BitConverter.GetBytes((ushort)(ushort)slot));
        // u8 : flag socket-capable = bit3 de clone+0x24 natif
        // (ctor 369 : this[8] = (clone+0x24 >> 3) & 1).
        body.Add(socketCapable);
        body.AddRange(BitConverter.GetBytes(
            (short)(I("embedded_amount") & 7)));            // s16 : nb sockets (bits 0-2)
        for (var i = 1; i <= 6; i++)                        // 6×s16 : embedded_id1-6
            body.AddRange(BitConverter.GetBytes(
                (short)I($"embedded_id{i}")));
        body.AddRange(BitConverter.GetBytes(
            (short)I("witchcraft")));                       // s16 : clone+0x4c
        var maker = System.Text.Encoding.ASCII.GetBytes(
            row.TryGetValue("maker", out var m) && m is string s ? s : "");
        body.AddRange(BitConverter.GetBytes((ushort)maker.Length));
        body.AddRange(maker);                               // string : maker (clone+0x20)
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("identify")));                        // u16 : clone+0x1c
        for (var i = 1; i <= 9; i++)                        // 9×u16 : extra_combo_id1-9
            body.AddRange(BitConverter.GetBytes(
                (ushort)I($"extra_combo_id{i}")));
        body.AddRange(BitConverter.GetBytes(
            Convert.ToInt64(row.TryGetValue("color", out var c)
                && c != null ? c : 0L)));                   // i64 : color (clone+0x50)
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("train_level")));                     // u16 : train lvl
        body.AddRange(BitConverter.GetBytes(
            (uint)I("train_exp")));                         // u32 : train exp
        // CEquipTrainAttrs sérialisé ENTRELACÉ [t1][l1][t2][l2][t3][l3]
        // — Serialize 369 écrit les champs pkt+0x48..+0x52 en séquence
        // (ctor @0x80c8540 : +0x48=t1, +0x4a=l1, +0x4c=t2, +0x4e=l2,
        // +0x50=t3, +0x52=l3 ; identique à l'ordre wire du 274).
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("train_slot1_type")));                // u16 : t1
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("train_slot1_level")));               // u16 : l1
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("train_slot2_type")));                // u16 : t2
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("train_slot2_level")));               // u16 : l2
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("train_slot3_type")));                // u16 : t3
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("train_slot3_level")));               // u16 : l3
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("extra_combo_id10")));                // u16 : +0x54
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("extra_combo_id11")));                // u16 : +0x56
        body.AddRange(BitConverter.GetBytes(
            (short)I("plus_parameter")));                   // s16 : +0x58 (clone+0x2e)
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("magic")));                           // u16 : +0x5a (clone+0x6c)
        body.AddRange(BitConverter.GetBytes(
            (short)I("quality")));                          // s16 : +0x66 (clone+0x2c)
        body.AddRange(BitConverter.GetBytes(
            (short)I("magicbox_enchant1")));                // s16 : +0x5c
        body.AddRange(BitConverter.GetBytes(
            (short)I("magicbox_enchant2")));                // s16 : +0x5e
        body.AddRange(BitConverter.GetBytes(
            (short)I("magicbox_enchant3")));                // s16 : +0x60
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("magicbox_energy")));                 // u16 : +0x62 (clone+0x7c)
        body.AddRange(BitConverter.GetBytes(
            (ushort)I("magicbox_starlevel")));              // u16 : +0x64 (clone+0x7e)
        body.Add(0);                                        // u8 : +0x6b (queue)
        body.Add(0);                                        // u8 : +0x6a (queue)
        return body.ToArray();
    }

    /// <summary>369 mode 8 — bind-only, 9 octets : le paquet natif
    /// émis par `FastDressDoItemBind` @0x080b9d60 quand un item passe
    /// lié (clone+0x24 |= 0x10). Le record mode -1 ne porte PAS le
    /// flag bind — sans ce paquet dédié le client n'affiche jamais
    /// « already binded » immédiatement (bug live montures).</summary>
    public static byte[] BuildModifyItemInfoBind(short containerType,
        short containerIndex, short slot, byte bind)
    {
        var body = new List<byte>(9);
        body.AddRange(BitConverter.GetBytes((short)8));     // s16 : mode 8 (bind)
        body.AddRange(BitConverter.GetBytes(
            (ushort)containerType));                        // u16 : GetType() conteneur
        body.AddRange(BitConverter.GetBytes(containerIndex)); // s16 : index conteneur
        body.AddRange(BitConverter.GetBytes((ushort)slot)); // u16 : loc
        body.Add(bind);                                     // u8  : 1=lié, 0=délié
        return body.ToArray();
    }
}
