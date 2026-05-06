<script lang="ts">
  import type { CombatInfo, RunTotals } from '../lib/aggregate';
  import type { EventRecord } from '../lib/types';
  import { computeRdps } from '../lib/rdps';
  import { computeRmit } from '../lib/rmit';
  import StatCard from './StatCard.svelte';
  import { STAT_HELP } from '../lib/statHelp';
  import CardTable from './CardTable.svelte';
  import DebuffTable from './DebuffTable.svelte';
  import DamageChart from './DamageChart.svelte';
  import RdpsPanel from './RdpsPanel.svelte';
  import RmitPanel from './RmitPanel.svelte';

  interface Props {
    combats: CombatInfo[];
    totals: RunTotals;
    playerIds: string[];
    playerNames: Record<string, string>;
    powerNames: Record<string, string>;
    allCombatEvents: EventRecord[];   // run 全体の戦闘内 events（rDPS 用）
  }
  let { combats, totals, playerIds, playerNames, powerNames, allCombatEvents }: Props = $props();
  let rdps = $derived(computeRdps(allCombatEvents));
  let rmit = $derived(computeRmit(allCombatEvents));

  let activePlayer: string = $state('');

  $effect.pre(() => {
    if (!playerIds.includes(activePlayer)) {
      activePlayer = playerIds[0] ?? '';
    }
  });

  let total = $derived(activePlayer ? totals.perPlayer[activePlayer] : null);
  let totalTurns = $derived(combats.reduce((s, c) => s + c.turns.length, 0) || 1);

  // プレイヤー間スタッツ比較用の棒グラフ（ラン全体）
  type StatKey = 'effective_damage_dealt' | 'damage_received' | 'effective_block' | 'max_single_hit';
  const STAT_KEYS: { key: StatKey; label: string }[] = [
    { key: 'effective_damage_dealt', label: '有効与ダメージ' },
    { key: 'damage_received',        label: '被ダメージ' },
    { key: 'effective_block',        label: '有効ブロック' },
    { key: 'max_single_hit',         label: '最大カードダメージ' },
  ];

  // プレイヤーごとに固定色
  const PLAYER_COLORS = ['#7aa2f7', '#bb9af7', '#9ece6a', '#e0af68', '#f7768e'];
  let playerColors = $derived(playerIds.map((_, i) => PLAYER_COLORS[i % PLAYER_COLORS.length]));

  function fmtNum(n: number, decimals = 1): string {
    if (Number.isInteger(n)) return n.toString();
    return n.toFixed(decimals);
  }
  function avg(value: number): number {
    return value / totalTurns;
  }

  let perMetricCharts = $derived(STAT_KEYS.map(s => ({
    key: s.key,
    label: s.label,
    chart: {
      labels: playerIds.map(pid => playerNames[pid] ?? pid),
      series: [{
        label: s.label,
        color: playerColors,
        values: playerIds.map(pid => {
          const t = totals.perPlayer[pid];
          return t ? (t[s.key] as number) ?? 0 : 0;
        }),
      }],
    },
  })));
</script>

<div class="space-y-6">

  {#if playerIds.length > 1}
    <div class="flex gap-2">
      {#each playerIds as pid (pid)}
        <button
          type="button"
          class="px-3 py-1 rounded text-sm border {activePlayer === pid ? 'bg-accent text-bg-0 border-accent' : 'bg-bg-2 border-bg-3 text-slate-300 hover:bg-bg-3'}"
          onclick={() => { activePlayer = pid; }}
        >
          {playerNames[pid] ?? pid}
        </button>
      {/each}
    </div>
  {/if}

  <section class="space-y-3">
    <h3 class="text-xs uppercase tracking-wide text-slate-500">ラン全体合計</h3>
    {#if total}
      <div class="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-5 gap-2">
        <StatCard label="有効与ダメージ"   value={total.effective_damage_dealt}  sub={`ターン平均: ${fmtNum(avg(total.effective_damage_dealt))}`} help={STAT_HELP['有効与ダメージ']} />
        <StatCard label="与ダメージ(HP)"   value={total.damage_dealt}            sub={`ターン平均: ${fmtNum(avg(total.damage_dealt))}`} help={STAT_HELP['与ダメージ(HP)']} />
        <StatCard label="オーバーキル"     value={total.overkill_damage}         sub={`ターン平均: ${fmtNum(avg(total.overkill_damage))}`} help={STAT_HELP['オーバーキル']} />
        <StatCard label="被ダメージ"       value={total.damage_received}         sub={`ターン平均: ${fmtNum(avg(total.damage_received))}`} help={STAT_HELP['被ダメージ']} />
        <StatCard label="有効ブロック"     value={total.effective_block}         sub={`ターン平均: ${fmtNum(avg(total.effective_block))}`} help={STAT_HELP['有効ブロック']} />
        <StatCard label="総獲得ブロック"   value={total.block_gained_self}       sub={`ターン平均: ${fmtNum(avg(total.block_gained_self))}`} help={STAT_HELP['総獲得ブロック']} />
        <StatCard label="味方付与ブロック" value={total.block_given_allies}      sub={`ターン平均: ${fmtNum(avg(total.block_given_allies))}`} help={STAT_HELP['味方付与ブロック']} />
        <StatCard label="エナジー使用"     value={total.energy_used}             sub={`ターン平均: ${fmtNum(avg(total.energy_used))}`} help={STAT_HELP['エナジー使用']} />
        <StatCard label="ポーション使用"   value={total.potions_used}            sub={`${combats.length}戦闘`} help={STAT_HELP['ポーション使用']} />
        <StatCard label="最大カードダメージ" value={total.max_single_hit}        sub={total.max_single_hit_card ?? `${totalTurns}ターン`} help={STAT_HELP['最大カードダメージ']} />
      </div>

      <h3 class="text-xs uppercase tracking-wide text-slate-500">プレイヤー比較</h3>
      <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
        {#each perMetricCharts as m (m.key)}
          <DamageChart
            labels={m.chart.labels}
            series={m.chart.series}
            type="bar"
            title={m.label}
          />
        {/each}
      </div>

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-3">
        <CardTable cards={total.card_stats} title="カード別集計（ラン合計）" {powerNames} />
        <DebuffTable debuffs={total.debuffs_applied} title="デバフ付与（ラン合計）" {powerNames} />
      </div>
    {:else}
      <div class="text-slate-500">データなし</div>
    {/if}
  </section>

  <RdpsPanel {rdps} {playerNames} title="ダメージ貢献度 (rDPS) — ラン全体" />
  <RmitPanel {rmit} {playerNames} title="被ダメ軽減貢献度 (rMit) — ラン全体" />

</div>
