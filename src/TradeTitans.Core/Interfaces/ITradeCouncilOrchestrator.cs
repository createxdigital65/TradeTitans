using TradeTitans.Core.Domain.Entities;
using TradeTitans.Core.DTOs.Python;
using TradeTitans.Core.RiskRules;

namespace TradeTitans.Core.Interfaces;

public record FullCouncilRunResponse(
    TradeCouncilSession Session,
    CouncilRunResultDto CouncilRunResult,
    RiskGuardianAssessment RiskAssessment,
    ExecutionResult ExecutionResult,
    TradeProposal? Proposal
);

public interface ITradeCouncilOrchestrator
{
    /// <summary>
    /// Runs the Python council debate, sizes a trade proposal, lets Risk Guardian evaluate it and
    /// persists the session. NEVER executes a broker order — execution only happens after explicit
    /// human confirmation via <see cref="ConfirmAndExecuteAsync"/>.
    /// </summary>
    Task<FullCouncilRunResponse> RunFullCouncilDebateAsync(string symbol, double portfolioValue = 100000.0, bool useOptions = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Human-confirmed execution step. Re-checks Risk Guardian against the current account and only
    /// then lets Chief Trader submit the order to Alpaca Paper Trading.
    /// </summary>
    Task<FullCouncilRunResponse> ConfirmAndExecuteAsync(Guid sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a pending session. A cancelled session can never be executed.
    /// </summary>
    Task<TradeCouncilSession> CancelPendingSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);

    Task<List<TradeCouncilSession>> GetRecentSessionsAsync(int limit = 50, CancellationToken cancellationToken = default);
}
