using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TradeTitans.Core.DTOs.Python;
using TradeTitans.Core.Interfaces;

namespace TradeTitans.Core.Services;

public class PythonAnalyticsClient : IPythonAnalyticsClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PythonAnalyticsClient> _logger;

    public PythonAnalyticsClient(HttpClient httpClient, ILogger<PythonAnalyticsClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<bool> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Python service health check.");
            return false;
        }
    }

    public async Task<MarketSnapshotDto?> GetMarketSnapshotAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/market/snapshot/{Uri.EscapeDataString(symbol)}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Python snapshot for {Symbol} returned HTTP {Status}.", symbol, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<MarketSnapshotDto>(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Python snapshot for {Symbol} timed out.", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching market snapshot for {Symbol}", symbol);
            return null;
        }
    }

    public async Task<OptionChainSnapshotDto?> GetMarketOptionsAsync(string symbol, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/market/options/{Uri.EscapeDataString(symbol)}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Python options chain for {Symbol} returned HTTP {Status}.", symbol, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<OptionChainSnapshotDto>(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Python options chain for {Symbol} timed out.", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching market options for {Symbol}", symbol);
            return null;
        }
    }

    public async Task<CouncilRunResultDto?> RunCouncilAsync(string symbol, double portfolioValue = 100000.0, bool useOptions = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/council/run/{Uri.EscapeDataString(symbol)}?portfolio_value={portfolioValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}&use_options={useOptions.ToString().ToLowerInvariant()}";
            _logger.LogInformation("Calling Python Run Council: {Url}", url);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(90));

            var response = await _httpClient.PostAsync(url, null, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Python council run for {Symbol} returned HTTP {Status}.", symbol, (int)response.StatusCode);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<CouncilRunResultDto>(cancellationToken: cts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Python council run for {Symbol} timed out after 90 seconds.", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing Run Council for {Symbol}", symbol);
            return null;
        }
    }
}
