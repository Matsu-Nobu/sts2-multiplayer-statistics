<script lang="ts">
  import type { CombatInfo, RunTotals } from '../lib/aggregate';
  import type { EventRecord } from '../lib/types';
  import { computeRdps } from '../lib/rdps';
  import StatCard from './StatCard.svelte';
  import CardTable from './CardTable.svelte';
  import DebuffTable from './DebuffTable.svelte';
  import DamageChart from './DamageChart.svelte';
  import RdpsPanel from './RdpsPanel.svelte';

  interface Props {
    combats: CombatInfo[];
    totals: RunTotals;
    playerIds: string[];
    playerNames: Record<string, string>;
    allCombatEvents: EventRecord[];   // run 全体の戦闘内 events（rDPS 用）
  }
  let { combats, totals, playerIds, playerNames, allCombatEvents }: Props = $props();
  let rdps = $derived(computeRdps(allCombatEvents));

  let activePlayer: string = $state('');

  $effect.pre(() => {
    if (!playerIds.includes(activePlayer)) {
      activePlayer = playerIds[0] ?? '';
    }
  });

  let total = $derived(activePlayer ? totals.perPlayer[activePlayer] : null);
  let totalTurns = $derived(combats.reduce((s, c) => s + c.turns.length, 0) || 1);

  // プレイヤー間スタッツ比較用の棒グラフ（ラン全体）
  type StatKey = 'damage_dealt' | 'damage_received' | 'block_gained_self' | 'block_given_allies' | 'energy_used' | 'cards_played' | 'max_single_hit';
  const STAT_KEYS: { key: StatKey; label: string; perTurn: boolean }[] = [
    { key: 'damage_dealt',       label: '与ダメ',       perTurn: true  },
    { key: 'damage_received',    label: '被ダメ',       perTurn: true  },
    { key: 'block_gained_self',  label: 'ブロック',     perTurn: true  },
    { key: 'block_given_allies', label: '味方ブロック', perTurn: true  },
    { key: 'energy_used',        label: 'エナジー',     perTurn: true  },
    { key: 'cards_played',       label: 'カード使用',   perTurn: true  },
    { key: 'max_single_hit',     label: '最大単発',     perTurn: false },
  ];
  const COLORS = ['#7aa2f7', '#bb9af7', '#9ece6a', '#e0af68', '#f7768e'];

  let mode: 'total' | 'avg' = $state('total');

  function fmtNum(n: number, decimals = 1): string {
    if (Number.isInteger(n)) return n.toString();
    return n.toFixed(decimals);
  }
  function avg(value: number): number {
    return value / totalTurns;
  }

  let chartLabels = $derived(STAT_KEYS.map(s => s.label));
  let chartSeries = $derived(playerIds.map((pid, i) => ({
    label: playerNames[pid] ?? pid,
    color: COLORS[i % COLORS.length],
    values: STAT_KEYS.map(s => {
      const t = totals.perPlayer[pid];
      const raw = t ? (t[s.key] as number) ?? 0 : 0;
      return mode === 'avg' && s.perTurn ? +(raw / totalTurns).toFixed(2) : raw;
    }),
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
      <div class="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-7 gap-2">
        <StatCard label="与ダメージ"     value={total.damage_dealt}       sub={`ターン平均: ${fmtNum(avg(total.damage_dealt))}`} />
        <StatCard label="被ダメージ"     value={total.damage_received}    sub={`ターン平均: ${fmtNum(avg(total.damage_received))}`} />
        <StatCard label="ブロック獲得"   value={total.block_gained_self}  sub={`ターン平均: ${fmtNum(avg(total.block_gained_self))}`} />
        <StatCard label="味方付与ブロック" value={total.block_given_allies} sub={`ターン平均: ${fmtNum(avg(total.block_given_allies))}`} />
        <StatCard label="エナジー使用"   value={total.energy_used}        sub={`ターン平均: ${fmtNum(avg(total.energy_used))}`} />
        <StatCard label="ポーション使用" value={total.potions_used}       sub={`${combats.length}戦闘`} />
        <StatCard label="最大単発"       value={total.max_single_hit}     sub={`${totalTurns}ターン`} />
      </div>

      <div class="flex items-center justify-between">
        <h3 class="text-xs uppercase tracking-wide text-slate-500">プレイヤー比較</h3>
        <div class="flex gap-1 bg-bg-2 border border-bg-3 rounded p-0.5 text-xs">
          <button type="button" class="px-2 py-1 rounded {mode === 'total' ? 'bg-accent text-bg-0' : 'text-slate-300'}" onclick={() => { mode = 'total'; }}>合計</button>
          <button type="button" class="px-2 py-1 rounded {mode === 'avg'   ? 'bg-accent text-bg-0' : 'text-slate-300'}" onclick={() => { mode = 'avg'; }}>ターン平均</button>
        </div>
      </div>
      <DamageChart labels={chartLabels} series={chartSeries} type="bar" title={mode === 'avg' ? `ラン合計 ÷ ${totalTurns}ターン` : 'ラン合計'} />

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-3">
        <CardTable cards={total.card_stats} title="カード別集計（ラン合計）" />
        <DebuffTable debuffs={total.debuffs_applied} title="デバフ付与（ラン合計）" />
      </div>
    {:else}
      <div class="text-slate-500">データなし</div>
    {/if}
  </section>

  {#if playerIds.length > 1}
    <RdpsPanel {rdps} {playerNames} title="貢献度ダメージ (rDPS) — ラン全体" />
  {/if}

</div>
