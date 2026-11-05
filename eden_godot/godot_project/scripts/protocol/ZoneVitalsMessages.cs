using EdenClient.Net;

namespace EdenClient.Protocol;

// Vitaux / stats du joueur (S→C) — compléments des déjà-couverts
// 280 (ZoneUpdateAttr), 296 (ZoneAttrRefresh), 600 (ZoneUpdateAttrNewExp),
// 265/310 (blocs d'entrée).
//
// Sources de preuve :
//   - $EDEN/server/src/Eden.Zone/ProfileStatEmitter.cs — ordre des
//     champs prouvé par désasm NotifyRefreshAttr @0x8415210 +
//     Serialize @0x81e8940/0x81e8c20 + golden zsniff ;
//   - golden server/tests/Eden.Tests/Data/golden/ncid_{305,328,492,493}.json ;
//   - $EDEN/docs/generated/constants.md pour la table attrId 280/328.

/// <summary>S→C NCID 492 — BaseAttrRefresh : stats de BASE du perso
/// (sans équipement — S_Level[level] × S_Class%). GOLDEN.
/// Corps 32B : `[s16×5 Str,Int,Agi,Wis,Lck][i32 hp][i32 mp]
/// [f32 walk][f32 run][s16×3 timings]`.</summary>
public sealed class ZoneBaseAttrRefresh : NetMessage
{
    public override int Opcode => 492;
    public override string Name => "Zone.ClientBaseAttrRefresh";
    /// <summary>s16×5 — Str, Int, Agi, Wis, Lck (ordre natif).</summary>
    public short[] Stats { get; } = new short[5];
    /// <summary>i32 — HP de base.</summary>
    public int Hp { get; set; }
    /// <summary>i32 — MP de base.</summary>
    public int Mp { get; set; }
    /// <summary>f32 — vitesse marche.</summary>
    public float Walk { get; set; }
    /// <summary>f32 — vitesse course.</summary>
    public float Run { get; set; }
    /// <summary>s16×3 — timings natifs.</summary>
    public short[] Timings { get; } = new short[3];

    public override void Serialize(BinWriter s)
    {
        foreach (var v in Stats) s.WriteShort(v);
        s.WriteInt(Hp);
        s.WriteInt(Mp);
        s.WriteFloat(Walk);
        s.WriteFloat(Run);
        foreach (var v in Timings) s.WriteShort(v);
    }
    public override void Deserialize(BinReader s)
    {
        for (var i = 0; i < 5; i++) Stats[i] = s.ReadShort();
        Hp = s.ReadInt();
        Mp = s.ReadInt();
        Walk = s.ReadFloat();
        Run = s.ReadFloat();
        for (var i = 0; i < 3; i++) Timings[i] = s.ReadShort();
    }
}

/// <summary>S→C NCID 493 — AttrLevelRefresh : stats « flottantes »
/// (bonus de niveau en f32). GOLDEN. Corps 56B = 14 × f32
/// (rôles unitaires non décodés — affichage brut).</summary>
public sealed class ZoneAttrLevelRefresh : NetMessage
{
    public override int Opcode => 493;
    public override string Name => "Zone.ClientAttrLevelRefresh";
    /// <summary>14 × f32 — attributs flottants de niveau.</summary>
    public float[] Fields { get; } = new float[14];
    public override void Serialize(BinWriter s)
    {
        foreach (var f in Fields) s.WriteFloat(f);
    }
    public override void Deserialize(BinReader s)
    {
        for (var i = 0; i < 14; i++) Fields[i] = s.ReadFloat();
    }
}

/// <summary>S→C NCID 328 — ModifyAttr : même forme que 280
/// `[u32 handle][s16 attrId][i32 p1][i64 p2]` (corps 18B capturé) mais
/// sémantique « modification » (debuff/bonus de combat — Spells.
/// Resolution.cs serveur). GOLDEN.</summary>
public sealed class ZoneModifyAttr : NetMessage
{
    public override int Opcode => 328;
    public override string Name => "Zone.ClientModifyAttr";
    /// <summary>u32 — handle de l'entité.</summary>
    public int Handle { get; set; }
    /// <summary>s16 — id d'attribut (table constants.md).</summary>
    public short AttrId { get; set; }
    /// <summary>i32 — valeur principale.</summary>
    public int Value { get; set; }
    /// <summary>i64 — valeur large.</summary>
    public long Value64 { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteShort(AttrId);
        s.WriteInt(Value);
        s.WriteLong(Value64);
    }
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        AttrId = s.ReadShort();
        Value = s.ReadInt();
        Value64 = s.ReadLong();
    }
}

