using Eden.Core.Game.Inventory;

namespace Eden.Zone.Codecs;

/// <summary>
/// Codec item natif lapis — décodé de
/// lapis::CNC_CZ_ClientEnterNodeInventory::Serialize @0x8233xxx (zone/decompiled.c ~402130)
/// et lapis::CNC_CZ_ClientPutItemIntoInventory::Serialize @0x80e3xxx (~57926).
/// Validé byte-pour-byte sur captures/zsniff_native_entry.bin (5 items, parse aligné).
/// </summary>
public static class NativeItemCodec
{
    /// <summary>Item de conteneur externe (banque/famille) — sous-ensemble
    /// des champs d'ItemEntity requis par le codec 358.</summary>
    public sealed record StorageItem(int Slot, int ItemId, int Durability,
        int MaxDurability, bool IsBound, int EnchantLevel, int CreateTime,
        long DueDate = 0);


    /// <summary>
    /// Enregistrement item (format 312/274/358) — sérialiseur natif
    /// CNC_CZ_ClientPutItemIntoInventory::Serialize @0x80e3xxx :
    ///   [u16 itemId][s16 f1][s16 f2](+274 : 2 s16 sac/slot)
    ///   [u16 slen][str][u8 flags][u24 mask][cond][34B tail].
    /// f1/f2 : durabilité courante/max (paires égales dans la capture).
    /// flags = SCloneItemFlags clone+0x24 : bits0-2 = nb sockets
    ///   ouverts, bit3 = socket-capable, bit6 = bind (0x48 capturé).
    /// mask — chaque bit = un champ conditionnel, émis dans l'ordre
    /// seulement si ≠ sentinelle (natif lit une s16 par bit puis
    /// l'inclut à postériori) :
    ///   bits0-5  : 6×s16 embedded_id1-6   — sentinelle -1
    ///   bit6     : s16 embedded_amount     — sentinelle 0
    ///   bit7     : s16 combo_id (+0x10)    — sentinelle 0
    ///   bit8     : u32 create_time (+0x18) — sentinelle 0
    ///   bit9     : u16 clone+0x1c          — sentinelle 0 (jamais émis)
    ///   bits10-18: 9×u16 extra_combo_id1-9 — sentinelle 0
    ///   bit19    : s16 strengthen (+0x2c)  — sentinelle 0
    ///   bit20    : s16 clone+0x2e          — sentinelle 0 (jamais émis)
    ///   bit21    : i64 due_date            — si dword haut|bas ≠ 0
    /// </summary>
    /// <param name="emitTail">Émet la queue fixe 34B (train/combo10-11/
    /// magicbox) — les records du 424 EnterNodeElfContainer s'arrêtent
    /// après le mask+conditionnels (désasm Serialize @0x8230030 :
    /// aucune queue après le bit21).</param>
    public static void WriteItem(BinaryWriter w, int itemId, short f1, short f2,
        byte flags, short enchant, int createTime = 0, long dueDate = 0,
        ItemEntity? meta = null, bool emitTail = true)
    {
        w.Write((ushort)itemId);  // u16 — id d'item (S_Item)
        w.Write(f1);              // s16 — paire avec f2 (durabilité/qté)
        w.Write(f2);              // s16 — paire avec f1
        w.Write((ushort)0);       // u16 len + chars — string item+0x20 (vide)
        // u8 flags : count de sockets en bits 0-2 + bit3 = identifié
        // (clone+0x24 natif — IdentifyItem : `clone+0x24 |= 8`).
        // meta présent → meta.Identify fait foi ; sinon byte appelant.
        var flagByte = (byte)(flags | (byte)((meta?.EmbeddedAmount ?? 0) & 7));
        if (meta != null)
            flagByte = (byte)((flagByte & ~8) | (meta.Identify != 0 ? 8 : 0));
        w.Write(flagByte);
        // u24 mask — calculé champ par champ comme le natif.
        uint mask = 0;
        if (meta != null)
        {
            for (var i = 0; i < 6; i++)
                if (meta.GetEmbeddedId(i) != -1) mask |= 1u << i;
            if (meta.EmbeddedAmount != 0) mask |= 0x40u;
            if (meta.Combo != 0) mask |= 0x80u;
        }
        if (createTime != 0) mask |= 0x000100u;
        if (meta != null)
            for (var i = 0; i < 9; i++)
                if (meta.ExtraCombos[i] != 0) mask |= 1u << (10 + i);
        if (enchant != 0) mask |= 0x080000u;
        if (dueDate != 0) mask |= 0x200000u;
        w.Write((byte)(mask & 0xff));
        w.Write((byte)(mask >> 8 & 0xff));
        w.Write((byte)(mask >> 16 & 0xff));
        // Champs conditionnels — ordre strict du sérialiseur natif.
        if (meta != null)
        {
            for (var i = 0; i < 6; i++)
            {
                var id = meta.GetEmbeddedId(i);
                if (id != -1)
                    w.Write((short)id);   // bits0-5 — s16 embedded_idN
            }
            if (meta.EmbeddedAmount != 0)
                w.Write((short)meta.EmbeddedAmount); // bit6 — s16
            if (meta.Combo != 0)
                w.Write((short)meta.Combo);          // bit7 — s16 combo
        }
        if (createTime != 0)
            w.Write((uint)createTime);    // bit8 — u32 clone+0x18
        if (meta != null)
            for (var i = 0; i < 9; i++)
                if (meta.ExtraCombos[i] != 0)
                    w.Write((ushort)meta.ExtraCombos[i]); // bits10-18 — u16
        if (enchant != 0)
            w.Write(enchant);             // bit19 — s16 strengthen
        if (dueDate != 0)
            w.Write(dueDate);             // bit21 — i64 due_date
        // 34B de queue fixe — ordre exact de
        // ClientPutItemIntoInventory::Serialize @0x80e3xxx
        // (zone/decompiled.c ~58030) + ctor @0x80c8800 (site d'appel
        // CItemFactory::AddCloneItemWorld @0x809c454) :
        //   CEquipTrainAttrs (clone+0x58) — pkt+0x4c..+0x5e, ordre
        //   wire entrelacé [t1][l1][t2][l2][t3][l3] ;
        //   puis [ec10][ec11][gold_enchant(+0x6c)]
        //   [magicbox_enchant1..3 (vecteur clone+0x70, s16)]
        //   [magicbox_energy(+0x7c)][magicbox_starlevel(+0x7e)].
        if (emitTail)
            WriteItemTail(w, meta);
    }

