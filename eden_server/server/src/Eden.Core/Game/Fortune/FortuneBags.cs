using Eden.Core.Networking;

namespace Eden.Core.Game.Fortune;

// -----------------------------------------------------------------------------
//  Sacs de fortune (fortune_bag) — modèle + codec du push monde→zone.
//
//  Preuves :
//  - SELECT natif (world PGAccountDBO @0x08059xxx) :
//      id, sequence, set, item_id, item_num, probability, item_counter,
//      bulletin, embedded, bind — ORDER BY id,set,sequence.
//  - Wire ZW 26 `CNC_ZW_ZoneServerUpdateFortuneBag` (ctor 14 deques
//    @world 0x08059f32 ; Deserialize zone @0x082afec0) : [s16 phase]
//    puis si u16 count>0, par record :
//      [u16 bag_id][u16 item_counter][u16 nb_sets][u16 set]
//      [u16 nb_lots_set][u16 prize+0][f32 cumHaute][u16 item_id]
//      [u16 item_num][u8 bulletin][f32 cumBasse][f32 cumHaute]
//      [s16 embedded][s16 bind]
//  - Phases : 0 → clear + FortuneUpdating=1 ; 1 → records ; 2 →
//    FortuneUpdating=0 (`CNE_ZW_ZoneServerUpdateFortuneBag::Execute`
//    zone @0x082afec0).
//  - Structure zone +0x124 : map<bag_id, BagDef> ; BagDef+4 = seuil
//    pity (item_counter), BagDef+0x1c = nb de sets (= slots vides
//    requis — un lot tiré par set), BagDef+0x14 → set-map ; holder
//    (pool 0x20) = {f32 range(+0), s16 set(+4), map cum→lot(+0x10),
//    +0x1c nb lots} ; CFortuneBag = {u16 seq(+0), u16 item_id(+2),
//    f32 cumBasse(+4), f32 cumHaute(+8), u16 item_num(+0xc),
//    u8 bulletin(+0xe), s16 embedded(+0x10), s16 bind(+0x12)}.
// -----------------------------------------------------------------------------

/// <summary>Un lot d'un sac : probabilité cumulée + item gagné.
/// Transcription de `CFortuneBag` (zone, 0x14 octets).</summary>
/// <param name="Sequence">Colonne `sequence` — ordre natif dans le
/// set (prize+0 sur le fil, f6).</param>
/// <param name="ItemId">Item gagné (prize+2, f8).</param>
/// <param name="ItemNum">Quantité (prize+0xc, f9).</param>
/// <param name="CumProbability">Borne haute cumulée = clé de la map
/// native (f7 = prize+8 = f12).</param>
/// <param name="Probability">Poids du lot = cum haute − cum basse
/// (prize+8 − prize+4 sur le fil — même soustraction que le natif
/// dans le tirage pity).</param>
/// <param name="Bulletin">Drapeau annonce écran (prize+0xe, f10) —
/// déclenche `BroadCastScreenMessage Type= 8` au GetReward.</param>
/// <param name="Embedded">Flag embedded (prize+0x10, f13 — validation
/// native &lt;2).</param>
/// <param name="Bind">Flag bind (prize+0x12, f14 — ≤1).</param>
/// <param name="ItemCounter">Compteur de rareté (col DB
/// `item_counter` — index dans player_fortune_bag ; non véhiculé par
/// le fil mais conservé pour les contrôles côté serveur).</param>
public sealed record FortunePrize(
    ushort Sequence, ushort ItemId, ushort ItemNum,
    float CumProbability, float Probability, byte Bulletin,
    short Embedded, short Bind, int ItemCounter);

