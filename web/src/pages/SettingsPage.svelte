<script lang="ts">
  // 設定 (spec ui.md「サイトの画面」): ユーザーページを公開するかどうか。
  import { auth, loadMe, loginHref, mutate } from '../lib/auth.svelte';
  import Panel from '../components/ui/Panel.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  $effect(() => { loadMe(); });

  let busy = $state(false);
  let error = $state<string | null>(null);
  async function setProfilePublic(v: boolean) {
    busy = true; error = null;
    try { await mutate('PATCH', '/api/me', { profile_public: v }); if (auth.me) auth.me.profile_public = v; }
    catch { error = '変更できませんでした。もう一度試してください。'; }
    finally { busy = false; }
  }
</script>

<main class="max-w-3xl mx-auto px-4 py-6 space-y-4">
  <h1 class="text-xl font-semibold text-slate-100">設定</h1>
  {#if auth.loaded && !auth.me}
    <EmptyState title="ログインすると設定できます" />
    <div class="text-center"><a href={loginHref()} class="text-accent hover:underline">Steam でログイン</a></div>
  {:else if auth.me}
    <Panel title="ユーザーページ">
      <label class="flex items-center gap-2 text-sm text-slate-200">
        <input type="checkbox" checked={auth.me.profile_public} disabled={busy}
               onchange={(e) => setProfilePublic((e.target as HTMLInputElement).checked)} />
        ユーザーページを公開する
      </label>
      <p class="text-xs text-slate-500 mt-1">公開すると、ほかの人もあなたのユーザーページで、公開しているランを見られます。</p>
      {#if error}<p class="text-xs text-bad mt-1" role="alert">{error}</p>{/if}
    </Panel>
  {/if}
</main>
