using System.Text.Json.Serialization;

namespace TradeTitans.Core.DTOs.Alpaca;

public record AlpacaAccountDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("account_number")] string AccountNumber,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("buying_power")] string BuyingPower,
    [property: JsonPropertyName("cash")] string Cash,
    [property: JsonPropertyName("portfolio_value")] string PortfolioValue,
    [property: JsonPropertyName("equity")] string Equity,
    [property: JsonPropertyName("long_market_value")] string LongMarketValue,
    [property: JsonPropertyName("short_market_value")] string ShortMarketValue,
    [property: JsonPropertyName("initial_margin")] string InitialMargin
);

public record AlpacaOrderRequestDto(
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("qty")] double? Qty,
    [property: JsonPropertyName("notional")] double? Notional,
    [property: JsonPropertyName("side")] string Side, // "buy" | "sell"
    [property: JsonPropertyName("type")] string Type, // "market" | "limit"
    [property: JsonPropertyName("time_in_force")] string TimeInForce, // "day" | "gtc"
    [property: JsonPropertyName("limit_price")] double? LimitPrice
);

public record AlpacaOrderResponseDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("client_order_id")] string ClientOrderId,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime? UpdatedAt,
    [property: JsonPropertyName("submitted_at")] DateTime? SubmittedAt,
    [property: JsonPropertyName("filled_at")] DateTime? FilledAt,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("qty")] string? Qty,
    [property: JsonPropertyName("filled_qty")] string FilledQty,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("time_in_force")] string TimeInForce,
    [property: JsonPropertyName("limit_price")] string? LimitPrice,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("filled_avg_price")] string? FilledAvgPrice
);

public record AlpacaPositionDto(
    [property: JsonPropertyName("asset_id")] string AssetId,
    [property: JsonPropertyName("symbol")] string Symbol,
    [property: JsonPropertyName("exchange")] string Exchange,
    [property: JsonPropertyName("asset_class")] string AssetClass,
    [property: JsonPropertyName("avg_entry_price")] string AvgEntryPrice,
    [property: JsonPropertyName("qty")] string Qty,
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("market_value")] string MarketValue,
    [property: JsonPropertyName("cost_basis")] string CostBasis,
    [property: JsonPropertyName("unrealized_pl")] string UnrealizedPl,
    [property: JsonPropertyName("unrealized_plpc")] string UnrealizedPlpc,
    [property: JsonPropertyName("current_price")] string CurrentPrice
);
