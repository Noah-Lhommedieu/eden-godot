using System.Collections.Concurrent;

namespace Eden.Zone;

/// <summary>
/// Trésors au sol (drop d'items quand l'inventaire est plein) — miroir
/// du <c>CTreasure</c> natif (<c>CItemFactory::CreateTreasure</c>
/// @0x808c2b0 + <c>DropTreasureOnFloorPos</c> @0x809d290 +
/// <c>CEventTreasure</c> @0x866fd50).
///
/// Cycle natif (EventTreasure::Execute) :
///   1. spawn ~10 ms → OnAddNewTreasure (sight, propriétaire seul),
///   2. +S_Parameter[341] = 120 s → PublicTreasure (visible de tous),
///   3. +30 s → CleanTreasure (despawn).
/// Spécification serveur de test (prompt3 P) : rétention 5 min réelles
/// au total → fenêtre propriétaire 270 s + 30 s public = 300 s.
///
/// Les ids vivent dans la plage native
/// <c>CTreasure::SetAllocateIDRange(0x713fb301, 1999999999)</c>
/// (zone/decompiled.c ligne 236627) — distincte des sight-handles
/// mobs/NPC pour éviter toute collision dans les maps `Sent` client.
/// </summary>
public sealed class GroundTreasureService
{
    /// <summary>Premier handle de trésor — borne basse native.</summary>
    public const uint FirstTreasureId = 0x713F_B301;

    /// <summary>Fenêtre d'exclusivité propriétaire (secondes). Natif :
    /// S_Parameter[341] = 120 ; ici 270 pour une rétention totale de
    /// 5 min réelles (spécification utilisateur).</summary>
    public const double OwnerSeconds = 270.0;

    /// <summary>Durée de visibilité publique après la fenêtre
    /// propriétaire — 30 s fixes natifs (EventTreasure : +0x1e s).</summary>
    public const double PublicSeconds = 30.0;

    /// <summary>Trésor posé au sol — équivalent des champs runtime du
    /// CTreasure (+0x28/+0x2c position, +0x50 id, +0x60 owner).</summary>
    public sealed class Treasure
    {
        /// <summary>Handle du trésor (plage 0x713FB301+).</summary>
        public required uint Id;
        /// <summary>Node (map) où l'item a été posé.</summary>
        public required int NodeId;
        /// <summary>Instance propriétaire — 0 = scène partagée,
        /// sinon le groupe (team.Id/-charId). Un drop en donjon
        /// n'existe que dans l'instance de celui qui l'a créé
        /// (CNodeGroup natif).</summary>
        public int InstanceKey;
        /// <summary>Template S_Item de l'item posé.</summary>
        public required int ItemId;
        /// <summary>Quantité posée (clone+0xc natif).</summary>
        public required int Count;
        /// <summary>charId du propriétaire — 0 = public d'emblée.</summary>
        public required int OwnerCharId;
        /// <summary>Position du drop (unités monde — x, z au sol).</summary>
        public required float X, Z;
        /// <summary>Horodatage du spawn.</summary>
        public DateTime SpawnedAt;
        /// <summary>Fin de la fenêtre propriétaire.</summary>
        public DateTime PublicAt;
        /// <summary>Horodatage de despawn (SpawnedAt + Owner+Public).</summary>
        public DateTime ExpireAt;
        /// <summary>Vrai quand la fenêtre propriétaire est passée —
        /// le trésor devient visible/prenable par tous
        /// (<c>CTreasure::PublicTreasure</c> natif).</summary>
        public bool IsPublic;
        /// <summary>Vrai quand un roll d'équipe est en cours sur ce
        /// trésor — `CTreasure::SetRoll(true)` natif : le pickup
        /// direct est alors refusé jusqu'à la résolution.</summary>
        public bool IsRolled;
        /// <summary>Mission ayant produit ce trésor de quête
        /// (MonsterTreasureGot posé au sol sur inventaire plein) —
        /// 0 = loot ordinaire. Au pickup : op 145 zone→mission pour
        /// créditer l'objectif, comme un drop direct en sac.</summary>
        public int QuestMissionId;
    }

