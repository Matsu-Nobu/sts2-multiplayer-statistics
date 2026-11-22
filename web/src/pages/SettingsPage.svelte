<script lang="ts">
  // 設定 (spec ui.md「サイトの画面」): ユーザーページを公開するかどうか。
  import { auth, loadMe, loginHref, mutate, type ProfileVisibility } from '../lib/auth.svelte';
  import Panel from '../components/ui/Panel.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  $effect(() => { loadMe(); });

  let busy = $state(false);
  let error = $state<string | null>(null);
  async function setVisibility(v: ProfileVisibility) {
    busy = true; error = null;
    try { await mutate('PATCH', '/api/me', { profile_visibility: v }); if (auth.me) auth.me.profile_visibility = v; }
    catch { error = '変更できませんでした。もう一度試してください。'; }
    finally { busy = false; }
  }
  const OPTIONS: { value: ProfileVisibility; label: string; hint: string }[] = [
    { value: 'public', label: '全体に公開', hint: '誰でも見られます。' },
    { value: 'friends', label: 'Steam のフレンドに公開', hint: 'ログインした Steam のフレンドだけが見られます。' },
    { value: 'private', label: '非公開', hint: '自分だけが見られます。' },
  ];
</script>

<main class="max-w-3xl mx-auto px-4 py-6 space-y-4">
  <h1 class="text-xl font-semibold text-slate-100">設定</h1>
  {#if auth.loaded && !auth.me}
    <EmptyState title="ログインすると設定できます" />
    <div class="text-center"><a href={loginHref()} class="text-accent hover:underline">Steam でログイン</a></div>
  {:else if auth.me}
    <Panel title="マイページ">
      <fieldset class="space-y-2" disabled={busy}>
        <legend class="sr-only">マイページの公開範囲</legend>
        {#each OPTIONS as o (o.value)}
          <label class="flex items-start gap-2 text-sm text-slate-200">
            <input type="radio" name="profile-visibility" class="mt-1" checked={auth.me.profile_visibility === o.value} onchange={() => setVisibility(o.value)} />
            <span>{o.label}<span class="block text-xs text-slate-500">{o.hint}</span></span>
          </label>
        {/each}
      </fieldset>
      {#if error}<p class="text-xs text-bad mt-1" role="alert">{error}</p>{/if}
    </Panel>
  {/if}
</main>
