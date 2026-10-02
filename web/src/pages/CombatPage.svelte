<script lang="ts">
  // 戦闘詳細 (spec ui.md §3.5): パンくず・前後の戦闘・メタ情報・サマリ / ターン / タイムライン。
  import { useSession } from '../lib/session';
  import { computeRdps } from '../lib/rdps';
  import { computeRmit } from '../lib/rmit';
  import { roomVisual } from '../lib/runOverview';
  import { link, href, navigate, type CombatViewName } from '../lib/router.svelte';
  import Badge from '../components/ui/Badge.svelte';
  import CombatSelector from '../components/CombatSelector.svelte';
  import SegmentedControl from '../components/ui/SegmentedControl.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  import CombatSummary from '../components/CombatSummary.svelte';
  import PerTurnTable from '../components/PerTurnTable.svelte';
  import TimelineView from '../components/TimelineView.svelte';

  interface Props { combatIndex: number; view: CombatViewName }
  let { combatIndex, view }: Props = $props();
  const s = useSession();

  let idx = $derived(s.combats.findIndex(c => c.combat_index === combatIndex));
  let combat = $derived(idx >= 0 ? s.combats[idx] : null);
  let events = $derived(s.eventsByCombat.get(combatIndex) ?? []);
  let rdps = $derived(computeRdps(events));
  let rmit = $derived(computeRmit(events));
  let stats = $derived(combat ? Object.fromEntries(Object.entries(combat.finalTurn.players).map(([pid, e]) => [pid, e.combat])) : {});
  const VIEWS: { value: CombatViewName; label: string }[] = [
    { value: 'summary', label: 'サマリ' }, { value: 'turns', label: 'ターン' }, { value: 'timeline', label: 'タイムライン' },
  ];
</script>

<div class="mb-4"><CombatSelector current={combatIndex} {view} /></div>

{#if !combat}
  <div class="bg-bg-1 border border-bg-3 rounded-lg"><EmptyState title="この戦闘の記録がありません" hint="戦闘一覧から選んでください。" /></div>
  <div class="mt-3 text-sm"><a class="link" href={href({ name: 'combats' })} use:link={{ name: 'combats' }}>戦闘一覧へ</a></div>
{:else}
  {@const v = roomVisual(combat.room_type ?? '')}
  <div class="space-y-6">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="flex flex-wrap items-center gap-2">
        <h1 class="text-xl font-semibold text-slate-100">{combat.encounter_name ?? '不明な遭遇'}</h1>
        {#if combat.room_type}<Badge label={v.label} color={v.color} />{/if}
        {#if combat.victory === true}<Badge label="勝利" tone="ok" />{:else if combat.victory === false}<Badge label="敗北" tone="bad" />{:else}<Badge label="進行中" tone="warn" />{/if}
        <span class="text-sm text-slate-400">{combatIndex} 階 · {combat.turns.length} ターン</span>
        <a class="link text-sm" href={href({ name: 'floor', floor: combatIndex })} use:link={{ name: 'floor', floor: combatIndex }}>この階の詳細へ</a>
      </div>
      <SegmentedControl label="表示" options={VIEWS} value={view}
                        onChange={(nv) => navigate({ name: 'combat', combat: combatIndex, view: nv }, { replace: true })} />
    </div>

    {#if view === 'summary'}
      <CombatSummary {stats} {rdps} {rmit} turns={combat.turns.length} rangeLabel="この戦闘" />
    {:else if view === 'turns'}
      <PerTurnTable turns={combat.turns} playerId={s.player} scope={s.playerIds.length > 1 ? (s.playerNames[s.player] ?? s.player) : undefined} />
    {:else}
      <TimelineView {events} playerNames={s.playerNames} powerNames={s.powerNames} cardNames={s.cardNames} />
    {/if}
  </div>
{/if}
