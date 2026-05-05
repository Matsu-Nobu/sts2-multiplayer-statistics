<script lang="ts">
  interface Props {
    debuffs: Record<string, number>;
    title?: string;
  }
  let { debuffs, title }: Props = $props();
  let entries = $derived(Object.entries(debuffs).sort((a, b) => b[1] - a[1]));
</script>

<div class="bg-bg-1 border border-bg-3 rounded-lg overflow-hidden">
  {#if title}
    <div class="px-3 py-2 text-sm uppercase tracking-wide text-slate-400 border-b border-bg-3">{title}</div>
  {/if}
  <table class="w-full text-sm tabular">
    <thead class="bg-bg-2 text-slate-400 text-xs uppercase">
      <tr>
        <th class="text-left py-2 px-3">デバフ種別</th>
        <th class="text-right py-2 px-3">スタック</th>
      </tr>
    </thead>
    <tbody>
      {#each entries as [k, v] (k)}
        <tr class="border-t border-bg-3">
          <td class="py-2 px-3">{k}</td>
          <td class="text-right py-2 px-3">{v}</td>
        </tr>
      {/each}
      {#if entries.length === 0}
        <tr><td colspan="2" class="text-center text-slate-500 py-3">デバフ付与なし</td></tr>
      {/if}
    </tbody>
  </table>
</div>
