/**
 * Phase 3.5: events 列から戦闘単位サマリ・ターン単位サマリを導出する純関数群。
 *
 * 既存の表示コンポーネント（CombatView / AllCombatsView 等）の入力 shape は
 * Phase 2 形式（TurnPayload[]）のままで、ここで events から組み立てる。
 */

import type {
  SessionDoc, EventRecord, TurnPayload, PlayerEntry, CardStats,
  PlayerTurnSummary, PlayerCombatSummary,
  CombatStartPayload, CombatEndPayload,
  DamageDealtPayload, DamageReceivedPayload, BlockGainedPayload,
  PowerChangedPayload, CardPlayedPayload, EnergySpentPayload,
} from './types';

export interface CombatInfo {
  combat_index: number;
  encounter_name: string | null;
  encounter_id: string | null;
  room_type: string | null;
  victory: boolean | null;
  turns: TurnPayload[];           // turn_number 昇順
  finalTurn: TurnPayload;         // 最後のターン（累計の保有者）
}

// === 公開 API ================================================================

export function buildCombatInfos(doc: SessionDoc): CombatInfo[] {
  // 1. combat_start / combat_end からメタ情報を集める
  const startByIdx = new Map<number, CombatStartPayload>();
  const endByIdx   = new Map<number, CombatEndPayload>();
  for (const ev of doc.events) {
    if (ev.event_type === 'combat_start') {
      const p = ev.payload as CombatStartPayload;
      const idx = p?.combat_index ?? ev.combat_index;
      if (idx != null) startByIdx.set(idx, p);
    } else if (ev.event_type === 'combat_end') {
      const p = ev.payload as CombatEndPayload;
      const idx = p?.combat_index ?? ev.combat_index;
      if (idx != null) endByIdx.set(idx, p);
    }
  }

  // 2. combat_index ごとに events をバケット化（戦闘内 event のみ）
  const byCombat = new Map<number, EventRecord[]>();
  for (const ev of doc.events) {
    if (ev.combat_index == null) continue;
    if (!byCombat.has(ev.combat_index)) byCombat.set(ev.combat_index, []);
    byCombat.get(ev.combat_index)!.push(ev);
  }

  // 3. 各 combat について turns を組み立てる
  const playerNameMap = new Map<string, string>(doc.players.map(p => [p.steam_id, p.display_name]));
  const result: CombatInfo[] = [];
  for (const [idx, events] of [...byCombat.entries()].sort((a, b) => a[0] - b[0])) {
    const turns = buildTurnsForCombat(idx, events, playerNameMap);
    const finalTurn = turns[turns.length - 1] ?? emptyTurnPayload(idx, 1);
    const start = startByIdx.get(idx);
    const end   = endByIdx.get(idx);
    result.push({
      combat_index:   idx,
      encounter_id:   start?.encounter_id ?? null,
      encounter_name: start?.encounter_name ?? null,
      room_type:      start?.room_type ?? null,
      victory:        end?.victory ?? null,
      turns,
      finalTurn,
    });
  }
  return result;
}

export interface RunTotals {
  perPlayer: Record<string, PlayerCombatSummary>;
  perCombatPerPlayer: Record<string, Array<{ combat_index: number; combat: PlayerCombatSummary }>>;
}

export function buildRunTotals(combats: CombatInfo[]): RunTotals {
  const perPlayer: Record<string, PlayerCombatSummary> = {};
  const perCombatPerPlayer: Record<string, Array<{ combat_index: number; combat: PlayerCombatSummary }>> = {};

  for (const c of combats) {
    for (const [pid, entry] of Object.entries(c.finalTurn.players)) {
      perCombatPerPlayer[pid] = perCombatPerPlayer[pid] ?? [];
      perCombatPerPlayer[pid].push({ combat_index: c.combat_index, combat: entry.combat });

      if (!perPlayer[pid]) perPlayer[pid] = emptyCombat();
      const cum = perPlayer[pid];
      const e = entry.combat;
      cum.damage_dealt        += e.damage_dealt;
      cum.damage_received     += e.damage_received;
      cum.block_gained_self   += e.block_gained_self;
      cum.block_given_allies  += e.block_given_allies;
      cum.energy_used         += e.energy_used;
      cum.cards_played        += e.cards_played;
      cum.cards_drawn         += e.cards_drawn;
      cum.potions_used        += e.potions_used;
      cum.max_single_hit       = Math.max(cum.max_single_hit, e.max_single_hit);
      for (const [k, v] of Object.entries(e.debuffs_applied))
        cum.debuffs_applied[k] = (cum.debuffs_applied[k] ?? 0) + v;
      cum.card_stats = mergeCardStats(cum.card_stats, e.card_stats);
    }
  }
  return { perPlayer, perCombatPerPlayer };
}

