using Microsoft.AspNetCore.Mvc;
using TradeTitans.Core.DTOs.Alpaca;
using TradeTitans.Core.DTOs.Python;
using TradeTitans.Core.Interfaces;

namespace TradeTitans.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CommandCenterController : ControllerBase
{
    private readonly IPythonAnalyticsClient _pythonClient;
    private readonly IAlpacaPaperService _alpacaService;

    public CommandCenterController(IPythonAnalyticsClient pythonClient, IAlpacaPaperService alpacaService)
    {
        _pythonClient = pythonClient;
        _alpacaService = alpacaService;
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        var pythonOk = await _pythonClient.GetHealthAsync(cancellationToken);

        AlpacaAccountDto? account = null;
        try
        {
            account = await _alpacaService.GetAccountAsync(cancellationToken);
        }
        catch
        {
            // Account failure is surfaced as alpacaPaperConnected=false.
        }

        var alpacaOk = account != null;

        return Ok(new
        {
            status = pythonOk && alpacaOk ? "ok" : "degraded",
            backend = "online",
            pythonServiceConnected = pythonOk,
            alpacaPaperConnected = alpacaOk,
            demoMode = !pythonOk
        });
    }

    [HttpGet("snapshot/{symbol}")]
    public async Task<ActionResult<MarketSnapshotDto>> GetSnapshot(string symbol, CancellationToken cancellationToken)
    {
        var snapshot = await _pythonClient.GetMarketSnapshotAsync(symbol, cancellationToken);
        if (snapshot == null)
        {
            return NotFound(new { message = $"Market snapshot for {symbol} could not be retrieved." });
        }
        return Ok(snapshot);
    }

    [HttpGet("options/{symbol}")]
    public async Task<ActionResult<OptionChainSnapshotDto>> GetOptions(string symbol, CancellationToken cancellationToken)
    {
        var options = await _pythonClient.GetMarketOptionsAsync(symbol, cancellationToken);
        if (options == null)
        {
            return NotFound(new { message = $"Options chain snapshot for {symbol} could not be retrieved." });
        }
        return Ok(options);
    }
}
