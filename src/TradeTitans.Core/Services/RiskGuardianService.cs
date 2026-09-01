using Microsoft.Extensions.Logging;
using TradeTitans.Core.DTOs.Alpaca;
using TradeTitans.Core.Interfaces;
using TradeTitans.Core.RiskRules;

namespace TradeTitans.Core.Services;

public class RiskGuardianService : IRiskGuardianService
{
    private readonly IEnumerable<IRiskRule> _rules;
    private readonly ILogger<RiskGuardianService> _logger;

    public RiskGuardianService(IEnumerable<IRiskRule> rules, ILogger<RiskGuardianService> logger)
    {
        _rules = rules;
        _logger = logger;
    }

    public async Task<RiskGuardianAssessment> EvaluateProposalAsync(TradeProposal proposal, AlpacaAccountDto accountInfo)
    {
        _logger.LogInformation(
            "Risk Guardian evaluating {Instrument} {Action} for {Symbol}: estimated cost {Cost:C}",
            proposal.Instrument, proposal.Action, proposal.Symbol, proposal.EstimatedCost);

        var results = new List<RiskRuleResult>();
        bool isApproved = true;
        var failureReasons = new List<string>();

        foreach (var rule in _rules)
        {
            var res = await rule.EvaluateAsync(proposal, accountInfo);
            results.Add(res);

            if (!res.Passed)
            {
                isApproved = false;
                failureReasons.Add($"[{rule.RuleName}] {res.Explanation}");
            }
        }

        string summary = isApproved
            ? "ALL deterministic risk checks PASSED. Trade approved for Chief Trader execution."
            : $"VETOED BY RISK GUARDIAN: {string.Join(" | ", failureReasons)}";

        _logger.LogInformation("Risk Guardian verdict for {Symbol}: Approved = {Approved}. {Summary}", proposal.Symbol, isApproved, summary);

        return new RiskGuardianAssessment(isApproved, results, summary);
    }
}
