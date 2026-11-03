using Eden.Core.Game.Party;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service de gestion des groupes — correspond a CTeamManager.
/// Party, raid, invitations, kick, partage d'XP (CEC_PartyExp),
/// distribution du loot.
/// </summary>
public sealed class TeamService
{
    private readonly Dictionary<int, TeamEntity> _teams = new();
    private readonly Dictionary<int, int> _teamByCharacter = new();
    /// <summary>Invitations en attente : inviteId -> (inviterId, targetId).
    /// L'id est interne — le wire natif ne le véhicule jamais
    /// (<c>ClientTeamInviteNotice</c> ne porte que l'inviteur) : la
    /// réponse se résout par la paire (inviteur, cible), voir
    /// <see cref="AnswerInviteByPair"/>.</summary>
    private readonly Dictionary<int, (int InviterId, int TargetId)> _invites = new();
    private int _nextInviteId = 1;
    private int _nextTeamId = 1;

    /// <summary>Notifié à chaque changement d'appartenance :
    /// <c>(charId, leftTeamId)</c> — leftTeamId = équipe quittée
    /// (0 pour un join). La zone s'y abonne pour recalculer la clé
    /// d'instance (<c>CNodeGroup</c> natif) du perso et détruire le
    /// groupe d'instances devenu orphelin.</summary>
    public Action<int, int>? MembershipChanged;

    /// <summary>Invite un joueur — CNE_CZ_ZoneServerTeamInvite.</summary>
    public (int InviteId, int InviterId)? Invite(int inviterId, int targetId)
    {
        if (inviterId == targetId || _teamByCharacter.ContainsKey(targetId))
            return null;
        var id = _nextInviteId++;
        _invites[id] = (inviterId, targetId);
        return (id, inviterId);
    }

    /// <summary>
    /// Réponse à une invitation — CNE_CZ_ZoneServerTeamInviteAnswer.
    /// Accepté : crée l'équipe si l'inviteur n'en a pas, puis ajoute le membre.
    /// </summary>
    public TeamEntity? AnswerInvite(int inviteId, int targetId, bool accepted,
        string targetName, int targetLevel, int targetClassId)
    {
        if (!_invites.Remove(inviteId, out var inv) || inv.TargetId != targetId || !accepted)
            return null;
        return AcceptInvite(inv.InviterId, targetId, targetName,
            targetLevel, targetClassId);
    }

    /// <summary>Réponse par paire (inviteur, cible) — chemin wire
    /// natif : <c>ZoneServerTeamInviteAnswer</c> porte
    /// <c>+0x14</c> = charId de l'inviteur et <c>+0x12</c> = type de
    /// réponse, jamais d'id d'invitation (Execute @0x82918f0). Une
    /// seule invitation peut être pendante pour une paire donnée —
    /// le premier match gagne.</summary>
    public TeamEntity? AnswerInviteByPair(int inviterId, int targetId,
        bool accepted, string targetName, int targetLevel,
        int targetClassId)
    {
        if (!accepted) return null;
        var id = _invites.FirstOrDefault(kv =>
            kv.Value.InviterId == inviterId &&
            kv.Value.TargetId == targetId).Key;
        if (id == 0 || !_invites.Remove(id)) return null;
        return AcceptInvite(inviterId, targetId, targetName,
            targetLevel, targetClassId);
    }

    /// <summary>Crée l'équipe si besoin et ajoute le membre —
    /// partagé par les deux chemins de réponse.</summary>
    private TeamEntity? AcceptInvite(int inviterId, int targetId,
        string targetName, int targetLevel, int targetClassId)
    {
        // Créer l'équipe si l'inviteur n'est pas groupé
        var team = GetTeamOf(inviterId)
            ?? CreateTeam(inviterId, $"#{inviterId}", 1, 0);
        AddMember(team.Id, targetId, targetName, targetLevel, targetClassId);
        return team;
    }

    /// <summary>Annule une invitation sortante — CNE_CZ_ZoneServerTeamInviteCancel.</summary>
    public bool CancelInvite(int inviterId)
    {
        var id = _invites.FirstOrDefault(kv => kv.Value.InviterId == inviterId).Key;
        return id != 0 && _invites.Remove(id);
    }

