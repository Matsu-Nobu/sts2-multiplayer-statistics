<script lang="ts">
  // 見られないとき (spec ui.md「サイトの画面」): 何が見られないかと、ホームへのリンク。
  import EmptyState from './ui/EmptyState.svelte';
  interface Props { title: string; hint?: string }
  let { title, hint }: Props = $props();
</script>

<main class="max-w-xl mx-auto px-4 mt-12 text-center">
  <EmptyState {title} {hint} />
  <a href="/" class="text-sm text-accent hover:underline focus-ring rounded">ホームへ</a>
</main>
