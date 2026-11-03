using EdenClient.Net;

namespace EdenClient.Protocol;

// Messages « chemin d'éveil » (destiny road) + compteurs fortune —
// lot Vague 2.
//
// Sources de preuve :
//   - $EDEN/server/src/Eden.Zone/ZoneEntryFlow.Destiny.cs —
//     transcription complète du sous-système natif
//     (DestinyRoadAction @0x810d610, DestinyDiceEvent @0x810d190,
//     NotifyUpdateDestinyRoad @0x8104710, SDestinyRoad::Serialize
//     @0x863b750).
//   - ZoneMessages.g.cs serveur : CNC_CZ_ClientPlayerDestinyRoad
//     (534), ClientPlayerDestinyRoadEvent (535),
//     ClientUpdateDestinyRoad (589), ClientDestinyDiceFailed (590),
//     ClientUpdateFortuneBagCounter (557) ; CNE_CZ_ZoneServerDestinyDice
//     (203), ZoneServerQueryDestinyRoad (204),
//     ZoneServerQueryFortuneBagCounter (223).
//   - docs/generated/ncid_map.json pour les numéros NCID.
//
// Types du 589 (CExecutionerData::UpdateDestinyRoad mission
// @0x83331f0) : 0=step, 1=dice_time(rollsLeft), 2=dpPool,
// 3=dice_point(movePoints), 4=function_state, 5=slot[v2]=v1,
// 6=flag reroll, 7=destiny_vit.

// ------------------------- S→C : route / événements ---------------

/// <summary>S→C NCID 534 — ClientPlayerDestinyRoad : état complet de
/// la route active (ou bloc tout-à-zéro après fin de route — le
/// client vide le plateau). Corps :
/// <c>[s16 roadId][s16 roadType][s16 step][s16 rollsLeft]
/// [i32 dpPool][s16 movePoints][s16 functionState][u8 rerollFlag]
/// [u16 n][n×s16 slots]</c> — n = 39 cases, chaque slot = l'eventId
/// résolu par le serveur (pioche uniforme dans le groupe de la
/// case, cf. DestinyDataParser).</summary>
public sealed class ZonePlayerDestinyRoad : NetMessage
{
    public override int Opcode => 534;
    public override string Name => "Zone.ClientPlayerDestinyRoad";

    /// <summary>s16 — id de route (C_DestinyRoad ; 0 = pas de route).</summary>
    public short RoadId { get; set; }
    /// <summary>s16 — type/tier de la route.</summary>
    public short RoadType { get; set; }
    /// <summary>s16 — case courante (0-38).</summary>
    public short Step { get; set; }
    /// <summary>s16 — lancers de dé restants.</summary>
    public short RollsLeft { get; set; }
    /// <summary>i32 — réserve de Destiny Points (coût lancer =
    /// S_Parameter 376).</summary>
    public int DpPool { get; set; }
    /// <summary>s16 — points du dernier lancer (avancée en attente).</summary>
    public short MovePoints { get; set; }
    /// <summary>s16 — bits des fonctions déjà consommées (f0-f15).</summary>
    public short FunctionState { get; set; }
    /// <summary>u8 — flag « choix de dé » armé par l'item f1.</summary>
    public byte RerollFlag { get; set; }
    /// <summary>39 eventIds de cases (u16 n puis s16×n).</summary>
    public List<short> Slots { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(RoadId);
        s.WriteShort(RoadType);
        s.WriteShort(Step);
        s.WriteShort(RollsLeft);
        s.WriteInt(DpPool);
        s.WriteShort(MovePoints);
        s.WriteShort(FunctionState);
        s.WriteByte(RerollFlag);
        s.WriteUShort((ushort)Slots.Count);
        foreach (var v in Slots) s.WriteShort(v);
    }

    public override void Deserialize(BinReader s)
    {
        RoadId = s.ReadShort();
        RoadType = s.ReadShort();
        Step = s.ReadShort();
        RollsLeft = s.ReadShort();
        DpPool = s.ReadInt();
        MovePoints = s.ReadShort();
        FunctionState = s.ReadShort();
        RerollFlag = s.ReadByte();
        Slots.Clear();
        var n = s.ReadUShort();
        for (var i = 0; i < n && s.Remaining >= 2; i++)
            Slots.Add(s.ReadShort());
    }
}

