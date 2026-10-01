// docs/api.md と一致させること。v2 形式 (docs/redesign-v2.md)。
import type { ModsV2 } from './contrib';
export type { ModsV2 } from './contrib';

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
  damage_dealt: number;               // 敵 HP に通ったダメ
  effective_damage_dealt: number;     // 敵 HP + 敵 block 削り（= 有効ダメージ）
  overkill_damage: number;            // HP を超えた分
  damage_received: number;
  effective_block: number;            // 自分の block が実際に吸収した分（= 有効シールド）
  block_gained_self: number;          // 総獲得ブロック量
  block_given_allies: number;
  energy_used: number;
  cards_played: number;
  cards_drawn: number;
  cards: CardStats[];
}

export interface PlayerCombatSummary {
  damage_dealt: number;
  effective_damage_dealt: number;
  overkill_damage: number;
  damage_received: number;
  effective_block: number;
  block_gained_self: number;
  block_given_allies: number;
  energy_used: number;
  cards_played: number;
  cards_drawn: number;
  potions_used: number;
  max_single_hit: number;
  max_single_hit_card?: string | null;     // max_single_hit を出したカード名
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
 * docs/api.md POST /sessions/{id}/events の各要素 + サーバ付与の received_at。
 * 戦闘内 event は combat_index / turn_number / sequence を持ち、
 * 戦闘外 event はそれらが null。
 */
export interface EventRecord<P = unknown> {
  event_uuid: string;
  event_type: string;
  occurred_at: string;
  received_at?: string;
  player_id?: string | null;
  floor?: number;
  combat_index?: number;
  turn_number?: number;
  sequence?: number;
  payload: P;
}

// === ラン全体ビュー用 events (v2: docs/api.md「floor_snapshot」) ==========

export type RoomTypeName = 'Monster' | 'Elite' | 'Boss' | 'Event' | 'Shop' | 'RestSite' | 'Treasure' | string;

export interface SnapshotCard {
  id: string;
  name: string;
  rarity: string;
  type?: string;
  upgrade_level?: number;
  enchantment_id?: string | null;
}

export interface SnapshotModel {
  id: string;
  name: string;
  rarity?: string;
  was_picked?: boolean | null;
}

export interface FloorSnapshotPlayer {
  player_id: string;
  hp:   { current: number; max: number; damage_taken: number; healed: number; max_gained: number; max_lost: number };
  gold: { current: number; gained: number; spent: number; lost: number; stolen: number };
  cards_gained:      SnapshotCard[];
  cards_removed:     SnapshotCard[];
  cards_transformed: { from: SnapshotCard; to: SnapshotCard }[];
  cards_upgraded:    SnapshotCard[];
  cards_downgraded:  SnapshotCard[];
  cards_enchanted:   { card: SnapshotCard; enchantment_id: string | null; enchantment_name: string }[];
  card_choices:      { card: SnapshotCard; was_picked: boolean }[];
  relic_choices:     SnapshotModel[];
  potion_choices:    SnapshotModel[];
  potions_used:      SnapshotModel[];
  potions_discarded: SnapshotModel[];
  relics_removed:    SnapshotModel[];
  event_choices:     string[];
  ancient_choices:   { title: string; was_chosen: boolean }[];
  rest_site_choices: string[];
  bought:            { relics: SnapshotModel[]; potions: SnapshotModel[]; colorless: SnapshotCard[] };
  completed_quests:  SnapshotModel[];
}

export interface FloorSnapshotPayload {
  floor: number;
  act_index: number;
  is_final: boolean;
  map_point_type?: string;
  rooms: { room_type: RoomTypeName; model_id?: string | null; model_name?: string; monster_ids: string[]; turns_taken: number }[];
  players: FloorSnapshotPlayer[];
}

export interface ItemPurchasedPayload {
  item_kind: string;
  card_id?: string;
  card_name?: string;
  card_rarity?: string;
  is_upgraded?: boolean;
  relic_id?: string;
  relic_name?: string;
  potion_id?: string;
  potion_name?: string;
  gold_spent: number;
}

// =============================================================

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
  game_mode?: string;
  player_name?: string;
  hp?: number;            // ラン開始時点 (1 階に入ったときの値)
  max_hp?: number;
  gold?: number;
}

export interface RunEndPayload {
  outcome: 'victory' | 'death' | 'abandoned';
  final_floor: number;
  final_hp?: Record<string, number>;   // player_id → ラン終了処理の直前の HP
}

export interface PowerSnapshot {
  power_id: string;
  power_name?: string | null;
  stacks: number;
  applier: string | null;                                  // 後方互換: 最大 stacks の applier
  appliers?: { player_id: string; stacks: number }[];      // stacks 加重帰属用
}

export interface DamageModification {
  pre: number;
  post: number;
  modifier_types: string[];           // 例: "VulnerablePower" / "WeakPower" / "PenNibRelic"
  modifier_ids: string[];             // 例: "VULNERABLE_POWER" or relic id（取れない場合は ""）
}

export interface DamageDealtPayload {
  amount: number;                     // 敵 HP に通った分
  total_damage?: number;              // 試行された総ダメ（block 吸収前）
  blocked_damage?: number;            // 敵 block で吸収された分
  overkill_damage?: number;           // HP を超えた分
  was_target_killed?: boolean;
  is_doom_kill?: boolean;             // Doom による撃破 (source_card_id = DOOM_POWER)
  // 相手に付けたデバフ (毒・Doom 等) によるダメージの付与者ごとのスタック数。全欄でこの比で按分する (spec combat-stats.md §4)
  source_appliers?: { player_id: string; stacks: number }[];
  target_creature_id: string | null;
  target_player_id?: string | null;
  source_card_id?: string | null;
  source_card_name?: string | null;
  source_card_type?: string | null;
  source_kind?: string;               // 'card' | 'power' | 'relic' | 'orb' | 'enchantment' | 'potion' | 'unknown'
  active_on_target: PowerSnapshot[];
  active_on_dealer: PowerSnapshot[];
  modifications?: DamageModification[] | ModsV2;  // v2: 補正 1 つずつ (ModsV2)。配列は旧データ
}

export interface DamageReceivedPayload {
  amount: number;                     // 自分が HP に受けた分
  total_damage?: number;              // 試行された総ダメ
  blocked_damage?: number;            // 自分の block で吸収した分（= 有効シールド）
  source_creature_id: string | null;
  source_card_id?: string | null;
  active_on_target: PowerSnapshot[];
  active_on_dealer?: PowerSnapshot[]; // dealer (敵) に乗っていた power（rMit で WEAK 等を見る）
  modifications?: ModsV2;              // 被ダメ側の補正 1 つずつ (rMit)
  block_sources?: { player_id: string; amount: number }[];   // 防いだブロックを付けた人ごとに
}

export interface BlockGainedPayload {
  amount: number;
  source_card_id?: string | null;
  source_card_name?: string | null;
  source_card_type?: string | null;   // "Attack" / "Skill" / "Power" / "Orb" 等
  source_kind?: string;
  from_player?: string;
}

export interface PowerChangedPayload {
  power_id: string;
  power_name?: string | null;
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
