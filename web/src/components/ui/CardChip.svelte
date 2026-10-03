<script lang="ts">
  // カードのチップ (spec run-overview.md §2.5): レアリティで背景色と枠、強化済みは文字色、選ばなかったものは打ち消し線。
  import CardTooltip from '../CardTooltip.svelte';
  interface Props { label: string; rarity?: string; upgraded?: boolean; suffix?: string; skipped?: boolean; tip?: string; count?: number }
  let { label, rarity, upgraded = false, suffix = '', skipped = false, tip = '', count = 1 }: Props = $props();
  let bg = $derived(rarity === 'Common' ? 'bg-slate-700/60 border-slate-500' : rarity === 'Uncommon' ? 'bg-sky-900/60 border-sky-500' : rarity === 'Rare' ? 'bg-yellow-900/60 border-yellow-500' : 'bg-bg-2 border-bg-3');
  let fg = $derived(upgraded ? 'text-lime-300' : (skipped ? 'text-slate-500' : 'text-slate-200'));
</script>

{#snippet body()}
  <span class="inline-flex items-center px-2 py-0.5 rounded text-sm border {bg} {fg} {skipped ? 'opacity-60 line-through' : ''} {tip ? 'cursor-help' : ''}">
    {label}{#if count > 1}<span class="text-slate-400 ml-1 tabular">×{count}</span>{/if}{#if suffix}<span class="text-yellow-400 ml-1">{suffix}</span>{/if}
  </span>
{/snippet}

{#if tip}<CardTooltip descriptionHtml={tip}>{@render body()}</CardTooltip>{:else}{@render body()}{/if}
