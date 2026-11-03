using EdenClient.Net;
using System.Linq;

namespace EdenClient.Protocol;

// Messages du burst d'entrée en zone + mises à jour éparses — lot
// Vague 4 : NCIDs reçus « non gérés » en live (le client demande les
// blocs EnterNode* mais ignorait leurs réponses).
//
// Sources de preuve : Serialize des CNC_CZ_* dans
// server/src/Eden.Core/Networking/Messages/Generated/ZoneMessages.g.cs,
// FastDressPackets.cs (593), ZoneEntryFlow.Limitless.cs (2032/2033),
// EntryHandlers.cs (351 = bitmask natif @0x8202200).
// Chaque trame a un état dans <see cref="PlayerProfileState"/> —
// mutations thread-safe sur le thread réseau, notification UI en file
// principale (même invariant que PlayerBag/VendingState).

/// <summary>Store global des données de profil/progression reçues à
/// l'entrée — indépendant des fenêtres (posées plus tard). Les HUD
/// lisent l'état au montage ; <see cref="Changed"/> est ré-émis en
/// file principale par HudExtensions.</summary>
public static class PlayerProfileState
{
    private static readonly object _lock = new();

    /// <summary>Section modifiée (barre, starway, cooldowns…).</summary>
    public static event Action<string>? Changed;

