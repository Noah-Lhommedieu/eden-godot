using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages classe/ métier du ZoneServer (:5201) — lot S6.
// Preuves : $EDEN/server/.../Generated/ZoneMessages.g.cs +
// captures zsniff natives (17:22) commentées par classe.

/// <summary>S→C NCID 489 — ClientPlayerClassList. GOLDEN
/// (capture native 162B + Serialize natif @0x813f2e0) :
/// <c>[u8 0xff][u8 0][u16 count]</c> puis par classe
/// <c>[u8 classId][u8 level][u8 style][i32 classExp][i32 classPoint]
/// [u8 aaPoint][u16 lic×7]</c>. Le serveur liste TOUTES les classes
/// accessibles au changement ({1..5,19} + celles possédées).</summary>
public sealed class ZonePlayerClassList : NetMessage
{
    public override int Opcode => 489;
    public override string Name => "Zone.PlayerClassList";

    public byte HeaderFlag1 { get; set; }
    public byte HeaderFlag2 { get; set; }
    public List<Entry> Entries { get; } = new();

    /// <summary>Une classe proposable (mapping positionnel du codec
    /// serveur — les noms générés du .g.cs étaient trompeurs).</summary>
    public sealed class Entry
    {
        public int ClassId { get; set; }       // u8 — id de classe
        public int ClassLevel { get; set; }    // u8 — level (col `level`)
        public int Style { get; set; }         // u8 — style/carte
        public int ClassExp { get; set; }      // i32
        public int ClassPoints { get; set; }   // i32 — points de classe
        public int AaPoint { get; set; }       // u8 — pool talents
        public ushort[] Licenses { get; } = new ushort[7];
    }

    public override void Serialize(BinWriter s)
    {
        s.WriteByte(HeaderFlag1);
        s.WriteByte(HeaderFlag2);
        s.WriteShort((short)Entries.Count);
        foreach (var e in Entries)
        {
            s.WriteByte((byte)e.ClassId);
            s.WriteByte((byte)e.ClassLevel);
            s.WriteByte((byte)e.Style);
            s.WriteInt(e.ClassExp);
            s.WriteInt(e.ClassPoints);
            s.WriteByte((byte)e.AaPoint);
            foreach (var l in e.Licenses) s.WriteShort((short)l);
        }
    }

    public override void Deserialize(BinReader s)
    {
        HeaderFlag1 = (byte)s.ReadByte();
        HeaderFlag2 = (byte)s.ReadByte();
        var count = s.ReadUShort();
        Entries.Clear();
        for (var i = 0; i < count; i++)
        {
            var e = new Entry
            {
                ClassId = s.ReadByte(),
                ClassLevel = s.ReadByte(),
                Style = s.ReadByte(),
                ClassExp = s.ReadInt(),
                ClassPoints = s.ReadInt(),
                AaPoint = s.ReadByte(),
            };
            for (var j = 0; j < 7; j++)
                e.Licenses[j] = s.ReadUShort();
            Entries.Add(e);
        }
    }
}

/// <summary>C→S NCID 168 — ChangeClass. Wire natif <c>[u8 classId]</c>
/// (désasm Deserialize — un seul octet ; le serveur répond par le
/// burst de changement de classe : vitals/296/488/489/attr 0x2c/283/
/// 509/286/316 puis 510).</summary>
public sealed class ZoneChangeClass : NetMessage
{
    public override int Opcode => 168;
    public override string Name => "Zone.ChangeClass";
    public byte ClassId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteByte(ClassId);
    public override void Deserialize(BinReader s) => ClassId = (byte)s.ReadByte();
}

/// <summary>S→C NCID 510 — ClientChangeClassShortcut. GOLDEN :
/// <c>[u8 barCount]</c> puis par barre <c>[u16 mask][i32 par bit
/// levé]</c> puis <c>[u16 extrasCount][i32 par extra]</c>. Réémis
/// après un changement de classe avec les barres de la nouvelle
/// classe — on le parse pour l'affichage/log, la hotbar locale est
/// re-remplie par l'éventuel 352 courant.</summary>
public sealed class ZoneChangeClassShortcut : NetMessage
{
    public override int Opcode => 510;
    public override string Name => "Zone.ChangeClassShortcut";

    /// <summary>Barres (pages) — 10 slots chacune, -1/0 = vide.</summary>
    public List<int[]> Bars { get; } = new();
    /// <summary>Vecteur int global (char+0x678 natif).</summary>
    public List<int> Extras { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteByte((byte)Bars.Count);
        foreach (var bar in Bars)
        {
            var mask = 0;
            var n = Math.Min(bar.Length, 10);
            for (var i = 0; i < n; i++)
                if (bar[i] != -1 && bar[i] != 0) mask |= 1 << i;
            s.WriteShort((short)mask);
            for (var i = 0; i < n; i++)
                if (bar[i] != -1 && bar[i] != 0) s.WriteInt(bar[i]);
        }
        s.WriteShort((short)Extras.Count);
        foreach (var v in Extras) s.WriteInt(v);
    }

    public override void Deserialize(BinReader s)
    {
        Bars.Clear();
        Extras.Clear();
        var barCount = s.ReadByte();
        for (var b = 0; b < barCount; b++)
        {
            var mask = s.ReadUShort();
            var bar = Enumerable.Repeat(-1, 10).ToArray();
            for (var i = 0; i < 10; i++)
                if ((mask & (1 << i)) != 0) bar[i] = s.ReadInt();
            Bars.Add(bar);
        }
        var extras = s.ReadUShort();
        for (var i = 0; i < extras; i++) Extras.Add(s.ReadInt());
    }
}

/// <summary>C→S NCID 12 — ZoneServerUseSkillPoint `[s16 spellmaster]` :
/// monte un sort d'un niveau (or ou CP selon C_Spell col42) — bouton
/// « +1 » de la fenêtre Classe. Réponse 286 ClientGetSpell.</summary>
public sealed class ZoneUseSkillPoint : NetMessage
{
    public override int Opcode => 12;
    public override string Name => "Zone.ServerUseSkillPoint";
    public short SpellId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(SpellId);
    public override void Deserialize(BinReader s) => SpellId = s.ReadShort();
}

/// <summary>C→S NCID 623 — « Level Max » Reforged `[s16 spellmaster]` :
/// autant de niveaux que l'or/les CP le permettent (bouton « Max »).</summary>
public sealed class ZoneUseSkillPointMax : NetMessage
{
    public override int Opcode => 623;
    public override string Name => "Zone.ServerUseSkillPointMax";
    public short SpellId { get; set; }
    public override void Serialize(BinWriter s) => s.WriteShort(SpellId);
    public override void Deserialize(BinReader s) => SpellId = s.ReadShort();
}

