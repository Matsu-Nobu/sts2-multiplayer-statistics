<script lang="ts">
  // ユーザーページ (本人にはマイページ。spec user-pages.md「ユーザーページ」): その人が遊んだゲームの一覧と絞り込み。
  import { userHref, type RunRelation } from '../lib/auth.svelte';
  import { GAME_MODE_LABEL } from '../lib/labels';
  import RunSettings from '../components/RunSettings.svelte';
  import Panel from '../components/ui/Panel.svelte';
  import Badge from '../components/ui/Badge.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  import Skeleton from '../components/ui/Skeleton.svelte';
  import Unavailable from '../components/Unavailable.svelte';

  interface Game {
    session_id: string; created_at: string; character_id: string | null; character_name: string | null; game_mode: string | null;
    ascension: number | null; outcome: 'victory' | 'death' | 'abandoned' | null; final_floor: number | null;
    visibility: 'public' | 'private'; hidden: boolean; is_recorder: boolean;
    participants: { steam_id: string; display_name: string }[];
  }
  interface UserPageDoc { user: { steam_id: string; display_name: string; profile_visibility: 'public' | 'friends' | 'private' }; is_me: boolean; runs: Game[] }

  interface Props { steamId: string }
  let { steamId }: Props = $props();

  let doc = $state<UserPageDoc | null>(null);
  let status = $state<'loading' | 'ok' | 'private' | 'notfound' | 'error'>('loading');

  async function load() {
    const res = await fetch(`/api/users/${encodeURIComponent(steamId)}`);
    if (res.status === 403) { status = 'private'; return; }
    if (res.status === 404) { status = 'notfound'; return; }
    if (!res.ok) { status = 'error'; return; }
    doc = (await res.json()) as UserPageDoc;
    status = 'ok';
  }
  $effect(() => { load(); });

  const relationOf = (g: Game): RunRelation => ({ is_participant: true, is_recorder: g.is_recorder, visibility: g.visibility, hidden: g.hidden });

  const OUTCOME: Record<string, { label: string; tone: 'ok' | 'bad' | 'neutral' }> = {
    victory: { label: '勝利', tone: 'ok' }, death: { label: '死亡', tone: 'bad' }, abandoned: { label: '放棄', tone: 'neutral' },
  };
  const outcomeKey = (g: Game) => g.outcome ?? 'running';
  const outcomeOf = (g: Game) => (g.outcome ? OUTCOME[g.outcome] : null) ?? { label: '進行中', tone: 'neutral' as const };
  const outcomeLabel = (k: string) => (k === 'running' ? '進行中' : OUTCOME[k]?.label ?? k);
  const characterOf = (g: Game) => g.character_name ?? g.character_id ?? '—';
  const modeOf = (g: Game) => (g.game_mode ? GAME_MODE_LABEL[g.game_mode] ?? g.game_mode : '—');
  const ascOf = (g: Game) => String(g.ascension ?? '—');
  const playersOf = (g: Game) => (g.participants.length > 1 ? 'マルチプレイ' : 'ソロ');
  const fmtDate = (s: string) => {
    const d = new Date(s);
    return Number.isNaN(d.getTime()) ? s : d.toLocaleString('ja-JP', { year: 'numeric', month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  };

  // 絞り込み (画面の中だけ。選べる値は一覧にあるゲームの値だけ)
  const ALL = '';
  let fChar = $state(ALL), fMode = $state(ALL), fAsc = $state(ALL), fOutcome = $state(ALL), fPlayers = $state(ALL);
  const uniq = (xs: string[]) => [...new Set(xs)];
  let games = $derived(doc?.runs ?? []);
  let charOptions = $derived(uniq(games.map(characterOf)).sort());
  let modeOptions = $derived(uniq(games.map(modeOf)));
  let ascOptions = $derived(uniq(games.map(ascOf)).sort((a, b) => Number(a) - Number(b)));
  let outcomeOptions = $derived(uniq(games.map(outcomeKey)));
  let playersOptions = $derived(uniq(games.map(playersOf)).sort());
  let filtered = $derived(games.filter(g =>
    (fChar === ALL || characterOf(g) === fChar) &&
    (fMode === ALL || modeOf(g) === fMode) &&
    (fAsc === ALL || ascOf(g) === fAsc) &&
    (fOutcome === ALL || outcomeKey(g) === fOutcome) &&
    (fPlayers === ALL || playersOf(g) === fPlayers)));
</script>

{#snippet filter(label: string, value: string, options: string[], set: (v: string) => void, show: (v: string) => string)}
  <label class="flex items-center gap-1.5 text-xs text-slate-400">
    {label}
    <select class="bg-bg-2 border border-bg-3 rounded-md px-2 py-1 text-sm text-slate-200 focus-ring" {value}
            onchange={(e) => set((e.target as HTMLSelectElement).value)}>
      <option value={ALL}>すべて</option>
      {#each options as o (o)}<option value={o}>{show(o)}</option>{/each}
    </select>
  </label>
{/snippet}

{#if status === 'loading'}
  <Skeleton />
{:else if status === 'private'}
  <Unavailable title="このユーザーのページは公開されていません" />
{:else if status === 'notfound'}
  <Unavailable title="ユーザーが見つかりません" hint="URL を確認してください。" />
{:else if status === 'error' || !doc}
  <Unavailable title="ページを読み込めませんでした" hint="しばらくしてから再読み込みしてください。" />
{:else}
  <main class="max-w-5xl mx-auto px-4 py-6 space-y-4">
    <div>
      <h1 class="text-xl font-semibold text-slate-100 break-words">{doc.user.display_name}</h1>
      {#if doc.is_me}
        <div class="mt-1 text-xs text-slate-500">
          {#if doc.user.profile_visibility === 'public'}このページは全体に公開中です。{:else if doc.user.profile_visibility === 'friends'}このページは Steam のフレンドに公開中です。{:else}このページは非公開です。{/if}<a href="/settings" class="text-accent hover:underline">設定</a>から変えられます。
        </div>
      {/if}
    </div>
    {#if games.length > 0}
      <div class="flex flex-wrap items-center gap-x-4 gap-y-2">
        {@render filter('キャラクター', fChar, charOptions, v => (fChar = v), v => v)}
        {@render filter('ゲームモード', fMode, modeOptions, v => (fMode = v), v => v)}
        {@render filter('アセンション', fAsc, ascOptions, v => (fAsc = v), v => v)}
        {@render filter('結果', fOutcome, outcomeOptions, v => (fOutcome = v), outcomeLabel)}
        {@render filter('人数', fPlayers, playersOptions, v => (fPlayers = v), v => v)}
        <span class="text-xs text-slate-500 tabular">{filtered.length} / {games.length} ゲーム</span>
      </div>
    {/if}

    <Panel flush>
      {#if games.length === 0}
        <EmptyState title="まだゲームがありません" />
      {:else if filtered.length === 0}
        <EmptyState title="条件に合うゲームがありません" />
      {:else}
        <ul class="divide-y divide-bg-3">
          {#each filtered as g (g.session_id)}
            {@const o = outcomeOf(g)}
            {@const others = g.participants.filter(p => p.steam_id !== doc!.user.steam_id)}
            <li class="px-4 py-2.5 flex flex-wrap sm:flex-nowrap items-center gap-x-3 gap-y-1.5">
              <a href={`/s/${g.session_id}`} class="order-1 shrink-0 sm:w-32 text-accent hover:underline focus-ring rounded text-sm tabular">{fmtDate(g.created_at)}</a>
              <div class="order-3 sm:order-2 w-full sm:w-auto flex flex-wrap sm:flex-nowrap gap-1.5 sm:shrink-0">
                <Badge label={characterOf(g)} tone="accent" />
                <Badge label={`アセンション ${g.ascension ?? '—'}`} />
                {#if g.game_mode}<Badge label={modeOf(g)} />{/if}
                <Badge label={o.label} tone={o.tone} />
                {#if g.final_floor != null}<Badge label={`${g.final_floor} 階`} />{/if}
                {#if doc.is_me && g.visibility === 'private'}<Badge label="非公開" tone="warn" />{/if}
                {#if doc.is_me && g.hidden}<Badge label="非表示" tone="warn" />{/if}
              </div>
              <div class="order-4 sm:order-3 w-full sm:w-auto min-w-0 sm:flex-1 truncate text-xs text-slate-400">{#each others as p, i (p.steam_id)}{#if i > 0}、{/if}<a href={userHref(p.steam_id)} class="hover:underline focus-ring rounded">{p.display_name || p.steam_id}</a>{/each}</div>
              {#if doc.is_me}<div class="order-2 sm:order-4 shrink-0 ml-auto"><RunSettings sessionId={g.session_id} relation={relationOf(g)} label="…" onchanged={load} ondeleted={load} /></div>{/if}
            </li>
          {/each}
        </ul>
      {/if}
    </Panel>
  </main>
{/if}
