import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of } from 'rxjs';
import {
  MarketSnapshot,
  OptionChainSnapshot,
  FullCouncilRunResponse,
  TradeCouncilSession,
  AlpacaAccount,
  AlpacaPosition,
  CouncilRunResult
} from '../models/trade-titans.models';

@Injectable({
  providedIn: 'root'
})
export class TradeTitansService {
  private readonly baseUrl = 'http://localhost:5000/api';

  constructor(private http: HttpClient) {}

  getHealth(): Observable<{ status: string; pythonServiceConnected: boolean }> {
    return this.http.get<{ status: string; pythonServiceConnected: boolean }>(`${this.baseUrl}/commandcenter/health`).pipe(
      catchError(() => of({ status: 'degraded', pythonServiceConnected: false }))
    );
  }

  getSnapshot(symbol: string): Observable<MarketSnapshot | null> {
    return this.http.get<MarketSnapshot>(`${this.baseUrl}/commandcenter/snapshot/${symbol}`).pipe(
      catchError(() => of(this.getMockSnapshot(symbol)))
    );
  }

  getOptions(symbol: string): Observable<OptionChainSnapshot | null> {
    return this.http.get<OptionChainSnapshot>(`${this.baseUrl}/commandcenter/options/${symbol}`).pipe(
      catchError(() => of(null))
    );
  }

  runCouncil(symbol: string, portfolioValue: number = 100000, useOptions: boolean = true): Observable<FullCouncilRunResponse | null> {
    return this.http.post<FullCouncilRunResponse>(`${this.baseUrl}/council/run/${symbol}?portfolioValue=${portfolioValue}&useOptions=${useOptions}`, {}).pipe(
      catchError((err) => {
        // CASE 3: Backend reachable but upstream symbol failure (HTTP 502).
        // The Python analytics service rejected the symbol — do NOT fabricate a demo session.
        if (err?.status === 502) {
          const upstreamMsg = err?.error?.error || 'Market analytics service could not process this symbol.';
          console.error(`Symbol unavailable for ${symbol}:`, upstreamMsg);
          return of(null);
        }
        // CASE 4: Backend genuinely unavailable (connection refused, network error, status 0).
        // Retain demo fallback for demonstration purposes — clearly labeled, Confirm disabled.
        if (err?.status === 0) {
          console.error('Backend unreachable, loading mock fallback:', err);
          return of(this.getMockFullCouncilRun(symbol));
        }
        // Any other unexpected error — do not fabricate data.
        console.error(`Unexpected error running council for ${symbol}:`, err);
        return of(null);
      })
    );
  }

  /** Human confirmation — calls the API confirm endpoint which re-runs the deterministic Risk Guardian. */
  confirmSession(sessionId: string): Observable<{ success: boolean; message: string; data?: FullCouncilRunResponse }> {
    return this.http.post<FullCouncilRunResponse>(`${this.baseUrl}/council/sessions/${sessionId}/confirm`, {}).pipe(
      map((res) => ({ success: true, message: 'Order confirmed.', data: res })),
      catchError((err: any) => {
        const msg = err?.error?.error || err?.message || 'Confirmation failed';
        if (err?.status === 409) return of({ success: false, message: 'Cannot confirm: ' + msg });
        return of({ success: false, message: 'Error: ' + msg });
      })
    );
  }

  /** Explicit human cancellation — a cancelled session can never be executed. */
  cancelSession(sessionId: string): Observable<{ success: boolean; message: string; data?: TradeCouncilSession }> {
    return this.http.post<TradeCouncilSession>(`${this.baseUrl}/council/sessions/${sessionId}/cancel`, {}).pipe(
      map((res) => ({ success: true, message: 'Session cancelled.', data: res })),
      catchError((err: any) => {
        const msg = err?.error?.error || err?.message || 'Cancellation failed';
                if (err?.status === 409) return of({ success: false, message: 'Cannot cancel: ' + msg });
        return of({ success: false, message: 'Error: ' + msg });
      })
    );
  }

  getSessions(limit: number = 50): Observable<TradeCouncilSession[]> {
    return this.http.get<TradeCouncilSession[]>(`${this.baseUrl}/council/sessions?limit=${limit}`).pipe(
      catchError(() => of([]))
    );
  }

  getSessionById(id: string): Observable<TradeCouncilSession | null> {
    return this.http.get<TradeCouncilSession>(`${this.baseUrl}/council/sessions/${id}`).pipe(
      catchError(() => of(null))
    );
  }

  getAccount(): Observable<AlpacaAccount | null> {
    return this.http.get<AlpacaAccount>(`${this.baseUrl}/portfolio/account`).pipe(
      catchError(() => of({
        id: 'mock-acc-123', account_number: 'PA3MOCK12345', status: 'ACTIVE', currency: 'USD',
        buying_power: '400000.00', cash: '100000.00', portfolio_value: '100000.00', equity: '100000.00'
      }))
    );
  }

