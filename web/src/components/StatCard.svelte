<script lang="ts">
  interface Props {
    label: string;
    value: number | string;
    sub?: string;
  }
  let { label, value, sub }: Props = $props();
</script>

<div class="bg-bg-2 border border-bg-3 rounded-lg p-3 min-w-[120px]">
  <div class="text-xs uppercase tracking-wide text-slate-400">{label}</div>
  <div class="text-2xl font-semibold tabular mt-1">{value}</div>
  {#if sub}
    <div class="text-xs text-slate-500 mt-1">{sub}</div>
  {/if}
</div>
