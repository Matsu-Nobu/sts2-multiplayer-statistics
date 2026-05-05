// docs/API.md と一致させること。Phase 3.5 形式（events 統合）。

export interface SessionMeta {
  id: string;
  created_at: string;
  host_name: string | null;
  host_steam_id: string | null;
  character_id: string | null;
  ascension: number | null;
  seed: string | null;
  outcome: 'victory' | 'death' | 'abandoned' | null;
  final_floor: number | null;
  finished_at: string | null;
}

export interface PlayerMeta {
  steam_id: string;
  display_name: string;
}

export interface CardStats {
  card_id: string;
  card_name: string;
  card_type: string;
  play_count: number;
  damage_dealt: number;
  block_provided: number;
  debuffs_applied: Record<string, number>;
  max_single_hit: number;
}

export interface PlayerTurnSummary {
  damage_dealt: number;
  damage_received: number;
  block_gained_self: number;
  block_given_allies: number;
  energy_used: number;
  cards_played: number;
  cards_drawn: number;
  cards: CardStats[];
}

export interface PlayerCombatSummary {
  damage_dealt: number;
  damage_received: number;
  block_gained_self: number;
  block_given_allies: number;
  energy_used: number;
  cards_played: number;
  cards_drawn: number;
  potions_used: number;
  max_single_hit: number;
  debuffs_applied: Record<string, number>;
  card_stats: CardStats[];
}

export interface PlayerEntry {
  player_name: string;
  turn: PlayerTurnSummary;
  combat: PlayerCombatSummary;
}

/**
 * 1 ターンぶんのスナップショット（既存表示コンポーネントの入力形式）。
 * Phase 3.5 では events 列から aggregate.ts で導出される。
 */
export interface TurnPayload {
  combat_index: number;
  turn_number: number;
  is_final: boolean;
  timestamp: string;
  players: Record<string, PlayerEntry>;
}

/**
 * docs/API.md POST /sessions/{id}/events の各要素 + サーバ付与の received_at。
 * 戦闘内 event は combat_index / turn_number / sequence を持ち、
 * 戦闘外 event はそれらが null。
 */
export interface EventRecord<P = unknown> {
  event_uuid: string;
  event_type: string;
  occurred_at: string;
  received_at?: string;
  player_id?: string;
  floor?: number;
  combat_index?: number;
  turn_number?: number;
  sequence?: number;
  payload: P;
}

export interface CombatStartPayload {
  combat_index: number;
  encounter_id?: string;
  encounter_name?: string;
  room_type?: 'Monster' | 'Elite' | 'Boss';
}

export interface CombatEndPayload {
  combat_index: number;
  victory: boolean;
}

export interface RunStartPayload {
  character_id: string;
  ascension: number;
  seed: string;
}

export interface RunEndPayload {
  outcome: 'victory' | 'death' | 'abandoned';
  final_floor: number;
}

export interface PowerSnapshot {
  power_id: string;
  stacks: number;
  applier: string | null;
}

export interface DamageDealtPayload {
  amount: number;
  target_creature_id: string | null;
  target_player_id?: string | null;
  source_card_id?: string | null;
  source_card_name?: string | null;
  source_card_type?: string | null;
  hit_index: number;
  active_on_target: PowerSnapshot[];
  active_on_dealer: PowerSnapshot[];
}

export interface DamageReceivedPayload {
  amount: number;
  source_creature_id: string | null;
  source_card_id?: string | null;
  active_on_target: PowerSnapshot[];
}

export interface BlockGainedPayload {
  amount: number;
  source_card_id?: string | null;
  from_player?: string;
}

export interface PowerChangedPayload {
  power_id: string;
  delta: number;
  target_creature_id?: string | null;
  target_player_id?: string | null;
  source_card_id?: string | null;
}

export interface CardPlayedPayload {
  card_id: string;
  card_name: string;
  card_type: string;
  target_creature_id?: string | null;
}

export interface CardDrawnPayload {
  card_id?: string;
  card_name?: string;
  from_hand_draw?: boolean;
}

export interface EnergySpentPayload {
  amount: number;
  source_card_id?: string | null;
}

export interface PotionUsedPayload {
  potion_id?: string | null;
  target_creature_id?: string | null;
}

export interface SessionDoc {
  session: SessionMeta;
  players: PlayerMeta[];
  events: EventRecord[];
}
