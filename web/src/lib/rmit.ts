/**
 * rMit (被ダメージ軽減への貢献。spec combat-stats.md「貢献スコア」)。
 *
 * プレイヤーが受けた 1 ヒットについて、防いだ量を行為者に配る。
 *   - 補正の負の寄与 (敵に付いた脱力、自分や味方に付いた霊体・バッファー等) → その補正の行為者
 *   - ブロックで防いだ量 → そのブロックを付けた味方 (block_sources)。自分で張ったブロックは誰の貢献にもしない
 *   - 正の寄与 (敵の筋力等) は誰の貢献にもしない
 */

import type { EventRecord, DamageReceivedPayload } from './types';
import { latestCombatEvents } from './aggregate';
import { stepContributions, stepActors, stepLabel, contribTable, type ContribTable } from './contrib';

export function computeRmit(events: EventRecord[]): ContribTable {
  const { credit, finish } = contribTable();

  for (const ev of latestCombatEvents(events)) {
    if (ev.event_type !== 'damage_received') continue;
    const p = ev.payload as DamageReceivedPayload;
    const recipient = ev.player_id;
    if (!recipient) continue;

    if (p.modifications) {
      const acc = new Map<string, number>();
      for (const { step, contrib } of stepContributions(p.modifications)) {
        if (contrib >= 0) continue;
        for (const a of stepActors(step)) {
          const k = `${a.player_id}\u0000${stepLabel(step)}`;
          acc.set(k, (acc.get(k) ?? 0) + (-contrib) * a.weight);
        }
      }
      for (const [k, v] of acc) {
        const [applier, label] = k.split('\u0000');
        credit(recipient, applier, applier === recipient ? 'self' : label, Math.round(v));
      }
    }
    for (const b of p.block_sources ?? []) {
      if (b.player_id !== recipient) credit(recipient, b.player_id, 'ブロック', b.amount);
    }
  }

  return finish();
}
