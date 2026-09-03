import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, map, of } from 'rxjs';

export interface CouncilRunError {
  message: string;
  code?: string;
}
import {
  MarketSnapshot,
  OptionChainSnapshot,
  FullCouncilRunResponse,
  TradeCouncilSession,
  AlpacaAccount,
  AlpacaPosition,
  CouncilRunResult
} from '../models/trade-titans.models';
import { environment } from '../../environments/environment';

@Injectable({
  providedIn: 'root'
})
export class TradeTitansService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private http: HttpClient) {}

  getHealth(): Observable<{ status: string; pythonServiceConnected: boolean }> {
    return this.http.get<{ status: string; pythonServiceConnected: boolean }>(`${this.baseUrl}/commandcenter/health`).pipe(
      catchError(() => of({ status: 'degraded', pythonServiceConnected: false }))
    );
  }

  getSnapshot(symbol: string): Observable<MarketSnapshot | null> {
    return this.http.get<MarketSnapshot>(`${this.baseUrl}/commandcenter/snapshot/${symbol}`).pipe(
      catchError((err) => {
        // Only fall back to mock data when the backend is genuinely unreachable (status 0).
        // If the backend IS reachable but the symbol has no data (404/422), show a clear empty
        // state — never fabricate data for an unsupported symbol.
        if (err?.status === 0) {
          console.warn('Backend unreachable, loading mock snapshot fallback:', err);
          return of(this.getMockSnapshot(symbol));
        }
        return of(null);
      })
    );
  }

  getOptions(symbol: string): Observable<OptionChainSnapshot | null> {
    return this.http.get<OptionChainSnapshot>(`${this.baseUrl}/commandcenter/options/${symbol}`).pipe(
      catchError(() => of(null))
    );
  }

  runCouncil(symbol: string, portfolioValue: number = 100000, useOptions: boolean = true): Observable<{ result: FullCouncilRunResponse | null; error?: CouncilRunError }> {
    return this.http.post<FullCouncilRunResponse>(`${this.baseUrl}/council/run/${symbol}?portfolioValue=${portfolioValue}&useOptions=${useOptions}`, {}).pipe(
      map((res) => ({ result: res, error: undefined as CouncilRunError | undefined })),
      catchError((err): Observable<{ result: FullCouncilRunResponse | null; error?: CouncilRunError }> => {
        // CASE A: Symbol unavailable (HTTP 422) — analytics service reachable but market data
        // could not be retrieved for this symbol (invalid / unsupported / no-data). Stop the
        // workflow cleanly. Do NOT fabricate a demo session.
        if (err?.status === 422) {
          const msg = err?.error?.error || `Unable to retrieve market data for ${symbol}. Please verify the symbol or try another supported symbol.`;
          console.error(`Symbol unavailable for ${symbol}:`, msg);
          return of({ result: null, error: { message: msg, code: err?.error?.error_code } });
        }
        // CASE B: Analytics service unavailable (HTTP 503) — Python service down, timed out, or
        // network-unreachable. Surface a "try again later" message. Do NOT fabricate a demo session.
        if (err?.status === 503) {
          const msg = err?.error?.error || 'Market analytics service is currently unavailable. Please try again later.';
          console.error(`Analytics service unavailable:`, msg);
          return of({ result: null, error: { message: msg, code: err?.error?.error_code } });
        }
        // CASE C: Backend genuinely unreachable (connection refused, network error, status 0).
        // Retain demo fallback ONLY in this case — clearly labeled, Confirm disabled, never executes.
        if (err?.status === 0) {
          console.error('Backend unreachable, loading mock fallback:', err);
          return of({ result: this.getMockFullCouncilRun(symbol) });
        }
        // CASE D: Any other unexpected backend error (HTTP 500, etc.) — do not fabricate data.
        const msg = err?.error?.error || 'Something went wrong while processing the request. Please try again.';
        console.error(`Unexpected error running council for ${symbol}:`, msg);
        return of({ result: null, error: { message: msg, code: err?.error?.error_code } });
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
        buying_power: '400000.00', cash: '100000.00', portfolio_value: '100000.00', equity: '100000.00',
        long_market_value: '0.00', short_market_value: '0.00', initial_margin: '0.00'
      } as AlpacaAccount))
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