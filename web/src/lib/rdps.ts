/**
 * Skada 風 rDPS（貢献度ダメージ）算出。
 *
 * 各 damage_dealt イベントについて:
 *   - 通常ダメ + Vulnerable on target by 別プレイヤー → 1/3 を applier に
 *   - 間接ダメ source_card_id="(poison)" → 100% を POISON_POWER の applier に
 *   - 間接ダメ source_card_id="(doom)"   → 100% を DOOM_POWER の applier に
 *
 * 各 player の rDPS 内訳:
 *   - self: 自分が dealer のとき自分に残った分
 *   - from_buffs / from_debuffs: 他人が自分の damage に貢献した分（受ける側）
 *   - to_others_via_*: 自分が他人の damage に貢献した分（出す側）
 */

import type { EventRecord, DamageDealtPayload, PowerSnapshot } from './types';

export interface RdpsBreakdown {
  total: number;          // self + from_*
  self: number;
  from: { source: string; applier: string; amount: number }[];   // 他人から貰った分（受領側）
  to:   { source: string; recipient: string; amount: number }[]; // 他人の damage に貢献した分（出す側）
}

export interface RdpsTable {
  byPlayer: Record<string, RdpsBreakdown>;
}

/**
 * 与えられた damage_dealt event 列から rDPS を計算する。
 * 戦闘単位 / ラン単位どちらでも使える（呼び出し側でフィルタしてから渡す）。
 */
export function computeRdps(events: EventRecord[]): RdpsTable {
  const byPlayer: Record<string, RdpsBreakdown> = {};
  const ensure = (pid: string): RdpsBreakdown => {
    if (!byPlayer[pid]) byPlayer[pid] = { total: 0, self: 0, from: [], to: [] };
    return byPlayer[pid];
  };
  const credit = (recipient: string, applier: string, source: string, amount: number) => {
    if (amount <= 0) return;
    if (recipient === applier) {
      ensure(recipient).self += amount;
    } else {
      ensure(recipient).from.push({ source, applier, amount });
      ensure(applier).to.push({ source, recipient, amount });
    }
  };

  for (const ev of events) {
    if (ev.event_type !== 'damage_dealt') continue;
    const p = ev.payload as DamageDealtPayload;
    const dealer = ev.player_id;
    if (!dealer) continue;
    const amount = p.amount ?? 0;
    if (amount <= 0) continue;

    // 1. 間接ダメ: poison / doom の applier に 100%
    if (p.source_card_id === '(poison)') {
      const applier = findApplier(p.active_on_target, 'POISON_POWER');
      credit(applier ?? dealer, applier ?? dealer, 'poison', amount);
      continue;
    }
    if (p.source_card_id === '(doom)') {
      const applier = findApplier(p.active_on_target, 'DOOM_POWER');
      credit(applier ?? dealer, applier ?? dealer, 'doom', amount);
      continue;
    }

    // 2. 通常ダメ: dealer に self、Vulnerable applier が他人なら 1/3 を applier に
    let dealerShare = amount;
    const vulnApplier = findApplier(p.active_on_target, 'VULNERABLE_POWER');
    if (vulnApplier && vulnApplier !== dealer) {
      const vulnContrib = Math.round(amount / 3);   // 1.5 倍効果 → 全 dmg の 1/3 が Vulnerable 由来
      credit(dealer, vulnApplier, 'vulnerable', vulnContrib);
      dealerShare -= vulnContrib;
    }
    credit(dealer, dealer, 'self', dealerShare);
  }

  // total = 自分の素ダメ (self) + 他人にバフ・デバフで貢献した分 (to)
  // (b.from は「他人のバフから受け取った分」で、これは別の dealer の self に
  //  含まれているのでここでは加算しない。表示用の情報として残す)
  for (const pid of Object.keys(byPlayer)) {
    const b = byPlayer[pid];
    b.total = b.self + b.to.reduce((s, x) => s + x.amount, 0);
  }
  return { byPlayer };
}

function findApplier(snapshot: PowerSnapshot[] | undefined, powerId: string): string | null {
  if (!snapshot) return null;
  const m = snapshot.find(s => s.power_id === powerId);
  return m?.applier ?? null;
}