/// <summary>Un set de lots d'un sac (2e niveau de la map native —
/// `set` SQL). <see cref="DiceRange"/> = borne haute du tirage
/// (<c>GetDoubleRand(0, range)</c> natif ; la dernière clé cumulée,
/// 100 pour les données officielles).</summary>
public sealed class FortuneSet
{
    /// <summary>Clé du set (u16 fil f4 — map&lt;set, holder&gt;).
    /// set==99 = pool spécial : dé ∈ [0,100) quel que soit le total
    /// cumulé, et si le dé dépasse la clé max le set est sauté
    /// (OpenFortuneBag @0x080bb300).</summary>
    public ushort SetId { get; set; }
    /// <summary>Nombre de lots du set (holder+0x1c natif = taille de
    /// la map cum, fil f5).</summary>
    public ushort PrizeCount { get; set; }
    /// <summary>Borne haute du dé natif (dernière clé cumulée).</summary>
    public float DiceRange { get; set; }
    /// <summary>Lots triés par probabilité cumulée (ordre du
    /// std::map&lt;float&gt; natif).</summary>
    public List<FortunePrize> Prizes { get; } = [];

    /// <summary>Map pity native : lots <c>bulletin == 1</c> pondérés
    /// par (cum haute − cum basse = <see cref="FortunePrize.
    /// Probability"/>), clés = poids cumulés courants
    /// (OpenFortuneBag : local_4c lots, local_2ac cumulatif).</summary>
    public List<(float Cum, FortunePrize Prize)> PityWeights()
    {
        float total = 0;
        var list = new List<(float, FortunePrize)>();
        foreach (var p in Prizes)
        {
            if (p.Bulletin != 1) continue;
            total = p.Probability + total;      // même ordre natif
            list.Add((total, p));
        }
        return list;
    }

    /// <summary>Tirage pity : upper_bound (premier poids cumulé
    /// strictement &gt; le dé). Null si aucune clé — abort natif
    /// quand la map n'était pas vide.</summary>
    public static FortunePrize? PityRoll(
        IReadOnlyList<(float Cum, FortunePrize Prize)> weights,
        float dice)
    {
        foreach (var (cum, p) in weights)
            if (dice < cum) return p;
        return null;
    }
}

/// <summary>Définition d'un sac — valeur de la map +0x124 native.
/// <see cref="Id"/> = l'item sac lui-même (lookup item type 0xc +
/// cast time pour le lancer). </summary>
public sealed class FortuneBagDef
{
    /// <summary>Id du sac = item_id du sac (bagdef+0 natif, fil
    /// f1).</summary>
    public int Id { get; set; }
    /// <summary>Seuil pity = colonne `item_counter` (bagdef+4 natif,
    /// fil f2). ≥1 : après autant d'ouvertures sans lot bulletin, le
    /// tirage devient pondéré sur les lots bulletin du set
    /// (OpenFortuneBag @0x080bb300).</summary>
    public ushort ItemCounter { get; set; }
    /// <summary>Nombre de sets = slots vides requis (bagdef+0x1c
    /// natif = taille de la set-map, fil f3 — un lot tiré par set,
    /// check <c>IItemContainer::GetEmptySlotNum</c>).</summary>
    public ushort RequiredSlots { get; set; }
    /// <summary>Sets par clé (map&lt;u16, holder&gt; native —
    /// OpenFortuneBag itère tous les sets, ChanceLuckyBag le
    /// premier).</summary>
    public Dictionary<ushort, FortuneSet> Sets { get; } = [];

    /// <summary>Premier set (comportement ChanceLuckyBag : le natif
    /// lit holder+0x14 directement — la data n'a que set=1).</summary>
    public FortuneSet? FirstSet =>
        Sets.TryGetValue(1, out var s) ? s :
        Sets.Count > 0 ? Sets.Values.First() : null;

    /// <summary>Tirage natif : <c>GetDoubleRand(0, range)</c> puis
    /// lower_bound(cumProb) dans la map ordonnée — le premier lot
    /// dont la clé cumulée &gt; le dé (DoChanceLuckyBagNext
    /// @0x08096b50).</summary>
    public FortunePrize? Roll(FortuneSet set, float dice)
    {
        foreach (var p in set.Prizes)
            if (dice < p.CumProbability) return p;
        // float edge : le dé peut égaler la borne haute (arrondi) —
        // le natif retourne 0 dans ce cas (pfVar9 == sentinelle).
        return null;
    }
}

