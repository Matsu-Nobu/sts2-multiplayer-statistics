import type {
  SessionDoc, TurnPayload, PlayerEntry, CardStats, PlayerCombatSummary,
  EventRecord, CombatStartPayload, CombatEndPayload,
} from './types';

export interface CombatInfo {
  combat_index: number;
  encounter_name: string | null;
  encounter_id: string | null;
  room_type: string | null;
  victory: boolean | null;        // null = まだ終わっていない / 不明
  turns: TurnPayload[];           // (combat_index, turn_number) 昇順
  finalTurn: TurnPayload;         // 最後のターン（累計の保有者）
}

export function buildCombatInfos(doc: SessionDoc): CombatInfo[] {
  const byCombat = new Map<number, TurnPayload[]>();
  for (const t of doc.turns) {
    const arr = byCombat.get(t.combat_index) ?? [];
    arr.push(t);
    byCombat.set(t.combat_index, arr);
  }

  // events から combat_start/end を集める
  const startByIdx = new Map<number, CombatStartPayload>();
  const endByIdx = new Map<number, CombatEndPayload>();
  for (const ev of doc.events) {
    if (ev.event_type === 'combat_start') {
      const p = ev.payload as CombatStartPayload;
      if (p?.combat_index != null) startByIdx.set(p.combat_index, p);
    } else if (ev.event_type === 'combat_end') {
      const p = ev.payload as CombatEndPayload;
      if (p?.combat_index != null) endByIdx.set(p.combat_index, p);
    }
  }

  const result: CombatInfo[] = [];
  for (const [idx, turns] of [...byCombat.entries()].sort((a, b) => a[0] - b[0])) {
    turns.sort((a, b) => a.turn_number - b.turn_number);
    const start = startByIdx.get(idx);
    const end = endByIdx.get(idx);
    result.push({
      combat_index: idx,
      encounter_name: start?.encounter_name ?? null,
      encounter_id: start?.encounter_id ?? null,
      room_type: start?.room_type ?? null,
      victory: end?.victory ?? null,
      turns,
      finalTurn: turns[turns.length - 1],
    });
  }
  return result;
}

// 全プレイヤー横断で run 全体の集計（All タブ用）
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
      cum.damage_dealt += e.damage_dealt;
      cum.damage_received += e.damage_received;
      cum.block_gained_self += e.block_gained_self;
      cum.block_given_allies += e.block_given_allies;
      cum.energy_used += e.energy_used;
      cum.cards_played += e.cards_played;
      cum.cards_drawn += e.cards_drawn;
      cum.potions_used += e.potions_used;
      cum.max_single_hit = Math.max(cum.max_single_hit, e.max_single_hit);
      for (const [k, v] of Object.entries(e.debuffs_applied)) cum.debuffs_applied[k] = (cum.debuffs_applied[k] ?? 0) + v;
      cum.card_stats = mergeCardStats(cum.card_stats, e.card_stats);
    }
  }
  return { perPlayer, perCombatPerPlayer };
}

function emptyCombat(): PlayerCombatSummary {
  return {
    damage_dealt: 0, damage_received: 0, block_gained_self: 0, block_given_allies: 0,
    energy_used: 0, cards_played: 0, cards_drawn: 0, potions_used: 0, max_single_hit: 0,
    debuffs_applied: {}, card_stats: [],
  };
}

export function mergeCardStats(a: CardStats[], b: CardStats[]): CardStats[] {
  const map = new Map<string, CardStats>();
  for (const c of a) map.set(c.card_id, { ...c, debuffs_applied: { ...c.debuffs_applied } });
  for (const c of b) {
    const e = map.get(c.card_id);
    if (!e) {
      map.set(c.card_id, { ...c, debuffs_applied: { ...c.debuffs_applied } });
    } else {
      e.play_count += c.play_count;
      e.damage_dealt += c.damage_dealt;
      e.block_provided += c.block_provided;
      e.max_single_hit = Math.max(e.max_single_hit, c.max_single_hit);
      for (const [k, v] of Object.entries(c.debuffs_applied)) e.debuffs_applied[k] = (e.debuffs_applied[k] ?? 0) + v;
    }
  }
  return [...map.values()];
}

export function playerName(doc: SessionDoc, pid: string): string {
  const p = doc.players.find(p => p.steam_id === pid);
  return p?.display_name ?? pid;
}
