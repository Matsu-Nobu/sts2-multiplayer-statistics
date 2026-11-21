<script lang="ts">
  // 全画面に共通の上の帯 (spec ui.md「共通の上の帯 (TopBar)」)。
  import { auth, loadMe, loginHref, logout, userHref } from '../lib/auth.svelte';
  $effect(() => { loadMe(); });

  let open = $state(false);
  let menuEl: HTMLDivElement | undefined = $state();
  function onWindowClick(e: MouseEvent) { if (open && menuEl && !menuEl.contains(e.target as Node)) open = false; }
  function onKey(e: KeyboardEvent) { if (e.key === 'Escape') open = false; }
</script>

<svelte:window onclick={onWindowClick} onkeydown={onKey} />

<div class="border-b border-bg-3 bg-bg-0">
  <div class="max-w-7xl mx-auto px-4 h-11 flex items-center justify-between gap-4">
    <a href="/" class="text-sm font-semibold text-slate-100 focus-ring rounded">STS2 Stats</a>
    {#if auth.loaded}
      {#if auth.me}
        <div class="flex items-center gap-4 text-sm">
          <a href={userHref(auth.me.steam_id)} class="text-slate-300 hover:text-slate-100 focus-ring rounded">自分のラン</a>
          <div class="relative" bind:this={menuEl}>
            <button type="button" aria-haspopup="menu" aria-expanded={open} onclick={() => (open = !open)}
                    class="text-slate-300 hover:text-slate-100 focus-ring rounded">{auth.me.display_name} ▾</button>
            {#if open}
              <div role="menu" class="absolute right-0 mt-2 w-40 bg-bg-1 border border-bg-3 rounded-md shadow-lg py-1 z-50">
                <a role="menuitem" href={userHref(auth.me.steam_id)} class="block px-3 py-1.5 text-slate-200 hover:bg-bg-2 focus-ring">自分のページ</a>
                <a role="menuitem" href="/settings" class="block px-3 py-1.5 text-slate-200 hover:bg-bg-2 focus-ring">設定</a>
                <button role="menuitem" type="button" onclick={logout} class="block w-full text-left px-3 py-1.5 text-slate-200 hover:bg-bg-2 focus-ring">ログアウト</button>
              </div>
            {/if}
          </div>
        </div>
      {:else}
        <a href={loginHref()} class="text-sm text-accent hover:underline focus-ring rounded">Steam でログイン</a>
      {/if}
    {/if}
  </div>
</div>
