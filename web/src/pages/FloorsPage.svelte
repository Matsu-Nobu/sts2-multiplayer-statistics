<script lang="ts">
  // 階の一覧 (spec ui.md §3.2)。選んだプレイヤーの視点。行全体が階の詳細へのリンク。
  import { useSession } from '../lib/session';
  import { buildFloorSummaries, roomVisual, type FloorSummary } from '../lib/runOverview';
  import { navigate, link, href } from '../lib/router.svelte';
  import Panel from '../components/ui/Panel.svelte';
  import Badge from '../components/ui/Badge.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';

  const s = useSession();
  let floors = $derived(buildFloorSummaries(s.doc.events, s.player));
  const MAX_CHIPS = 3;

  function gains(f: FloorSummary): { label: string; tone: string }[] {
    const out: { label: string; tone: string }[] = [];
    for (const c of f.cards_obtained) out.push({ label: c.card_name ?? s.cardNames[c.card_id] ?? c.card_id, tone: 'card' });
    for (const p of f.shop_purchases) out.push({ label: p.name || p.id, tone: 'shop' });
    for (const r of f.relics_obtained) out.push({ label: r.relic_name ?? r.relic_id, tone: 'relic' });
    for (const p of f.potions_obtained) out.push({ label: p.potion_name ?? p.potion_id, tone: 'potion' });
    for (const c of f.cards_upgraded) out.push({ label: `${c.card_name ?? s.cardNames[c.card_id] ?? c.card_id} 強化`, tone: 'upgrade' });
    for (const c of f.cards_removed) out.push({ label: `${c.card_name ?? s.cardNames[c.card_id] ?? c.card_id} 除去`, tone: 'remove' });
    return out;
  }
  const sign = (n: number) => (n > 0 ? `+${n}` : `${n}`);
</script>

<Panel title={`${floors.length} 階`} scope={s.playerIds.length > 1 ? (s.playerNames[s.player] ?? s.player) : undefined} flush>
  {#if floors.length === 0}
    <EmptyState title="階ごとの記録がありません" hint="1 階目を出ると表示されます。" />
  {:else}
    <div class="dt-wrap">
      <table class="dt">
        <thead>
          <tr>
            <th class="num w-12">階</th><th>部屋</th><th>遭遇・イベント</th><th class="num">HP</th><th class="num">ゴールド</th><th>主な変化</th>
          </tr>
        </thead>
        <tbody>
          {#each floors as f (f.floor)}
            {@const v = roomVisual(f.room_type)}
            {@const g = gains(f)}
            {@const dhp = f.hp_out - f.hp_in}
            {@const dg = f.gold_out - f.gold_in}
            <tr class="row-link" onclick={() => navigate({ name: 'floor', floor: f.floor })}>
              <td class="num text-slate-400">{f.floor}</td>
              <td><Badge label={v.label} color={v.color} /></td>
              <td class="min-w-[10rem]">
                <a href={href({ name: 'floor', floor: f.floor })} use:link={{ name: 'floor', floor: f.floor }} class="text-slate-100 hover:underline focus-ring rounded-sm" onclick={(e) => e.stopPropagation()}>
                  {f.encounter_name ?? v.label}
                </a>
              </td>
              <td class="num">
                <span class="text-slate-100">{f.hp_out}</span><span class="text-slate-500">/{f.max_hp_out}</span>
                {#if dhp !== 0}<span class="ml-1 text-xs {dhp < 0 ? 'text-bad' : 'text-ok'}">{sign(dhp)}</span>{/if}
              </td>
              <td class="num">
                <span class="text-yellow-300">{f.gold_out}</span>
                {#if dg !== 0}<span class="ml-1 text-xs {dg < 0 ? 'text-bad' : 'text-ok'}">{sign(dg)}</span>{/if}
              </td>
              <td class="text-xs text-slate-300 min-w-[12rem]">
                {#if g.length === 0}<span class="text-slate-600">—</span>{:else}
                  {g.slice(0, MAX_CHIPS).map(x => x.label).join('、')}{#if g.length > MAX_CHIPS}<span class="text-slate-500"> ほか {g.length - MAX_CHIPS}</span>{/if}
                {/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</Panel>
