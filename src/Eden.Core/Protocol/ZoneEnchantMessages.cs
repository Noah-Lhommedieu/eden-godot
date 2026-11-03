using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages « enchants / statuts » (buffs & debuffs) — lot Vague 2.
//
// Sources de preuve :
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.Status.cs — cycles
//     add/remove des enchants joueur, gate icône, broadcast voyants.
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.DisplayChar.cs —
//     BuildEnchantUpdate (301 émis au snapshot SeeChar d'un joueur
//     portant des status).
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.cs —
//     HandleEnchantUpdateAsync (18 → 301 vide côté mobs).
//   - ZoneMessages.g.cs serveur : CNC_CZ_ClientEnchantAdd/Pause/
//     Remove/Reset/Start/Update/TeamMemberEnchant (Serialize natifs
//     @0x81e9840, @0x81e98d0, @0x822fb70 — corps 23B/15B corrigés
//     manuellement côté serveur).
//   - docs/generated/ncid_map.json pour les numéros NCID.
//
// Identité : pour les joueurs le « handle » du wire EST le charId
// (les mobs portent des sight-handles 0xFFFFAxxx mais n'ont pas de
// chemin d'émission d'enchants côté serveur C# — limite documentée).

// ------------------------- S→C : ajout / retrait / refresh ---------

/// <summary>S→C NCID 299 — ClientEnchantAdd : un enchant visible
/// vient d'être posé sur une entité. Corps 23 B
/// <c>[i32 handle][s16 effectId][i32 restSec][u8 serial]
/// [s16 stackDisplay][i32 flagBits][s16 level][i32 counter2]</c>.
/// Émis au joueur ET broadcast aux voyants — le client suit donc les
/// buffs des autres joueurs par handle.</summary>
public sealed class ZoneEnchantAdd : NetMessage
{
    public override int Opcode => 299;
    public override string Name => "Zone.ClientEnchantAdd";

    /// <summary>i32 — handle entité (charId joueur).</summary>
    public int Handle { get; set; }
    /// <summary>s16 — id de l'enchant (S_Enchant / c_enchant).</summary>
    public short EffectId { get; set; }
    /// <summary>i32 — durée restante en secondes (−1 = permanente
    /// côté natif ; les enchants « -9 » ne produisent pas de 299).</summary>
    public int RestSeconds { get; set; }
    /// <summary>u8 — char+0x280 serial (non utilisé par l'UI).</summary>
    public byte Serial { get; set; }
    /// <summary>s16 — cumul affiché (« ×N » de l'icône) — StackCount
    /// capé col59, pas le compteur de recast brut.</summary>
    public short StackCount { get; set; }
    /// <summary>i32 — (sets &amp; 0x7c000000)&gt;&gt;0x1a natif ; 0
    /// côté serveur C# → inutilisé pour la classification.</summary>
    public int FlagBits { get; set; }
    /// <summary>s16 — CEnchant+0x3c : palier du sort.</summary>
    public short Level { get; set; }
    /// <summary>i32 — CEnchant+0x34 (compteur interne, non affiché).</summary>
    public int Counter2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteShort(EffectId);
        s.WriteInt(RestSeconds);
        s.WriteByte(Serial);
        s.WriteShort(StackCount);
        s.WriteInt(FlagBits);
        s.WriteShort(Level);
        s.WriteInt(Counter2);
    }

    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        EffectId = s.ReadShort();
        RestSeconds = s.ReadInt();
        Serial = s.ReadByte();
        StackCount = s.ReadShort();
        FlagBits = s.ReadInt();
        Level = s.ReadShort();
        Counter2 = s.ReadInt();
    }
}

/// <summary>S→C NCID 300 — ClientEnchantRemove : fin d'un enchant
/// visible. Corps 15 B <c>[i32 handle][s16 effectId][u8 act]
/// [i32 f4][i32 f5]</c> (Serialize natif @0x81e98d0).</summary>
public sealed class ZoneEnchantRemove : NetMessage
{
    public override int Opcode => 300;
    public override string Name => "Zone.ClientEnchantRemove";

    /// <summary>i32 — handle entité.</summary>
    public int Handle { get; set; }
    /// <summary>s16 — id de l'enchant retiré.</summary>
    public short EffectId { get; set; }
    /// <summary>u8 — action natif (0 fin naturelle, autre =
    /// dispel/abandon — non consommé par l'UI).</summary>
    public byte Act { get; set; }
    /// <summary>i32 — champ natif +0xc (observé 0).</summary>
    public int Field4 { get; set; }
    /// <summary>i32 — champ natif +0x10 (observé 0).</summary>
    public int Field5 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteShort(EffectId);
        s.WriteByte(Act);
        s.WriteInt(Field4);
        s.WriteInt(Field5);
    }

    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        EffectId = s.ReadShort();
        Act = s.ReadByte();
        Field4 = s.ReadInt();
        Field5 = s.ReadInt();
    }
}

