using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages zone du système de familiers (elf pets) — lot couvrant
// l'instantané du tableau (412), les mises à jour (413/414/418/568/
// 588), le cycle de vie (513/470/567) et les requêtes C→S (107/108/
// 109/137/187/188/227/228/229).
//
// Sources de preuve (toutes côté serveur, lecture seule) :
//   - $EDEN/server/src/Eden.Zone/Codecs/NativeElfCodec.cs — layouts
//     412/413/418/513/568/574/575/588/414/424 prouvés par
//     désassemblage des Serialize/ctor natifs (adresses citées dans
//     les commentaires serveur : Serialize 412 @0x81e7xxx, ctor
//     @0x824dxxx, 413 @0x80f4xxx, 513 @0x807eec0, 418 @0x807ecxx,
//     568 ctor @0x807a3f0, 588 ctor @0x8455b30, 424 Serialize
//     @0x8230030 + ctor @0x82309c0).
//   - $EDEN/server/.../Generated/ZoneMessages.g.cs — layouts aplatis
//     des paquets fixes (417/419/420/431/442/470/513/567/574/575) et
//     des requêtes C→S (107/137/187/188/227/228/229).
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.Elf{,.Work,.Spell,
//     .Mix}.cs — sémantique des actions (ToWork @0x807c780, CallElf
//     @0x8072c30, CallElfBack, ReturnElf/ReturnElfToEgg @0x8249df0,
//     SendPetCmd @0x824axxx, ElfMix CPetUpgradeOp).
//
// NOTA BENE « faim/humeur » : le snapshot 412 natif ne transporte NI
// faim NI humeur NI énergie (champs +0xa6..+0xea = données de
// loterie/mix, tous émis à 0). ElfService serveur a Mood/Energy/
// Friendship en mémoire mais n'est pas relié au tableau elf réel —
// l'UI n'affiche donc ces champs que comme « non transmis ».

// ---------- C→S ----------

/// <summary>C→S NCID 107 — ElfWork : change le mode de travail de
/// l'elf. Wire natif <c>CNE_CZ_ZoneServerElfWork</c> :
/// <c>[s16 loc][s16 mode]</c> — mode 0 = ToIdle (rappel / sort du mix),
/// 1 = ToMix (slot d'intégration), 2 = CallElf (invocation) ;
/// 7/8/9 = loteries. Le serveur intercepte ce NCID dans
/// <c>ZoneEntryFlow.TryHandleAsync</c> (gate de carte S_Node
/// col42 + ToWork natif).</summary>
public sealed class ZoneElfWork : NetMessage
{
    public override int Opcode => 107;
    public override string Name => "Zone.ZoneServerElfWork";
    /// <summary>s16 — loc (slot) de l'elf dans le tableau.</summary>
    public short Loc { get; set; }
    /// <summary>s16 — mode demandé (0 repos, 1 mix, 2 invoquer).</summary>
    public short Mode { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Loc);          // s16 — loc de l'elf
        s.WriteShort(Mode);         // s16 — mode ToWork
    }
    public override void Deserialize(BinReader s)
    {
        Loc = s.ReadShort();
        Mode = s.ReadShort();
    }
}

/// <summary>C→S NCID 108 — ElfReturn : libération (toEgg=0) ou
/// scellement en œuf (toEgg≠0). Wire natif
/// <c>CNE_CZ_ZoneServerElfReturn</c> : <c>[s16 loc][u8 toEgg]
/// [s16 sealBag][s16 sealSlot]</c> — sealBag/sealSlot = item sceau à
/// consommer ; −1/−1 = voie « interface » payée 10 000 or (server
/// <c>HandleElfReturnAsync</c>, désasm Execute @0x8249df0).</summary>
public sealed class ZoneElfReturn : NetMessage
{
    public override int Opcode => 108;
    public override string Name => "Zone.ZoneServerElfReturn";
    /// <summary>s16 — loc de l'elf.</summary>
    public short Loc { get; set; }
    /// <summary>u8 — 0 = libérer, ≠0 = sceller en œuf.</summary>
    public byte ToEgg { get; set; }
    /// <summary>s16 — conteneur du sceau (−1 = voie or).</summary>
    public short SealBag { get; set; } = -1;
    /// <summary>s16 — slot du sceau (−1 = voie or).</summary>
    public short SealSlot { get; set; } = -1;

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Loc);          // s16 — loc de l'elf
        s.WriteByte(ToEgg);         // u8  — flag « retour en œuf »
        s.WriteShort(SealBag);      // s16 — bag du sceau (−1 = or)
        s.WriteShort(SealSlot);     // s16 — slot du sceau (−1 = or)
    }
    public override void Deserialize(BinReader s)
    {
        Loc = s.ReadShort();
        ToEgg = s.ReadByte();
        SealBag = s.ReadShort();
        SealSlot = s.ReadShort();
    }
}

