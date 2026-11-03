namespace Eden.Core.Game.Services;

/// <summary>
/// Registre des salles personnelles côté zone — miroir de la
/// `map&lt;room_id,CPlayerRoom*&gt;` native `CZoneServer+0x564`,
/// remplie par les pushes `CNE_ZMis_ZoneServerUpdateRoom` (779) de
/// la mission et consultée par `EnterRoomCheck`
/// (`CPlayerRoomManager` @0x814eaf0).
/// </summary>
public sealed class RoomRegistry
{
    /// <summary>Une salle — champs du struct `CPlayerRoom` zone
    /// (copie champ à champ dans l'Execute 779 @0x833f9c0) :
    /// +0 room_id, +4 name, +8 hotel_type(u16), +0xc/0x10/0x14/0x18
    /// privilèges (ordre wire : friend, family, lover, other),
    /// +0x1c populity, +0x20/0x24/0x28 stats, +0x2c extend,
    /// +0x30 due_date, +0x34/0x38/0x3c textures, +0x40 busy(u8),
    /// +0x44 party_time.</summary>
    public sealed class ZoneRoom
    {
        public int RoomId { get; init; }
        public string Name { get; set; } = "";
        /// <summary>Type d'hôtel (1 perso / 2 duo / 3 famille) —
        /// comparé au s16 du paquet 207 dans EnterRoomCheck.</summary>
        public short HotelType { get; set; }
        public int FriendPrivilege { get; set; }
        public int FamilyPrivilege { get; set; }
        public int LoverPrivilege { get; set; }
        public int OtherPrivilege { get; set; }
        public short Populity { get; set; }
        public int Stat1 { get; set; }
        public int Stat2 { get; set; }
        public int Stat3 { get; set; }
        public short Extend { get; set; }
        /// <summary>Échéance de location epoch — `&lt; now` → texte
        /// 0x10d7 dans EnterRoomCheck.</summary>
        public int DueDate { get; set; }
        public int Wallpaper { get; set; }
        public int LivingFloor { get; set; }
        public int BedFloor { get; set; }
        /// <summary>Mode d'édition/occupation — `room+0x40` natif :
        /// non nul → « salle occupée » (texte 0xef7).</summary>
        public byte Busy { get; set; }
        public int PartyTime { get; set; }
    }

    private readonly Dictionary<int, ZoneRoom> _rooms = new();
    private readonly object _gate = new();

    /// <summary>Applique un push 779 : op 1 = suppression,
    /// 2/3 = création ou mise à jour (Execute natif : case 1 efface
    /// + expulse les visiteurs, cases 2/3 créent/mettent à jour).
    /// Retourne la salle fusionnée (null si suppression).</summary>
    public ZoneRoom? ApplyUpdate(short op, ZoneRoom update)
    {
        lock (_gate)
        {
            if (op == 1)
            {
                _rooms.Remove(update.RoomId);
                return null;
            }
            _rooms[update.RoomId] = update;
            return update;
        }
    }

    /// <summary>Recherche par room_id (`CZoneServer+0x564` map).</summary>
    public ZoneRoom? Get(int roomId)
    {
        lock (_gate)
            return _rooms.TryGetValue(roomId, out var r) ? r : null;
    }

    /// <summary>Nombre de salles connues.</summary>
    public int Count { get { lock (_gate) return _rooms.Count; } }
}

/// <summary>
/// Cache des classements côté zone — miroir des maps natives
/// `CZoneServer+0x5e0`/`+0x5f8` remplies par `CNE_ZMis_ZoneServerRankInfo`
/// (763) et lues par `ZoneServerRankInfoQuery` (194 → 521).
/// Un enregistrement = `SRankInfoData` (0x38) : nom + 9 champs i32
/// (+4..+0x24) + str2 (+0x28) + liste de chaînes (+0x2c).
/// </summary>
public sealed class RankInfoCache
{
    /// <summary>Un jeu de classement pour un type donné.</summary>
    public sealed class RankSet
    {
        /// <summary>Noms des rangés (vec&lt;str&gt; du 763, +0 des
        /// records).</summary>
        public List<string> Names { get; set; } = new();
        /// <summary>9 vecteurs i32 parallèles (champs +4..+0x24 du
        /// SRankInfoData reconstruit par l'Execute zone natif).</summary>
        public List<int>[] Cols { get; set; } =
            Enumerable.Range(0, 9).Select(_ => new List<int>()).ToArray();
        /// <summary>Version (`+0x8e` s16 du 763) — le client renvoie
        /// sa version connue ; égale → pas de repush natif.</summary>
        public short Version { get; set; }
        /// <summary>2ᵉ chaîne par record (+0x28, vec&lt;str&gt; du 763
        /// — ex. nom du chef de famille pour le type 200).</summary>
        public List<string> Str2s { get; set; } = new();
        /// <summary>Listes de chaînes par record (+0x2c, vec&lt;vec&lt;
        /// str&gt;&gt; du 763 — ex. membres d'équipe du type 600).</summary>
        public List<List<string>> StrLists { get; set; } = new();
    }

    private readonly Dictionary<short, RankSet> _sets = new();
    private readonly object _gate = new();

    /// <summary>Remplace le jeu d'un type (push 763).</summary>
    public void Set(short type, RankSet set)
    {
        lock (_gate) _sets[type] = set;
    }

    /// <summary>Jeu d'un type — null si jamais poussé.</summary>
    public RankSet? Get(short type)
    {
        lock (_gate)
            return _sets.TryGetValue(type, out var s) ? s : null;
    }
}
