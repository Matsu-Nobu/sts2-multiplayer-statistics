/**
 * rDPS (与ダメージへの貢献。spec combat-stats.md §3.5)。
 *
 * 1 ヒットの有効ダメージ E (= HP に通った分 + 敵のブロックを削った分) を配る。
 *   - 相手に付けたデバフ (毒・破滅等) によるダメージ (source_appliers あり) → 付与者にスタック比で全量
 *     (与ダメージ集計 aggregate.splitSharedDamage と同じ splitByStacks で按分するので、欄によって数字がずれない)
 *   - それ以外 → 補正 1 つずつの内訳 (modifications) で、基礎ダメージは本人、正の寄与は補正の行為者へ
 *   - modifications が無いヒット (補正計算を通らないダメージ) → 全量を本人
 */

import type { EventRecord, DamageDealtPayload } from './types';
import { latestCombatEvents, splitByStacks, sharedAppliers } from './aggregate';
import { stepContributions, stepActors, stepLabel, allocateInt, contribTable, type ModsV2, type Credit, type ContribTable } from './contrib';

export function computeRdps(events: EventRecord[]): ContribTable {
  const { credit, finish } = contribTable();

  for (const ev of latestCombatEvents(events)) {
    if (ev.event_type !== 'damage_dealt') continue;
    const p = ev.payload as DamageDealtPayload;
    const dealer = ev.player_id;
    if (!dealer) continue;
    // ゲームの DamageResult.TotalDamage は BlockedDamage + UnblockedDamage で、HP を超えた分 (overkill) を含まない
    const effective = (p.amount ?? 0) + (p.blocked_damage ?? 0);
    if (effective <= 0) continue;

    const shared = sharedAppliers(p);
    if (shared) {
      const source = p.is_doom_kill ? 'doom' : (p.source_card_id ?? 'debuff').replace(/_POWER$/i, '').toLowerCase();
      for (const s of splitByStacks(effective, shared)) credit(s.player_id, s.player_id, source, s.share);
    } else if (p.modifications) {
      attribute(p.modifications, dealer, effective, credit);
    } else {
      credit(dealer, dealer, 'self', effective);
    }
  }

  return finish();
}

/**
 * 1 ヒットの有効ダメージ E を、基礎ダメージ (本人) と補正ごとの寄与 (行為者) に配る。
 * 正の寄与は行為者へ (本人が付けたものは本人)、負の寄与は本人の取り分から引く (0 未満にしない)。
 * 最後に全体を E に合わせて縮める → 合計は必ず E。
 */
function attribute(m: ModsV2, dealer: string, effective: number, credit: Credit): void {
  const contribs = stepContributions(m).filter(c => c.step.phase !== 'hp_lost');
  const SELF = `${dealer}\u0000self`;
  const weights = new Map<string, number>();
  const add = (k: string, w: number) => weights.set(k, (weights.get(k) ?? 0) + w);
  const base = m.base ?? ((m.final ?? effective) - contribs.reduce((a, c) => a + c.contrib, 0));
  add(SELF, base);
  for (const { step, contrib } of contribs) {
    const actors = stepActors(step);
    if (contrib > 0 && actors.length > 0) {
      for (const a of actors) add(a.player_id === dealer ? SELF : `${a.player_id}\u0000${stepLabel(step)}`, contrib * a.weight);
    } else {
      add(SELF, contrib);   // 行為者のいない正の寄与 / 負の寄与は本人の取り分で調整
    }
  }
  if ((weights.get(SELF) ?? 0) < 0) weights.set(SELF, 0);
  for (const [k, n] of allocateInt(effective, weights)) {
    const [party, label] = k.split('\u0000');
    credit(dealer, party, party === dealer ? 'self' : label, n);
  }
}
