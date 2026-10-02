<script lang="ts">
  // 戦闘の統計 (全戦闘の合計 / 1 戦闘のサマリ共通。spec ui.md §3.4・§3.5)。
  //   主要数値 (選んだプレイヤー) → プレイヤー比較 (全員) → 貢献スコア (全員) → カード別・デバフ付与 (選んだプレイヤー)
  import type { PlayerCombatSummary } from '../lib/types';
  import type { RdpsTable } from '../lib/rdps';
  import type { RmitTable } from '../lib/rmit';
  import { useSession } from '../lib/session';
  import { STAT_HELP } from '../lib/statHelp';
  import { RDPS_HELP, RMIT_HELP } from '../lib/contribHelp';
  import StatTile from './ui/StatTile.svelte';
  import EmptyState from './ui/EmptyState.svelte';
  import PlayerComparison from './PlayerComparison.svelte';
  import ContribPanel from './ContribPanel.svelte';
  import CardTable from './CardTable.svelte';
  import DebuffTable from './DebuffTable.svelte';

  interface Props {
    stats: Record<string, PlayerCombatSummary | undefined>;
    rdps: RdpsTable;
    rmit: RmitTable;
    turns: number;
    combatsCount?: number;      // 全戦闘の合計のとき
    rangeLabel: string;         // 「ラン合計」「この戦闘」
  }
  let { stats, rdps, rmit, turns, combatsCount, rangeLabel }: Props = $props();
  const s = useSession();

  let me = $derived(stats[s.player]);
  let who = $derived(s.playerIds.length > 1 ? (s.playerNames[s.player] ?? s.player) : undefined);
  const fmt = (n: number) => (Number.isInteger(n) ? String(n) : n.toFixed(1));
  const perTurn = (n: number) => `ターン平均 ${fmt(n / Math.max(1, turns))}`;
</script>

<div class="space-y-6">
  <section aria-label="主要数値" class="space-y-3">
    {#if who}<div class="text-sm text-slate-400"><span class="text-slate-200 font-medium">{who}</span> の{rangeLabel}</div>{/if}
    {#if me}
      <div class="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-3">
        <StatTile size="primary" label="有効与ダメージ" value={me.effective_damage_dealt} sub={perTurn(me.effective_damage_dealt)} help={STAT_HELP['有効与ダメージ']} />
        <StatTile size="primary" label="被ダメージ" value={me.damage_received} sub={perTurn(me.damage_received)} help={STAT_HELP['被ダメージ']} />
        <StatTile size="primary" label="有効ブロック" value={me.effective_block} sub={perTurn(me.effective_block)} help={STAT_HELP['有効ブロック']} />
        <StatTile size="primary" label="与ダメ貢献 (rDPS)" value={rdps.byPlayer[s.player]?.total ?? 0} help={RDPS_HELP} />
        <StatTile size="primary" label="被ダメ軽減貢献 (rMit)" value={rmit.byPlayer[s.player]?.total ?? 0} help={RMIT_HELP} />
      </div>
      <div class="grid grid-cols-2 sm:grid-cols-4 gap-2">
        <StatTile label="与ダメージ(HP)" value={me.damage_dealt} help={STAT_HELP['与ダメージ(HP)']} />
        <StatTile label="オーバーキル" value={me.overkill_damage} help={STAT_HELP['オーバーキル']} />
        <StatTile label="総獲得ブロック" value={me.block_gained_self} help={STAT_HELP['総獲得ブロック']} />
        <StatTile label="味方付与ブロック" value={me.block_given_allies} help={STAT_HELP['味方付与ブロック']} />
        <StatTile label="エナジー使用" value={me.energy_used} help={STAT_HELP['エナジー使用']} />
        <StatTile label="カード使用枚数" value={me.cards_played} help={STAT_HELP['カード使用枚数']} />
        <StatTile label="ポーション使用" value={me.potions_used} sub={combatsCount != null ? `${combatsCount} 戦` : undefined} help={STAT_HELP['ポーション使用']} />
        <StatTile label="最大カードダメージ" value={me.max_single_hit} sub={me.max_single_hit_card ?? undefined} help={STAT_HELP['最大カードダメージ']} />
      </div>
    {:else}
      <div class="bg-bg-1 border border-bg-3 rounded-lg"><EmptyState title="このプレイヤーの記録はありません" hint="ほかのプレイヤーを選んでください。" /></div>
    {/if}
  </section>

  {#if s.playerIds.length > 1}
    <PlayerComparison {stats} {rdps} {rmit} />
  {/if}

  <div class="grid grid-cols-1 gap-4">
    <ContribPanel table={rdps} title="与ダメ貢献 (rDPS)" help={RDPS_HELP} />
    <ContribPanel table={rmit} title="被ダメ軽減貢献 (rMit)" help={RMIT_HELP} />
  </div>

  {#if me}
    <div class="grid grid-cols-1 lg:grid-cols-3 gap-4 items-start">
      <div class="lg:col-span-2 min-w-0"><CardTable cards={me.card_stats} title={`カード別 (${rangeLabel})`} scope={who} powerNames={s.powerNames} /></div>
      <DebuffTable debuffs={me.debuffs_applied} title={`デバフ付与 (${rangeLabel})`} scope={who} powerNames={s.powerNames} />
    </div>
  {/if}
</div>
