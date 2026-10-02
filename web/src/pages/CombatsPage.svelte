<script lang="ts">
  // 戦闘 (spec ui.md §3.4): 全戦闘の合計 + 戦闘の表。
  import { useSession } from '../lib/session';
  import { computeRdps } from '../lib/rdps';
  import { computeRmit } from '../lib/rmit';
  import { roomVisual } from '../lib/runOverview';
  import { navigate, link, href } from '../lib/router.svelte';
  import Panel from '../components/ui/Panel.svelte';
  import Badge from '../components/ui/Badge.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  import CombatSummary from '../components/CombatSummary.svelte';

  const s = useSession();
  let rdps = $derived(computeRdps(s.combatEvents));
  let rmit = $derived(computeRmit(s.combatEvents));
  let wins = $derived(s.combats.filter(c => c.victory === true).length);
  let losses = $derived(s.combats.filter(c => c.victory === false).length);
  let turns = $derived(s.combats.reduce((a, c) => a + c.turns.length, 0));
</script>

<div class="space-y-6">
  <div class="flex flex-wrap items-baseline gap-x-3 gap-y-1">
    <h1 class="text-xl font-semibold text-slate-100">{s.combats.length} 戦</h1>
    <span class="text-sm text-slate-400">勝利 {wins}{#if losses > 0} · 敗北 {losses}{/if} · {turns} ターン</span>
  </div>

  {#if s.combats.length === 0}
    <div class="bg-bg-1 border border-bg-3 rounded-lg"><EmptyState title="戦闘の記録がありません" hint="最初の戦闘が始まると表示されます。" /></div>
  {:else}
    <Panel title="戦闘一覧" scope={s.playerIds.length > 1 ? (s.playerNames[s.player] ?? s.player) : undefined} flush>
      <div class="dt-wrap max-h-[28rem] overflow-y-auto">
        <table class="dt">
          <thead><tr><th class="num w-10">#</th><th class="num">階</th><th>遭遇</th><th>部屋</th><th>結果</th><th class="num">ターン</th><th class="num">有効与ダメージ</th><th class="num">被ダメージ</th></tr></thead>
          <tbody>
            {#each s.combats as c, i (c.combat_index)}
              {@const v = roomVisual(c.room_type ?? '')}
              {@const me = c.finalTurn.players[s.player]?.combat}
              {@const page = { name: 'combat' as const, combat: c.combat_index, view: 'summary' as const }}
              <tr class="row-link" onclick={() => navigate(page)}>
                <td class="num text-slate-500">{i + 1}</td>
                <td class="num text-slate-400">{c.combat_index}</td>
                <td class="whitespace-nowrap"><a href={href(page)} use:link={page} class="text-slate-100 hover:underline focus-ring rounded-sm" onclick={(e) => e.stopPropagation()}>{c.encounter_name ?? '不明な遭遇'}</a></td>
                <td>{#if c.room_type}<Badge label={v.label} color={v.color} />{/if}</td>
                <td>{#if c.victory === true}<Badge label="勝利" tone="ok" />{:else if c.victory === false}<Badge label="敗北" tone="bad" />{:else}<Badge label="進行中" tone="warn" />{/if}</td>
                <td class="num">{c.turns.length}</td>
                <td class="num">{me?.effective_damage_dealt ?? '—'}</td>
                <td class="num">{me?.damage_received ?? '—'}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    </Panel>

    <div>
      <h2 class="text-base font-semibold text-slate-100 mb-3">全戦闘の合計</h2>
      <CombatSummary stats={s.totals.perPlayer} {rdps} {rmit} turns={turns} combatsCount={s.combats.length} rangeLabel="ラン合計" />
    </div>
  {/if}
</div>
