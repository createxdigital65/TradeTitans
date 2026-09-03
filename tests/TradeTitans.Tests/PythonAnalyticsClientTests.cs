using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using TradeTitans.Core.Interfaces;
using TradeTitans.Core.Services;
using Xunit;

namespace TradeTitans.Tests;

public class PythonAnalyticsClientTests
{
    private const string ValidCouncilJson = """
        {
          "symbol": "AAPL",
          "snapshot": {
            "symbol": "AAPL",
            "as_of": "2026-09-01T12:00:00Z",
            "price": 224.5,
            "prev_close": 221.1,
            "change_pct": 1.54,
            "volume": 10000000,
            "avg_volume_20d": 10000000,
            "volume_ratio": 1.0,
            "volatility_20d": 0.18,
            "indicators": [],
            "news_headlines": [],
            "data_quality": "ok"
          },
          "option_chain": null,
          "bull": { "agent": "bull", "decision": "BUY", "confidence": 84, "thesis": "Bull thesis", "evidence": [], "risks": [], "invalidators": [], "questions_for_other_agents": [], "is_fallback": false },
          "bear": { "agent": "bear", "decision": "HOLD", "confidence": 61, "thesis": "Bear thesis", "evidence": [], "risks": [], "invalidators": [], "questions_for_other_agents": [], "is_fallback": false },
          "hype": { "agent": "hype", "decision": "BUY", "confidence": 78, "thesis": "Hype thesis", "evidence": [], "risks": [], "invalidators": [], "questions_for_other_agents": [], "is_fallback": false },
          "challenger": { "agent": "challenger", "decision": "BUY", "confidence": 81, "thesis": "Challenger thesis", "evidence": [], "risks": [], "invalidators": [], "questions_for_other_agents": [], "is_fallback": false },
          "instrument_decision": {
            "symbol": "AAPL",
            "instrument": "EQUITY",
            "action": "BUY",
            "rationale": "Equity sized by Chief Trader",
            "option_details": null,
            "rejected_reason": null
          }
        }
        """;

    [Fact]
    public async Task RunCouncil_Success_DeserializesCouncilResult()
    {
        var client = CreateClient(new StubHttpHandler(HttpStatusCode.OK, ValidCouncilJson));

        var result = await client.RunCouncilAsync("AAPL");

        Assert.NotNull(result);
        Assert.Equal("AAPL", result.Symbol);
        Assert.Equal("BUY", result.Challenger.Decision);
    }

    [Fact]
    public async Task RunCouncil_HttpError_ReturnsNull()
    {
        var client = CreateClient(new StubHttpHandler(HttpStatusCode.InternalServerError, """{ "detail": "boom" }"""));

        var result = await client.RunCouncilAsync("AAPL");

        Assert.Null(result);
    }

    [Fact]
    public async Task RunCouncil_Timeout_ReturnsNull()
    {
        var client = CreateClient(new TimeoutHttpHandler());

        var result = await client.RunCouncilAsync("AAPL");

        Assert.Null(result);
    }

    [Fact]
    public async Task RunCouncil_MalformedJson_ReturnsNull()
    {
        var client = CreateClient(new StubHttpHandler(HttpStatusCode.OK, "{ not valid json"));

        var result = await client.RunCouncilAsync("AAPL");

        Assert.Null(result);
    }

    // --- RunCouncilWithStatusAsync tests — the hardened boundary ---

    [Fact]
    public async Task RunCouncilWithStatusAsync_Success_ReturnsSuccess()
    {
        var client = CreateClient(new StubHttpHandler(HttpStatusCode.OK, ValidCouncilJson));

        var result = await client.RunCouncilWithStatusAsync("AAPL");

        Assert.Equal(CouncilRunStatus.Success, result.Status);
        Assert.NotNull(result.CouncilResult);
        Assert.Equal("AAPL", result.CouncilResult!.Symbol);
    }

    [Fact]
    public async Task RunCouncilWithStatusAsync_404_ReturnsSymbolUnavailable()
    {
        var client = CreateClient(new StubHttpHandler(HttpStatusCode.NotFound, "Not found: symbol INVALIDXYZ"));

        var result = await client.RunCouncilWithStatusAsync("INVALIDXYZ");

        Assert.Equal(CouncilRunStatus.SymbolUnavailable, result.Status);
        Assert.Null(result.CouncilResult);
    }

    [Fact]
    public async Task RunCouncilWithStatusAsync_422_ReturnsSymbolUnavailable()
    {
        var client = CreateClient(new StubHttpHandler((HttpStatusCode)422, "No data for symbol NESHAT"));

        var result = await client.RunCouncilWithStatusAsync("NESHAT");

        Assert.Equal(CouncilRunStatus.SymbolUnavailable, result.Status);
    }

    [Fact]
    public async Task RunCouncilWithStatusAsync_500_WithSymbolMarker_ReturnsSymbolUnavailable()
    {
        // The hosted Python service returns HTTP 500 for invalid symbols. When the body names the
        // symbol as unknown, we must classify it as SymbolUnavailable, NOT ServiceUnavailable.
        var client = CreateClient(new StubHttpHandler(HttpStatusCode.InternalServerError, "Unknown ticker NESHAT"));

        var result = await client.RunCouncilWithStatusAsync("NESHAT");

        Assert.Equal(CouncilRunStatus.SymbolUnavailable, result.Status);
    }

    [Fact]
    public async Task RunCouncilWithStatusAsync_500_Generic_ReturnsServiceUnavailable()
    {
        var client = CreateClient(new StubHttpHandler(HttpStatusCode.InternalServerError, "Internal server error"));

        var result = await client.RunCouncilWithStatusAsync("AAPL");

        Assert.Equal(CouncilRunStatus.ServiceUnavailable, result.Status);
    }

    [Fact]
    public async Task RunCouncilWithStatusAsync_Timeout_ReturnsServiceUnavailable()
    {
        var client = CreateClient(new TimeoutHttpHandler());

        var result = await client.RunCouncilWithStatusAsync("AAPL");

        Assert.Equal(CouncilRunStatus.ServiceUnavailable, result.Status);
    }

    [Fact]
    public async Task RunCouncilWithStatusAsync_503_ReturnsServiceUnavailable()
    {
        var client = CreateClient(new StubHttpHandler(HttpStatusCode.ServiceUnavailable, "Service temporarily unavailable"));

        var result = await client.RunCouncilWithStatusAsync("AAPL");

        Assert.Equal(CouncilRunStatus.ServiceUnavailable, result.Status);
    }

    private static PythonAnalyticsClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://python.test") };
        return new PythonAnalyticsClient(httpClient, NullLogger<PythonAnalyticsClient>.Instance);
    }
}

internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _statusCode;
    private readonly string _body;

    public StubHttpHandler(HttpStatusCode statusCode, string body)
    {
        _statusCode = statusCode;
        _body = body;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_body)
        };
        return Task.FromResult(response);
    }
}

internal sealed class TimeoutHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        throw new TaskCanceledException("Simulated upstream timeout.");
    }
}