/// <summary>S→C NCID 305 — Die : une entité meurt.
/// GOLDEN — corps 9B `[u32 handle][u32 killer?][u8]`
/// (golden : mob 0xfffff4fa + charId self ; native idem).</summary>
public sealed class ZoneDie : NetMessage
{
    public override int Opcode => 305;
    public override string Name => "Zone.ClientDie";
    /// <summary>u32 — handle de l'entité morte.</summary>
    public uint Handle { get; set; }
    /// <summary>u32 — second handle (tueur/cible — rôle non prouvé).</summary>
    public uint Other { get; set; }
    /// <summary>u8 — flag.</summary>
    public byte Flag { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Handle);
        s.WriteInt((int)Other);
        s.WriteByte(Flag);
    }
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadUInt();
        Other = s.ReadUInt();
        Flag = s.ReadByte();
    }
}

/// <summary>S→C NCID 306 — Revive : respawn.
/// DESASM — `[u32 charId][i32 4B bruts][u8 type]`.</summary>
public sealed class ZoneRevive : NetMessage
{
    public override int Opcode => 306;
    public override string Name => "Zone.ClientRevive";
    /// <summary>u32 — charId/handle de l'entité réanimée.</summary>
    public uint Handle { get; set; }
    /// <summary>i32 — 4 octets natifs (+0x8).</summary>
    public int Field2 { get; set; }
    /// <summary>u8 — type de revive.</summary>
    public byte ReviveType { get; set; }
    public override void Serialize(BinWriter s)
    {
        s.WriteInt((int)Handle);
        s.WriteInt(Field2);
        s.WriteByte(ReviveType);
    }
    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadUInt();
        Field2 = s.ReadInt();
        ReviveType = s.ReadByte();
    }
}

// ---------- état agrégé pour le HUD ----------

/// <summary>Snapshot des stats totales (296) — nommage prouvé par
/// ProfileStatEmitter.cs (ordre NotifyRefreshAttr natif).</summary>
public sealed record PlayerStats(
    int Str, int Int, int Agi, int Wis, int Lck,
    int[] Resists,          // 9 — ORDRE D'AFFICHAGE natif (slash, strike,
                            // pierce, fire, lightning, ice, nature, holy,
                            // dark) — réordonné du wire par ResistWire
    int MaxMp,
    int PhysicalAttack,     // MeleeDamage (P-ATK)
    int MagicAttack,        // MagicDamage (M-ATK)
    float AttackSpeedRate, float MagicSpeedRate,
    int PhysicalDefence,
    short[] Rates,          // 8 : Parry,Block,Hit,Dodge,Crit,CritDmg,MCrit,MCritDmg
    float[] Speeds);        // 6 : Movement,BuffAtkSpd,BuffCast,DefReduce,MoveRate,CriDmgReduce

/// <summary>
/// État vitaux/progression du personnage self — alimenté par les
/// handlers de <c>GameScene</c> sur le thread principal ; consommé
/// par le HUD (TopLeftVitals / ProfileWindow) via
/// <see cref="Hud.HudRoot"/> ou par lecture directe des propriétés.
/// </summary>
public static class PlayerVitals
{
    /// <summary>HP/MP courant et max changés.</summary>
    public static event Action? VitalsChanged;
    /// <summary>XP changée (600).</summary>
    public static event Action? ExpChanged;
    /// <summary>XP de classe changée (280 attr 0x06).</summary>
    public static event Action? ClassExpChanged;
    /// <summary>Or changé (280 attr 0x02).</summary>
    public static event Action? GoldChanged;
    /// <summary>Stats totales changées (296/492/493).</summary>
    public static event Action? StatsChanged;
    /// <summary>Self mort (305) — param = handle tueur.</summary>
    public static event Action<uint>? Died;
    /// <summary>Self réanimé (306).</summary>
    public static event Action? Revived;

