<script lang="ts">
  // 戦闘セレクタ (spec ui.md §3.4): 「全戦闘の合計」と各戦闘のドロップダウン + 前 / 次。選ぶと URL が変わる。
  import { useSession } from '../lib/session';
  import { navigate, type CombatViewName } from '../lib/router.svelte';

  interface Props { current: number | null; view?: CombatViewName }
  let { current, view = 'summary' }: Props = $props();
  const s = useSession();

  const ROOM_TAG: Record<string, string> = { Elite: ' [エリート]', Boss: ' [ボス]' };
  let idx = $derived(current == null ? -1 : s.combats.findIndex(c => c.combat_index === current));
  let prev = $derived(idx > 0 ? s.combats[idx - 1] : null);
  let next = $derived(idx >= 0 && idx < s.combats.length - 1 ? s.combats[idx + 1] : null);

  function go(v: string) {
    if (v === 'all') navigate({ name: 'combats' });
    else navigate({ name: 'combat', combat: Number(v), view });
  }
  const btn = 'px-2.5 py-2 rounded-md border border-bg-3 bg-bg-1 text-sm focus-ring';
</script>

<div class="flex items-center gap-2">
  <label class="sr-only" for="combat-select">戦闘</label>
  <select id="combat-select"
          class="bg-bg-1 border border-bg-3 rounded-md px-3 py-2 text-sm text-slate-200 hover:bg-bg-2 focus-ring min-w-0 w-full sm:w-auto sm:min-w-[20rem]"
          value={current == null ? 'all' : String(current)} onchange={(e) => go((e.target as HTMLSelectElement).value)}>
    <option value="all">全戦闘の合計（{s.combats.length} 戦）</option>
    {#each s.combats as c, i (c.combat_index)}
      <option value={String(c.combat_index)}>{i + 1}. {c.encounter_name ?? '不明な遭遇'}{ROOM_TAG[c.room_type ?? ''] ?? ''}</option>
    {/each}
  </select>
  <button type="button" class="{btn} {prev ? 'text-slate-200 hover:bg-bg-2' : 'text-slate-600'}" disabled={!prev} aria-label="前の戦闘"
          onclick={() => prev && go(String(prev.combat_index))}>‹ 前</button>
  <button type="button" class="{btn} {next ? 'text-slate-200 hover:bg-bg-2' : 'text-slate-600'}" disabled={!next} aria-label="次の戦闘"
          onclick={() => next && go(String(next.combat_index))}>次 ›</button>
</div>
