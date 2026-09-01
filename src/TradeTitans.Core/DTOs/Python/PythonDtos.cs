using System.Text.Json.Serialization;

namespace TradeTitans.Core.DTOs.Python;

public record IndicatorDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("interpretation")] string? Interpretation
);

public record MarketSnapshotDto(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("as_of")] DateTime AsOf,
    [property: JsonPropertyName("price")] double Price,
    [property: JsonPropertyName("prev_close")] double PrevClose,
    [property: JsonPropertyName("change_pct")] double ChangePct,
    [property: JsonPropertyName("volume")] double Volume,
    [property: JsonPropertyName("avg_volume_20d")] double AvgVolume20d,
    [property: JsonPropertyName("volume_ratio")] double VolumeRatio,
    [property: JsonPropertyName("volatility_20d")] double Volatility20d,
    [property: JsonPropertyName("indicators")] List<IndicatorDto>? Indicators,
    [property: JsonPropertyName("news_headlines")] List<string>? NewsHeadlines,
    [property: JsonPropertyName("data_quality")] string DataQuality
);

public record OptionContractDto(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("underlying")] string Underlying,
    [property: JsonPropertyName("contract_type")] string ContractType,
    [property: JsonPropertyName("strike")] double Strike,
    [property: JsonPropertyName("expiration")] string Expiration,
    [property: JsonPropertyName("bid")] double Bid,
    [property: JsonPropertyName("ask")] double Ask,
    [property: JsonPropertyName("last_price")] double? LastPrice,
    [property: JsonPropertyName("open_interest")] int OpenInterest,
    [property: JsonPropertyName("implied_volatility")] double? ImpliedVolatility,
    [property: JsonPropertyName("days_to_expiration")] int DaysToExpiration
);

public record OptionChainSnapshotDto(
    [property: JsonPropertyName("underlying")] string Underlying,
    [property: JsonPropertyName("as_of")] DateTime AsOf,
    [property: JsonPropertyName("calls")] List<OptionContractDto>? Calls,
    [property: JsonPropertyName("puts")] List<OptionContractDto>? Puts,
    [property: JsonPropertyName("data_quality")] string DataQuality
);

public record AgentOutputDto(
    [property: JsonPropertyName("agent")] string Agent,
    [property: JsonPropertyName("decision")] string Decision,
    [property: JsonPropertyName("confidence")] int Confidence,
    [property: JsonPropertyName("thesis")] string Thesis,
    [property: JsonPropertyName("evidence")] List<string>? Evidence,
    [property: JsonPropertyName("risks")] List<string>? Risks,
    [property: JsonPropertyName("invalidators")] List<string>? Invalidators,
    [property: JsonPropertyName("questions_for_other_agents")] List<string>? QuestionsForOtherAgents,
    [property: JsonPropertyName("is_fallback")] bool IsFallback,
    [property: JsonPropertyName("confidence_source")] string? ConfidenceSource = null
);

public record OptionLegDetailsDto(
    [property: JsonPropertyName("contract_type")] string ContractType,
    [property: JsonPropertyName("contract_symbol")] string ContractSymbol,
    [property: JsonPropertyName("strike")] double Strike,
    [property: JsonPropertyName("expiration")] string Expiration,
    [property: JsonPropertyName("contracts")] int Contracts,
    [property: JsonPropertyName("estimated_premium_per_contract")] double EstimatedPremiumPerContract,
    [property: JsonPropertyName("estimated_total_premium")] double EstimatedTotalPremium,
    [property: JsonPropertyName("max_loss")] double MaxLoss,
    [property: JsonPropertyName("breakeven_price")] double BreakevenPrice,
    [property: JsonPropertyName("days_to_expiration")] int DaysToExpiration
);

public record InstrumentDecisionDto(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("instrument")] string Instrument,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("rationale")] string Rationale,
    [property: JsonPropertyName("option_details")] OptionLegDetailsDto? OptionDetails,
    [property: JsonPropertyName("rejected_reason")] string? RejectedReason
);

public record CouncilRunResultDto(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("snapshot")] MarketSnapshotDto Snapshot,
    [property: JsonPropertyName("option_chain")] OptionChainSnapshotDto? OptionChain,
    [property: JsonPropertyName("bull")] AgentOutputDto Bull,
    [property: JsonPropertyName("bear")] AgentOutputDto Bear,
    [property: JsonPropertyName("hype")] AgentOutputDto Hype,
    [property: JsonPropertyName("challenger")] AgentOutputDto Challenger,
    [property: JsonPropertyName("instrument_decision")] InstrumentDecisionDto? InstrumentDecision
);
