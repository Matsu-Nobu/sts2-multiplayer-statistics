<script lang="ts">
  // 階セレクタ (spec ui.md §3.2): 各階のドロップダウン + 前 / 次。選ぶと URL が変わる。
  import { roomVisual, type FloorSummary } from '../lib/runOverview';
  import { navigate } from '../lib/router.svelte';

  interface Props { floors: FloorSummary[]; current: number }
  let { floors, current }: Props = $props();

  let idx = $derived(floors.findIndex(f => f.floor === current));
  let prev = $derived(idx > 0 ? floors[idx - 1] : null);
  let next = $derived(idx >= 0 && idx < floors.length - 1 ? floors[idx + 1] : null);
  const go = (n: number) => navigate({ name: 'floor', floor: n });
  const label = (f: FloorSummary) => {
    const v = roomVisual(f.room_type);
    return f.encounter_name && f.encounter_name !== v.label ? `${f.floor} 階 - ${v.label} (${f.encounter_name})` : `${f.floor} 階 - ${v.label}`;
  };
  const btn = 'px-2.5 py-2 rounded-md border border-bg-3 bg-bg-1 text-sm focus-ring';
</script>

<div class="flex items-center gap-2">
  <label class="sr-only" for="floor-select">階</label>
  <select id="floor-select"
          class="bg-bg-1 border border-bg-3 rounded-md px-3 py-2 text-sm text-slate-200 hover:bg-bg-2 focus-ring min-w-0 w-full sm:w-auto sm:min-w-[20rem]"
          value={String(current)} onchange={(e) => go(Number((e.target as HTMLSelectElement).value))}>
    {#each floors as f (f.floor)}<option value={String(f.floor)}>{label(f)}</option>{/each}
  </select>
  <button type="button" class="{btn} {prev ? 'text-slate-200 hover:bg-bg-2' : 'text-slate-600'}" disabled={!prev} aria-label="前の階" onclick={() => prev && go(prev.floor)}>‹ 前</button>
  <button type="button" class="{btn} {next ? 'text-slate-200 hover:bg-bg-2' : 'text-slate-600'}" disabled={!next} aria-label="次の階" onclick={() => next && go(next.floor)}>次 ›</button>
</div>