    /// <summary>Queue fixe 34B commune aux records item chaînés
    /// (274/312/358/571) — voir <see cref="WriteItem"/> pour la
    /// provenance du mapping.</summary>
    private static void WriteItemTail(BinaryWriter w, ItemEntity? meta)
    {
        w.Write((ushort)(meta?.TrainLevel ?? 0));       // u16 — attrs+0x00 train_level
        w.Write((uint)(meta?.TrainExp ?? 0));           // u32 — attrs+0x04 train_exp
        w.Write((ushort)(meta?.TrainSlot1Type ?? 0));   // u16 — pkt+0x54 ← attrs+0x08
        w.Write((ushort)(meta?.TrainSlot1Level ?? 0));  // u16 — pkt+0x5a ← attrs+0x0e
        w.Write((ushort)(meta?.TrainSlot2Type ?? 0));   // u16 — pkt+0x56 ← attrs+0x0a
        w.Write((ushort)(meta?.TrainSlot2Level ?? 0));  // u16 — pkt+0x5c ← attrs+0x10
        w.Write((ushort)(meta?.TrainSlot3Type ?? 0));   // u16 — pkt+0x58 ← attrs+0x0c
        w.Write((ushort)(meta?.TrainSlot3Level ?? 0));  // u16 — pkt+0x5e ← attrs+0x12
        w.Write((ushort)(meta?.ExtraComboId10 ?? 0));   // u16 — vec+0x34[9]
        w.Write((ushort)(meta?.ExtraComboId11 ?? 0));   // u16 — vec+0x34[10]
        w.Write((ushort)(meta?.GoldEnchant ?? 0));      // u16 — clone+0x6c
        w.Write((short)(meta?.MagicBoxEnchant1 ?? 0));  // s16 — vec70[0]
        w.Write((short)(meta?.MagicBoxEnchant2 ?? 0));  // s16 — vec70[1]
        w.Write((short)(meta?.MagicBoxEnchant3 ?? 0));  // s16 — vec70[2]
        w.Write((ushort)(meta?.MagicBoxEnergy ?? 0));   // u16 — clone+0x7c
        w.Write((ushort)(meta?.MagicBoxStarLevel ?? 0));// u16 — clone+0x7e
    }

    /// <summary>
    /// Record item « vue d'équipement » du NCID 571 — désassemblage de
    /// lapis::CNC_CZ_ClientPVPRankLookEquip::Serialize @0x8205af0
    /// (zone/decompiled.c ~356916) : même famille chaînée que
    /// <see cref="WriteItem"/> mais SANS create_time et avec le mask
    /// patché u24 en place-réservée (le natif réserve 3 octets, émet
    /// les champs puis réécrit le mask) :
    ///   [u16 itemId (def+8→+4)][u8 clone+0x24 &amp; 7 (sockets)]
    ///   [u16 len + string clone+0x20]
    ///   [u24 mask][champs émis si ≠ sentinelle, ordre natif] :
    ///     bits0-5  : 6×s16 vec clone+0x40 (embedded_idN, sent. -1)
    ///     bit6     : s16 clone+0x4c (embedded_amount, sent. 0)
    ///     bit7     : s16 clone+0x10 (combo_id, sent. 0)
    ///     bit8     : u16 clone+0x1c (sent. 0 — non mappé côté DB)
    ///     bits9-17 : 9×u16 vec clone+0x34[0..8] (extra combos, sent. 0)
    ///     bit18    : u16 clone+0x2c (strengthen, sent. 0)
    ///     bit19    : s16 clone+0x2e (sent. 0 — non mappé côté DB)
    ///     bit20    : i64 clone+0x50 (due_date, sent. 0)
    ///   [34B queue fixe <see cref="WriteItemTail"/>].
    /// </summary>
    public static void WriteEquipViewItem(BinaryWriter w, ItemEntity item)
    {
        w.Write((ushort)item.ItemId);        // u16 — id d'item (S_Item)
        WriteEquipViewItemBody(w, item);
    }