    /// <summary>Barres de raccourcis : 10 slots chacune (-1 = vide,
    /// refId de l'action).</summary>
    public static List<int[]> ShortcutBars { get; private set; } = new();
    /// <summary>Voie des étoiles (609) — champs + texte.</summary>
    public static short[] StarWay { get; private set; } = new short[6];
    public static string StarWayText { get; private set; } = "";
    /// <summary>Carte joueur (490) — 12 compteurs + titre affiché.</summary>
    public static short[] PlayerCard { get; private set; } = new short[12];
    public static string PlayerCardText { get; private set; } = "";
    /// <summary>Salon/pièce du joueur (523) — champs numériques.</summary>
    public static int[] PlayerRoomInts { get; private set; }
        = Array.Empty<int>();
    public static string PlayerRoomText { get; private set; } = "";
    /// <summary>Dressing rapide (593) — records [itemId, containerType,
    /// fromLoc, fromIndex, page, tarLoc].</summary>
    public static List<int[]> FastDress { get; private set; } = new();
    /// <summary>Collections (472) — bookId → cardIds.</summary>
    public static Dictionary<short, List<short>> Collections { get; }
        = new();
    /// <summary>Copie thread-safe des item_id archivés (472).</summary>
    public static HashSet<int> ArchiveOwned()
    {
        lock (_lock)
        {
            var set = new HashSet<int>();
            foreach (var l in Collections.Values)
                foreach (var id in l) set.Add(id);
            return set;
        }
    }
    /// <summary>Points d'archive totaux (472 U1).</summary>
    public static int CollectionPoints { get; private set; }
    /// <summary>Données champ de bataille (482).</summary>
    public static (short Kind, byte Flag, int Value) BFData { get;
        private set; }
    /// <summary>Custom de classe (592) — 9 champs natifs.</summary>
    public static int[] ClassCustom { get; private set; }
        = Array.Empty<int>();
    /// <summary>Aptitude avancée (471).</summary>
    public static (byte Id, short V1, short V2, short V3)
        AdvancedAbility { get; private set; }  // obsolète (voir Talents)
    /// <summary>Garde-robe (488) — entrées brutes 27B par type.</summary>
    public static Dictionary<byte, List<byte[]>> Closets { get; }
        = new();
    /// <summary>Permis (509) — (itemId, location).</summary>
    public static List<(short ItemId, byte Loc)> Licenses { get;
        private set; } = new();
    /// <summary>Compétences de vie (543) — recipes/appris, exps, dues.</summary>
    public static short LifeSkillField { get; private set; }
    public static List<short> LifeSkillRecipes { get; private set; }
        = new();
    public static List<int> LifeSkillExps { get; private set; } = new();
    public static List<int> LifeSkillDues { get; private set; } = new();
    /// <summary>Activités recommandées (559).</summary>
    public static List<(short Id, int Count, byte Flag)> Activities
        { get; private set; } = new();
    /// <summary>Variables système (344) — index → (v1, v2).</summary>
    public static Dictionary<byte, (int, int)> SystemVars { get; }
        = new();
    /// <summary>Cooldowns actifs (368) — (kind, id, aux, ds restants).</summary>
    public static List<(byte Kind, short Id, short Aux, int RemainDs)>
        Cooldowns { get; private set; } = new();
    /// <summary>Guide nouveau joueur (551).</summary>
    public static (short F1, short F2) NewPlayerGuide { get;
        private set; }
    /// <summary>Info réanimation (531) — valeur/mode.</summary>
    public static int ReviveInfo { get; private set; }
    /// <summary>Drapeau fin de migration acquitté (382).</summary>
    public static bool MigrationDone { get; private set; }
    /// <summary>NodeTimes (563) — (node, times).</summary>
    public static (short Node, short Times) NodeTimes { get;
        private set; }
    /// <summary>Flags d'entité (302 FlagModify) — handle → reste
    /// brut du paquet (selector/op/actionId, 9 B wire).</summary>
    public static Dictionary<int, byte[]> EntityFlags { get; }
        = new();
    /// <summary>Dernier échec de commande (410) — (code, param).</summary>
    public static (int Code, int Param) LastCommandFail { get;
        private set; }
    /// <summary>État Limitless (2032) : exp ultime + palier.</summary>
    public static (int CharId, long Exp, short Level, int Perks,
        int NextExp) Limitless { get; private set; }
    /// <summary>Paliers du Limitless Pass (2033).</summary>
    public static List<(short Level, long ExpReq, string Rewards)>
        LimitlessPass { get; private set; } = new();
    /// <summary>Recrutements d'équipe reçus (519/583) — derniers 20.</summary>
    public static List<string> TeamRecruits { get; } = new();
    /// <summary>Enchantements actifs (301) — par effectId.</summary>
    public static Dictionary<short, ZoneEnchantUpdate.Entry> Enchants
        { get; } = new();
    /// <summary>Monde : reset time (40), prestige (60), coins (78 —
    /// liste type/total, vide au départ).</summary>
    public static (int F1, int F2) WorldResetTime { get; private set; }
    public static (short F1, short F2, short F3, int V)
        PrestigeData { get; private set; }
    /// <summary>Première monnaie spéciale (78) — (0,0) si aucune.</summary>
    public static (short Id, int Value) SpecialCoin { get;
        private set; }
    /// <summary>Monde : dernière invitation famille (48).</summary>
    public static (int CharId, string Family, string Inviter)
        FamilyInvitation { get; private set; }
    /// <summary>Monde : flag double connexion (54).</summary>
    public static bool RepeatedLogin { get; private set; }

    /// <summary>Réinitialise l'état à la connexion (nouveau perso).</summary>
    public static void Reset()
    {
        lock (_lock)
        {
            ShortcutBars = new();
            StarWay = new short[6]; StarWayText = "";
            PlayerCard = new short[12]; PlayerCardText = "";
            PlayerRoomInts = Array.Empty<int>(); PlayerRoomText = "";
            FastDress = new();
            Collections.Clear(); Closets.Clear(); SystemVars.Clear();
            Enchants.Clear(); TeamRecruits.Clear();
            EntityFlags.Clear();
            Licenses = new(); Activities = new(); Cooldowns = new();
            LifeSkillRecipes = new(); LifeSkillExps = new();
            LifeSkillDues = new(); LimitlessPass = new();
            MigrationDone = false; ReviveInfo = 0;
            RepeatedLogin = false;
        }
    }

    public static void RaiseChanged(string section)
        => Changed?.Invoke(section);

    // ---- Mutations (thread réseau, sous verrou) ----

    public static void Apply(ZoneEnterNodeShortcut m)
    {
        lock (_lock) ShortcutBars = m.Bars;
    }