export function mergeCardStats(a: CardStats[], b: CardStats[]): CardStats[] {
  const map = new Map<string, CardStats>();
  for (const c of a) map.set(c.card_id, { ...c, debuffs_applied: { ...c.debuffs_applied } });
  for (const c of b) {
    const e = map.get(c.card_id);
    if (!e) {
      map.set(c.card_id, { ...c, debuffs_applied: { ...c.debuffs_applied } });
    } else {
      e.play_count    += c.play_count;
      e.damage_dealt  += c.damage_dealt;
      e.block_provided += c.block_provided;
      e.max_single_hit = Math.max(e.max_single_hit, c.max_single_hit);
      for (const [k, v] of Object.entries(c.debuffs_applied))
        e.debuffs_applied[k] = (e.debuffs_applied[k] ?? 0) + v;
    }
  }
  return [...map.values()];
}

export function playerName(doc: SessionDoc, pid: string): string {
  const p = doc.players.find(p => p.steam_id === pid);
  return p?.display_name ?? pid;
}

// === 内部 ===================================================================

function buildTurnsForCombat(
  combatIndex: number,
  events: EventRecord[],
  playerNames: Map<string, string>,
): TurnPayload[] {
  // turn_number ごとにイベントを分割（null は戦闘単位 event なのでスキップ）
  const byTurn = new Map<number, EventRecord[]>();
  for (const ev of events) {
    if (ev.turn_number == null) continue;
    if (!byTurn.has(ev.turn_number)) byTurn.set(ev.turn_number, []);
    byTurn.get(ev.turn_number)!.push(ev);
  }
  const turnNums = [...byTurn.keys()].sort((a, b) => a - b);
  if (turnNums.length === 0) return [];

  // 各 player の累計を保持し、ターン境界でスナップショット
  const cumByPlayer = new Map<string, PlayerCombatSummary>();
  const result: TurnPayload[] = [];

  for (const turnNum of turnNums) {
    const turnEvents = byTurn.get(turnNum)!.sort(
      (a, b) => (a.sequence ?? 0) - (b.sequence ?? 0)
    );

    // このターン内の delta を集計
    const turnByPlayer = new Map<string, PlayerTurnSummary>();
    const ensure = (pid: string): { turn: PlayerTurnSummary; cum: PlayerCombatSummary } => {
      let turn = turnByPlayer.get(pid);
      if (!turn) { turn = emptyTurn(); turnByPlayer.set(pid, turn); }
      let cum = cumByPlayer.get(pid);
      if (!cum) { cum = emptyCombat(); cumByPlayer.set(pid, cum); }
      return { turn, cum };
    };

    for (const ev of turnEvents) {
      applyEvent(ev, ensure);
    }

    // この turn の TurnPayload を構築。
    // 既知の player すべて（このターン delta が 0 でも）を含めて、累計を carry-forward。
    const players: Record<string, PlayerEntry> = {};
    for (const pid of cumByPlayer.keys()) {
      const cum = cumByPlayer.get(pid)!;
      const turn = turnByPlayer.get(pid) ?? emptyTurn();
      players[pid] = {
        player_name: playerNames.get(pid) ?? pid,
        turn:        deepCloneTurn(turn),
        combat:      deepCloneCombat(cum),
      };
    }

    result.push({
      combat_index: combatIndex,
      turn_number:  turnNum,
      is_final:     false,
      timestamp:    turnEvents[0]?.occurred_at ?? '',
      players,
    });
  }
  return result;
}

