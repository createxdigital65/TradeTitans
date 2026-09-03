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
        var result = await RunCouncilWithStatusAsync(symbol, portfolioValue, useOptions, cancellationToken);
        return result.CouncilResult;
    }

    /// <summary>
    /// Runs the Python council and returns rich status so callers can distinguish a reachable service
    /// rejecting the symbol (4xx / body markers — SymbolUnavailable) from the service being down or
    /// timing out (5xx, network error — ServiceUnavailable). Raw 500s for invalid symbols are
    /// surfaced as SymbolUnavailable via body heuristics, so Angular never sees a raw stack trace.
    /// </summary>
    public async Task<CouncilRunStatusResult> RunCouncilWithStatusAsync(string symbol, double portfolioValue = 100000.0, bool useOptions = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"/council/run/{Uri.EscapeDataString(symbol)}?portfolio_value={portfolioValue.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}&use_options={useOptions.ToString().ToLowerInvariant()}";
            _logger.LogInformation("Calling Python Run Council: {Url}", url);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(90));

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsync(url, null, cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Python council run for {Symbol} timed out after 90 seconds.", symbol);
                return new CouncilRunStatusResult(CouncilRunStatus.ServiceUnavailable, null, null, "Python analytics service timed out.");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Python service unreachable for {Symbol}.", symbol);
                return new CouncilRunStatusResult(CouncilRunStatus.ServiceUnavailable, null, null, ex.Message);
            }

            var httpStatus = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    var dto = await response.Content.ReadFromJsonAsync<CouncilRunResultDto>(cancellationToken: cts.Token);
                    if (dto == null)
                    {
                        _logger.LogWarning("Python council for {Symbol} returned empty body.", symbol);
                        return new CouncilRunStatusResult(CouncilRunStatus.UnexpectedError, null, httpStatus, "Empty response from analytics service.");
                    }
                    return new CouncilRunStatusResult(CouncilRunStatus.Success, dto, httpStatus);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Python council for {Symbol} returned malformed JSON.", symbol);
                    return new CouncilRunStatusResult(CouncilRunStatus.UnexpectedError, null, httpStatus, "Malformed analytics response.");
                }
            }

            var classification = ClassifyFailure(httpStatus, symbol, response);
            string? detail;
            try
            {
                detail = (await response.Content.ReadAsStringAsync(cancellationToken))?.Trim();
                if (string.IsNullOrEmpty(detail)) detail = null;
            }
            catch
            {
                detail = null;
            }

            _logger.LogWarning(
                "Python council for {Symbol} returned HTTP {Status}, classified as {Classification}. Detail: {Detail}",
                symbol, httpStatus, classification, detail);

            return new CouncilRunStatusResult(classification, null, httpStatus, detail);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Python council run for {Symbol} timed out after 90 seconds.", symbol);
            return new CouncilRunStatusResult(CouncilRunStatus.ServiceUnavailable, null, null, "Python analytics service timed out.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error executing Run Council for {Symbol}", symbol);
            return new CouncilRunStatusResult(CouncilRunStatus.UnexpectedError, null, null, ex.Message);
        }
    }

    private static CouncilRunStatus ClassifyFailure(int httpStatus, string symbol, HttpResponseMessage response)
    {
        if (httpStatus == 404 || httpStatus == 422 || httpStatus == 400)
        {
            return CouncilRunStatus.SymbolUnavailable;
        }

        if (httpStatus >= 500 && httpStatus < 600)
        {
            string? body = null;
            try
            {
                body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(body))
            {
                var b = body.ToLowerInvariant();
                var sym = symbol.ToLowerInvariant();
                if (b.Contains(sym) || b.Contains("unknown symbol") || b.Contains("invalid symbol")
                    || b.Contains("no data") || b.Contains("not found") || b.Contains("unrecognized ticker")
                    || b.Contains("unrecognised ticker") || b.Contains("unknown ticker"))
                {
                    return CouncilRunStatus.SymbolUnavailable;
                }
            }
            return CouncilRunStatus.ServiceUnavailable;
        }

        return CouncilRunStatus.UnexpectedError;
    }
}
