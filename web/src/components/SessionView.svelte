<script lang="ts">
  // 画面の骨格 (AppShell。spec ui.md §2): ヘッダー・タブ・プレイヤー切り替え・本文。
  import type { SessionDoc, RunStartPayload } from '../lib/types';
  import { buildCombatInfos, buildRunTotals, buildPowerNames, buildCardNames, latestCombatEvents } from '../lib/aggregate';
  import { loadCatalog, type CatalogLookup } from '../lib/catalog';
  import { route, navigate, link, href, type Page } from '../lib/router.svelte';
  import { setSession } from '../lib/session';
  import { playerColor } from '../lib/players';
  import Badge from './ui/Badge.svelte';
  import RunPage from '../pages/RunPage.svelte';
  import FloorPage from '../pages/FloorPage.svelte';
  import CombatsPage from '../pages/CombatsPage.svelte';
  import CombatPage from '../pages/CombatPage.svelte';

  interface Props { doc: SessionDoc; live: 'live' | 'final' | 'offline'; lastUpdated: Date | null }
  let { doc, live, lastUpdated }: Props = $props();

  let catalog: CatalogLookup | null = $state(null);
  $effect(() => { loadCatalog('ja').then(l => { catalog = l; }); });

  let combats = $derived(buildCombatInfos(doc));
  let totals = $derived(buildRunTotals(combats));
  let playerIds = $derived(doc.players.map(p => p.steam_id));
  let playerNames = $derived(Object.fromEntries(doc.players.map(p => [p.steam_id, p.display_name])));
  let powerNames = $derived(buildPowerNames(doc.events));
  let cardNames = $derived(buildCardNames(doc.events));
  let combatEvents = $derived(latestCombatEvents(doc.events).filter(e => e.combat_index != null));
  let eventsByCombat = $derived.by(() => {
    const m = new Map<number, typeof combatEvents>();
    for (const ev of combatEvents) {
      if (!m.has(ev.combat_index!)) m.set(ev.combat_index!, []);
      m.get(ev.combat_index!)!.push(ev);
    }
    return m;
  });
  // 選んでいるプレイヤー (?p=)。無い・知らない ID なら先頭 (spec ui.md §1)
  let player = $derived(route.player && playerIds.includes(route.player) ? route.player : (playerIds[0] ?? ''));

  setSession({
    get doc() { return doc; }, get combats() { return combats; }, get totals() { return totals; },
    get playerIds() { return playerIds; }, get playerNames() { return playerNames; },
    get powerNames() { return powerNames; }, get cardNames() { return cardNames; }, get catalog() { return catalog; },
    get combatEvents() { return combatEvents; }, get eventsByCombat() { return eventsByCombat; },
    get player() { return player; },
  });

  // === ヘッダー ===
  let runStarts = $derived(doc.events.filter(e => e.event_type === 'run_start'));
  let characterName = $derived.by(() => {
    const p = runStarts.find(e => e.player_id === player)?.payload as RunStartPayload | undefined;
    return p?.character_name || p?.character_id || doc.session.character_id || null;
  });
  const OUTCOME: Record<string, { label: string; tone: 'ok' | 'bad' | 'neutral' }> = {
    victory: { label: '勝利', tone: 'ok' }, death: { label: '死亡', tone: 'bad' }, abandoned: { label: '放棄', tone: 'neutral' },
  };
  let outcome = $derived(doc.session.outcome ? OUTCOME[doc.session.outcome] ?? { label: doc.session.outcome, tone: 'neutral' as const } : { label: '進行中', tone: 'warn' as const });

  function fmtRel(d: Date | null): string {
    if (!d) return '—';
    const s = Math.round((Date.now() - d.getTime()) / 1000);
    if (s < 60) return `${s}秒前`;
    const m = Math.round(s / 60);
    return m < 60 ? `${m}分前` : `${Math.round(m / 60)}時間前`;
  }
  // 「最終更新 N秒前」を進める
  let now = $state(0);
  $effect(() => { const t = setInterval(() => now++, 5000); return () => clearInterval(t); });
  let updatedLabel = $derived.by(() => { void now; return fmtRel(lastUpdated); });

  // === ナビゲーション ===
  const TABS: { key: 'run' | 'floors' | 'combats'; label: string; page: Page }[] = [
    { key: 'run', label: 'ラン全体', page: { name: 'run' } },
    { key: 'floors', label: '階', page: { name: 'floors' } },
    { key: 'combats', label: '戦闘', page: { name: 'combats' } },
  ];
  let activeTab = $derived(route.page.name === 'floor' ? 'floors' : route.page.name === 'combat' ? 'combats' : route.page.name);

  function selectPlayer(pid: string) { navigate(route.page, { replace: true, player: pid }); }