    private readonly ConcurrentDictionary<uint, Treasure> _treasures =
        new();
    private uint _nextId = FirstTreasureId;

    /// <summary>Snapshot des trésors d'un (node,instance).</summary>
    public Treasure[] ListForNode(int nodeId, int inst) =>
        _treasures.Values.Where(t => t.NodeId == nodeId &&
            t.InstanceKey == inst).ToArray();

    /// <summary>Recherche par handle.</summary>
    public bool TryGet(uint id, out Treasure treasure) =>
        _treasures.TryGetValue(id, out treasure!);

    /// <summary>Pose un trésor au sol près de (x, z) — miroir de
    /// <c>DropTreasureOnFloorPos</c> natif (anneau autour du joueur :
    /// le natif teste les 4 quadrants ±rayon puis retombe sur la
    /// position du joueur ; on tire directement un point de l'anneau,
    /// le sol n'étant pas testé par IsBlock côté C#).</summary>
    public Treasure Spawn(int nodeId, int inst, int ownerCharId,
        float x, float z, int itemId, int count, DateTime now,
        int questMissionId = 0)
    {
        var angle = Random.Shared.NextDouble() * Math.PI * 2.0;
        var radius = 1.5f + (float)Random.Shared.NextDouble() * 1.5f;
        var t = new Treasure
        {
            Id = NextId(),
            NodeId = nodeId,
            InstanceKey = inst,
            ItemId = itemId,
            Count = count,
            OwnerCharId = ownerCharId,
            QuestMissionId = questMissionId,
            X = x + (float)Math.Cos(angle) * radius,
            Z = z + (float)Math.Sin(angle) * radius,
            SpawnedAt = now,
            PublicAt = now + TimeSpan.FromSeconds(OwnerSeconds),
            ExpireAt = now + TimeSpan.FromSeconds(
                OwnerSeconds + PublicSeconds),
        };
        _treasures[t.Id] = t;
        return t;
    }

    /// <summary>Retire le trésor (ramassage réussi ou despawn) —
    /// TryRemove : un seul ramasseur l'emporte, le client perdant
    /// verra le trésor disparaître.</summary>
    public bool TryRemove(uint id, out Treasure treasure) =>
        _treasures.TryRemove(id, out treasure!);

    /// <summary>Passe les fenêtres de temps : retourne les trésors à
    /// publiciser (fenêtre propriétaire écoulée → broadcast 329 u8=1)
    /// et ceux à despawner (expiration → broadcast 330). Appelé par le
    /// timer de la zone (miroir du CEventTreasure natif).</summary>
    public (List<Treasure> ToPublic, List<Treasure> ToDespawn)
        Sweep(DateTime now)
    {
        var toPublic = new List<Treasure>();
        var toDespawn = new List<Treasure>();
        foreach (var t in _treasures.Values)
        {
            if (now >= t.ExpireAt)
            {
                if (TryRemove(t.Id, out var dead)) toDespawn.Add(dead);
            }
            else if (!t.IsPublic && now >= t.PublicAt)
            {
                t.IsPublic = true;
                toPublic.Add(t);
            }
        }
        return (toPublic, toDespawn);
    }

    /// <summary>Nettoie les trésors d'un (node,instance) — changement
    /// de map (inst=0 partout ailleurs) ou destruction d'instance.</summary>
    public List<Treasure> ClearNode(int nodeId, int inst)
    {
        var removed = new List<Treasure>();
        foreach (var t in _treasures.Values)
            if (t.NodeId == nodeId && t.InstanceKey == inst &&
                TryRemove(t.Id, out var dead))
                removed.Add(dead);
        return removed;
    }

    /// <summary>Nettoie tous les trésors d'une instance détruite
    /// (NodeGroupReset / disband / déco solo).</summary>
    public void ClearInstance(int inst)
    {
        foreach (var t in _treasures.Values.ToArray())
            if (t.InstanceKey == inst)
                TryRemove(t.Id, out _);
    }

    private uint NextId()
    {
        // Plage native 0x713FB301..+1999999999 — wrapping défensif.
        var id = _nextId;
        _nextId = _nextId >= FirstTreasureId + 1_999_999_999u
            ? FirstTreasureId : _nextId + 1;
        return id;
    }
}
