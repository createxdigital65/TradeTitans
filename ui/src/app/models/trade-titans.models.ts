export interface Indicator {
  name: string;
  value: number;
  interpretation?: string;
}

export interface MarketSnapshot {
  symbol: string;
  as_of: string;
  price: number;
  prev_close: number;
  change_pct: number;
  volume: number;
  avg_volume_20d: number;
  volume_ratio: number;
  volatility_20d: number;
  indicators?: Indicator[];
  news_headlines?: string[];
  data_quality: string;
}

export interface OptionContract {
  symbol: string;
  underlying: string;
  contract_type: 'CALL' | 'PUT';
  strike: number;
  expiration: string;
  bid: number;
  ask: number;
  last_price?: number;
  open_interest: number;
  implied_volatility?: number;
  days_to_expiration: number;
}

export interface OptionChainSnapshot {
  underlying: string;
  as_of: string;
  calls?: OptionContract[];
  puts?: OptionContract[];
  data_quality: string;
}

export interface AgentOutput {
  agent: string;
  decision: 'BUY' | 'SELL' | 'HOLD' | 'NO_TRADE';
  confidence: number;
  thesis: string;
  evidence?: string[];
  risks?: string[];
  invalidators?: string[];
  questions_for_other_agents?: string[];
  is_fallback: boolean;
  /**
   * Set by the .NET orchestrator: 'derived_consensus' when the challenger fell back with
   * confidence 0 and the displayed value was calculated from the responding agents' votes.
   * Absent = the value is exactly as reported by Python.
   */
  confidence_source?: string;
}

export interface OptionLegDetails {
  contract_type: 'CALL' | 'PUT';
  contract_symbol: string;
  strike: number;
  expiration: string;
  contracts: number;
  estimated_premium_per_contract: number;
  estimated_total_premium: number;
  max_loss: number;
  breakeven_price: number;
  days_to_expiration: number;
}

export interface InstrumentDecision {
  symbol: string;
  instrument: 'EQUITY' | 'OPTION';
  action: 'BUY' | 'SELL' | 'HOLD' | 'NO_TRADE';
  rationale: string;
  option_details?: OptionLegDetails;
  rejected_reason?: string;
}

export interface CouncilRunResult {
  symbol: string;
  snapshot: MarketSnapshot;
  option_chain?: OptionChainSnapshot;
  bull: AgentOutput;
  bear: AgentOutput;
  hype: AgentOutput;
  challenger: AgentOutput;
  instrument_decision?: InstrumentDecision;
}

export interface RiskRuleResult {
  ruleName: string;
  passed: boolean;
  actualValue: string;
  threshold: string;
  explanation: string;
}

export interface RiskGuardianAssessment {
  approved: boolean;
  ruleResults: RiskRuleResult[];
  summaryReason: string;
}

export interface AlpacaOrderResponse {
  id: string;
  client_order_id: string;
  created_at: string;
  symbol: string;
  qty?: string;
  filled_qty: string;
  type: string;
  side: string;
  status: string;
  filled_avg_price?: string;
}

export interface ExecutionResult {
  executed: boolean;
  orderResponse?: AlpacaOrderResponse;
  message: string;
}

export interface FullCouncilRunResponse {
  session: TradeCouncilSession;
  councilRunResult: CouncilRunResult;
  riskAssessment: RiskGuardianAssessment;
  executionResult: ExecutionResult;
}

export interface TradeCouncilSession {
  id: string;
  symbol: string;
  timestamp: string;
  marketPrice: number;
  volumeRatio: number;
  volatility20d: number;
  challengerDecision: string;
  challengerConfidence: number;
  challengerThesis: string;
  proposedInstrument: string;
  proposedAction: string;
  optionContractSymbol?: string;
  estimatedCost: number;
    proposedQuantity?: number;
  sessionStatus: string;
  riskGuardianStatus: string;
  riskGuardianSummary?: string;
  vetoReason?: string;
  chiefTraderExecuted: boolean;
  brokerOrderId?: string;
  executionFailureReason?: string;
  agentProposals?: AgentProposalUi[];
  riskLogs?: RiskCheckLogUi[];
  executedOrder?: ExecutedOrderUi;
}

export interface AgentProposalUi {
  id: string;
  tradeCouncilSessionId: string;
  agentName: string;
  decision: string;
  confidence: number;
  thesis: string;
  evidenceJson: string;
  risksJson: string;
  isFallback: boolean;
}

export interface RiskCheckLogUi {
  id: string;
  tradeCouncilSessionId: string;
  ruleName: string;
  threshold: string;
  actualValue: string;
  passed: boolean;
  details: string;
  /** Evaluation pass that produced this log: INITIAL_EVALUATION | CONFIRMATION_RECHECK */
  phase?: string;
  timestamp: string;
}

export interface ExecutedOrderUi {
  id: string;
  tradeCouncilSessionId: string;
  symbol: string;
  instrument: string;
  side: string;
  quantity: number;
  price: number;
  brokerOrderId: string;
  status: string;
  executedAt: string;
}

export interface AlpacaAccount {
  id: string;
  account_number: string;
  status: string;
  currency: string;
  buying_power: string;
  cash: string;
  portfolio_value: string;
  equity: string;
  long_market_value: string;
  short_market_value: string;
  initial_margin: string;
}

export interface AlpacaPosition {
  asset_id: string;
  symbol: string;
  exchange: string;
  asset_class: string;
  avg_entry_price: string;
  qty: string;
  side: string;
  market_value: string;
  cost_basis: string;
  unrealized_pl: string;
  unrealized_plpc: string;
  current_price: string;
}
