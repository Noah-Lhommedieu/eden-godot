using System.Buffers.Binary;
using System.IO;
using System.Text;
using Eden.Core.Data;
using Eden.Core.Database.Repositories;

namespace Eden.Zone.Codecs;

/// <summary>
/// Builders des paquets familiers (elf pets) — corps binaires natifs
/// vérifiés champ par champ sur le désassemblage zone :
///
/// - <b>412 ClientEnterNodeElfInfo</b> — <c>Serialize</c> @0x81e7xxx :
///   <c>[u16 count]</c> puis <c>count × 43 champs</c> (vecteurs
///   parallèles remplis dans le ctor @0x824dxxx depuis CKFElf) puis
///   <c>[s16]</c> terminal. Record (offsets CKFElf) :
///   <code>
///   s16 loc(+0x3c) | i32 elf_id(+4) | i32 owner_char(+8) |
///   i32 template_id(+0x10 = id item œuf) | s16 status(+0x14) |
///   i32 exp(+0x18) | i32 hp(+0x1c) | s16 (+0x20 maxHp→s16) |
///   s16 quality(+0x58) | s16 level(+0x24) | str name(+0x28) |
///   s16×4 grow(+0x2c) | s16 mix_star(+0x38) |
///   s16×5 battle(+0x40) | s16×5 integrate(+0x4c) |
///   u16 +0xa6 | u16 +0xa8 | s16 +0xaa | u8 bool +0xbd→u16 |
///   u16 +0xbe | u16 +0xc0 | s16 +0xc2 | u16 +0xce |
///   s16 +0xd0 | s16 +0xd2 | s16 +0xe0 | s16 +0xe2 |
///   u8 bool +0xe5→u16 | u16 +0xe6 | u16 +0xe8 | s16 +0xea
///   </code>
///   (les champs +0xa6..+0xea = données de loterie/mix — 0 pour les
///   elfs standards, la colonne mix_star reste portée à sa place).
/// - <b>413 ClientElfAdd</b> — <c>Serialize</c> @0x80f4xxx :
///   <c>[s16 loc][i32 elf_id][i32 template][s16 level][i32 exp]
///   [s16 hp][str name][s16 quality][u16 nGrow][nGrow×s16]
///   [s16 mix_star][u16 nBattle][n×s16][u16 nInteg][n×s16]</c>.
/// - <b>513 ClientPetShortCut</b> — <c>Serialize</c> @0x807eec0 :
///   <c>[u8 flag][s16 loc]</c> — 1 à l'invocation, 0 au renvoi
///   (émis par CallElf / CallElfBack natifs).
/// - <b>418 ClientElfUpdateAttr</b> — <c>Serialize</c> @0x807ecxx :
///   <c>[s16 loc][s16 type][i32 valeur][str]</c> — CallElf émet
///   type=2, valeur = CKFElf+0x1c (HP courant), chaîne vide.
/// </summary>
public static class NativeElfCodec
{
    /// <summary>Record 412 — un elf du tableau du perso. Ordre exact
    /// du ctor natif @0x824d5f0 (poussage des vecteurs) : pos5 =
    /// LEVEL (CKFElf+0x14, byte), pos8 = <b>local_18 non initialisé</b>
    /// (bug natif — le serveur pousse un s16 de pile indéfini ;
    /// le client l'ignore, on émet 0), pos9 = CKFElf+0x58 tronqué
    /// u16 (pointeur CElfPet live — non nul quand invoqué), pos10 =
    /// CKFElf+0x24 = CPartnerData+0x14 (GrowType).</summary>
    public sealed class ElfInfoRecord
    {
        public short Loc;              // CKFElf+0x3c
        public int ElfId;              // CKFElf+4
        public int OwnerCharId;        // CKFElf+8
        public int TemplateId;         // CKFElf+0x10 (id item œuf)
        public short Level;            // CKFElf+0x14 (byte→s16) — pos5
        public int Exp;                // CKFElf+0x18
        public int Hp;                 // CKFElf+0x1c
        // pos8 — local_18 NON INITIALISÉ dans le ctor natif : on
        // émet 0 (le client ne s'en sert pas).
        public short Status;           // CKFElf+0x58 → u16 — pos9
        public short Quality;          // CKFElf+0x24 (GrowType) — pos10
        public string Name = "";       // CKFElf+0x28
        public short[] Grow = new short[4];        // CKFElf+0x2c
        public short MixStar;                       // CKFElf+0x38
        public int[] Battle = new int[5];          // CKFElf+0x40
        public int[] Integrate = new int[5];       // CKFElf+0x4c
    }

