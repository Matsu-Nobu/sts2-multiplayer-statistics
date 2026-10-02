<script lang="ts">
  // 説明の「?」ボタン (spec ui.md §4.2)。押す (タップ) と開き、マウスを乗せても開く。Esc / 外を押すと閉じる。
  interface Props { text: string; label?: string; align?: 'left' | 'center' | 'right' }
  let { text, label = '説明', align = 'center' }: Props = $props();
  let pinned = $state(false);
  let hover = $state(false);
  let open = $derived(pinned || hover);
  let root: HTMLSpanElement;
  const id = `tip-${Math.random().toString(36).slice(2, 9)}`;

  function onDocClick(e: MouseEvent) { if (pinned && !root.contains(e.target as Node)) pinned = false; }
  function onKey(e: KeyboardEvent) { if (e.key === 'Escape') { pinned = false; hover = false; } }
  const pos = $derived(align === 'left' ? 'left-0' : align === 'right' ? 'right-0' : 'left-1/2 -translate-x-1/2');
</script>

<svelte:document onclick={onDocClick} onkeydown={onKey} />

<span class="relative inline-flex align-middle" bind:this={root}
      onmouseenter={() => hover = true} onmouseleave={() => hover = false} role="presentation">
  <button type="button"
          class="inline-flex items-center justify-center w-4 h-4 rounded-full bg-bg-3 text-slate-400 text-[10px] leading-none hover:text-slate-100 focus-ring"
          aria-label={label} aria-expanded={open} aria-describedby={open ? id : undefined}
          onclick={(e) => { e.stopPropagation(); pinned = !pinned; }}>?</button>
  {#if open}
    <span {id} role="tooltip"
          class="absolute {pos} top-full mt-1.5 z-40 w-72 max-w-[calc(100vw-2rem)] bg-bg-0 border border-bg-3 rounded-md shadow-xl px-3 py-2 text-xs font-normal text-slate-200 leading-relaxed whitespace-pre-line text-left normal-case tracking-normal">
      {text}
    </span>
  {/if}
</span>
