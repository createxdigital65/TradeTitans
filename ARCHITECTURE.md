# Trade Titans — Architectural Specification

## 1. Domain Separation & Microservices Boundary

### Python AI & Analytics Microservice (Remote Hosted on Vercel)
- **Base URL**: `https://trade-titan-seven.vercel.app`
- **Responsibilities**:
  - Ingests raw market bars and computes quant metrics (20D Volume Ratio, 20D Volatility, RSI, SMA).
  - Executes 4 LLM Agent Prompts with JSON schema enforcement:
    1. **Bull Agent**: Formulates bullish thesis and upside catalysts.
    2. **Bear Agent**: Formulates bearish counters and macro risks.
    3. **Hype Investigator**: Measures social/news sentiment and FOMO levels.
    4. **Challenger Agent**: Synthesizes the debate into a direction verdict.
  - Runs **Options Strategist** step to recommend stock vs single-leg call/put options.

### ASP.NET Core Engine (Local D:\TeamTitans)
- **Responsibilities**:
  - **Orchestration**: Calls Python API `/council/run/{symbol}`, passing portfolio cash context.
  - **Deterministic Risk Guardian**: Hard C# rules engine enforcing position limits, cash reserves, DTE safety, and data quality.
  - **Chief Trader**: Final execution authorization layer.
  - **Alpaca Paper Adapter**: Integrates with Alpaca REST API for paper trading.
  - **SQLite Audit Trail**: Persists sessions, proposals, risk logs, and executed paper orders.
  - **REST API & CORS**: Exposes clean endpoints for Angular dashboard.

---

## 2. Deterministic Safety Boundary
Risk Guardian rules are written in pure C# and evaluated before any broker submission:
1. `MaxPositionSizeRule`: Limit to max 10% portfolio value.
2. `MinimumCashReserveRule`: Maintain min 20% cash reserve.
3. `OptionsDteLiquidityRule`: Min 7 DTE required for options.
4. `DataQualityRule`: Market snapshot data quality must be `OK`.

If any rule fails, Risk Guardian issues a binding veto (`VETOED_BY_RISK_GUARDIAN`) and Chief Trader blocks order creation.

---

## 3. Angular 18 Frontend
- **Aesthetic**: Dark Trading Terminal Theme (`#0b0e14`).
- **Features**:
  - Live Market Intelligence Bar
  - 4-Agent Debate Cards (Bull, Bear, Hype, Challenger)
  - Visual Decision Pipeline Diagram
  - Risk Guardian Rule Breakdown Table
  - Trade Preview & Controlled Paper Execution
  - Portfolio Cash & Open Positions View
  - Session Audit History Inspector
