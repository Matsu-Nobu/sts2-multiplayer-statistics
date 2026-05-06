<script lang="ts">
  import type { CombatInfo } from '../lib/aggregate';
  import type { EventRecord } from '../lib/types';
  import { computeRdps } from '../lib/rdps';
  import { computeRmit } from '../lib/rmit';
  import StatCard from './StatCard.svelte';
  import { STAT_HELP } from '../lib/statHelp';
  import CardTable from './CardTable.svelte';
  import DebuffTable from './DebuffTable.svelte';
  import DamageChart from './DamageChart.svelte';
  import PerTurnTable from './PerTurnTable.svelte';
  import RdpsPanel from './RdpsPanel.svelte';
  import RmitPanel from './RmitPanel.svelte';
  import TimelineView from './TimelineView.svelte';

  interface Props {
    combat: CombatInfo;
    playerIds: string[];
    playerNames: Record<string, string>;
    powerNames: Record<string, string>;
    combatEvents: EventRecord[];   // この戦闘の生 events（rDPS/タイムライン用）
  }
  let { combat, playerIds, playerNames, powerNames, combatEvents }: Props = $props();
  let view: 'summary' | 'timeline' = $state('summary');
  let rdps = $derived(computeRdps(combatEvents));
  let rmit = $derived(computeRmit(combatEvents));

  let activePlayer: string = $state('');

  // 初回 + playerIds 変動時の補正
  $effect.pre(() => {
    if (!playerIds.includes(activePlayer)) {
      activePlayer = playerIds[0] ?? '';
    }
  });

  let entry = $derived(activePlayer ? combat.finalTurn.players[activePlayer] : null);
  let turnsCount = $derived(combat.turns.length || 1);

  // プレイヤー間スタッツ比較用の棒グラフ
  type StatKey = 'effective_damage_dealt' | 'damage_received' | 'effective_block' | 'max_single_hit';
  const STAT_KEYS: { key: StatKey; label: string }[] = [
    { key: 'effective_damage_dealt', label: '有効与ダメージ' },
    { key: 'damage_received',        label: '被ダメージ' },
    { key: 'effective_block',        label: '有効ブロック' },
    { key: 'max_single_hit',         label: '最大カードダメージ' },
  ];
  // プレイヤーごとに固定色（全チャートで一貫させる）
  const PLAYER_COLORS = ['#7aa2f7', '#bb9af7', '#9ece6a', '#e0af68', '#f7768e'];
  let playerColors = $derived(playerIds.map((_, i) => PLAYER_COLORS[i % PLAYER_COLORS.length]));

  function fmtNum(n: number, decimals = 1): string {
    if (Number.isInteger(n)) return n.toString();
    return n.toFixed(decimals);
  }
  function avg(value: number): number {
    return value / turnsCount;
  }

  // メトリックごとに「プレイヤー名を X 軸にした単一系列のチャート」を生成
  let perMetricCharts = $derived(STAT_KEYS.map(s => ({
    key: s.key,
    label: s.label,
    chart: {
      labels: playerIds.map(pid => playerNames[pid] ?? pid),
      series: [{
        label: s.label,
        color: playerColors,
        values: playerIds.map(pid => {
          const c = combat.finalTurn.players[pid]?.combat;
          return c ? (c[s.key] as number) ?? 0 : 0;
        }),
      }],
    },
  })));
</script>

<div class="space-y-6">

  <!-- サマリ／タイムライン切替 -->
  <div class="flex gap-1 bg-bg-2 border border-bg-3 rounded p-0.5 text-xs w-fit">
    <button type="button" class="px-3 py-1 rounded {view === 'summary'  ? 'bg-accent text-bg-0' : 'text-slate-300'}" onclick={() => { view = 'summary'; }}>サマリ</button>
    <button type="button" class="px-3 py-1 rounded {view === 'timeline' ? 'bg-accent text-bg-0' : 'text-slate-300'}" onclick={() => { view = 'timeline'; }}>タイムライン</button>
  </div>

  {#if view === 'timeline'}
    <TimelineView events={combatEvents} {playerNames} {powerNames} />
  {:else}

  <!-- プレイヤー切替 -->
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

  <!-- 戦闘サマリ -->
  <section class="space-y-3">
    <h3 class="text-xs uppercase tracking-wide text-slate-500">戦闘サマリ</h3>
    {#if entry}
      <div class="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-5 gap-2">
        <StatCard label="有効与ダメージ"   value={entry.combat.effective_damage_dealt}  sub={`ターン平均: ${fmtNum(avg(entry.combat.effective_damage_dealt))}`} help={STAT_HELP['有効与ダメージ']} />
        <StatCard label="与ダメージ(HP)"   value={entry.combat.damage_dealt}            sub={`ターン平均: ${fmtNum(avg(entry.combat.damage_dealt))}`} help={STAT_HELP['与ダメージ(HP)']} />
        <StatCard label="オーバーキル"     value={entry.combat.overkill_damage}         sub={`ターン平均: ${fmtNum(avg(entry.combat.overkill_damage))}`} help={STAT_HELP['オーバーキル']} />
        <StatCard label="被ダメージ"       value={entry.combat.damage_received}         sub={`ターン平均: ${fmtNum(avg(entry.combat.damage_received))}`} help={STAT_HELP['被ダメージ']} />
        <StatCard label="有効ブロック"     value={entry.combat.effective_block}         sub={`ターン平均: ${fmtNum(avg(entry.combat.effective_block))}`} help={STAT_HELP['有効ブロック']} />
        <StatCard label="総獲得ブロック"   value={entry.combat.block_gained_self}       sub={`ターン平均: ${fmtNum(avg(entry.combat.block_gained_self))}`} help={STAT_HELP['総獲得ブロック']} />
        <StatCard label="味方付与ブロック" value={entry.combat.block_given_allies}      sub={`ターン平均: ${fmtNum(avg(entry.combat.block_given_allies))}`} help={STAT_HELP['味方付与ブロック']} />
        <StatCard label="エナジー使用"     value={entry.combat.energy_used}             sub={`ターン平均: ${fmtNum(avg(entry.combat.energy_used))}`} help={STAT_HELP['エナジー使用']} />
        <StatCard label="カード使用枚数"   value={entry.combat.cards_played}            sub={`ターン平均: ${fmtNum(avg(entry.combat.cards_played))}`} help={STAT_HELP['カード使用枚数']} />
        <StatCard label="最大カードダメージ" value={entry.combat.max_single_hit}        sub={entry.combat.max_single_hit_card ?? `${turnsCount}ターン`} help={STAT_HELP['最大カードダメージ']} />
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
        <CardTable cards={entry.combat.card_stats} title="カード別集計（戦闘合計）" {powerNames} />
        <DebuffTable debuffs={entry.combat.debuffs_applied} title="デバフ付与（戦闘合計）" {powerNames} />
      </div>
    {:else}
      <div class="text-slate-500">このプレイヤーのデータはありません</div>
    {/if}
  </section>

  <!-- rDPS / rMit -->
  <RdpsPanel {rdps} {playerNames} />
  <RmitPanel {rmit} {playerNames} />

  <!-- ターン別 -->
  <section class="space-y-3">
    <h3 class="text-xs uppercase tracking-wide text-slate-500">ターン別内訳</h3>
    {#if activePlayer}
      <PerTurnTable turns={combat.turns} playerId={activePlayer} />
    {/if}
  </section>

  {/if}

</div>
