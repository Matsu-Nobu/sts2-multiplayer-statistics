<script lang="ts">
  // ユーザーページ (spec user-pages.md「ユーザーページ」): その人が参加したランの一覧。本人なら各行の「…」でランの設定を開く。
  import { userHref, type RunRelation } from '../lib/auth.svelte';
  import RunSettings from '../components/RunSettings.svelte';
  import Panel from '../components/ui/Panel.svelte';
  import Badge from '../components/ui/Badge.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  import Unavailable from '../components/Unavailable.svelte';
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

  async function load() {
    const res = await fetch(`/api/users/${encodeURIComponent(steamId)}`);
    if (res.status === 403) { status = 'private'; return; }
    if (res.status === 404) { status = 'notfound'; return; }
    if (!res.ok) { status = 'error'; return; }
    doc = (await res.json()) as UserPageDoc;
    status = 'ok';
  }
  $effect(() => { load(); });

  const relationOf = (r: Run): RunRelation => ({ is_participant: true, is_recorder: r.is_recorder, visibility: r.visibility, hidden: r.hidden });

  const OUTCOME: Record<string, { label: string; tone: 'ok' | 'bad' | 'neutral' }> = {
    victory: { label: '勝利', tone: 'ok' }, death: { label: '死亡', tone: 'bad' }, abandoned: { label: '放棄', tone: 'neutral' },
  };
  const outcomeOf = (o: Run['outcome']) => (o ? OUTCOME[o] : null) ?? { label: '進行中', tone: 'neutral' as const };
  const fmtDate = (s: string) => {
    const d = new Date(s);
    return Number.isNaN(d.getTime()) ? s : d.toLocaleString('ja-JP', { year: 'numeric', month: 'numeric', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  };
</script>

{#if doc}
  <header class="border-b border-bg-3 bg-bg-1">
    <div class="max-w-7xl mx-auto px-4 py-4">
      <h1 class="text-xl font-semibold text-slate-100 break-words">{doc.user.display_name}</h1>
      {#if doc.is_me}<div class="mt-1 text-xs text-slate-500">ユーザーページは{doc.user.profile_public ? '公開中' : '非公開 (自分だけ見られる)'}です。<a href="/settings" class="text-accent hover:underline">設定</a>で変えられます。</div>{/if}
    </div>
  </header>
{/if}

{#if status === 'loading'}
  <Skeleton />
{:else if status === 'private'}
  <Unavailable title="このユーザーのページは公開されていません" />
{:else if status === 'notfound'}
  <Unavailable title="ユーザーが見つかりません" hint="URL を確認してください。" />
{:else if status === 'error' || !doc}
  <Unavailable title="ユーザーページを読み込めませんでした" hint="しばらくしてから再読み込みしてください。" />
{:else}
<main class="max-w-7xl mx-auto px-4 py-6 space-y-4">
    <Panel title="ラン" flush>
      {#if doc.runs.length === 0}
        <EmptyState title="まだランがありません" />
      {:else}
        <!-- 狭い画面: 1 つのランを 1 枚のカードにする (表だと「…」が画面の外に隠れる。spec ui.md「狭い画面 (スマホ)」) -->
        <ul class="sm:hidden divide-y divide-bg-3">
          {#each doc.runs as r (r.session_id)}
            {@const o = outcomeOf(r.outcome)}
            <li class="px-4 py-3 flex items-start justify-between gap-3">
              <div class="min-w-0 space-y-1">
                <a href={`/s/${r.session_id}`} class="text-accent hover:underline focus-ring rounded text-sm">{fmtDate(r.created_at)}</a>
                <div class="text-sm text-slate-200">{r.character_name ?? r.character_id ?? '—'}<span class="text-slate-500"> ・ アセンション {r.ascension ?? '—'} ・ {r.final_floor ?? '—'} 階</span></div>
                <div class="flex flex-wrap gap-1">
                  <Badge label={o.label} tone={o.tone} />
                  {#if doc.is_me && r.visibility === 'private'}<Badge label="非公開" />{/if}
                  {#if doc.is_me && r.hidden}<Badge label="ページに載せない" />{/if}
                </div>
                {#if r.participants.some(p => p.steam_id !== doc!.user.steam_id)}
                  <div class="text-xs text-slate-400">{#each r.participants.filter(p => p.steam_id !== doc!.user.steam_id) as p, i (p.steam_id)}{#if i > 0}、{/if}<a href={userHref(p.steam_id)} class="hover:underline">{p.display_name || p.steam_id}</a>{/each}</div>
                {/if}
              </div>
              {#if doc.is_me}<RunSettings sessionId={r.session_id} relation={relationOf(r)} label="…" onchanged={load} ondeleted={load} />{/if}
            </li>
          {/each}
        </ul>
        <div class="dt-wrap hidden sm:block">
          <table class="dt">
            <thead>
              <tr>
                <th>日付</th><th>キャラクター</th><th class="num">アセンション</th><th>結果</th><th class="num">到達階</th><th>一緒に遊んだ人</th>
                {#if doc.is_me}<th><span class="sr-only">設定</span></th>{/if}
              </tr>
            </thead>
            <tbody>
              {#each doc.runs as r (r.session_id)}
                {@const o = outcomeOf(r.outcome)}
                <tr>
                  <td class="whitespace-nowrap"><a href={`/s/${r.session_id}`} class="text-accent hover:underline focus-ring rounded">{fmtDate(r.created_at)}</a></td>
                  <td>{r.character_name ?? r.character_id ?? '—'}</td>
                  <td class="num">{r.ascension ?? '—'}</td>
                  <td class="whitespace-nowrap">
                    <Badge label={o.label} tone={o.tone} />
                    {#if doc.is_me && r.visibility === 'private'}<Badge label="非公開" />{/if}
                    {#if doc.is_me && r.hidden}<Badge label="ページに載せない" />{/if}
                  </td>
                  <td class="num">{r.final_floor ?? '—'}</td>
                  <td class="text-xs">
                    {#each r.participants.filter(p => p.steam_id !== doc!.user.steam_id) as p, i (p.steam_id)}{#if i > 0}、{/if}<a href={userHref(p.steam_id)} class="hover:underline focus-ring rounded">{p.display_name || p.steam_id}</a>{:else}<span class="text-slate-600">—</span>{/each}
                  </td>
                  {#if doc.is_me}
                    <td class="text-right"><RunSettings sessionId={r.session_id} relation={relationOf(r)} label="…" onchanged={load} ondeleted={load} /></td>
                  {/if}
                </tr>
              {/each}
            </tbody>
          </table>
        </div>
      {/if}
    </Panel>
</main>
{/if}