    /// <summary>Cree une equipe — CCharacter::OnCreateTeam.</summary>
    public TeamEntity CreateTeam(int leaderId, string leaderName, int level,
        int classId, bool isRaid = false, int mapId = 0)
    {
        // Quitter l'equipe actuelle si deja membre
        LeaveTeam(leaderId);

        var team = new TeamEntity
        {
            Id = _nextTeamId++,
            LeaderCharacterId = leaderId,
            IsRaid = isRaid,
            TargetMapId = mapId,
        };
        team.Members.Add(new TeamEntity.TeamMember
        {
            CharacterId = leaderId,
            Name = leaderName,
            Level = level,
            ClassId = classId,
            IsLeader = true,
            MapId = mapId,
        });

        _teams[team.Id] = team;
        _teamByCharacter[leaderId] = team.Id;
        MembershipChanged?.Invoke(leaderId, 0);
        return team;
    }

    /// <summary>Ajoute un membre — invitation acceptee.</summary>
    public bool AddMember(int teamId, int characterId, string name, int level,
        int classId, int mapId = 0)
    {
        if (!_teams.TryGetValue(teamId, out var team)) return false;
        if (team.IsFull) return false;
        if (_teamByCharacter.ContainsKey(characterId)) return false;

        team.Members.Add(new TeamEntity.TeamMember
        {
            CharacterId = characterId,
            Name = name,
            Level = level,
            ClassId = classId,
            MapId = mapId,
        });
        _teamByCharacter[characterId] = team.Id;
        MembershipChanged?.Invoke(characterId, 0);
        return true;
    }

    /// <summary>Quitte l'equipe — CCharacter::OnQuitFromTeam.</summary>
    public bool LeaveTeam(int characterId)
    {
        if (!_teamByCharacter.TryGetValue(characterId, out var teamId)) return false;
        var team = _teams[teamId];
        team.Members.RemoveAll(m => m.CharacterId == characterId);
        _teamByCharacter.Remove(characterId);

        if (team.Members.Count == 0)
        {
            _teams.Remove(teamId);
        }
        else if (team.LeaderCharacterId == characterId)
        {
            // Promouvoir le prochain membre en leader
            team.LeaderCharacterId = team.Members[0].CharacterId;
            team.Members[0].IsLeader = true;
        }
        MembershipChanged?.Invoke(characterId, teamId);
        return true;
    }

    /// <summary>Kick un membre (leader uniquement).</summary>
    public bool KickMember(int leaderId, int targetId)
    {
        if (!_teamByCharacter.TryGetValue(leaderId, out var teamId)) return false;
        var team = _teams[teamId];
        if (team.LeaderCharacterId != leaderId) return false;
        if (targetId == leaderId) return false;
        return LeaveTeam(targetId);
    }

    /// <summary>Passe le leadership.</summary>
    public bool TransferLeadership(int leaderId, int targetId)
    {
        if (!_teamByCharacter.TryGetValue(leaderId, out var teamId)) return false;
        var team = _teams[teamId];
        if (team.LeaderCharacterId != leaderId) return false;
        var target = team.Members.Find(m => m.CharacterId == targetId);
        var old = team.Members.Find(m => m.CharacterId == leaderId);
        if (target == null || old == null) return false;
        old.IsLeader = false;
        target.IsLeader = true;
        team.LeaderCharacterId = targetId;
        return true;
    }

    /// <summary>Changement de règle de loot — `CForceTeam::ChageLootRule`
    /// (op TeamOperation 0) : leader uniquement, valeurs natives 1-3
    /// ((ushort)(rule-1) &lt; 3 — hors plage = ignoré comme le natif).
    /// 1=Free, 2=Random, 3=Taunt-Exclusive (textes client 208/207/2156).
    /// Retourne la règle appliquée ou null (pas leader / valeur
    /// invalide) pour que l'appelant sache s'il doit notifier.</summary>
    public TeamEntity? ChangeLootRule(int characterId, short rule)
    {
        var team = GetTeamOf(characterId);
        if (team == null || team.LeaderCharacterId != characterId)
            return null;
        if ((ushort)(rule - 1) >= 3) return null;
        team.LootRuleValue = rule;
        // Couplage natif : ChageLootRule écrit aussi +0x56 — la
        // règle 3 (Taunt-Exclusive) coupe le partage d'exp (le tueur
        // garde la sienne) ; 1-2 → partage actif.
        team.ExpNoShare = rule == 3;
        return team;
    }

