<script lang="ts">
  // ターン推移 (spec combat-stats.md §3.2)。行を押すとそのターンに使ったカードを開く。
  import type { TurnPayload } from '../lib/types';
  import { cardTypeLabel } from '../lib/labels';
  import Panel from './ui/Panel.svelte';

  interface Props { turns: TurnPayload[]; playerId: string; scope?: string }
  let { turns, playerId, scope }: Props = $props();
  let expanded = $state<Set<number>>(new Set());
  function toggle(n: number) { const s = new Set(expanded); if (s.has(n)) s.delete(n); else s.add(n); expanded = s; }
</script>

<Panel title="ターン推移" {scope} flush>
  <div class="dt-wrap">
    <table class="dt">
      <thead>
        <tr><th class="w-8"><span class="sr-only">開閉</span></th><th class="num">ターン</th><th class="num">与ダメージ</th><th class="num">被ダメージ</th><th class="num">獲得ブロック</th><th class="num">エナジー</th><th class="num">カード使用</th><th class="num">ドロー</th></tr>
      </thead>
      <tbody>
        {#each turns as t (t.turn_number)}
          {@const e = t.players[playerId]}
          {#if e}
            {@const open = expanded.has(t.turn_number)}
            <tr class="row-link" onclick={() => toggle(t.turn_number)}>
              <td><button type="button" class="text-slate-500 focus-ring rounded-sm" aria-expanded={open} aria-label={`${t.turn_number} ターン目のカード`} onclick={(ev) => { ev.stopPropagation(); toggle(t.turn_number); }}>{open ? '▾' : '▸'}</button></td>
              <td class="num">{t.turn_number}</td>
              <td class="num text-ok">{e.turn.damage_dealt}</td>
              <td class="num text-bad">{e.turn.damage_received}</td>
              <td class="num text-accent">{e.turn.block_gained_self}</td>
              <td class="num">{e.turn.energy_used}</td>
              <td class="num">{e.turn.cards_played}</td>
              <td class="num">{e.turn.cards_drawn}</td>
            </tr>
            {#if open}
              <tr class="bg-bg-0/40">
                <td></td>
                <td colspan="7">
                  {#if e.turn.cards.length === 0}
                    <span class="text-slate-500 text-xs">このターンに使ったカードはありません</span>
                  {:else}
                    <ul class="flex flex-wrap gap-x-4 gap-y-1 text-xs">
                      {#each e.turn.cards as c (c.card_id)}
                        <li><span class="text-slate-100">{c.card_name}</span><span class="text-slate-500 ml-1">{cardTypeLabel(c.card_type)}</span>
                          {#if c.play_count > 1}<span class="text-slate-400 ml-1">×{c.play_count}</span>{/if}
                          {#if c.damage_dealt > 0}<span class="text-ok ml-1">{c.damage_dealt} ダメージ</span>{/if}
                          {#if c.block_provided > 0}<span class="text-accent ml-1">{c.block_provided} ブロック</span>{/if}</li>
                      {/each}
                    </ul>
                  {/if}
                </td>
              </tr>
            {/if}
          {/if}
        {/each}
      </tbody>
    </table>
  </div>
</Panel>