/// <summary>C→S NCID 109 — ElfCallElf : invocation directe. Wire natif
/// <c>CNE_CZ_ZoneServerElfCallElf</c> : <c>[s16 loc][s16][s16][u8]</c>
/// — le serveur ne lit que le loc (payload[0]) ; les 3 champs de
/// queue sont inusités (server <c>HandleElfCallElfAsync</c> —
/// CallElf natif @0x8072c30 : cooldown 60 s, BornElfPet, 513+418).
/// </summary>
public sealed class ZoneElfCallElf : NetMessage
{
    public override int Opcode => 109;
    public override string Name => "Zone.ZoneServerElfCallElf";
    /// <summary>s16 — loc de l'elf à invoquer.</summary>
    public short Loc { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Loc);          // s16 — loc
        s.WriteShort(0);            // s16 — inusité natif
        s.WriteShort(0);            // s16 — inusité natif
        s.WriteByte(0);             // u8  — inusité natif
    }
    public override void Deserialize(BinReader s)
    {
        Loc = s.ReadShort();
        if (s.Remaining >= 5) { s.ReadShort(); s.ReadShort(); s.ReadByte(); }
    }
}

/// <summary>C→S NCID 137 — ElfTalk « caresser ». Wire natif
/// <c>CNE_CZ_ZoneServerElfTalk</c> : <c>[s16 elfId]</c>. MORT EN
/// NATIF (Execute = tail-call GetType ignoré) — le serveur C# y
/// ajoute +1 friendship via ElfService (chemin assumé, sans effet
/// sur les elfs réels du tableau). Envoyé pour fidélité UI.</summary>
public sealed class ZoneElfTalk : NetMessage
{
    public override int Opcode => 137;
    public override string Name => "Zone.ZoneServerElfTalk";
    /// <summary>s16 — identifiant d'instance de l'elf (elfId).</summary>
    public short ElfId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(ElfId);
    public override void Deserialize(BinReader s) => ElfId = s.ReadShort();
}

/// <summary>C→S NCID 187 — PetModeChange : mode passif/combat du pet
/// invoqué. Wire natif <c>CNE_CZ_ZoneServerPetModeChange</c> :
/// <c>[s16 mode]</c> — 2 = passif (pet+0x3ec, ne combat pas) ;
/// autres = assiste (server <c>HandlePetModeChangeAsync</c>).</summary>
public sealed class ZonePetModeChange : NetMessage
{
    public override int Opcode => 187;
    public override string Name => "Zone.ZoneServerPetModeChange";
    /// <summary>s16 — mode (2 = passif ; sinon assiste/combat).</summary>
    public short Mode { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(Mode);
    public override void Deserialize(BinReader s) => Mode = s.ReadShort();
}

/// <summary>C→S NCID 188 — SendPetCmd : ordre au pet invoqué. Wire
/// natif <c>CNE_CZ_ZoneServerSendPetCmd</c> : <c>[s16 instr]
/// [i32 cible][s16][i32]</c> (server <c>HandleSendPetCmdAsync</c> :
/// instr 1 = attaque la cible (SetPetInstruction 100 + mode 1) ;
/// instrs 6/7/8 = emotes ; instrs 101-105 = cast battle_spell[i-101]
/// sur la cible). Le serveur relaie 470 ClientPlayElfPetAction.</summary>
public sealed class ZoneSendPetCmd : NetMessage
{
    public override int Opcode => 188;
    public override string Name => "Zone.ZoneServerSendPetCmd";
    /// <summary>s16 — instruction (1 attaque, 6-8 emotes, 101-105 sorts).</summary>
    public short Instruction { get; set; }
    /// <summary>i32 — handle de la cible (sight-handle mob).</summary>
    public int Target { get; set; }
    /// <summary>s16 — inusité natif (0).</summary>
    public short Field3 { get; set; }
    /// <summary>i32 — inusité natif (0).</summary>
    public int Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Instruction);  // s16 — instruction pet
        s.WriteInt(Target);         // i32 — handle cible
        s.WriteShort(Field3);       // s16 — inusité
        s.WriteInt(Field4);         // i32 — inusité
    }
    public override void Deserialize(BinReader s)
    {
        Instruction = s.ReadShort();
        Target = s.ReadInt();
        Field3 = s.ReadShort();
        Field4 = s.ReadInt();
    }
}

