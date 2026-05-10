<script lang="ts">
  // hover 時に右隣に description popover を出す chip コンポーネント。
  // description は HTML 文字列 ([gold]X[/gold] が <span> 化済) を受け取る。
  //
  // 使い方: <CardTooltip description={...}><chip 本体> </CardTooltip>

  interface Props {
    descriptionHtml: string;           // 本文 HTML (renderCardDescription 出力)
    children?: import('svelte').Snippet;
  }
  let { descriptionHtml, children }: Props = $props();

  let show = $state(false);
</script>

<span
  class="relative inline-block"
  onmouseenter={() => show = true}
  onmouseleave={() => show = false}
  role="tooltip"
>
  {@render children?.()}
  {#if show && descriptionHtml}
    <span
      class="absolute left-0 top-full mt-1 z-50 w-64 max-w-[20rem] bg-bg-0 border border-bg-3 rounded shadow-lg p-2.5 text-xs text-slate-200 leading-relaxed pointer-events-none"
    >
      {@html descriptionHtml}
    </span>
  {/if}
</span>
