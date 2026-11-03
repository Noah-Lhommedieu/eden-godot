using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages « ordres de travail » (métiers 12-17) du ZoneServer —
// lot S8. Preuves : $EDEN/server/src/Eden.Zone/Codecs/WorkPackets.cs
// (sérialiseurs désassemblés) + Generated/ZoneMessages.g.cs.

/// <summary>S→C NCID 496 — ClientEnterWorks. DESASM :
/// <c>[i32 npcUid][s16 workType][s16 bldId][s16 ?][u16 0]</c>.
/// workType = type de service NPC : 12 mining, 13 planting,
/// 14 summon, 15 alchemy, 16 casting, 17 enchanting ; bldId/? =
/// bâtiment familial (0 hors ville de famille).</summary>
public sealed class ZoneEnterWorks : NetMessage
{
    public override int Opcode => 496;
    public override string Name => "Zone.EnterWorks";
    public int NpcUid { get; set; }
    public short WorkType { get; set; }
    public short BuildingId { get; set; }
    public short Field4 { get; set; }
    public ushort Field5 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(NpcUid);
        s.WriteShort(WorkType);
        s.WriteShort(BuildingId);
        s.WriteShort(Field4);
        s.WriteShort((short)Field5);
    }
    public override void Deserialize(BinReader s)
    {
        NpcUid = s.ReadInt();
        WorkType = s.ReadShort();
        BuildingId = s.ReadShort();
        Field4 = s.ReadShort();
        Field5 = s.ReadUShort();
    }
}

/// <summary>S→C NCID 497 — ClientPlayerWorkUpdate (agrégé). PASSTHRU
/// côté serveur, corps natif : <c>[u16 count]</c> puis par ordre
/// <c>[s16 workNum][s16 workType][i32 workId][i32 eventId(enchant
/// 200+num)][str fmName]</c>. Émis à l'armement et au 172.</summary>
public sealed class ZonePlayerWorkUpdate : NetMessage
{
    public override int Opcode => 497;
    public override string Name => "Zone.PlayerWorkUpdate";

    public sealed class WorkOrder
    {
        public short WorkNum { get; set; }    // slot 0-10
        public short WorkType { get; set; }   // 12-17
        public int WorkId { get; set; }       // id de recette
        public int EventId { get; set; }      // enchant 200+num
        public string FmName { get; set; } = "";
    }

    public List<WorkOrder> Orders { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Orders.Count);
        foreach (var o in Orders)
        {
            s.WriteShort(o.WorkNum);
            s.WriteShort(o.WorkType);
            s.WriteInt(o.WorkId);
            s.WriteInt(o.EventId);
            s.WriteString(o.FmName);
        }
    }
    public override void Deserialize(BinReader s)
    {
        Orders.Clear();
        var count = s.ReadUShort();
        for (var i = 0; i < count; i++)
            Orders.Add(new WorkOrder
            {
                WorkNum = s.ReadShort(),
                WorkType = s.ReadShort(),
                WorkId = s.ReadInt(),
                EventId = s.ReadInt(),
                FmName = s.ReadString(),
            });
    }
}

/// <summary>Un produit d'ordre terminé — SWorkItem aplati en 13 i32
/// ([itemId, amount, socketAmount, combo, extraCombo×9]).</summary>
public readonly record struct WorkProduct(int ItemId, int Amount,
    int Sockets, int Combo, int[] ExtraCombos);

