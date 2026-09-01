using TradeTitans.Core.DTOs.Alpaca;

namespace TradeTitans.Core.RiskRules;

/// <summary>
/// A concrete trade proposal produced by Chief Trader position sizing and consumed by the
/// deterministic Risk Guardian. Every risk rule evaluates a TradeProposal against the current
/// Alpaca account so that thresholds, actuals, PASS and VETO are fully auditable.
/// </summary>
public record TradeProposal(
    string Symbol,
    string Action, // "BUY" | "SELL" | "HOLD" | "NO_TRADE"
    string Instrument, // "EQUITY" | "OPTION"
    double Quantity,
    double EstimatedCost,
    double CurrentPrice,
    string? OptionContractSymbol,
    int OptionDte, // 0 for equity
    string DataQuality,
    double OptionOpenInterest = 0, // open interest for the chosen contract from the Python option chain (options only)
    double OptionSpread = 0,       // bid/ask spread in $ for the chosen contract from the Python option chain (options only)
    double OptionMidPrice = 0      // (bid + ask) / 2 for the chosen contract from the Python option chain (options only)
);

public record RiskRuleResult(
    string RuleName,
    bool Passed,
    string ActualValue,
    string Threshold,
    string Explanation
);

public interface IRiskRule
{
    string RuleName { get; }
    string Threshold { get; }
    string Description { get; }
    Task<RiskRuleResult> EvaluateAsync(TradeProposal proposal, AlpacaAccountDto accountInfo);
}

public class MaxPositionSizeRule : IRiskRule
{
    private const double MaxPositionFraction = 0.10; // 10% max portfolio allocation

    public string RuleName => "Maximum Position Size";
    public string Threshold => "10.0% Max";
    public string Description => "Proposed trade cost must not exceed 10% of the paper portfolio value.";

    public Task<RiskRuleResult> EvaluateAsync(TradeProposal proposal, AlpacaAccountDto accountInfo)
    {
        double.TryParse(accountInfo.PortfolioValue, out var portfolioValue);
        if (portfolioValue <= 0) portfolioValue = 100000.0;

        var maxAllowed = portfolioValue * MaxPositionFraction;
        var positionPct = portfolioValue > 0 ? (proposal.EstimatedCost / portfolioValue) * 100 : 0;

        bool passed = proposal.EstimatedCost <= maxAllowed;
        string explanation = passed
            ? $"Proposed trade cost (${proposal.EstimatedCost:N2}, {positionPct:F1}%) is within the 10% portfolio position limit (${maxAllowed:N2})."
            : $"VETO: Proposed trade cost (${proposal.EstimatedCost:N2}, {positionPct:F1}%) exceeds maximum allowable 10% position limit (${maxAllowed:N2}).";

        return Task.FromResult(new RiskRuleResult(
            RuleName,
            passed,
            $"{positionPct:F1}% (${proposal.EstimatedCost:N2})",
            Threshold,
            explanation
        ));
    }
}

public class MinimumCashReserveRule : IRiskRule
{
    private const double MinCashFraction = 0.20; // 20% minimum cash reserve

    public string RuleName => "Minimum Cash Reserve";
    public string Threshold => "20.0% Min";
    public string Description => "Account must retain at least 20% cash so concentrated risk stays bounded.";

    public Task<RiskRuleResult> EvaluateAsync(TradeProposal proposal, AlpacaAccountDto accountInfo)
    {
        double.TryParse(accountInfo.Cash, out var cash);
        double.TryParse(accountInfo.PortfolioValue, out var portfolioValue);
        if (portfolioValue <= 0) portfolioValue = 100000.0;

        var minCashRequired = portfolioValue * MinCashFraction;
        var cashPct = portfolioValue > 0 ? (cash / portfolioValue) * 100 : 0;

        bool passed = cash >= minCashRequired;
        string explanation = passed
            ? $"Account cash reserve (${cash:N2}, {cashPct:F1}%) satisfies the 20% minimum cash requirement (${minCashRequired:N2})."
            : $"VETO: Cash balance (${cash:N2}, {cashPct:F1}%) is below required 20% cash reserve (${minCashRequired:N2}).";

        return Task.FromResult(new RiskRuleResult(
            RuleName,
            passed,
            $"{cashPct:F1}% (${cash:N2})",
            Threshold,
            explanation
        ));
    }
}

