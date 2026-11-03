using Eden.Core.Game.Trade;

namespace Eden.Core.Database.Repositories;

public interface ITradeRepository
{
    Task<TradeEntity?> GetActiveTradeAsync(int initiatorId, int partnerId);
    /// <summary>Trade actif impliquant ce joueur (initiateur ou partenaire).</summary>
    Task<TradeEntity?> GetActiveTradeByPlayerAsync(int playerId);
    Task CreateTradeAsync(TradeEntity trade);
    Task<bool> UpdateTradeAsync(TradeEntity trade);
    Task<bool> DeleteTradeAsync(int tradeId);
}
