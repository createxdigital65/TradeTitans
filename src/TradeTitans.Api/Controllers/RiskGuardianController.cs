using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeTitans.Core.Data;
using TradeTitans.Core.Domain.Enums;
using TradeTitans.Core.RiskRules;

namespace TradeTitans.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RiskGuardianController : ControllerBase
{
    private readonly TradeTitansDbContext _dbContext;
    private readonly IEnumerable<IRiskRule> _rules;

    public RiskGuardianController(TradeTitansDbContext dbContext, IEnumerable<IRiskRule> rules)
    {
        _dbContext = dbContext;
        _rules = rules;
    }

    [HttpGet("active-rules")]
    public IActionResult GetActiveRules()
    {
        var rulesList = _rules.Select(r => new
        {
            name = r.RuleName,
            threshold = r.Threshold,
            description = r.Description
        }).ToList();

        return Ok(rulesList);
    }

    [HttpGet("veto-logs")]
    public async Task<IActionResult> GetVetoLogs([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var vetoes = await _dbContext.TradeCouncilSessions
            .Include(s => s.RiskLogs)
            .Where(s => s.RiskGuardianStatus == RiskStatus.VETOED_BY_RISK_GUARDIAN)
            .OrderByDescending(s => s.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return Ok(vetoes);
    }
}