</script>

<header class="border-b border-bg-3 bg-bg-1">
  <div class="max-w-7xl mx-auto px-4 pt-4">
    <div class="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
      <div class="min-w-0">
        <h1 class="text-xl font-semibold text-slate-100 break-words">{doc.players.map(p => p.display_name).join('・') || (doc.session.host_name ?? '不明なプレイヤー')}</h1>
        <div class="mt-1.5 flex flex-wrap items-center gap-1.5">
          {#if playerIds.length > 1}<Badge label={`${playerIds.length} 人`} />{:else if characterName}<Badge label={characterName} />{/if}
          {#if doc.session.ascension != null}<Badge label={`アセンション ${doc.session.ascension}`} />{/if}
          {#if doc.session.final_floor != null}<Badge label={`${doc.session.final_floor} 階`} />{/if}
          <Badge label={outcome.label} tone={outcome.tone} />
        </div>
      </div>
      <div class="text-xs text-slate-500 sm:text-right flex sm:block items-center gap-3" aria-live="polite">
        <div class="flex items-center gap-1.5 sm:justify-end">
          <span class="w-2 h-2 rounded-full inline-block {live === 'live' ? 'bg-ok animate-pulse' : live === 'final' ? 'bg-slate-500' : 'bg-bad'}"></span>
          <span class={live === 'live' ? 'text-ok' : live === 'offline' ? 'text-bad' : ''}>{live === 'live' ? 'ライブ' : live === 'final' ? '完了' : 'オフライン'}</span>
        </div>
        <div class="mt-0.5">最終更新 {updatedLabel}</div>
      </div>
    </div>

    <div class="mt-3 flex items-end justify-between gap-3">
      <nav aria-label="画面" class="flex gap-1 overflow-x-auto -mb-px">
        {#each TABS as t (t.key)}
          <a href={href(t.page)} use:link={t.page}
             aria-current={activeTab === t.key ? 'page' : undefined}
             class="px-3 py-2 text-sm whitespace-nowrap border-b-2 focus-ring rounded-t {activeTab === t.key ? 'border-accent text-slate-100 font-medium' : 'border-transparent text-slate-400 hover:text-slate-200'}">{t.label}</a>
        {/each}
      </nav>

      {#if playerIds.length > 1}
        <div class="pb-1.5 shrink-0">
          <!-- 広い画面: ボタン列 / 狭い画面: セレクトボックス (spec ui.md §2) -->
          <div class="hidden sm:flex items-center gap-1" role="group" aria-label="プレイヤー">
            {#each playerIds as pid (pid)}
              <button type="button" aria-pressed={pid === player} onclick={() => selectPlayer(pid)}
                      class="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-md text-sm border focus-ring {pid === player ? 'bg-bg-3 border-slate-500 text-slate-100' : 'border-transparent text-slate-400 hover:text-slate-200 hover:bg-bg-2'}">
                <span class="w-2 h-2 rounded-full" style:background-color={playerColor(playerIds, pid)}></span>
                {playerNames[pid] ?? pid}
              </button>
            {/each}
          </div>
          <label class="sm:hidden flex items-center gap-1.5 text-sm">
            <span class="w-2 h-2 rounded-full" style:background-color={playerColor(playerIds, player)}></span>
            <span class="sr-only">プレイヤー</span>
            <select class="bg-bg-2 border border-bg-3 rounded-md px-2 py-1 text-slate-200 focus-ring" value={player}
                    onchange={(e) => selectPlayer((e.target as HTMLSelectElement).value)}>
              {#each playerIds as pid (pid)}<option value={pid}>{playerNames[pid] ?? pid}</option>{/each}
            </select>
          </label>
        </div>
      {/if}
    </div>
  </div>
</header>

<main class="max-w-7xl mx-auto px-4 py-6">
  {#if route.page.name === 'run'}
    <RunPage />
  {:else if route.page.name === 'floors'}
    <FloorPage floor={null} />
  {:else if route.page.name === 'floor'}
    <FloorPage floor={route.page.floor} />
  {:else if route.page.name === 'combats'}
    <CombatsPage />
  {:else if route.page.name === 'combat'}
    {#key route.page.combat}<CombatPage combatIndex={route.page.combat} view={route.page.view} />{/key}
  {/if}
</main>