    /// <summary>510 ChangeClassShortcut — les pages de la NOUVELLE
    /// classe remplacent les anciennes puis notifient la hotbar.</summary>
    public static void Apply(ZoneChangeClassShortcut m)
    {
        lock (_lock) ShortcutBars = m.Bars;
        RaiseChanged("shortcuts");
    }

    /// <summary>352 ReceiveShortcutOfPage — page demandée par
    /// GetShortcutOfPage (55) : remplit ShortcutBars[page] selon le
    /// mask (bit i+1 = slot i rempli) puis notifie la hotbar.</summary>
    public static void Apply(ZoneClientReceiveShortcutOfPage m)
    {
        if (m.Page is < 0 or > 10) return;
        lock (_lock)
        {
            while (ShortcutBars.Count <= m.Page)
                ShortcutBars.Add(
                    Enumerable.Repeat(-1, 10).ToArray());
            var slots = ShortcutBars[m.Page];
            Array.Fill(slots, -1);
            var vi = 0;
            for (var i = 0; i < 10 && i < 15; i++)
                if ((m.Mask & (1 << (i + 1))) != 0 &&
                    vi < m.Values.Length)
                    slots[i] = m.Values[vi++];
        }
        RaiseChanged("shortcuts");
    }

    public static void Apply(ZoneEnterStarWayData m)
    {
        lock (_lock) { StarWay = m.Fields; StarWayText = m.Text; }
    }

    public static void Apply(ZoneEnterNodePlayerCard m)
    {
        lock (_lock) { PlayerCard = m.Fields; PlayerCardText = m.Text; }
    }

    public static void Apply(ZoneEnterNodePlayerRoom m)
    {
        lock (_lock)
        {
            PlayerRoomInts = m.Ints; PlayerRoomText = m.Text;
        }
    }

    public static void Apply(ZoneEnterNodeFastDress m)
    {
        lock (_lock) FastDress = m.Records;
    }

    public static void Apply(ZoneUpdateCollection m)
    {
        // 472 : BookId = type (0 liste complète, 1 ajout, 2 retrait),
        // U1 = points totaux. Tout est rangé sous la clé 0 (archive
        // unique par compte).
        lock (_lock)
        {
            if (!Collections.TryGetValue(0, out var l))
                Collections[0] = l = new List<short>();
            if (m.BookId == 0) l.Clear();
            foreach (var c in m.CardIds)
            {
                if (m.BookId == 2) l.Remove(c);
                else if (!l.Contains(c)) l.Add(c);
            }
            CollectionPoints = m.U1;
        }
    }

    public static void Apply(ZoneUpdateBFData m)
    {
        lock (_lock) BFData = (m.Kind, m.Flag, m.Value);
    }

    public static void Apply(ZonePlayerClassCustomData m)
    {
        lock (_lock) ClassCustom = m.Fields;
    }

    /// <summary>Points investis par talent (effectId s16 → points).</summary>
    public static Dictionary<short, short> Talents { get; } = new();

    public static void Apply(ZoneUpdateAdvancedAbility m)
    {
        lock (_lock)
        {
            // 471 : type 0 = classe courante, 2 = autre classe (deltas) ;
            // type 1 = reset talents — le serveur réémet la map restante
            // (autres classes conservées) en SNAPSHOT (KB-16 :
            // ResetAdvancedAbilitiesAsync) → on remplace, on ne fusionne pas.
            if (m.Type == 1) Talents.Clear();
            if (m.Type is 0 or 1 or 2)
                foreach (var (id, pt) in m.Entries) Talents[id] = pt;
        }
    }

    public static void Apply(ZonePlayerClosets m)
    {
        lock (_lock)
        {
            if (!Closets.TryGetValue(m.Type, out var l))
                Closets[m.Type] = l = new List<byte[]>();
            foreach (var e in m.Entries) l.Add(e);
        }
    }

    public static void Apply(ZonePlayerLicense m)
    {
        // Le 509 porte le vecteur complet en réponse au 181, mais
        // seulement les licences MODIFIÉES en écho du 182 — merge par
        // itemId (loc mise à jour / nouvelle entrée), jamais remplacé.
        lock (_lock)
        {
            foreach (var e in m.Entries)
            {
                var i = Licenses.FindIndex(l => l.ItemId == e.ItemId);
                if (i >= 0) Licenses[i] = e;
                else Licenses.Add(e);
            }
        }
    }