    /// <summary>Changement de règle de roll — `CForceTeam::ChageRollRule`
    /// (op 3) : leader uniquement, seuil qualité 0=White/1=Green/2=Blue
    /// (textes 2887-2889). Le natif n'impose pas de borne — valeur
    /// stockée telle quelle.</summary>
    public TeamEntity? ChangeRollRule(int characterId, short rule)
    {
        var team = GetTeamOf(characterId);
        if (team == null || team.LeaderCharacterId != characterId)
            return null;
        team.RollRule = rule;
        return team;
    }

    /// <summary>Conversion en raid — `CForceTeam::ChangeToRaid` (op 1,
    /// cap=10) / `CreateRaid25Team` (op 6, cap=25). Leader uniquement ;
    /// le refus natif « membre en node-type 5 » (texte 1579) n'a pas
    /// d'équivalent ici : pas de notion d'instance sur les membres —
    /// on refuse seulement si l'équipe dépasse déjà la capacité cible.</summary>
    public TeamEntity? ConvertToRaid(int characterId, int raidCap)
    {
        var team = GetTeamOf(characterId);
        if (team == null || team.LeaderCharacterId != characterId)
            return null;
        if (team.Members.Count > raidCap) return null;
        team.IsRaid = true;
        team.RaidCap = raidCap;
        return team;
    }

