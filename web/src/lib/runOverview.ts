/**
 * events から「階ごとのサマリ」を導出する (docs/spec/run-overview.md §3、data-sources.md §2.1)。
 *
 * v2: ゲーム自身の階ごとの記録 (floor_snapshot) を、階ごとに最後に受け取った 1 件だけ使う。
 * HP・ゴールド・入手物・選択などの推測や重複除去は行わない。
 */

import type {
  EventRecord,
  FloorSnapshotPayload, FloorSnapshotPlayer, SnapshotCard, SnapshotModel, Badge,
  ItemPurchasedPayload, RunStartPayload, RunEndPayload,
  CombatStartPayload, CombatEndPayload,
} from './types';
import { latestCombatEvents } from './aggregate';

export interface ShopPurchase {
  kind: 'card' | 'relic' | 'potion';
  id: string;
  name: string;
  rarity?: string;
  is_upgraded?: boolean;
  gold_spent?: number;          // ホスト自身の購入だけ分かる (item_purchased)
}

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
  cards_obtained:    { card_id: string; card_name?: string; card_rarity?: string; is_upgraded?: boolean }[];
  relics_obtained:   { relic_id: string; relic_name?: string }[];
  potions_obtained:  { potion_id: string; potion_name?: string }[];
  cards_removed:     { card_id: string; card_name?: string }[];
  cards_upgraded:    { card_id: string; card_name?: string; card_rarity?: string }[];
  cards_enchanted:   { card_id: string; card_name?: string; enchantment_id: string; enchantment_name?: string }[];
  cards_transformed: { from: ChipCard; to: ChipCard }[];
  rest_options:      string[];          // ゲームの OptionId ("SMITH" / "HEAL" 等)
  shop_purchases:    ShopPurchase[];
  event_choices:     { title: string }[];
  // 提示されたカードの選択肢 (選んだ / 選ばなかった)。ゲームの記録は階ごとに 1 本なので 0 か 1 件。
  card_choices:      { picked_card_id: string; choices: { card_id: string; card_name: string; card_rarity?: string; is_upgraded?: boolean; was_picked: boolean }[] }[];
}

export interface ChipCard { card_id: string; card_name?: string; card_rarity?: string; is_upgraded?: boolean }

const chip = (c: SnapshotCard): ChipCard => ({
  card_id: c.id, card_name: c.name || undefined, card_rarity: c.rarity || undefined, is_upgraded: (c.upgrade_level ?? 0) > 0,
});

/**
 * playerId のプレイヤーの視点で階の一覧を作る。
 */