/// <summary>C→S NCID 227 — ElfMix : fusion de deux elfs. Wire natif
/// <c>CNE_CZ_ZoneServerElfMix</c> : <c>[s16 elfLoc1][s16 elfLoc2]
/// [s16 bag][s16 slot]</c> — elfLoc2 = familier consommé, bag/slot =
/// item catalyseur (server <c>HandleElfMixAsync</c> → CPetUpgradeOp :
/// 574 casting, 568 résultat, 414 pour l'elf consommé).</summary>
public sealed class ZoneElfMix : NetMessage
{
    public override int Opcode => 227;
    public override string Name => "Zone.ZoneServerElfMix";
    /// <summary>s16 — loc de l'elf qui reçoit la fusion.</summary>
    public short LocMain { get; set; }
    /// <summary>s16 — loc de l'elf consommé.</summary>
    public short LocConsumed { get; set; }
    /// <summary>s16 — conteneur du catalyseur (−1 si aucun).</summary>
    public short MaterialBag { get; set; } = -1;
    /// <summary>s16 — slot du catalyseur (−1 si aucun).</summary>
    public short MaterialSlot { get; set; } = -1;

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(LocMain);      // s16 — elf1 (receveur)
        s.WriteShort(LocConsumed);  // s16 — elf2 (consommé)
        s.WriteShort(MaterialBag);  // s16 — sac du catalyseur
        s.WriteShort(MaterialSlot); // s16 — slot du catalyseur
    }
    public override void Deserialize(BinReader s)
    {
        LocMain = s.ReadShort();
        LocConsumed = s.ReadShort();
        MaterialBag = s.ReadShort();
        MaterialSlot = s.ReadShort();
    }
}

/// <summary>C→S NCID 228 — ElfChangeName : renommage. Wire natif
/// <c>CNE_CZ_ZoneServerElfChangeName</c> : <c>[s16 loc][u16str nom]
/// </c> — le serveur exige le parchemin 40056/40156 en inventaire
/// (server <c>HandleElfChangeNameAsync</c>) ; réponse 418 type 12.</summary>
public sealed class ZoneElfChangeName : NetMessage
{
    public override int Opcode => 228;
    public override string Name => "Zone.ZoneServerElfChangeName";
    /// <summary>s16 — loc de l'elf.</summary>
    public short Loc { get; set; }
    /// <summary>str — nouveau nom affiché.</summary>
    public string NewName { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Loc);          // s16 — loc
        s.WriteString(NewName);     // str — nouveau nom (u16 + Big5)
    }
    public override void Deserialize(BinReader s)
    {
        Loc = s.ReadShort();
        NewName = s.ReadString();
    }
}

/// <summary>C→S NCID 229 — ElfRevive : « nourrir »/ranimer l'elf.
/// Wire natif <c>CNE_CZ_ZoneServerElfRevive</c> : <c>[s16 elfId]</c>.
/// MORT EN NATIF (Execute @0x824a1e9 = tail-call GetType ignoré,
/// objdump) — le serveur C# y branche ElfService.Feed(100,50)
/// (chemin assumé, sans effet sur les elfs réels). Gardé pour
/// fidélité du bouton « nourrir ».</summary>
public sealed class ZoneElfRevive : NetMessage
{
    public override int Opcode => 229;
    public override string Name => "Zone.ZoneServerElfRevive";
    /// <summary>s16 — identifiant d'instance de l'elf.</summary>
    public short ElfId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(ElfId);
    public override void Deserialize(BinReader s) => ElfId = s.ReadShort();
}

// ---------- S→C ----------

/// <summary>Un elf du tableau (record du 412 / ajout du 413).</summary>
public sealed class ElfRecord
{
    /// <summary>s16 — loc (slot du tableau).</summary>
    public short Loc;
    /// <summary>i32 — elf_id (clé BDD).</summary>
    public int ElfId;
    /// <summary>i32 — charId propriétaire (412 seul).</summary>
    public int OwnerCharId;
    /// <summary>i32 — template = id de l'item œuf (C_Partner.ini).</summary>
    public int TemplateId;
    /// <summary>s16 — niveau.</summary>
    public short Level;
    /// <summary>i32 — expérience.</summary>
    public int Exp;
    /// <summary>i32 — HP courant (« vitalité »).</summary>
    public int Hp;
    /// <summary>s16 — status CKFElf+0x58 (0 repos, 1 mix, 2 invoqué,
    /// 7/8/9 loterie).</summary>
    public short Status;
    /// <summary>s16 — quality = CPartnerData+0x14 GrowType (pas une
    /// « qualité » au sens loot — doc ElfInfoRecord serveur).</summary>
    public short Quality;
    /// <summary>str — nom donné (vide = nom du partner).</summary>
    public string Name = "";
    /// <summary>s16×4 — indices de croissance (attack/hit/crit/atkspd).</summary>
    public short[] Grow = new short[4];
    /// <summary>s16 — étoile de mix.</summary>
    public short MixStar;
    /// <summary>s16×5 — ids des sorts de combat.</summary>
    public int[] Battle = new int[5];
    /// <summary>s16×5 — ids des sorts intégrés (mix).</summary>
    public int[] Integrate = new int[5];
}

