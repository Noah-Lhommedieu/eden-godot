using EdenClient.Net;

namespace EdenClient.Protocol;

// Équipement & sorts du joueur (S→C) + refresh d'équipement (C→S 8).
//
// Sources de preuve :
//   - $EDEN/server/src/Eden.Zone/Codecs/NativeItemCodec.cs —
//     BuildEnterNodeEquipAndSpell (311), BuildUpdateEquipment (282,
//     désasm Serialize @0x82xxxxx + ctor @0x86742b0), BuildChangeEquip
//     (276, NotifyChangeEquipment @0x843df10) ;
//   - golden server/tests/Eden.Tests/Data/golden/ncid_311.json ;
//   - CNE_CZ_ZoneServerUpdateEquipment (8) : InventoryHandlers.cs —
//     wire natif [u32 mask des locs modifiés].

// ---------- C→S ----------

/// <summary>C→S NCID 8 — UpdateEquipment [u32 locMask] : le client
/// signale les locs d'équipement modifiés (bit k = loc k) ; le serveur
/// répond 282 + 283. CNE_CZ_ZoneServerUpdateEquipment.</summary>
public sealed class ZoneUpdateEquipment : NetMessage
{
    public override int Opcode => 8;
    public override string Name => "Zone.ZoneServerUpdateEquipment";
    /// <summary>u32 — bitmap des locs d'équipement modifiés.</summary>
    public uint UpdateFlags { get; set; }
    public override void Serialize(BinWriter s) =>
        s.WriteInt((int)UpdateFlags);
    public override void Deserialize(BinReader s) =>
        UpdateFlags = s.ReadUInt();
}

// ---------- S→C ----------

/// <summary>Item porté à une loc d'équipement (record 282/276/311).</summary>
public sealed record EquippedItem(int Loc, int Container, int ItemId,
    byte Flags, int Amount, int Strengthen,
    int Durability = 0, int MaxDurability = 0);

/// <summary>S→C NCID 282 — UpdateEquipment : état complet porté d'un
/// personnage (réponse au C→S 8). PASSTHRU côté serveur (codec
/// NativeItemCodec.BuildUpdateEquipment — désasm Serialize
/// @0x82xxxxx) :
///   `[i32 charId][u32 locMask]` puis par loc équipé (ordre croissant)
///   un record UpdateEquip (`[u16 itemId][u8 flags][str][u24 mask]
///   [cond][16B tail]`), puis `[u32 nHidden][nHidden×u16 itemId]
///   [s16][s8]`.</summary>
public sealed class ZoneUpdateEquipmentView : NetMessage
{
    public override int Opcode => 282;
    public override string Name => "Zone.ClientUpdateEquipment";
    /// <summary>i32 — charId/handle du porteur.</summary>
    public int CharId { get; set; }
    /// <summary>u32 — bitmap des locs (bit k = loc k équipé).</summary>
    public uint LocMask { get; set; }
    /// <summary>Items par loc (ordre croissant — même ordre que les
    /// bits de <see cref="LocMask"/>).</summary>
    public List<EquippedItem> Items { get; } = new();
    /// <summary>Ids d'items équipés non visibles (slots IsCanSee=false).</summary>
    public List<ushort> Hidden { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId);
        s.WriteInt((int)LocMask);
        // Corps variable — la ré-émission exacte n'est pas requise
        // (parse S→C seulement) ; on écrit les records minimaux.
        foreach (var it in Items)
        {
            s.WriteShort((short)it.ItemId);
            s.WriteByte(it.Flags);
            s.WriteShort(0);                    // str vide
            s.WriteBytes(new byte[3]);          // u24 mask = 0
            s.WriteBytes(new byte[16]);         // tail 16B
        }
        s.WriteInt(Hidden.Count);
        foreach (var h in Hidden) s.WriteShort((short)h);
        s.WriteShort(0);                        // s16 +0x28
        s.WriteByte(0);                         // s8 +0x2a
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt();
        LocMask = s.ReadUInt();
        Items.Clear();
        Hidden.Clear();
        for (var loc = 0; loc < 32; loc++)
        {
            if ((LocMask & (1u << loc)) == 0) continue;
            if (!NativeItemRecord.TryReadUpdateEquip(s, out var rec))
                return;                         // corps tronqué — stop
            Items.Add(new EquippedItem(loc, 0, rec.ItemId, rec.Flags,
                rec.Amount, rec.Strengthen,
                rec.Durability, rec.MaxDurability));
        }
        if (s.Remaining >= 4)
        {
            var nHidden = s.ReadInt();
            for (var i = 0; i < nHidden && s.Remaining >= 2; i++)
                Hidden.Add(s.ReadUShort());
        }
        // queue : [s16 +0x28][s8 +0x2a] — ignorée.
    }
}

