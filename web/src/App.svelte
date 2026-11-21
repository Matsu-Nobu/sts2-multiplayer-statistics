<script lang="ts">
  // 共通の上の帯と、URL で決まる画面 (spec ui.md「サイト全体の構成」)。
  import type { SessionDoc } from './lib/types';
  import { fetchSession, HttpError } from './lib/api';
  import { route } from './lib/router.svelte';
  import TopBar from './components/TopBar.svelte';
  import SessionView from './components/SessionView.svelte';
  import Skeleton from './components/ui/Skeleton.svelte';
  import Unavailable from './components/Unavailable.svelte';
  import UserPage from './pages/UserPage.svelte';
  import HomePage from './pages/HomePage.svelte';
  import SettingsPage from './pages/SettingsPage.svelte';

  let doc = $state<SessionDoc | null>(null);
  let etag: string | null = null;       // $state にしない（読み書き両方するため）
  let live = $state<'live' | 'final' | 'offline'>('offline');
  let lastUpdated = $state<Date | null>(null);
  let status = $state<'loading' | 'ok' | 'notfound' | 'error'>('loading');

  let iv: ReturnType<typeof setInterval> | null = null;

  async function fetchOnce(id: string) {
    try {
      const r = await fetchSession(id, etag);
      if (r.doc) {
        doc = r.doc;
        etag = r.etag;
        live = r.doc.session.outcome ? 'final' : 'live';
        // 終わったセッション (勝利・死亡・放棄) はもう変わらないので取り直しを止める
        if (r.doc.session.outcome && iv != null) { clearInterval(iv); iv = null; }
      }
      lastUpdated = new Date();
      status = 'ok';
    } catch (e) {
      live = 'offline';
      status = e instanceof HttpError && e.status === 404 ? 'notfound' : (doc ? 'ok' : 'error');
    }
  }

  // 起動時に1度だけ実行されるよう、effect の本体は何も読まない構成にする
  $effect(() => {
    const id = route.sessionId;
    if (id == null) return;
    iv = setInterval(() => fetchOnce(id), 10_000);
    fetchOnce(id);
    return () => { if (iv != null) clearInterval(iv); };
  });
</script>

<TopBar />

{#if route.section === 'home'}
  <HomePage />
{:else if route.section === 'settings'}
  <SettingsPage />
{:else if route.section === 'user' && route.userId != null}
  {#key route.userId}<UserPage steamId={route.userId} />{/key}
{:else if route.section === 'session'}
  {#if status === 'notfound'}
    <Unavailable title="このランは見つかりません" hint="削除されたか、非公開のランです。" />
  {:else if doc}
    <SessionView {doc} {live} {lastUpdated} />
  {:else if status === 'error'}
    <Unavailable title="ランを読み込めませんでした" hint="しばらくしてから再読み込みしてください。" />
  {:else}
    <Skeleton />
  {/if}
{:else}
  <Unavailable title="ページが見つかりません" />
{/if}
