<script lang="ts">
  // ユーザーページ (spec user-pages.md「ユーザーページ」): その人が参加したランの一覧。本人なら公開・非公開・削除の操作。
  import { mutate, userHref } from '../lib/auth.svelte';
  import AuthMenu from '../components/AuthMenu.svelte';
  import Panel from '../components/ui/Panel.svelte';
  import Badge from '../components/ui/Badge.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  import Skeleton from '../components/ui/Skeleton.svelte';

  interface Run {
    session_id: string; created_at: string; character_id: string | null; character_name: string | null;
    ascension: number | null; outcome: 'victory' | 'death' | 'abandoned' | null; final_floor: number | null;
    visibility: 'public' | 'private'; hidden: boolean; is_recorder: boolean;
    participants: { steam_id: string; display_name: string }[];
  }
  interface UserPageDoc { user: { steam_id: string; display_name: string; profile_public: boolean }; is_me: boolean; runs: Run[] }

  interface Props { steamId: string }
  let { steamId }: Props = $props();

  let doc = $state<UserPageDoc | null>(null);
  let status = $state<'loading' | 'ok' | 'private' | 'notfound' | 'error'>('loading');
  let busy = $state(false);
  let actionError = $state<string | null>(null);

  async function load() {
    const res = await fetch(`/api/users/${encodeURIComponent(steamId)}`);
    if (res.status === 403) { status = 'private'; return; }
    if (res.status === 404) { status = 'notfound'; return; }
    if (!res.ok) { status = 'error'; return; }
    doc = (await res.json()) as UserPageDoc;
    status = 'ok';
  }
  $effect(() => { load(); });

  // 操作したあとは一覧を取り直す (backend の状態をそのまま映す)
  async function act(fn: () => Promise<void>) {
    busy = true;
    actionError = null;
    try { await fn(); await load(); }
    catch { actionError = '変更できませんでした。もう一度試してください。'; }
    finally { busy = false; }
  }
  const setProfilePublic = (v: boolean) => act(() => mutate('PATCH', '/api/me', { profile_public: v }));
  const setVisibility = (r: Run, v: Run['visibility']) => act(() => mutate('PATCH', `/api/sessions/${r.session_id}`, { visibility: v }));
  const setListed = (r: Run, listed: boolean) => act(() => mutate('PATCH', `/api/sessions/${r.session_id}/listing`, { hidden: !listed }));
  function remove(r: Run) {
    if (!confirm('このランの記録を削除します。元に戻せません。削除しますか？')) return;
    act(() => mutate('DELETE', `/api/sessions/${r.session_id}`));
  }

  const OUTCOME: Record<string, { label: string; tone: 'ok' | 'bad' | 'neutral' }> = {
    victory: { label: '勝利', tone: 'ok' }, death: { label: '死亡', tone: 'bad' }, abandoned: { label: '放棄', tone: 'neutral' },
  };
  const outcomeOf = (o: Run['outcome']) => (o ? OUTCOME[o] : null) ?? { label: '進行中', tone: 'neutral' as const };
  const fmtDate = (s: string) => {
    const d = new Date(s);
    return Number.isNaN(d.getTime()) ? s : d.toLocaleString('ja-JP', { year: 'numeric', month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  };
</script>

<header class="border-b border-bg-3 bg-bg-1">
  <div class="max-w-7xl mx-auto px-4 py-4 flex items-start justify-between gap-4">
    <h1 class="text-xl font-semibold text-slate-100 break-words">{doc?.user.display_name ?? ''}</h1>
    <AuthMenu />
  </div>
</header>

<main class="max-w-7xl mx-auto px-4 py-6 space-y-4">
  {#if status === 'loading'}
    <Skeleton />
  {:else if status === 'private'}
    <EmptyState title="このユーザーのページは公開されていません" />
  {:else if status === 'notfound'}
    <EmptyState title="ユーザーが見つかりません" hint="URL を確認してください。" />
  {:else if status === 'error' || !doc}
    <EmptyState title="ユーザーページを読み込めませんでした" hint="しばらくしてから再読み込みしてください。" />
  {:else}
    {#if doc.is_me}
      <Panel>
        <label class="flex items-center gap-2 text-sm text-slate-200">
          <input type="checkbox" checked={doc.user.profile_public} disabled={busy}
                 onchange={(e) => setProfilePublic((e.target as HTMLInputElement).checked)} class="focus-ring" />
          ユーザーページを公開する
        </label>
        <div class="text-xs text-slate-500 mt-1">公開すると、ほかの人もこのページで、あなたのランのうち公開しているものを見られます。</div>
      </Panel>
    {/if}
    {#if actionError}<div class="text-sm text-bad" role="alert">{actionError}</div>{/if}

    <Panel title="ラン" flush>
      {#if doc.runs.length === 0}
        <EmptyState title="まだランがありません" />
      {:else}
        <div class="dt-wrap">
          <table class="dt">
            <thead>
              <tr>
                <th>日付</th><th>キャラクター</th><th class="num">アセンション</th><th>結果</th><th class="num">到達階</th><th>一緒に遊んだ人</th>
                {#if doc.is_me}<th>設定</th>{/if}
              </tr>
            </thead>
            <tbody>
              {#each doc.runs as r (r.session_id)}
                {@const o = outcomeOf(r.outcome)}
                <tr>
                  <td class="whitespace-nowrap"><a href={`/s/${r.session_id}`} class="text-accent hover:underline focus-ring rounded">{fmtDate(r.created_at)}</a></td>
                  <td>{r.character_name ?? r.character_id ?? '—'}</td>
                  <td class="num">{r.ascension ?? '—'}</td>
                  <td><Badge label={o.label} tone={o.tone} /></td>
                  <td class="num">{r.final_floor ?? '—'}</td>
                  <td class="text-xs">
                    {#each r.participants.filter(p => p.steam_id !== doc!.user.steam_id) as p, i (p.steam_id)}{#if i > 0}、{/if}<a href={userHref(p.steam_id)} class="hover:underline focus-ring rounded">{p.display_name || p.steam_id}</a>{:else}<span class="text-slate-600">—</span>{/each}
                  </td>
                  {#if doc.is_me}
                    <td class="text-xs whitespace-nowrap">
                      <div class="flex flex-wrap items-center gap-x-3 gap-y-1">
                        {#if r.is_recorder}
                          <label class="inline-flex items-center gap-1">
                            <select class="bg-bg-2 border border-bg-3 rounded px-1.5 py-0.5 focus-ring" value={r.visibility} disabled={busy}
                                    onchange={(e) => setVisibility(r, (e.target as HTMLSelectElement).value as Run['visibility'])}>
                              <option value="public">公開</option><option value="private">非公開</option>
                            </select>
                          </label>
                        {/if}
                        <label class="inline-flex items-center gap-1">
                          <input type="checkbox" checked={!r.hidden} disabled={busy} class="focus-ring"
                                 onchange={(e) => setListed(r, (e.target as HTMLInputElement).checked)} />
                          自分のページに載せる
                        </label>
                        {#if r.is_recorder}
                          <button type="button" class="text-bad hover:underline focus-ring rounded disabled:opacity-50" disabled={busy} onclick={() => remove(r)}>削除</button>
                        {/if}
                      </div>
                    </td>
                  {/if}
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    </Panel>
  {/if}
</main>
