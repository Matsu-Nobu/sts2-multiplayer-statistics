<script lang="ts">
  // カード別 (選んだプレイヤー)。上位 10 件 + 「すべて表示」(spec ui.md §3.4)。
  import type { CardStats } from '../lib/types';
  import { formatPowerName } from '../lib/powers';
  import { cardTypeLabel } from '../lib/labels';
  import Panel from './ui/Panel.svelte';
  import EmptyState from './ui/EmptyState.svelte';

  interface Props { cards: CardStats[]; title: string; scope?: string; powerNames?: Record<string, string> }
  let { cards, title, scope, powerNames = {} }: Props = $props();

  type SortKey = 'card_name' | 'play_count' | 'damage_dealt' | 'max_single_hit' | 'block_provided';
  let sortKey: SortKey = $state('damage_dealt');
  let desc = $state(true);
  let showAll = $state(false);
  const LIMIT = 10;

  let sorted = $derived(
    [...cards].sort((a, b) => {
      const av = a[sortKey], bv = b[sortKey];
      if (typeof av === 'string') return desc ? (bv as string).localeCompare(av, 'ja') : av.localeCompare(bv as string, 'ja');
      return desc ? (bv as number) - av : av - (bv as number);
    })
  );
  let shown = $derived(showAll ? sorted : sorted.slice(0, LIMIT));

  function setSort(k: SortKey) { if (sortKey === k) desc = !desc; else { sortKey = k; desc = k !== 'card_name'; } }
  const arrow = (k: SortKey) => (sortKey === k ? (desc ? ' ↓' : ' ↑') : '');
  const aria = (k: SortKey) => (sortKey === k ? (desc ? 'descending' : 'ascending') : 'none');
  const fmtDebuffs = (d: Record<string, number>) => Object.entries(d).map(([k, v]) => `${formatPowerName(k, powerNames)} ${v}`).join('、');

  const COLS: { key: SortKey; label: string; num: boolean }[] = [
    { key: 'card_name', label: 'カード', num: false }, { key: 'play_count', label: '使用', num: true },
    { key: 'damage_dealt', label: 'ダメージ', num: true }, { key: 'max_single_hit', label: '最大ダメージ', num: true },
    { key: 'block_provided', label: 'ブロック', num: true },
  ];
</script>

<Panel {title} {scope} flush>
  {#snippet actions()}<span class="text-xs text-slate-500 tabular">{cards.length} 種類</span>{/snippet}
  {#if cards.length === 0}
    <EmptyState title="カードの記録がありません" />
  {:else}
    <div class="dt-wrap">
      <table class="dt">
        <thead>
          <tr>
            {#each COLS as c (c.key)}
              <th class="sortable {c.num ? 'num' : ''}" aria-sort={aria(c.key)}>
                <button type="button" class="focus-ring rounded-sm" onclick={() => setSort(c.key)}>{c.label}{arrow(c.key)}</button>
              </th>
            {/each}
            <th>デバフ</th>
          </tr>
        </thead>
        <tbody>
          {#each shown as c (c.card_id)}
            <tr>
              <td class="whitespace-nowrap"><span class="text-slate-100">{c.card_name}</span><span class="text-xs text-slate-500 ml-2">{cardTypeLabel(c.card_type)}</span></td>
              <td class="num">{c.play_count}</td>
              <td class="num">{c.damage_dealt}</td>
              <td class="num">{c.max_single_hit}</td>
              <td class="num">{c.block_provided}</td>
              <td class="text-xs text-slate-400 min-w-[6rem]">{fmtDebuffs(c.debuffs_applied)}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
    {#if sorted.length > LIMIT}
      <div class="px-4 py-2 border-t border-bg-3">
        <button type="button" class="link text-sm" onclick={() => showAll = !showAll}>{showAll ? '上位 10 件だけ表示' : `すべて表示 (${sorted.length} 件)`}</button>
      </div>
    {/if}
  {/if}
</Panel>
