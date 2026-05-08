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
      cum.effective_damage_dealt += e.effective_damage_dealt;
      cum.overkill_damage     += e.overkill_damage;
      cum.damage_received     += e.damage_received;
      cum.effective_block     += e.effective_block;
      cum.block_gained_self   += e.block_gained_self;
      cum.block_given_allies  += e.block_given_allies;
      cum.energy_used         += e.energy_used;
      cum.cards_played        += e.cards_played;
      cum.cards_drawn         += e.cards_drawn;
      cum.potions_used        += e.potions_used;
      if (e.max_single_hit > cum.max_single_hit) {
        cum.max_single_hit = e.max_single_hit;
        cum.max_single_hit_card = e.max_single_hit_card ?? cum.max_single_hit_card;
      }
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

/**
 * events から power_id → ローカライズ済み power_name の lookup を作る。
 * power_changed.payload と damage_*.payload.active_on_{target,dealer} を走査。
 * 同じ id に複数の name がある場合は後勝ち（実運用では同一 session 内では同じ）。
 */
export function buildPowerNames(events: EventRecord[]): Record<string, string> {
  const out: Record<string, string> = {};
  const visit = (id: unknown, name: unknown): void => {
    if (typeof id === 'string' && typeof name === 'string' && name.length > 0) out[id] = name;
  };
  for (const ev of events) {
    const p = ev.payload as Record<string, unknown> | undefined;
    if (!p) continue;
    if (ev.event_type === 'power_changed') visit(p.power_id, p.power_name);
    for (const arr of [p.active_on_target, p.active_on_dealer]) {
      if (!Array.isArray(arr)) continue;
      for (const s of arr) visit((s as any)?.power_id, (s as any)?.power_name);
    }
  }
  return out;
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

    // ★ プレイヤーごとの「現在進行中のカードプレイ」状態。
    //    AfterCardPlayed hook は全ヒット解決後に発火するため、event の sequence 順では
    //      damage_dealt(N) → damage_dealt(N+1) → ... → card_played(N+k)
    //    となる。よって以下のように動かす:
    //      - damage_dealt: 同じ source_card_id が連続している間は一つの play として累積
    //      - card_played:  対応する damage 群の終端なので finalize（card_name/type を上書き）
    //      - source_card_id が「(...)」で始まる合成タグ (poison/doom/lightning 等) は
    //        1 hit = 1 play 扱いにして個別に finalize（DoT を 1 つの play にまとめないため）
    //    これにより max_single_hit は「カード1枚で与えた最大ダメージ」になる
    //    （Whirlwind のような multi-hit カードはヒット合計、Strike 単発はそのダメ）。
    interface CardPlay { card_id: string; card_name: string; card_type: string; damage: number; }
    const inProgress = new Map<string, CardPlay>();

    const finalizeCardPlay = (pid: string, play: CardPlay): void => {
      if (play.damage <= 0) return;
      const { turn, cum } = ensure(pid);
      const tCard = upsertCard(turn.cards, play.card_id, play.card_name, play.card_type);
      tCard.max_single_hit = Math.max(tCard.max_single_hit, play.damage);
      const cCard = upsertCard(cum.card_stats, play.card_id, play.card_name, play.card_type);
      cCard.max_single_hit = Math.max(cCard.max_single_hit, play.damage);
      if (play.damage > cum.max_single_hit) {
        cum.max_single_hit = play.damage;
        cum.max_single_hit_card = play.card_name || play.card_id;
      }
    };

    for (const ev of turnEvents) {
      const pid = ev.player_id;

      if (ev.event_type === 'damage_dealt' && pid) {
        const p = ev.payload as DamageDealtPayload;
        const hpLost = p.amount ?? 0;          // mod 保証で amount = HP loss
        const sid = p.source_card_id ?? '(unknown)';
        const isSynthetic = sid.startsWith('(');

        if (isSynthetic) {
          // poison / doom / lightning 等は 1 hit = 1 play 扱い
          // 既存の in-progress（実カード分）があればここで一旦 finalize する
          const cur = inProgress.get(pid);
          if (cur) { finalizeCardPlay(pid, cur); inProgress.delete(pid); }
          finalizeCardPlay(pid, {
            card_id:   sid,
            card_name: p.source_card_name ?? sid,
            card_type: p.source_card_type ?? '',
            damage:    hpLost,
          });
        } else {
          const cur = inProgress.get(pid);
          if (cur && cur.card_id === sid) {
            cur.damage += hpLost;
          } else {
            if (cur) finalizeCardPlay(pid, cur);
            inProgress.set(pid, {
              card_id:   sid,
              card_name: p.source_card_name ?? sid,
              card_type: p.source_card_type ?? '',
              damage:    hpLost,
            });
          }
        }
      } else if (ev.event_type === 'card_played' && pid) {
        // 対応する damage 群の終端。in-progress が同じ card_id を持っていれば finalize。
        const cur = inProgress.get(pid);
        const p = ev.payload as CardPlayedPayload;
        if (cur && cur.card_id === p.card_id) {
          // card_played の方が name/type が信頼できるので上書き
          cur.card_name = p.card_name;
          cur.card_type = p.card_type;
          finalizeCardPlay(pid, cur);
          inProgress.delete(pid);
        } else if (cur) {
          // 直前の damage 群とこの card_played が一致しない（=damage を出さないカード等）
          // 旧 group を finalize しておく
          finalizeCardPlay(pid, cur);
          inProgress.delete(pid);
        }
      }

      // 通常のカウンタ集計（max_single_hit 以外）
      applyEvent(ev, ensure);
    }
    // ターン終端で残った進行中グループを finalize
    for (const [pid, play] of inProgress.entries()) {
      finalizeCardPlay(pid, play);
    }
    inProgress.clear();

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
      const amt      = p.amount ?? 0;                 // UnblockedDamage = 実際の HP loss（HP でキャップ済）
      const total    = p.total_damage ?? amt;
      const blocked  = p.blocked_damage ?? 0;
      const overkill = p.overkill_damage ?? 0;
      // mod 側で amount = HP loss を保証しているため hpLost = amt そのまま
      const hpLost = amt;
      // 有効与ダメ = HP に通った分 + 敵 block 削り
      const effective = hpLost + blocked;
      const { turn, cum } = ensure(pid);
      turn.damage_dealt += hpLost;
      cum.damage_dealt  += hpLost;
      turn.effective_damage_dealt += effective;
      cum.effective_damage_dealt  += effective;
      turn.overkill_damage += overkill;
      cum.overkill_damage  += overkill;
      // max_single_hit は呼び出し元ループで「カード1プレイあたり」に集計するためここでは触らない
      if (p.source_card_id) {
        const tCard = upsertCard(turn.cards, p.source_card_id, p.source_card_name ?? p.source_card_id, p.source_card_type ?? '');
        tCard.damage_dealt += hpLost;
        const cCard = upsertCard(cum.card_stats, p.source_card_id, p.source_card_name ?? p.source_card_id, p.source_card_type ?? '');
        cCard.damage_dealt += hpLost;
      }
      break;
    }
    case 'damage_received': {
      if (!pid) return;
      const p = ev.payload as DamageReceivedPayload;
      const amt     = p.amount ?? 0;
      const blocked = p.blocked_damage ?? 0;
      const { turn, cum } = ensure(pid);
      turn.damage_received += amt;
      cum.damage_received  += amt;
      turn.effective_block += blocked;
      cum.effective_block  += blocked;
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
        const name = p.source_card_name ?? p.source_card_id;
        const type = p.source_card_type ?? '';
        const tCard = upsertCard(turn.cards, p.source_card_id, name, type);
        tCard.block_provided += amt;
        const cCard = upsertCard(cum.card_stats, p.source_card_id, name, type);
        cCard.block_provided += amt;
      }
      // 味方付与の場合: giver にも記録
      if (p.from_player && p.from_player !== pid) {
        const g = ensure(p.from_player);
        g.turn.block_given_allies += amt;
        g.cum.block_given_allies  += amt;
        if (p.source_card_id) {
          const name = p.source_card_name ?? p.source_card_id;
          const type = p.source_card_type ?? '';
          const gCard = upsertCard(g.cum.card_stats, p.source_card_id, name, type);
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
    damage_dealt: 0, effective_damage_dealt: 0, overkill_damage: 0,
    damage_received: 0, effective_block: 0,
    block_gained_self: 0, block_given_allies: 0,
    energy_used: 0, cards_played: 0, cards_drawn: 0, cards: [],
  };
}

function emptyCombat(): PlayerCombatSummary {
  return {
    damage_dealt: 0, effective_damage_dealt: 0, overkill_damage: 0,
    damage_received: 0, effective_block: 0,
    block_gained_self: 0, block_given_allies: 0,
    energy_used: 0, cards_played: 0, cards_drawn: 0, potions_used: 0,
    max_single_hit: 0, max_single_hit_card: null,
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
