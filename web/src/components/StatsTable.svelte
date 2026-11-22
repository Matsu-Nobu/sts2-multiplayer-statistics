<script lang="ts" generics="T">
  // プレイヤーの統計の表 (spec player-stats.md「表の共通の決まり」)。
  // 見出しを押すとその列で並べ替え、最初は 10 行だけを出す。hideLow なら count が MIN_GAMES 未満の行を除く。
  import { untrack } from 'svelte';
  import Badge from './ui/Badge.svelte';
  import { MIN_GAMES, type Column } from '../lib/playerStats';

  interface Props {
    columns: Column<T>[];
    rows: T[];
    rowKey: (r: T) => string;
    count: (r: T) => number;
    hideLow: boolean;
    initialSort: number;
  }
  let { columns, rows, rowKey, count, hideLow, initialSort }: Props = $props();

  const LIMIT = 10;
  let sortCol = $state(untrack(() => initialSort));
  let desc = $state(true);
  let showAll = $state(false);

  function onHeader(i: number) {
    if (sortCol === i) desc = !desc; else { sortCol = i; desc = true; }
  }

  let visible = $derived.by(() => {
    const key = columns[sortCol].sort;
    const dir = desc ? -1 : 1;
    return rows.filter(r => !hideLow || count(r) >= MIN_GAMES).sort((a, b) => {
      const x = key(a), y = key(b);
      if (x == null || y == null) return x == null ? (y == null ? 0 : 1) : -1;
      return (x < y ? -1 : x > y ? 1 : 0) * dir;
    });
  });
  let shown = $derived(showAll ? visible : visible.slice(0, LIMIT));
  const TONE = { ok: 'text-ok', bad: 'text-bad' };
</script>

<div class="overflow-x-auto">
  <table class="w-full text-sm">
    <thead>
      <tr>
        {#each columns as c, i (c.label)}
          <th class="px-2 py-1.5 text-xs font-normal whitespace-nowrap {c.num ? 'text-right' : 'text-left'}"
              aria-sort={i === sortCol ? (desc ? 'descending' : 'ascending') : 'none'}>
            <button type="button" class="focus-ring rounded hover:text-slate-100 {i === sortCol ? 'text-slate-100' : 'text-slate-400'}" onclick={() => onHeader(i)}>
              {c.label}{#if i === sortCol}<span class="text-accent ml-0.5">{desc ? '▼' : '▲'}</span>{/if}
            </button>
          </th>
        {/each}
      </tr>
    </thead>
    <tbody>
      {#each shown as r (rowKey(r))}
        <tr class="border-t border-bg-3">
          {#each columns as c (c.label)}
            {@const tone = c.tone?.(r)}
            <td class="px-2 py-1.5 {c.num ? 'text-right tabular' : 'whitespace-nowrap'} {tone ? TONE[tone] : ''}">
              {#if c.badge}<Badge label={c.text(r)} />
              {:else if c.href}<a href={c.href(r)} class="text-accent hover:underline focus-ring rounded">{c.text(r)}</a>
              {:else}{c.text(r)}{/if}
            </td>
          {/each}
        </tr>
      {:else}
        <tr>
          <td colspan={columns.length} class="px-2 py-3 text-xs text-slate-500">
            {#if hideLow && rows.length > 0}{MIN_GAMES} ゲーム以上の行がありません。「{MIN_GAMES} ゲーム未満を隠す」を外すと表示します。{:else}まだありません{/if}
          </td>
        </tr>
      {/each}
    </tbody>
  </table>
</div>
{#if visible.length > LIMIT}
  <button type="button" class="mt-2 text-xs text-accent hover:underline focus-ring rounded" onclick={() => (showAll = !showAll)}>
    {showAll ? `${LIMIT} 行だけ表示` : `すべて表示 (${visible.length} 行)`}
  </button>
{/if}