/// <summary>S→C NCID 276 — ChangeEquip : delta d'un slot d'équipement
/// broadcasté aux observateurs (NotifyChangeEquipment — soi exclu).
/// PASSTHRU. Corps (BuildChangeEquip, Serialize @0x843exxx) :
///   `[i32 charId][u16 slot][u16 itemId(0=vidé)][s16 container]
///   [i8 révision]` puis corps EquipView `[u8 flags][str][u24 mask]
///   [cond][34B tail]`.</summary>
public sealed class ZoneChangeEquip : NetMessage
{
    public override int Opcode => 276;
    public override string Name => "Zone.ClientChangeEquip";
    /// <summary>i32 — charId du porteur.</summary>
    public int CharId { get; set; }
    /// <summary>u16 — loc d'équipement changée.</summary>
    public ushort Slot { get; set; }
    /// <summary>u16 — itemId (0 = slot vidé).</summary>
    public ushort ItemId { get; set; }
    /// <summary>s16 — conteneur (0 tenu, 1 pet, 2-4 pages, 5 glyphes).</summary>
    public short Container { get; set; }
    /// <summary>i8 — révision d'équipement.</summary>
    public sbyte Revision { get; set; }
    /// <summary>Corps item lu (flags/mask/cond).</summary>
    public ItemRecord? Item { get; private set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId);
        s.WriteShort((short)Slot);
        s.WriteShort((short)ItemId);
        s.WriteShort(Container);
        s.WriteByte((byte)Revision);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt();
        Slot = s.ReadUShort();
        ItemId = s.ReadUShort();
        Container = s.ReadShort();
        Revision = (sbyte)s.ReadByte();
        if (ItemId != 0 &&
            NativeItemRecord.TryReadEquipView(s, out var rec))
            Item = rec;
        else
            Item = null;
    }
}

// ---------- parseur du corps 311 (équipement + sorts) ----------

/// <summary>Sort connu du personnage (fin du corps 311) — spellmaster
/// lu en u16 (les ids réels dépassent 32767, ex. 50001 du golden).</summary>
public sealed record KnownSpell(int SpellMaster, byte Level)
{
    /// <summary>Nom EN via T_Spell.ini (repli « #id »).</summary>
    public string SpellName =>
        Data.TextResolver.SpellName(SpellMaster) ?? $"#{SpellMaster}";
}