public class OptionsDteLiquidityRule : IRiskRule
{
    private const int MinDte = 7;                        // minimum days to expiration
    private const int MinOpenInterest = 100;             // minimum open interest (liquidity)
    private const double MaxSpreadFractionOfMid = 0.25;  // maximum bid/ask spread: 25% of mid

    public string RuleName => "Options Expiration & Liquidity Safety";
    public string Threshold => "7 DTE Min | OI 100 Min | Spread <= 25% Mid";
    public string Description => "Options proposals must expire at least 7 days out, be liquid (open interest) and carry a tight bid/ask spread. The Python service's options checks are a first pass; this rule is the binding veto.";

    public Task<RiskRuleResult> EvaluateAsync(TradeProposal proposal, AlpacaAccountDto accountInfo)
    {
        if (proposal.Instrument != "OPTION" || string.IsNullOrEmpty(proposal.OptionContractSymbol))
        {
            return Task.FromResult(new RiskRuleResult(
                RuleName,
                true,
                "N/A (Equity)",
                Threshold,
                "PASS: Equity trade — Options DTE/liquidity checks skipped."
            ));
        }

        var actualValue = $"{proposal.OptionDte} DTE | OI {proposal.OptionOpenInterest} | spread ${proposal.OptionSpread:F2}";

        if (proposal.OptionDte < MinDte)
        {
            return Task.FromResult(new RiskRuleResult(
                RuleName,
                false,
                actualValue,
                Threshold,
                $"VETO: Option contract {proposal.OptionContractSymbol} has only {proposal.OptionDte} DTE (minimum {MinDte} DTE required)."
            ));
        }

        if (proposal.OptionOpenInterest < MinOpenInterest)
        {
            return Task.FromResult(new RiskRuleResult(
                RuleName,
                false,
                actualValue,
                Threshold,
                $"VETO: Option contract {proposal.OptionContractSymbol} has open interest {proposal.OptionOpenInterest} (minimum {MinOpenInterest} required)" +
                (proposal.OptionOpenInterest <= 0
                    ? "; liquidity data missing or the contract is dead — liquidity cannot be verified, so the trade is blocked."
                    : ".")
            ));
        }

        if (proposal.OptionMidPrice <= 0 || proposal.OptionSpread > MaxSpreadFractionOfMid * proposal.OptionMidPrice)
        {
            var spreadPct = proposal.OptionMidPrice > 0
                ? (proposal.OptionSpread / proposal.OptionMidPrice * 100).ToString("F1") + "%"
                : "unknown";
            return Task.FromResult(new RiskRuleResult(
                RuleName,
                false,
                actualValue,
                Threshold,
                $"VETO: Option contract {proposal.OptionContractSymbol} bid/ask spread ${proposal.OptionSpread:F2} ({spreadPct} of mid) exceeds the maximum allowed {MaxSpreadFractionOfMid:P0} of mid."
            ));
        }

        var passSpreadPct = (proposal.OptionSpread / proposal.OptionMidPrice * 100).ToString("F1");
        return Task.FromResult(new RiskRuleResult(
            RuleName,
            true,
            actualValue,
            Threshold,
            $"PASS: Option contract {proposal.OptionContractSymbol} meets expiration safety ({proposal.OptionDte} DTE >= {MinDte}), liquidity (OI {proposal.OptionOpenInterest} >= {MinOpenInterest}) and spread (${proposal.OptionSpread:F2}, {passSpreadPct}% of mid <= {MaxSpreadFractionOfMid:P0})."
        ));
    }
}

public class DataQualityRule : IRiskRule
{
    public string RuleName => "Market Data Quality Safeguard";
    public string Threshold => "OK Required";
    public string Description => "Trading halts when the market snapshot is stale or flagged non-OK.";

    public Task<RiskRuleResult> EvaluateAsync(TradeProposal proposal, AlpacaAccountDto accountInfo)
    {
        var quality = string.IsNullOrWhiteSpace(proposal.DataQuality)
            ? "unknown"
            : proposal.DataQuality.ToLowerInvariant();

        bool passed = quality == "ok";
        string explanation = passed
            ? "Market snapshot data quality verified as OK."
            : $"VETO: Market data quality marked as '{quality}'. Trading halted due to stale/missing data.";

        return Task.FromResult(new RiskRuleResult(
            RuleName,
            passed,
            quality.ToUpperInvariant(),
            Threshold,
            explanation
        ));
    }
}