    /// <summary>Construit le corps du 412 — count + 43 champs/elf +
    /// s16 terminal (ordre exact du Serialize natif).</summary>
    public static byte[] BuildEnterNodeElfInfo(
        IReadOnlyList<ElfInfoRecord> elfs, short tail = 0)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, EdenEncoding.Big5, true);
        w.Write((ushort)elfs.Count);                     // u16 : count
        foreach (var e in elfs)
        {
            w.Write(e.Loc);                              // s16  : loc
            w.Write(e.ElfId);                            // i32  : elf_id
            w.Write(e.OwnerCharId);                      // i32  : owner
            w.Write(e.TemplateId);                       // i32  : template
            w.Write(e.Level);                            // s16  : level
            w.Write(e.Exp);                              // i32  : exp
            w.Write(e.Hp);                               // i32  : hp
            w.Write((short)0);                           // s16  : garbage
            w.Write(e.Status);                           // s16  : +0x58
            w.Write(e.Quality);                          // s16  : quality
            WriteStr(w, e.Name);                         // str  : given_name
            for (var i = 0; i < 4; i++)                  // s16×4: grow
                w.Write(e.Grow[i]);
            w.Write(e.MixStar);                          // s16  : mix_star
            for (var i = 0; i < 5; i++)                  // s16×5: battle
                w.Write((short)e.Battle[i]);
            for (var i = 0; i < 5; i++)                  // s16×5: integ
                w.Write((short)e.Integrate[i]);
            // 17 champs loterie/mix — ordre du Serialize natif
            // (zone/decompiled.c:339153+) : +0x158 u16, +0x164 s16,
            // +0x170 s16, +0x17c s16, +0x188 bit→u16, +0x19c u16,
            // +0x1a8 u16, +0x1b4 s16, +0x1c0 u16, +0x1cc s16,
            // +0x1d8 s16, +0x1e4 s16, +0x1f0 s16, +0x1fc bit→u16,
            // +0x210 u16, +0x21c u16, +0x228 s16. Un champ omis
            // = record 2 octets trop court → le client lit au-delà
            // du paquet (int3 invalid-param) dès qu'un elf existe.
            w.Write((ushort)0);                          // u16 : +0x158
            w.Write((short)0);                           // s16 : +0x164
            w.Write((short)0);                           // s16 : +0x170
            w.Write((short)0);                           // s16 : +0x17c
            w.Write((ushort)0);                          // u16 : bool +0x188
            w.Write((ushort)0);                          // u16 : +0x19c
            w.Write((ushort)0);                          // u16 : +0x1a8
            w.Write((short)0);                           // s16 : +0x1b4
            w.Write((ushort)0);                          // u16 : +0x1c0
            w.Write((short)0);                           // s16 : +0x1cc
            w.Write((short)0);                           // s16 : +0x1d8
            w.Write((short)0);                           // s16 : +0x1e4
            w.Write((short)0);                           // s16 : +0x1f0
            w.Write((ushort)0);                          // u16 : bool +0x1fc
            w.Write((ushort)0);                          // u16 : +0x210
            w.Write((ushort)0);                          // u16 : +0x21c
            w.Write((short)0);                           // s16 : +0x228
        }
        w.Write(tail);                                   // s16 : terminal
        return ms.ToArray();
    }

    /// <summary>Construit le corps du 413 (elf ajouté au tableau) —
    /// ordre exact du Serialize natif (vecteurs à compteur u16).</summary>
    public static byte[] BuildElfAdd(ElfInfoRecord e)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, EdenEncoding.Big5, true);
        w.Write(e.Loc);                                  // s16  : loc
        w.Write(e.ElfId);                                // i32  : elf_id
        w.Write(e.TemplateId);                           // i32  : template
        w.Write(e.Level);                                // s16  : level
        w.Write(e.Exp);                                  // i32  : exp
        w.Write((short)e.Hp);                            // s16  : hp
        WriteStr(w, e.Name);                             // str  : name
        w.Write(e.Quality);                              // s16  : quality
        w.Write((ushort)e.Grow.Length);                  // u16  : n grow
        for (var i = 0; i < e.Grow.Length; i++)
            w.Write(e.Grow[i]);                          // s16×n: grow
        w.Write(e.MixStar);                              // s16  : mix_star
        w.Write((ushort)e.Battle.Length);                // u16  : n battle
        for (var i = 0; i < e.Battle.Length; i++)
            w.Write((short)e.Battle[i]);                 // s16×n: battle
        w.Write((ushort)e.Integrate.Length);             // u16  : n integ
        for (var i = 0; i < e.Integrate.Length; i++)
            w.Write((short)e.Integrate[i]);              // s16×n: integ
        return ms.ToArray();
    }

    /// <summary>Corps du 513 ClientPetShortCut — <c>[u8 flag][s16
    /// loc]</c> : 1 invocation, 0 renvoi.</summary>
    public static byte[] BuildPetShortCut(byte flag, short loc)
        => new byte[] { flag, (byte)loc, (byte)(loc >> 8) };

    /// <summary>Corps du 418 ClientElfUpdateAttr — <c>[s16 loc]
    /// [s16 type][i32 valeur][str]</c> (CallElf : type=2, valeur =
    /// HP courant, chaîne vide).</summary>
    public static byte[] BuildElfUpdateAttr(short loc, short type,
        int value, string text = "")
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, EdenEncoding.Big5, true);
        w.Write(loc);                                    // s16 : loc
        w.Write(type);                                   // s16 : type
        w.Write(value);                                  // i32 : valeur
        WriteStr(w, text);                               // str : texte
        return ms.ToArray();
    }

    /// <summary>Corps du 568 ClientUpdateElfMixElf — résultat de la
    /// fusion (ctor natif @0x807a3f0 dans DoElfUpgrade :
    /// obj 0x3c = loc+elfId+star + 3 vecteurs s16) :
    /// <code>
    ///   [s16 loc][i32 elf_id][s16 mix_star]
    ///   [u16 n][n×s16 grow][u16 n][n×s16 battle][u16 n][n×s16 integrate]
    /// </code>
    /// Le classe générée aplatit les vecteurs (u16+1 s16) — inutilisable
    /// pour n=5, d'où ce builder dédié.</summary>
    public static byte[] BuildUpdateElfMixElf(short loc, int elfId,
        short mixStar, IReadOnlyList<short> grow,
        IReadOnlyList<int> battle, IReadOnlyList<int> integrate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(loc);                                   // s16 : loc elf
        w.Write(elfId);                                 // i32 : elf_id
        w.Write(mixStar);                               // s16 : mix_star
        w.Write((ushort)grow.Count);                    // u16 : n grow
        foreach (var g in grow) w.Write(g);             // s16×n
        w.Write((ushort)battle.Count);                  // u16 : n battle
        foreach (var b in battle) w.Write((short)b);    // s16×n
        w.Write((ushort)integrate.Count);               // u16 : n integ
        foreach (var i in integrate) w.Write((short)i); // s16×n
        return ms.ToArray();
    }

    /// <summary>Corps du 588 ClientUpdateElfSpell — sort appris par
    /// l'elf (ctor natif dans CKFElf::LearnSpell @0x8455b30 :
    /// `[s16 loc][u16 n][n×s16 battle][u16 n][n×s16 integrate]`).</summary>
    public static byte[] BuildUpdateElfSpell(short loc,
        IReadOnlyList<int> battle, IReadOnlyList<int> integrate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(loc);                                   // s16 : loc elf
        w.Write((ushort)battle.Count);                  // u16 : n battle
        foreach (var b in battle) w.Write((short)b);    // s16×n
        w.Write((ushort)integrate.Count);               // u16 : n integ
        foreach (var i in integrate) w.Write((short)i); // s16×n
        return ms.ToArray();
    }

    /// <summary>Corps du 414 ClientElfReturnOK —
    /// <c>[s16 loc][s16 flag]</c> (Serialize @0x807ecb0).</summary>
    public static byte[] BuildElfReturnOK(short loc, short flag = 0)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(0), loc);
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(2), flag);
        return b;
    }

    /// <summary>
    /// Corps du 424 ClientEnterNodeElfContainer — snapshot des
    /// conteneurs d'équipement elf à la demande du client (NCID 113
    /// CNE_CZ_ZoneServerEnterNodeElfContainer →
    /// <c>Execute</c> @0x824ce80 qui sérialise la map char+0x540).
    /// Format prouvé par
    /// <c>CNC_CZ_ClientEnterNodeElfContainer::Serialize</c>
    /// @0x8230030 + le ctor @0x82309c0 :
    /// <code>
    ///   [u8 nContainers]
    ///   par conteneur :
    ///     [s16 capacity]          — taille du vecteur de slots (4)
    ///     [s8  elfLoc]            — clé de la map (loc de l'elf,
    ///                             tronquée u8 par le ctor natif)
    ///     [u16 blobLen][blob]     — bitmap d'occupation MSB-first
    ///                             (slot i → bit 0x80>>(i%8), comme
    ///                             le 312 — ctor : local_12<<=1 par
    ///                             slot, bit0 posé puis décalé)
    ///     records d'items triés par slot — même layout que
    ///     <see cref="NativeItemCodec.WriteItem"/> SANS la queue
    ///     fixe 34B (la boucle s'arrête après le mask+conditionnels).
    /// </code>
    /// </summary>
    /// <param name="containers">(loc elf, items du conteneur avec
    /// Slot = 0-3). La capacité est fixe 4 — slots 0-1 pour les
    /// EquipLoc 7/19 (arme pet), 2-3 pour 10/34 (collier) — désasm
    /// <c>ElfEquip</c> @0x808cb20.</param>
    public static byte[] BuildEnterNodeElfContainer(
        IReadOnlyList<(short ElfLoc, IReadOnlyList<Eden.Core.Game
            .Inventory.ItemEntity> Items)> containers)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write((byte)containers.Count);         // u8 — taille de la map
        foreach (var (elfLoc, items) in containers)
        {
            const int cap = 4;                   // 4 slots natifs
            w.Write((short)cap);                 // s16 — GetContainerSize
            w.Write((sbyte)elfLoc);              // s8 — clé map (elf loc)
            var blob = new byte[(cap + 7) / 8];
            foreach (var it in items)
                if (it.Slot >= 0 && it.Slot < cap)
                    blob[it.Slot / 8] |= (byte)(0x80 >> (it.Slot & 7));
            w.Write((ushort)blob.Length);        // u16 — taille du blob
            w.Write(blob);                       // bitmap MSB-first
            foreach (var it in items.OrderBy(i => i.Slot))
                NativeItemCodec.WriteItem(w, it.ItemId,
                    (short)it.Durability, (short)it.MaxDurability,
                    (byte)(it.IsBound ? 0x18 : 0x08),
                    (short)it.EnchantLevel, it.CreateTime, it.DueDate,
                    it, emitTail: false);        // pas de queue 34B
        }
        return ms.ToArray();
    }

    /// <summary>Chaîne préfixée u16 (WriteString natif —
    /// <c>COutBinStream::operator&lt;&lt;(string)</c> : s16 len +
    /// octets bruts Big5).</summary>
    private static void WriteStr(BinaryWriter w, string s)
    {
        var bytes = EdenEncoding.Big5.GetBytes(s ?? string.Empty);
        w.Write((ushort)bytes.Length);
        w.Write(bytes);
    }
}
