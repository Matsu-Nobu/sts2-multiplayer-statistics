<script lang="ts">
  // レリック・ポーション・その他のチップ (spec run-overview.md §2.5)。
  import CardTooltip from '../CardTooltip.svelte';
  interface Props { label: string; sub?: string; subClass?: string; tip?: string }
  let { label, sub = '', subClass = 'text-slate-400', tip = '' }: Props = $props();
</script>

{#snippet body()}
  <span class="inline-flex items-center px-2 py-0.5 rounded text-sm border bg-bg-2 border-bg-3 text-slate-200 {tip ? 'cursor-help' : ''}">
    {label}{#if sub}<span class="{subClass} ml-1">{sub}</span>{/if}
  </span>
{/snippet}

{#if tip}<CardTooltip descriptionHtml={tip}>{@render body()}</CardTooltip>{:else}{@render body()}{/if}