    /// <summary>Corps commun des records « vue d'équipement »
    /// (571/263) et du record delta du 276 — désassemblage de
    /// <c>CNC_CZ_ClientChangeEquip::Serialize</c> @0x843exxx : mêmes
    /// [u8 flags][str][u24 mask chaîné][34B tail]. <paramref name="item"/>
    /// nul = slot vidé (flags 0xff, mask 0, tail à 0 — branche
    /// <c>iVar32 == 0</c> du ctor <c>NotifyChangeEquipment</c>).</summary>
    private static void WriteEquipViewItemBody(BinaryWriter w,
        ItemEntity? item)
    {
        // u8 — clone+0x24 & 7 natif = nb de sockets ouverts (bits 0-2),
        // comme le record 282 : ni bind ni identify transmis ici.
        // 0xff quand le slot est vide (ctor branche vide, pkt+0xf).
        w.Write(item == null ? (byte)0xff
            : (byte)(item.EmbeddedAmount & 7));
        w.Write((ushort)0);                  // u16 len — string clone+0x20 vide
        // u24 mask — ordre bit natif du Serialize 571.
        uint mask = 0;
        if (item != null)
        {
            for (var i = 0; i < 6; i++)
                if (item.GetEmbeddedId(i) != -1) mask |= 1u << i;
            if (item.EmbeddedAmount != 0) mask |= 0x40u;   // bit6 — clone+0x4c
            if (item.Combo != 0) mask |= 0x80u;            // bit7 — clone+0x10
            // bit8 (0x100) = u16 clone+0x1c — jamais émis (sent. 0,
            // pas de colonne DB correspondante).
            for (var i = 0; i < 9; i++)
                if (item.ExtraCombos[i] != 0)
                    mask |= 1u << (9 + i);              // bits9-17 — vec+0x34
            if (item.EnchantLevel != 0) mask |= 0x40000u; // bit18 — clone+0x2c
            // bit19 (0x80000) = s16 clone+0x2e — jamais émis (sent. 0).
            if (item.DueDate != 0) mask |= 0x100000u;    // bit20 — i64 +0x50
        }
        w.Write((byte)(mask & 0xff));          // mask octet 0
        w.Write((byte)(mask >> 8 & 0xff));     // mask octet 1
        w.Write((byte)(mask >> 16 & 0xff));    // mask octet 2
        if (item == null)
        {
            WriteItemTail(w, null);            // 34B — queue fixe à 0
            return;
        }
        // Champs conditionnels — ordre strict du sérialiseur natif.
        for (var i = 0; i < 6; i++)
        {
            var id = item.GetEmbeddedId(i);
            if (id != -1)
                w.Write((short)id);            // bits0-5 — s16 embedded_idN
        }
        if (item.EmbeddedAmount != 0)
            w.Write((short)item.EmbeddedAmount); // bit6 — s16 +0x4c
        if (item.Combo != 0)
            w.Write((short)item.Combo);          // bit7 — s16 +0x10 combo
        // bit8 — u16 clone+0x1c omis (sent. 0).
        for (var i = 0; i < 9; i++)
            if (item.ExtraCombos[i] != 0)
                w.Write((ushort)item.ExtraCombos[i]); // bits9-17 — u16
        if (item.EnchantLevel != 0)
            w.Write((ushort)item.EnchantLevel);  // bit18 — u16 strengthen
        // bit19 — s16 clone+0x2e omis (sent. 0).
        if (item.DueDate != 0)
            w.Write((long)item.DueDate);         // bit20 — i64 +0x50
        WriteItemTail(w, item);                  // 34B — queue fixe
    }