/// <summary>S→C NCID 412 — ClientEnterNodeElfInfo : tableau complet
/// des elfs (réponse au 110). Corps :
/// <c>[u16 count][count × record][s16 terminal]</c>. Record (ordre
/// exact du Serialize natif, NativeElfCodec.BuildEnterNodeElfInfo) :
/// <code>
/// s16 loc | i32 elfId | i32 owner | i32 template | s16 level |
/// i32 exp | i32 hp | s16 garbage | s16 status | s16 quality |
/// u16str name | s16×4 grow | s16 mixStar | s16×5 battle |
/// s16×5 integrate | 17 champs mix/loterie | …
/// </code>
/// (17 champs = u16, s16, s16, s16, u16, u16, u16, s16, u16, s16,
/// s16, s16, s16, u16, u16, u16, s16 — tous à 0 pour les elfs
/// standards mais obligatoires pour ne pas désynchroniser le corps.)
/// </summary>
public sealed class ZoneEnterNodeElfInfo : NetMessage
{
    public override int Opcode => 412;
    public override string Name => "Zone.ClientEnterNodeElfInfo";
    /// <summary>Elfs du tableau (ordre du serveur).</summary>
    public List<ElfRecord> Elfs { get; } = new();

    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        var count = s.ReadUShort();                    // u16 — nb elfs
        for (var i = 0; i < count; i++)
        {
            var e = new ElfRecord
            {
                Loc = s.ReadShort(),                   // s16 — loc
                ElfId = s.ReadInt(),                   // i32 — elf_id
                OwnerCharId = s.ReadInt(),             // i32 — owner
                TemplateId = s.ReadInt(),              // i32 — template
                Level = s.ReadShort(),                 // s16 — level
                Exp = s.ReadInt(),                     // i32 — exp
                Hp = s.ReadInt(),                      // i32 — hp
            };
            s.ReadShort();                             // s16 — garbage natif
            e.Status = s.ReadShort();                  // s16 — +0x58
            e.Quality = s.ReadShort();                 // s16 — +0x24 growtype
            e.Name = s.ReadString();                   // str — given_name
            for (var g = 0; g < 4; g++)
                e.Grow[g] = s.ReadShort();             // s16×4 — grow
            e.MixStar = s.ReadShort();                 // s16 — mix_star
            for (var b = 0; b < 5; b++)
                e.Battle[b] = s.ReadShort();           // s16×5 — battle
            for (var t = 0; t < 5; t++)
                e.Integrate[t] = s.ReadShort();        // s16×5 — integ
            // 17 champs loterie/mix — ordre du Serialize natif
            // (zone/decompiled.c:339153+) ; tous lus et ignorés
            // (u16 s16 s16 s16 u16 u16 u16 s16 u16 s16 s16 s16 s16
            // u16 u16 u16 s16 — cf. NativeElfCodec lignes 107-123).
            s.ReadUShort(); s.ReadShort(); s.ReadShort(); s.ReadShort();
            s.ReadUShort(); s.ReadUShort(); s.ReadUShort(); s.ReadShort();
            s.ReadUShort(); s.ReadShort(); s.ReadShort(); s.ReadShort();
            s.ReadShort(); s.ReadUShort(); s.ReadUShort(); s.ReadUShort();
            s.ReadShort();
            Elfs.Add(e);
        }
        if (s.Remaining >= 2) s.ReadShort();           // s16 — terminal
    }
}

