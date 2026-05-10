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
  // 階で提示された CardReward の選択肢（pick した / skip した両方）。1 階に複数 reward あり得る。
  card_choices:    { picked_card_id: string; choices: { card_id: string; card_name: string; was_picked: boolean }[] }[];
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
  const makeEmpty = (floor: number, partial: Partial<FloorSummary> = {}): FloorSummary => ({
    floor,
    act_index: partial.act_index ?? 0,
    room_type: partial.room_type ?? '',
    room_class: partial.room_class ?? '',
    hp_in: partial.hp_in ?? 0,
    hp_out: partial.hp_out ?? 0,
    max_hp_in: partial.max_hp_in ?? 0,
    max_hp_out: partial.max_hp_out ?? 0,
    gold_in: partial.gold_in ?? 0,
    gold_out: partial.gold_out ?? 0,
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
    card_choices: [],
  });
  for (const ev of events) {
    if (ev.event_type !== 'room_entered') continue;
    const p = ev.payload as RoomEnteredPayload;
    if (byFloor.has(p.floor)) continue;
    orderedFloors.push(p.floor);
    byFloor.set(p.floor, makeEmpty(p.floor, {
      act_index: p.act_index,
      room_type: p.room_type,
      room_class: p.room_class,
      hp_in: p.hp,
      hp_out: p.hp,
      max_hp_in: p.max_hp,
      max_hp_out: p.max_hp,
      gold_in: p.gold,
      gold_out: p.gold,
    }));
  }
  // room_entered が無い floor (= STS2 の Neow/初期フロアは Hook.AfterRoomEntered が
  // 発火しない) を、他 event から floor 番号だけ拾って補完。room_type は空のままだが
  // reward_taken / event_choice 等が紐付いて表示されるようになる。
  for (const ev of events) {
    if (ev.floor == null) continue;
    if (byFloor.has(ev.floor)) continue;
    orderedFloors.push(ev.floor);
    byFloor.set(ev.floor, makeEmpty(ev.floor));
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
        // CardReward の場合、提示された全選択肢（picked + skipped）も記録
        if (p.card_choices && p.card_choices.length > 0) {
          sum.card_choices.push({
            picked_card_id: p.card_id ?? '',
            choices: p.card_choices,
          });
        }
        // potion は AfterPotionProcured 経由で別途記録されるためここでは追加しない
        break;
      }
      case 'item_purchased': {
        const p = ev.payload as ItemPurchasedPayload;
        sum.shop_purchases.push(p);
        if (p.card_id)  sum.cards_obtained.push({ card_id: p.card_id, card_name: p.card_name });
        if (p.relic_id) sum.relics_obtained.push({ relic_id: p.relic_id, relic_name: p.relic_name });
        // potion は AfterPotionProcured 経由で別途記録されるためここでは追加しない
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
        // mod 側の dedup は CardModel instance hashcode 単位なので、同じ logical card に対する
        // 複数 instance (ハンド/master deck/clone 等) が個別に EnchantInternal を呼ぶと素通りする。
        // floor 単位で (card_id, enchantment_id) が同一なら 1 回扱いにする。
        const dup = sum.cards_enchanted.some(
          e => e.card_id === p.card_id && e.enchantment_id === p.enchantment_id,
        );
        if (!dup) sum.cards_enchanted.push(p);
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

  // 各階の hp_out / max_hp_out / gold_out は **その階内で発生した最後の値**
  // (hp_changed / gold_changed) を使う。次階の hp_in は使わない:
  //   - 階間の遷移ヒール / 階開始時 relic 効果等が次階へ寄ってしまうため
  //   - 階内で hp_changed が無ければ hp_out = hp_in (変化なし) として扱う
  // 全 events を occurred_at 順に並び替えて 1 度だけ走査する。
  const sortedAll = events.slice().sort((a, b) => (a.occurred_at ?? '').localeCompare(b.occurred_at ?? ''));

  for (const f of orderedFloors) {
    const sum = byFloor.get(f)!;
    sum.hp_out = sum.hp_in;
    sum.max_hp_out = sum.max_hp_in;
    sum.gold_out = sum.gold_in;
  }
  // hp_changed event は creature 全般 (敵含む) に対して発火する。playerId=null の
  // hp_changed は敵の HP 変動 (敵の cur=262 等) なので player の hp_out には使えない。
  // playerId 付きの hp_changed のみ採用する。
  for (const ev of sortedAll) {
    if (ev.floor == null) continue;
    const sum = byFloor.get(ev.floor);
    if (!sum) continue;
    if (ev.event_type === 'hp_changed' && ev.player_id) {
      if (filterPlayerId != null && ev.player_id !== filterPlayerId) continue;
      const p = ev.payload as HpChangedPayload;
      sum.hp_out = p.current_hp;
      sum.max_hp_out = p.max_hp;
    } else if (ev.event_type === 'gold_changed' && (filterPlayerId == null || ev.player_id === filterPlayerId)) {
      const p = ev.payload as { current_gold: number };
      sum.gold_out = p.current_gold;
    }
  }
  // 休憩所(heal): silent heal で hp_changed が発火しないため、次階 room_entered.hp との
  // 差分から hp_out を導出する。
  const roomByFloor = new Map<number, RoomEnteredPayload>();
  for (const ev of sortedAll) {
    if (ev.event_type !== 'room_entered') continue;
    const p = ev.payload as RoomEnteredPayload;
    if (!roomByFloor.has(p.floor)) roomByFloor.set(p.floor, p);
  }
  for (const ev of sortedAll) {
    if (ev.event_type !== 'rest_action') continue;
    if ((ev.payload as RestActionPayload).option !== 'heal') continue;
    const restFloor = ev.floor;
    if (restFloor == null) continue;
    const sum = byFloor.get(restFloor);
    if (!sum) continue;
    const next = roomByFloor.get(restFloor + 1);
    if (next) {
      sum.hp_out = next.hp;
      sum.max_hp_out = next.max_hp;
    }
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
