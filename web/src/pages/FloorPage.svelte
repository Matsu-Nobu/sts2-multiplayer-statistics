<script lang="ts">
  // 階の詳細 (spec ui.md §3.3)。パンくず・前後の階・戦闘詳細へのリンク・run-overview.md §2.4 の内容。
  import { useSession } from '../lib/session';
  import { buildFloorSummaries, roomVisual } from '../lib/runOverview';
  import { link, href } from '../lib/router.svelte';
  import StatTile from '../components/ui/StatTile.svelte';
  import Badge from '../components/ui/Badge.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  import FloorDetail from '../components/FloorDetail.svelte';

  interface Props { floor: number }
  let { floor }: Props = $props();
  const s = useSession();

  let floors = $derived(buildFloorSummaries(s.doc.events, s.player));
  let idx = $derived(floors.findIndex(f => f.floor === floor));
  let f = $derived(idx >= 0 ? floors[idx] : null);
  let prev = $derived(idx > 0 ? floors[idx - 1] : null);
  let next = $derived(idx >= 0 && idx < floors.length - 1 ? floors[idx + 1] : null);
  const sign = (n: number) => (n > 0 ? `+${n}` : `${n}`);
</script>

<nav aria-label="パンくず" class="flex flex-wrap items-center justify-between gap-2 text-sm mb-4">
  <ol class="flex items-center gap-1.5 text-slate-400">
    <li><a class="link" href={href({ name: 'floors' })} use:link={{ name: 'floors' }}>階</a></li>
    <li aria-hidden="true">›</li>
    <li class="text-slate-200" aria-current="page">{floor} 階</li>
  </ol>
  <div class="flex items-center gap-3">
    {#if prev}<a class="link" href={href({ name: 'floor', floor: prev.floor })} use:link={{ name: 'floor', floor: prev.floor }}>‹ {prev.floor} 階</a>{:else}<span class="text-slate-600">‹ 前の階</span>{/if}
    {#if next}<a class="link" href={href({ name: 'floor', floor: next.floor })} use:link={{ name: 'floor', floor: next.floor }}>{next.floor} 階 ›</a>{:else}<span class="text-slate-600">次の階 ›</span>{/if}
  </div>
</nav>

{#if !f}
  <div class="bg-bg-1 border border-bg-3 rounded-lg">
    <EmptyState title={`${floor} 階の記録がありません`} hint="階の一覧から選んでください。" />
  </div>
  <div class="mt-3 text-sm"><a class="link" href={href({ name: 'floors' })} use:link={{ name: 'floors' }}>階の一覧へ</a></div>
{:else}
  {@const v = roomVisual(f.room_type)}
  <div class="space-y-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div class="flex flex-wrap items-center gap-2 min-w-0">
        <h1 class="text-xl font-semibold text-slate-100">{f.encounter_name ?? v.label}</h1>
        <Badge label={v.label} color={v.color} />
        {#if f.victory === true}<Badge label="勝利" tone="ok" />{:else if f.victory === false}<Badge label="敗北" tone="bad" />{/if}
        {#if s.playerIds.length > 1}<span class="text-xs text-slate-400">{s.playerNames[s.player] ?? s.player} の視点</span>{/if}
      </div>
      {#if f.combat_index != null}
        <a class="link text-sm" href={href({ name: 'combat', combat: f.combat_index, view: 'summary' })} use:link={{ name: 'combat', combat: f.combat_index, view: 'summary' }}>この戦闘の詳細へ ›</a>
      {/if}
    </div>

    <div class="grid grid-cols-2 gap-3 max-w-xl">
      <StatTile label="HP" value={`${f.hp_out}/${f.max_hp_out}`} sub={`入場時 ${f.hp_in}/${f.max_hp_in} (${sign(f.hp_out - f.hp_in)})`} />
      <StatTile label="ゴールド" value={f.gold_out} sub={`入場時 ${f.gold_in} (${sign(f.gold_out - f.gold_in)})`} />
    </div>

    <FloorDetail {f} />
  </div>
{/if}
