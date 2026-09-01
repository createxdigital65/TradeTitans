# ⚔️ Trade Titans — Multi-Agent AI Trading Council

**Alpaca AI Trading Agents Hackathon 2026 Submission**

> *"Before AI trades, make AI argue."*

Trade Titans is a multi-agent AI trading command center built for the Alpaca AI Trading Agents Hackathon 2026. It combines quantitative market analytics, a 4-agent LLM council (Bull, Bear, Hype Investigator, Challenger), an Options Strategist, a **Deterministic Risk Guardian** safety boundary, and automated paper trade execution via the Alpaca Paper Trading API.

---

## 🏛️ System Architecture

```
Angular 18 Command Center
   │
   ▼
ASP.NET Core REST API & Trade Council Orchestrator
   │
   ├── Python AI Analytics Service (Vercel: https://trade-titan-seven.vercel.app)
   │     ├── Market Intelligence & Indicators
   │     ├── Bull Agent 🐂
   │     ├── Bear Agent 🐻
   │     ├── Hype Investigator 🔥
   │     ├── Challenger Agent ⚔️
   │     └── Options Strategist 📈
   │
   ├── Deterministic Risk Guardian 🛡️ (Hard Safety Rules Engine)
   │     ├── Maximum Position Size Rule (10% max allocation)
   │     ├── Minimum Cash Reserve Rule (20% cash reserve)
   │     ├── Options Expiration Safety (Min 7 DTE)
   │     └── Market Data Quality Safeguard
   │
   ├── Chief Trader Authorization & Execution 👤
   │
   ├── Alpaca Paper Trading Adapter 📄 (REST API)
   │
   └── EF Core SQLite Audit Trail (tradetitans.db)
```

---

## 🚀 How to Run the Complete System

### 1. Prerequisites
- **.NET 8.0 SDK or .NET 9/10 SDK**
- **Node.js (v18+ or v24+) & npm**

---

### 2. Start ASP.NET Core Backend
From `D:\TeamTitans`:

```bash
cd src/TradeTitans.Api
dotnet run
```

The REST API will launch at `http://localhost:5000` (or `https://localhost:5001`).
Interactive Swagger documentation is available at `http://localhost:5000/swagger`.

---

### 3. Start Angular 18 Command Center
From `D:\TeamTitans`:

```bash
cd ui
npm start
```

Open your browser at `http://localhost:4200` to access the live Trade Titans Command Center dashboard.

---

## 🧪 Running Automated Tests

```bash
cd D:\TeamTitans
dotnet test
```

All 5 unit & safety tests verify:
- Deterministic Risk Guardian vetoes on position size violations.
- Risk Guardian approvals on compliant trades.
- Stale market data quality halts.
- Chief Trader execution block upon Risk Guardian veto.
- Order submission authorization upon Risk Guardian approval.

---

## 🔒 Safety & Paper Trading
- **Broker Endpoint**: Configured exclusively to Alpaca Paper Trading (`https://paper-api.alpaca.markets/v2`).
- **Mock Mode**: Enabled by default (`Alpaca:UseMock = true`) for offline/demo reliability.
- **LLM Safety Guarantee**: Hard deterministic risk rules in C# **cannot be bypassed** by any LLM proposal.

---

## 📄 License & Hackathon Info
Built for the Alpaca AI Trading Agents Hackathon 2026.
