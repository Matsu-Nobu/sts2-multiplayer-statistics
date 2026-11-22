<script lang="ts">
  // ユーザーページの統計のタブ (spec player-stats.md)。
  import { loadCatalog, type CatalogLookup } from '../lib/catalog';
  import { userHref } from '../lib/auth.svelte';
  import { GAME_MODE_LABEL } from '../lib/labels';
  import {
    ALL, MULTI, SOLO, MIN_GAMES, applyFilters, characterKey, characterTabs, characterStats, deltaPoints, pct, rate,
    type CardRow, type ChoiceRow, type Column, type EnemyRow, type Filters, type PartnerRow, type RelicRow, type StatsDoc,
  } from '../lib/playerStats';
  import Panel from './ui/Panel.svelte';
  import StatTile from './ui/StatTile.svelte';
  import EmptyState from './ui/EmptyState.svelte';
  import Skeleton from './ui/Skeleton.svelte';
  import Unavailable from './Unavailable.svelte';
  import StatsTable from './StatsTable.svelte';

  interface Props { steamId: string }
  let { steamId }: Props = $props();

  let doc = $state<StatsDoc | null>(null);
  let failed = $state(false);
  let catalog = $state<CatalogLookup | null>(null);

  $effect(() => {
    fetch(`/api/users/${encodeURIComponent(steamId)}/stats`)
      .then(async res => { if (!res.ok) throw new Error(`stats ${res.status}`); doc = (await res.json()) as StatsDoc; })
      .catch(() => { failed = true; });
    loadCatalog('ja').then(c => { catalog = c; });
  });

  let filters = $state<Filters>({ mode: ALL, asc: ALL, players: ALL, period: 'all' });
  let hideLow = $state(true);
  let selected = $state<string | null>(null);
  let ancientSel = $state<string | null>(null);
  let eventSel = $state<string | null>(null);

  const uniq = (xs: string[]) => [...new Set(xs)];
  let games = $derived(doc?.games ?? []);
  let modeOptions = $derived(uniq(games.map(g => g.game_mode).filter((m): m is string => m != null)));
  let ascOptions = $derived(uniq(games.map(g => g.ascension).filter((a): a is number => a != null).map(String)).sort((a, b) => Number(a) - Number(b)));
  let filtered = $derived(applyFilters(games, filters, new Date()));
  let tabs = $derived(characterTabs(filtered));
  let current = $derived(tabs.find(t => t.key === selected) ?? tabs[0] ?? null);
  let stats = $derived(current ? characterStats(filtered.filter(g => characterKey(g) === current.key), catalog) : null);
  let base = $derived(stats ? rate(stats.wins, stats.games) : null);
  let ancient = $derived(stats ? stats.ancients.find(a => a.id === ancientSel) ?? stats.ancients[0] ?? null : null);
  let event = $derived(stats ? stats.events.find(e => e.id === eventSel) ?? stats.events[0] ?? null : null);

  const ROOM_LABEL: Record<string, string> = { Monster: '通常', Elite: 'エリート', Boss: 'ボス' };

  // 「{キャラクター名}の勝率との差」の列 (spec player-stats.md「表の共通の決まり」)
  function deltaColumn<R>(winRate: (r: R) => number | null): Column<R> {
    const d = (r: R) => deltaPoints(winRate(r), base);
    return {
      label: `${current?.name ?? ''}の勝率との差`, num: true, sort: d,
      text: r => { const v = d(r); return v == null ? '—' : v > 0 ? `+${v}` : String(v); },
      tone: r => { const v = d(r); return v == null || v === 0 ? null : v > 0 ? 'ok' : 'bad'; },
    };
  }
  function winRateColumn<R>(winRate: (r: R) => number | null): Column<R> {
    return { label: '勝率', num: true, sort: winRate, text: r => pct(winRate(r)) };
  }
  const count = <R,>(label: string, get: (r: R) => number): Column<R> => ({ label, num: true, sort: get, text: r => String(get(r)) });

  const cardWin = (r: CardRow) => rate(r.wins, r.games);
  let cardColumns = $derived<Column<CardRow>[]>([
    { label: 'カード', sort: r => r.name, text: r => r.name },
    count('提示回数', r => r.offered),
    { label: '取得率', num: true, sort: r => rate(r.picks, r.offered), text: r => pct(rate(r.picks, r.offered)) },
    count('取得ゲーム数', r => r.games),
    winRateColumn(cardWin),
    deltaColumn(cardWin),
  ]);
  const relicWin = (r: RelicRow) => rate(r.wins, r.games);
  let relicColumns = $derived<Column<RelicRow>[]>([
    { label: 'レリック', sort: r => r.name, text: r => r.name },
    count('所持ゲーム数', r => r.games),
    winRateColumn(relicWin),
    deltaColumn(relicWin),
  ]);
  const enemyColumns: Column<EnemyRow>[] = [
    { label: '敵', sort: r => r.name, text: r => r.name },
    { label: '種類', sort: r => r.roomType, text: r => ROOM_LABEL[r.roomType] ?? r.roomType, badge: true },
    count('戦闘回数', r => r.fights),
    { label: '平均被ダメージ', num: true, sort: r => r.damage / r.fights, text: r => String(Math.round(r.damage / r.fights)) },
    { label: '死亡回数', num: true, sort: r => r.deaths, text: r => String(r.deaths), tone: r => (r.deaths > 0 ? 'bad' : null) },
  ];
  const choiceWin = (r: ChoiceRow) => rate(r.wins, r.chosen);
  let ancientColumns = $derived<Column<ChoiceRow>[]>([
    { label: '祝福', sort: r => r.title, text: r => r.title },
    count('提示回数', r => r.offered),
    count('選択回数', r => r.chosen),
    { label: '選択率', num: true, sort: r => rate(r.chosen, r.offered), text: r => pct(rate(r.chosen, r.offered)) },
    winRateColumn(choiceWin),
    deltaColumn(choiceWin),
  ]);
  let eventColumns = $derived<Column<ChoiceRow>[]>([
    { label: '選択肢', sort: r => r.title, text: r => r.title },
    count('選択回数', r => r.chosen),
    winRateColumn(choiceWin),
    deltaColumn(choiceWin),
  ]);
  const partnerColumns: Column<PartnerRow>[] = [
    { label: 'プレイヤー', sort: r => r.name, text: r => r.name, href: r => userHref(r.steamId) },
    count('ゲーム数', r => r.games),
    winRateColumn(r => rate(r.wins, r.games)),
    { label: '自分の与ダメージの割合', num: true, sort: r => rate(r.shareSum, r.shareGames), text: r => pct(rate(r.shareSum, r.shareGames)) },
  ];

  let deathMax = $derived(Math.max(1, ...(stats?.deathFloors.map(d => d.games) ?? [])));
  let restTotal = $derived(stats?.rest.reduce((s, r) => s + r.count, 0) ?? 0);
  const selectClass = 'bg-bg-2 border border-bg-3 rounded-md px-2 py-1 text-sm text-slate-200 focus-ring';
