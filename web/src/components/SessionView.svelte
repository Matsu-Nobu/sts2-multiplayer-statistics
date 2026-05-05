<script lang="ts">
  import type { SessionDoc } from '../lib/types';
  import { buildCombatInfos, buildRunTotals } from '../lib/aggregate';
  import Header from './Header.svelte';
  import CombatView from './CombatView.svelte';
  import AllCombatsView from './AllCombatsView.svelte';

  interface Props {
    doc: SessionDoc;
    live: 'live' | 'final' | 'offline';
    lastUpdated: Date | null;
  }
  let { doc, live, lastUpdated }: Props = $props();

  let combats = $derived(buildCombatInfos(doc));
  let totals = $derived(buildRunTotals(combats));
  let playerIds = $derived(doc.players.map(p => p.steam_id));
  let playerNames = $derived(Object.fromEntries(doc.players.map(p => [p.steam_id, p.display_name])));

  // events を combat_index でバケット化（CombatView / AllCombatsView へ渡す）
  let eventsByCombat = $derived.by(() => {
    const m = new Map<number, typeof doc.events>();
    for (const ev of doc.events) {
      if (ev.combat_index == null) continue;
      if (!m.has(ev.combat_index)) m.set(ev.combat_index, []);
      m.get(ev.combat_index)!.push(ev);
    }
    return m;
  });
  let allCombatEvents = $derived(doc.events.filter(e => e.combat_index != null));

  // 'all' or combat_index (number)
  let activeTab: 'all' | number = $state('all');

  $effect(() => {
    if (typeof activeTab === 'number' && !combats.find(c => c.combat_index === activeTab)) {
      activeTab = 'all';
    }
  });

  let activeCombat = $derived(
    typeof activeTab === 'number' ? combats.find(c => c.combat_index === activeTab) ?? null : null
  );

  function combatLabel(c: typeof combats[number]): string {
    const name = c.encounter_name ? `${c.combat_index}. ${c.encounter_name}` : `戦闘 ${c.combat_index}`;
    const room = (c.room_type === 'Elite' || c.room_type === 'Boss') ? ` [${c.room_type}]` : '';
    return name + room;
  }

  function onSelect(e: Event) {
    const v = (e.target as HTMLSelectElement).value;
    activeTab = v === 'all' ? 'all' : Number(v);
  }
</script>

<Header session={doc.session} {live} {lastUpdated} />

<main class="max-w-7xl mx-auto px-4 py-6 space-y-4">

  <!-- 戦闘セレクタ -->
  <div class="flex items-center gap-3">
    <label for="combat-select" class="text-xs uppercase tracking-wide text-slate-400">表示対象</label>
    <select
      id="combat-select"
      class="bg-bg-1 border border-bg-3 rounded px-3 py-2 text-sm text-slate-200 hover:bg-bg-2 focus:outline-none focus:ring-1 focus:ring-accent min-w-[280px]"
      value={activeTab === 'all' ? 'all' : String(activeTab)}
      onchange={onSelect}
    >
      <option value="all">全体（{combats.length}戦闘）</option>
      {#each combats as c (c.combat_index)}
        <option value={String(c.combat_index)}>{combatLabel(c)}</option>
      {/each}
    </select>
  </div>

  {#if activeTab === 'all'}
    <AllCombatsView {combats} {totals} {playerIds} {playerNames} allCombatEvents={allCombatEvents} />
  {:else if activeCombat}
    {#key activeCombat.combat_index}
      <CombatView
        combat={activeCombat}
        {playerIds}
        {playerNames}
        combatEvents={eventsByCombat.get(activeCombat.combat_index) ?? []}
      />
    {/key}
  {:else}
    <div class="text-slate-500">データなし</div>
  {/if}

</main>
