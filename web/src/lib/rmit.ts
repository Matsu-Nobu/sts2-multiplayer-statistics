/**
 * Skada 風 rMit（被ダメ軽減貢献度）算出。rDPS の鏡像。
 *
 * 各 damage_received について、敵 (dealer) に乗っている軽減系 power の
 * applier 群に「無ければ受けていたであろう追加ダメ」相当を配分。
 *
 *   - WEAK on dealer: counterfactual = total / 0.75 → saved = total / 3
 *     stacks 加重で applier 群に配分
 *   - STRENGTH on dealer:
 *     applier ごとに「自分が下げた量（stacks<0 の自applier貢献）」を独立に評価。
 *     合計 stacks がプラスでも、減らした側は減らした分の貢献として加算する。
 *     例: 元 STR +5 の敵に A が −3 撒いた場合（合計 +2 でもよい）、
 *         A は 1 ヒットあたり 3 ダメ軽減として帰属。
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

    // STRENGTH: 各 applier の負方向貢献を独立に加算。
    // 合計 stacks がプラスでも、個別に「減らした人」は貢献ありとして扱う。
    const str = onDealer.find(s => s.power_id === 'STRENGTH_POWER');
    if (str) {
      const appliers = str.appliers && str.appliers.length > 0
        ? str.appliers
        : (str.applier && str.stacks < 0
            ? [{ player_id: str.applier, stacks: str.stacks }]
            : []);
      for (const a of appliers) {
        if (a.stacks >= 0) continue;        // プラス貢献（強化）は rMit ではなく rDPS マイナス側
        credit(recipient, a.player_id, 'strength_down', -a.stacks);
      }
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