/// <summary>Codec du push monde→zone (ZW 26 / opcode généré 651) —
/// [s16 phase][u16 count][14 champs × count] quand count &gt; 0.</summary>
public static class FortuneBagWire
{
    /// <summary>Record fil complet (14 champs — ordre exact des deques
    /// du Serialize monde).
    /// <paramref name="ItemCounter"/> = seuil pity (bagdef+4),
    /// <paramref name="RequiredSlots"/> = nb de sets (bagdef+0x1c),
    /// <paramref name="PrizeCount"/> = nb de lots du set
    /// (holder+0x1c), <paramref name="CumLower"/> = prize+4,
    /// <paramref name="CumUpper"/> = prize+8 (= CumProb).</summary>
    public sealed record WireRecord(
        ushort BagId, ushort ItemCounter, ushort RequiredSlots,
        ushort SetId, ushort PrizeCount, ushort PrizeSeq,
        float CumProb, ushort ItemId, ushort ItemNum, byte Bulletin,
        float CumLower, float CumUpper, short Embedded, short Bind);

    /// <summary>Sérialise [s16 phase][u16 n][records…] — la phase
    /// 0/2 native n'a pas de vecteur (count absent).</summary>
    public static byte[] Build(short phase, IReadOnlyList<WireRecord> records)
    {
        var s = new OutBinStream();
        s.WriteShort(phase);                                  // s16 phase (0/1/2)
        if (phase == 1)
        {
            s.WriteShort((short)records.Count);               // u16 count
            foreach (var r in records)
            {
                s.WriteShort((short)r.BagId);                 // f1  u16 bag_id
                s.WriteShort((short)r.ItemCounter);           // f2  u16 seuil pity (item_counter)
                s.WriteShort((short)r.RequiredSlots);         // f3  u16 nb de sets
                s.WriteShort((short)r.SetId);                 // f4  u16 set
                s.WriteShort((short)r.PrizeCount);            // f5  u16 nb de lots du set
                s.WriteShort((short)r.PrizeSeq);              // f6  u16 prize+0 (sequence)
                s.WriteFloat(r.CumProb);                      // f7  f32 clé cumulée (haute)
                s.WriteShort((short)r.ItemId);                // f8  u16 item_id
                s.WriteShort((short)r.ItemNum);               // f9  u16 item_num
                s.WriteByte(r.Bulletin);                      // f10 u8  bulletin
                s.WriteFloat(r.CumLower);                     // f11 f32 cum basse (prize+4)
                s.WriteFloat(r.CumUpper);                     // f12 f32 cum haute (prize+8)
                s.WriteShort(r.Embedded);                     // f13 s16 embedded
                s.WriteShort(r.Bind);                         // f14 s16 bind
            }
        }
        return s.ToArray();
    }

    /// <summary>Parse la charge entrante (zone) : phase + records.</summary>
    public static (short Phase, List<WireRecord> Records) Parse(ReadOnlySpan<byte> body)
    {
        var records = new List<WireRecord>();
        if (body.Length < 2) return (0, records);
        var phase = BitConverter.ToInt16(body);
        if (phase != 1 || body.Length < 4) return (phase, records);
        var n = BitConverter.ToUInt16(body.Slice(2, 2));
        var off = 4;
        for (var i = 0; i < n && off + 33 <= body.Length; i++)
        {
            var r = new WireRecord(
                BagId: BitConverter.ToUInt16(body.Slice(off, 2)),
                ItemCounter: BitConverter.ToUInt16(body.Slice(off + 2, 2)),
                RequiredSlots: BitConverter.ToUInt16(body.Slice(off + 4, 2)),
                SetId: BitConverter.ToUInt16(body.Slice(off + 6, 2)),
                PrizeCount: BitConverter.ToUInt16(body.Slice(off + 8, 2)),
                PrizeSeq: BitConverter.ToUInt16(body.Slice(off + 10, 2)),
                CumProb: BitConverter.ToSingle(body.Slice(off + 12, 4)),
                ItemId: BitConverter.ToUInt16(body.Slice(off + 16, 2)),
                ItemNum: BitConverter.ToUInt16(body.Slice(off + 18, 2)),
                Bulletin: body[off + 20],
                CumLower: BitConverter.ToSingle(body.Slice(off + 21, 4)),
                CumUpper: BitConverter.ToSingle(body.Slice(off + 25, 4)),
                Embedded: BitConverter.ToInt16(body.Slice(off + 29, 2)),
                Bind: BitConverter.ToInt16(body.Slice(off + 31, 2)));
            records.Add(r);
            off += 33;                                // 33 octets/record
        }
        return (phase, records);
    }

