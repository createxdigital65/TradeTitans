# Trade Titans - Two-Phase Flow Runtime Verification
$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5000/api'

function PostJson($url) {
    try { return Invoke-RestMethod -Method Post -Uri $url -Body '{}' -ContentType 'application/json' -TimeoutSec 180 }
    catch {
        Write-Output ("  HTTP-FAIL: " + $_.Exception.Message)
        if ($_.ErrorDetails.Message) { Write-Output ("  Detail: " + $_.ErrorDetails.Message) }
        return $null
    }
}

function GetJson($url) {
    try { return Invoke-RestMethod -Uri $url -TimeoutSec 60 }
    catch { return $null }
}

# ---------- PHASE 1: council/run on multiple symbols; assert NEVER executes ----------
Write-Output '=== PHASE 1: /council/run analyze-only guarantee ==='
$pending = $null
foreach ($sym in @('AAPL','NVDA','TSLA','MSFT','AMD')) {
    $r = PostJson "$base/council/run/$sym`?portfolioValue=100000&useOptions=false"
    if (-not $r) { continue }
    $s = $r.session
    $safe = (-not $r.executionResult.executed) -and (-not $s.chiefTraderExecuted) -and (-not $s.brokerOrderId)
    Write-Output ("{0}: status={1} action={2} risk={3} executed={4} SAFE={5}" -f $sym, $s.sessionStatus, $s.proposedAction, $s.riskGuardianStatus, $r.executionResult.executed, $safe)
    if ($s.sessionStatus -eq 'PENDING_CONFIRMATION' -and -not $pending) { $pending = $r }
}