export function buildFloorSummaries(events: EventRecord[], playerId: string): FloorSummary[] {
  // 1. 階ごとに最後の floor_snapshot
  const snapByFloor = new Map<number, FloorSnapshotPayload>();
  const sorted = events.slice().sort((a, b) => (a.occurred_at ?? '').localeCompare(b.occurred_at ?? ''));
  for (const ev of sorted) {
    if (ev.event_type !== 'floor_snapshot') continue;
    const p = ev.payload as FloorSnapshotPayload;
    snapByFloor.set(p.floor, p);
  }
  const floors = [...snapByFloor.keys()].sort((a, b) => a - b);
  if (floors.length === 0) return [];

  // 2. 戦闘 (中断→再開でやり直した戦闘は最後の試行だけ) と、その他の付帯 event
  const combatEvents = latestCombatEvents(events);
  const combatStartByFloor = new Map<number, CombatStartPayload>();
  const combatEndByFloor = new Map<number, CombatEndPayload>();
  for (const ev of combatEvents) {
    if (ev.event_type === 'combat_start') combatStartByFloor.set((ev.payload as CombatStartPayload).combat_index, ev.payload as CombatStartPayload);
    if (ev.event_type === 'combat_end')   combatEndByFloor.set((ev.payload as CombatEndPayload).combat_index, ev.payload as CombatEndPayload);
  }
  const runStart = sorted.find(e => e.event_type === 'run_start' && e.player_id === playerId)?.payload as RunStartPayload | undefined;
  const runEnd = sorted.find(e => e.event_type === 'run_end')?.payload as RunEndPayload | undefined;
  const purchasesByFloor = new Map<number, ItemPurchasedPayload[]>();
  for (const ev of sorted) {
    if (ev.event_type !== 'item_purchased' || ev.player_id !== playerId || ev.floor == null) continue;
    if (!purchasesByFloor.has(ev.floor)) purchasesByFloor.set(ev.floor, []);
    purchasesByFloor.get(ev.floor)!.push(ev.payload as ItemPurchasedPayload);
  }

  const result: FloorSummary[] = [];
  let prev: { hp: number; max: number; gold: number } | null =
    runStart && runStart.hp != null ? { hp: runStart.hp, max: runStart.max_hp ?? 0, gold: runStart.gold ?? 0 } : null;
  const lastFloor = floors[floors.length - 1];

  for (const f of floors) {
    const snap = snapByFloor.get(f)!;
    const me: FloorSnapshotPlayer | undefined = snap.players.find(p => p.player_id === playerId);
    const room = snap.rooms[snap.rooms.length - 1];
    const start = combatStartByFloor.get(f);
    const end = combatEndByFloor.get(f);

    let hpOut = me?.hp.current ?? 0;
    // 勝利・放棄のランの最後の階: ゲームは全員を倒してから記録を確定するので HP は 0。終了直前の HP を使う
    if (f === lastFloor && runEnd && runEnd.outcome !== 'death' && runEnd.final_hp?.[playerId] != null) {
      hpOut = runEnd.final_hp[playerId];
    }
    const sum: FloorSummary = {
      floor: f,
      act_index: snap.act_index,
      room_type: room?.room_type ?? '',
      room_class: snap.map_point_type ?? '',
      encounter_id: start?.encounter_id ?? room?.model_id ?? undefined,
      encounter_name: start?.encounter_name ?? (room?.model_name || undefined),
      combat_index: start ? f : undefined,
      victory: end?.victory,
      hp_in: prev?.hp ?? hpOut,
      max_hp_in: prev?.max ?? (me?.hp.max ?? 0),
      gold_in: prev?.gold ?? (me?.gold.current ?? 0),
      hp_out: hpOut,
      max_hp_out: me?.hp.max ?? 0,
      gold_out: me?.gold.current ?? 0,
      damage_taken: me?.hp.damage_taken ?? 0,
      cards_obtained: [], relics_obtained: [], potions_obtained: [],
      cards_removed: [], cards_upgraded: [], cards_enchanted: [], cards_transformed: [],
      rest_options: [], shop_purchases: [], event_choices: [], card_choices: [],
    };
    prev = { hp: sum.hp_out, max: sum.max_hp_out, gold: sum.gold_out };
    if (!me) { result.push(sum); continue; }

    const gainedCards = me.cards_gained.map(chip);
    const pickedRelics = me.relic_choices.filter(r => r.was_picked);
    const pickedPotions = me.potion_choices.filter(p => p.was_picked);

    if (sum.room_type === 'Shop') {
      // ショップの階で入手したものは全部購入品 (spec §3.5)。値段はホスト自身の購入だけ分かる
      const priced = (purchasesByFloor.get(f) ?? []).slice();
      const takePrice = (key: 'card_id' | 'relic_id' | 'potion_id', id: string): number | undefined => {
        const i = priced.findIndex(p => p[key] === id);
        if (i < 0) return undefined;
        return priced.splice(i, 1)[0].gold_spent;
      };
      for (const c of gainedCards) sum.shop_purchases.push({ kind: 'card', id: c.card_id, name: c.card_name ?? c.card_id, rarity: c.card_rarity, is_upgraded: c.is_upgraded, gold_spent: takePrice('card_id', c.card_id) });
      for (const r of pickedRelics) sum.shop_purchases.push({ kind: 'relic', id: r.id, name: r.name || r.id, gold_spent: takePrice('relic_id', r.id) });
      for (const p of pickedPotions) sum.shop_purchases.push({ kind: 'potion', id: p.id, name: p.name || p.id, gold_spent: takePrice('potion_id', p.id) });
    } else {
      sum.cards_obtained = gainedCards;
      sum.relics_obtained = pickedRelics.map(r => ({ relic_id: r.id, relic_name: r.name || undefined }));
      sum.potions_obtained = pickedPotions.map(p => ({ potion_id: p.id, potion_name: p.name || undefined }));
      const choices = me.card_choices.map(c => ({ ...chip(c.card), card_name: c.card.name || c.card.id, was_picked: c.was_picked }));
      if (choices.length > 0) {
        sum.card_choices.push({ picked_card_id: choices.find(c => c.was_picked)?.card_id ?? '', choices });
      }
    }

    sum.cards_removed = me.cards_removed.map(c => ({ card_id: c.id, card_name: c.name || undefined }));
    sum.cards_upgraded = me.cards_upgraded.map(c => ({ card_id: c.id, card_name: c.name || undefined, card_rarity: c.rarity || undefined }));
    sum.cards_enchanted = me.cards_enchanted.map(e => ({
      card_id: e.card.id, card_name: e.card.name || undefined,
      enchantment_id: e.enchantment_id ?? '', enchantment_name: e.enchantment_name || undefined,
    }));
    sum.cards_transformed = me.cards_transformed.map(t => ({ from: chip(t.from), to: chip(t.to) }));
    sum.rest_options = me.rest_site_choices.slice();
    // ネオウ等の選択 (選んだもの) → イベントの選択肢
    const ancient = me.ancient_choices.filter(a => a.was_chosen).map(a => ({ title: a.title }));
    const eventTitles = me.event_choices.map(t => ({ title: t }));
    sum.event_choices = ancient.length > 0
      ? [...ancient, ...eventTitles.filter(e => !ancient.some(a => a.title === e.title))]
      : eventTitles;

    result.push(sum);
  }
  return result;
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

// === プレイヤーの最終状態 (ラン全体の画面。spec run-overview.md §3.6) ===

export interface PlayerFinalState {
  deck: SnapshotCard[] | null;      // 記録の無い (古い mod の) セッションは null
  relics: SnapshotModel[] | null;
  potions: SnapshotModel[] | null;
  badges: Badge[] | null | 'none';   // run_end がまだ無ければ null、run_end に記録が無い (古い mod) なら 'none'
}

export function buildPlayerFinalState(events: EventRecord[], playerId: string): PlayerFinalState {
  let last: FloorSnapshotPlayer | null = null;
  let lastFloor = -1;
  let lastAt = '';
  let runEnd: RunEndPayload | null = null;
  for (const ev of events) {
    if (ev.event_type === 'run_end') runEnd = ev.payload as RunEndPayload;
    if (ev.event_type !== 'floor_snapshot') continue;
    const p = ev.payload as FloorSnapshotPayload;
    const me = p.players.find(x => x.player_id === playerId);
    if (!me || me.deck == null) continue;
    const at = ev.occurred_at ?? '';
    if (p.floor > lastFloor || (p.floor === lastFloor && at >= lastAt)) { last = me; lastFloor = p.floor; lastAt = at; }
  }
  return {
    deck: last?.deck ?? null,
    relics: last?.relics ?? null,
    potions: last?.potions ?? null,
    badges: runEnd ? (runEnd.badges ? (runEnd.badges[playerId] ?? []) : 'none') : null,
  };
}
