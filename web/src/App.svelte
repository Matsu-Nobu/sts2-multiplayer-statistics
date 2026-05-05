<script lang="ts">
  import type { SessionDoc } from './lib/types';
  import { fetchSession } from './lib/api';
  import { mockSession } from './lib/mock';
  import SessionView from './components/SessionView.svelte';

  let doc = $state<SessionDoc | null>(null);
  let etag: string | null = null;       // $state にしない（読み書き両方するため）
  let live = $state<'live' | 'final' | 'offline'>('offline');
  let lastUpdated = $state<Date | null>(null);
  let error = $state<string | null>(null);

  function parsePath(): { id: string; isDemo: boolean } {
    const url = new URL(window.location.href);
    if (url.searchParams.has('demo')) return { id: 'demo', isDemo: true };
    const m = url.pathname.match(/^\/s\/([^/]+)$/);
    if (m) return { id: m[1], isDemo: false };
    return { id: 'demo', isDemo: true };
  }

  const { id, isDemo } = parsePath();

  async function fetchOnce() {
    try {
      const r = await fetchSession(id, etag);
      if (r.doc) {
        doc = r.doc;
        etag = r.etag;
        live = r.doc.session.outcome ? 'final' : 'live';
      }
      lastUpdated = new Date();
      error = null;
    } catch (e) {
      live = 'offline';
      error = (e as Error).message;
    }
  }

  // 起動時に1度だけ実行されるよう、effect の本体は何も読まない構成にする
  $effect(() => {
    if (isDemo) {
      const d = mockSession();
      doc = d;
      live = d.session.outcome ? 'final' : 'live';
      lastUpdated = new Date();
      return;
    }
    fetchOnce();
    const iv = setInterval(fetchOnce, 10_000);
    return () => clearInterval(iv);
  });
</script>

{#if doc}
  <SessionView {doc} {live} {lastUpdated} />
{:else if error}
  <div class="p-8 text-bad">エラー: {error}</div>
{:else}
  <div class="p-8 text-slate-500">読み込み中…</div>
{/if}