    /// <summary>
    /// Corps du NCID 276 <c>CNC_CZ_ClientChangeEquip</c> — delta d'un
    /// slot d'équipement broadcasté aux observateurs par
    /// <c>CCharacter::NotifyChangeEquipment</c> @0x843df10
    /// (<c>BroadCastToCanSee</c>, soi exclu) depuis
    /// <c>CItemFactory::Equip/UnEquip/EquipForSwitch/UnEquipForSwitch/
    /// EquipNoCheck/DropItem</c>. Serialize @0x843exxx :
    ///   [i32 charId(+4)][u16 slot(+8)][u16 tid(+0xa, 0 si vidé)]
    ///   [s16 conteneur(+0xc)][i8 rev char+0x4dc(+0xe)][u8 flags(+0xf)]
    ///   [str][u24 mask chaîné][34B tail] — corps identique à
    ///   <see cref="WriteEquipViewItemBody"/>.
    /// </summary>
    /// <param name="slot">Loc d'équipement (param_1 &amp; 0xff).</param>
    /// <param name="container">Clé du conteneur équipement
    /// (param_2 — 0 tenu, 1 pet, 2-4 pages, 5 glyphes).</param>
    /// <param name="rev">Révision d'équipement — char+0x4dc incrémenté
    /// à chaque appel natif (<see cref="ZoneServices.ModelVersions"/>).</param>
    /// <param name="item">Item désormais au slot, null = vidé.</param>
    public static byte[] BuildChangeEquip(int charId, ushort slot,
        short container, byte rev, ItemEntity? item)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(charId);                     // i32 — handle char+0x34
        w.Write(slot);                       // u16 — pkt+0x08 loc
        w.Write((ushort)(item?.ItemId ?? 0));// u16 — pkt+0x0a proto+4 (0 si vidé)
        w.Write(container);                  // s16 — pkt+0x0c param_2
        w.Write((sbyte)rev);                 // i8  — pkt+0x0e char+0x4dc
        WriteEquipViewItemBody(w, item);     // flags/str/mask/champs/tail
        return ms.ToArray();
    }

    /// <summary>Record léger du 2ᵉ set du NCID 571 — Serialize @0x8205af0
    /// seconde boucle : `[u16 itemId][i64 clone+0x50][3×s16 vec+0x70]
    /// [u16 +0x7c][u16 +0x7e]` = 20B par item.</summary>
    public static void WriteLightItem(BinaryWriter w, ItemEntity item)
    {
        w.Write((ushort)item.ItemId);        // u16 — id d'item
        w.Write((long)item.DueDate);         // i64 — clone+0x50 due_date
        w.Write((short)item.MagicBoxEnchant1); // s16 — vec70[0]
        w.Write((short)item.MagicBoxEnchant2); // s16 — vec70[1]
        w.Write((short)item.MagicBoxEnchant3); // s16 — vec70[2]
        w.Write((ushort)item.MagicBoxEnergy);  // u16 — clone+0x7c
        w.Write((ushort)item.MagicBoxStarLevel); // u16 — clone+0x7e
    }

    /// <summary>
    /// Corps NCID 312 ClientEnterNodeInventory — désassemblage de
    /// `CNC_CZ_ClientEnterNodeInventory::Serialize`
    /// (zone/decompiled.c:402130) :
    ///   [u8 nBags] { [u16 capacity][u8 containerIdx][s16 bmpLen]
    ///                [bitmap] {items} } ×nBags
    ///   [u8 nHolders] { [u8 holderIdx][u16 nSlots] { par slot :
    ///     u16 itemId (0=vide) ; si occupé : string maker, s16
    ///     clone+0xc, u8 flags(+0x24) } } }
    /// Chaque conteneur ouvert et ses items sont émis ensemble ; ne
    /// jamais annoncer une capacité tout en omettant les records du sac.
    /// Les sacs équipés (table `bags`, holder 0 = inv) sont émis dans
    /// le bloc holder 0 — le client ouvre l'onglet inv N avec la
    /// capacité lue dans sa S_Item (col28). Holder 4 = sacs banque,
    /// toujours vide ici.</summary>
    public static byte[] BuildEnterNodeInventory(
        IReadOnlyList<ItemEntity> items,
        IReadOnlyDictionary<int, int> capacities,
        IReadOnlyList<Eden.Core.Database.Repositories.EquippedBag> equippedBags)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var maxBag = items.Where(i => i.BagId >= 0 && i.BagId <= 4)
            .Select(i => i.BagId)
            .Concat(capacities.Where(kv => kv.Value > 0)
                .Select(kv => kv.Key).Where(k => k <= 4))
            .Concat(equippedBags.Where(b => b.HolderIndex == 0)
                .Select(b => b.Loc))
            .DefaultIfEmpty(0).Max();
        w.Write((byte)(maxBag + 1));          // u8 nBags (sacs contigus 0..max)
        for (var bagId = 0; bagId <= maxBag; bagId++)
        {
            var capacity = capacities.TryGetValue(bagId, out var c)
                ? c : (bagId == 0 ? 32 : 0); // sac0 = 32 (capture native 312)
            w.Write((ushort)capacity);        // u16 — nb de slots du sac
            // u8 — index du conteneur = clé de la map CInventory
            // (char+0x4e0) : ctor 0x08234520 pousse `*(short*)(node+0x10)`
            // dans le vecteur émis ici (Serialize : `*(char*)(this+0x14+i)`).
            // Émettre une constante fait router toutes les sections vers
            // le même conteneur client → sac1 peint par-dessus sac0.
            w.Write((byte)bagId);
            var blobLen = (capacity + 7) / 8;
            var blob = new byte[blobLen];
            var bag = items.Where(i => i.BagId == bagId &&
                    i.Slot >= 0 && i.Slot < capacity)
                .OrderBy(i => i.Slot).ToList();
            // Bitmap MSB-first : slot i → bit 0x80>>(i%8) — capture test1
            // `ffc00000` = slots 0-9 contigus (l'ordre LSB aurait donné
            // `ff07` et décalé les items aux slots 14-15).
            foreach (var i in bag) blob[i.Slot / 8] |= (byte)(0x80 >> (i.Slot & 7));
            w.Write((short)blobLen);          // s16 — taille du bitmap
            w.Write(blob);                    // bitmap slot-occupancy
            foreach (var i in bag)
                // f1 = durabilité courante, f2 = max courant (paires
                // égales dans la capture), flags 0x08/0x18 (bit4=bind), create_time.
                // meta → sockets incrustés + combos (player_works).
                WriteItem(w, i.ItemId, (short)i.Durability,
                    (short)i.MaxDurability,
                    (byte)(i.IsBound ? 0x18 : 0x08), (short)i.EnchantLevel,
                    i.CreateTime, i.DueDate, i);
        }
        // Blocs CBagContainer — désasm Serialize : u8 nHolders puis
        // { u8 contIdx, u16 nSlots, per slot u16 itemId (0=vide) +
        // [string maker][s16 +0xc][u8 flags] si occupé }.
        w.Write((byte)2);                     // u8 — 2 porte-sacs (0 inv, 4 banque)
        WriteBagHolder(w, equippedBags, 0);
        WriteBagHolder(w, equippedBags, 4);
        return ms.ToArray();
    }

    /// <summary>Un bloc porte-sacs du 312 — holder index + 5 slots :
    /// par slot `u16 itemId` (0 = vide), et si occupé la string maker,
    /// clone+0xc (capacité du sac = col28) et les flags +0x24.</summary>
    private static void WriteBagHolder(BinaryWriter w,
        IReadOnlyList<Eden.Core.Database.Repositories.EquippedBag> bags,
        int holderIdx)
    {
        w.Write((byte)holderIdx);             // u8 — index du porte-sacs
        w.Write((ushort)5);                   // u16 — 5 slots natifs (0-4)
        for (var loc = 0; loc < 5; loc++)
        {
            var bag = bags.FirstOrDefault(
                b => b.HolderIndex == holderIdx && b.Loc == loc);
            if (bag == null)
            {
                w.Write((ushort)0);           // u16 — slot vide
                continue;
            }
            w.Write((ushort)bag.ItemId);      // u16 — template du sac
            var maker = System.Text.Encoding.GetEncoding("big5")
                .GetBytes(bag.Maker ?? string.Empty);
            w.Write((ushort)maker.Length);    // u16 — len string maker
            w.Write(maker);                   // maker brut
            w.Write((short)bag.Capacity);     // s16 — clone+0xc (capacité sac)
            // u8 flags +0x24 — bits bind/identify comme WriteItem.
            byte f = 0x08;
            if (bag.Bind != 0) f |= 0x10;
            w.Write(f);
        }
    }

    /// <summary>
    /// Corps NCID 311 ClientEnterNodeEquipAndSpell — désassemblé
    /// `lapis::CNC_CZ_ClientEnterNodeEquipAndSpell::Serialize`
    /// (zone/decompiled.c ~400262) + capture test3 décodée octet par
    /// octet :
    ///   [u8 nContainers]                   — map char+0x4fc, 6 clés 0..5
    ///   par conteneur (ordre de clé) :
    ///     [u8 key][u32 slotMask]           — bit k = loc k occupé
    ///     puis un item-record par bit levé, dans l'ordre croissant :
    ///       [u16 itemId][s16 f1][s16 f2][u16 slen+str maker]
    ///       [u8 flags][u24 mask][champs conditionnels][34B queue]
    ///       — même codec que le 312 (<see cref="WriteItem"/>)
    ///   [s16 nSpells] puis n × [s16 spellmaster][u8 spell_level]
    ///   (Serialize @0x8231xxx : `*psVar14` = smId u16,
    ///   `(char)psVar14[0x10]` = spellmaster+0x20 = niveau réel —
    ///   `spell_order` en DB = position de raccourci, PAS le niveau).
    /// `equipped` = lignes de la vue `equipment` (container_index =
    /// clé de conteneur, loc = bit du mask). `loc` est déjà canonisé
    /// par `NormalizeEquipmentSlotsAsync` avant l'entrée. La capture
    /// test3 portait SES items (31939@0:2 …) : rejouer sa tête affichait
    /// l'équipement du perso capturé — panneau profil/types d'armure faux
    /// pour tout autre perso.
    /// </summary>
    public static byte[] BuildEnterNodeEquipAndSpell(
        IReadOnlyList<ItemEntity> equipped,
        IReadOnlyList<(short SpellMaster, short Level)> spells)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)6);                     // u8 — nb de conteneurs
                                              // (6 clés natives 0..5)
        for (var key = 0; key < 6; key++)
        {
            // Déduplique par loc (buffers de save non permutés — même
            // convention que BuildUpdateEquipment : première ligne).
            var group = equipped
                .Where(e => e.BagId == key && e.Slot >= 0 && e.Slot < 32)
                .GroupBy(e => e.Slot).Select(g => g.First())
                .OrderBy(e => e.Slot).ToList();
            uint mask = 0;
            foreach (var e in group) mask |= 1u << e.Slot;
            w.Write((byte)key);               // u8 — clé EClassEquipType
            w.Write(mask);                    // u32 — bitmap des locs
            foreach (var e in group)
                WriteItem(w, e.ItemId, (short)e.Durability,
                    (short)e.MaxDurability,
                    (byte)(e.IsBound ? 0x18 : 0x08),
                    (short)e.EnchantLevel, e.CreateTime, e.DueDate, e);
        }
        w.Write((short)spells.Count);         // s16 — nb de sorts connus
        foreach (var (sm, level) in spells)
        {
            w.Write(sm);                      // s16 — spellmaster signé
            w.Write((byte)Math.Max(level, (short)1));
                                              // u8  — spell_level (natif
                                              // +0x20 du spellmaster)
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Corps NCID 358 ClientGetStorageData (réponse au 62) — désassemblé
    /// lapis::CNC_CZ_ClientGetStorageData::Serialize @0x82366b0 :
    ///   [s16 type]                        — echo du champ s16 de la requête 62
    ///   [u8 nContainers]                  — map&lt;s16, CStorage*&gt; char+0x510
    ///   par conteneur : [s16 capacity][u8 flag][s16 blobLen][bitmap]
    ///                   puis un item-record par bit levé (même codec que
    ///                   le 312 : [u16 id][s16][s16][str][u8][u24 mask]
    ///                   [cond][34B tail]).
    /// Le bitmap est MSB-first comme le 312 (même famille IItemContainer).
    /// </summary>
    public static byte[] BuildGetStorageData(short type,
        IReadOnlyList<(short Capacity, byte Flag,
            IReadOnlyList<StorageItem> Items)> containers)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(type);                         // s16 — champ de la requête
        w.Write((byte)containers.Count);       // u8 — nb de conteneurs
        foreach (var (cap, flag, items) in containers)
        {
            w.Write(cap);                      // s16 — capacité du conteneur
            w.Write(flag);                     // u8 — flag conteneur
            var blobLen = (cap + 7) / 8;
            var blob = new byte[blobLen];
            var inBag = items.Where(i => i.Slot >= 0 && i.Slot < cap)
                .OrderBy(i => i.Slot).ToList();
            foreach (var i in inBag)
                blob[i.Slot / 8] |= (byte)(0x80 >> (i.Slot & 7));
            w.Write((short)blobLen);           // s16 — taille bitmap
            w.Write(blob);                     // bitmap MSB-first
            foreach (var i in inBag)
                WriteItem(w, i.ItemId, (short)i.Durability,
                    (short)i.MaxDurability,
                    (byte)(i.IsBound ? 0x18 : 0x08),
                    (short)i.EnchantLevel, i.CreateTime, i.DueDate);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Corps NCID 282 ClientUpdateEquipment — désassemblé
    /// lapis::CNC_CZ_ClientUpdateEquipment::Serialize @0x82xxxxx
    /// (zone/decompiled.c ~354130) et ctor @0x86742b0 :
    ///   [i32 charId][u32 equipSlotMask]    — bit k = loc d'équipement k
    ///   par loc équipé (ordre croissant) :
    ///     [u16 itemId][u8 flags (entry+0x24 & 7)][u16 len + str maker]
    ///     [u24 mask] — ordre natif : bits 0-5 : 6×s16 vec +0x40 (≠-1),
    ///       bit6 : s16 +0x4c, bit7 : s16 +0x10, bit8 : u16 +0x1c,
    ///       bits 9-17 : 9×u16 vec +0x34 (≠0), bit18 : u16 +0x2c,
    ///       bit19 : s16 +0x2e, bit20 : i64 +0x50, bit21 : bloc +0x58
    ///       (u16 +0x58, u32 +0x5c, 6×u16 +0x60..+0x6a)
    ///     queue inconditionnelle : [u16 +0x34[9]][u16 +0x34[10]]
    ///       [u16 +0x6c][3×s16 *+0x70][u16 +0x7c][u16 +0x7e] = 16B
    ///   [u32 nHidden][nHidden × u16 itemId] — slots non-IsCanSee
    ///   [s16 +0x28][s8 +0x2a]
    /// bit21 du mask (bloc CEquipTrainAttrs) émis pour les items
    /// entraînés ; les autres champs conditionnels restent à zéro.
    /// </summary>
    public static byte[] BuildUpdateEquipment(int charId,
        IReadOnlyList<(int Loc, ItemEntity Item)> equipped)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(charId);                    // i32 — handle du porteur
        // Le client lit EXACTEMENT popcount(mask) records — tout record
        // émis sans bit correspondant dans le mask est relu comme le
        // u32 nHidden puis les ids cachés → ids fantômes → GetSItem
        // null → crash client (clone-ctor deref null, 0x74dc0b live).
        // Seuls les slots canoniques 0..31 sont représentables par ce
        // mask. Les EquipType bruts (ex. 35) doivent être normalisés
        // avant le codec vers leur loc native (35→10); ne pas les
        // laisser produire un record sans bit correspondant.
        var visible = equipped
            .Where(e => e.Loc is >= 0 and < 32)
            .GroupBy(e => e.Loc).Select(g => g.First())
            .OrderBy(e => e.Loc).ToList();
        var mask = 0u;
        foreach (var e in visible) mask |= 1u << e.Loc;
        w.Write(mask);                      // u32 — bitmap des locs équipés
        foreach (var e in visible)
        {
            var item = e.Item;
            w.Write((ushort)item.ItemId);   // u16 — id d'item
            // u8 — clone+0x24 & 7 natif = nb de sockets ouverts
            // (bits 0-2) — PAS le bind : un item bind émettait flags=1
            // → 1 socket fantôme affiché. Bind est bit3, non transmis
            // par le record équipement natif.
            w.Write((byte)(item.EmbeddedAmount & 7));
            w.Write((ushort)0);             // u16 len — maker vide
            // bit21 du mask = bloc CEquipTrainAttrs présent — émis
            // quand l'item est entraîné (Mana Infusion) : natif écrit
            // u16 +0x58, u32 +0x5c puis les 6×u16 +0x60..+0x6a dans
            // l'ordre MÉMOIRE groupé [t1][t2][t3][l1][l2][l3] (diffère
            // de l'ordre entrelacé du record 274/312 — preuve :
            // Serialize 282 @0x81f7bxx vs Serialize 274 @0x80e3xxx).
            var trained = item.TrainLevel != 0 ||
                          item.TrainSlot1Type != 0 ||
                          item.TrainSlot2Type != 0 ||
                          item.TrainSlot3Type != 0;
            w.Write((byte)0x00);                     // mask bits 0-7
            w.Write((byte)0x00);                     // mask bits 8-15
            w.Write((byte)(trained ? 0x20 : 0x00));  // mask bits 16-23
                                                     // (bit21 = 0x200000)
            if (trained)
            {
                w.Write((ushort)item.TrainLevel);    // u16 — clone+0x58
                w.Write((uint)item.TrainExp);        // u32 — clone+0x5c
                w.Write((ushort)item.TrainSlot1Type);   // u16 — +0x60
                w.Write((ushort)item.TrainSlot2Type);   // u16 — +0x62
                w.Write((ushort)item.TrainSlot3Type);   // u16 — +0x64
                w.Write((ushort)item.TrainSlot1Level);  // u16 — +0x66
                w.Write((ushort)item.TrainSlot2Level);  // u16 — +0x68
                w.Write((ushort)item.TrainSlot3Level);  // u16 — +0x6a
            }
            // Queue fixe INCONDITIONNELLE — Serialize natif @0x81f7bxx :
            // [u16 ec10(+0x34[9])][u16 ec11(+0x34[10])]
            // [u16 gold_enchant(+0x6c)][3×s16 magicbox_enchant(+0x70)]
            // [u16 magicbox_energy(+0x7c)][u16 starlevel(+0x7e)] = 16B.
            // ERREUR corrigée : 14 octets étaient écrits → records
            // désalignés → items fantômes → crash client (bug live).
            // Mapping prouvé par le ctor du 274 @0x80c8800 + site
            // d'appel CItemFactory::AddCloneItemWorld @0x809c454.
            w.Write((ushort)item.ExtraComboId10);     // u16 — ec10
            w.Write((ushort)item.ExtraComboId11);     // u16 — ec11
            w.Write((ushort)item.GoldEnchant);        // u16 — +0x6c
            w.Write((short)item.MagicBoxEnchant1);    // s16 — vec70[0]
            w.Write((short)item.MagicBoxEnchant2);    // s16 — vec70[1]
            w.Write((short)item.MagicBoxEnchant3);    // s16 — vec70[2]
            w.Write((ushort)item.MagicBoxEnergy);     // u16 — +0x7c
            w.Write((ushort)item.MagicBoxStarLevel);  // u16 — +0x7e
        }
        w.Write(0u);                        // u32 — nb items non-visibles
        w.Write((short)0);                  // s16 — champ +0x28
        w.Write((sbyte)0);                  // s8  — champ +0x2a
        return ms.ToArray();
    }

    /// <summary>
    /// Corps NCID 283 ClientModelModify — format utilisé par
    /// ZoneEntryFlow.SendModelModifyAsync (même builder, extrait pour le
    /// réutiliser depuis les handlers équipement) :
    ///   [i32 charId][s8 0x0a][i32 mask 0x5e00][u16 8][8×u32 itemId]
    ///   [u16 1][u32 charId] — slots 0-7 = items portés par loc.
    /// </summary>
    public static byte[] BuildModelModify(int charId,
        IReadOnlyDictionary<int, int> itemByLoc)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(charId);                    // i32 — handle
        w.Write((sbyte)0x0a);               // s8  — type char
        w.Write(0x5e00);                    // i32 — mask de modification
        w.Write((ushort)8);                 // u16 — nb de slots modèle
        for (var loc = 0; loc < 8; loc++)
            w.Write(itemByLoc.TryGetValue(loc, out var id)
                ? (uint)id : 0u);           // u32 — itemId par loc (0=vide)
        w.Write((ushort)1);                 // u16 — nb de handles suivants
        w.Write(charId);                    // u32 — le perso lui-même
        return ms.ToArray();
    }

    /// <summary>Notification d'insertion d'items — par slot touché :
    /// merge de stack → 291 ClientModifyItemDurability (nouveau count)
    /// ; nouveau slot → 274 ClientPutItemIntoInventory (record complet
    /// relu). Le natif émet 291 sur merge (CItemFactory::BuyItems
    /// @0x80aca40) — un 274 sur un slot déjà occupé fait matérialiser
    /// un clone au-dessus de l'existant et crashe le client (bugs
    /// live : achat boutique, loot sur stack, /give, craft).
    /// À utiliser pour TOUT ajout via AddItemAsync(…, touched).</summary>
    public static async Task EmitItemSlotsAsync(
        Eden.Core.Networking.Session.NetSession session,
        Eden.Core.Database.Repositories.InventoryRepository repo,
        int charId,
        List<(int BagId, int Slot, bool Merged)> touched,
        CancellationToken token)
    {
        foreach (var (bag, slot, merged) in touched)
        {
            var item = await repo.GetItemAsync(charId, bag, slot);
            if (item == null) continue;
            await EmitItemSlotAsync(session, bag, slot, merged, item,
                token);
        }
    }

    /// <summary>Émet un 291/274 à partir de l'entité déjà en mémoire
    /// (chemin bulk <c>InventoryService.AddItemsAsync</c>) — aucun
    /// SELECT : l'entité mutée/créée porte déjà tous ses champs.</summary>
    public static async Task EmitItemSlotAsync(
        Eden.Core.Networking.Session.NetSession session,
        int bag, int slot, bool merged, ItemEntity item,
        CancellationToken token)
    {
        if (merged)
            await session.SendAsync(
                new Eden.Core.Networking.Messages.Generated
                    .CNC_CZ_ClientModifyItemDurability
                {
                    Field1 = 0,           // s16 — contType (0=sac)
                    Field2 = (short)slot, // s16 — loc dans le sac
                    Field3 = (short)item.Quantity,   // s16 — count
                    Field4 = (short)item.MaxDurability, // s16 — dur.max
                    Field5 = (short)bag,  // s16 — container_index
                    Field6 = 0,           // s16 — +0xe natif
                }, token);
        else
            await session.SendPacketAsync(274, BuildPutItem(item),
                token);
    }

    /// <summary>
    /// Corps NCID 274 ClientPutItemIntoInventory — désassemblé
    /// `CItemFactory::PutIntoContainer` @0x809ea00 (ctor du message,
    /// 54B corps comme la capture zone) :
    ///   `[u16 itemId][s16 container_index][s16 loc]
    ///   [s16 durability][s16 cur_maxdurability]
    ///   [u16 slen=0][u8 flags][u24 mask][u32 create_time][34B queue]`.
    /// container = `IItemContainer::GetContainerIndex`, loc = slot
    /// choisi par `PutIn`/`PutItemInInventory`. Durabilité = count
    /// pour les stacks (clone+0xc) ; param_5=-1 natif → dur=maxdur
    /// (équipement neuf). Le bug live « item acheté invisible » venait
    /// de l'ordre `[id][qty][qty][bag][slot]` : le client lisait
    /// bag=quantité et durability=0.
    /// meta → mêmes champs masqués que WriteItem (sockets, combos).
    /// </summary>
    public static byte[] BuildPutItem(int itemId, short bag,
        short slot, short durability, short maxDurability, byte flags,
        short enchant, int createTime = 0, long dueDate = 0,
        ItemEntity? meta = null)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((ushort)itemId);              // u16 — id d'item (S_Item)
        w.Write(bag);                         // s16 — container_index
        w.Write(slot);                        // s16 — loc dans le sac
        w.Write(durability);                  // s16 — clone+0xc (count stack)
        w.Write(maxDurability);               // s16 — clone+0xe cur_max
        w.Write((ushort)0);                   // u16 len — string vide
        // u8 flags — bits 0-2 = nb de sockets ouverts, bit3 =
        // identifié (clone+0x24 natif — cf. WriteItem).
        var putFlagByte = (byte)(flags | (byte)((meta?.EmbeddedAmount ?? 0) & 7));
        if (meta != null)
            putFlagByte = (byte)((putFlagByte & ~8) | (meta.Identify != 0 ? 8 : 0));
        w.Write(putFlagByte);
        // u24 mask — mêmes bits que WriteItem (record natif @0x80e3xxx).
        uint mask = 0x000100u;
        if (meta != null)
        {
            for (var i = 0; i < 6; i++)
                if (meta.GetEmbeddedId(i) != -1) mask |= 1u << i;
            if (meta.EmbeddedAmount != 0) mask |= 0x40u;
            if (meta.Combo != 0) mask |= 0x80u;
            for (var i = 0; i < 9; i++)
                if (meta.ExtraCombos[i] != 0) mask |= 1u << (10 + i);
        }
        if (enchant != 0) mask |= 0x080000u;
        if (dueDate != 0) mask |= 0x200000u;
        w.Write((byte)(mask & 0xff));
        w.Write((byte)(mask >> 8 & 0xff));
        w.Write((byte)(mask >> 16 & 0xff));
        if (meta != null)
        {
            for (var i = 0; i < 6; i++)
            {
                var id = meta.GetEmbeddedId(i);
                if (id != -1)
                    w.Write((short)id);   // bits0-5 — s16 embedded_idN
            }
            if (meta.EmbeddedAmount != 0)
                w.Write((short)meta.EmbeddedAmount); // bit6 — s16
            if (meta.Combo != 0)
                w.Write((short)meta.Combo);          // bit7 — s16 combo
        }
        // bit8 — u32 clone+0x18 create_time : valeur réelle de l'item
        // (un item déplacé/racheté garde sa date native, pas "now").
        var ct = createTime != 0 ? createTime
            : (meta?.CreateTime ?? (int)DateTimeOffset.UtcNow
                .ToUnixTimeSeconds());
        w.Write((uint)ct);
        if (meta != null)
            for (var i = 0; i < 9; i++)
                if (meta.ExtraCombos[i] != 0)
                    w.Write((ushort)meta.ExtraCombos[i]); // bits10-18
        if (enchant != 0)
            w.Write(enchant);                 // bit19 — s16 strengthen
        if (dueDate != 0)
            w.Write(dueDate);                 // bit21 — i64 due_date
        w.Write(new byte[34]);
        return ms.ToArray();
    }

    /// <summary>
    /// Surcharge entité de <see cref="BuildPutItem(int,short,short,
    /// short,short,byte,short,int,long,ItemEntity)"/> — dérive tous
    /// les champs de l'entité relue (durability = count pour les
    /// stacks, comme clone+0xc natif ; IsBound → flags 0x18/0x08).
    /// Élimine la classe de bug « mauvais ordre de champs » des
    /// appelants : tout insert notifié via 274 doit passer par là.
    /// </summary>
    public static byte[] BuildPutItem(ItemEntity item) =>
        BuildPutItem(item.ItemId, (short)item.BagId, (short)item.Slot,
            (short)item.Durability, (short)item.MaxDurability,
            (byte)(item.IsBound ? 0x18 : 0x08),
            (short)item.EnchantLevel, item.CreateTime, item.DueDate,
            item);
}
