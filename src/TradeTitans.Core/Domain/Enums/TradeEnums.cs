namespace TradeTitans.Core.Domain.Enums;

public enum TradeAction
{
    BUY,
    SELL,
    HOLD,
    NO_TRADE
}

public enum InstrumentType
{
    EQUITY,
    OPTION
}

public enum RiskStatus
{
    APPROVED,
    VETOED_BY_RISK_GUARDIAN,
    WARNING_ISSUED
}

public enum OrderExecutionStatus
{
    PENDING,
    SUBMITTED,
    FILLED,
    CANCELLED,
    REJECTED,
    FAILED
}

/// <summary>
/// Lifecycle of a trade council session through the AI-debate -> Risk -> Chief Trader -> Broker
/// pipeline. A session starts PENDING_CONFIRMATION after Risk Guardian approval and only moves to
/// EXECUTED when a human explicitly confirms the paper trade. A veto moves it straight to VETOED.
/// </summary>
public enum CouncilSessionStatus
{
    PENDING_CONFIRMATION,
    EXECUTED,
    VETOED_BY_RISK_GUARDIAN,
    NO_TRADE,
    CANCELED,
    FAILED
}
