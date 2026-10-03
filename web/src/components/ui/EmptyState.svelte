<script lang="ts">
  // データが無いとき (spec ui.md §4.2): 何が無いのか + 次にすること。
  interface Props { title: string; hint?: string }
  let { title, hint }: Props = $props();
</script>

<div class="py-8 px-4 text-center">
  <div class="text-sm text-slate-300">{title}</div>
  {#if hint}<div class="text-xs text-slate-500 mt-1">{hint}</div>{/if}
</div>
