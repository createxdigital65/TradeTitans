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
    /// </summary>
    [HttpPost("run/{symbol}")]
    public async Task<IActionResult> RunCouncil(string symbol, [FromQuery] double portfolioValue = 100000.0, [FromQuery] bool useOptions = true, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _orchestrator.RunFullCouncilDebateAsync(symbol, portfolioValue, useOptions, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            // Upstream Python analytics service failure / invalid payload.
            return StatusCode(502, new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
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
