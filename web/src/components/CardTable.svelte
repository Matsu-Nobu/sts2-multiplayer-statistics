<script lang="ts">
  import type { CardStats } from '../lib/types';

  interface Props {
    cards: CardStats[];
    title?: string;
  }
  let { cards, title }: Props = $props();

  type SortKey = 'card_name' | 'play_count' | 'damage_dealt' | 'max_single_hit' | 'block_provided';
  let sortKey: SortKey = $state('damage_dealt');
  let desc = $state(true);

  let sorted = $derived(
    [...cards].sort((a, b) => {
      const av = (a as any)[sortKey];
      const bv = (b as any)[sortKey];
      if (typeof av === 'string') return desc ? bv.localeCompare(av) : av.localeCompare(bv);
      return desc ? (bv - av) : (av - bv);
    })
  );

  function setSort(k: SortKey) {
    if (sortKey === k) desc = !desc;
    else { sortKey = k; desc = true; }
  }

  function fmtDebuffs(d: Record<string, number>) {
    return Object.entries(d).map(([k, v]) => `${k}: ${v}`).join(', ');
  }
</script>

<div class="bg-bg-1 border border-bg-3 rounded-lg overflow-hidden">
  {#if title}
    <div class="px-3 py-2 text-sm uppercase tracking-wide text-slate-400 border-b border-bg-3">{title}</div>
  {/if}
  <table class="w-full text-sm tabular">
    <thead class="bg-bg-2 text-slate-400 text-xs uppercase">
      <tr>
        <th class="text-left py-2 px-3 cursor-pointer" onclick={() => setSort('card_name')}>カード</th>
        <th class="text-right py-2 px-3 cursor-pointer" onclick={() => setSort('play_count')}>使用 {sortKey === 'play_count' ? (desc ? '↓' : '↑') : ''}</th>
        <th class="text-right py-2 px-3 cursor-pointer" onclick={() => setSort('damage_dealt')}>ダメージ {sortKey === 'damage_dealt' ? (desc ? '↓' : '↑') : ''}</th>
        <th class="text-right py-2 px-3 cursor-pointer" onclick={() => setSort('max_single_hit')}>最大単発 {sortKey === 'max_single_hit' ? (desc ? '↓' : '↑') : ''}</th>
        <th class="text-right py-2 px-3 cursor-pointer" onclick={() => setSort('block_provided')}>ブロック {sortKey === 'block_provided' ? (desc ? '↓' : '↑') : ''}</th>
        <th class="text-left py-2 px-3">デバフ</th>
      </tr>
    </thead>
    <tbody>
      {#each sorted as c (c.card_id)}
        <tr class="border-t border-bg-3 hover:bg-bg-2">
          <td class="py-2 px-3">
            <span>{c.card_name}</span>
            <span class="text-xs text-slate-500 ml-2">{c.card_type}</span>
          </td>
          <td class="text-right py-2 px-3">{c.play_count}</td>
          <td class="text-right py-2 px-3">{c.damage_dealt}</td>
          <td class="text-right py-2 px-3">{c.max_single_hit}</td>
          <td class="text-right py-2 px-3">{c.block_provided}</td>
          <td class="py-2 px-3 text-slate-400 text-xs">{fmtDebuffs(c.debuffs_applied)}</td>
        </tr>
      {/each}
      {#if sorted.length === 0}
        <tr><td colspan="6" class="text-center text-slate-500 py-3">カード履歴なし</td></tr>
      {/if}
    </tbody>
  </table>
</div>