    public static void Apply(ZonePlayerLifeSkill m)
    {
        lock (_lock)
        {
            LifeSkillField = m.Field;
            LifeSkillRecipes = m.RecipeIds;
            LifeSkillExps = m.Exps;
            LifeSkillDues = m.Dues;
        }
    }

    public static void Apply(ZoneRecommendActivities m)
    {
        lock (_lock) Activities = m.Entries;
    }

    public static void Apply(ZoneUpdateSystemVar m)
    {
        lock (_lock) SystemVars[m.Index] = (m.V1, m.V2);
    }

    public static void Apply(ZoneUpdateCoolDown m)
    {
        lock (_lock) Cooldowns = m.Entries;
        CooldownTracker.Apply(m);
    }

    public static void Apply(ZoneClientNewPlayerGuide m)
    {
        lock (_lock) NewPlayerGuide = (m.F1, m.F2);
    }

    public static void Apply(ZoneReviveInfo m)
    {
        lock (_lock) ReviveInfo = m.Value;
    }

    public static void Apply(ZoneClientMigrationDone m)
    {
        lock (_lock) MigrationDone = true;
    }

    public static void Apply(ZoneUpdateNodeTimes m)
    {
        lock (_lock) NodeTimes = (m.Node, m.Times);
    }

    public static void Apply(ZoneFlagModify m)
    {
        lock (_lock) EntityFlags[m.Field1] = m.Rest;
    }

    public static void Apply(ZoneCommandFailReturn m)
    {
        lock (_lock) LastCommandFail = (m.Code, m.Param);
    }

    public static void Apply(ZoneLimitlessState m)
    {
        lock (_lock)
            Limitless = (m.CharId, m.Exp, m.Level, m.Perks, m.NextExp);
    }

    public static void Apply(ZoneLimitlessPass m)
    {
        lock (_lock) LimitlessPass = m.Tiers;
    }

    public static void Apply(ZoneEnchantUpdate m)
    {
        lock (_lock)
        {
            foreach (var e in m.Entries)
                Enchants[e.EffectId] = e;
        }
    }

    public static void Apply(WorldResetTime m)
    {
        lock (_lock) WorldResetTime = (m.Field1, m.Field2);
    }

    public static void Apply(WorldUpdatePrestigeData m)
    {
        lock (_lock)
            PrestigeData = (m.Field1, m.Field2, m.Field3, m.Field4);
    }

    public static void Apply(WorldUpdateSpecialCoin m)
    {
        lock (_lock)
            SpecialCoin = (m.CoinId, m.Value);
    }

    public static void Apply(WorldFamilyInvitation m)
    {
        lock (_lock)
            FamilyInvitation =
                (m.InviterId, m.FamilyName, m.InviterName);
    }

    public static void Apply(WorldRepeatedLogin m)
    {
        lock (_lock) RepeatedLogin = m.Value != 0;
    }

    public static void ApplyTeamRecruit(string text)
    {
        lock (_lock)
        {
            TeamRecruits.Add(text);
            if (TeamRecruits.Count > 20) TeamRecruits.RemoveAt(0);
        }
    }

    public static void Apply(ZoneEnchantAdd m)
    {
        lock (_lock)
        {
            if (!Enchants.TryGetValue(m.EffectId, out var e))
                Enchants[m.EffectId] = e = new ZoneEnchantUpdate.Entry
                { EffectId = m.EffectId };
            e.RestSeconds = m.RestSeconds;
        }
    }
}

// ===================== Blocs EnterNode* (réponses) =================

