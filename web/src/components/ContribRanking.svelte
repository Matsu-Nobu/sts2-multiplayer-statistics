<script lang="ts">
  // 貢献度スコア (spec ui.md「ラン全体」): Act の範囲で絞った戦闘について、合計貢献 = 与ダメ貢献 (rDPS) + 被ダメ軽減貢献 (rMit) の大きい順。
  import { useSession } from '../lib/session';
  import { computeRdps } from '../lib/rdps';
  import { computeRmit } from '../lib/rmit';
  import { playerColor } from '../lib/players';
  import { SCORE_HELP } from '../lib/contribHelp';
  import { actRangeLabel, actRangeOptions, inActRange, type ActRange } from '../lib/actRange';
  import Panel from './ui/Panel.svelte';
  import SegmentedControl from './ui/SegmentedControl.svelte';
  import EmptyState from './ui/EmptyState.svelte';

  const s = useSession();
  let range: ActRange = $state('all');
  let rangeOptions = $derived(actRangeOptions(s.combatActs));
  let events = $derived(s.combatEvents.filter(e => inActRange(s.combatActs, e.combat_index!, range)));
  let rdps = $derived(computeRdps(events));
  let rmit = $derived(computeRmit(events));
  let rows = $derived(s.playerIds.map(pid => {
    const d = rdps.byPlayer[pid]?.total ?? 0;
    const m = rmit.byPlayer[pid]?.total ?? 0;
    return { pid, d, m, total: d + m };
  }).sort((a, b) => b.total - a.total));
  let max = $derived(Math.max(1, ...rows.map(r => r.total)));
  let sum = $derived(rows.reduce((a, r) => a + r.total, 0) || 1);

</script>

<Panel title="貢献度スコア" scope={`全員・${actRangeLabel(range)}`} help={SCORE_HELP}>
  {#snippet actions()}
    {#if rangeOptions.length > 1}
      <SegmentedControl label="範囲" options={rangeOptions} value={range} onChange={(v) => range = v} />
    {/if}
    <div class="hidden sm:flex items-center gap-3 text-xs text-slate-400">
      <span class="inline-flex items-center gap-1"><span class="w-2.5 h-2.5 rounded-sm bg-ok"></span>与ダメ貢献</span>
      <span class="inline-flex items-center gap-1"><span class="w-2.5 h-2.5 rounded-sm bg-accent"></span>被ダメ軽減貢献</span>
    </div>
  {/snippet}
  {#if rows.every(r => r.total === 0)}
    <EmptyState title="貢献の記録がありません" hint="戦闘が終わると表示されます。" />
  {:else}
    <ol class="space-y-3">
      {#each rows as r, i (r.pid)}
        <li class="grid grid-cols-[2rem_1fr] sm:grid-cols-[2.5rem_10rem_1fr_auto] items-center gap-x-3 gap-y-1.5 rounded-md px-2 py-2 {r.pid === s.player && s.playerIds.length > 1 ? 'bg-bg-2/60' : ''}">
          <div class="text-2xl font-bold tabular text-center {i === 0 ? 'text-yellow-300' : 'text-slate-500'}" aria-label={`${i + 1} 位`}>{i + 1}</div>
          <div class="flex items-center gap-2 min-w-0">
            <span class="w-2.5 h-2.5 rounded-full shrink-0" style:background-color={playerColor(s.playerIds, r.pid)}></span>
            <span class="text-base font-semibold text-slate-100 truncate">{s.playerNames[r.pid] ?? r.pid}</span>
          </div>
          <div class="col-start-2 sm:col-start-auto min-w-0">
            <div class="h-3 bg-bg-3 rounded-full overflow-hidden flex" role="img" aria-label={`与ダメ貢献 ${r.d}、被ダメ軽減貢献 ${r.m}`}>
              <div class="h-full bg-ok" style:width={`${(r.d / max) * 100}%`}></div>
              <div class="h-full bg-accent" style:width={`${(r.m / max) * 100}%`}></div>
            </div>
            <div class="mt-1 text-xs text-slate-400 tabular">与ダメ貢献 <span class="text-ok">{r.d}</span> · 被ダメ軽減貢献 <span class="text-accent">{r.m}</span></div>
          </div>
          <div class="col-start-2 sm:col-start-auto sm:text-right tabular">
            <span class="text-2xl font-bold text-slate-100">{r.total}</span>
            <span class="ml-1.5 text-sm text-slate-400">{Math.round((r.total / sum) * 100)}%</span>
          </div>
        </li>
      {/each}
    </ol>
  {/if}
</Panel>
