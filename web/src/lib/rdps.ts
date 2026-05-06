/**
 * Skada 風 rDPS（ダメージ貢献度）算出。
 *
 * 基準は「有効ダメージ」= 敵 HP に通った分 + 敵 block を削った分（total - overkill）。
 * シールド削りも貢献として扱う。
 *
 * 各 damage_dealt イベントについて:
 *   - 通常ダメ + Vulnerable on target → 1/3 を Vulnerable applier に stacks 加重で配分
 *   - 間接ダメ source_card_id="(poison)" → 100% を POISON_POWER の applier 群に stacks 加重で配分
 *   - 間接ダメ source_card_id="(doom)"   → 100% を DOOM_POWER の applier 群に stacks 加重で配分
 *
 * 複数プレイヤーが同じデバフを撒いた場合、各 applier の stacks 比で按分する。
 * stacks 情報が無い旧 payload では `applier` 単独に全額を帰属（後方互換）。
 */

import type { EventRecord, DamageDealtPayload, PowerSnapshot } from './types';

export interface RdpsBreakdown {
  total: number;          // self + to の合計
  self: number;
  from: { source: string; applier: string; amount: number }[];
  to:   { source: string; recipient: string; amount: number }[];
}

export interface RdpsTable {
  byPlayer: Record<string, RdpsBreakdown>;
}

export function computeRdps(events: EventRecord[]): RdpsTable {
  const byPlayer: Record<string, RdpsBreakdown> = {};
  const ensure = (pid: string): RdpsBreakdown => {
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
    if (ev.event_type !== 'damage_dealt') continue;
    const p = ev.payload as DamageDealtPayload;
    const dealer = ev.player_id;
    if (!dealer) continue;
    // 「有効ダメージ」基準で計算: HP に通った分 + 敵 block を削った分（= total - overkill）
    // 旧 payload (total_damage 無し) では amount にフォールバック
    const amount = p.amount ?? 0;
    const blocked = p.blocked_damage ?? 0;
    const effective = (p.total_damage != null)
      ? Math.max(0, p.total_damage - (p.overkill_damage ?? 0))
      : (amount + blocked);
    if (effective <= 0) continue;

    // 1. 間接ダメ: poison / doom の applier に 100%（stacks 加重で按分）
    if (p.source_card_id === '(poison)') {
      distributeByStacks(p.active_on_target, 'POISON_POWER', effective, dealer, 'poison', credit);
      continue;
    }
    if (p.source_card_id === '(doom)') {
      distributeByStacks(p.active_on_target, 'DOOM_POWER', effective, dealer, 'doom', credit);
      continue;
    }

    // 2. 通常ダメ: dealer に self、Vulnerable があれば 1/3 を applier 群に配分
    const vulnContrib = Math.round(effective / 3);     // VULN は 1.5 倍効果 → 1/3 が VULN 由来
    let dealerShare = effective;
    const vulnSnap = findPower(p.active_on_target, 'VULNERABLE_POWER');
    if (vulnSnap) {
      const distributed = distributeAmongAppliers(vulnSnap, vulnContrib, dealer, 'vulnerable', credit);
      dealerShare -= distributed;
    }
    credit(dealer, dealer, 'self', dealerShare);
  }

  for (const pid of Object.keys(byPlayer)) {
    const b = byPlayer[pid];
    b.total = b.self + b.to.reduce((s, x) => s + x.amount, 0);
  }
  return { byPlayer };
}

function findPower(snap: PowerSnapshot[] | undefined, powerId: string): PowerSnapshot | null {
  if (!snap) return null;
  return snap.find(s => s.power_id === powerId) ?? null;
}

/**
 * power の applier 群に amount を stacks 加重で配分し、`credit` に流す。
 * 戻り値: 実際に配分された合計（rounding により amount と僅差になり得る）。
 * 自分自身が全 stacks を持つ等で fallback できない場合は dealer に self として戻す。
 */
function distributeAmongAppliers(
  snap: PowerSnapshot,
  amount: number,
  dealer: string,
  source: string,
  credit: (recipient: string, applier: string, source: string, amt: number) => void,
): number {
  const appliers = snap.appliers && snap.appliers.length > 0
    ? snap.appliers
    : (snap.applier ? [{ player_id: snap.applier, stacks: snap.stacks }] : []);
  if (appliers.length === 0) return 0;

  const totalStacks = appliers.reduce((s, a) => s + a.stacks, 0);
  if (totalStacks <= 0) return 0;

  // 整数誤差は最後の applier に押し付ける
  let distributed = 0;
  for (let i = 0; i < appliers.length - 1; i++) {
    const share = Math.round(amount * appliers[i].stacks / totalStacks);
    credit(dealer, appliers[i].player_id, source, share);
    distributed += share;
  }
  const last = appliers[appliers.length - 1];
  credit(dealer, last.player_id, source, amount - distributed);
  return amount;
}

/**
 * 間接ダメ用: amount 全額を power の applier 群に「self ダメ」として配分。
 * Poison/Doom は本来 applier の与ダメなので、各 applier に self credit。
 * applier 不明時は dealer に self としてフォールバック。
 */
function distributeByStacks(
  snap: PowerSnapshot[] | undefined,
  powerId: string,
  amount: number,
  dealer: string,
  source: string,
  credit: (recipient: string, applier: string, source: string, amt: number) => void,
): void {
  const power = findPower(snap, powerId);
  const appliers = power?.appliers && power.appliers.length > 0
    ? power.appliers
    : (power?.applier ? [{ player_id: power.applier, stacks: power.stacks }] : []);

  if (appliers.length === 0) {
    credit(dealer, dealer, source, amount);
    return;
  }

  const totalStacks = appliers.reduce((s, a) => s + a.stacks, 0) || 1;
  let distributed = 0;
  for (let i = 0; i < appliers.length - 1; i++) {
    const share = Math.round(amount * appliers[i].stacks / totalStacks);
    credit(appliers[i].player_id, appliers[i].player_id, source, share);
    distributed += share;
  }
  const last = appliers[appliers.length - 1];
  credit(last.player_id, last.player_id, source, amount - distributed);
}
