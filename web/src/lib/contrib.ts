/**
 * ダメージ補正の内訳 (modifications v2) から、補正ごとの寄与を求める (spec combat-stats.md §3.5)。
 * rDPS (rdps.ts) と rMit (rmit.ts) の両方がこれを使う。
 *
 *   足し算 (additive / enchant / cap / other / hp_lost): 記録された増減量そのまま
 *   掛け算 (multiplicative): 対数で按分。足し算の後を A、掛け算の後を M として
 *     寄与_i = L(M, A) × ln f_i、L(M, A) = (M − A) / ln(M / A) (M = A なら A)
 *     → 寄与の合計は必ず M − A。掛ける順番に依らない。
 */

export interface ModStep {
  phase: 'enchant' | 'additive' | 'multiplicative' | 'cap' | 'hp_lost' | 'other' | string;
  model_id?: string | null;
  model_name?: string | null;
  kind?: string | null;
  value: number;
  appliers?: { player_id: string; stacks: number }[] | null;
  owner?: string | null;
}

export interface ModsV2 {
  base?: number | null;
  final?: number | null;
  steps: ModStep[];
}

export function isModsV2(m: unknown): m is ModsV2 {
  return !!m && !Array.isArray(m) && typeof m === 'object' && Array.isArray((m as ModsV2).steps);
}

export interface StepContribution { step: ModStep; contrib: number }

export function stepContributions(m: ModsV2): StepContribution[] {
  const out: StepContribution[] = [];
  let running = m.base ?? 0;
  let i = 0;
  const steps = m.steps;
  while (i < steps.length) {
    const s = steps[i];
    if (s.phase !== 'multiplicative') {
      out.push({ step: s, contrib: s.value });
      if (s.phase !== 'hp_lost') running += s.value;
      i++;
      continue;
    }
    // 連続する掛け算をまとめて対数で按分
    const group: ModStep[] = [];
    while (i < steps.length && steps[i].phase === 'multiplicative') group.push(steps[i++]);
    const A = running;
    const M = group.reduce((x, g) => x * g.value, A);
    const logsOk = A > 0 && M > 0 && group.every(g => g.value > 0);
    if (logsOk) {
      const L = Math.abs(M - A) < 1e-9 ? A : (M - A) / Math.log(M / A);
      for (const g of group) out.push({ step: g, contrib: L * Math.log(g.value) });
    } else {
      // 0 倍などで対数が使えないときは、計算順どおりに積む
      let r = A;
      for (const g of group) { const next = r * g.value; out.push({ step: g, contrib: next - r }); r = next; }
    }
    running = M;
  }
  return out;
}

/** 補正の行為者と重み。パワーは付与者のスタック比、レリック等は持ち主。分からなければ空。 */
export function stepActors(s: ModStep): { player_id: string; weight: number }[] {
  if (s.appliers && s.appliers.length > 0) {
    const tot = s.appliers.reduce((x, a) => x + Math.max(0, a.stacks), 0);
    if (tot > 0) return s.appliers.filter(a => a.stacks > 0).map(a => ({ player_id: a.player_id, weight: a.stacks / tot }));
  }
  if (s.kind !== 'power' && s.owner) return [{ player_id: s.owner, weight: 1 }];
  return [];
}

/** 出どころの表示名 (パネル用)。 */
export function stepLabel(s: ModStep): string {
  return s.model_name || s.model_id || '補正';
}

/** 整数 total を、重みの比で配る (端数は大きい順に 1 ずつ。合計は必ず total)。 */
export function allocateInt(total: number, weights: Map<string, number>): Map<string, number> {
  const out = new Map<string, number>();
  const sum = [...weights.values()].reduce((a, b) => a + Math.max(0, b), 0);
  if (total <= 0 || sum <= 0) return out;
  const rows = [...weights.entries()].map(([k, w]) => {
    const raw = total * Math.max(0, w) / sum;
    return { k, n: Math.floor(raw), rem: raw - Math.floor(raw) };
  });
  let left = total - rows.reduce((a, r) => a + r.n, 0);
  for (const r of [...rows].sort((a, b) => b.rem - a.rem)) { if (left <= 0) break; r.n++; left--; }
  for (const r of rows) if (r.n > 0) out.set(r.k, r.n);
  return out;
}
