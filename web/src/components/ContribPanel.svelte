<script lang="ts">
  // 貢献スコアのパネル (rDPS / rMit 共通。内容は spec combat-stats.md §3.5)。全員の表。
  import type { RdpsTable } from '../lib/rdps';
  import { useSession } from '../lib/session';
  import { playerColor } from '../lib/players';
  import Panel from './ui/Panel.svelte';
  import EmptyState from './ui/EmptyState.svelte';

  interface Props { table: RdpsTable; title: string; help: string; scope?: string }
  let { table, title, help, scope = '全員' }: Props = $props();
  const s = useSession();

  let rows = $derived(Object.entries(table.byPlayer).map(([pid, b]) => ({ pid, ...b })).sort((a, b) => b.total - a.total));
  let max = $derived(rows.reduce((m, r) => Math.max(m, r.total), 0) || 1);

  // 出どころの表示名: ゲーム内のパワー名 (events から) を優先
  const FALLBACK: Record<string, string> = { self: '自力', vulnerable: '弱体', weak: '脱力', strength_down: '筋力低下', poison: '毒' };
  function label(src: string): string {
    return s.powerNames[`${src.toUpperCase()}_POWER`] ?? s.powerNames[src] ?? FALLBACK[src] ?? src;
  }
  const name = (pid: string) => s.playerNames[pid] ?? pid;

  function group<T extends { source: string; amount: number }>(list: T[], who: (x: T) => string) {
    const m = new Map<string, { amount: number; who: Set<string> }>();
    for (const x of list) {
      const e = m.get(x.source) ?? { amount: 0, who: new Set<string>() };
      e.amount += x.amount; e.who.add(name(who(x)));
      m.set(x.source, e);
    }
    return [...m.entries()].map(([src, v]) => ({ src, amount: v.amount, who: [...v.who].join('、') })).sort((a, b) => b.amount - a.amount);
  }
</script>

<Panel {title} {scope} {help} flush>
  {#if rows.length === 0}
    <EmptyState title="データがありません" />
  {:else}
    <div class="dt-wrap">
      <table class="dt">
        <thead>
          <tr><th>プレイヤー</th><th class="num">合計</th><th class="num">自力</th><th>味方への貢献</th><th>味方から受けた分 (参考)</th></tr>
        </thead>
        <tbody>
          {#each rows as r (r.pid)}
            {@const color = playerColor(s.playerIds, r.pid)}
            {@const to = group(r.to, x => x.recipient)}
            {@const from = group(r.from, x => x.applier)}
            <tr class={r.pid === s.player ? 'bg-bg-2/60' : ''}>
              <td class="whitespace-nowrap">
                <span class="inline-flex items-center gap-1.5"><span class="w-2 h-2 rounded-full" style:background-color={color}></span><span class="text-slate-100">{name(r.pid)}</span></span>
              </td>
              <td class="num min-w-[7rem]">
                <div class="text-slate-100 font-semibold">{r.total}</div>
                <div class="mt-1 h-1 bg-bg-3 rounded-full overflow-hidden"><div class="h-full rounded-full" style:width={`${(r.total / max) * 100}%`} style:background-color={color}></div></div>
              </td>
              <td class="num text-slate-300">{r.self}</td>
              <td class="text-xs min-w-[12rem]">
                {#if to.length === 0}<span class="text-slate-600">—</span>{:else}
                  <ul class="space-y-0.5">{#each to as t (t.src)}<li><span class="text-ok tabular">+{t.amount}</span> <span class="text-slate-300">{label(t.src)}</span> <span class="text-slate-500">→ {t.who}</span></li>{/each}</ul>
                {/if}
              </td>
              <td class="text-xs min-w-[12rem] text-slate-500">
                {#if from.length === 0}<span class="text-slate-600">—</span>{:else}
                  <ul class="space-y-0.5">{#each from as f (f.src)}<li><span class="tabular">+{f.amount}</span> {label(f.src)} ← {f.who}</li>{/each}</ul>
                {/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</Panel>
