<script lang="ts">
  // デバフ付与 (選んだプレイヤー)。
  import Panel from './ui/Panel.svelte';
  import EmptyState from './ui/EmptyState.svelte';
  import { formatPowerName } from '../lib/powers';
  interface Props { debuffs: Record<string, number>; title: string; scope?: string; powerNames?: Record<string, string> }
  let { debuffs, title, scope, powerNames = {} }: Props = $props();
  let entries = $derived(Object.entries(debuffs).sort((a, b) => b[1] - a[1]));
  let max = $derived(Math.max(1, ...entries.map(e => e[1])));
</script>

<Panel {title} {scope} flush>
  {#if entries.length === 0}
    <EmptyState title="デバフの付与はありません" />
  {:else}
    <div class="dt-wrap">
      <table class="dt">
        <thead><tr><th>デバフ</th><th class="num">スタック</th></tr></thead>
        <tbody>
          {#each entries as [k, v] (k)}
            <tr>
              <td class="w-full">
                <div class="text-slate-100">{formatPowerName(k, powerNames)}</div>
                <div class="mt-1 h-1 bg-bg-3 rounded-full overflow-hidden"><div class="h-full bg-accent/70 rounded-full" style:width={`${(v / max) * 100}%`}></div></div>
              </td>
              <td class="num">{v}</td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</Panel>
