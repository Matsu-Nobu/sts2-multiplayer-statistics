<script lang="ts">
  // 戦闘 (spec ui.md §3.4): 戦闘セレクタ + 全戦闘の合計。
  import { useSession } from '../lib/session';
  import { computeRdps } from '../lib/rdps';
  import { computeRmit } from '../lib/rmit';
  import CombatSelector from '../components/CombatSelector.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  import CombatSummary from '../components/CombatSummary.svelte';

  const s = useSession();
  let rdps = $derived(computeRdps(s.combatEvents));
  let rmit = $derived(computeRmit(s.combatEvents));
  let turns = $derived(s.combats.reduce((a, c) => a + c.turns.length, 0));
</script>

<div class="space-y-6">
  <CombatSelector current={null} />

  {#if s.combats.length === 0}
    <div class="bg-bg-1 border border-bg-3 rounded-lg"><EmptyState title="戦闘の記録がありません" hint="最初の戦闘が始まると表示されます。" /></div>
  {:else}
    <div>
      <h2 class="text-base font-semibold text-slate-100 mb-3">全戦闘の合計</h2>
      <CombatSummary stats={s.totals.perPlayer} {rdps} {rmit} turns={turns} combatsCount={s.combats.length} rangeLabel="ラン合計" />
    </div>
  {/if}
</div>