/// <summary>S→C NCID 413 — ClientElfAdd : un elf entre dans le
/// tableau (après usage d'un œuf). Corps :
/// <c>[s16 loc][i32 elfId][i32 template][s16 level][i32 exp][s16 hp]
/// [str name][s16 quality][u16 nGrow][nGrow×s16][s16 mixStar]
/// [u16 nBattle][n×s16][u16 nInteg][n×s16]</c>
/// (NativeElfCodec.BuildElfAdd).</summary>
public sealed class ZoneElfAdd : NetMessage
{
    public override int Opcode => 413;
    public override string Name => "Zone.ClientElfAdd";
    /// <summary>Record complet de l'elf ajouté.</summary>
    public ElfRecord? Elf { get; private set; }

    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        var e = new ElfRecord
        {
            Loc = s.ReadShort(),                       // s16 — loc
            ElfId = s.ReadInt(),                       // i32 — elf_id
            TemplateId = s.ReadInt(),                  // i32 — template
            Level = s.ReadShort(),                     // s16 — level
            Exp = s.ReadInt(),                         // i32 — exp
            Hp = s.ReadShort(),                        // s16 — hp (court)
        };
        e.Name = s.ReadString();                       // str — given_name
        e.Quality = s.ReadShort();                     // s16 — quality
        var nGrow = s.ReadUShort();                    // u16 — n grow
        for (var i = 0; i < nGrow && i < 4; i++)
            e.Grow[i] = s.ReadShort();
        for (var i = 4; i < nGrow; i++) s.ReadShort();
        e.MixStar = s.ReadShort();                     // s16 — mix_star
        var nBattle = s.ReadUShort();                  // u16 — n battle
        for (var i = 0; i < nBattle && i < 5; i++)
            e.Battle[i] = s.ReadShort();
        for (var i = 5; i < nBattle; i++) s.ReadShort();
        var nInteg = s.ReadUShort();                   // u16 — n integ
        for (var i = 0; i < nInteg && i < 5; i++)
            e.Integrate[i] = s.ReadShort();
        for (var i = 5; i < nInteg; i++) s.ReadShort();
        Elf = e;
    }
}

/// <summary>S→C NCID 414 — ClientElfReturnOK : l'elf a quitté le
/// tableau (libération/scellement/fusion consommée). Corps
/// <c>[s16 loc][s16 flag]</c> (Serialize @0x807ecb0).</summary>
public sealed class ZoneElfReturnOk : NetMessage
{
    public override int Opcode => 414;
    public override string Name => "Zone.ClientElfReturnOK";
    /// <summary>s16 — loc de l'elf retiré.</summary>
    public short Loc { get; set; }
    /// <summary>s16 — flag natif (1 observé).</summary>
    public short Flag { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Loc); s.WriteShort(Flag);
    }
    public override void Deserialize(BinReader s)
    {
        Loc = s.ReadShort(); Flag = s.ReadShort();
    }
}

/// <summary>S→C NCID 418 — ClientElfUpdateAttr : attribut ponctuel
/// d'un elf. Corps <c>[s16 loc][s16 type][i32 valeur][u16str]</c>
/// (BuildElfUpdateAttr). Types émis par le serveur C# : 1 = exp,
/// 2 = hp, 8 = attack, 9 = hit, 10 = crit, 11 = atkspd (les 4 =
/// stats du nouveau niveau, CPartnerGrowData), 12 = nom (str).</summary>
public sealed class ZoneElfUpdateAttr : NetMessage
{
    public override int Opcode => 418;
    public override string Name => "Zone.ClientElfUpdateAttr";
    /// <summary>s16 — loc de l'elf.</summary>
    public short Loc { get; set; }
    /// <summary>s16 — type d'attribut (1 exp, 2 hp, 8-11 stats, 12 nom).</summary>
    public short AttrType { get; set; }
    /// <summary>i32 — valeur entière (0 pour le nom).</summary>
    public int Value { get; set; }
    /// <summary>str — texte (nom pour type 12).</summary>
    public string Text { get; set; } = "";

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Loc); s.WriteShort(AttrType);
        s.WriteInt(Value); s.WriteString(Text);
    }
    public override void Deserialize(BinReader s)
    {
        Loc = s.ReadShort(); AttrType = s.ReadShort();
        Value = s.ReadInt(); Text = s.ReadString();
    }
}

/// <summary>S→C NCID 424 — ClientEnterNodeElfContainer : snapshot
/// des conteneurs d'équipement elf (réponse au 113). Corps
/// (BuildEnterNodeElfContainer, Serialize @0x8230030) :
/// <code>
/// [u8 nContainers]
/// par conteneur : [s16 capacity][s8 elfLoc][u16 blobLen][bitmap
/// MSB-first][records d'items — WriteItem SANS la queue 34B]
/// </code>
/// Le nombre d'items d'un conteneur = bits posés du bitmap.</summary>
public sealed class ZoneEnterNodeElfContainer : NetMessage
{
    /// <summary>Équipement d'un elf : (slot 0-3, itemId).</summary>
    public sealed class ElfEquipSlot
    {
        /// <summary>s8 — loc de l'elf propriétaire.</summary>
        public sbyte ElfLoc;
        /// <summary>Slot d'équipement (0-3 : armes puis colliers).</summary>
        public int Slot;
        /// <summary>ItemId équipé.</summary>
        public int ItemId;
        /// <summary>Quantité (amount du record).</summary>
        public int Amount;
    }
    /// <summary>Lignes lues (vide si aucun elf / conteneur vide).</summary>
    public List<ElfEquipSlot> Items { get; } = new();

