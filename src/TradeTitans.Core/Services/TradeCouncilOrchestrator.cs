using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradeTitans.Core.Data;
using TradeTitans.Core.Domain.Entities;
using TradeTitans.Core.Domain.Enums;
using TradeTitans.Core.DTOs.Alpaca;
using TradeTitans.Core.DTOs.Python;
using TradeTitans.Core.Interfaces;
using TradeTitans.Core.RiskRules;

namespace TradeTitans.Core.Services;

public class TradeCouncilOrchestrator : ITradeCouncilOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IPythonAnalyticsClient _pythonClient;
    private readonly IAlpacaPaperService _alpacaService;
    private readonly IRiskGuardianService _riskGuardian;
    private readonly IChiefTraderService _chiefTrader;
    private readonly TradeTitansDbContext _dbContext;
    private readonly ILogger<TradeCouncilOrchestrator> _logger;

    public TradeCouncilOrchestrator(
        IPythonAnalyticsClient pythonClient,
        IAlpacaPaperService alpacaService,
        IRiskGuardianService riskGuardian,
        IChiefTraderService chiefTrader,
        TradeTitansDbContext dbContext,
        ILogger<TradeCouncilOrchestrator> logger)
    {
        _pythonClient = pythonClient;
        _alpacaService = alpacaService;
        _riskGuardian = riskGuardian;
        _chiefTrader = chiefTrader;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<FullCouncilRunResponse> RunFullCouncilDebateAsync(string symbol, double portfolioValue = 100000.0, bool useOptions = true, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Trade Council Orchestration for symbol {Symbol}", symbol);

        // 1. Run the Python council debate.
        var councilResult = await _pythonClient.RunCouncilAsync(symbol, portfolioValue, useOptions, cancellationToken);
        if (councilResult == null)
        {
            throw new InvalidOperationException($"Failed to receive response from Python Council service for {symbol}");
        }

        // 1b. Normalize synthesis confidence: when the Python challenger agent's LLM call fails it
        //     reports confidence=0. Derive the council consensus from the agents that responded so
        //     the UI displays the confidence the council actually calculated (see method docs).
        councilResult = DeriveSynthesisConfidence(councilResult);

        // 2. Load account context (mock-safe).
        var account = await GetAccountAsync(cancellationToken);

        // 3. Chief Trader sizes a concrete, auditable proposal. No order is created here.
        var proposal = await _chiefTrader.PrepareProposalAsync(councilResult, account, cancellationToken);

        // 4. Risk Guardian evaluates the proposal (deterministic, binding veto).
        var riskAssessment = await _riskGuardian.EvaluateProposalAsync(proposal, account);

        // 5. Persist the session. Human confirmation is REQUIRED before any paper order.
        var session = BuildSession(councilResult, account, proposal, riskAssessment);

        _dbContext.TradeCouncilSessions.Add(session);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Council session {SessionId} persisted for {Symbol}. Status={Status}, RiskApproved={Approved}",
            session.Id, session.Symbol, session.SessionStatus, riskAssessment.Approved);

        return new FullCouncilRunResponse(
            session,
            councilResult,
            riskAssessment,
            new ExecutionResult(false, null, "Awaiting human confirmation before paper execution."),
            proposal);
    }

    public async Task<FullCouncilRunResponse> ConfirmAndExecuteAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.TradeCouncilSessions
            .Include(s => s.AgentProposals)
            .Include(s => s.RiskLogs)
            .Include(s => s.ExecutedOrder)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Council session '{sessionId}' not found.");

        // Structural guard: only a human-confirmable, Risk-APPROVED session may execute.
        if (session.SessionStatus != CouncilSessionStatus.PENDING_CONFIRMATION)
        {
            throw new InvalidOperationException($"Session '{sessionId}' is in state {session.SessionStatus} and cannot be executed.");
        }
        if (session.RiskGuardianStatus != RiskStatus.APPROVED)
        {
            throw new InvalidOperationException("Execution blocked: Risk Guardian did not approve this proposal.");
        }
        if (string.IsNullOrEmpty(session.CouncilResultJson))
        {
            throw new InvalidOperationException("Session has no stored council payload; execution is not possible.");
        }

        var councilResult = JsonSerializer.Deserialize<CouncilRunResultDto>(session.CouncilResultJson, JsonOptions);
        if (councilResult == null)
        {
            throw new InvalidOperationException("Stored council payload could not be deserialized.");
        }

        // Secondary deterministic re-check against the CURRENT account before any order is submitted.
        var account = await GetAccountAsync(cancellationToken);
        var proposal = await _chiefTrader.PrepareProposalAsync(councilResult, account, cancellationToken);
        var reAssessment = await _riskGuardian.EvaluateProposalAsync(proposal, account);

        // Append the confirm-time risk evaluation to the audit trail. The initial run logs
        // are preserved so the full decision history stays visible in the Command Center.
        foreach (var rule in reAssessment.RuleResults)
        {
            var riskLog = new RiskCheckLog
            {
                RuleName = rule.RuleName,
                Threshold = rule.Threshold,
                ActualValue = rule.ActualValue,
                Passed = rule.Passed,
                Details = rule.Explanation,
                Timestamp = DateTime.UtcNow
            };
            session.RiskLogs.Add(riskLog);
            // Force Added: fresh client-keyed entities appended to a tracked session can otherwise
            // be mis-tracked as Modified, producing an UPDATE that affects 0 rows.
            _dbContext.Entry(riskLog).State = EntityState.Added;
        }
        session.RiskGuardianStatus = reAssessment.Approved ? RiskStatus.APPROVED : RiskStatus.VETOED_BY_RISK_GUARDIAN;
        session.RiskGuardianSummary = reAssessment.SummaryReason;

        if (!reAssessment.Approved)
        {
            session.SessionStatus = CouncilSessionStatus.VETOED_BY_RISK_GUARDIAN;
            session.VetoReason = reAssessment.SummaryReason;
            session.ExecutionFailureReason = "Risk Guardian veto at confirmation time. No order was submitted.";
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Confirm-time Risk Guardian VETO for session {SessionId}. NO order submitted to broker.", session.Id);

            return new FullCouncilRunResponse(
                session,
                councilResult,
                reAssessment,
                new ExecutionResult(false, null, "Execution blocked by Risk Guardian at confirmation."),
                proposal);
        }

        // Risk approved at confirm time -> Chief Trader submits through Alpaca Paper Trading only.
        var execution = await _chiefTrader.AuthorizeAndExecuteAsync(councilResult, proposal, reAssessment, cancellationToken);

        if (execution.Executed && execution.OrderResponse != null)
        {
            double.TryParse(execution.OrderResponse.Qty, out var orderQty);
            double.TryParse(execution.OrderResponse.FilledAvgPrice, out var orderPrice);

            var executedOrder = new ExecutedOrder
            {
                Symbol = execution.OrderResponse.Symbol,
                Instrument = proposal.Instrument,
                Side = execution.OrderResponse.Side,
                Quantity = orderQty > 0 ? orderQty : proposal.Quantity,
                Price = orderPrice > 0 ? orderPrice : proposal.CurrentPrice,
                BrokerOrderId = execution.OrderResponse.Id,
                Status = execution.OrderResponse.Status,
                ExecutedAt = DateTime.UtcNow
            };
            session.ExecutedOrder = executedOrder;
            // Force Added: this brand-new dependent row must be INSERTed, never UPDATE d.
            _dbContext.Entry(executedOrder).State = EntityState.Added;

            session.ChiefTraderExecuted = true;
            session.BrokerOrderId = execution.OrderResponse.Id;
            session.SessionStatus = CouncilSessionStatus.EXECUTED;
        }
        else
        {
            session.SessionStatus = CouncilSessionStatus.FAILED;
            session.ExecutionFailureReason = execution.Message;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Session {SessionId} final status: {Status} (brokerOrderId: {OrderId})",
            session.Id, session.SessionStatus, session.BrokerOrderId ?? "n/a");

        return new FullCouncilRunResponse(session, councilResult, reAssessment, execution, proposal);
    }

    private async Task<AlpacaAccountDto> GetAccountAsync(CancellationToken cancellationToken)
    {
        var account = await _alpacaService.GetAccountAsync(cancellationToken);
        if (account == null)
        {
            // Mock-safe fallback identical to AlpacaPaperService mock mode.
            return new AlpacaAccountDto("mock-account-123", "PA3MOCK12345", "ACTIVE", "USD",
                "400000.00", "100000.00", "100000.00", "100000.00", "0.00", "0.00", "0.00");
        }
        return account;
    }

    /// <summary>
    /// The Python challenger (synthesis) agent runs last in the LLM chain; when its LLM call fails,
    /// the Python service returns is_fallback=true with confidence=0, which the UI would render as a
    /// meaningless "Confidence: 0%".
    ///
    /// Contract rules enforced here:
    /// 1. A confidence Python actually reported — fallback or not — is REAL output and is NEVER
    ///    overwritten. Derivation applies ONLY to the exact case is_fallback=true AND confidence=0.
    /// 2. The derived value is CALCULATED from the actual votes of the agents that responded —
    ///    never a hardcoded number — and is marked ConfidenceSource="derived_consensus" so the UI
    ///    can clearly distinguish derived from real confidence.
    /// </summary>
    private static CouncilRunResultDto DeriveSynthesisConfidence(CouncilRunResultDto councilResult)
    {
        var challenger = councilResult.Challenger;
        if (!challenger.IsFallback || challenger.Confidence != 0)
        {
            return councilResult; // Real or Python-reported confidence — never overwrite.
        }

        var respondedConfidences = new[] { councilResult.Bull, councilResult.Bear, councilResult.Hype }
            .Where(a => !a.IsFallback && a.Confidence > 0)
            .Select(a => (double)a.Confidence)
            .ToList();

        if (respondedConfidences.Count == 0)
        {
            return councilResult; // No agent responded with a confidence — keep the honest 0.
        }

        var consensus = (int)Math.Round(respondedConfidences.Average());
        return councilResult with
        {
            Challenger = challenger with { Confidence = consensus, ConfidenceSource = "derived_consensus" }
        };
    }

    private TradeCouncilSession BuildSession(
        CouncilRunResultDto councilResult,
        AlpacaAccountDto account,
        TradeProposal proposal,
        RiskGuardianAssessment riskAssessment)
    {
        bool riskApproved = riskAssessment.Approved;
        bool actionable = proposal.Action == "BUY" || proposal.Action == "SELL";

        var session = new TradeCouncilSession
        {
            Symbol = councilResult.Symbol,
            Timestamp = DateTime.UtcNow,
            MarketPrice = councilResult.Snapshot.Price,
            VolumeRatio = councilResult.Snapshot.VolumeRatio,
            Volatility20d = councilResult.Snapshot.Volatility20d,
            ChallengerDecision = councilResult.Challenger.Decision,
            ChallengerConfidence = councilResult.Challenger.Confidence,
            ChallengerThesis = councilResult.Challenger.Thesis,
            ProposedInstrument = proposal.Instrument == "OPTION" ? InstrumentType.OPTION : InstrumentType.EQUITY,
            ProposedAction = proposal.Action,
            ProposedQuantity = proposal.Quantity,
            EstimatedCost = proposal.EstimatedCost,
            OptionContractSymbol = proposal.OptionContractSymbol,
            RiskGuardianStatus = riskApproved ? RiskStatus.APPROVED : RiskStatus.VETOED_BY_RISK_GUARDIAN,
            RiskGuardianSummary = riskAssessment.SummaryReason,
            VetoReason = riskApproved ? null : riskAssessment.SummaryReason,
            SessionStatus = !riskApproved
                ? CouncilSessionStatus.VETOED_BY_RISK_GUARDIAN
                : actionable
                    ? CouncilSessionStatus.PENDING_CONFIRMATION
                    : CouncilSessionStatus.NO_TRADE,
            CouncilResultJson = JsonSerializer.Serialize(councilResult, JsonOptions)
        };

        // Preserve every agent's individual reasoning for the Command Center UI.
        var agents = new[] { councilResult.Bull, councilResult.Bear, councilResult.Hype, councilResult.Challenger };
        foreach (var agent in agents)
        {
            session.AgentProposals.Add(new AgentProposal
            {
                AgentName = agent.Agent,
                Decision = agent.Decision,
                Confidence = agent.Confidence,
                Thesis = agent.Thesis,
                EvidenceJson = JsonSerializer.Serialize(agent.Evidence ?? new List<string>()),
                RisksJson = JsonSerializer.Serialize(agent.Risks ?? new List<string>()),
                IsFallback = agent.IsFallback
            });
        }

        // Persist every risk rule with its threshold, actual value and PASS/VETO verdict.
        foreach (var rule in riskAssessment.RuleResults)
        {
            session.RiskLogs.Add(new RiskCheckLog
            {
                RuleName = rule.RuleName,
                Threshold = rule.Threshold,
                ActualValue = rule.ActualValue,
                Passed = rule.Passed,
                Details = rule.Explanation,
                Timestamp = DateTime.UtcNow
            });
        }

        return session;
    }

    public async Task<TradeCouncilSession> CancelPendingSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.TradeCouncilSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException($"Council session '{sessionId}' not found.");

        if (session.SessionStatus != CouncilSessionStatus.PENDING_CONFIRMATION)
        {
            throw new InvalidOperationException($"Session '{sessionId}' is in state {session.SessionStatus} and cannot be cancelled.");
        }

        session.SessionStatus = CouncilSessionStatus.CANCELED;
        session.ExecutionFailureReason = "Cancelled by operator before confirmation. No order was submitted.";
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Council session {SessionId} cancelled by operator.", session.Id);

        return session;
    }

    public async Task<List<TradeCouncilSession>> GetRecentSessionsAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TradeCouncilSessions
            .Include(s => s.AgentProposals)
            .Include(s => s.RiskLogs)
            .Include(s => s.ExecutedOrder)
            .OrderByDescending(s => s.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
