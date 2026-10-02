<script lang="ts">
  // 数値のタイル (spec ui.md §4.2)。primary = 大きい主要数値、secondary = 小さい補助の数値。
  import InfoTip from './InfoTip.svelte';
  interface Props { label: string; value: number | string; sub?: string; help?: string; size?: 'primary' | 'secondary' }
  let { label, value, sub, help, size = 'secondary' }: Props = $props();
</script>

<div class="bg-bg-2 border border-bg-3 rounded-md min-w-0 {size === 'primary' ? 'p-4' : 'px-3 py-2.5'}">
  <div class="text-xs text-slate-400 flex items-center gap-1.5">
    <span class="truncate">{label}</span>
    {#if help}<InfoTip text={help} label={`${label}の説明`} />{/if}
  </div>
  <div class="font-semibold tabular text-slate-100 {size === 'primary' ? 'text-[28px] leading-tight mt-1' : 'text-lg mt-0.5'}">{value}</div>
  {#if sub}<div class="text-xs text-slate-500 mt-0.5 truncate">{sub}</div>{/if}
</div>
