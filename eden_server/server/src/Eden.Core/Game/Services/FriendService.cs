using Eden.Core.Database.Repositories;

namespace Eden.Core.Game.Services;

/// <summary>
/// Service d'amis — correspond au <c>CFriend</c> natif porté par
/// l'executioner (mission). Modèle <c>friends.friend_type</c> :
/// <b>0</b> = blacklist, <b>1</b> = j'ai ajouté l'autre,
/// <b>2</b> = l'autre m'a ajouté (invitation pendante), <b>3</b> =
/// mutuel (<c>TC_AddFriend</c> @0x82386f0).
/// Chaque direction est une ligne — comme le natif, les mises à jour
/// sont persistées immédiatement (ajout offline-safe) puis poussées
/// via 52/61 aux deux parties connectées.
/// </summary>
public sealed class FriendService
{
    /// <summary>charId → (friendId → friend_type).</summary>
    private readonly Dictionary<int, Dictionary<int, int>> _relations = new();
    private SocialRepository? _repo;

    public void Attach(SocialRepository repo) => _repo = repo;

    /// <summary>Charge les relations du perso depuis <c>friends</c>
    /// (appelé au login du joueur côté monde).</summary>
    public async Task LoadForAsync(int characterId)
    {
        if (_repo == null) return;
        var rows = await _repo.GetFriendRelationsAsync(characterId);
        _relations[characterId] =
            rows.ToDictionary(r => r.FriendId, r => r.FriendType);
    }

    /// <summary>friend_type charId→friendId, −1 si pas de relation —
    /// signature de <c>CFriend::IsMember</c> (retourne le type ou −1).</summary>
    public int GetRelationType(int charId, int friendId)
        => _relations.TryGetValue(charId, out var m)
            && m.TryGetValue(friendId, out var t) ? t : -1;

    /// <summary>Lookup DB quand la cible n'est pas en mémoire —
    /// <c>CExecutionerDBO::GetFriendType</c> natif.</summary>
    public async Task<int> GetRelationTypeDbAsync(int charId, int friendId)
        => _repo == null ? GetRelationType(charId, friendId)
            : (await _repo.GetFriendTypeAsync(charId, friendId)) ?? -1;

    /// <summary>Ids avec friend_type 1/2/3 (liste amis + invitations —
    /// le CFriend natif contient tous les membres non-blacklist).</summary>
    public IReadOnlyCollection<int> GetFriends(int charId)
        => _relations.TryGetValue(charId, out var m)
            ? m.Where(kv => kv.Value is >= 1).Select(kv => kv.Key).ToList()
            : (IReadOnlyCollection<int>)Array.Empty<int>();

    /// <summary>Ids blacklistés (friend_type 0).</summary>
    public IReadOnlyCollection<int> GetBlocked(int charId)
        => _relations.TryGetValue(charId, out var m)
            ? m.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList()
            : (IReadOnlyCollection<int>)Array.Empty<int>();

    public bool AreFriends(int a, int b) => GetRelationType(a, b) == 3;
    public bool IsBlocked(int charId, int targetId)
        => GetRelationType(charId, targetId) == 0;

    /// <summary>Écrit une direction (mémoire + <c>friends</c>) —
    /// <c>CFriend::AddMember</c> + <c>AddFriendData</c>/<c>UpdateFriendData</c>.</summary>
    public async Task SetRelationAsync(int charId, int friendId, int type)
    {
        if (charId == friendId) return;
        (_relations.TryGetValue(charId, out var m)
            ? m : _relations[charId] = new Dictionary<int, int>())[friendId] = type;
        if (_repo != null)
            await _repo.SetFriendTypeAsync(charId, friendId, type);
    }

    /// <summary>Efface une direction — <c>CFriend::RemoveMember</c>
    /// + <c>DeleteFriendData</c> natifs.</summary>
    public async Task RemoveRelationAsync(int charId, int friendId)
    {
        if (_relations.TryGetValue(charId, out var m))
            m.Remove(friendId);
        if (_repo != null)
            await _repo.DeleteFriendAsync(charId, friendId);
    }

    /// <summary>Recherche dans le panneau communaute — par niveau,
    /// classe, nom. Correspond a CCharacter search community panel.</summary>
    public IReadOnlyList<(int CharacterId, string Name, int Level, int ClassId)> Search(
        IEnumerable<(int Id, string Name, int Level, int ClassId)> players,
        string? nameFilter = null, int? minLevel = null, int? maxLevel = null,
        int? classId = null)
        => players.Where(p =>
                (nameFilter == null || p.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)) &&
                (minLevel == null || p.Level >= minLevel) &&
                (maxLevel == null || p.Level <= maxLevel) &&
                (classId == null || p.ClassId == classId))
            .Select(p => (p.Id, p.Name, p.Level, p.ClassId))
            .ToList();
}