/// <summary>S→C NCID 301 — ClientEnchantUpdate : liste complète des
/// enchants visibles d'une entité (snapshot). Corps variable
/// <c>[i32 handle][u8 n][n×(s16 effectId][i32 casterId][i32 restSec]
/// [s16 level][s16 f4][i32 note)][i32 lifeFlags][u8 rev]</c> —
/// Serialize natif @0x822fb70 (l'entrée « SpellId » générée porte en
/// réalité l'effect_id, cf. BuildEnchantUpdate serveur).</summary>
public sealed class ZoneEnchantUpdate : NetMessage
{
    public override int Opcode => 301;
    public override string Name => "Zone.ClientEnchantUpdate";

    /// <summary>Un enchant visible de l'entité.</summary>
    public sealed class Entry
    {
        /// <summary>s16 — effect_id (le champ s'appelle SpellId dans
        /// la classe générée mais le serveur y écrit EffectId).</summary>
        public short EffectId { get; set; }
        /// <summary>i32 — handle/charId du lanceur.</summary>
        public int CasterId { get; set; }
        /// <summary>i32 — durée restante en secondes.</summary>
        public int RestSeconds { get; set; }
        /// <summary>s16 — level de l'enchant (champ « Stacks » du
        /// codec généré = CEnchant+0x3c côté DisplayChar).</summary>
        public short Level { get; set; }
        /// <summary>s16 — champ natif f4 (observé 0).</summary>
        public short F4 { get; set; }
        /// <summary>i32 — note native (observé 0).</summary>
        public int Note { get; set; }
    }

    /// <summary>i32 — handle entité décrite.</summary>
    public int Handle { get; set; }
    /// <summary>Enchants visibles (u8 n).</summary>
    public List<Entry> Entries { get; } = new();
    /// <summary>i32 — LifeFlags natif (dead/mounted/invincible).</summary>
    public int Flags { get; set; }
    /// <summary>u8 — révision (+0x118 natif).</summary>
    public byte Revision { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteByte((byte)Entries.Count);
        foreach (var e in Entries)
        {
            s.WriteShort(e.EffectId);
            s.WriteInt(e.CasterId);
            s.WriteInt(e.RestSeconds);
            s.WriteShort(e.Level);
            s.WriteShort(e.F4);
            s.WriteInt(e.Note);
        }
        s.WriteInt(Flags);
        s.WriteByte(Revision);
    }

    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        Entries.Clear();
        var n = s.ReadByte();
        for (var i = 0; i < n; i++)
        {
            Entries.Add(new Entry
            {
                EffectId = s.ReadShort(),
                CasterId = s.ReadInt(),
                RestSeconds = s.ReadInt(),
                Level = s.ReadShort(),
                F4 = s.ReadShort(),
                Note = s.ReadInt(),
            });
        }
        Flags = s.ReadInt();
        Revision = s.ReadByte();
    }
}

/// <summary>S→C NCID 315 — ClientEnchantReset
/// <c>[s16][i32]</c>. Jamais émis par le serveur C# (codec posé pour
/// la couverture du protocole — le client viderait les enchants du
/// handle concerné).</summary>
public sealed class ZoneEnchantReset : NetMessage
{
    public override int Opcode => 315;
    public override string Name => "Zone.ClientEnchantReset";

    /// <summary>s16 — champ natif (type de reset).</summary>
    public short Field1 { get; set; }
    /// <summary>i32 — handle entité.</summary>
    public int Handle { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Field1);
        s.WriteInt(Handle);
    }

    public override void Deserialize(BinReader s)
    {
        Field1 = s.ReadShort();
        Handle = s.ReadInt();
    }
}

/// <summary>S→C NCID 372 — ClientTeamMemberEnchant : enchants
/// visibles d'un membre d'équipe (icônes groupe). Corps boucle
/// <c>[s16 serial][i32 memberCharId][u8 n][n×(s16 effectId]
/// [s16 counter][s16 level][i32 restSec)]</c> — la classe générée
/// serveur est la version aplatie d'une entrée ; la boucle réelle est
/// prouvée par NotifyTeamEnchantAsync (Status.cs).</summary>
public sealed class ZoneTeamMemberEnchant : NetMessage
{
    public override int Opcode => 372;
    public override string Name => "Zone.ClientTeamMemberEnchant";

