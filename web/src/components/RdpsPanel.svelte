<script lang="ts">
  import type { RdpsTable } from '../lib/rdps';

  interface Props {
    rdps: RdpsTable;
    playerNames: Record<string, string>;
    title?: string;
  }
  let { rdps, playerNames, title = '貢献度ダメージ (rDPS)' }: Props = $props();

  let rows = $derived(
    Object.entries(rdps.byPlayer)
      .map(([pid, b]) => ({ pid, ...b }))
      .sort((a, b) => b.total - a.total)
  );
  let max = $derived(rows.reduce((m, r) => Math.max(m, r.total), 0) || 1);

  // バフ/デバフ別の小計を作る
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
</script>

<section class="space-y-3">
  <h3 class="text-xs uppercase tracking-wide text-slate-500">{title}</h3>

  {#if rows.length === 0}
    <div class="text-slate-500 text-sm">データなし</div>
  {:else}
    <div class="bg-bg-1 border border-bg-3 rounded-lg overflow-hidden">
      <table class="w-full text-sm">
        <thead class="bg-bg-2 text-slate-400 text-xs uppercase">
          <tr>
            <th class="text-left  py-2 px-3">プレイヤー</th>
            <th class="text-right py-2 px-3">合計</th>
            <th class="text-right py-2 px-3">自力</th>
            <th class="text-left  py-2 px-3">他人から（バフ・デバフ）</th>
            <th class="text-left  py-2 px-3">他人へ貢献</th>
            <th class="text-left  py-2 px-3 w-1/4">バー</th>
          </tr>
        </thead>
        <tbody>
          {#each rows as r (r.pid)}
            {@const fromList = fromBreakdown(r)}
            <tr class="border-t border-bg-3 hover:bg-bg-2 align-top">
              <td class="py-2 px-3 font-medium">{playerNames[r.pid] ?? r.pid}</td>
              <td class="py-2 px-3 text-right tabular text-ok">{r.total}</td>
              <td class="py-2 px-3 text-right tabular text-slate-300">{r.self}</td>
              <td class="py-2 px-3">
                {#if fromList.length === 0}
                  <span class="text-slate-500">—</span>
                {:else}
                  <ul class="space-y-0.5">
                    {#each fromList as f}
                      <li class="text-xs">
                        <span class="text-accent">+{f.amount}</span>
                        <span class="text-slate-400">{sourceLabel(f.source)}</span>
                        <span class="text-slate-500">({f.appliers.join(', ')})</span>
                      </li>
                    {/each}
                  </ul>
                {/if}
              </td>
              <td class="py-2 px-3">
                {#if r.to.length === 0}
                  <span class="text-slate-500">—</span>
                {:else}
                  {@const total = r.to.reduce((s, x) => s + x.amount, 0)}
                  <span class="text-xs text-slate-300">{total}</span>
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
    <div class="text-xs text-slate-500">
      Skada 風 v1: 通常ダメージは Vulnerable applier に 1/3 帰属、間接ダメ (poison/doom) は applier に 100% 帰属。
    </div>
  {/if}
</section>
