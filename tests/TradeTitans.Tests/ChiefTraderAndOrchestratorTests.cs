using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TradeTitans.Core.Data;
using TradeTitans.Core.Domain.Enums;
using TradeTitans.Core.DTOs.Alpaca;
using TradeTitans.Core.DTOs.Python;
using TradeTitans.Core.Interfaces;
using TradeTitans.Core.RiskRules;
using TradeTitans.Core.Services;
using Xunit;

namespace TradeTitans.Tests;

public class ChiefTraderAndOrchestratorTests
{
    [Fact]
    public async Task PrepareProposal_Equity_SizesFromPortfolioNotStaticQuantity()
    {
        var mockAlpaca = new MockAlpacaService();
        var chief = new ChiefTraderService(mockAlpaca, NullLogger<ChiefTraderService>.Instance);
        var council = TestData.CouncilResult("AAPL", "BUY", "EQUITY", price: 224.5);
        var account = Account(portfolio: 100_000, cash: 100_000);

        var proposal = await chief.PrepareProposalAsync(council, account);

        // 5% of $100k = $5,000 / $224.5 = 22.27 -> 22 shares
        Assert.Equal("BUY", proposal.Action);
        Assert.Equal("EQUITY", proposal.Instrument);
        Assert.Equal(22, proposal.Quantity);
        Assert.True(proposal.EstimatedCost > 0);
        Assert.True(proposal.EstimatedCost < 5_000);
    }

    [Fact]
    public async Task PrepareProposal_NoTrade_Verdict_IsZeroSized()
    {
        var chief = new ChiefTraderService(new MockAlpacaService(), NullLogger<ChiefTraderService>.Instance);
        var council = TestData.CouncilResult("AAPL", "HOLD", "EQUITY", price: 224.5);

        var proposal = await chief.PrepareProposalAsync(council, Account(100_000, 100_000));

        Assert.Equal("HOLD", proposal.Action);
        Assert.Equal(0, proposal.Quantity);
        Assert.Equal(0, proposal.EstimatedCost);
    }

    [Fact]
    public async Task PrepareProposal_Option_CarriesChainLiquidityIntoProposal()
    {
        var chief = new ChiefTraderService(new MockAlpacaService(), NullLogger<ChiefTraderService>.Instance);
        var council = TestData.CouncilResultWithOptionChain("AAPL");

        var proposal = await chief.PrepareProposalAsync(council, Account(100_000, 100_000));

        // The Options Strategist's decision must reach the proposal together with the chosen
        // contract's chain liquidity data so the Risk Guardian can enforce its binding veto.
        Assert.Equal("OPTION", proposal.Instrument);
        Assert.Equal("BUY", proposal.Action);
        Assert.Equal(2, proposal.Quantity);
        Assert.Equal(860, proposal.EstimatedCost);
        Assert.Equal("AAPL260918C00225000", proposal.OptionContractSymbol);
        Assert.Equal(17, proposal.OptionDte);
        Assert.Equal(750, proposal.OptionOpenInterest);
        Assert.Equal(0.4, proposal.OptionSpread, 5);   // ask 4.50 - bid 4.10
        Assert.Equal(4.3, proposal.OptionMidPrice, 5); // (4.10 + 4.50) / 2
    }

    [Fact]
    public async Task ChiefTrader_BlocksExecution_WhenRiskGuardianVetos()
    {
        var mockAlpaca = new MockAlpacaService();
        var chief = new ChiefTraderService(mockAlpaca, NullLogger<ChiefTraderService>.Instance);
        var council = TestData.CouncilResult("AAPL", "BUY", "EQUITY");
        var veto = new RiskGuardianAssessment(false, new List<RiskRuleResult>
        {
            new("Maximum Position Size", false, "25%", "10.0% Max", "Exceeds max position size.")
        }, "VETOED BY RISK GUARDIAN: Maximum Position Size exceeded.");

        var result = await chief.AuthorizeAndExecuteAsync(council, TestData.Proposal("BUY"), veto);

        Assert.False(result.Executed);
        Assert.Null(result.OrderResponse);
        Assert.Contains("blocked by Risk Guardian", result.Message);
        Assert.Equal(0, mockAlpaca.SubmittedOrdersCount);
    }

