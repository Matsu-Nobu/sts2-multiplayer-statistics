<script lang="ts">
  import type { SessionMeta } from '../lib/types';

  interface Props {
    session: SessionMeta;
    live: 'live' | 'final' | 'offline';
    lastUpdated: Date | null;
  }
  let { session, live, lastUpdated }: Props = $props();

  function fmtRel(d: Date | null): string {
    if (!d) return '—';
    const ms = Date.now() - d.getTime();
    const s = Math.round(ms / 1000);
    if (s < 60) return `${s}秒前`;
    const m = Math.round(s / 60);
    if (m < 60) return `${m}分前`;
    const h = Math.round(m / 60);
    return `${h}時間前`;
  }

  let outcomeLabel = $derived.by(() => {
    if (!session.outcome) return '進行中';
    if (session.outcome === 'victory') return '勝利';
    if (session.outcome === 'death') return '死亡';
    return '中断';
  });
  let outcomeColor = $derived.by(() => {
    if (!session.outcome) return 'text-warn';
    if (session.outcome === 'victory') return 'text-ok';
    if (session.outcome === 'death') return 'text-bad';
    return 'text-slate-400';
  });
  let liveLabel = $derived.by(() => {
    if (live === 'live') return 'ライブ';
    if (live === 'final') return '完了';
    return 'オフライン';
  });
</script>

<header class="border-b border-bg-3 bg-bg-1">
  <div class="max-w-7xl mx-auto px-4 py-4 flex items-center justify-between">
    <div>
      <div class="text-xs uppercase tracking-widest text-slate-500">STS2 統計</div>
      <h1 class="text-2xl font-semibold mt-1">{session.host_name ?? '不明なプレイヤー'}</h1>
      <div class="text-sm text-slate-400 mt-1 flex flex-wrap items-center gap-2">
        {#if session.character_id}<span>⚔ {session.character_id}</span><span>·</span>{/if}
        {#if session.ascension != null}<span>Ascension {session.ascension}</span><span>·</span>{/if}
        {#if session.final_floor != null}<span>{session.final_floor}階</span><span>·</span>{/if}
        <span class="font-medium {outcomeColor}">{outcomeLabel}</span>
      </div>
    </div>

    <div class="text-right text-xs text-slate-500">
      <div class="flex items-center gap-2 justify-end">
        {#if live === 'live'}
          <span class="w-2 h-2 rounded-full bg-ok inline-block animate-pulse"></span>
          <span class="text-ok">{liveLabel}</span>
        {:else if live === 'final'}
          <span class="w-2 h-2 rounded-full bg-slate-500 inline-block"></span>
          <span>{liveLabel}</span>
        {:else}
          <span class="w-2 h-2 rounded-full bg-bad inline-block"></span>
          <span class="text-bad">{liveLabel}</span>
        {/if}
      </div>
      <div class="mt-1">最終更新 {fmtRel(lastUpdated)}</div>
    </div>
  </div>
</header>
