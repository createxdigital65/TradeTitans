using TradeTitans.Core.DTOs.Python;

namespace TradeTitans.Core.Interfaces;

/// <summary>
/// Classifies the outcome of a Python council run so the API layer can return meaningful HTTP
/// responses to the Angular client instead of collapsing everything into a generic 500/502.
/// </summary>
public enum CouncilRunStatus
{
    /// <summary>Python returned a valid, fully populated council result.</summary>
    Success,

    /// <summary>
    /// Python returned a 4xx for this specific symbol (e.g. 404 ticker not found, 422 the market
    /// data provider has no data for it). The analytics service itself was reachable.
    /// </summary>
    SymbolUnavailable,

    /// <summary>
    /// The Python service is down / returned 5xx / timed out / connection failed. Different from
    /// a symbol-specific failure — the caller may choose to surface a "try again later" message.
    /// </summary>
    ServiceUnavailable,

    /// <summary>Any other unexpected failure (deserialization error, etc.).</summary>
    UnexpectedError
}

/// <summary>
/// Rich result from a Python council run carrying both the payload (when present) and a status
/// code so the controller can differentiate:
///   - service unavailable (HTTP 503)
///   - symbol unavailable (HTTP 422)
///   - unexpected backend error (HTTP 500)
///   - success (HTTP 200)
/// </summary>
public record CouncilRunStatusResult(
    CouncilRunStatus Status,
    CouncilRunResultDto? CouncilResult,
    int? HttpStatusCode = null,
    string? Detail = null)
{
    public bool IsSuccess => Status == CouncilRunStatus.Success && CouncilResult != null;
}

/// <summary>
/// Thrown by the orchestrator when the Python analytics service is unreachable, returned a 5xx
/// without symbol-specific markers, or timed out. The API layer surfaces HTTP 503 with an
/// ANALYTICS_SERVICE_UNAVAILABLE error code.
/// </summary>
public class CouncilServiceException : InvalidOperationException
{
    public CouncilServiceException(string message) : base(message) { }
    public CouncilServiceException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown by the orchestrator when the analytics service is reachable but the requested symbol is
/// invalid, unsupported, or has no retrievable market data. The API layer surfaces HTTP 422 with
/// a SYMBOL_UNAVAILABLE error code so Angular can show a clear "verify the symbol" message and
/// stop the workflow without fabricating data.
/// </summary>
public class CouncilSymbolUnavailableException : InvalidOperationException
{
    public string Symbol { get; }

    public CouncilSymbolUnavailableException(string symbol, string message)
        : base(message)
    {
        Symbol = symbol;
    }
}

public interface IPythonAnalyticsClient
{
    Task<bool> GetHealthAsync(CancellationToken cancellationToken = default);
    Task<MarketSnapshotDto?> GetMarketSnapshotAsync(string symbol, CancellationToken cancellationToken = default);
    Task<OptionChainSnapshotDto?> GetMarketOptionsAsync(string symbol, CancellationToken cancellationToken = default);
    Task<CouncilRunResultDto?> RunCouncilAsync(string symbol, double portfolioValue = 100000.0, bool useOptions = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the Python council and returns a rich status result so callers can differentiate a genuine
    /// analytics service failure from a symbol-specific failure (invalid / unsupported / no-data).
    /// </summary>
    Task<CouncilRunStatusResult> RunCouncilWithStatusAsync(string symbol, double portfolioValue = 100000.0, bool useOptions = true, CancellationToken cancellationToken = default);
}