/// <summary>S→C 351 ClientEnterNodeShortcut — barres de raccourcis.
/// Natif @0x8202200 : u16 barCount ; par barre u16 mask (bit0=1
/// sentinelle vide, slot i ↔ bit i+1), i32 par slot marqué, puis
/// u16 extraCount + i32 extras.</summary>
public sealed class ZoneEnterNodeShortcut : NetMessage
{
    public override int Opcode => 351;
    public override string Name => "Zone.EnterNodeShortcut";
    public List<int[]> Bars { get; } = new();
    public List<int[]> Extras { get; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        var n = s.ReadUShort();
        for (var b = 0; b < n; b++)
        {
            var mask = s.ReadUShort();
            var slots = new int[10];
            for (var i = 0; i < 10; i++)
                slots[i] = ((mask >> (i + 1)) & 1) != 0
                    ? s.ReadInt() : -1;
            Bars.Add(slots);
            var en = s.ReadUShort();
            var extras = new int[en];
            for (var i = 0; i < en; i++) extras[i] = s.ReadInt();
            Extras.Add(extras);
        }
    }
}

/// <summary>S→C 523 ClientEnterNodePlayerRoom — salon/pièce du joueur
/// (housing). Corps : 16 i32 + 5 s16 + u8/s16/u8/s16/u8/s16 + 3 s16 +
/// string + s16 — conservés en tableau brut.</summary>
public sealed class ZoneEnterNodePlayerRoom : NetMessage
{
    public override int Opcode => 523;
    public override string Name => "Zone.EnterNodePlayerRoom";
    public int[] Ints { get; private set; } = Array.Empty<int>();
    public string Text { get; private set; } = "";

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        var ints = new List<int>();
        // Ordre Serialize serveur : i32 s16 i32 i32 i32 i32 s16 i32
        // i32 i32 s16 i32 i32 i32 i32 u8 s16 u8 s16 u8 s16 s16 s16 s16
        // string s16 — on consomme selon le même ordre.
        ints.Add(s.ReadInt()); s.ReadShort();
        for (var i = 0; i < 4; i++) ints.Add(s.ReadInt());
        s.ReadShort();
        for (var i = 0; i < 3; i++) ints.Add(s.ReadInt());
        s.ReadShort();
        for (var i = 0; i < 4; i++) ints.Add(s.ReadInt());
        s.ReadByte(); s.ReadShort(); s.ReadByte(); s.ReadShort();
        s.ReadByte(); s.ReadShort(); s.ReadShort(); s.ReadShort();
        if (s.Remaining > 4) Text = s.ReadString();
        if (s.Remaining >= 2) s.ReadShort();
        Ints = ints.ToArray();
    }
}

/// <summary>S→C 593 ClientEnterNodeFastDress — records dressing rapide
/// [itemId, containerType, fromLoc, fromIndex, page, tarLoc] (6×i32,
/// FastDressPackets.cs serveur).</summary>
public sealed class ZoneEnterNodeFastDress : NetMessage
{
    public override int Opcode => 593;
    public override string Name => "Zone.EnterNodeFastDress";
    public List<int[]> Records { get; private set; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        var n = s.ReadUShort();
        Records = new List<int[]>(n);
        for (var i = 0; i < n; i++)
        {
            var r = new int[6];
            for (var j = 0; j < 6; j++) r[j] = s.ReadInt();
            Records.Add(r);
        }
    }
}

/// <summary>S→C 609 ClientEnterStarWayData — voie des étoiles :
/// 6 s16 + string.</summary>
public sealed class ZoneEnterStarWayData : NetMessage
{
    public override int Opcode => 609;
    public override string Name => "Zone.EnterStarWayData";
    public short[] Fields { get; private set; } = new short[6];
    public string Text { get; private set; } = "";

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        for (var i = 0; i < 6 && s.Remaining >= 2; i++)
            Fields[i] = s.ReadShort();
        if (s.Remaining > 0) Text = s.ReadString();
    }
}

/// <summary>S→C 490 ClientEnterNodePlayerCard — carte joueur :
/// 12 s16 + string + u8.</summary>
public sealed class ZoneEnterNodePlayerCard : NetMessage
{
    public override int Opcode => 490;
    public override string Name => "Zone.EnterNodePlayerCard";
    public short[] Fields { get; private set; } = new short[12];
    public string Text { get; private set; } = "";
    public byte Tail { get; private set; }

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        for (var i = 0; i < 12 && s.Remaining >= 2; i++)
            Fields[i] = s.ReadShort();
        if (s.Remaining > 1) Text = s.ReadString();
        if (s.Remaining > 0) Tail = s.ReadByte();
    }
}

