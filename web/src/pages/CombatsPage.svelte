<script lang="ts">
  // 戦闘 (spec ui.md「戦闘」): 戦闘セレクタ + 全戦闘の合計 (Act の範囲で絞れる)。
  import { useSession } from '../lib/session';
  import { computeRdps } from '../lib/rdps';
  import { computeRmit } from '../lib/rmit';
  import { buildRunTotals } from '../lib/aggregate';
  import { actRangeLabel, actRangeOptions, inActRange, type ActRange } from '../lib/actRange';
  import SegmentedControl from '../components/ui/SegmentedControl.svelte';
  import CombatSelector from '../components/CombatSelector.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  import CombatSummary from '../components/CombatSummary.svelte';

  const s = useSession();
  let range: ActRange = $state('all');
  let rangeOptions = $derived(actRangeOptions(s.combatActs));
  let combats = $derived(s.combats.filter(c => inActRange(s.combatActs, c.combat_index, range)));
  let totals = $derived(range === 'all' ? s.totals : buildRunTotals(combats));
  let events = $derived(s.combatEvents.filter(e => inActRange(s.combatActs, e.combat_index!, range)));
  let rdps = $derived(computeRdps(events));
  let rmit = $derived(computeRmit(events));
  let turns = $derived(combats.reduce((a, c) => a + c.turns.length, 0));
</script>

<div class="space-y-6">
  <CombatSelector current={null} />

  {#if s.combats.length === 0}
    <div class="bg-bg-1 border border-bg-3 rounded-lg"><EmptyState title="戦闘の記録がありません" hint="最初の戦闘が始まると表示されます。" /></div>
  {:else}
    <div>
      <div class="flex flex-wrap items-center justify-between gap-3 mb-3">
        <h2 class="text-base font-semibold text-slate-100">全戦闘の合計</h2>
        {#if rangeOptions.length > 1}
          <SegmentedControl label="範囲" options={rangeOptions} value={range} onChange={(v) => range = v} />
        {/if}
      </div>
      <CombatSummary stats={totals.perPlayer} {rdps} {rmit} turns={turns} combatsCount={combats.length} rangeLabel={range === 'all' ? 'ラン合計' : actRangeLabel(range)} />
    </div>
  {/if}
</div>