    /// <summary>Un enchant d'un membre d'équipe.</summary>
    public sealed class Entry
    {
        /// <summary>s16 — effect_id.</summary>
        public short EffectId { get; set; }
        /// <summary>s16 — CEnchant+0x30 counter (0 côté serveur).</summary>
        public short Counter { get; set; }
        /// <summary>s16 — level (+0x3c).</summary>
        public short Level { get; set; }
        /// <summary>i32 — rest_time (s).</summary>
        public int RestSeconds { get; set; }
    }

    /// <summary>s16 — serial char+0x288.</summary>
    public short Serial { get; set; }
    /// <summary>i32 — charId du membre concerné.</summary>
    public int MemberCharId { get; set; }
    /// <summary>Enchants visibles du membre.</summary>
    public List<Entry> Entries { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Serial);
        s.WriteInt(MemberCharId);
        s.WriteByte((byte)Entries.Count);
        foreach (var e in Entries)
        {
            s.WriteShort(e.EffectId);
            s.WriteShort(e.Counter);
            s.WriteShort(e.Level);
            s.WriteInt(e.RestSeconds);
        }
    }

    public override void Deserialize(BinReader s)
    {
        Serial = s.ReadShort();
        MemberCharId = s.ReadInt();
        Entries.Clear();
        var n = s.ReadByte();
        for (var i = 0; i < n; i++)
        {
            Entries.Add(new Entry
            {
                EffectId = s.ReadShort(),
                Counter = s.ReadShort(),
                Level = s.ReadShort(),
                RestSeconds = s.ReadInt(),
            });
        }
    }
}

/// <summary>S→C NCID 445 — ClientEnchantPause : le décompte d'un
/// enchant est suspendu. <c>[i32 handle][s16 effectId][u8][i32]</c>
/// — écho serveur du 127 (CombatHandlers.cs).</summary>
public sealed class ZoneEnchantPause : NetMessage
{
    public override int Opcode => 445;
    public override string Name => "Zone.ClientEnchantPause";

    /// <summary>i32 — handle entité.</summary>
    public int Handle { get; set; }
    /// <summary>s16 — effect_id.</summary>
    public short EffectId { get; set; }
    /// <summary>u8 — flag natif.</summary>
    public byte Flag { get; set; }
    /// <summary>i32 — valeur native (reste au moment du freeze).</summary>
    public int Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteShort(EffectId);
        s.WriteByte(Flag);
        s.WriteInt(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        EffectId = s.ReadShort();
        Flag = s.ReadByte();
        Field4 = s.ReadInt();
    }
}

/// <summary>S→C NCID 446 — ClientEnchantStart : reprise du décompte
/// d'un enchant suspendu — même corps que 445 (écho du 128).</summary>
public sealed class ZoneEnchantStart : NetMessage
{
    public override int Opcode => 446;
    public override string Name => "Zone.ClientEnchantStart";

    /// <summary>i32 — handle entité.</summary>
    public int Handle { get; set; }
    /// <summary>s16 — effect_id.</summary>
    public short EffectId { get; set; }
    /// <summary>u8 — flag natif.</summary>
    public byte Flag { get; set; }
    /// <summary>i32 — valeur native.</summary>
    public int Field4 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteInt(Handle);
        s.WriteShort(EffectId);
        s.WriteByte(Flag);
        s.WriteInt(Field4);
    }

    public override void Deserialize(BinReader s)
    {
        Handle = s.ReadInt();
        EffectId = s.ReadShort();
        Flag = s.ReadByte();
        Field4 = s.ReadInt();
    }
}

// ------------------------- C→S ------------------------------------

/// <summary>C→S NCID 18 — ZoneServerEnchantUpdate : demande la liste
/// des enchants d'une entité (cible). <c>[u32 handle]</c> — le
/// serveur répond un 301 (vide pour les mobs : pas d'enchants
/// persistants — HandleEnchantUpdateAsync).</summary>
public sealed class ZoneEnchantUpdateRequest : NetMessage
{
    public override int Opcode => 18;
    public override string Name => "Zone.ServerEnchantUpdate";

    /// <summary>u32 — handle entité à rafraîchir.</summary>
    public uint TargetHandle { get; set; }

    public override void Serialize(BinWriter s) =>
        s.WriteUInt(TargetHandle);

    public override void Deserialize(BinReader s) =>
        TargetHandle = s.ReadUInt();
}