    [Fact]
    public async Task ChiefTrader_ExecutesApprovedProposal_ViaAlpacaPaperAdapter()
    {
        var mockAlpaca = new MockAlpacaService();
        var chief = new ChiefTraderService(mockAlpaca, NullLogger<ChiefTraderService>.Instance);
        var council = TestData.CouncilResult("AAPL", "BUY", "EQUITY");
        var approved = new RiskGuardianAssessment(true, new List<RiskRuleResult>
        {
            new("Maximum Position Size", true, "5%", "10.0% Max", "PASS")
        }, "ALL deterministic risk checks PASSED.");

        var result = await chief.AuthorizeAndExecuteAsync(council, TestData.Proposal("BUY"), approved);

        Assert.True(result.Executed);
        Assert.NotNull(result.OrderResponse);
        Assert.Equal(1, mockAlpaca.SubmittedOrdersCount);
    }

    [Fact]
    public async Task Orchestrator_Run_PersistsWithoutExecuting_BeforeHumanConfirmation()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var orchestrator = BuildOrchestrator(db, mockAlpaca);

        var response = await orchestrator.RunFullCouncilDebateAsync("AAPL");

        Assert.Equal(CouncilSessionStatus.PENDING_CONFIRMATION, response.Session.SessionStatus);
        Assert.Equal(RiskStatus.APPROVED, response.Session.RiskGuardianStatus);
        Assert.False(response.Session.ChiefTraderExecuted);
        Assert.Null(response.Session.ExecutedOrder);
        Assert.Equal(0, mockAlpaca.SubmittedOrdersCount);
        Assert.False(response.ExecutionResult.Executed);
        Assert.NotNull(response.Proposal);
        Assert.Equal(4, response.Session.RiskLogs.Count);
    }

    [Fact]
    public async Task Orchestrator_Confirm_Executes_AfterHumanConfirmation()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var orchestrator = BuildOrchestrator(db, mockAlpaca);

        var run = await orchestrator.RunFullCouncilDebateAsync("AAPL");
        var confirmed = await orchestrator.ConfirmAndExecuteAsync(run.Session.Id);

        Assert.Equal(CouncilSessionStatus.EXECUTED, confirmed.Session.SessionStatus);
        Assert.True(confirmed.Session.ChiefTraderExecuted);
        Assert.NotNull(confirmed.Session.BrokerOrderId);
        Assert.NotNull(confirmed.Session.ExecutedOrder);
        Assert.Equal(1, mockAlpaca.SubmittedOrdersCount);
        Assert.True(confirmed.ExecutionResult.Executed);
    }

    [Fact]
    public async Task Orchestrator_Confirm_RevalidatesRisk_AndCanVetoAtConfirmationTime()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var orchestrator = BuildOrchestrator(db, mockAlpaca);

        var run = await orchestrator.RunFullCouncilDebateAsync("AAPL");
        Assert.Equal(CouncilSessionStatus.PENDING_CONFIRMATION, run.Session.SessionStatus);

        // Account degrades before the human confirms -> deterministic risk re-check must veto.
        mockAlpaca.Account = Account(portfolio: 100_000, cash: 5_000); // 5% cash < 20% reserve

        var confirmed = await orchestrator.ConfirmAndExecuteAsync(run.Session.Id);

        Assert.Equal(CouncilSessionStatus.VETOED_BY_RISK_GUARDIAN, confirmed.Session.SessionStatus);
        Assert.Equal(RiskStatus.VETOED_BY_RISK_GUARDIAN, confirmed.Session.RiskGuardianStatus);
        Assert.False(confirmed.Session.ChiefTraderExecuted);
        Assert.Null(confirmed.Session.ExecutedOrder);
        Assert.Null(confirmed.Session.BrokerOrderId);
        Assert.Equal(0, mockAlpaca.SubmittedOrdersCount);
        Assert.False(confirmed.ExecutionResult.Executed);
    }

    [Fact]
    public async Task Orchestrator_Cancel_PreventsLaterConfirmationExecution()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var orchestrator = BuildOrchestrator(db, mockAlpaca);

        var run = await orchestrator.RunFullCouncilDebateAsync("AAPL");

        var cancelled = await orchestrator.CancelPendingSessionAsync(run.Session.Id);
        Assert.Equal(CouncilSessionStatus.CANCELED, cancelled.SessionStatus);

        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ConfirmAndExecuteAsync(run.Session.Id));
        Assert.Equal(0, mockAlpaca.SubmittedOrdersCount);
    }

    [Fact]
    public async Task Orchestrator_Confirm_Twice_SecondAttemptIsRejectedWithoutDuplicateOrder()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var orchestrator = BuildOrchestrator(db, mockAlpaca);

        var run = await orchestrator.RunFullCouncilDebateAsync("AAPL");
        var first = await orchestrator.ConfirmAndExecuteAsync(run.Session.Id);
        Assert.Equal(CouncilSessionStatus.EXECUTED, first.Session.SessionStatus);

        // A second confirmation attempt must be rejected — no duplicate order may reach the broker.
        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.ConfirmAndExecuteAsync(run.Session.Id));
        Assert.Equal(1, mockAlpaca.SubmittedOrdersCount);
    }

    [Fact]
    public async Task Orchestrator_ChallengerFallback_DerivesCouncilConsensusConfidence()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var pythonClient = new FakePythonClient
        {
            // Challenger LLM unavailable -> Python service returns is_fallback=true, confidence=0.
            Result = TestData.CouncilResultWithFallbackChallenger("AAPL", bullConfidence: 84, bearConfidence: 60, hypeConfidence: 78)
        };
        var orchestrator = BuildOrchestrator(db, mockAlpaca, pythonClient);

        var response = await orchestrator.RunFullCouncilDebateAsync("AAPL");

        // Consensus derived from the agents that actually responded: (84 + 60 + 78) / 3 = 74.
        Assert.Equal(74, response.Session.ChallengerConfidence);
        Assert.Equal(74, response.CouncilRunResult.Challenger.Confidence);
        var challengerProposal = response.Session.AgentProposals.Single(a => a.AgentName == "challenger");
        Assert.Equal(74, challengerProposal.Confidence);
        Assert.True(challengerProposal.IsFallback); // stays honestly labeled as fallback/derived
    }

    [Fact]
    public async Task Orchestrator_RealChallengerConfidence_IsNeverOverwritten()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var orchestrator = BuildOrchestrator(db, mockAlpaca); // default fake: challenger confidence 81, not fallback

        var response = await orchestrator.RunFullCouncilDebateAsync("AAPL");

        Assert.Equal(81, response.Session.ChallengerConfidence);
        Assert.Equal(81, response.CouncilRunResult.Challenger.Confidence);
    }

    [Fact]
    public async Task Orchestrator_FallbackChallengerWithReportedConfidence_IsNeverOverwritten()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var pythonClient = new FakePythonClient
        {
            // Python fallback that still reported a confidence: .NET must keep the real value
            // and must NOT mark it as derived.
            Result = TestData.CouncilResultWithFallbackChallenger("AAPL", bullConfidence: 84, bearConfidence: 60, hypeConfidence: 78, challengerReportedConfidence: 45)
        };
        var orchestrator = BuildOrchestrator(db, mockAlpaca, pythonClient);

        var response = await orchestrator.RunFullCouncilDebateAsync("AAPL");

        Assert.Equal(45, response.Session.ChallengerConfidence);
        Assert.Equal(45, response.CouncilRunResult.Challenger.Confidence);
        Assert.Null(response.CouncilRunResult.Challenger.ConfidenceSource);
    }

    [Fact]
    public async Task Orchestrator_OptionProposal_LiquidChain_IsPendingConfirmationWithChainData()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var pythonClient = new FakePythonClient { Result = TestData.CouncilResultWithOptionChain("AAPL") };
        var orchestrator = BuildOrchestrator(db, mockAlpaca, pythonClient);

        var response = await orchestrator.RunFullCouncilDebateAsync("AAPL");

        // Phase 1: option proposal passes all binding risk checks and waits for human confirmation.
        Assert.Equal(CouncilSessionStatus.PENDING_CONFIRMATION, response.Session.SessionStatus);
        Assert.Equal(InstrumentType.OPTION, response.Session.ProposedInstrument);
        Assert.NotNull(response.Proposal);
        Assert.Equal(750, response.Proposal!.OptionOpenInterest);
        Assert.Equal(0.4, response.Proposal.OptionSpread, 5);
        Assert.Equal(0, mockAlpaca.SubmittedOrdersCount); // preview only — no order before confirmation
    }

    [Fact]
    public async Task Orchestrator_OptionProposal_IlliquidChain_IsVetoedAtRunTime()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var baseResult = TestData.CouncilResultWithOptionChain("AAPL");
        var illiquid = baseResult with
        {
            OptionChain = baseResult.OptionChain! with
            {
                Calls = new List<OptionContractDto> { baseResult.OptionChain.Calls![0] with { OpenInterest = 40 } }
            }
        };
        var pythonClient = new FakePythonClient { Result = illiquid };
        var orchestrator = BuildOrchestrator(db, mockAlpaca, pythonClient);

        var response = await orchestrator.RunFullCouncilDebateAsync("AAPL");

        // The .NET Risk Guardian is the binding veto: an illiquid Python option proposal is
        // vetoed at run time and can never be executed.
        Assert.Equal(CouncilSessionStatus.VETOED_BY_RISK_GUARDIAN, response.Session.SessionStatus);
        Assert.Contains(response.Session.RiskLogs, r => !r.Passed);
        Assert.Equal(0, mockAlpaca.SubmittedOrdersCount);
    }


    [Fact]
    public async Task Orchestrator_RunVeto_PersistsVetoAndNeverTouchesAlpaca()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var orchestrator = BuildOrchestrator(db, mockAlpaca);

        // Cash reserve rule fails immediately: 5% cash < 20% minimum.
        mockAlpaca.Account = Account(portfolio: 100_000, cash: 5_000);

        var response = await orchestrator.RunFullCouncilDebateAsync("AAPL");

        Assert.Equal(CouncilSessionStatus.VETOED_BY_RISK_GUARDIAN, response.Session.SessionStatus);
        Assert.Equal(RiskStatus.VETOED_BY_RISK_GUARDIAN, response.Session.RiskGuardianStatus);
        Assert.NotNull(response.Session.VetoReason);
        Assert.False(response.Session.ChiefTraderExecuted);
        Assert.Null(response.Session.ExecutedOrder);
        Assert.Null(response.Session.BrokerOrderId);
        Assert.Equal(0, mockAlpaca.SubmittedOrdersCount);
        Assert.Contains(response.Session.RiskLogs, r => !r.Passed);
        Assert.False(response.ExecutionResult.Executed);
    }

    [Fact]
    public async Task Orchestrator_Confirm_PersistsCompleteAuditTrail()
    {
        var db = CreateDb();
        var mockAlpaca = new MockAlpacaService();
        var orchestrator = BuildOrchestrator(db, mockAlpaca);

        var run = await orchestrator.RunFullCouncilDebateAsync("AAPL");
        var confirmed = await orchestrator.ConfirmAndExecuteAsync(run.Session.Id);
        var session = confirmed.Session;

        Assert.Equal(4, session.AgentProposals.Count);                 // bull, bear, hype, challenger
        Assert.Equal(8, session.RiskLogs.Count);                       // 4 run + 4 confirm-time
        Assert.Equal(4, session.RiskLogs.Count(r => r.Phase == "INITIAL_EVALUATION"));     // phase 1 evaluation
        Assert.Equal(4, session.RiskLogs.Count(r => r.Phase == "CONFIRMATION_RECHECK"));   // phase 2 re-check
        Assert.False(string.IsNullOrEmpty(session.CouncilResultJson)); // full debate payload
        Assert.All(session.RiskLogs, r => Assert.False(string.IsNullOrEmpty(r.Threshold)));
        Assert.All(session.RiskLogs, r => Assert.False(string.IsNullOrEmpty(r.ActualValue)));
        Assert.NotNull(session.ExecutedOrder);
        Assert.NotNull(session.BrokerOrderId);
        Assert.True(session.ChiefTraderExecuted);
        Assert.Equal(CouncilSessionStatus.EXECUTED, session.SessionStatus);
    }

    private static TradeCouncilOrchestrator BuildOrchestrator(TradeTitansDbContext db, MockAlpacaService mockAlpaca, FakePythonClient? pythonClient = null)
    {
        var riskGuardian = new RiskGuardianService(new IRiskRule[]
        {
            new MaxPositionSizeRule(),
            new MinimumCashReserveRule(),
            new OptionsDteLiquidityRule(),
            new DataQualityRule()
        }, NullLogger<RiskGuardianService>.Instance);

        var chiefTrader = new ChiefTraderService(mockAlpaca, NullLogger<ChiefTraderService>.Instance);

        return new TradeCouncilOrchestrator(
            pythonClient ?? new FakePythonClient(),
            mockAlpaca,
            riskGuardian,
            chiefTrader,
            db,
            NullLogger<TradeCouncilOrchestrator>.Instance);
    }

    private static TradeTitansDbContext CreateDb()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TradeTitansDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new TradeTitansDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AlpacaAccountDto Account(double portfolio, double cash) =>
        new("1", "PA1", "ACTIVE", "USD", "0", cash.ToString("F2"), portfolio.ToString("F2"),
            portfolio.ToString("F2"), "0", "0", "0");

    private class FakePythonClient : IPythonAnalyticsClient
    {
        public CouncilRunResultDto Result { get; set; } = TestData.CouncilResult("AAPL", "BUY", "EQUITY", 224.5);

        public Task<bool> GetHealthAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<MarketSnapshotDto?> GetMarketSnapshotAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult<MarketSnapshotDto?>(TestData.CouncilResult(symbol, "BUY", "EQUITY", 224.5).Snapshot);

        public Task<OptionChainSnapshotDto?> GetMarketOptionsAsync(string symbol, CancellationToken cancellationToken = default) =>
            Task.FromResult<OptionChainSnapshotDto?>(null);

        public Task<CouncilRunResultDto?> RunCouncilAsync(string symbol, double portfolioValue = 100000.0, bool useOptions = true, CancellationToken cancellationToken = default) =>
            Task.FromResult<CouncilRunResultDto?>(Result);
    }

    private class MockAlpacaService : IAlpacaPaperService
    {
        public AlpacaAccountDto Account { get; set; } =
            new("1", "PA1", "ACTIVE", "USD", "0", "100000.00", "100000.00", "100000.00", "0", "0", "0");

        public int SubmittedOrdersCount { get; private set; }

        public Task<AlpacaAccountDto?> GetAccountAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<AlpacaAccountDto?>(Account);

        public Task<List<AlpacaPositionDto>> GetPositionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<AlpacaPositionDto>());

        public Task<AlpacaOrderResponseDto?> SubmitOrderAsync(AlpacaOrderRequestDto orderRequest, CancellationToken cancellationToken = default)
        {
            SubmittedOrdersCount++;
            return Task.FromResult<AlpacaOrderResponseDto?>(new AlpacaOrderResponseDto(
                Id: Guid.NewGuid().ToString(),
                ClientOrderId: Guid.NewGuid().ToString(),
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: DateTime.UtcNow,
                SubmittedAt: DateTime.UtcNow,
                FilledAt: DateTime.UtcNow,
                Symbol: orderRequest.Symbol,
                Qty: orderRequest.Qty?.ToString(),
                FilledQty: orderRequest.Qty?.ToString() ?? "1",
                Type: orderRequest.Type,
                Side: orderRequest.Side,
                TimeInForce: orderRequest.TimeInForce,
                LimitPrice: null,
                Status: "filled",
                FilledAvgPrice: "224.50"));
        }
    }
}