    public override int Opcode => 424;
    public override string Name => "Zone.ClientEnterNodeElfContainer";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        var count = s.ReadByte();                      // u8 — n conteneurs
        for (var c = 0; c < count; c++)
        {
            var cap = s.ReadShort();                   // s16 — capacité (4)
            var elfLoc = (sbyte)s.ReadByte();          // s8 — elfLoc
            var blobLen = s.ReadUShort();              // u16 — len bitmap
            var blob = s.ReadBytes(blobLen);           // bitmap MSB-first
            // Les records suivent l'ordre des bits posés du bitmap
            // (triés par slot, WriteItem sans queue 34B).
            for (var i = 0; i < cap && i < blobLen * 8; i++)
            {
                if ((blob[i / 8] & (0x80 >> (i & 7))) == 0) continue;
                if (!NativeItemRecord.TryReadElfContainerItem(s,
                        out var item)) return;
                Items.Add(new ElfEquipSlot
                {
                    ElfLoc = elfLoc, Slot = i,
                    ItemId = item.ItemId, Amount = item.Amount,
                });
            }
        }
    }
}

/// <summary>S→C NCID 417 — ClientElfEvent : évènement elf (format
/// aplati généré <c>[s16][s16][i32][s16]</c>). Non émis par le
/// serveur C# — gardé pour ne pas logguer « non géré ».</summary>
public sealed class ZoneElfEvent : NetMessage
{
    public override int Opcode => 417;
    public override string Name => "Zone.ClientElfEvent";
    /// <summary>s16 — elfId.</summary>
    public short ElfId { get; set; }
    /// <summary>s16 — code d'évènement.</summary>
    public short Event { get; set; }
    /// <summary>i32 — paramètre.</summary>
    public int Param { get; set; }
    /// <summary>s16 — queue.</summary>
    public short Tail { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(ElfId); s.WriteShort(Event);
        s.WriteInt(Param); s.WriteShort(Tail);
    }
    public override void Deserialize(BinReader s)
    {
        ElfId = s.ReadShort(); Event = s.ReadShort();
        Param = s.ReadInt();
        if (s.Remaining >= 2) Tail = s.ReadShort();
    }
}

/// <summary>S→C NCID 419 — ClientElfUpdateSkill : mise à jour d'un
/// sort elf (format aplati généré <c>[s16×4][i32][s16]</c>). Non
/// émis par le serveur C# — l'apprentissage passe par 588.</summary>
public sealed class ZoneElfUpdateSkill : NetMessage
{
    public override int Opcode => 419;
    public override string Name => "Zone.ClientElfUpdateSkill";
    /// <summary>s16 — champ 1.</summary>
    public short Field1 { get; set; }
    /// <summary>s16 — champ 2.</summary>
    public short Field2 { get; set; }
    /// <summary>s16 — champ 3.</summary>
    public short Field3 { get; set; }
    /// <summary>s16 — champ 4.</summary>
    public short Field4 { get; set; }
    /// <summary>i32 — champ 5.</summary>
    public int Field5 { get; set; }
    /// <summary>s16 — champ 6.</summary>
    public short Field6 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1); s.WriteShort(Field2); s.WriteShort(Field3);
        s.WriteShort(Field4); s.WriteInt(Field5); s.WriteShort(Field6);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort(); Field2 = s.ReadShort(); Field3 = s.ReadShort();
        Field4 = s.ReadShort(); Field5 = s.ReadInt(); Field6 = s.ReadShort();
    }
}

/// <summary>S→C NCID 420 — ClientElfUpdateSoulAbility (format aplati
/// généré <c>[s16×4][u16]</c>). Non émis par le serveur C#.</summary>
public sealed class ZoneElfUpdateSoulAbility : NetMessage
{
    public override int Opcode => 420;
    public override string Name => "Zone.ClientElfUpdateSoulAbility";
    /// <summary>s16 — champs génériques.</summary>
    public short Field1 { get; set; }
    /// <summary>s16 — champ 2.</summary>
    public short Field2 { get; set; }
    /// <summary>s16 — champ 3.</summary>
    public short Field3 { get; set; }
    /// <summary>s16 — champ 4.</summary>
    public short Field4 { get; set; }
    /// <summary>u16 — champ 5.</summary>
    public ushort Field5 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1); s.WriteShort(Field2); s.WriteShort(Field3);
        s.WriteShort(Field4); s.WriteShort((short)Field5);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort(); Field2 = s.ReadShort(); Field3 = s.ReadShort();
        Field4 = s.ReadShort(); Field5 = s.ReadUShort();
    }
}