// ===================== Mises à jour profil ========================

/// <summary>S→C 472 ClientUpdateCollection — livre de collection :
/// [s16 bookId][i32][u16 n][n×s16 cardId].</summary>
public sealed class ZoneUpdateCollection : NetMessage
{
    public override int Opcode => 472;
    public override string Name => "Zone.UpdateCollection";
    public short BookId { get; private set; }
    public int U1 { get; private set; }
    public List<short> CardIds { get; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        BookId = s.ReadShort();
        U1 = s.ReadInt();
        var n = s.ReadUShort();
        for (var i = 0; i < n; i++) CardIds.Add(s.ReadShort());
    }
}

/// <summary>S→C 482 ClientUpdateBFData — données battlefield :
/// [s16 kind][u8 flag][i32 value].</summary>
public sealed class ZoneUpdateBFData : NetMessage
{
    public override int Opcode => 482;
    public override string Name => "Zone.UpdateBFData";
    public short Kind { get; private set; }
    public byte Flag { get; private set; }
    public int Value { get; private set; }

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Kind = s.ReadShort();
        Flag = s.ReadByte();
        Value = s.ReadInt();
    }
}

/// <summary>S→C 592 ClientPlayerClassCustomData — données custom de
/// classe : u8 u8 s16 u8 s16 u8 i32 s16 i32.</summary>
public sealed class ZonePlayerClassCustomData : NetMessage
{
    public override int Opcode => 592;
    public override string Name => "Zone.PlayerClassCustomData";
    public int[] Fields { get; private set; } = new int[9];

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Fields[0] = s.ReadByte(); Fields[1] = s.ReadByte();
        Fields[2] = s.ReadShort(); Fields[3] = s.ReadByte();
        Fields[4] = s.ReadShort(); Fields[5] = s.ReadByte();
        Fields[6] = s.ReadInt();  Fields[7] = s.ReadShort();
        Fields[8] = s.ReadInt();
    }
}

/// <summary>S→C 471 ClientUpdateAdvancedAbility — talents de classe :
/// [u8 type (0 courante, 2 autre)][u16 n][n × (s16 effectId, s16 points)]
/// (cf. ZoneEntryFlow.AdvancedAbility serveur).</summary>
public sealed class ZoneUpdateAdvancedAbility : NetMessage
{
    public override int Opcode => 471;
    public override string Name => "Zone.UpdateAdvancedAbility";
    public byte Type { get; private set; }
    public List<(short Id, short Points)> Entries { get; } = new();
    /// <summary>Compat : premier couple (id, points) — l'ancien parse.</summary>
    public byte Id => Type;

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Type = s.ReadByte();
        var n = s.Remaining >= 2 ? s.ReadUShort() : 0;
        for (var i = 0; i < n && s.Remaining >= 4; i++)
            Entries.Add((s.ReadShort(), s.ReadShort()));
    }
}

/// <summary>S→C 488 ClientPlayerClosets — garde-robe :
/// [u8 type][u16 n][n×27B entrées brutes].</summary>
public sealed class ZonePlayerClosets : NetMessage
{
    public override int Opcode => 488;
    public override string Name => "Zone.PlayerClosets";
    public byte Type { get; private set; }
    public List<byte[]> Entries { get; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Type = s.ReadByte();
        var n = s.ReadUShort();
        for (var i = 0; i < n; i++) Entries.Add(s.ReadBytes(27));
    }
}

/// <summary>S→C 509 ClientPlayerLicense — permis détenus :
/// [u16 n][n×(s16 itemId][u8 loc)].</summary>
public sealed class ZonePlayerLicense : NetMessage
{
    public override int Opcode => 509;
    public override string Name => "Zone.PlayerLicense";
    public List<(short ItemId, byte Loc)> Entries { get; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        var n = s.ReadUShort();
        for (var i = 0; i < n; i++)
            Entries.Add((s.ReadShort(), s.ReadByte()));
    }
}

