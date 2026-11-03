using Eden.Core.Game.Auction;

namespace Eden.Core.Database.Repositories;

public interface IAuctionRepository
{
    Task<int> CreateAuctionAsync(AuctionEntity auction);
    Task<AuctionEntity?> GetAuctionAsync(int auctionId);
    Task<IReadOnlyList<AuctionEntity>> GetActiveAuctionsAsync();
    Task<bool> UpdateAuctionAsync(AuctionEntity auction);
    Task<bool> DeleteAuctionAsync(int auctionId);
}