    /// <summary>Classe ACTIVE (280 attr 0x2c du burst de changement de
    /// classe) — -1 tant qu'aucun changement n'a été reçu (on se
    /// rabat alors sur la classe du personnage choisi).</summary>
    public static int ActiveClass { get; private set; } = -1;
    public static event Action? ClassChanged;

    /// <summary>Points de talent (KP) de la classe active — 280 attr
    /// 0x31 (GainAAPoint natif) ; -1 avant réception (repli : champ
    /// aa du 489).</summary>
    public static int AaPoint { get; private set; } = -1;
    /// <summary>CP dépensables — 280 attr 0x3ef (GainNp natif) ; -1
    /// avant réception.</summary>
    public static int SkillPoints { get; private set; } = -1;
    public static event Action? PointsChanged;

    public static long Hp { get; set; }
    public static long MaxHp { get; set; }
    public static long Mp { get; set; }
    public static long MaxMp { get; set; }
    /// <summary>XP courante du perso (600 UpdateAttrNewExp).</summary>
    public static long Exp { get; private set; }

    /// <summary>Enregistre le 600 (notifie <see cref="ExpChanged"/>
    /// si la valeur a bougé).</summary>
    public static void ApplyExp(long exp)
    {
        if (Exp == exp) return;
        Exp = exp;
        ExpChanged?.Invoke();
    }
    /// <summary>XP/points de la classe active (280 attr 0x06,
    /// p1=class_point — désasm GainClassPoint @0841c24b).</summary>
    public static long ClassExp { get; private set; }
    /// <summary>Amorce l'XP de classe depuis le 310 (+74 i32) — le
    /// serveur ne réémet l'attr 0x06 qu'au prochain kill : sans ce
    /// seed la barre d'XP de classe affiche 0% jusqu'au premier mob
    /// tué alors que la progression est persistée.</summary>
    public static void SeedClassExp(long exp)
    {
        if (ClassExp == exp) return;
        ClassExp = exp;
        ClassExpChanged?.Invoke();
    }
    /// <summary>Or (280 attr 0x02).</summary>
    public static long Gold { get; set; }
    /// <summary>Stats totales du dernier 296 (null avant réception).</summary>
    public static PlayerStats? TotalStats { get; private set; }
    /// <summary>Stats de base du dernier 492 (null avant réception).</summary>
    public static ZoneBaseAttrRefresh? BaseStats { get; private set; }
    /// <summary>Paliers du dernier 493 (14 f32, ordre vtable CCharacter :
    /// HitLv, CriLv, MCriLv, DodgeLv, ParryLv, BlockLv, Base×6,
    /// AtkSpdLv, CastSpdLv) — colonne « valeur » de la fiche ; null
    /// avant réception.</summary>
    public static float[]? LevelStats { get; private set; }
    /// <summary>Mort/vivant du self (305/306).</summary>
    public static bool IsDead { get; private set; }

    /// <summary>Point unique de mutation des vitaux (notifie).</summary>
    public static void SetVitals(long hp, long maxHp, long mp, long maxMp)
    {
        if (Hp == hp && MaxHp == maxHp && Mp == mp && MaxMp == maxMp)
            return;
        Hp = hp; MaxHp = maxHp; Mp = mp; MaxMp = maxMp;
        VitalsChanged?.Invoke();
    }

