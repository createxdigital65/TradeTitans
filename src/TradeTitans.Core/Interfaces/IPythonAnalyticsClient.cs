using TradeTitans.Core.DTOs.Python;

namespace TradeTitans.Core.Interfaces;

public interface IPythonAnalyticsClient
{
    Task<bool> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<MarketSnapshotDto?> GetMarketSnapshotAsync(string symbol, CancellationToken cancellationToken = default);
    Task<OptionChainSnapshotDto?> GetMarketOptionsAsync(string symbol, CancellationToken cancellationToken = default);
    Task<CouncilRunResultDto?> RunCouncilAsync(string symbol, double portfolioValue = 100000.0, bool useOptions = true, CancellationToken cancellationToken = default);
}