    /// <summary>Construit les records fil depuis les lignes DB
    /// (probabilités cumulées par (id,set) dans l'ordre sequence —
    /// ORDER BY id,set,sequence natif). Deux passes : les compteurs de
    /// la rb-tree native (nb de sets par sac — bagdef+0x1c — et nb de
    /// lots par set — holder+0x1c) sont connus seulement en fin de
    /// groupe.</summary>
    public static List<WireRecord> RecordsFromRows(
        IEnumerable<FortuneBagRow> rows)
    {
        var src = rows as IList<FortuneBagRow> ?? rows.ToList();
        var setCounts = new Dictionary<int, int>();
        var prizeCounts = new Dictionary<long, int>();
        foreach (var r in src)
        {
            var key = (long)r.Id * 65536 + r.Set;
            if (!prizeCounts.TryGetValue(key, out var pc))
            {
                pc = 0;
                setCounts[r.Id] = setCounts.TryGetValue(r.Id, out var sc)
                    ? sc + 1 : 1;
            }
            prizeCounts[key] = pc + 1;
        }
        var list = new List<WireRecord>(src.Count);
        float cum = 0; long lastKey = -1;
        foreach (var r in src)
        {
            var key = (long)r.Id * 65536 + r.Set;
            if (key != lastKey) { cum = 0; lastKey = key; }
            var lower = cum;
            cum += r.Probability;
            list.Add(new WireRecord(
                (ushort)r.Id, (ushort)Math.Max(0, r.ItemCounter),
                (ushort)setCounts[r.Id], (ushort)r.Set,
                (ushort)prizeCounts[key],
                (ushort)r.Sequence, cum,
                (ushort)r.ItemId, (ushort)r.ItemNum, (byte)r.Bulletin,
                lower, cum, (short)r.Embedded, (short)r.Bind));
        }
        return list;
    }

    /// <summary>Applique les records à un registre (rebuild natif
    /// zone : map&lt;bag_id → BagDef&gt; + map&lt;set → holder&gt; +
    /// map&lt;float → prize&gt;).</summary>
    public static void Apply(Dictionary<int, FortuneBagDef> bags,
        IReadOnlyList<WireRecord> records)
    {
        foreach (var r in records)
        {
            if (!bags.TryGetValue(r.BagId, out var bag))
                bags[r.BagId] = bag = new FortuneBagDef { Id = r.BagId };
            bag.ItemCounter = r.ItemCounter;
            bag.RequiredSlots = r.RequiredSlots;
            if (!bag.Sets.TryGetValue(r.SetId, out var set))
            {
                set = new FortuneSet { SetId = r.SetId };
                bag.Sets[r.SetId] = set;
            }
            set.PrizeCount = r.PrizeCount;
            // Poids = cum haute − cum basse : la soustraction du natif
            // dans la map pity (prize+8 − prize+4).
            set.Prizes.Add(new FortunePrize(
                r.PrizeSeq, r.ItemId, r.ItemNum, r.CumProb,
                r.CumUpper - r.CumLower, r.Bulletin, r.Embedded,
                r.Bind, r.ItemCounter));
            if (r.CumProb > set.DiceRange) set.DiceRange = r.CumProb;
        }
    }
}

/// <summary>Ligne de la table `fortune_bag` (SELECT natif).</summary>
public sealed record FortuneBagRow(
    int Id, int Sequence, int Set, int ItemId, int ItemNum,
    float Probability, int ItemCounter, int Bulletin, int Embedded,
    int Bind);
