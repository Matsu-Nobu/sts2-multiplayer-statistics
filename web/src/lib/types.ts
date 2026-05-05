// docs/API.md と一致させること。

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

export interface TurnPayload {
  combat_index: number;
  turn_number: number;
  is_final: boolean;
  timestamp: string;
  players: Record<string, PlayerEntry>;
}

export interface EventRecord<P = unknown> {
  event_uuid: string;
  event_type: string;
  occurred_at: string;
  received_at?: string;
  player_id?: string;
  floor?: number;
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

export interface SessionDoc {
  session: SessionMeta;
  players: PlayerMeta[];
  turns: TurnPayload[];
  events: EventRecord[];
}
