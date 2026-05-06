<script lang="ts">
  interface Props {
    label: string;
    value: number | string;
    sub?: string;
    help?: string;
  }
  let { label, value, sub, help }: Props = $props();
</script>

<div class="bg-bg-2 border border-bg-3 rounded-lg p-3 min-w-[120px] relative">
  <div class="text-xs uppercase tracking-wide text-slate-400 flex items-center gap-1">
    <span>{label}</span>
    {#if help}
      <span class="relative inline-flex group">
        <span
          class="inline-flex items-center justify-center w-3.5 h-3.5 rounded-full bg-bg-3 text-slate-400 text-[10px] leading-none cursor-help select-none"
          aria-label={help}
        >?</span>
        <span
          class="pointer-events-none absolute left-1/2 -translate-x-1/2 top-full mt-1 w-56 z-20 bg-bg-0 border border-bg-3 rounded shadow-lg px-2 py-1.5 text-[11px] text-slate-200 normal-case tracking-normal whitespace-normal opacity-0 group-hover:opacity-100 transition-opacity"
        >{help}</span>
      </span>
    {/if}
  </div>
  <div class="text-2xl font-semibold tabular mt-1">{value}</div>
  {#if sub}
    <div class="text-xs text-slate-500 mt-1">{sub}</div>
  {/if}
</div>