    /// <summary>Annonce de recrutement — données de
    /// `ChangeRecruitDataV2` natif : appartient au leader d'équipe
    /// (<see cref="TeamId"/> renseigné) ou à un joueur solo
    /// (TeamId == 0). Champs F2-F5 = paramètres wire
    /// `[s16][s16][s16][u32]` dont la sémantique exacte n'est pas
    /// encore désassemblée (plage de niveau / carte cible) —
    /// conservés verbatim pour le broadcast et le filtrage.</summary>
    public sealed class RecruitPost
    {
        public int OwnerCharId { get; set; }
        public int TeamId { get; set; }
        public bool Enabled { get; set; }
        public short F2 { get; set; }
        public short F3 { get; set; }
        public short F4 { get; set; }
        public uint F5 { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>Annonces actives par charId du déposant —
    /// équivalent local de `CCharacter+0xd84` /
    /// `CForceTeam::ChangeRecruitTeamDataV2` (le natif les pousse à
    /// la mission ; notre index est zone-local tant que le bridge
    /// RecruitTeam n'existe pas).</summary>
    private readonly Dictionary<int, RecruitPost> _recruits = new();

    /// <summary>Publie/retire une annonce — `ChangeRecruitDataV2`
    /// natif : solo → annonce personnelle ; en équipe → leader
    /// uniquement (retour null sinon). `enabled == false` retire
    /// l'annonce existante.</summary>
    public RecruitPost? RegisterRecruit(int characterId, bool enabled,
        short f2, short f3, short f4, uint f5, string message)
    {
        var team = GetTeamOf(characterId);
        var teamId = 0;
        if (team != null)
        {
            if (team.LeaderCharacterId != characterId) return null;
            teamId = team.Id;
        }
        if (!enabled)
        {
            _recruits.Remove(characterId);
            return new RecruitPost { OwnerCharId = characterId };
        }
        var post = new RecruitPost
        {
            OwnerCharId = characterId,
            TeamId = teamId,
            Enabled = true,
            F2 = f2, F3 = f3, F4 = f4, F5 = f5,
            Message = message,
        };
        _recruits[characterId] = post;
        return post;
    }

    /// <summary>Annonces actives, filtrées par les critères de
    /// `SearchTeamV2` (s16 ×2 + mot-clé) : le mot-clé matche le
    /// message ; les deux shorts filtrent sur F2/F4 quand ils sont
    /// non nuls (sémantique de plage native non confirmée — le
    /// filtre le plus permissif est choisi pour ne pas masquer
    /// d'annonce).</summary>
    public IReadOnlyList<RecruitPost> SearchRecruits(short f1,
        short f2, string keyword)
    {
        var list = new List<RecruitPost>();
        foreach (var p in _recruits.Values)
        {
            if (!p.Enabled) continue;
            if (f1 != 0 && p.F2 != 0 && p.F2 != f1) continue;
            if (f2 != 0 && p.F4 != 0 && p.F4 != f2) continue;
            if (!string.IsNullOrEmpty(keyword) &&
                p.Message.IndexOf(keyword,
                    StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            list.Add(p);
        }
        return list;
    }

    /// <summary>Retire l'annonce d'un joueur (déconnexion, sortie
    /// d'équipe, changement de leader à gérer par l'appelant).</summary>
    public void ClearRecruit(int characterId)
    {
        _recruits.Remove(characterId);
        _positions.Remove(characterId);
    }

    /// <summary>Positions de recrutement par charId — équivalent de
    /// `CCharacter+0xd88` zone-side / `ExecutionerData+0x87e` mission
    /// (`Zone_TEC_RecruitUpdatePositionV2` @0x8252640 y écrit ; le
    /// natif pousse via la commande textex "RecruitUpdatePositionV2",
    /// notre index d'annonces étant zone-local la valeur vit ici).
    /// Défaut natif = 2 (init ExecutionerData).</summary>
    private readonly Dictionary<int, short> _positions = new();

    /// <summary>`CCharacter::ChangePositionV2` @0x840b190 : positions
    /// valides &lt;3 (rôle LFG tank/dps/heal), no-op si identique à
    /// l'état courant. Retourne true quand la valeur change.</summary>
    public bool SetRecruitPosition(int characterId, short position)
    {
        if ((ushort)position >= 3) return false;
        var cur = GetRecruitPosition(characterId);
        if (cur == position && _positions.ContainsKey(characterId))
            return false;
        _positions[characterId] = position;
        return true;
    }

    /// <summary>Position de recrutement — `ExecutionerData+0x87e`
    /// natif, affichée dans `SSearchTeamMemberDataV2+0x12`.</summary>
    public short GetRecruitPosition(int characterId) =>
        _positions.TryGetValue(characterId, out var p) ? p : (short)2;

    /// <summary>Règle d'exp d'équipe — `CForceTeam::SetExpRule`
    /// @0x846cdb0 : valeurs 1-2 → partage actif (+0x56 = 0) ;
    /// valeurs &gt; 2 → no-share (+0x56 = 1), seul le tueur reçoit
    /// `ExpForTeamNoShare`. Leader uniquement, comme les autres
    /// règles d'équipe. Retourne l'équipe modifiée ou null.</summary>
    public TeamEntity? SetExpRule(int characterId, short rule)
    {
        var team = GetTeamOf(characterId);
        if (team == null || team.LeaderCharacterId != characterId)
            return null;
        // Natif : (ushort)(rule-1) > 1 → seul >2 écrit 1 ; les
        // valeurs ≤0 ou non prévues ne modifient pas le flag.
        if ((ushort)(rule - 1) > 1)
        {
            if (rule > 2) team.ExpNoShare = true;
            return team;
        }
        team.ExpNoShare = false;
        return team;
    }

    /// <summary>Distribue un drop selon la règle de loot —
    /// transcription de `CItemFactory::GetTreasureLooter`
    /// @0x0808c1f0 + `CForceTeam::GetLooter` @0x0846dfe0 +
    /// `GetLootOrderMember` @0x0846deb0. Le natif attribue au
    /// RAMASSAGE d'un trésor au sol ; chez nous le drop part direct
    /// en sac au kill — la sémantique de distribution est identique :
    /// - règle 3 (Taunt-Exclusive) ou TeamType 3 (raid 25) → le tueur
    ///   garde, aucune redistribution (guard natif) ;
    /// - sinon les éligibles sont parcourus en rotation à partir de
    ///   <see cref="TeamEntity.LootCursor"/>+1 (membres locaux :
    ///   même node, connectés, vivants) ;
    /// - moins de 2 éligibles → le tueur garde (le natif renvoie le
    ///   cueilleur), curseur calé sur l'unique éligible s'il existe ;
    /// - règles 1-2 (Free/Random) → tirage UNIFORME
    ///   `random() % n` : chaque éligible reçoit exactement 1/n des
    ///   drops (2→50/50, 3→33%, 5→20%, 10→10%, 25→4%) — le natif
    ///   n'a PAS de table de paliers pondérés ;
    /// - autre valeur de règle → premier éligible après le curseur
    ///   (chemin séquentiel natif, équivalent round-robin) ;
    /// le curseur (+0x52) pointe ensuite sur le membre servi.
    /// <paramref name="isEligible"/> rejoue le filtre natif (session
    /// migrée sur le node, membre actif) ; null = tous éligibles.
    /// Retourne null si aucun membre n'est éligible.</summary>
    public int? PickLootReceiver(int teamId, int killerCharId = 0,
        Func<int, bool>? isEligible = null)
    {
        if (!_teams.TryGetValue(teamId, out var team) ||
            team.Members.Count == 0)
            return null;
        bool Eligible(int cid) => isEligible?.Invoke(cid) ?? true;
        // Gate GetTreasureLooter : règle 3 / TeamType 3 → tueur garde.
        if (team.LootRuleValue == 3 || team.TeamType == 3)
            return KillerOrEligible(team, killerCharId, Eligible);
        var order = LootOrderMembers(team, Eligible);
        if (order.Count < 2)
        {
            // Natif : moins de 2 éligibles → le cueilleur (= le tueur
            // chez nous) garde le drop, même s'il n'est pas dans la
            // liste ; le curseur se cale sur l'unique éligible.
            if (order.Count == 1) team.LootCursor = order[0].Index;
            if (killerCharId != 0) return killerCharId;
            return order.Count == 1 ? order[0].CharId : (int?)null;
        }
        var pos = (ushort)(team.LootRuleValue - 1) < 2
            ? Random.Shared.Next(order.Count)   // natif random()%n
            : 0;                                // 1er après le curseur
        team.LootCursor = order[pos].Index;
        return order[pos].CharId;
    }

    /// <summary>Le tueur s'il est recevable, sinon le premier membre
    /// éligible de l'équipe — repli hors-chemin-natif (le natif aurait
    /// toujours un cueilleur local en main).</summary>
    private static int? KillerOrEligible(TeamEntity team,
        int killerCharId, Func<int, bool> eligible)
    {
        if (killerCharId != 0 && eligible(killerCharId))
            return killerCharId;
        var m = team.Members.FirstOrDefault(
            x => eligible(x.CharacterId));
        return m?.CharacterId;
    }

    /// <summary>Éligibles dans l'ordre de rotation natif —
    /// `GetLootOrderMember` : index `(i + LootCursor) % taille` pour i
    /// de 1 à taille, donc le premier candidat suit le dernier servi.</summary>
    private static List<(int Index, int CharId)> LootOrderMembers(
        TeamEntity team, Func<int, bool> eligible)
    {
        var n = team.Members.Count;
        var list = new List<(int, int)>(n);
        for (var i = 1; i <= n; i++)
        {
            var idx = (i + team.LootCursor) % n;
            var m = team.Members[idx];
            if (eligible(m.CharacterId)) list.Add((idx, m.CharacterId));
        }
        return list;
    }

    public TeamEntity? GetTeam(int teamId)
        => _teams.TryGetValue(teamId, out var t) ? t : null;

    public TeamEntity? GetTeamOf(int characterId)
        => _teamByCharacter.TryGetValue(characterId, out var id) ? _teams[id] : null;

    public bool IsInTeam(int characterId) => _teamByCharacter.ContainsKey(characterId);

    /// <summary>Toutes les équipes actives (recherche LFG).</summary>
    public IReadOnlyCollection<TeamEntity> AllTeams => _teams.Values;
}
