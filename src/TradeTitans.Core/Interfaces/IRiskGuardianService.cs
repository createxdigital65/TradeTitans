using TradeTitans.Core.DTOs.Alpaca;
using TradeTitans.Core.RiskRules;

namespace TradeTitans.Core.Interfaces;

public record RiskGuardianAssessment(
    bool Approved,
    List<RiskRuleResult> RuleResults,
    string SummaryReason
);

public interface IRiskGuardianService
{
    Task<RiskGuardianAssessment> EvaluateProposalAsync(TradeProposal proposal, AlpacaAccountDto accountInfo);
}