/// <summary>S→C NCID 431 — ClientChangeElf : broadcast « joueur X a
/// invoqué/renommé l'elf Y » (ancien chemin ElfHandlers serveur).
/// Corps <c>[i32 charId][i32 elfDefId][u16str nom]</c>.</summary>
public sealed class ZoneChangeElf : NetMessage
{
    public override int Opcode => 431;
    public override string Name => "Zone.ClientChangeElf";
    /// <summary>i32 — charId du propriétaire.</summary>
    public int CharId { get; set; }
    /// <summary>i32 — id de définition elf.</summary>
    public int ElfDefId { get; set; }
    /// <summary>str — nom affiché.</summary>
    public string ElfName { get; set; } = "";
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId); s.WriteInt(ElfDefId); s.WriteString(ElfName);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt(); ElfDefId = s.ReadInt(); ElfName = s.ReadString();
    }
}

/// <summary>S→C NCID 442 — ClientElfAction : action elf (format
/// aplati généré <c>[s16×3][i32][s16][i32]</c>). Non émis par le
/// serveur C# — l'animation du pet live arrive par 470.</summary>
public sealed class ZoneElfAction : NetMessage
{
    public override int Opcode => 442;
    public override string Name => "Zone.ClientElfAction";
    /// <summary>s16 — champ 1.</summary>
    public short Field1 { get; set; }
    /// <summary>s16 — champ 2.</summary>
    public short Field2 { get; set; }
    /// <summary>s16 — champ 3.</summary>
    public short Field3 { get; set; }
    /// <summary>i32 — champ 4.</summary>
    public int Field4 { get; set; }
    /// <summary>s16 — champ 5.</summary>
    public short Field5 { get; set; }
    /// <summary>i32 — champ 6.</summary>
    public int Field6 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1); s.WriteShort(Field2); s.WriteShort(Field3);
        s.WriteInt(Field4); s.WriteShort(Field5); s.WriteInt(Field6);
    }
    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort(); Field2 = s.ReadShort(); Field3 = s.ReadShort();
        Field4 = s.ReadInt(); Field5 = s.ReadShort(); Field6 = s.ReadInt();
    }
}

/// <summary>S→C NCID 470 — ClientPlayElfPetAction : le serveur relaie
/// l'ordre au pet live — <c>[i32 handle pet][s16 cmd]</c> (body
/// construit octet par octet dans SendPetCmd serveur ; handle =
/// sight-handle du mob elf).</summary>
public sealed class ZonePlayElfPetAction : NetMessage
{
    public override int Opcode => 470;
    public override string Name => "Zone.ClientPlayElfPetAction";
    /// <summary>i32 — sight-handle du pet.</summary>
    public int PetHandle { get; set; }
    /// <summary>s16 — commande reluée (1 attaque, 6-8 emotes, 101+ sorts).</summary>
    public short Cmd { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(PetHandle); s.WriteShort(Cmd);
    }
    public override void Deserialize(BinReader s)
    {
        PetHandle = s.ReadInt(); Cmd = s.ReadShort();
    }
}

/// <summary>S→C NCID 513 — ClientPetShortCut : invocation/renvoi du
/// familier — <c>[u8 flag][s16 loc]</c> (BuildPetShortCut natif
/// @0x807eec0 : flag 1 = invoqué, 0 = renvoyé ; loc = slot).</summary>
public sealed class ZonePetShortCut : NetMessage
{
    public override int Opcode => 513;
    public override string Name => "Zone.ClientPetShortCut";
    /// <summary>u8 — 1 invoqué, 0 renvoyé.</summary>
    public byte Flag { get; set; }
    /// <summary>s16 — loc de l'elf.</summary>
    public short Loc { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteByte(Flag); s.WriteShort(Loc);
    }
    public override void Deserialize(BinReader s)
    {
        Flag = s.ReadByte(); Loc = s.ReadShort();
    }
}

