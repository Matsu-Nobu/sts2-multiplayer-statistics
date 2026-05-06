/**
 * Skada 風 rMit（被ダメ軽減貢献度）算出。rDPS の鏡像。
 *
 * 各 damage_received について、敵 (dealer) に乗っている軽減系 power の
 * applier 群に「無ければ受けていたであろう追加ダメ」相当を stacks 加重で配分。
 *
 *   - WEAK on dealer: counterfactual = total / 0.75 → saved = total / 3
 *   - STRENGTH<0 on dealer: 1 ヒットあたり |stacks| 軽減
 *
 * stacks 情報が無い旧 payload では `applier` 単独に全額帰属（後方互換）。
 */

import type { EventRecord, DamageReceivedPayload, PowerSnapshot } from './types';

export interface RmitBreakdown {
  total: number;
  self: number;
  from: { source: string; applier: string; amount: number }[];
  to:   { source: string; recipient: string; amount: number }[];
}

export interface RmitTable {
  byPlayer: Record<string, RmitBreakdown>;
}

export function computeRmit(events: EventRecord[]): RmitTable {
  const byPlayer: Record<string, RmitBreakdown> = {};
  const ensure = (pid: string): RmitBreakdown => {
    if (!byPlayer[pid]) byPlayer[pid] = { total: 0, self: 0, from: [], to: [] };
    return byPlayer[pid];
  };
  const credit = (recipient: string, applier: string, source: string, amount: number) => {
    if (amount <= 0) return;
    if (recipient === applier) ensure(recipient).self += amount;
    else {
      ensure(recipient).from.push({ source, applier, amount });
      ensure(applier).to.push({ source, recipient, amount });
    }
  };

  for (const ev of events) {
    if (ev.event_type !== 'damage_received') continue;
    const p = ev.payload as DamageReceivedPayload;
    const recipient = ev.player_id;
    if (!recipient) continue;
    const total = p.total_damage ?? p.amount ?? 0;
    if (total <= 0) continue;
    const onDealer = p.active_on_dealer ?? [];

    // WEAK: total/3 を applier 群へ stacks 加重で配分
    const weak = onDealer.find(s => s.power_id === 'WEAK_POWER');
    if (weak) {
      const saved = Math.round(total / 3);
      distributeAmongAppliers(weak, recipient, 'weak', saved, credit);
    }

    // STRENGTH<0: |stacks| を applier 群へ配分
    const str = onDealer.find(s => s.power_id === 'STRENGTH_POWER');
    if (str && str.stacks < 0) {
      const saved = -str.stacks;
      distributeAmongAppliers(str, recipient, 'strength_down', saved, credit);
    }
  }

  for (const pid of Object.keys(byPlayer)) {
    const b = byPlayer[pid];
    b.total = b.self + b.to.reduce((s, x) => s + x.amount, 0);
  }
  return { byPlayer };
}

function distributeAmongAppliers(
  snap: PowerSnapshot,
  recipient: string,
  source: string,
  amount: number,
  credit: (recipient: string, applier: string, source: string, amt: number) => void,
): void {
  if (amount <= 0) return;
  const appliers = snap.appliers && snap.appliers.length > 0
    ? snap.appliers
    : (snap.applier ? [{ player_id: snap.applier, stacks: Math.abs(snap.stacks) }] : []);
  if (appliers.length === 0) return;

  const totalStacks = appliers.reduce((s, a) => s + a.stacks, 0) || 1;
  let distributed = 0;
  for (let i = 0; i < appliers.length - 1; i++) {
    const share = Math.round(amount * appliers[i].stacks / totalStacks);
    credit(recipient, appliers[i].player_id, source, share);
    distributed += share;
  }
  const last = appliers[appliers.length - 1];
  credit(recipient, last.player_id, source, amount - distributed);
}
