<script lang="ts">
  import type { RdpsTable } from '../lib/rdps';

  interface Props {
    rdps: RdpsTable;
    playerNames: Record<string, string>;
    title?: string;
  }
  let { rdps, playerNames, title = 'ダメージ貢献度 (rDPS)' }: Props = $props();

  let rows = $derived(
    Object.entries(rdps.byPlayer)
      .map(([pid, b]) => ({ pid, ...b }))
      .sort((a, b) => b.total - a.total)
  );
  let max = $derived(rows.reduce((m, r) => Math.max(m, r.total), 0) || 1);

  // 「他人に貢献した分」の小計: バフ/デバフ source ごとに集計し、誰に貢献したかをまとめる
  function toBreakdown(rec: typeof rows[number]): { source: string; amount: number; recipients: string[] }[] {
    const m = new Map<string, { amount: number; recipients: Set<string> }>();
    for (const t of rec.to) {
      if (!m.has(t.source)) m.set(t.source, { amount: 0, recipients: new Set() });
      const e = m.get(t.source)!;
      e.amount += t.amount;
      e.recipients.add(t.recipient);
    }
    return [...m.entries()].map(([source, v]) => ({
      source,
      amount: v.amount,
      recipients: [...v.recipients].map(r => playerNames[r] ?? r),
    }));
  }

  // 「他人から受けた分」の小計（表示参考用、rDPS には含まれない）
  function fromBreakdown(rec: typeof rows[number]): { source: string; amount: number; appliers: string[] }[] {
    const m = new Map<string, { amount: number; appliers: Set<string> }>();
    for (const f of rec.from) {
      if (!m.has(f.source)) m.set(f.source, { amount: 0, appliers: new Set() });
      const e = m.get(f.source)!;
      e.amount += f.amount;
      e.appliers.add(f.applier);
    }
    return [...m.entries()].map(([source, v]) => ({
      source,
      amount: v.amount,
      appliers: [...v.appliers].map(a => playerNames[a] ?? a),
    }));
  }

  function sourceLabel(s: string): string {
    switch (s) {
      case 'vulnerable': return 'Vulnerable';
      case 'poison':     return 'Poison';
      case 'doom':       return 'Doom';
      case 'self':       return '自力';
      default:           return s;
    }
  }

  const help = `基準は「有効ダメージ」= 敵 HP に通った分 + 敵 block を削った分（オーバーキル分は除外）。
各 damage_dealt について、ゲーム内のダメージ修飾（power / relic / カード Enchant）の前後値を観測して、修飾で増えた分を加算者へ帰属させる。複数人が同じデバフを撒いている場合は各人の stacks 比で按分。
・通常ダメージ: ModifyDamage の (pre, post, modifier) を直接観測する方式。Vulnerable / Strength / 倍率変更レリック等が動的に変わっても自動追従。同フェーズに複数 modifier が居る場合は均等分割の近似。
・毒(Poison): ダメージ全量を Poison applier へ stacks 加重で加算。
・ドゥーム(Doom): ダメージ全量を Doom applier へ stacks 加重で加算。
・観測データの無い旧セッション: 「Vulnerable=1.5x 固定」で 1/3 を applier へ加算するフォールバック。`;
</script>

<section class="space-y-3">
  <h3 class="text-xs uppercase tracking-wide text-slate-500 flex items-center gap-1">
    <span>{title}</span>
    <span class="relative inline-flex group">
      <span
        class="inline-flex items-center justify-center w-3.5 h-3.5 rounded-full bg-bg-3 text-slate-400 text-[10px] leading-none cursor-help select-none"
        aria-label={help}
      >?</span>
      <span
        class="pointer-events-none absolute left-0 top-full mt-1 w-80 z-20 bg-bg-0 border border-bg-3 rounded shadow-lg px-2 py-1.5 text-[11px] text-slate-200 normal-case tracking-normal whitespace-pre-line opacity-0 group-hover:opacity-100 transition-opacity"
      >{help}</span>
    </span>
  </h3>

  {#if rows.length === 0}
    <div class="text-slate-500 text-sm">データなし</div>
  {:else}
    <div class="bg-bg-1 border border-bg-3 rounded-lg overflow-hidden">
      <table class="w-full text-sm">
        <thead class="bg-bg-2 text-slate-400 text-xs uppercase">
          <tr>
            <th class="text-left  py-2 px-3">プレイヤー</th>
            <th class="text-right py-2 px-3">rDPS 合計</th>
            <th class="text-right py-2 px-3">自力</th>
            <th class="text-left  py-2 px-3">他人へ貢献（rDPS加算）</th>
            <th class="text-left  py-2 px-3">他人から（参考）</th>
            <th class="text-left  py-2 px-3 w-1/4">バー</th>
          </tr>
        </thead>
        <tbody>
          {#each rows as r (r.pid)}
            {@const toList = toBreakdown(r)}
            {@const fromList = fromBreakdown(r)}
            <tr class="border-t border-bg-3 hover:bg-bg-2 align-top">
              <td class="py-2 px-3 font-medium">{playerNames[r.pid] ?? r.pid}</td>
              <td class="py-2 px-3 text-right tabular text-ok">{r.total}</td>
              <td class="py-2 px-3 text-right tabular text-slate-300">{r.self}</td>
              <td class="py-2 px-3">
                {#if toList.length === 0}
                  <span class="text-slate-500">—</span>
                {:else}
                  <ul class="space-y-0.5">
                    {#each toList as t}
                      <li class="text-xs">
                        <span class="text-ok">+{t.amount}</span>
                        <span class="text-slate-400">{sourceLabel(t.source)}</span>
                        <span class="text-slate-500">→ {t.recipients.join(', ')}</span>
                      </li>
                    {/each}
                  </ul>
                {/if}
              </td>
              <td class="py-2 px-3">
                {#if fromList.length === 0}
                  <span class="text-slate-500">—</span>
                {:else}
                  <ul class="space-y-0.5">
                    {#each fromList as f}
                      <li class="text-xs text-slate-500">
                        +{f.amount} {sourceLabel(f.source)} ← {f.appliers.join(', ')}
                      </li>
                    {/each}
                  </ul>
                {/if}
              </td>
              <td class="py-2 px-3">
                <div class="w-full bg-bg-2 rounded h-2 overflow-hidden">
                  <div class="bg-ok h-full" style:width={`${(r.total / max) * 100}%`}></div>
                </div>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</section>