internal static class TestData
{
    public static TradeProposal Proposal(string action) =>
        new("AAPL", action, "EQUITY", Quantity: 22, EstimatedCost: 4_939, CurrentPrice: 224.5, null, 0, "ok");

    /// <summary>
    /// Simulates the live Python service behaviour when an agent's LLM call fails: the agent is
    /// returned with is_fallback=true and confidence=0. Used to verify the .NET synthesis-confidence
    /// derivation (confidence must be calculated from real council output, never hardcoded).
    /// </summary>
    public static CouncilRunResultDto CouncilResultWithFallbackChallenger(
        string symbol, int bullConfidence, int bearConfidence, int hypeConfidence, int challengerReportedConfidence = 0)
    {
        var result = CouncilResult(symbol, "BUY", "EQUITY");
        return result with
        {
            Bull = result.Bull with { Confidence = bullConfidence, IsFallback = false },
            Bear = result.Bear with { Confidence = bearConfidence, IsFallback = false },
            Hype = result.Hype with { Confidence = hypeConfidence, IsFallback = false },
            Challenger = result.Challenger with { Confidence = challengerReportedConfidence, IsFallback = true }
        };
    }

    /// <summary>
    /// Full Options Strategist output: an OPTION instrument decision plus the option chain that
    /// contains the chosen contract (bid 4.10 / ask 4.50 / OI 750 / 17 DTE), mirroring the live
    /// Python /council/run response shape.
    /// </summary>
    public static CouncilRunResultDto CouncilResultWithOptionChain(string symbol)
    {
        var contractSymbol = symbol + "260918C00225000";
        var snapshot = new MarketSnapshotDto(symbol, DateTime.UtcNow, 224.5, 222.0, 1.1, 10_000_000, 10_000_000,
            1.0, 0.18, new List<IndicatorDto>(), new List<string>(), "ok");
        var chain = new OptionChainSnapshotDto(symbol, DateTime.UtcNow,
            new List<OptionContractDto>
            {
                new(contractSymbol, symbol, "CALL", 225.0, "2026-09-18", 4.10, 4.50, 4.30, 750, 0.32, 17)
            },
            new List<OptionContractDto>(), "ok");
        var decision = new InstrumentDecisionDto(symbol, "OPTION", "BUY", "Long call via Options Strategist",
            new OptionLegDetailsDto("CALL", contractSymbol, 225.0, "2026-09-18", 2, 4.30, 860.0, 860.0, 229.30, 17), null);

        return new CouncilRunResultDto(symbol, snapshot, chain,
            new AgentOutputDto("bull", "BUY", 84, "Bull thesis", new List<string>(), new List<string>(), new List<string>(), new List<string>(), false),
            new AgentOutputDto("bear", "HOLD", 61, "Bear thesis", new List<string>(), new List<string>(), new List<string>(), new List<string>(), false),
            new AgentOutputDto("hype", "BUY", 78, "Hype thesis", new List<string>(), new List<string>(), new List<string>(), new List<string>(), false),
            new AgentOutputDto("challenger", "BUY", 81, "Challenger thesis", new List<string>(), new List<string>(), new List<string>(), new List<string>(), false),
            decision);
    }