/// <summary>C→S NCID 85 — ZoneServerEnchantCancel : abandon volontaire
/// d'un buff (clic droit natif sur l'icône). <c>[s16 effectId]</c> —
/// le serveur refuse les enchants « can't abandon » (col8 bit 0x10).</summary>
public sealed class ZoneEnchantCancel : NetMessage
{
    public override int Opcode => 85;
    public override string Name => "Zone.ServerEnchantCancel";

    /// <summary>s16 — effect_id à retirer.</summary>
    public short EffectId { get; set; }

    public override void Serialize(BinWriter s) => s.WriteShort(EffectId);
    public override void Deserialize(BinReader s) =>
        EffectId = s.ReadShort();
}

/// <summary>C→S NCID 127 — ZoneServerEnchantPause : suspend le
/// décompte d'un enchant. <c>[s16 effectId]</c> → écho 445.</summary>
public sealed class ZoneEnchantPauseRequest : NetMessage
{
    public override int Opcode => 127;
    public override string Name => "Zone.ServerEnchantPause";

    /// <summary>s16 — effect_id.</summary>
    public short EffectId { get; set; }

    public override void Serialize(BinWriter s) => s.WriteShort(EffectId);
    public override void Deserialize(BinReader s) =>
        EffectId = s.ReadShort();
}

/// <summary>C→S NCID 128 — ZoneServerEnchantStart : reprend le
/// décompte d'un enchant suspendu. <c>[s16 effectId]</c> → écho 446.</summary>
public sealed class ZoneEnchantStartRequest : NetMessage
{
    public override int Opcode => 128;
    public override string Name => "Zone.ServerEnchantStart";

    /// <summary>s16 — effect_id.</summary>
    public short EffectId { get; set; }

    public override void Serialize(BinWriter s) => s.WriteShort(EffectId);
    public override void Deserialize(BinReader s) =>
        EffectId = s.ReadShort();
}

// ------------------------- store client ---------------------------

/// <summary>
/// État client des enchants visibles par entité — alimenté par les
/// NCIDs 299/300/301/372/315 et consommé par <c>BuffBar</c> (self),
/// <c>TargetFrame.SetBuffs</c> (cible) et <c>PartyHud</c> (membres).
///
/// Le décompte est local : le wire porte des secondes au moment de
/// l'émission, l'UI fait défiler entre deux updates (un enchant dont
/// le reste tombe à 0 disparaît — le serveur émet son 300 de toute
/// façon, l'expiration locale n'est qu'une anticipation visuelle).
///
/// Thread : la mutation est faite par le pont réseau déjà rebasculé
/// sur le thread principal (file <c>_pending</c>) — le store n'est
/// donc pas verrouillé, même convention que <see cref="PlayerBag"/>.
/// </summary>
public static class EntityBuffs
{
    /// <summary>Un enchant visible d'une entité.</summary>
    public sealed class BuffInst
    {
        /// <summary>effect_id (S_Enchant / c_enchant).</summary>
        public int EffectId;
        /// <summary>charId/handle du lanceur.</summary>
        public int CasterId;
        /// <summary>Durée restante au moment de la réception (s ;
        /// négatif = permanent).</summary>
        public int RestSeconds;
        /// <summary>Cumul affiché (« ×N ») — champ stack du 299 ;
        /// pour les entrées 301/372 (level) reste à 1.</summary>
        public int StackCount = 1;
        /// <summary>Palier de l'enchant.</summary>
        public int Level = 1;
        /// <summary>Décompte figé (445/446).</summary>
        public bool Paused;
        /// <summary>Horodatage UTC de la réception — base du décompte
        /// local.</summary>
        public DateTime ReceivedUtc = DateTime.UtcNow;

        /// <summary>Secondes restantes estimées maintenant
        /// (−1 = permanent).</summary>
        public double Remaining
        {
            get
            {
                if (RestSeconds < 0) return -1;
                if (Paused) return RestSeconds;
                return Math.Max(0, RestSeconds -
                    (DateTime.UtcNow - ReceivedUtc).TotalSeconds);
            }
        }
    }

    private static readonly Dictionary<int, List<BuffInst>> _map = new();

    /// <summary>Émis (thread principal) après toute mutation — les UI
    /// s'y abonnent pour se repeindre.</summary>
    public static event Action? Changed;

    /// <summary>Copie de la liste d'un handle (jamais null).</summary>
    public static List<BuffInst> Snapshot(int handle)
    {
        Prune(handle);
        return _map.TryGetValue(handle, out var l)
            ? new List<BuffInst>(l) : new List<BuffInst>();
    }

