using System.Text.Json.Serialization;
using TradeTitans.Core.Domain.Enums;

namespace TradeTitans.Core.Domain.Entities;

public class TradeCouncilSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Symbol { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // Lifecycle: PENDING_CONFIRMATION -> EXECUTED | VETOED_BY_RISK_GUARDIAN | NO_TRADE | CANCELED | FAILED
    public CouncilSessionStatus SessionStatus { get; set; } = CouncilSessionStatus.PENDING_CONFIRMATION;

    // Market context
    public double MarketPrice { get; set; }
    public double VolumeRatio { get; set; }
    public double Volatility20d { get; set; }

    // Challenger verdict
    public string ChallengerDecision { get; set; } = "NO_TRADE";
    public int ChallengerConfidence { get; set; }
    public string ChallengerThesis { get; set; } = string.Empty;

    // Proposed trade (built by Chief Trader sizing, evaluated by Risk Guardian)
    public InstrumentType ProposedInstrument { get; set; } = InstrumentType.EQUITY;
    public string ProposedAction { get; set; } = "NO_TRADE";
    public double ProposedQuantity { get; set; }
    public double EstimatedCost { get; set; }
    public string? OptionContractSymbol { get; set; }

    // Risk Guardian outcome
    public RiskStatus RiskGuardianStatus { get; set; } = RiskStatus.APPROVED;
    public string? RiskGuardianSummary { get; set; }
    public string? VetoReason { get; set; }

    // Execution outcome
    public bool ChiefTraderExecuted { get; set; }
    public string? BrokerOrderId { get; set; }
    public string? ExecutionFailureReason { get; set; }

    // Full Python council payload for history replay and confirm-time re-checks
    public string? CouncilResultJson { get; set; }

    // Navigation properties
    public List<AgentProposal> AgentProposals { get; set; } = new();
    public List<RiskCheckLog> RiskLogs { get; set; } = new();
    public ExecutedOrder? ExecutedOrder { get; set; }
}

public class AgentProposal
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TradeCouncilSessionId { get; set; }
    public string AgentName { get; set; } = string.Empty; // "bull", "bear", "hype", "challenger", "options_strategist"
    public string Decision { get; set; } = string.Empty; // "BUY", "SELL", "HOLD", "NO_TRADE"
    public int Confidence { get; set; }
    public string Thesis { get; set; } = string.Empty;
    public string EvidenceJson { get; set; } = "[]";
    public string RisksJson { get; set; } = "[]";
    public bool IsFallback { get; set; }

    [JsonIgnore]
    public TradeCouncilSession Session { get; set; } = null!;
}

public class RiskCheckLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TradeCouncilSessionId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public string Threshold { get; set; } = string.Empty;
    public string ActualValue { get; set; } = string.Empty;
    public bool Passed { get; set; }
    public string Details { get; set; } = string.Empty;
    public string Phase { get; set; } = "INITIAL_EVALUATION"; // evaluation pass: INITIAL_EVALUATION | CONFIRMATION_RECHECK
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public TradeCouncilSession Session { get; set; } = null!;
}

public class ExecutedOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TradeCouncilSessionId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Instrument { get; set; } = "EQUITY";
    public string Side { get; set; } = "buy";
    public double Quantity { get; set; }
    public double Price { get; set; }
    public string BrokerOrderId { get; set; } = string.Empty;
    public string Status { get; set; } = "SUBMITTED";
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

    [JsonIgnore]
    public TradeCouncilSession Session { get; set; } = null!;
}
