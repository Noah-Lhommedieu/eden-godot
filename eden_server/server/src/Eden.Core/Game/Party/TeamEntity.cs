namespace Eden.Core.Game.Party;

/// <summary>
/// Entite de groupe — correspond a CTeamManager / CTeamMember.
/// Party (5 joueurs) et Raid (jusqu'a 25) partagent la meme structure.
/// </summary>
public sealed class TeamEntity
{
    public const int MaxPartySize = 5;
    public const int MaxRaidSize = 25;

    public int Id { get; set; }
    public int LeaderCharacterId { get; set; }
    public bool IsRaid { get; set; }
    /// <summary>Capacité raid courante — `CForceTeam+0x54` : op 1
    /// `ChangeToRaid` = 10, op 6 `CreateRaid25Team` = 25 (désasm
    /// TeamOperation @zone:532135).</summary>
    public int RaidCap { get; set; } = MaxRaidSize;
    public List<TeamMember> Members { get; } = new();
    /// <summary>Règle de loot native `CForceTeam+0x50` — valeurs wire
    /// 1=Free (texte 208), 2=Random (207), 3=Taunt-Exclusive (2156) ;
    /// c'est cette valeur qui part dans `ClientTeamInfo` type 9 et le
    /// TeamMode du 324. Sémantique native : 1-2 = redistribution
    /// aléatoire uniforme au ramassage (`GetLooter`), 3 = le tueur
    /// garde (seul lui peut ramasser — `CanPickUp`), autre = premier
    /// éligible après le curseur (séquentiel).</summary>
    public short LootRuleValue { get; set; } = 1;
    /// <summary>Seuil qualité du roll `CForceTeam+0x58` — 0=White,
    /// 1=Green, 2=Blue (textes client 2887-2889). Wire :
    /// `ClientTeamInfo` type 10.</summary>
    public short RollRule { get; set; }
    /// <summary>Curseur de loot `CForceTeam+0x52` — index dans
    /// <see cref="Members"/> du DERNIER membre servi ; les éligibles
    /// sont parcourus à partir de l'index suivant (rotation native
    /// de <c>GetLootOrderMember</c>). −1 = personne encore servi.</summary>
    public int LootCursor = -1;
    /// <summary>Flag « pas de partage d'exp » — `CForceTeam+0x56` :
    /// `SetExpRule` natif met 1 quand la règle d'exp d'équipe est
    /// > 2 (1/2 = partagée). Quand levé, seul le tueur reçoit
    /// `ExpForTeamNoShare`, les autres 0.</summary>
    public bool ExpNoShare { get; set; }
    public int TargetMapId { get; set; }
    /// <summary>evalId du trial en cours — `CForceTeam+0x9c` natif,
    /// posé par `EnterEvaluate` (zone @0x80fa620) et consulté par
    /// `CharacterKillMonster` pour créditer les kills à la bonne
    /// instance quand plusieurs équipes partagent la même arène.
    /// 0 = pas d'évaluation en cours.</summary>
    public int EvalId { get; set; }
    /// <summary>Nœud du trial associé à <see cref="EvalId"/> —
    /// nécessaire au rejoin (TeamOperation op4 : un membre hérite de
    /// l'evalId d'un coéquipier déjà en instance, natif
    /// `CNE_CZ_ZoneServerTeamOperation::Execute` chemin « member
    /// already in evaluate »).</summary>
    public int EvalNodeId { get; set; }

    /// <summary>Type d'équipe natif `CForceTeam+0x64` — déduit de la
    /// capacité comme `SetMaxTeamSize` : 5→1 (party), 10→2 (raid10),
    /// 25→3 (raid25, exp et or non partagés), autre→0.</summary>
    public int TeamType => !IsRaid ? 1 : RaidCap switch
    {
        10 => 2,
        25 => 3,
        _ => 0,
    };

    public int MaxSize => IsRaid ? RaidCap : MaxPartySize;
    public bool IsFull => Members.Count >= MaxSize;

    public sealed class TeamMember
    {
        public int CharacterId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Level { get; set; }
        public int ClassId { get; set; }
        public bool IsLeader { get; set; }
        public bool IsOnline { get; set; } = true;
        public int MapId { get; set; }
    }
}
