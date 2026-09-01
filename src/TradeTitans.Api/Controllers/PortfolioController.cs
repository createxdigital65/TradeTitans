using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeTitans.Core.Data;
using TradeTitans.Core.Interfaces;

namespace TradeTitans.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PortfolioController : ControllerBase
{
    private readonly IAlpacaPaperService _alpacaService;
    private readonly TradeTitansDbContext _dbContext;

    public PortfolioController(IAlpacaPaperService alpacaService, TradeTitansDbContext dbContext)
    {
        _alpacaService = alpacaService;
        _dbContext = dbContext;
    }

    [HttpGet("account")]
    public async Task<IActionResult> GetAccount(CancellationToken cancellationToken)
    {
        var account = await _alpacaService.GetAccountAsync(cancellationToken);
        if (account == null)
        {
            return StatusCode(500, new { message = "Could not retrieve account details from broker." });
        }
        return Ok(account);
    }

    [HttpGet("positions")]
    public async Task<IActionResult> GetPositions(CancellationToken cancellationToken)
    {
        var positions = await _alpacaService.GetPositionsAsync(cancellationToken);
        return Ok(positions);
    }

    [HttpGet("orders")]
    public async Task<IActionResult> GetOrders([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var orders = await _dbContext.ExecutedOrders
            .OrderByDescending(o => o.ExecutedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return Ok(orders);
    }
}
