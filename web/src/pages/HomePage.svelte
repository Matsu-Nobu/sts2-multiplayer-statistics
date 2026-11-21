<script lang="ts">
  // ホーム (spec ui.md「サイトの画面」): ログイン済みなら自分のユーザーページへ移す。未ログインならサイトの説明。
  import { auth, loadMe, loginHref, userHref } from '../lib/auth.svelte';
  import Panel from '../components/ui/Panel.svelte';
  $effect(() => { loadMe(); });
  $effect(() => { if (auth.loaded && auth.me) location.replace(userHref(auth.me.steam_id)); });
</script>

{#if auth.loaded && !auth.me}
  <main class="max-w-3xl mx-auto px-4 py-10 space-y-6">
    <section>
      <h1 class="text-2xl font-semibold text-slate-100">Slay the Spire 2 のランの統計</h1>
      <p class="mt-2 text-slate-300">mod を入れて遊ぶと、ランの記録 (階ごとの入手・HP・ゴールド、戦闘ごとのダメージ・ブロック・貢献度) がこのサイトに送られ、URL を共有して仲間と見られます。</p>
      <a href={loginHref()} class="inline-block mt-4 px-4 py-2 rounded-md bg-accent text-bg-0 font-medium focus-ring">Steam でログイン</a>
      <p class="mt-2 text-xs text-slate-500">ログインすると、自分が遊んだランの一覧を見て、公開・非公開や削除を選べます。</p>
    </section>

    <Panel title="使い方">
      <ol class="list-decimal pl-5 space-y-1.5 text-sm text-slate-300">
        <li><a class="text-accent hover:underline" href="https://github.com/Matsu-Nobu/sts2-multiplayer-statistics/releases">GitHub Releases</a> から <code>StsStats.dll</code> と <code>StsStats.json</code> をダウンロードし、ゲームの <code>mods/StsStats/</code> フォルダに置く。</li>
        <li>ゲームを起動してランを始める。mod がこのサイトにランを作り、共有 URL をクリップボードにコピーする。</li>
        <li>共有 URL をブラウザで開く。戦闘中も 10 秒ごとに更新される。</li>
      </ol>
      <p class="mt-3 text-sm text-slate-400">マルチプレイでは、ホストが mod を入れていれば全員分が記録されます。</p>
    </Panel>

    <Panel title="送るデータ">
      <p class="text-sm text-slate-300">Steam ID と Steam の表示名、キャラクター・アセンション・シード、階ごとの記録、戦闘の記録 (ダメージ・ブロック・カード・パワー)、ランの結果。</p>
      <p class="mt-2 text-sm text-slate-400">公開のランは、共有 URL を知っている人なら誰でも見られます。サーバは個人で運用しているので、データを予告なく消すことがあります。</p>
    </Panel>
  </main>
{/if}
