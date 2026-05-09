/**
 * events から「階ごとのサマリ」を導出するユーティリティ。
 *
 * 入力: doc.events 全体（戦闘内 + 戦闘外イベント）
 * 出力: FloorSummary[] — 階番号順（room_entered で観測された階のみ）
 *
 * 各階の境界は `room_entered` の floor を使う:
 *   - 階入場時の HP / max_hp / gold は room_entered.payload から取る
 *   - 階内の events は floor フィールドで紐づける
 *   - 退出時 HP は次階の room_entered.hp（最後の階は session.final / run_end から）
 */

import type {
  EventRecord,
  RoomEnteredPayload, HpChangedPayload,
  RestActionPayload, ItemPurchasedPayload, RewardTakenPayload,
  PotionObtainedPayload, CardUpgradedPayload, CardRemovedPayload,
  DamageDealtPayload, DamageReceivedPayload,
  CombatStartPayload, CombatEndPayload,
} from './types';

export interface FloorSummary {
  floor: number;
  act_index: number;
  room_type: string;
  room_class: string;
  encounter_id?: string;
  encounter_name?: string;
  combat_index?: number;
  victory?: boolean;
  hp_in: number;
  hp_out: number;
  max_hp_in: number;
  max_hp_out: number;
  gold_in: number;
  gold_out: number;
  damage_taken: number;
  damage_dealt: number;
  cards_obtained: { card_id: string; card_name?: string }[];
  relics_obtained: { relic_id: string; relic_name?: string }[];
  potions_obtained: { potion_id: string; potion_name?: string }[];
  cards_removed:   { card_id: string; card_name?: string }[];
  cards_upgraded:  { card_id: string; card_name?: string }[];
  cards_enchanted: { card_id: string; card_name?: string; enchantment_id: string; amount: number }[];
  rest_options:    string[];          // "heal" / "smith" 等
  shop_purchases:  ItemPurchasedPayload[];
  event_choices:   { title: string; history_name: string; text_key: string }[];
}

/**
 * 特定プレイヤーの視点で集計する場合は filterPlayerId を指定。
 * - player_id 付き event はその player のものだけ通す（global は常に通す）
 * - hp_in / max_hp_in / gold_in は (a) そのプレイヤーの hp_changed / gold_changed が
 *   一定数あればそれから推定、(b) なければ room_entered.payload の値（= local プレイヤー値）を使う
 */