    public static CouncilRunResultDto CouncilResult(string symbol, string action, string instrument, double price = 224.5)
    {
        var snapshot = new MarketSnapshotDto(
            symbol, DateTime.UtcNow, price, price * 0.99, 1.5, 10_000_000, 10_000_000,
            1.0, 0.18, new List<IndicatorDto>(), new List<string>(), "ok");

        InstrumentDecisionDto? decision = instrument == "OPTION"
            ? new InstrumentDecisionDto(symbol, "OPTION", action, "Long call", new OptionLegDetailsDto(
                "CALL", symbol + "260918C00225000", 225.0, "2026-09-18", 2, 4.30, 860.0, 860.0, 229.30, 17), null)
            : new InstrumentDecisionDto(symbol, "EQUITY", action, "Equity sized by Chief Trader", null, null);

        return new CouncilRunResultDto(
            symbol, snapshot, null,
            new AgentOutputDto("bull", "BUY", 84, "Bull thesis", new List<string>(), new List<string>(), new List<string>(), new List<string>(), false),
            new AgentOutputDto("bear", "HOLD", 61, "Bear thesis", new List<string>(), new List<string>(), new List<string>(), new List<string>(), false),
            new AgentOutputDto("hype", "BUY", 78, "Hype thesis", new List<string>(), new List<string>(), new List<string>(), new List<string>(), false),
            new AgentOutputDto("challenger", action, 81, "Challenger thesis", new List<string>(), new List<string>(), new List<string>(), new List<string>(), false),
            decision);
    }
}