/// <summary>S→C 543 ClientPlayerLifeSkill — compétences de vie :
/// s16 + u16 n×s16 recipes + u16 n×i32 exps + u16 n×i32 dues.</summary>
public sealed class ZonePlayerLifeSkill : NetMessage
{
    public override int Opcode => 543;
    public override string Name => "Zone.PlayerLifeSkill";
    public short Field { get; private set; }
    public List<short> RecipeIds { get; private set; } = new();
    public List<int> Exps { get; private set; } = new();
    public List<int> Dues { get; private set; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Field = s.ReadShort();
        var n = s.ReadUShort();
        RecipeIds = new List<short>(n);
        for (var i = 0; i < n; i++) RecipeIds.Add(s.ReadShort());
        n = s.ReadUShort();
        Exps = new List<int>(n);
        for (var i = 0; i < n; i++) Exps.Add(s.ReadInt());
        n = s.ReadUShort();
        Dues = new List<int>(n);
        for (var i = 0; i < n; i++) Dues.Add(s.ReadInt());
    }
}

/// <summary>S→C 559 ClientRecommendActivities — activités :
/// s16 + i32 + u16 n×(s16,i32,u8) + u16 n×s16 tracing.</summary>
public sealed class ZoneRecommendActivities : NetMessage
{
    public override int Opcode => 559;
    public override string Name => "Zone.RecommendActivities";
    public short Field { get; private set; }
    public int Value { get; private set; }
    public List<(short Id, int Count, byte Flag)> Entries { get; }
        = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Field = s.ReadShort();
        Value = s.ReadInt();
        var n = s.ReadUShort();
        for (var i = 0; i < n; i++)
            Entries.Add((s.ReadShort(), s.ReadInt(), s.ReadByte()));
        n = s.ReadUShort();
        for (var i = 0; i < n; i++) s.ReadShort(); // ids traqués
    }
}

/// <summary>S→C 344 ClientUpdateSystemVar — variable système :
/// [u8 index][i32 v1][i32 v2].</summary>
public sealed class ZoneUpdateSystemVar : NetMessage
{
    public override int Opcode => 344;
    public override string Name => "Zone.UpdateSystemVar";
    public byte Index { get; private set; }
    public int V1 { get; private set; }
    public int V2 { get; private set; }

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Index = s.ReadByte(); V1 = s.ReadInt(); V2 = s.ReadInt();
    }
}

/// <summary>S→C 368 ClientUpdateCoolDown — cooldowns actifs :
/// [u8 n][n×(u8 kind][s16 id][s16 aux][i32 ds restants)].</summary>
public sealed class ZoneUpdateCoolDown : NetMessage
{
    public override int Opcode => 368;
    public override string Name => "Zone.UpdateCoolDown";
    public List<(byte Kind, short Id, short Aux, int RemainDs)> Entries
        { get; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        var n = s.ReadByte();
        for (var i = 0; i < n; i++)
            Entries.Add((s.ReadByte(), s.ReadShort(), s.ReadShort(),
                s.ReadInt()));
    }
}

/// <summary>S→C 519 ClientTeamRecruitMsg — message de recrutement :
/// str str u8 u8 str str s16 s16.</summary>
public sealed class ZoneTeamRecruitMsg : NetMessage
{
    public override int Opcode => 519;
    public override string Name => "Zone.TeamRecruitMsg";
    public string Summary { get; private set; } = "";

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        var name = s.ReadString();
        var title = s.ReadString();
        var f3 = s.ReadByte(); var f4 = s.ReadByte();
        var desc = s.ReadString(); var extra = s.ReadString();
        s.ReadShort(); s.ReadShort();
        Summary = $"{name} — {title} ({f3}/{f4}) {desc} {extra}"
            .Trim();
    }
}

/// <summary>S→C 583 ClientTeamRecruitMsgV2 — recrutement v2 :
/// u8 s16 s16 str i32.</summary>
public sealed class ZoneTeamRecruitMsgV2 : NetMessage
{
    public override int Opcode => 583;
    public override string Name => "Zone.TeamRecruitMsgV2";
    public string Summary { get; private set; } = "";

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        var flag = s.ReadByte();
        var f2 = s.ReadShort(); var f3 = s.ReadShort();
        var text = s.ReadString();
        var val = s.ReadInt();
        Summary = $"{text} ({f2},{f3},{flag},{val})".Trim();
    }
}

