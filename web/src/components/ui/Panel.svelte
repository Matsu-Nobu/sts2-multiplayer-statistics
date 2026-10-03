<script lang="ts">
  // 画面の区切り (spec ui.md §4.2)。見出しに範囲 (「全員」かプレイヤー名) を明記する。
  import type { Snippet } from 'svelte';
  import InfoTip from './InfoTip.svelte';
  interface Props {
    title?: string;
    scope?: string;          // 「全員」・プレイヤー名など
    help?: string;
    actions?: Snippet;
    children: Snippet;
    flush?: boolean;         // 中身を端まで (表など)
  }
  let { title, scope, help, actions, children, flush = false }: Props = $props();
</script>

<section class="bg-bg-1 border border-bg-3 rounded-lg min-w-0">
  {#if title || actions}
    <header class="flex items-center justify-between gap-3 px-4 py-3 {flush ? 'border-b border-bg-3' : ''}">
      <h2 class="text-base font-semibold text-slate-100 flex items-center gap-2 min-w-0">
        <span class="truncate">{title}</span>
        {#if scope}<span class="text-xs font-normal text-slate-400 whitespace-nowrap">{scope}</span>{/if}
        {#if help}<InfoTip text={help} align="left" />{/if}
      </h2>
      {#if actions}<div class="flex items-center gap-2 shrink-0">{@render actions()}</div>{/if}
    </header>
  {/if}
  <div class={flush ? '' : 'px-4 pb-4 ' + (title || actions ? '' : 'pt-4')}>
    {@render children()}
  </div>
</section>
