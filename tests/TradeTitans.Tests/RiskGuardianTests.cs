using Microsoft.Extensions.Logging.Abstractions;
using TradeTitans.Core.DTOs.Alpaca;
using TradeTitans.Core.RiskRules;
using TradeTitans.Core.Services;
using Xunit;

namespace TradeTitans.Tests;

public class RiskGuardianTests
{
    [Fact]
    public async Task MaxPositionSize_Vetos_WhenEstimatedCostExceedsTenPercent()
    {
        var rule = new MaxPositionSizeRule();
        var proposal = Proposal("BUY", "EQUITY", cost: 30_000);
        var account = Account(portfolio: 100_000, cash: 100_000);

        var result = await rule.EvaluateAsync(proposal, account);

        Assert.False(result.Passed);
        Assert.Contains("VETO", result.Explanation);
        Assert.Equal("10.0% Max", result.Threshold);
    }

    [Fact]
    public async Task MaxPositionSize_Passes_WhenEstimatedCostWithinTenPercent()
    {
        var rule = new MaxPositionSizeRule();
        var proposal = Proposal("BUY", "EQUITY", cost: 5_000);
        var account = Account(portfolio: 100_000, cash: 100_000);

        var result = await rule.EvaluateAsync(proposal, account);

        Assert.True(result.Passed);
    }

    [Fact]
    public async Task MinimumCashReserve_Vetos_WhenCashBelowTwentyPercent()
    {
        var rule = new MinimumCashReserveRule();
        var proposal = Proposal("BUY", "EQUITY", cost: 5_000);
        var account = Account(portfolio: 100_000, cash: 15_000); // 15% < 20% reserve

        var result = await rule.EvaluateAsync(proposal, account);

        Assert.False(result.Passed);
        Assert.Contains("VETO", result.Explanation);
        Assert.Equal("20.0% Min", result.Threshold);
    }

    [Fact]
    public async Task OptionsDte_Vetos_WhenDaysToExpirationBelowSeven()
    {
        var rule = new OptionsDteLiquidityRule();
        var proposal = Proposal("BUY", "OPTION", cost: 860, dte: 3, contractSymbol: "AAPL260101C00300000");

        var result = await rule.EvaluateAsync(proposal, Account(100_000, 50_000));

        Assert.False(result.Passed);
        Assert.Contains("3 DTE", result.ActualValue);
    }

    [Fact]
    public async Task OptionsDte_SkipsCheck_ForEquityProposal()
    {
        var rule = new OptionsDteLiquidityRule();
        var proposal = Proposal("BUY", "EQUITY", cost: 5_000);

        var result = await rule.EvaluateAsync(proposal, Account(100_000, 50_000));

        Assert.True(result.Passed);
        Assert.Contains("N/A", result.ActualValue);
    }

    [Fact]
    public async Task OptionsLiquidity_Vetos_WhenOpenInterestBelowMinimum()
    {
        var rule = new OptionsDteLiquidityRule();
        var proposal = Proposal("BUY", "OPTION", cost: 860, dte: 30, contractSymbol: "AAPL260918C00225000", openInterest: 40, spread: 0.10, mid: 4.30);

        var result = await rule.EvaluateAsync(proposal, Account(100_000, 50_000));

        Assert.False(result.Passed);
        Assert.Contains("open interest", result.Explanation);
    }

    [Fact]
    public async Task OptionsLiquidity_Vetos_WhenSpreadExceedsLimit()
    {
        var rule = new OptionsDteLiquidityRule();
        var proposal = Proposal("BUY", "OPTION", cost: 860, dte: 30, contractSymbol: "AAPL260918C00225000", openInterest: 500, spread: 1.50, mid: 4.30);

        var result = await rule.EvaluateAsync(proposal, Account(100_000, 50_000));

        Assert.False(result.Passed);
        Assert.Contains("spread", result.Explanation);
    }