export function buildFloorSummaries(events: EventRecord[], filterPlayerId?: string): FloorSummary[] {
  // 全イベント（per-player HP 推定に使うため filter 前のものを保持）
  const allEvents = events;
  if (filterPlayerId) {
    events = events.filter(e => !e.player_id || e.player_id === filterPlayerId);
  }
  // room_entered で各階のスケルトンを作る
  const byFloor = new Map<number, FloorSummary>();
  const orderedFloors: number[] = [];
  for (const ev of events) {
    if (ev.event_type !== 'room_entered') continue;
    const p = ev.payload as RoomEnteredPayload;
    if (byFloor.has(p.floor)) continue;
    orderedFloors.push(p.floor);
    byFloor.set(p.floor, {
      floor: p.floor,
      act_index: p.act_index,
      room_type: p.room_type,
      room_class: p.room_class,
      hp_in: p.hp,
      hp_out: p.hp,
      max_hp_in: p.max_hp,
      max_hp_out: p.max_hp,
      gold_in: p.gold,
      gold_out: p.gold,
      damage_taken: 0,
      damage_dealt: 0,
      cards_obtained: [],
      relics_obtained: [],
      potions_obtained: [],
      cards_removed: [],
      cards_upgraded: [],
      cards_enchanted: [],
      rest_options: [],
      shop_purchases: [],
      event_choices: [],
    });
  }
  orderedFloors.sort((a, b) => a - b);

  // combat_start / combat_end を encounter 名と victory に紐づける
  for (const ev of events) {
    if (ev.event_type === 'combat_start' && ev.floor != null) {
      const sum = byFloor.get(ev.floor);
      if (sum) {
        const p = ev.payload as CombatStartPayload;
        sum.encounter_id = p.encounter_id;
        sum.encounter_name = p.encounter_name;
        sum.combat_index = p.combat_index;
      }
    } else if (ev.event_type === 'combat_end' && ev.floor != null) {
      const sum = byFloor.get(ev.floor);
      if (sum) sum.victory = (ev.payload as CombatEndPayload).victory;
    }
  }

  // 階内 event を集計
  for (const ev of events) {
    if (ev.floor == null) continue;
    const sum = byFloor.get(ev.floor);
    if (!sum) continue;

    switch (ev.event_type) {
      case 'damage_dealt': {
        const p = ev.payload as DamageDealtPayload;
        sum.damage_dealt += p.amount ?? 0;
        break;
      }
      case 'damage_received': {
        const p = ev.payload as DamageReceivedPayload;
        sum.damage_taken += p.amount ?? 0;
        break;
      }
      case 'reward_taken': {
        const p = ev.payload as RewardTakenPayload;
        if (p.card_id)  sum.cards_obtained.push({ card_id: p.card_id, card_name: p.card_name });
        if (p.relic_id) sum.relics_obtained.push({ relic_id: p.relic_id, relic_name: p.relic_name });
        if (p.potion_id) sum.potions_obtained.push({ potion_id: p.potion_id, potion_name: p.potion_name });
        break;
      }
      case 'item_purchased': {
        const p = ev.payload as ItemPurchasedPayload;
        sum.shop_purchases.push(p);
        if (p.card_id)  sum.cards_obtained.push({ card_id: p.card_id, card_name: p.card_name });
        if (p.relic_id) sum.relics_obtained.push({ relic_id: p.relic_id, relic_name: p.relic_name });
        if (p.potion_id) sum.potions_obtained.push({ potion_id: p.potion_id, potion_name: p.potion_name });
        break;
      }
      case 'potion_obtained': {
        const p = ev.payload as PotionObtainedPayload;
        if (p.potion_id) sum.potions_obtained.push({ potion_id: p.potion_id, potion_name: p.potion_name });
        break;
      }
      case 'card_upgraded': {
        const p = ev.payload as CardUpgradedPayload;
        sum.cards_upgraded.push({ card_id: p.card_id, card_name: p.card_name });
        break;
      }
      case 'card_removed': {
        const p = ev.payload as CardRemovedPayload;
        sum.cards_removed.push({ card_id: p.card_id, card_name: p.card_name });
        break;
      }
      case 'rest_action': {
        const p = ev.payload as RestActionPayload;
        sum.rest_options.push(p.option);
        break;
      }
      case 'card_enchanted': {
        const p = ev.payload as { card_id: string; card_name: string; enchantment_id: string; amount: number };
        sum.cards_enchanted.push(p);
        break;
      }
      case 'event_choice': {
        const p = ev.payload as { text_key: string; title: string; history_name: string };
        sum.event_choices.push(p);
        break;
      }
    }
  }

  // 選択プレイヤーの hp/gold を hp_changed / gold_changed から推定（room_entered.hp は local 値なので
  // MP 他プレイヤー視点では正しくない）。各階の room_entered の occurred_at 直前の最新値を採用。
  if (filterPlayerId) {
    const hpChanges = allEvents
      .filter(e => e.event_type === 'hp_changed' && e.player_id === filterPlayerId)
      .slice()
      .sort((a, b) => (a.occurred_at ?? '').localeCompare(b.occurred_at ?? ''));
    const goldChanges = allEvents
      .filter(e => e.event_type === 'gold_changed' && e.player_id === filterPlayerId)
      .slice()
      .sort((a, b) => (a.occurred_at ?? '').localeCompare(b.occurred_at ?? ''));
    const roomEvents = allEvents
      .filter(e => e.event_type === 'room_entered')
      .slice()
      .sort((a, b) => (a.occurred_at ?? '').localeCompare(b.occurred_at ?? ''));

    if (hpChanges.length > 0) {
      // 階ごとに room_entered の occurred_at 以前の最新 hp_changed を採用
      let hpIdx = 0; let lastHp = -1; let lastMax = -1;
      for (const re of roomEvents) {
        const reTs = re.occurred_at ?? '';
        // re より前の hp_changed を消化
        while (hpIdx < hpChanges.length && (hpChanges[hpIdx].occurred_at ?? '') <= reTs) {
          const p = hpChanges[hpIdx].payload as HpChangedPayload;
          lastHp = p.current_hp; lastMax = p.max_hp;
          hpIdx++;
        }
        const floor = (re.payload as RoomEnteredPayload).floor;
        const sum = byFloor.get(floor);
        if (sum && lastHp >= 0) {
          sum.hp_in = lastHp;
          sum.max_hp_in = lastMax;
        }
      }
    }
    if (goldChanges.length > 0) {
      let gIdx = 0; let lastG = -1;
      for (const re of roomEvents) {
        const reTs = re.occurred_at ?? '';
        while (gIdx < goldChanges.length && (goldChanges[gIdx].occurred_at ?? '') <= reTs) {
          const p = goldChanges[gIdx].payload as { current_gold: number };
          lastG = p.current_gold; gIdx++;
        }
        const floor = (re.payload as RoomEnteredPayload).floor;
        const sum = byFloor.get(floor);
        if (sum && lastG >= 0) sum.gold_in = lastG;
      }
    }
  }

  // 各階の hp_out / max_hp_out / gold_out を「次階の hp_in」で確定
  // 最後の階は hp_changed の最後の current_hp を使う
  for (let i = 0; i < orderedFloors.length - 1; i++) {
    const cur = byFloor.get(orderedFloors[i])!;
    const next = byFloor.get(orderedFloors[i + 1])!;
    cur.hp_out = next.hp_in;
    cur.max_hp_out = next.max_hp_in;
    cur.gold_out = next.gold_in;
  }
  // 最終階: 直近の hp_changed / gold_changed から推定。
  // events は配列順序が必ずしも時系列保証されないので occurred_at で並び替えてから走査。
  if (orderedFloors.length > 0) {
    const lastFloor = orderedFloors[orderedFloors.length - 1];
    const lastSum = byFloor.get(lastFloor)!;
    let lastHp = lastSum.hp_in;
    let lastMaxHp = lastSum.max_hp_in;
    let lastGold = lastSum.gold_in;
    const lastFloorEvents = events
      .filter(e => e.floor === lastFloor)
      .slice()
      .sort((a, b) => (a.occurred_at ?? '').localeCompare(b.occurred_at ?? ''));
    for (const ev of lastFloorEvents) {
      if (ev.event_type === 'hp_changed') {
        const p = ev.payload as HpChangedPayload;
        lastHp = p.current_hp;
        lastMaxHp = p.max_hp;
      } else if (ev.event_type === 'gold_changed') {
        const p = ev.payload as { current_gold: number };
        lastGold = p.current_gold;
      }
    }
    lastSum.hp_out = lastHp;
    lastSum.max_hp_out = lastMaxHp;
    lastSum.gold_out = lastGold;
  }

  return orderedFloors.map(f => byFloor.get(f)!);
}

export const ROOM_TYPE_VISUAL: Record<string, { emoji: string; label: string; color: string }> = {
  Monster:  { emoji: '⚔️', label: '通常戦闘', color: '#94a3b8' },
  Elite:    { emoji: '💀', label: 'エリート', color: '#fb923c' },
  Boss:     { emoji: '👑', label: 'ボス',     color: '#f87171' },
  Event:    { emoji: '❓', label: 'イベント', color: '#c084fc' },
  Shop:     { emoji: '🏪', label: 'ショップ', color: '#facc15' },
  RestSite: { emoji: '🔥', label: '休憩所',   color: '#4ade80' },
  Treasure: { emoji: '📦', label: '宝箱',     color: '#fbbf24' },
};

export function roomVisual(type: string) {
  return ROOM_TYPE_VISUAL[type] ?? { emoji: '·', label: type, color: '#64748b' };
}