    /// <summary>299 : ajoute/remplace l'enchant d'un handle.</summary>
    public static void ApplyAdd(ZoneEnchantAdd m)
    {
        var list = ListFor(m.Handle);
        list.RemoveAll(b => b.EffectId == (m.EffectId & 0xffff));
        list.Add(new BuffInst
        {
            EffectId = m.EffectId & 0xffff,
            RestSeconds = m.RestSeconds,
            StackCount = Math.Max(1, (int)m.StackCount),
            Level = Math.Max(1, (int)m.Level),
            ReceivedUtc = DateTime.UtcNow,
        });
        Changed?.Invoke();
    }

    /// <summary>300 : retire l'enchant d'un handle.</summary>
    public static void ApplyRemove(ZoneEnchantRemove m)
    {
        if (_map.TryGetValue(m.Handle, out var l) &&
            l.RemoveAll(b => b.EffectId == (m.EffectId & 0xffff)) > 0)
            Changed?.Invoke();
    }

    /// <summary>301 : remplace la liste complète d'un handle
    /// (snapshot cible/SeeChar — liste vide = purge).</summary>
    public static void ApplyUpdate(ZoneEnchantUpdate m)
    {
        var list = ListFor(m.Handle);
        list.Clear();
        foreach (var e in m.Entries)
        {
            list.Add(new BuffInst
            {
                EffectId = e.EffectId & 0xffff,
                CasterId = e.CasterId,
                RestSeconds = e.RestSeconds,
                Level = Math.Max(1, (int)e.Level),
                ReceivedUtc = DateTime.UtcNow,
            });
        }
        Changed?.Invoke();
    }

    /// <summary>372 : remplace la liste d'un membre d'équipe
    /// (même sémantique snapshot que le 301, charId membre).</summary>
    public static void ApplyTeamMember(ZoneTeamMemberEnchant m)
    {
        var list = ListFor(m.MemberCharId);
        list.Clear();
        foreach (var e in m.Entries)
        {
            list.Add(new BuffInst
            {
                EffectId = e.EffectId & 0xffff,
                RestSeconds = e.RestSeconds,
                Level = Math.Max(1, (int)e.Level),
                ReceivedUtc = DateTime.UtcNow,
            });
        }
        Changed?.Invoke();
    }

    /// <summary>315 : vide la table d'un handle (jamais émis par le
    /// serveur actuel — posé pour complétude).</summary>
    public static void ApplyReset(ZoneEnchantReset m)
    {
        if (_map.Remove(m.Handle)) Changed?.Invoke();
    }

    /// <summary>445/446 : fige/défige le décompte d'un enchant.</summary>
    public static void ApplyPause(int handle, short effectId,
        bool paused)
    {
        if (!_map.TryGetValue(handle, out var l)) return;
        var b = l.FirstOrDefault(x => x.EffectId == (effectId & 0xffff));
        if (b == null || b.Paused == paused) return;
        // Rebase le reste figé à la valeur courante.
        var rem = b.Remaining;
        b.Paused = paused;
        b.RestSeconds = rem < 0 ? -1 : (int)Math.Ceiling(rem);
        b.ReceivedUtc = DateTime.UtcNow;
        Changed?.Invoke();
    }

    /// <summary>Purge les entrées expirées d'un handle (le 300
    /// serveur suit de toute façon — anticipation visuelle du
    /// décompte arrivé à 0).</summary>
    private static void Prune(int handle)
    {
        if (_map.TryGetValue(handle, out var l) &&
            l.RemoveAll(b => b.Remaining == 0) > 0)
            Changed?.Invoke();
    }

    /// <summary>Purge périodique appelée par BuffBridge — permet au
    /// décompte arrivé à 0 de disparaître même sans snapshot.</summary>
    public static void PruneAll()
    {
        var any = false;
        foreach (var l in _map.Values)
            any |= l.RemoveAll(b => b.Remaining == 0) > 0;
        if (any) Changed?.Invoke();
    }

    /// <summary>Retire toutes les entrées d'un handle (despawn).</summary>
    public static void Clear(int handle)
    {
        if (_map.Remove(handle)) Changed?.Invoke();
    }

    /// <summary>Vide tout (changement de zone/déconnexion).</summary>
    public static void ClearAll()
    {
        if (_map.Count == 0) return;
        _map.Clear();
        Changed?.Invoke();
    }

    private static List<BuffInst> ListFor(int handle)
    {
        if (!_map.TryGetValue(handle, out var l))
            _map[handle] = l = new List<BuffInst>();
        return l;
    }
}