    [Fact]
    public async Task OptionsLiquidity_Vetos_WhenLiquidityDataMissing()
    {
        var rule = new OptionsDteLiquidityRule();
        // Option proposal without any chain liquidity data (OI/spread/mid = 0) must be blocked:
        // liquidity cannot be verified -> Risk Guardian veto.
        var proposal = Proposal("BUY", "OPTION", cost: 860, dte: 30, contractSymbol: "AAPL260918C00225000");

        var result = await rule.EvaluateAsync(proposal, Account(100_000, 50_000));

        Assert.False(result.Passed);
        Assert.Contains("liquidity data missing", result.Explanation);
    }

    [Fact]
    public async Task OptionsLiquidity_Passes_ForLiquidOption()
    {
        var rule = new OptionsDteLiquidityRule();
        var proposal = Proposal("BUY", "OPTION", cost: 860, dte: 30, contractSymbol: "AAPL260918C00225000", openInterest: 750, spread: 0.40, mid: 4.30);

        var result = await rule.EvaluateAsync(proposal, Account(100_000, 50_000));

        Assert.True(result.Passed);
        Assert.Contains("30 DTE", result.ActualValue);
        Assert.Contains("750", result.ActualValue);
    }

    [Fact]
    public async Task DataQuality_Vetos_WhenSnapshotStale()
    {
        var rule = new DataQualityRule();
        var proposal = Proposal("BUY", "EQUITY", cost: 5_000, dataQuality: "stale");

        var result = await rule.EvaluateAsync(proposal, Account(100_000, 50_000));

        Assert.False(result.Passed);
        Assert.Contains("stale", result.Explanation);
    }

    [Fact]
    public async Task RiskGuardianService_Approves_WhenAllRulesPass()
    {
        var guardian = BuildGuardian();
        var proposal = Proposal("BUY", "EQUITY", cost: 5_000);
        var account = Account(portfolio: 100_000, cash: 50_000);

        var assessment = await guardian.EvaluateProposalAsync(proposal, account);

        Assert.True(assessment.Approved);
        Assert.Equal(4, assessment.RuleResults.Count);
        Assert.Contains("PASSED", assessment.SummaryReason);
        Assert.All(assessment.RuleResults, r => Assert.True(r.Passed));
    }

    [Fact]
    public async Task RiskGuardianService_Vetos_WhenAnyRuleFails()
    {
        var guardian = BuildGuardian();
        var proposal = Proposal("BUY", "EQUITY", cost: 60_000); // 60% of 100k portfolio
        var account = Account(portfolio: 100_000, cash: 50_000);

        var assessment = await guardian.EvaluateProposalAsync(proposal, account);

        Assert.False(assessment.Approved);
        Assert.Contains("VETOED BY RISK GUARDIAN", assessment.SummaryReason);
    }

    private static RiskGuardianService BuildGuardian() =>
        new(
            new IRiskRule[]
            {
                new MaxPositionSizeRule(),
                new MinimumCashReserveRule(),
                new OptionsDteLiquidityRule(),
                new DataQualityRule()
            },
            NullLogger<RiskGuardianService>.Instance);

    private static TradeProposal Proposal(
        string action,
        string instrument,
        double cost,
        int dte = 0,
        string? contractSymbol = null,
        string dataQuality = "ok",
        double openInterest = 0,
        double spread = 0,
        double mid = 0) =>
        new("AAPL", action, instrument, Quantity: 0, EstimatedCost: cost, CurrentPrice: 224.5,
            OptionContractSymbol: contractSymbol, OptionDte: dte, DataQuality: dataQuality,
            OptionOpenInterest: openInterest, OptionSpread: spread, OptionMidPrice: mid);

    private static AlpacaAccountDto Account(double portfolio, double cash) =>
        new("1", "PA1", "ACTIVE", "USD", "0", cash.ToString("F2"), portfolio.ToString("F2"),
            portfolio.ToString("F2"), "0", "0", "0");
}