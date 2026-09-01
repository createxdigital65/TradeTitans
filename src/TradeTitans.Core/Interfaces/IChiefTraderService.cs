using TradeTitans.Core.DTOs.Alpaca;
using TradeTitans.Core.DTOs.Python;
using TradeTitans.Core.RiskRules;

namespace TradeTitans.Core.Interfaces;

public record ExecutionResult(
    bool Executed,
    AlpacaOrderResponseDto? OrderResponse,
    string Message
);

public interface IChiefTraderService
{
    /// <summary>
    /// Converts a council verdict into a concretely sized TradeProposal (safe quantity / notional)
    /// that Risk Guardian then evaluates. No order is created here.
    /// </summary>
    Task<TradeProposal> PrepareProposalAsync(CouncilRunResultDto councilResult, AlpacaAccountDto account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an APPROVED proposal through the Alpaca paper adapter.
    /// Returns a blocked result immediately if Risk Guardian did not approve.
    /// </summary>
    Task<ExecutionResult> AuthorizeAndExecuteAsync(CouncilRunResultDto councilResult, TradeProposal proposal, RiskGuardianAssessment riskAssessment, CancellationToken cancellationToken = default);
}
