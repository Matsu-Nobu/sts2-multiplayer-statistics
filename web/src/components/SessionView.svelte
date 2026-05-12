<script lang="ts">
  import type { SessionDoc } from '../lib/types';
  import { buildCombatInfos, buildRunTotals, buildPowerNames, buildCardNames } from '../lib/aggregate';
  import { loadCatalog, type CatalogLookup } from '../lib/catalog';
  import Header from './Header.svelte';
  import CombatView from './CombatView.svelte';
  import AllCombatsView from './AllCombatsView.svelte';
  import RunOverview from './RunOverview.svelte';

  // カタログを 1 回 fetch (キャッシュ effective) して props で下に渡す。
  // Promise を $state に詰めて await した結果を $derived で取り出す形でも良いが、
  // ここはシンプルに「ロード完了するまで empty lookup を渡す」方式。
  let catalog: CatalogLookup | null = $state(null);
  $effect(() => { loadCatalog('ja').then(l => { catalog = l; }); });

  interface Props {
    doc: SessionDoc;
    live: 'live' | 'final' | 'offline';
    lastUpdated: Date | null;
  }
  let { doc: rawDoc, live, lastUpdated }: Props = $props();

  // mod 側で MP の local player 自身の event は LocalContext.NetId / Player.NetId が
  // "1" (local-player pseudo) として記録されることがある。host_steam_id が実 Steam ID
  // のときは "1" を host_steam_id にエイリアスして同一人物として扱う。
  let doc = $derived.by(() => {
    const hostId = rawDoc.session?.host_steam_id;
    if (!hostId || hostId === '1') return rawDoc;
    const remap = (pid: string | null | undefined) => (pid === '1' ? hostId : pid);
    return {
      ...rawDoc,
      players: rawDoc.players
        .map(p => ({ ...p, steam_id: p.steam_id === '1' ? hostId : p.steam_id }))
        .filter((p, i, arr) => arr.findIndex(q => q.steam_id === p.steam_id) === i),
      events: rawDoc.events.map(e => ({ ...e, player_id: remap(e.player_id) ?? null })),
    };
  });

  let combats = $derived(buildCombatInfos(doc));
  let totals = $derived(buildRunTotals(combats));
  let playerIds = $derived(doc.players.map(p => p.steam_id));
  let playerNames = $derived(Object.fromEntries(doc.players.map(p => [p.steam_id, p.display_name])));
  let powerNames = $derived(buildPowerNames(doc.events));
  let cardNames = $derived(buildCardNames(doc.events));

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

  // 上位タブ: 'combats' (戦闘統計) or 'run' (ラン全体)
  let topTab: 'combats' | 'run' = $state('combats');

  // 戦闘タブ内の選択（既存ロジック）
  let activeTab: 'all' | number = $state('all');
  $effect(() => {
    if (typeof activeTab === 'number' && !combats.find(c => c.combat_index === activeTab)) {
      activeTab = 'all';
    }
  });
  let activeCombat = $derived(
    typeof activeTab === 'number' ? combats.find(c => c.combat_index === activeTab) ?? null : null
  );

  function combatLabel(c: typeof combats[number], ordinal: number): string {
    const name = c.encounter_name ? `${ordinal}. ${c.encounter_name}` : `戦闘 ${ordinal}`;
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

  <!-- トップタブ -->
  <div class="flex gap-1 bg-bg-2 border border-bg-3 rounded p-0.5 text-sm w-fit">
    <button
      type="button"
      class="px-4 py-1.5 rounded {topTab === 'combats' ? 'bg-accent text-bg-0' : 'text-slate-300 hover:text-slate-100'}"
      onclick={() => { topTab = 'combats'; }}
    >戦闘統計</button>
    <button
      type="button"
      class="px-4 py-1.5 rounded {topTab === 'run' ? 'bg-accent text-bg-0' : 'text-slate-300 hover:text-slate-100'}"
      onclick={() => { topTab = 'run'; }}
    >ラン全体</button>
  </div>

  {#if topTab === 'combats'}
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
        {#each combats as c, i (c.combat_index)}
          <option value={String(c.combat_index)}>{combatLabel(c, i + 1)}</option>
        {/each}
      </select>
    </div>

    {#if activeTab === 'all'}
      <AllCombatsView {combats} {totals} {playerIds} {playerNames} {powerNames} allCombatEvents={allCombatEvents} />
    {:else if activeCombat}
      {#key activeCombat.combat_index}
        <CombatView
          combat={activeCombat}
          {playerIds}
          {playerNames}
          {powerNames}
          {cardNames}
          combatEvents={eventsByCombat.get(activeCombat.combat_index) ?? []}
        />
      {/key}
    {:else}
      <div class="text-slate-500">データなし</div>
    {/if}
  {:else}
    <RunOverview
      events={doc.events}
      {combats}
      {playerIds}
      {playerNames}
      {powerNames}
      {cardNames}
      {catalog}
      onJumpToCombat={(ci) => { topTab = 'combats'; activeTab = ci; }}
    />
  {/if}

</main>
