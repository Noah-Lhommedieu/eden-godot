using System.Collections.Concurrent;
using Eden.Core.Networking;
using Eden.Core.Networking.Session;

namespace Eden.Zone;

/// <summary>
/// Registre des sessions actives de la zone — équivalent C# du
/// <c>CPlayerFactory</c>/session map de lapis.
/// Permet lookup par CharacterId/AccountId et broadcast aux voisins.
/// </summary>
public sealed class ZoneSessionManager
{
    private readonly ConcurrentDictionary<Guid, NetSession> _sessions = new();
    private readonly ConcurrentDictionary<int, NetSession> _byCharacter = new();

    public int Count => _sessions.Count;

    /// <summary>Toutes les sessions connectées (snapshot énumérable).</summary>
    public IEnumerable<NetSession> All => _sessions.Values;

    public void Add(NetSession session) => _sessions[session.SessionId] = session;

    public void Remove(NetSession session)
    {
        _sessions.TryRemove(session.SessionId, out _);
        if (session.CharacterId is { } cid)
            _byCharacter.TryRemove(cid, out _);
    }

    /// <summary>Associe un personnage authentifié à sa session.</summary>
    public void BindCharacter(NetSession session, int characterId)
    {
        session.CharacterId = characterId;
        _byCharacter[characterId] = session;
    }

    public NetSession? FindByCharacter(int characterId) =>
        _byCharacter.TryGetValue(characterId, out var s) ? s : null;

    /// <summary>Broadcast d'une commande à toutes les sessions authentifiées.</summary>
    /// <summary>Envoie un message au joueur donné s'il est connecté.</summary>
    public async Task SendToAsync(int characterId, NetCommand command,
        CancellationToken token = default)
    {
        if (FindByCharacter(characterId) is { } target)
            await target.SendAsync(command, token);
    }

    public async Task BroadcastAllAsync(NetCommand command, CancellationToken token = default)
    {
        foreach (var s in _sessions.Values)
        {
            if (!s.IsAuthenticated) continue;
            try { await s.SendAsync(command, token); }
            catch { /* session morte — nettoyée par la boucle de lecture */ }
        }
    }

    /// <summary>Broadcast à toutes les sessions sauf <paramref name="exclude"/>.</summary>
    public async Task BroadcastExceptAsync(NetSession exclude, NetCommand command,
        CancellationToken token = default)
    {
        foreach (var s in _sessions.Values)
        {
            if (s == exclude || !s.IsAuthenticated) continue;
            try { await s.SendAsync(command, token); }
            catch { }
        }
    }
}