/// <summary>S→C NCID 567 — ClientUpdateMixElf : template de l'elf
/// actuellement en slot d'intégration d'un joueur —
/// <c>[i32 charId][i32 mixTemplateId]</c> (SendUpdateMixElfAsync :
/// NotifyClient + BroadCastToCanSee).</summary>
public sealed class ZoneUpdateMixElf : NetMessage
{
    public override int Opcode => 567;
    public override string Name => "Zone.ClientUpdateMixElf";
    /// <summary>i32 — charId du joueur.</summary>
    public int CharId { get; set; }
    /// <summary>i32 — templateId de l'elf en mix (0 = aucun).</summary>
    public int MixTemplateId { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(CharId); s.WriteInt(MixTemplateId);
    }
    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt(); MixTemplateId = s.ReadInt();
    }
}

/// <summary>S→C NCID 568 — ClientUpdateElfMixElf : résultat d'une
/// fusion (BuildUpdateElfMixElf, ctor @0x807a3f0) :
/// <c>[s16 loc][i32 elfId][s16 mixStar][u16 n][n×s16 grow]
/// [u16 n][n×s16 battle][u16 n][n×s16 integrate]</c>.</summary>
public sealed class ZoneUpdateElfMixElf : NetMessage
{
    public override int Opcode => 568;
    public override string Name => "Zone.ClientUpdateElfMixElf";
    /// <summary>s16 — loc de l'elf fusionné.</summary>
    public short Loc { get; set; }
    /// <summary>i32 — elf_id.</summary>
    public int ElfId { get; set; }
    /// <summary>s16 — nouvelle étoile de mix.</summary>
    public short MixStar { get; set; }
    /// <summary>Nouvelles valeurs grow.</summary>
    public List<short> Grow { get; } = new();
    /// <summary>Nouvelle liste de sorts de combat.</summary>
    public List<int> Battle { get; } = new();
    /// <summary>Nouvelle liste de sorts intégrés.</summary>
    public List<int> Integrate { get; } = new();

    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        Loc = s.ReadShort();                            // s16 — loc
        ElfId = s.ReadInt();                            // i32 — elf_id
        MixStar = s.ReadShort();                        // s16 — mix_star
        var n = s.ReadUShort();                         // u16 — n grow
        for (var i = 0; i < n; i++) Grow.Add(s.ReadShort());
        n = s.ReadUShort();                             // u16 — n battle
        for (var i = 0; i < n; i++) Battle.Add(s.ReadShort());
        n = s.ReadUShort();                             // u16 — n integ
        for (var i = 0; i < n; i++) Integrate.Add(s.ReadShort());
    }
}

/// <summary>S→C NCID 574 — ClientPetUpgradeCasting : début du cast
/// de fusion — <c>[s16 durée en ticks]</c> (40 natif = 4 s).</summary>
public sealed class ZonePetUpgradeCasting : NetMessage
{
    public override int Opcode => 574;
    public override string Name => "Zone.ClientPetUpgradeCasting";
    /// <summary>s16 — durée du cast en ticks serveur (100 ms).</summary>
    public short Ticks { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(Ticks);
    public override void Deserialize(BinReader s) => Ticks = s.ReadShort();
}

/// <summary>S→C NCID 575 — ClientPetUpgradeCancel : fusion annulée
/// (corps vide — classe générée sans champ).</summary>
public sealed class ZonePetUpgradeCancel : NetMessage
{
    public override int Opcode => 575;
    public override string Name => "Zone.ClientPetUpgradeCancel";
    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s) { }
}

/// <summary>S→C NCID 588 — ClientUpdateElfSpell : sorts d'un elf
/// après apprentissage d'un livre (BuildUpdateElfSpell, ctor dans
/// CKFElf::LearnSpell @0x8455b30) :
/// <c>[s16 loc][u16 n][n×s16 battle][u16 n][n×s16 integrate]</c>.</summary>
public sealed class ZoneUpdateElfSpell : NetMessage
{
    public override int Opcode => 588;
    public override string Name => "Zone.ClientUpdateElfSpell";
    /// <summary>s16 — loc de l'elf.</summary>
    public short Loc { get; set; }
    /// <summary>Sorts de combat complets.</summary>
    public List<int> Battle { get; } = new();
    /// <summary>Sorts intégrés complets.</summary>
    public List<int> Integrate { get; } = new();

    public override void Serialize(BinWriter s) { }
    public override void Deserialize(BinReader s)
    {
        Loc = s.ReadShort();                            // s16 — loc
        var n = s.ReadUShort();                         // u16 — n battle
        for (var i = 0; i < n; i++) Battle.Add(s.ReadShort());
        n = s.ReadUShort();                             // u16 — n integ
        for (var i = 0; i < n; i++) Integrate.Add(s.ReadShort());
    }
}