</script>

{#snippet filter(id: string, label: string, value: string, options: [string, string][], set: (v: string) => void)}
  <label for={id} class="flex items-center gap-1.5 text-xs text-slate-400">
    {label}
    <select {id} class={selectClass} {value} onchange={(e) => set((e.target as HTMLSelectElement).value)}>
      {#each options as [v, l] (v)}<option value={v}>{l}</option>{/each}
    </select>
  </label>
{/snippet}

{#snippet groupSelect(label: string, groups: { id: string; name: string; visits: number }[], value: string, set: (v: string) => void)}
  <select aria-label={label} class="{selectClass} mb-2" {value} onchange={(e) => set((e.target as HTMLSelectElement).value)}>
    {#each groups as g (g.id)}<option value={g.id}>{g.name} ({g.visits} 回)</option>{/each}
  </select>
{/snippet}

{#if failed}
  <Unavailable title="統計を読み込めませんでした" hint="しばらくしてから再読み込みしてください。" />
{:else if !doc}
  <Skeleton />
{:else if games.length === 0}
  <Panel><EmptyState title="まだ勝利か死亡で終わったゲームがありません" /></Panel>
{:else}
  <div class="space-y-4">
    <nav class="flex gap-1 border-b border-bg-3 overflow-x-auto" aria-label="キャラクター">
      {#each tabs as t (t.key)}
        <button type="button" onclick={() => (selected = t.key)}
                class="px-3 py-2 text-sm whitespace-nowrap border-b-2 focus-ring {t.key === current?.key ? 'text-slate-100 font-semibold border-accent' : 'text-slate-400 border-transparent hover:text-slate-200'}">
          {t.name}<span class="ml-1 text-xs text-slate-500 tabular">{t.games}</span>
        </button>
      {/each}
    </nav>

    <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
      {@render filter('f-mode', 'ゲームモード', filters.mode, [[ALL, 'すべて'], ...modeOptions.map(m => [m, GAME_MODE_LABEL[m] ?? m] as [string, string])], v => (filters.mode = v))}
      {@render filter('f-asc', 'アセンション', filters.asc, [[ALL, 'すべて'], ...ascOptions.map(a => [a, a] as [string, string])], v => (filters.asc = v))}
      {@render filter('f-players', '人数', filters.players, [[ALL, 'すべて'], [SOLO, 'ソロ'], [MULTI, 'マルチプレイ']], v => (filters.players = v))}
      {@render filter('f-period', '期間', filters.period, [['all', '全期間'], ['30d', '直近 30 日']], v => (filters.period = v as Filters['period']))}
      <label for="f-low" class="flex items-center gap-1.5 text-xs text-slate-400">
        <input id="f-low" type="checkbox" class="focus-ring accent-accent" bind:checked={hideLow} />{MIN_GAMES} ゲーム未満を隠す
      </label>
    </div>

    {#if !current || !stats}
      <Panel><EmptyState title="条件に合うゲームがありません" /></Panel>
    {:else}
      <Panel title="{current.name}の概要">
        <div class="grid grid-cols-2 sm:grid-cols-5 gap-3">
          <StatTile label="ゲーム数" value={stats.games} />
          <StatTile label="勝率" value={pct(base)} sub="{stats.wins} 勝" />
          <StatTile label="平均到達階" value={stats.avgFloor == null ? '—' : stats.avgFloor.toFixed(1)} />
          <StatTile label="勝利時の最高アセンション" value={stats.bestAscension ?? '—'} />
          <StatTile label="最長連勝" value={stats.longestStreak} />
        </div>
      </Panel>

      <div class="grid md:grid-cols-2 gap-4 items-start">
        <Panel title="カード">
          <StatsTable columns={cardColumns} rows={stats.cards} rowKey={r => r.id} count={r => r.games} {hideLow} initialSort={3} />
        </Panel>
        <Panel title="レリック">
          <StatsTable columns={relicColumns} rows={stats.relics} rowKey={r => r.id} count={r => r.games} {hideLow} initialSort={1} />
        </Panel>
      </div>

      <div class="grid md:grid-cols-2 gap-4 items-start">
        <Panel title="敵">
          <StatsTable columns={enemyColumns} rows={stats.enemies} rowKey={r => r.id} count={r => r.fights} {hideLow} initialSort={4} />
        </Panel>
        <Panel title="進行">
          <div class="space-y-4">
            <div class="grid grid-cols-2 sm:grid-cols-3 gap-3">
              {#each stats.acts as a (a.act)}<StatTile label="Act {a.act} 突破" value={pct(a.rate)} />{/each}
            </div>
            <div>
              <div class="text-xs text-slate-400 mb-1">死亡時の階</div>
              {#if stats.deathFloors.length === 0}
                <div class="text-xs text-slate-500">死んだゲームはありません</div>
              {:else}
                <div class="flex items-end gap-1.5 h-24 overflow-x-auto">
                  {#each stats.deathFloors as d (d.floor)}
                    <div class="flex flex-col items-center gap-1 shrink-0" title="{d.floor} 階: {d.games} ゲーム">
                      <span class="text-[10px] text-slate-400 tabular">{d.games}</span>
                      <div class="w-5 bg-bad/70 rounded-t" style:height="{Math.round((56 * d.games) / deathMax)}px"></div>
                      <span class="text-[10px] text-slate-500 tabular">{d.floor}</span>
                    </div>
                  {/each}
                </div>
              {/if}
            </div>
            <div>
              <div class="text-xs text-slate-400 mb-1">休憩所の選択</div>
              {#if restTotal === 0}
                <div class="text-xs text-slate-500">まだありません</div>
              {:else}
                <div class="space-y-1.5">
                  {#each stats.rest as r (r.name)}
                    <div class="grid grid-cols-[5rem_1fr_3rem] items-center gap-2 text-sm">
                      <span class="text-slate-300 truncate">{r.name}</span>
                      <div class="h-2 bg-bg-3 rounded"><div class="h-2 bg-ok rounded" style:width="{(100 * r.count) / restTotal}%"></div></div>
                      <span class="text-right tabular text-slate-400">{pct(r.count / restTotal)}</span>
                    </div>
                  {/each}
                </div>
              {/if}
            </div>
          </div>
        </Panel>
      </div>

      <div class="grid md:grid-cols-2 gap-4 items-start">
        <Panel title="選択">
          <div class="space-y-4">
            <div>
              <div class="text-xs text-slate-400 mb-1">エンシェント</div>
              {#if ancient}
                {@render groupSelect('エンシェント', stats.ancients, ancient.id, v => (ancientSel = v))}
                {#key ancient.id}
                  <StatsTable columns={ancientColumns} rows={ancient.rows} rowKey={r => r.title} count={r => r.chosen} {hideLow} initialSort={2} />
                {/key}
              {:else}
                <div class="text-xs text-slate-500">まだありません</div>
              {/if}
            </div>
            <div>
              <div class="text-xs text-slate-400 mb-1">イベント</div>
              {#if event}
                {@render groupSelect('イベント', stats.events, event.id, v => (eventSel = v))}
                {#key event.id}
                  <StatsTable columns={eventColumns} rows={event.rows} rowKey={r => r.title} count={r => r.chosen} {hideLow} initialSort={1} />
                {/key}
              {:else}
                <div class="text-xs text-slate-500">まだありません</div>
              {/if}
            </div>
          </div>
        </Panel>
        <Panel title="マルチプレイ">
          <div class="space-y-4">
            <div class="grid grid-cols-2 gap-3">
              <StatTile label="ソロの勝率" value={pct(rate(stats.solo.wins, stats.solo.games))} sub="{stats.solo.games} ゲーム" />
              <StatTile label="マルチプレイの勝率" value={pct(rate(stats.multi.wins, stats.multi.games))} sub="{stats.multi.games} ゲーム" />
            </div>
            <StatsTable columns={partnerColumns} rows={stats.partners} rowKey={r => r.steamId} count={r => r.games} {hideLow} initialSort={1} />
          </div>
        </Panel>
      </div>
    {/if}
  </div>
{/if}
