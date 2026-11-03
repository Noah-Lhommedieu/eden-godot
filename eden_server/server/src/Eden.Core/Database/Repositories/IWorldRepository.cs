using Eden.Core.Database.Entities;
using Eden.Core.Networking.Messages.Login;

namespace Eden.Core.Database.Repositories;

/// <summary>
/// Interface d'accès aux serveurs de monde et états de population.
/// </summary>
public interface IWorldRepository
{
    Task<IReadOnlyList<WorldServerEntity>> GetAllWorldsAsync();
    Task<WorldServerEntity?> GetWorldByIdAsync(int worldId);
    Task<List<WorldServerEntry>> GetWorldStatesForAccountAsync(int accountId);
    Task UpdateWorldPopulationAsync(int worldId, int onlineUsers);
}