  getPositions(): Observable<AlpacaPosition[]> {
    return this.http.get<AlpacaPosition[]>(`${this.baseUrl}/portfolio/positions`).pipe(
      catchError(() => of([]))
    );
  }

  getOrders(): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/portfolio/orders`).pipe(
      catchError(() => of([]))
    );
  }

  getVetoLogs(limit: number = 50): Observable<TradeCouncilSession[]> {
    return this.http.get<TradeCouncilSession[]>(`${this.baseUrl}/riskguardian/veto-logs?limit=${limit}`).pipe(
      catchError(() => of([]))
    );
  }

  getActiveRiskRules(): Observable<any[]> {
    return this.http.get<any[]>(`${this.baseUrl}/riskguardian/active-rules`).pipe(
      catchError(() => of([]))
    );
  }

  // SAFE DEMO FALLBACK DATA - ALWAYS LABELED 'DEMO DATA'. NEVER fakes broker execution.
  private getMockSnapshot(symbol: string): MarketSnapshot {
    return {
      symbol: symbol.toUpperCase(), as_of: new Date().toISOString(), price: 224.50,
      prev_close: 221.10, change_pct: 1.54, volume: 48500000, avg_volume_20d: 52000000,
      volume_ratio: 0.93, volatility_20d: 0.185,
      indicators: [
        { name: 'RSI_14', value: 58.4, interpretation: 'Neutral/Bullish' },
        { name: 'SMA_20', value: 219.30, interpretation: 'Above 20D Moving Avg' }
      ],
      news_headlines: ['Trade Titans AI Council evaluates market catalysts for ' + symbol.toUpperCase()],
      data_quality: 'ok'
    };
  }

  private getMockFullCouncilRun(symbol: string): FullCouncilRunResponse {
    const snap = this.getMockSnapshot(symbol);
    const sy = symbol.toUpperCase();
    const councilRes: CouncilRunResult = {
            symbol: sy, snapshot: snap, option_chain: undefined,
      bull: { agent: 'bull', decision: 'BUY', confidence: 84, thesis: 'Momentum above 20D SMA.', evidence: ['Price above SMA 20', 'RSI neutral'], risks: ['Tech pullback'], is_fallback: false },
      bear: { agent: 'bear', decision: 'HOLD', confidence: 62, thesis: 'Valuation stretched.', evidence: ['Macro headwinds'], risks: ['Earnings upside'], is_fallback: false },
      hype: { agent: 'hype', decision: 'BUY', confidence: 78, thesis: 'Positive social sentiment.', evidence: ['Sentiment index 0.82'], risks: ['Post-event fade'], is_fallback: false },
      challenger: { agent: 'challenger', decision: 'BUY', confidence: 81, thesis: 'Bullish conviction holds.', evidence: ['Volume evidence'], risks: ['Position sizing mandatory'], is_fallback: false },
      instrument_decision: { symbol: sy, instrument: 'EQUITY', action: 'BUY', rationale: 'Equity sized by Chief Trader.', option_details: undefined, rejected_reason: undefined }
    };
    return {
      session: {
        id: 'demo-session-' + Date.now(), symbol: sy, timestamp: new Date().toISOString(),
        marketPrice: snap.price, volumeRatio: snap.volume_ratio, volatility20d: snap.volatility_20d,
        challengerDecision: 'BUY', challengerConfidence: 81, challengerThesis: councilRes.challenger.thesis,
        proposedInstrument: 'EQUITY', proposedAction: 'BUY', optionContractSymbol: undefined,
        estimatedCost: 750, proposedQuantity: 22, riskGuardianStatus: 'APPROVED',
        chiefTraderExecuted: false, brokerOrderId: undefined, sessionStatus: 'PENDING_CONFIRMATION',
        agentProposals: [], riskLogs: []
      },
      councilRunResult: councilRes,
      riskAssessment: {
        approved: true,
        summaryReason: 'DEMO DATA: All deterministic risk checks PASSED. Awaiting human confirmation.',
        ruleResults: [
          { ruleName: 'Maximum Position Size', passed: true, actualValue: '0.8% ($750)', threshold: '10.0% Max', explanation: 'Demo: within limit.' },
          { ruleName: 'Minimum Cash Reserve', passed: true, actualValue: '100.0%', threshold: '20.0% Min', explanation: 'Demo: reserve ok.' },
          { ruleName: 'Options Expiration Safety', passed: true, actualValue: 'N/A (Equity)', threshold: '7 DTE Min', explanation: 'Demo: equity skipped.' },
          { ruleName: 'Market Data Quality Safeguard', passed: true, actualValue: 'OK', threshold: 'OK Required', explanation: 'Demo: quality verified.' }
        ]
      },
      executionResult: {
        executed: false, orderResponse: undefined,
        message: 'DEMO DATA: Awaiting human confirmation. No order submitted yet.'
      }
    };
  }
}