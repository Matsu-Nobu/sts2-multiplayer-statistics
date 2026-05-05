<script lang="ts">
  import type { CombatInfo } from '../lib/aggregate';
  import StatCard from './StatCard.svelte';
  import CardTable from './CardTable.svelte';
  import DebuffTable from './DebuffTable.svelte';
  import DamageChart from './DamageChart.svelte';
  import PerTurnTable from './PerTurnTable.svelte';

  interface Props {
    combat: CombatInfo;
    playerIds: string[];
    playerNames: Record<string, string>;
  }
  let { combat, playerIds, playerNames }: Props = $props();

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
  type StatKey = 'damage_dealt' | 'damage_received' | 'block_gained_self' | 'block_given_allies' | 'energy_used' | 'cards_played' | 'max_single_hit';
  const STAT_KEYS: { key: StatKey; label: string; perTurn: boolean }[] = [
    { key: 'damage_dealt',       label: '与ダメ',       perTurn: true  },
    { key: 'damage_received',    label: '被ダメ',       perTurn: true  },
    { key: 'block_gained_self',  label: 'ブロック',     perTurn: true  },
    { key: 'block_given_allies', label: '味方ブロック', perTurn: true  },
    { key: 'energy_used',        label: 'エナジー',     perTurn: true  },
    { key: 'cards_played',       label: 'カード使用',   perTurn: true  },
    { key: 'max_single_hit',     label: '最大単発',     perTurn: false },  // 平均しても意味ない
  ];
  const COLORS = ['#7aa2f7', '#bb9af7', '#9ece6a', '#e0af68', '#f7768e'];

  let mode: 'total' | 'avg' = $state('total');

  function fmtNum(n: number, decimals = 1): string {
    if (Number.isInteger(n)) return n.toString();
    return n.toFixed(decimals);
  }
  function avg(value: number): number {
    return value / turnsCount;
  }

  let chartLabels = $derived(STAT_KEYS.map(s => s.label));
  let chartSeries = $derived(playerIds.map((pid, i) => ({
    label: playerNames[pid] ?? pid,
    color: COLORS[i % COLORS.length],
    values: STAT_KEYS.map(s => {
      const c = combat.finalTurn.players[pid]?.combat;
      const raw = c ? (c[s.key] as number) ?? 0 : 0;
      return mode === 'avg' && s.perTurn ? +(raw / turnsCount).toFixed(2) : raw;
    }),
  })));
</script>

<div class="space-y-6">

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
      <div class="grid grid-cols-2 sm:grid-cols-4 lg:grid-cols-7 gap-2">
        <StatCard label="与ダメージ"     value={entry.combat.damage_dealt}       sub={`ターン平均: ${fmtNum(avg(entry.combat.damage_dealt))}`} />
        <StatCard label="被ダメージ"     value={entry.combat.damage_received}    sub={`ターン平均: ${fmtNum(avg(entry.combat.damage_received))}`} />
        <StatCard label="ブロック獲得"   value={entry.combat.block_gained_self}  sub={`ターン平均: ${fmtNum(avg(entry.combat.block_gained_self))}`} />
        <StatCard label="味方付与ブロック" value={entry.combat.block_given_allies} sub={`ターン平均: ${fmtNum(avg(entry.combat.block_given_allies))}`} />
        <StatCard label="エナジー使用"   value={entry.combat.energy_used}        sub={`ターン平均: ${fmtNum(avg(entry.combat.energy_used))}`} />
        <StatCard label="カード使用枚数" value={entry.combat.cards_played}       sub={`ターン平均: ${fmtNum(avg(entry.combat.cards_played))}`} />
        <StatCard label="最大単発"       value={entry.combat.max_single_hit}     sub={`${turnsCount}ターン`} />
      </div>

      <div class="flex items-center justify-between">
        <h3 class="text-xs uppercase tracking-wide text-slate-500">プレイヤー比較</h3>
        <div class="flex gap-1 bg-bg-2 border border-bg-3 rounded p-0.5 text-xs">
          <button type="button" class="px-2 py-1 rounded {mode === 'total' ? 'bg-accent text-bg-0' : 'text-slate-300'}" onclick={() => { mode = 'total'; }}>合計</button>
          <button type="button" class="px-2 py-1 rounded {mode === 'avg'   ? 'bg-accent text-bg-0' : 'text-slate-300'}" onclick={() => { mode = 'avg'; }}>ターン平均</button>
        </div>
      </div>
      <DamageChart labels={chartLabels} series={chartSeries} type="bar" title={mode === 'avg' ? `戦闘合計 ÷ ${turnsCount}ターン` : '戦闘合計'} />

      <div class="grid grid-cols-1 lg:grid-cols-2 gap-3">
        <CardTable cards={entry.combat.card_stats} title="カード別集計（戦闘合計）" />
        <DebuffTable debuffs={entry.combat.debuffs_applied} title="デバフ付与（戦闘合計）" />
      </div>
    {:else}
      <div class="text-slate-500">このプレイヤーのデータはありません</div>
    {/if}
  </section>

  <!-- ターン別 -->
  <section class="space-y-3">
    <h3 class="text-xs uppercase tracking-wide text-slate-500">ターン別内訳</h3>
    {#if activePlayer}
      <PerTurnTable turns={combat.turns} playerId={activePlayer} />
    {/if}
  </section>

</div>
