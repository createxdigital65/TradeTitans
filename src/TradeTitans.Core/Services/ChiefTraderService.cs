using Microsoft.Extensions.Logging;
using TradeTitans.Core.DTOs.Alpaca;
using TradeTitans.Core.DTOs.Python;
using TradeTitans.Core.Interfaces;
using TradeTitans.Core.RiskRules;

namespace TradeTitans.Core.Services;

public class ChiefTraderService : IChiefTraderService
{
    private readonly IAlpacaPaperService _alpacaService;
    private readonly ILogger<ChiefTraderService> _logger;

    // Target equity allocation: 5% of portfolio value — safely under Risk Guardian's 10% veto ceiling.
    private const double EquityTargetAllocationFraction = 0.05;

    public ChiefTraderService(IAlpacaPaperService alpacaService, ILogger<ChiefTraderService> logger)
    {
        _alpacaService = alpacaService;
        _logger = logger;
    }

    public Task<TradeProposal> PrepareProposalAsync(
        CouncilRunResultDto councilResult,
        AlpacaAccountDto account,
        CancellationToken cancellationToken = default)
    {
        var decision = councilResult.InstrumentDecision;
        var action = decision?.Action ?? councilResult.Challenger.Decision;
        var dataQuality = councilResult.Snapshot.DataQuality ?? "ok";

        // Non-actionable verdicts produce a no-trade proposal (Quantity/Cost = 0).
        if (action != "BUY" && action != "SELL")
        {
            return Task.FromResult(new TradeProposal(
                councilResult.Symbol,
                action,
                "EQUITY",
                Quantity: 0,
                EstimatedCost: 0,
                councilResult.Snapshot.Price,
                OptionContractSymbol: null,
                OptionDte: 0,
                dataQuality));
        }

        // Options: use the Options Strategist's size decision as-is, and carry the chosen
        // contract's chain liquidity data (open interest, bid/ask spread) into the proposal so the
        // Risk Guardian can enforce its BINDING liquidity veto before any execution.
        if (decision != null && decision.Instrument == "OPTION" && decision.OptionDetails != null)
        {
            var od = decision.OptionDetails;
            var contracts = Math.Max(1, od.Contracts);
            var chainContract = FindChainContract(councilResult.OptionChain, od.ContractSymbol);
            var spread = chainContract != null ? Math.Max(0, chainContract.Ask - chainContract.Bid) : 0;
            var mid = chainContract != null ? (chainContract.Ask + chainContract.Bid) / 2.0 : 0;

            return Task.FromResult(new TradeProposal(
                councilResult.Symbol,
                action,
                "OPTION",
                Quantity: contracts,
                EstimatedCost: od.EstimatedTotalPremium,
                councilResult.Snapshot.Price,
                od.ContractSymbol,
                od.DaysToExpiration,
                dataQuality,
                OptionOpenInterest: chainContract?.OpenInterest ?? 0,
                OptionSpread: spread,
                OptionMidPrice: mid));
        }

        // Equity: size from the CURRENT account (5% target), never a static share count.
        double.TryParse(account.PortfolioValue, out var portfolioValue);
        if (portfolioValue <= 0) portfolioValue = 100000.0;

        var currentPrice = councilResult.Snapshot.Price > 0 ? councilResult.Snapshot.Price : 100.0;
        var targetBudget = portfolioValue * EquityTargetAllocationFraction;
        var quantity = Math.Max(1, Math.Floor(targetBudget / currentPrice));
        var estimatedCost = quantity * currentPrice;

        return Task.FromResult(new TradeProposal(
            councilResult.Symbol,
            action,
            "EQUITY",
            Quantity: quantity,
            EstimatedCost: estimatedCost,
            currentPrice,
            OptionContractSymbol: null,
            OptionDte: 0,
            dataQuality));
    }

    public async Task<ExecutionResult> AuthorizeAndExecuteAsync(
        CouncilRunResultDto councilResult,
        TradeProposal proposal,
        RiskGuardianAssessment riskAssessment,
        CancellationToken cancellationToken = default)
    {
        if (!riskAssessment.Approved)
        {
            _logger.LogWarning("Chief Trader execution BLOCKED by Risk Guardian for {Symbol}: {Reason}",
                councilResult.Symbol, riskAssessment.SummaryReason);
            return new ExecutionResult(false, null,
                $"Execution blocked by Risk Guardian: {riskAssessment.SummaryReason}");
        }

        if (proposal.Action != "BUY" && proposal.Action != "SELL")
        {
            _logger.LogInformation("Chief Trader standing down for {Symbol} — Council verdict is {Action}",
                councilResult.Symbol, proposal.Action);
            return new ExecutionResult(false, null,
                $"No trade executed. Council verdict is {proposal.Action}.");
        }

        string orderSymbol = proposal.Instrument == "OPTION" && !string.IsNullOrEmpty(proposal.OptionContractSymbol)
            ? proposal.OptionContractSymbol
            : proposal.Symbol;

        var orderRequest = new AlpacaOrderRequestDto(
            Symbol: orderSymbol,
            Qty: proposal.Quantity,
            Notional: null,
            Side: proposal.Action.ToLowerInvariant(),
            Type: "market",
            TimeInForce: "day",
            LimitPrice: null);

        _logger.LogInformation("Chief Trader authorizing order: {Side} {Qty} {Symbol} (instrument {Instrument}, est. ${Cost:N2})",
            orderRequest.Side, orderRequest.Qty, orderRequest.Symbol, proposal.Instrument, proposal.EstimatedCost);

        var orderResponse = await _alpacaService.SubmitOrderAsync(orderRequest, cancellationToken);

        if (orderResponse != null)
        {
            return new ExecutionResult(true, orderResponse,
                $"Successfully submitted paper order {orderResponse.Id} for {proposal.Quantity} x {orderSymbol}.");
        }

        return new ExecutionResult(false, null,
            $"Failed to submit paper order to Alpaca for {orderSymbol}.");
    }

    private static OptionContractDto? FindChainContract(OptionChainSnapshotDto? chain, string contractSymbol)
    {
        if (chain == null || string.IsNullOrEmpty(contractSymbol))
        {
            return null;
        }

        return (chain.Calls ?? new List<OptionContractDto>())
            .Concat(chain.Puts ?? new List<OptionContractDto>())
            .FirstOrDefault(c => string.Equals(c.Symbol, contractSymbol, StringComparison.OrdinalIgnoreCase));
    }
}
