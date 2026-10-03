<script lang="ts">
  import type { SessionDoc } from './lib/types';
  import { fetchSession } from './lib/api';
  import { mockSession } from './lib/mock';
  import SessionView from './components/SessionView.svelte';
  import Skeleton from './components/ui/Skeleton.svelte';
  import { route } from './lib/router.svelte';

  let doc = $state<SessionDoc | null>(null);
  let etag: string | null = null;       // $state にしない（読み書き両方するため）
  let live = $state<'live' | 'final' | 'offline'>('offline');
  let lastUpdated = $state<Date | null>(null);
  let error = $state<string | null>(null);

  const id = route.sessionId ?? 'demo';
  const isDemo = route.isDemo;

  let iv: ReturnType<typeof setInterval> | null = null;

  async function fetchOnce() {
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
    iv = setInterval(fetchOnce, 10_000);
    fetchOnce();
    return () => { if (iv != null) clearInterval(iv); };
  });
</script>

{#if doc}
  <SessionView {doc} {live} {lastUpdated} />
{:else if error}
  <div class="max-w-xl mx-auto mt-16 px-4 text-center">
    <div class="text-slate-200">セッションを読み込めませんでした</div>
    <div class="text-xs text-slate-500 mt-1">{error}。URL を確認するか、しばらくしてから再読み込みしてください。</div>
  </div>
{:else}
  <Skeleton />
{/if}