/// <summary>S→C NCID 535 — ClientPlayerDestinyRoadEvent : événement
/// déclenché à l'arrivée sur la case <c>step</c> (op 4).
/// <c>[s16 eventId]</c> — l'effet (or/exp/mob/enchant/dés) est déjà
/// appliqué côté serveur ; le client affiche le bandeau.</summary>
public sealed class ZonePlayerDestinyRoadEvent : NetMessage
{
    public override int Opcode => 535;
    public override string Name => "Zone.ClientPlayerDestinyRoadEvent";

    /// <summary>s16 — id d'événement (C_DestinyEvent).</summary>
    public short EventId { get; set; }

    public override void Serialize(BinWriter s) => s.WriteShort(EventId);
    public override void Deserialize(BinReader s) =>
        EventId = s.ReadShort();
}

/// <summary>S→C NCID 589 — ClientUpdateDestinyRoad : update atomique
/// d'un champ de la route <c>[s16 type][s16 v1][s16 v2]</c> —
/// NotifyUpdateDestinyRoad natif (types 0-7, voir en-tête).</summary>
public sealed class ZoneUpdateDestinyRoad : NetMessage
{
    public override int Opcode => 589;
    public override string Name => "Zone.ClientUpdateDestinyRoad";

    /// <summary>s16 — champ mis à jour (0 step, 1 dés, 2 DP, 3 move,
    /// 4 fonctions, 5 slot, 6 reroll, 7 vitalité).</summary>
    public short Type { get; set; }
    /// <summary>s16 — valeur (eventId pour type 5).</summary>
    public short V1 { get; set; }
    /// <summary>s16 — index de case (type 5 uniquement).</summary>
    public short V2 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Type);
        s.WriteShort(V1);
        s.WriteShort(V2);
    }

    public override void Deserialize(BinReader s)
    {
        Type = s.ReadShort();
        V1 = s.ReadShort();
        V2 = s.ReadShort();
    }
}

/// <summary>S→C NCID 590 — ClientDestinyDiceFailed : refus d'une
/// action destin <c>[s16 op]</c> (op = l'op du 203 refusée — scène
/// interdite, mort, scène versus).</summary>
public sealed class ZoneDestinyDiceFailed : NetMessage
{
    public override int Opcode => 590;
    public override string Name => "Zone.ClientDestinyDiceFailed";

    /// <summary>s16 — op refusée (0,1,4).</summary>
    public short Op { get; set; }

    public override void Serialize(BinWriter s) => s.WriteShort(Op);
    public override void Deserialize(BinReader s) => Op = s.ReadShort();
}

/// <summary>S→C NCID 557 — ClientUpdateFortuneBagCounter : compteurs
/// des sacs fortune <c>[s16 n][n×(s16 bagId][s16 count)]</c>.</summary>
public sealed class ZoneUpdateFortuneBagCounter : NetMessage
{
    public override int Opcode => 557;
    public override string Name => "Zone.ClientUpdateFortuneBagCounter";

    /// <summary>Paire bagId → compteur.</summary>
    public List<(short BagId, short Count)> Entries { get; } = new();

    public override void Serialize(BinWriter s)
    {
        s.WriteShort((short)Entries.Count);
        foreach (var (bagId, count) in Entries)
        {
            s.WriteShort(bagId);
            s.WriteShort(count);
        }
    }

    public override void Deserialize(BinReader s)
    {
        Entries.Clear();
        var n = s.ReadShort();
        for (var i = 0; i < n && s.Remaining >= 4; i++)
            Entries.Add((s.ReadShort(), s.ReadShort()));
    }
}

// ------------------------- C→S ------------------------------------

