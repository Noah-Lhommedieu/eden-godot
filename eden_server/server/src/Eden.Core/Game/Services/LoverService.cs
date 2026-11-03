using Eden.Core.Database.Repositories;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service couple/mariage — correspond a CLover + CWedding.
/// Systeme romantique: demande de couple, affection, mariage, teleportation
/// conjoint (CEC_GotoLover), divorce.
/// </summary>
public sealed class LoverService
{
    public sealed class Couple
    {
        public int Partner1Id { get; set; }
        public int Partner2Id { get; set; }
        public int Affection { get; set; }       // niveau d'affection
        public DateTime? WeddingDate { get; set; }
        public int DbId { get; set; }              // id player_couples (0 = non persisté)
        public bool IsMarried => WeddingDate.HasValue;
    }

    private readonly Dictionary<int, int> _partnerOf = new();
    private readonly Dictionary<int, Couple> _couples = new();
    private readonly HashSet<(int, int)> _pendingProposals = new();
    private SocialRepository? _repo;

    public void Attach(SocialRepository repo) => _repo = repo;

    /// <summary>Recharge le couple d'un joueur (à sa connexion).</summary>
    public async Task LoadForAsync(int characterId)
    {
        if (_repo == null) return;
        var row = await _repo.GetCoupleAsync(characterId);
        if (row == null) return;
        var couple = new Couple
        {
            Partner1Id = row.Partner1Id, Partner2Id = row.Partner2Id,
            Affection = row.Affection, WeddingDate = row.WeddingDate,
            DbId = row.Id,
        };
        _couples[row.Partner1Id] = couple;
        _partnerOf[row.Partner1Id] = row.Partner1Id;
        _partnerOf[row.Partner2Id] = row.Partner1Id;
    }

    /// <summary>Propose le couple a un autre joueur.</summary>
    public bool Propose(int fromId, int toId)
    {
        if (fromId == toId || _partnerOf.ContainsKey(fromId) || _partnerOf.ContainsKey(toId))
            return false;
        return _pendingProposals.Add((fromId, toId));
    }

    /// <summary>Accepte la proposition — le couple est forme.</summary>
    public async Task<Couple?> AcceptAsync(int characterId, int fromId)
    {
        if (!_pendingProposals.Remove((fromId, characterId))) return null;
        var couple = new Couple { Partner1Id = fromId, Partner2Id = characterId };
        if (_repo != null)
            couple.DbId = await _repo.CreateCoupleAsync(fromId,
                characterId);
        _couples[fromId] = couple;
        _partnerOf[fromId] = fromId;          // clé du couple = Partner1Id
        _partnerOf[characterId] = fromId;
        return couple;
    }

    public bool Decline(int characterId, int fromId)
        => _pendingProposals.Remove((fromId, characterId));

    /// <summary>
    /// Mariage — correspond a CWedding.
    /// </summary>
    public async Task<bool> MarryAsync(int characterId)
    {
        var couple = GetCouple(characterId);
        if (couple == null || couple.IsMarried) return false;
        couple.WeddingDate = DateTime.UtcNow;
        if (couple.DbId > 0 && _repo != null)
            await _repo.UpdateCoupleAsync(couple.DbId, couple.Affection,
                couple.WeddingDate);
        return true;
    }

    /// <summary>Divorce/rupture — dissout le couple.</summary>
    public async Task<bool> BreakupAsync(int characterId)
    {
        var couple = GetCouple(characterId);
        if (couple == null) return false;
        _couples.Remove(couple.Partner1Id);
        _partnerOf.Remove(couple.Partner1Id);
        _partnerOf.Remove(couple.Partner2Id);
        if (couple.DbId > 0 && _repo != null)
            await _repo.DeleteCoupleAsync(couple.DbId);
        return true;
    }

    /// <summary>Augmente l'affection (actions communes, quetes couple).</summary>
    public void AddAffection(int characterId, int amount)
    {
        var couple = GetCouple(characterId);
        if (couple != null) couple.Affection += amount;
    }

    /// <summary>
    /// Teleportation vers le conjoint — CEC_GotoLover.
    /// Retourne l'id du partenaire cible.
    /// </summary>
    public int? GetPartnerForTeleport(int characterId)
    {
        var couple = GetCouple(characterId);
        if (couple == null) return null;
        return couple.Partner1Id == characterId ? couple.Partner2Id : couple.Partner1Id;
    }

    public Couple? GetCouple(int characterId)
        => _partnerOf.TryGetValue(characterId, out var key)
            ? _couples.GetValueOrDefault(key)
            : null;

    public int? GetPartnerId(int characterId) => GetPartnerForTeleport(characterId);
}