/** 単一 event を該当 player（複数いることも）の turn delta / cum に適用する。 */
function applyEvent(
  ev: EventRecord,
  ensure: (pid: string) => { turn: PlayerTurnSummary; cum: PlayerCombatSummary },
): void {
  const pid = ev.player_id;
  switch (ev.event_type) {
    case 'damage_dealt': {
      if (!pid) return;
      const p = ev.payload as DamageDealtPayload;
      const amt = p.amount ?? 0;
      const { turn, cum } = ensure(pid);
      turn.damage_dealt += amt;
      cum.damage_dealt  += amt;
      cum.max_single_hit = Math.max(cum.max_single_hit, amt);
      if (p.source_card_id) {
        const tCard = upsertCard(turn.cards, p.source_card_id, p.source_card_name ?? p.source_card_id, p.source_card_type ?? '');
        tCard.damage_dealt   += amt;
        tCard.max_single_hit  = Math.max(tCard.max_single_hit, amt);
        const cCard = upsertCard(cum.card_stats, p.source_card_id, p.source_card_name ?? p.source_card_id, p.source_card_type ?? '');
        cCard.damage_dealt   += amt;
        cCard.max_single_hit  = Math.max(cCard.max_single_hit, amt);
      }
      break;
    }
    case 'damage_received': {
      if (!pid) return;
      const amt = (ev.payload as DamageReceivedPayload).amount ?? 0;
      const { turn, cum } = ensure(pid);
      turn.damage_received += amt;
      cum.damage_received  += amt;
      break;
    }
    case 'block_gained': {
      if (!pid) return;
      const p = ev.payload as BlockGainedPayload;
      const amt = p.amount ?? 0;
      // ev.player_id = receiver
      const { turn, cum } = ensure(pid);
      turn.block_gained_self += amt;
      cum.block_gained_self  += amt;
      if (p.source_card_id) {
        const tCard = upsertCard(turn.cards, p.source_card_id, p.source_card_id, '');
        tCard.block_provided += amt;
        const cCard = upsertCard(cum.card_stats, p.source_card_id, p.source_card_id, '');
        cCard.block_provided += amt;
      }
      // 味方付与の場合: giver にも記録
      if (p.from_player && p.from_player !== pid) {
        const g = ensure(p.from_player);
        g.turn.block_given_allies += amt;
        g.cum.block_given_allies  += amt;
        if (p.source_card_id) {
          const gCard = upsertCard(g.cum.card_stats, p.source_card_id, p.source_card_id, '');
          gCard.block_provided += amt;
        }
      }
      break;
    }
    case 'card_played': {
      if (!pid) return;
      const p = ev.payload as CardPlayedPayload;
      const { turn, cum } = ensure(pid);
      turn.cards_played++;
      cum.cards_played++;
      const tCard = upsertCard(turn.cards, p.card_id, p.card_name, p.card_type);
      tCard.play_count++;
      const cCard = upsertCard(cum.card_stats, p.card_id, p.card_name, p.card_type);
      cCard.play_count++;
      break;
    }
    case 'card_drawn': {
      if (!pid) return;
      const { turn, cum } = ensure(pid);
      turn.cards_drawn++;
      cum.cards_drawn++;
      break;
    }
    case 'energy_spent': {
      if (!pid) return;
      const amt = (ev.payload as EnergySpentPayload).amount ?? 0;
      const { turn, cum } = ensure(pid);
      turn.energy_used += amt;
      cum.energy_used  += amt;
      break;
    }
    case 'power_changed': {
      if (!pid) return;
      const p = ev.payload as PowerChangedPayload;
      const delta = p.delta ?? 0;
      if (delta <= 0) break;            // バフ獲得 / デバフ付与の正方向のみ
      // applier 視点: target が enemy creature（プレイヤーでない）→ debuff として記録
      if (p.target_creature_id && !p.target_player_id) {
        const { cum } = ensure(pid);
        cum.debuffs_applied[p.power_id] = (cum.debuffs_applied[p.power_id] ?? 0) + delta;
        if (p.source_card_id) {
          const cCard = upsertCard(cum.card_stats, p.source_card_id, p.source_card_id, '');
          cCard.debuffs_applied[p.power_id] = (cCard.debuffs_applied[p.power_id] ?? 0) + delta;
        }
      }
      break;
    }
    case 'potion_used': {
      if (!pid) return;
      const { cum } = ensure(pid);
      cum.potions_used++;
      break;
    }
  }
}

// === ユーティリティ =========================================================

function upsertCard(arr: CardStats[], id: string, name: string, type: string): CardStats {
  let card = arr.find(c => c.card_id === id);
  if (!card) {
    card = {
      card_id: id, card_name: name || id, card_type: type || '',
      play_count: 0, damage_dealt: 0, block_provided: 0,
      debuffs_applied: {}, max_single_hit: 0,
    };
    arr.push(card);
  } else {
    // 名前・タイプを後から良いものに上書き
    if (name && card.card_name === card.card_id) card.card_name = name;
    if (type && !card.card_type) card.card_type = type;
  }
  return card;
}

function emptyTurn(): PlayerTurnSummary {
  return {
    damage_dealt: 0, damage_received: 0, block_gained_self: 0, block_given_allies: 0,
    energy_used: 0, cards_played: 0, cards_drawn: 0, cards: [],
  };
}

function emptyCombat(): PlayerCombatSummary {
  return {
    damage_dealt: 0, damage_received: 0, block_gained_self: 0, block_given_allies: 0,
    energy_used: 0, cards_played: 0, cards_drawn: 0, potions_used: 0, max_single_hit: 0,
    debuffs_applied: {}, card_stats: [],
  };
}

function emptyTurnPayload(combatIndex: number, turnNumber: number): TurnPayload {
  return { combat_index: combatIndex, turn_number: turnNumber, is_final: false, timestamp: '', players: {} };
}

function deepCloneTurn(t: PlayerTurnSummary): PlayerTurnSummary {
  return { ...t, cards: t.cards.map(c => ({ ...c, debuffs_applied: { ...c.debuffs_applied } })) };
}

function deepCloneCombat(c: PlayerCombatSummary): PlayerCombatSummary {
  return {
    ...c,
    debuffs_applied: { ...c.debuffs_applied },
    card_stats: c.card_stats.map(cs => ({ ...cs, debuffs_applied: { ...cs.debuffs_applied } })),
  };
}
