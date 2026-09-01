using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TradeTitans.Core.DTOs.Alpaca;
using TradeTitans.Core.Interfaces;

namespace TradeTitans.Core.Services;

public class AlpacaPaperService : IAlpacaPaperService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<AlpacaPaperService> _logger;
    private readonly bool _isMockMode;

    public AlpacaPaperService(HttpClient httpClient, IConfiguration config, ILogger<AlpacaPaperService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        var useMockStr = config["Alpaca:UseMock"];
        _isMockMode = string.Equals(useMockStr, "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<AlpacaAccountDto?> GetAccountAsync(CancellationToken cancellationToken = default)
    {
        if (_isMockMode)
        {
            return new AlpacaAccountDto(
                Id: "mock-account-123",
                AccountNumber: "PA3MOCK12345",
                Status: "ACTIVE",
                Currency: "USD",
                BuyingPower: "400000.00",
                Cash: "100000.00",
                PortfolioValue: "100000.00",
                Equity: "100000.00",
                LongMarketValue: "0.00",
                ShortMarketValue: "0.00",
                InitialMargin: "0.00"
            );
        }

        try
        {
            return await _httpClient.GetFromJsonAsync<AlpacaAccountDto>("/v2/account", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Alpaca Paper account info. Falling back to default mock.");
            return new AlpacaAccountDto("mock-account-123", "PA3MOCK12345", "ACTIVE", "USD", "400000.00", "100000.00", "100000.00", "100000.00", "0.00", "0.00", "0.00");
        }
    }

    public async Task<List<AlpacaPositionDto>> GetPositionsAsync(CancellationToken cancellationToken = default)
    {
        if (_isMockMode)
        {
            return new List<AlpacaPositionDto>();
        }

        try
        {
            var positions = await _httpClient.GetFromJsonAsync<List<AlpacaPositionDto>>("/v2/positions", cancellationToken);
            return positions ?? new List<AlpacaPositionDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Alpaca Paper positions.");
            return new List<AlpacaPositionDto>();
        }
    }

    public async Task<AlpacaOrderResponseDto?> SubmitOrderAsync(AlpacaOrderRequestDto orderRequest, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Submitting Alpaca Paper Order for {Symbol}: {Side} {Qty} shares/contracts", orderRequest.Symbol, orderRequest.Side, orderRequest.Qty);

        if (_isMockMode)
        {
            return new AlpacaOrderResponseDto(
                Id: Guid.NewGuid().ToString(),
                ClientOrderId: Guid.NewGuid().ToString(),
                CreatedAt: DateTime.UtcNow,
                UpdatedAt: DateTime.UtcNow,
                SubmittedAt: DateTime.UtcNow,
                FilledAt: DateTime.UtcNow,
                Symbol: orderRequest.Symbol,
                Qty: orderRequest.Qty?.ToString() ?? "1",
                FilledQty: orderRequest.Qty?.ToString() ?? "1",
                Type: orderRequest.Type,
                Side: orderRequest.Side,
                TimeInForce: orderRequest.TimeInForce,
                LimitPrice: orderRequest.LimitPrice?.ToString(),
                Status: "filled",
                FilledAvgPrice: orderRequest.LimitPrice?.ToString() ?? "224.50"
            );
        }

        try
        {
            var response = await _httpClient.PostAsJsonAsync("/v2/orders", orderRequest, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<AlpacaOrderResponseDto>(cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting order to Alpaca Paper Trading.");
            return null;
        }
    }
}