/// <summary>
/// Parseur du corps NCID 311 <c>ClientEnterNodeEquipAndSpell</c> —
/// port client de <c>NativeItemCodec.BuildEnterNodeEquipAndSpell</c>
/// (désasm + golden ncid_311.json) :
///   `[u8 nContainers]` puis par conteneur
///   `[u8 key][u32 slotMask]` + un item-record WriteItem par bit levé
///   (ordre croissant des locs), puis `[s16 nSpells]` +
///   n × `[s16 spellmaster][u8 spell_level]`.
/// Les 6 conteneurs natifs (clés 0-5) : 0 = porté, 1 = pet,
/// 2-4 = pages d'équipement, 5 = glyphes (doc BuildChangeEquip).
/// </summary>
public static class EquipAndSpellParser
{
    /// <summary>Parse le corps 311 (sans NCID) → équipement + sorts.
    /// Conservateur : retourne ce qui a été lu avant une troncature.</summary>
    public static (List<EquippedItem> Equipped, List<KnownSpell> Spells)
        Parse(byte[] body)
    {
        var equipped = new List<EquippedItem>();
        var spells = new List<KnownSpell>();
        var r = new BinReader(body);
        if (r.Remaining < 1) return (equipped, spells);
        var nContainers = r.ReadByte();
        for (var c = 0; c < nContainers; c++)
        {
            if (r.Remaining < 5) return (equipped, spells);
            var key = r.ReadByte();             // u8 — clé conteneur
            var mask = r.ReadUInt();            // u32 — bitmap des locs
            for (var loc = 0; loc < 32; loc++)
            {
                if ((mask & (1u << loc)) == 0) continue;
                if (!NativeItemRecord.TryReadInventory(r, out var rec))
                    return (equipped, spells);
                equipped.Add(new EquippedItem(loc, key, rec.ItemId,
                    rec.Flags, rec.Amount, rec.Strengthen,
                    rec.Durability, rec.MaxDurability));
            }
        }
        if (r.Remaining < 2) return (equipped, spells);
        var nSpells = r.ReadShort();
        for (var i = 0; i < nSpells && r.Remaining >= 3; i++)
            spells.Add(new KnownSpell(r.ReadUShort(), r.ReadByte()));
        return (equipped, spells);
    }
}

// ---------- états agrégés pour le HUD ----------

/// <summary>
/// Équipement du joueur self (loc → itemId) — alimenté par le 311
/// d'entrée puis les updates 276/282 ; lu par le profil HUD (C4) pour
/// l'affichage équipement + apparence.
/// </summary>
public static class PlayerEquipment
{
    /// <summary>Émis à chaque mutation (311 entrée, 276, 282).</summary>
    public static event Action? Changed;

    /// <summary>Re-notifie l'état courant — les cases « ShowKuso » du
    /// profil ne changent pas l'équipement mais forcent un rebuild du
    /// visuel porté (même canal que les vraies mutations).</summary>
    public static void RaiseChanged() => Changed?.Invoke();

    /// <summary>loc → itemId actuellement porté (conteneur 0 = tenu).</summary>
    private static readonly Dictionary<int, int> _worn = new();
    /// <summary>Snapshot loc → itemId.</summary>
    public static IReadOnlyDictionary<int, int> Worn => _worn;

    /// <summary>A6.6 — pages costume/ensemble (conteneurs 2-4, locs 1-5)
    /// : (conteneur, loc) → itemId. Zéro stat propre ; seuls les
    /// enchants posés comptent (stage serveur « costumes »).</summary>
    private static readonly Dictionary<(int, int), int> _costumes = new();
    /// <summary>Pose/retire localement une pièce de page costume
    /// (ack 277 : le serveur n'émet pas de 276 vers soi).</summary>
    public static void SetCostume(int container, int loc, int itemId)
    {
        lock (_costumes)
        {
            if (itemId == 0) _costumes.Remove((container, loc));
            else _costumes[(container, loc)] = itemId;
        }
        Changed?.Invoke();
    }
    public static int CostumeAt(int container, int loc)
    {
        lock (_costumes)
            return _costumes.TryGetValue((container, loc), out var id) ? id : 0;
    }
    /// <summary>Tenue rendue sur le corps du joueur : <see cref="Worn"/>
    /// + pages costume fusionnées (A6.6, voir
    /// <see cref="EdenClient.Game.CostumeVisual"/>).</summary>
    public static IReadOnlyDictionary<int, int> VisualWorn(
        Func<int, int> kindOf) =>
        EdenClient.Game.CostumeVisual.Merge(_worn, CostumeAt, kindOf);

    /// <summary>Model-ids des 8 premiers locs (ordre 283 — utilisé par
    /// l'apparence 3D).</summary>
    public static int[] ModelItemIds
    {
        get
        {
            var ids = new int[8];
            for (var i = 0; i < 8; i++)
                _worn.TryGetValue(i, out ids[i]);
            return ids;
        }
    }

