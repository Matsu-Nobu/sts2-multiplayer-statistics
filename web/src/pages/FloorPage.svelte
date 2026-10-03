<script lang="ts">
  // 階 (spec ui.md §3.2): 階セレクタ + 1 階分の詳細 (run-overview.md §2.4)。floor が null なら最初の階。
  import { useSession } from '../lib/session';
  import { buildFloorSummaries, roomVisual } from '../lib/runOverview';
  import { link, href } from '../lib/router.svelte';
  import StatTile from '../components/ui/StatTile.svelte';
  import Badge from '../components/ui/Badge.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  import FloorDetail from '../components/FloorDetail.svelte';
  import FloorSelector from '../components/FloorSelector.svelte';

  interface Props { floor: number | null }
  let { floor }: Props = $props();
  const s = useSession();

  let floors = $derived(buildFloorSummaries(s.doc.events, s.player));
  let f = $derived(floor == null ? floors[0] ?? null : floors.find(x => x.floor === floor) ?? floors[0] ?? null);
  const sign = (n: number) => (n > 0 ? `+${n}` : `${n}`);
</script>

{#if !f}
  <div class="bg-bg-1 border border-bg-3 rounded-lg"><EmptyState title="階ごとの記録がありません" hint="1 階目を出ると表示されます。" /></div>
{:else}
  {@const v = roomVisual(f.room_type)}
  <div class="space-y-4">
    <FloorSelector {floors} current={f.floor} />

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