/// <summary>S→C NCID 498 — ClientPlayerWorkResult. PASSTHRU :
/// <c>[i32 workId (&lt;0 = échec)][s16 workType][s16 workNum]
/// [str fmName][u16 nVec]</c> puis par produit <c>[u16 13][i32×13]</c>.
/// </summary>
public sealed class ZonePlayerWorkResult : NetMessage
{
    public override int Opcode => 498;
    public override string Name => "Zone.PlayerWorkResult";
    public int WorkId { get; set; }         // négatif = échec
    public short WorkType { get; set; }
    public short WorkNum { get; set; }
    public string FmName { get; set; } = "";
    public List<WorkProduct> Products { get; } = new();
    public bool Failed => WorkId < 0;

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(WorkId);
        s.WriteShort(WorkType);
        s.WriteShort(WorkNum);
        s.WriteString(FmName);
        s.WriteShort((short)Products.Count);
        foreach (var p in Products)
        {
            s.WriteShort(13);
            s.WriteInt(p.ItemId);
            s.WriteInt(p.Amount);
            s.WriteInt(p.Sockets);
            s.WriteInt(p.Combo);
            for (var i = 0; i < 9; i++)
                s.WriteInt(p.ExtraCombos[i]);
        }
    }
    public override void Deserialize(BinReader s)
    {
        WorkId = s.ReadInt();
        WorkType = s.ReadShort();
        WorkNum = s.ReadShort();
        FmName = s.ReadString();
        Products.Clear();
        var vecCount = s.ReadUShort();
        for (var v = 0; v < vecCount; v++)
        {
            var len = s.ReadUShort();
            var vals = new int[len];
            for (var i = 0; i < len; i++) vals[i] = s.ReadInt();
            if (len >= 13)
                Products.Add(new WorkProduct(vals[0], vals[1], vals[2],
                    vals[3], vals[4..13]));
        }
    }
}

/// <summary>S→C NCID 500 — ClientPlayerWorkResultEnd <c>[s16
/// workNum]</c> — le slot est vidé après récupération (171).</summary>
public sealed class ZonePlayerWorkResultEnd : NetMessage
{
    public override int Opcode => 500;
    public override string Name => "Zone.PlayerWorkResultEnd";
    public short WorkNum { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(WorkNum);
    public override void Deserialize(BinReader s) => WorkNum = s.ReadShort();
}

/// <summary>S→C NCID 433 — ClientStrengthenCasting <c>[s16 type]
/// [s16 durée×100ms][s16 0]</c> — barre de cast à l'armement d'un
/// ordre (combine : toujours ; collect instantanée / type 14).</summary>
public sealed class ZoneStrengthenCasting : NetMessage
{
    public override int Opcode => 433;
    public override string Name => "Zone.StrengthenCasting";
    public short WorkType { get; set; }
    public short DurationUnits { get; set; }   // ×100 ms
    public short Field3 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteShort(WorkType);
        s.WriteShort(DurationUnits);
        s.WriteShort(Field3);
    }
    public override void Deserialize(BinReader s)
    {
        WorkType = s.ReadShort();
        DurationUnits = s.ReadShort();
        Field3 = s.ReadShort();
    }
}

/// <summary>C→S NCID 170 — PlayerWork (démarrage d'ordre). DESASM :
/// <c>[u32 workId][s16 catBag][s16 catSlot][s16 boostBag]
/// [s16 boostSlot]</c> ; -1,-1 = pas de booster. Le workType vient
/// de l'opération works ouverte (496), pas du paquet.</summary>
public sealed class ZonePlayerWork : NetMessage
{
    public override int Opcode => 170;
    public override string Name => "Zone.PlayerWork";
    public uint WorkId { get; set; }
    public short CatalystBag { get; set; }
    public short CatalystSlot { get; set; }
    public short BoostBag { get; set; } = -1;
    public short BoostSlot { get; set; } = -1;
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)WorkId);
        s.WriteShort(CatalystBag);
        s.WriteShort(CatalystSlot);
        s.WriteShort(BoostBag);
        s.WriteShort(BoostSlot);
    }
    public override void Deserialize(BinReader s)
    {
        WorkId = (uint)s.ReadInt();
        CatalystBag = s.ReadShort();
        CatalystSlot = s.ReadShort();
        BoostBag = s.ReadShort();
        BoostSlot = s.ReadShort();
    }
}

/// <summary>C→S NCID 171 — PlayerWorkResultOK (récupération) :
/// <c>[s16 workNum]</c>.</summary>
public sealed class ZonePlayerWorkResultOK : NetMessage
{
    public override int Opcode => 171;
    public override string Name => "Zone.PlayerWorkResultOK";
    public short WorkNum { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(WorkNum);
    public override void Deserialize(BinReader s) => WorkNum = s.ReadShort();
}
