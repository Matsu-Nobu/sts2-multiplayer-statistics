<script lang="ts" generics="T">
  // プレイヤーの統計の表 (spec player-stats.md「表の共通の決まり」)。
  // 見出しを押すとその列で並べ替え、最初は 10 行だけを出す。hideLow なら count が MIN_GAMES 未満の行を除く。
  // totalRow は並べ替えと hideLow の対象にせず、いつも 1 行目に出す。1 列目は横にずらしても左に残す。
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
    totalRow?: T;
  }
  let { columns, rows, rowKey, count, hideLow, initialSort, totalRow }: Props = $props();

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

  // 上の段の見出し。group の無い列は 2 段ぶんの高さにし、group が続く列は 1 つにまとめる
  let grouped = $derived(columns.some(c => c.group));
  let topRow = $derived.by(() => {
    const out: { group?: string; span: number; col?: number }[] = [];
    columns.forEach((c, i) => {
      const last = out[out.length - 1];
      if (c.group && last?.group === c.group) last.span++;
      else out.push(c.group ? { group: c.group, span: 1 } : { span: 1, col: i });
    });
    return out;
  });
  const STICKY = 'sticky left-0 bg-bg-1 z-10';
</script>

{#snippet head(i: number, span: number)}
  {@const c = columns[i]}
  <th rowspan={span} class="px-2 py-1.5 text-xs font-normal whitespace-nowrap {c.num ? 'text-right' : 'text-left'} {i === 0 ? STICKY : ''} {c.group && c.group !== columns[i - 1]?.group ? 'border-l border-bg-3' : ''}"
      aria-sort={i === sortCol ? (desc ? 'descending' : 'ascending') : 'none'}>
    <button type="button" class="focus-ring rounded hover:text-slate-100 {i === sortCol ? 'text-slate-100' : 'text-slate-400'}" onclick={() => onHeader(i)}>
      {c.label}{#if i === sortCol}<span class="text-accent ml-0.5">{desc ? '▼' : '▲'}</span>{/if}
    </button>
  </th>
{/snippet}

<div class="overflow-x-auto">
  <table class="w-full text-sm">
    <thead>
      {#if grouped}
        <tr>
          {#each topRow as t, i (i)}
            {#if t.group}
              <th colspan={t.span} class="px-2 py-1.5 text-xs font-normal text-slate-300 text-center border-l border-b border-bg-3 whitespace-nowrap">{t.group}</th>
            {:else}
              {@render head(t.col!, 2)}
            {/if}
          {/each}
        </tr>
        <tr>
          {#each columns as c, i (i)}{#if c.group}{@render head(i, 1)}{/if}{/each}
        </tr>
      {:else}
        <tr>{#each columns as _, i (i)}{@render head(i, 1)}{/each}</tr>
      {/if}
    </thead>
    <tbody>
      {#if totalRow}
        <tr class="border-t border-bg-3 bg-bg-2 text-slate-400">
          {#each columns as c, i (i)}
            <td class="px-2 py-1.5 {c.num ? 'text-right tabular' : 'whitespace-nowrap'} {i === 0 ? 'sticky left-0 bg-bg-2 z-10' : ''} {c.group && c.group !== columns[i - 1]?.group ? 'border-l border-bg-3' : ''}">{c.text(totalRow)}</td>
          {/each}
        </tr>
      {/if}
      {#each shown as r (rowKey(r))}
        <tr class="border-t border-bg-3">
          {#each columns as c, i (i)}
            {@const tone = c.tone?.(r)}
            <td class="px-2 py-1.5 {c.num ? 'text-right tabular' : 'whitespace-nowrap'} {tone ? TONE[tone] : ''} {i === 0 ? STICKY : ''} {c.group && c.group !== columns[i - 1]?.group ? 'border-l border-bg-3' : ''}">
              {#if c.badge}<Badge label={c.text(r)} />
              {:else if c.href}<a href={c.href(r)} class="text-accent hover:underline focus-ring rounded">{c.text(r)}</a>
              {:else}{c.text(r)}{/if}
            </td>
          {/each}
        </tr>
      {/each}
    </tbody>
  </table>
</div>
{#if shown.length === 0}
  <!-- 表の外に置き、横に長い表の中で案内文が切れないようにする -->
  <p class="px-2 py-3 text-xs text-slate-500">
    {#if hideLow && rows.length > 0}{MIN_GAMES} ゲーム以上の行がありません。「{MIN_GAMES} ゲーム未満を隠す」を外すと表示します。{:else}まだありません{/if}
  </p>
{/if}
{#if visible.length > LIMIT}
  <button type="button" class="mt-2 text-xs text-accent hover:underline focus-ring rounded" onclick={() => (showAll = !showAll)}>
    {showAll ? `${LIMIT} 行だけ表示` : `すべて表示 (${visible.length} 行)`}
  </button>
{/if}