/// <summary>S→C 551 ClientNewPlayerGuide — guide nouveau joueur :
/// 2×s16. (Renommé ZoneClient* : ZoneNewPlayerGuide = C→S 218.)</summary>
public sealed class ZoneClientNewPlayerGuide : NetMessage
{
    public override int Opcode => 551;
    public override string Name => "Zone.ClientNewPlayerGuide";
    public short F1 { get; private set; }
    public short F2 { get; private set; }

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        F1 = s.ReadShort(); F2 = s.ReadShort();
    }
}

/// <summary>S→C 531 ClientReviveInfo — info de réanimation (mode/
/// délai) : i32.</summary>
public sealed class ZoneReviveInfo : NetMessage
{
    public override int Opcode => 531;
    public override string Name => "Zone.ReviveInfo";
    public int Value { get; private set; }

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
        => Value = s.ReadInt();
}

/// <summary>S→C 382 ClientMigrationDone — acquittement fin de
/// migration : s16 (généralement 0).</summary>
public sealed class ZoneClientMigrationDone : NetMessage
{
    public override int Opcode => 382;
    public override string Name => "Zone.ClientMigrationDone";
    public short Value { get; private set; }

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
        => Value = s.ReadShort();
}

/// <summary>S→C 563 ClientUpdateNodeTimes — compteurs par nœud :
/// s16 node + s16 times.</summary>
public sealed class ZoneUpdateNodeTimes : NetMessage
{
    public override int Opcode => 563;
    public override string Name => "Zone.UpdateNodeTimes";
    public short Node { get; private set; }
    public short Times { get; private set; }

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Node = s.ReadShort(); Times = s.ReadShort();
    }
}

/// <summary>S→C 410 ClientCommandFailReturn — échec de commande
/// (C→S rejeté) : i32 code + i32 param.</summary>
public sealed class ZoneCommandFailReturn : NetMessage
{
    public override int Opcode => 410;
    public override string Name => "Zone.CommandFailReturn";
    public int Code { get; private set; }
    public int Param { get; private set; }

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        Code = s.ReadInt(); Param = s.ReadInt();
    }
}

/// <summary>S→C 2032 Limitless — état Exp Ultime (custom serveur) :
/// [i32 charId][i64 exp][s16 level][u16 perks][i32 nextExp] =
/// 20 octets (ZoneEntryFlow.Limitless.cs EmitAsync).</summary>
public sealed class ZoneLimitlessState : NetMessage
{
    public override int Opcode => 2032;
    public override string Name => "Zone.LimitlessState";
    public int CharId { get; private set; }
    public long Exp { get; private set; }
    public short Level { get; private set; }
    public int Perks { get; private set; }
    public int NextExp { get; private set; }

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt(); Exp = s.ReadLong();
        Level = s.ReadShort(); Perks = s.ReadUShort();
        NextExp = s.ReadInt();
    }
}

/// <summary>S→C 2033 Limitless Pass — paliers :
/// [i32 charId][u16 n][n×(s16 level][i64 expReq][u16 len][utf8)].</summary>
public sealed class ZoneLimitlessPass : NetMessage
{
    public override int Opcode => 2033;
    public override string Name => "Zone.LimitlessPass";
    public int CharId { get; private set; }
    public List<(short Level, long ExpReq, string Rewards)> Tiers
        { get; } = new();

    public override void Serialize(BinWriter s) =>
        throw new NotSupportedException("S→C");

    public override void Deserialize(BinReader s)
    {
        CharId = s.ReadInt();
        var n = s.ReadUShort();
        for (var i = 0; i < n; i++)
        {
            var lvl = s.ReadShort();
            var req = s.ReadLong();
            var len = s.ReadUShort();
            var txt = System.Text.Encoding.UTF8.GetString(
                s.ReadBytes(len));
            Tiers.Add((lvl, req, txt));
        }
    }
}
