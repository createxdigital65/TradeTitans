import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TradeTitansService } from './services/trade-titans.service';
import {
  MarketSnapshot,
  FullCouncilRunResponse,
  TradeCouncilSession,
  AlpacaAccount,
  AlpacaPosition
} from './models/trade-titans.models';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.css'
})
export class App implements OnInit {
  activeTab: 'command-center' | 'portfolio' | 'audit-history' = 'command-center';

  // Search & State
  symbolInput: string = 'AAPL';
  selectedSymbol: string = 'AAPL';
  isLoading: boolean = false;
  isExecutingOrder: boolean = false;
  systemStatus: { status: string; pythonServiceConnected: boolean } = { status: 'checking...', pythonServiceConnected: false };

  // Data State
  currentSnapshot: MarketSnapshot | null = null;
  currentCouncilRun: FullCouncilRunResponse | null = null;
  accountInfo: AlpacaAccount | null = null;
  positions: AlpacaPosition[] = [];
  recentSessions: TradeCouncilSession[] = [];
  selectedAuditSession: TradeCouncilSession | null = null;

  // Notification message
  notification: { message: string; type: 'success' | 'danger' | 'warning' } | null = null;

  constructor(private tradeTitansService: TradeTitansService) {}

  ngOnInit(): void {
    this.checkHealth();
    this.loadAccountInfo();
    this.loadMarketSnapshot('AAPL');
    this.loadAuditSessions();
  }

  setTab(tab: 'command-center' | 'portfolio' | 'audit-history'): void {
    this.activeTab = tab;
    if (tab === 'portfolio') {
      this.loadAccountInfo();
      this.loadPositions();
    } else if (tab === 'audit-history') {
      this.loadAuditSessions();
    }
  }

  checkHealth(): void {
    this.tradeTitansService.getHealth().subscribe(health => {
      this.systemStatus = health;
    });
  }

  onSymbolSearch(): void {
    if (!this.symbolInput.trim()) return;
    this.selectedSymbol = this.symbolInput.trim().toUpperCase();
    this.loadMarketSnapshot(this.selectedSymbol);
  }

  selectQuickPick(symbol: string): void {
    this.symbolInput = symbol;
    this.selectedSymbol = symbol;
    this.loadMarketSnapshot(symbol);
  }

  loadMarketSnapshot(symbol: string): void {
    this.isLoading = true;
    this.currentCouncilRun = null;
    this.tradeTitansService.getSnapshot(symbol).subscribe(snapshot => {
      this.currentSnapshot = snapshot;
      this.isLoading = false;
    });
  }

  runCouncilDebate(): void {
    if (!this.selectedSymbol) return;
    this.isLoading = true;
    this.showNotification('Running AI Council debate (Bull -> Bear -> Hype -> Challenger)...', 'warning');

    const portfolioVal = this.accountInfo ? parseFloat(this.accountInfo.portfolio_value) : 100000;
    this.tradeTitansService.runCouncil(this.selectedSymbol, portfolioVal, true).subscribe(result => {
      this.currentCouncilRun = result;
      this.isLoading = false;
      if (result) {
        if (result.riskAssessment.approved) {
          this.showNotification('AI Council finished. Risk Guardian APPROVED trade proposal!', 'success');
        } else {
          this.showNotification('AI Council finished. VETOED BY RISK GUARDIAN!', 'danger');
        }
      }
    });
  }

  confirmExecutePaperTrade(): void {
    if (!this.currentCouncilRun) return;
    const sessionId = this.currentCouncilRun.session?.id;
    if (!sessionId) return;

    // CRITICAL: Confirm calls the API which re-runs Risk Guardian and only
    // then lets Chief Trader submit a paper order. No automatic execution.
    this.isExecutingOrder = true;
    this.showNotification('Chief Trader executing order via Alpaca Paper Trading...', 'warning');

    this.tradeTitansService.confirmSession(sessionId).subscribe(result => {
      this.isExecutingOrder = false;

            if (result.success && result.data) {
        this.currentCouncilRun = result.data;
        this.loadAuditSessions();

        if (result.data.executionResult.executed) {
          this.showNotification(
            `Order executed via Alpaca Paper Trading! Broker Order ID: ${result.data.executionResult.orderResponse?.id || 'N/A'}`,
            'success'
          );
        } else {
          this.showNotification(`Trade confirmed but not executed: ${result.data.executionResult.message}`, 'danger');
        }
      } else {
        this.showNotification(result.message, 'danger');
      }
    });
  }

    cancelPendingTrade(): void {
    if (!this.currentCouncilRun) return;
    const sessionId = this.currentCouncilRun.session?.id;
    if (!sessionId) return;

    this.tradeTitansService.cancelSession(sessionId).subscribe(result => {
      if (result.success && result.data) {
        this.showNotification('Trade session cancelled. No order was submitted.', 'warning');
        this.currentCouncilRun = null;
        this.loadAuditSessions();
      } else {
        this.showNotification(result.message, 'danger');
      }
    });
  }

  loadAccountInfo(): void {
    this.tradeTitansService.getAccount().subscribe(acc => {
      this.accountInfo = acc;
    });
  }

  loadPositions(): void {
    this.tradeTitansService.getPositions().subscribe(pos => {
      this.positions = pos;
    });
  }

  loadAuditSessions(): void {
    this.tradeTitansService.getSessions(20).subscribe(sessions => {
      this.recentSessions = sessions;
    });
  }

  inspectSession(session: TradeCouncilSession): void {
    this.selectedAuditSession = session;
  }

  /** True when the stored session's challenger verdict came from a Python fallback response. */
  isChallengerFallback(session: TradeCouncilSession): boolean {
    return !!session.agentProposals?.some(a => a.agentName === 'challenger' && a.isFallback);
  }

  parseFloat(val: string): number {
    return parseFloat(val || '0');
  }

  showNotification(msg: string, type: 'success' | 'danger' | 'warning'): void {
    this.notification = { message: msg, type: type };
    setTimeout(() => {
      if (this.notification?.message === msg) {
        this.notification = null;
      }
    }, 5000);
  }
}
