<script lang="ts">
  import type { TurnPayload } from '../lib/types';
  import CardTable from './CardTable.svelte';

  interface Props {
    turns: TurnPayload[];
    playerId: string;
  }
  let { turns, playerId }: Props = $props();
  let expanded = $state<Set<number>>(new Set());

  function toggle(n: number) {
    const s = new Set(expanded);
    if (s.has(n)) s.delete(n); else s.add(n);
    expanded = s;
  }
</script>

<div class="bg-bg-1 border border-bg-3 rounded-lg overflow-hidden">
  <div class="px-3 py-2 text-sm uppercase tracking-wide text-slate-400 border-b border-bg-3">
    ターン別内訳
  </div>
  <table class="w-full text-sm tabular">
    <thead class="bg-bg-2 text-slate-400 text-xs uppercase">
      <tr>
        <th class="text-left py-2 px-3 w-10"></th>
        <th class="text-right py-2 px-3">ターン</th>
        <th class="text-right py-2 px-3">与ダメージ</th>
        <th class="text-right py-2 px-3">被ダメージ</th>
        <th class="text-right py-2 px-3">ブロック</th>
        <th class="text-right py-2 px-3">エナジー</th>
        <th class="text-right py-2 px-3">使用</th>
        <th class="text-right py-2 px-3">ドロー</th>
      </tr>
    </thead>
    <tbody>
      {#each turns as t (t.turn_number)}
        {@const e = t.players[playerId]}
        {#if e}
          <tr
            class="border-t border-bg-3 hover:bg-bg-2 cursor-pointer"
            onclick={() => toggle(t.turn_number)}
          >
            <td class="py-2 px-3 text-slate-500">{expanded.has(t.turn_number) ? '▾' : '▸'}</td>
            <td class="text-right py-2 px-3">{t.turn_number}</td>
            <td class="text-right py-2 px-3 text-ok">{e.turn.damage_dealt}</td>
            <td class="text-right py-2 px-3 text-bad">{e.turn.damage_received}</td>
            <td class="text-right py-2 px-3 text-accent">{e.turn.block_gained_self}</td>
            <td class="text-right py-2 px-3">{e.turn.energy_used}</td>
            <td class="text-right py-2 px-3">{e.turn.cards_played}</td>
            <td class="text-right py-2 px-3">{e.turn.cards_drawn}</td>
          </tr>
          {#if expanded.has(t.turn_number)}
            <tr class="bg-bg-0/40">
              <td colspan="8" class="px-6 py-3">
                {#if e.turn.cards.length > 0}
                  <CardTable cards={e.turn.cards} />
                {:else}
                  <div class="text-slate-500 text-sm">このターンに使ったカードはありません</div>
                {/if}
              </td>
            </tr>
          {/if}
        {/if}
      {/each}
    </tbody>
  </table>
</div>
