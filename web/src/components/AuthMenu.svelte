<script lang="ts">
  // 画面の右上のログインの表示 (spec user-pages.md「ログイン」)。
  import { auth, loadMe, loginHref, logout, userHref } from '../lib/auth.svelte';
  $effect(() => { loadMe(); });
</script>

{#if auth.loaded}
  {#if auth.me}
    <span class="inline-flex items-center gap-2 text-xs">
      <a href={userHref(auth.me.steam_id)} class="text-accent hover:underline focus-ring rounded">{auth.me.display_name}</a>
      <button type="button" onclick={logout} class="text-slate-500 hover:text-slate-300 focus-ring rounded">ログアウト</button>
    </span>
  {:else}
    <a href={loginHref()} class="text-xs text-accent hover:underline focus-ring rounded">Steam でログイン</a>
  {/if}
{/if}