/// <summary>C→S NCID 203 — ZoneServerDestinyDice : action destin
/// <c>[s16 op][s16 p2][s16 p3]</c>. Ops
/// (DestinyRoadAction @0x810d610) : 0 = créer la route (vitalité
/// ≥100), 1 = lancer le dé, 2 = item destin (p2=itemId, p3=index
/// inventaire), 3 = fonction/choix (p2 : index fonction f0-15, ou
/// face de dé si reroll armé), 4 = avancer de movePoints.</summary>
public sealed class ZoneDestinyDice : NetMessage
{
    public override int Opcode => 203;
    public override string Name => "Zone.ServerDestinyDice";

    /// <summary>s16 — op 0-4.</summary>
    public short Op { get; set; }
    /// <summary>s16 — paramètre 2 (fonction / itemId / face dé).</summary>
    public short P2 { get; set; }
    /// <summary>s16 — paramètre 3 (index inventaire pour op 2).</summary>
    public short P3 { get; set; }

    public override void Serialize(BinWriter s)
    {
        s.WriteShort(Op);
        s.WriteShort(P2);
        s.WriteShort(P3);
    }

    public override void Deserialize(BinReader s)
    {
        Op = s.ReadShort();
        P2 = s.ReadShort();
        P3 = s.ReadShort();
    }
}

/// <summary>C→S NCID 204 — ZoneServerQueryDestinyRoad : requête
/// l'état de la route → 534 si route active, rien sinon
/// (Execute natif @0x8275db0). Corps vide.</summary>
public sealed class ZoneQueryDestinyRoad : NetMessage
{
    public override int Opcode => 204;
    public override string Name => "Zone.ServerQueryDestinyRoad";

    public override void Serialize(BinWriter s) { /* corps vide */ }
    public override void Deserialize(BinReader s) { /* corps vide */ }
}

/// <summary>C→S NCID 223 — ZoneServerQueryFortuneBagCounter : requête
/// les compteurs de sacs fortune → 557. Corps vide.</summary>
public sealed class ZoneQueryFortuneBagCounter : NetMessage
{
    public override int Opcode => 223;
    public override string Name => "Zone.ServerQueryFortuneBagCounter";

    public override void Serialize(BinWriter s) { /* corps vide */ }
    public override void Deserialize(BinReader s) { /* corps vide */ }
}

// ------------------------- état client ----------------------------

/// <summary>
/// État destin du joueur — alimenté par 534 (complet) et 589
/// (updates atomiques), consommé par <c>DestinyWindow</c>.
/// Mutations sur le thread principal uniquement (file _pending du
/// bridge — même convention que <see cref="EntityBuffs"/>).
/// </summary>
public sealed class DestinyState
{
    /// <summary>Instance partagée alimentée par les binds précoces
    /// de HudExtensions (mutations thread réseau via les méthodes
    /// <c>ApplyData</c>, événements ré-émis en file principale —
    /// même invariant que <c>VendingState</c>). La fenêtre consomme
    /// cette instance — l'état ne dépend plus de son montage.</summary>
    public static DestinyState Shared { get; } = new();

    /// <summary>Route active (null après fin de route / reset).</summary>
    public sealed class Route
    {
        public int RoadId;
        public int RoadType;
        /// <summary>Case courante 0-38.</summary>
        public int Step;
        /// <summary>Lancers restants.</summary>
        public int RollsLeft;
        /// <summary>Réserve de Destiny Points.</summary>
        public int DpPool;
        /// <summary>Points du dernier lancer en attente d'avancée.</summary>
        public int MovePoints;
        /// <summary>Bits des fonctions f0-f15 déjà utilisées.</summary>
        public int FunctionState;
        /// <summary>Choix de dé armé par l'item f1.</summary>
        public bool Reroll;
        /// <summary>39 eventIds de cases.</summary>
        public readonly short[] Slots = new short[39];
    }

    /// <summary>Route courante (null = plateau vide).</summary>
    public Route? Current { get; private set; }

    /// <summary>Vitalité destin (589 type 7 — jamais poussée à
    /// l'entrée ; -1 = « inconnue » tant qu'aucun update n'arrive).</summary>
    public int DestinyVit { get; private set; } = -1;

    /// <summary>Dernier événement de case (535) — bandeau.</summary>
    public int LastEventId { get; private set; }

    /// <summary>Compteurs fortune 557 (bagId → count).</summary>
    public readonly Dictionary<int, int> FortuneBags = new();