    /// <summary>Retire un loc du conteneur « porté » (ack 277 d'un
    /// déséquipement, dont l'auto-déséquipement 1H/2H/arc du serveur qui
    /// n'envoie ni 276 ni 282 à l'acteur — sans ça l'ancienne arme
    /// restait dans l'état porté et sur le modèle 3D).</summary>
    public static void RemoveWorn(int loc)
    {
        if (_worn.Remove(loc)) Changed?.Invoke();
    }

    /// <summary>311 : recharge l'état complet du conteneur « porté ».</summary>
    public static void ApplyEnterNode(IReadOnlyList<EquippedItem> items)
    {
        _worn.Clear();
        lock (_costumes) _costumes.Clear();
        foreach (var it in items)
        {
            if (it.Container == 0) _worn[it.Loc] = it.ItemId;
            else if (it.Container is >= 2 and <= 4)
                lock (_costumes) _costumes[(it.Container, it.Loc)] = it.ItemId;
        }
        Changed?.Invoke();
    }

    /// <summary>282 : état complet porté (réponse au 8).</summary>
    public static void ApplyUpdateEquipment(ZoneUpdateEquipmentView m,
        uint selfCharId)
    {
        if ((uint)m.CharId != selfCharId) return;
        for (var loc = 0; loc < 32; loc++)
            _worn.Remove(loc);
        foreach (var it in m.Items)
            _worn[it.Loc] = it.ItemId;
        Changed?.Invoke();
    }

    /// <summary>276 : delta d'un slot (soi inclus si le serveur
    /// l'émet — UpdateEquipment(8) broadcaste à la vue).</summary>
    public static void ApplyChangeEquip(ZoneChangeEquip m, uint selfCharId)
    {
        if ((uint)m.CharId == selfCharId && m.Container is >= 2 and <= 4)
        {
            lock (_costumes)
            {
                if (m.ItemId == 0) _costumes.Remove((m.Container, m.Slot));
                else _costumes[(m.Container, m.Slot)] = m.ItemId;
            }
            Changed?.Invoke();
            return;
        }
        if ((uint)m.CharId != selfCharId || m.Container != 0) return;
        if (m.ItemId == 0) _worn.Remove(m.Slot);
        else _worn[m.Slot] = m.ItemId;
        Changed?.Invoke();
    }
}

/// <summary>
/// Sorts connus du personnage (fin du corps 311) — lu par la fenêtre
/// skills du HUD (C10) ; noms EN via <see cref="Data.TextResolver.SpellName"/>.
/// </summary>
public static class PlayerSkills
{
    /// <summary>Émis quand la liste (ré)arrive (311).</summary>
    public static event Action? Changed;

    private static readonly List<KnownSpell> _spells = new();
    /// <summary>Sorts connus (spellmaster + niveau).</summary>
    public static IReadOnlyList<KnownSpell> Spells => _spells;

    /// <summary>311 : recharge la liste complète.</summary>
    public static void ApplyEnterNode(IReadOnlyList<KnownSpell> spells)
    {
        _spells.Clear();
        _spells.AddRange(spells);
        Changed?.Invoke();
    }

    /// <summary>286 ClientGetSpell : nouveau niveau d'un sort
    /// (montée +1/Max ou apprentissage).</summary>
    public static void SetLevel(int spellMaster, int level)
    {
        var i = _spells.FindIndex(s => s.SpellMaster == spellMaster);
        var k = new KnownSpell(spellMaster, (byte)Math.Clamp(level, 0, 255));
        if (i >= 0) _spells[i] = k; else _spells.Add(k);
        // Un level-up de classe envoie ~70 × 286 d'affilée : un seul Changed par frame
        // (hotbar + fenêtre Classe se reconstruisaient 70 fois).
        if (_changedQueued) return;
        _changedQueued = true;
        Defer(() => { _changedQueued = false; Changed?.Invoke(); });
    }

    private static bool _changedQueued;

    /// <summary>Planifie à l'image suivante (posé par le client Godot) ; immédiat par défaut (tests purs).</summary>
    public static Action<Action> Defer { get; set; } = a => a();
}
