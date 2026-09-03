using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeTitans.Core.Data;
using TradeTitans.Core.Interfaces;

namespace TradeTitans.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CouncilController : ControllerBase
{
    private readonly ITradeCouncilOrchestrator _orchestrator;
    private readonly TradeTitansDbContext _dbContext;

    public CouncilController(ITradeCouncilOrchestrator orchestrator, TradeTitansDbContext dbContext)
    {
        _orchestrator = orchestrator;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Runs the AI council debate and Risk Guardian evaluation, persists the session and returns a
    /// TRADE PREVIEW. It never executes an order — the user must confirm via POST sessions/{id}/confirm.
    ///
    /// Returns differentiated status codes so the Angular client can distinguish failure modes:
    ///   200 — success (valid council session, including legitimate NO_TRADE verdicts)
    ///   422 — symbol unavailable: analytics service reachable but market data could not be retrieved
    ///         for the requested symbol (invalid / unsupported / no-data symbol)
    ///   503 — analytics service unavailable: Python service down, timed out, or network-unreachable
    ///   500 — unexpected backend error
    /// A cancelled / vetoed / NO_TRADE session is still a 200 with the appropriate session state.
    /// </summary>
    [HttpPost("run/{symbol}")]
    public async Task<IActionResult> RunCouncil(string symbol, [FromQuery] double portfolioValue = 100000.0, [FromQuery] bool useOptions = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _orchestrator.RunFullCouncilDebateAsync(symbol, portfolioValue, useOptions, cancellationToken);
            if (result == null)
            {
                // Orchestrator chose not to return a session (shouldn't happen via current path).
                return StatusCode(500, new { error = "Council orchestrator returned no result.", error_code = "COUNCIL_NO_RESULT" });
            }
            return Ok(result);
        }
        catch (CouncilServiceException ex)
        {
            // Python analytics service is down / timed out / unreachable. 503 = try again later.
            return StatusCode(503, new { error = ex.Message, error_code = "ANALYTICS_SERVICE_UNAVAILABLE" });
        }
        catch (CouncilSymbolUnavailableException ex)
        {
            // Analytics service reachable but symbol rejected (4xx or 5xx-with-symbol-marker).
            // 422 = well-formed request, semantically invalid symbol for the upstream data provider.
            return StatusCode(422, new { error = ex.Message, error_code = "SYMBOL_UNAVAILABLE", symbol = ex.Symbol });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(500, new { error = ex.Message, error_code = "INVALID_OPERATION" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message, error_code = "UNEXPECTED_ERROR" });
        }
    }

    /// <summary>
    /// Explicit human confirmation. Re-runs the deterministic Risk Guardian against the current
    /// account and only then lets Chief Trader submit the order to Alpaca PAPER trading.
    /// </summary>
    [HttpPost("sessions/{id:guid}/confirm")]
    public async Task<IActionResult> ConfirmSession(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _orchestrator.ConfirmAndExecuteAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            var message = ex.Message.ToLowerInvariant();
            if (message.Contains("not found"))
            {
                return NotFound(new { error = ex.Message });
            }
            if (message.Contains("cannot be executed") || message.Contains("did not approve") || message.Contains("no stored council payload"))
            {
                // Session exists but is vetoed / already executed / not actionable -> conflict state.
                return StatusCode(409, new { error = ex.Message });
            }
            return StatusCode(502, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Explicit human cancellation. A cancelled session can never be executed.
    /// </summary>
    [HttpPost("sessions/{id:guid}/cancel")]
    public async Task<IActionResult> CancelSession(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            var session = await _orchestrator.CancelPendingSessionAsync(id, cancellationToken);
            return Ok(session);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message.ToLowerInvariant().Contains("not found")
                ? NotFound(new { error = ex.Message })
                : StatusCode(409, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var sessions = await _orchestrator.GetRecentSessionsAsync(limit, cancellationToken);
        return Ok(sessions);
    }

    [HttpGet("sessions/{id:guid}")]
    public async Task<IActionResult> GetSessionById(Guid id, CancellationToken cancellationToken = default)
    {
        var session = await _dbContext.TradeCouncilSessions
            .Include(s => s.AgentProposals)
            .Include(s => s.RiskLogs)
            .Include(s => s.ExecutedOrder)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

        if (session == null)
        {
            return NotFound(new { message = $"Council session with ID '{id}' not found." });
        }

        return Ok(session);
    }
}
