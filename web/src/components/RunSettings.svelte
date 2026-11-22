<script lang="ts">
  // ゲームの設定のポップアップ (spec ui.md「ランの設定」)。記録した人には公開・非公開と削除、参加者にはマイページに表示するかどうか。
  import { mutate, type RunRelation } from '../lib/auth.svelte';
  interface Props {
    sessionId: string;
    relation: RunRelation;
    label: string;                 // 開くボタンの文字 (「…」「このランの設定」)
    onchanged: () => void;         // 設定を変えたあと (呼び出し側が取り直す)
    ondeleted: () => void;
  }
  let { sessionId, relation, label, onchanged, ondeleted }: Props = $props();

  let open = $state(false);
  let busy = $state(false);
  let error = $state<string | null>(null);
  let rootEl: HTMLDivElement | undefined = $state();
  let buttonEl: HTMLButtonElement | undefined = $state();
  // 表の枠 (横スクロールの入れ物) の中でも切れないよう、画面に対して固定の位置に出す
  let pos = $state({ top: 0, right: 0 });
  function toggle() {
    if (!open && buttonEl) {
      const r = buttonEl.getBoundingClientRect();
      pos = { top: r.bottom + 4, right: Math.max(8, window.innerWidth - r.right) };
    }
    open = !open;
  }
  function onWindowClick(e: MouseEvent) { if (open && rootEl && !rootEl.contains(e.target as Node)) open = false; }
  function onKey(e: KeyboardEvent) { if (e.key === 'Escape') open = false; }

  async function act(fn: () => Promise<void>, after: () => void) {
    busy = true; error = null;
    try { await fn(); after(); }
    catch { error = '変更できませんでした。もう一度試してください。'; }
    finally { busy = false; }
  }
  const setVisibility = (v: RunRelation['visibility']) => act(() => mutate('PATCH', `/api/sessions/${sessionId}`, { visibility: v }), onchanged);
  const setListed = (listed: boolean) => act(() => mutate('PATCH', `/api/sessions/${sessionId}/listing`, { hidden: !listed }), onchanged);
  function remove() {
    if (!confirm('このゲームの記録を削除します。元に戻せません。')) return;
    act(() => mutate('DELETE', `/api/sessions/${sessionId}`), ondeleted);
  }
</script>

<svelte:window onclick={onWindowClick} onkeydown={onKey} onscroll={() => (open = false)} onresize={() => (open = false)} />

{#if relation.is_participant}
  <div class="relative inline-block text-left" bind:this={rootEl}>
    <button type="button" aria-haspopup="dialog" aria-expanded={open} onclick={toggle} bind:this={buttonEl}
            class="px-2 py-0.5 rounded-md text-xs border border-bg-3 text-slate-300 hover:text-slate-100 hover:bg-bg-2 focus-ring">{label}</button>
    {#if open}
      <div role="dialog" aria-label="ゲームの設定" style:top={`${pos.top}px`} style:right={`${pos.right}px`} class="fixed w-56 bg-bg-1 border border-bg-3 rounded-md shadow-lg p-3 z-50 space-y-3 text-sm">
        {#if relation.is_recorder}
          <fieldset class="space-y-1" disabled={busy}>
            <legend class="sr-only">公開・非公開</legend>
            <label class="flex items-center gap-2"><input type="radio" name={`vis-${sessionId}`} checked={relation.visibility === 'public'} onchange={() => setVisibility('public')} /> 公開</label>
            <label class="flex items-center gap-2"><input type="radio" name={`vis-${sessionId}`} checked={relation.visibility === 'private'} onchange={() => setVisibility('private')} /> 非公開</label>
          </fieldset>
        {/if}
        <label class="flex items-center gap-2">
          <input type="checkbox" checked={!relation.hidden} disabled={busy} onchange={(e) => setListed((e.target as HTMLInputElement).checked)} />
          マイページに表示する
        </label>
        {#if relation.is_recorder}
          <button type="button" disabled={busy} onclick={remove} class="text-bad hover:underline focus-ring rounded disabled:opacity-50">削除</button>
        {/if}
        {#if error}<div class="text-xs text-bad" role="alert">{error}</div>{/if}
      </div>
    {/if}
  </div>
{/if}