    /// <summary>Applique un 280 self (attrId 0/1/0x11/0x12/0x02/0x06/
    /// 0x2c) — mapping prouvé ZoneEntryFlow.Combat.cs + constants.md.</summary>
    public static void ApplyUpdateAttr(ZoneUpdateAttr m)
    {
        EdenClient.Combat.PlayerStateLocks.Apply(m);   // 1002-1006 verrous
        switch (m.AttrId)
        {
            case 0x00:                          // HP courant
                SetVitals(m.Current, MaxHp, Mp, MaxMp); break;
            case 0x01:                          // MP courant
                SetVitals(Hp, MaxHp, m.Current, MaxMp); break;
            case 0x11:                          // MaxHP (p1 & p2 = max)
                SetVitals(Hp, m.Max, Mp, MaxMp); break;
            case 0x12:                          // MaxMP (p1 = max)
                SetVitals(Hp, MaxHp, Mp, m.Current); break;
            case 0x02:                          // or
                if (Gold != m.Current)
                { Gold = m.Current; GoldChanged?.Invoke(); }
                break;
            case 0x2c:                          // classe active
                if (m.Current > 0 && ActiveClass != (int)m.Current)
                { ActiveClass = (int)m.Current; ClassChanged?.Invoke(); }
                break;
            case 0x31:                          // KP (talents) classe active
                AaPoint = (int)m.Current; PointsChanged?.Invoke(); break;
            case 0x3ef:                         // CP dépensables (GainNp)
                if (m.Current > 0)
                { SkillPoints = (int)m.Current; PointsChanged?.Invoke(); }
                break;
            case 0x06:                          // XP de classe (p1)
                if (ClassExp != m.Current)
                { ClassExp = m.Current; ClassExpChanged?.Invoke(); }
                break;
        }
    }

    /// <summary>Position wire 296 (Fields1to14[5+k]) de la résistance
    /// affichée en position i du profil. Le serveur émet les ids natifs
    /// 8,9,6,4,5,7,1,2,3 (ordre d'appels AttrResist de
    /// NotifyRefreshAttr — ProfileStatEmitter.resistOrder) ; les ids
    /// suivent l'ordre c_item/AttrResist 1..9 = slash, strike, pierce,
    /// fire, ice, lightning, nature, holy, dark (CCharFormula::
    /// AttrResist : equip[type−1] = colonne c_item 62+type — « Ice Gem »
    /// = col 67, « Thunder Gem » = col 68). Le wire porte donc
    /// {holy, dark, lightning, fire, ice, nature, slash, strike,
    /// pierce} ; l'affichage natif (AntiChop…AntiDark) veut
    /// {slash, strike, pierce, fire, lightning, ice, nature, holy,
    /// dark} — les deux ordres divergent sur ice/lightning (ids 5/6)
    /// ET sur les positions physiques.</summary>
    public static readonly int[] ResistWire = { 6, 7, 8, 3, 2, 4, 5, 0, 1 };

    /// <summary>Enregistre le 296 — nommage du ProfileStatEmitter :
    /// Fields1to14 = 5 stats + 9 résists ; Fields15to18 =
    /// [0][MaxMP][P-ATK][M-ATK] ; Field21 = DEF ;
    /// Fields22to29 = 8 taux ; Fields30to35 = 6 vitesses.</summary>
    public static void ApplyAttrRefresh(ZoneAttrRefresh m)
    {
        var f = m.Fields1to14;
        TotalStats = new PlayerStats(
            f[0], f[1], f[2], f[3], f[4],
            ResistWire.Select(k => (int)f[5 + k]).ToArray(),
            m.Fields15to18[1], m.Fields15to18[2], m.Fields15to18[3],
            m.Fields19to20[0], m.Fields19to20[1],
            m.Field21,
            m.Fields22to29, m.Fields30to35);
        StatsChanged?.Invoke();
    }

    /// <summary>Enregistre le 492.</summary>
    public static void ApplyBaseAttrRefresh(ZoneBaseAttrRefresh m)
    {
        BaseStats = m;
        StatsChanged?.Invoke();
    }

    /// <summary>Enregistre le 493 (stats flottantes — relayé via
    /// <see cref="StatsChanged"/> pour les fenêtres de profil).</summary>
    public static void ApplyAttrLevelRefresh(ZoneAttrLevelRefresh m)
    {
        LevelStats = (float[])m.Fields.Clone();
        StatsChanged?.Invoke();
    }

    /// <summary>305 — si le handle est le self, marque mort.</summary>
    public static void ApplyDie(ZoneDie m, uint selfCharId)
    {
        if (m.Handle == selfCharId)
        {
            IsDead = true;
            Died?.Invoke(m.Other);
        }
    }

    /// <summary>306 — respawn du self.</summary>
    public static void ApplyRevive(ZoneRevive m, uint selfCharId)
    {
        if (m.Handle == selfCharId)
        {
            IsDead = false;
            Revived?.Invoke();
        }
    }
}