    /// <summary>Émis après mutation (thread principal).</summary>
    public event Action? Changed;

    /// <summary>Émis à la réception d'un 535 (bandeau événement).</summary>
    public event Action<int>? EventReceived;

    /// <summary>Émis à la réception d'un 590 (op refusée).</summary>
    public event Action<short>? OpFailed;

    /// <summary>534 : remplace l'état complet (bloc 0 = reset).
    /// Variante <c>Data</c> : mutation seule, sans événement —
    /// appelable depuis le thread réseau.</summary>
    public void ApplyData(ZonePlayerDestinyRoad m)
    {
        if (m.RoadId == 0)
        {
            Current = null;
        }
        else
        {
            var r = new Route
            {
                RoadId = m.RoadId,
                RoadType = m.RoadType,
                Step = m.Step,
                RollsLeft = m.RollsLeft,
                DpPool = m.DpPool,
                MovePoints = m.MovePoints,
                FunctionState = m.FunctionState,
                Reroll = m.RerollFlag != 0,
            };
            for (var i = 0; i < r.Slots.Length && i < m.Slots.Count; i++)
                r.Slots[i] = m.Slots[i];
            Current = r;
        }
    }

    /// <summary>534 : mutation + notification (thread principal).</summary>
    public void Apply(ZonePlayerDestinyRoad m)
    {
        ApplyData(m);
        Changed?.Invoke();
    }

    /// <summary>589 : update atomique (type 7 = vitalité — le seul
    /// champ qui vit hors route). Variante <c>Data</c> : mutation
    /// seule — thread réseau.</summary>
    public void ApplyData(ZoneUpdateDestinyRoad m)
    {
        if (m.Type == 7)
        {
            DestinyVit = m.V1;
            return;
        }
        var r = Current;
        if (r == null) return;
        switch (m.Type)
        {
            case 0: r.Step = m.V1; break;
            case 1: r.RollsLeft = m.V1; break;
            case 2: r.DpPool = m.V1; break;
            case 3: r.MovePoints = m.V1; break;
            case 4: r.FunctionState = m.V1; break;
            case 5:
                if (m.V2 is >= 0 and < 39) r.Slots[m.V2] = m.V1;
                break;
            case 6: r.Reroll = m.V1 != 0; break;
        }
    }

    /// <summary>589 : mutation + notification (thread principal).</summary>
    public void Apply(ZoneUpdateDestinyRoad m)
    {
        ApplyData(m);
        Changed?.Invoke();
    }

    /// <summary>535 : événement de case — mutation seule.</summary>
    public void ApplyData(ZonePlayerDestinyRoadEvent m)
    {
        LastEventId = m.EventId;
    }

    /// <summary>535 : mutation + notifications (thread principal).</summary>
    public void Apply(ZonePlayerDestinyRoadEvent m)
    {
        ApplyData(m);
        EventReceived?.Invoke(m.EventId);
        Changed?.Invoke();
    }

    /// <summary>590 : op refusée → notification.</summary>
    public void Apply(ZoneDestinyDiceFailed m) =>
        OpFailed?.Invoke(m.Op);

    /// <summary>557 : compteurs fortune — mutation seule.</summary>
    public void ApplyData(ZoneUpdateFortuneBagCounter m)
    {
        FortuneBags.Clear();
        foreach (var (bagId, count) in m.Entries)
            FortuneBags[bagId] = count;
    }

    /// <summary>557 : mutation + notification (thread principal).</summary>
    public void Apply(ZoneUpdateFortuneBagCounter m)
    {
        ApplyData(m);
        Changed?.Invoke();
    }

    /// <summary>Ré-émet <see cref="Changed"/> (file principale).</summary>
    public void RaiseChanged() => Changed?.Invoke();

    /// <summary>Ré-émet <see cref="EventReceived"/> pour le dernier
    /// événement de case (file principale).</summary>
    public void RaiseEventReceived(int id) => EventReceived?.Invoke(id);

    /// <summary>Ré-émet <see cref="OpFailed"/> (file principale).</summary>
    public void RaiseOpFailed(short op) => OpFailed?.Invoke(op);
}
