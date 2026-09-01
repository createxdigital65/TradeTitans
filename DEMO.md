# 🎬 Trade Titans — Hackathon Live Demo Guide

Follow these 3 demo flows during your hackathon presentation to showcase the power, debate visibility, and safety of Trade Titans.

---

## DEMO 1: Normal AI Council Debate & Approved Paper Execution
1. Open Angular dashboard at `http://localhost:4200`.
2. Ensure top bar displays `PYTHON API: ONLINE` and `ALPACA: PAPER TRADING ONLY`.
3. Select quick target `AAPL` and click **"⚡ RUN AI COUNCIL DEBATE"**.
4. Show the judges:
   - **Market Snapshot Bar**: Price $224.50, Volume Ratio 0.93x, Volatility 18.5%.
   - **4 Agent Cards**:
     - 🐂 **Bull Agent**: BUY (84% confidence) — thesis on volume momentum.
     - 🐻 **Bear Agent**: HOLD (62% confidence) — counters valuation resistance.
     - 🔥 **Hype Investigator**: BUY (78% confidence) — high social sentiment 0.82.
     - ⚔️ **Challenger**: BUY (81% confidence) — synthesizes consensus.
   - **Options Strategy**: Recommends `AAPL260918C00225000` (17 DTE long call).
   - **Risk Guardian**: All 4 rules display **✓ PASS**.
   - **Chief Trader Preview**: Trade preview shows estimated cost $860.00.
5. Click **"⚡ EXECUTE PAPER TRADE VIA ALPACA"**.
6. Show the success notification with Broker Order ID.

---

## DEMO 2: Deterministic Risk Guardian Veto (Safety Guarantee)
1. Select target symbol or set portfolio value low.
2. If a trade proposal cost exceeds 10% of portfolio allocation or option DTE < 7 days:
3. Show the judges:
   - Risk Guardian Banner turns RED: **✕ VETOED BY RISK GUARDIAN**.
   - Maximum Position Size rule displays **✕ VETO** (e.g. 25% > 10% threshold).
   - Chief Trader execution button is **DISABLED** with message: `🚫 EXECUTION BLOCKED BY RISK GUARDIAN`.
   - Highlight: *"No LLM proposal can bypass our hard C# safety rules."*

---

## DEMO 3: Portfolio & Audit Trail
1. Click the **"Portfolio"** tab in top navigation:
   - Show live Paper Trading Cash ($100,000) and Buying Power ($400,000).
   - Show active open paper positions.
2. Click the **"Audit & Veto Logs"** tab:
   - Show table of historic council debate sessions.
   - Click **"INSPECT"** on any session to reveal the full audit trail: Challenger thesis, risk rule pass/fail logs, and broker order IDs.
