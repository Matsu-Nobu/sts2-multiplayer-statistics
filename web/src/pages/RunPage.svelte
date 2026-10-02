<script lang="ts">
  // ラン全体 (spec ui.md §3.1): 貢献ランキング、HP 推移 (全員)、プレイヤーごとのデッキ・レリック・ポーション・ゴールド・バッジ。
  import { useSession } from '../lib/session';
  import { buildFloorSummaries, buildPlayerFinalState } from '../lib/runOverview';
  import { navigate } from '../lib/router.svelte';
  import { playerColor } from '../lib/players';
  import { BADGE_RARITY } from '../lib/labels';
  import { cardTip, relicTip, potionTip } from '../lib/tips';
  import type { RunStartPayload, SnapshotCard } from '../lib/types';
  import Panel from '../components/ui/Panel.svelte';
  import CardChip from '../components/ui/CardChip.svelte';
  import Chip from '../components/ui/Chip.svelte';
  import EmptyState from '../components/ui/EmptyState.svelte';
  import HpRunChart from '../components/HpRunChart.svelte';
  import ContribRanking from '../components/ContribRanking.svelte';
  import CardTooltip from '../components/CardTooltip.svelte';

  const s = useSession();

  let perPlayer = $derived(s.playerIds.map(pid => {
    const floors = buildFloorSummaries(s.doc.events, pid);
    const start = s.doc.events.find(e => e.event_type === 'run_start' && e.player_id === pid)?.payload as RunStartPayload | undefined;
    return {
      pid,
      name: s.playerNames[pid] ?? pid,
      color: playerColor(s.playerIds, pid),
      character: start?.character_name || start?.character_id || '',
      floors,
      last: floors[floors.length - 1] ?? null,
      final: buildPlayerFinalState(s.doc.events, pid),
    };
  }));
  let anyFloors = $derived(perPlayer.some(p => p.floors.length > 0));

  // デッキ: 種類ごと、同じカード (同じ強化・エンチャント) は ×N にまとめる
  const GROUPS = [
    { key: 'Attack', label: 'アタック' }, { key: 'Skill', label: 'スキル' }, { key: 'Power', label: 'パワー' }, { key: 'other', label: 'その他' },
  ];
  const RARITY_ORDER: Record<string, number> = { Rare: 0, Uncommon: 1, Common: 2, Basic: 3 };
  function groupDeck(deck: SnapshotCard[]) {
    const m = new Map<string, { card: SnapshotCard; count: number }>();
    for (const c of deck) {
      const k = `${c.id}|${c.upgrade_level ?? 0}|${c.enchantment_id ?? ''}`;
      const e = m.get(k);
      if (e) e.count++; else m.set(k, { card: c, count: 1 });
    }
    const items = [...m.values()].sort((a, b) =>
      (RARITY_ORDER[a.card.rarity] ?? 9) - (RARITY_ORDER[b.card.rarity] ?? 9) || a.card.name.localeCompare(b.card.name, 'ja'));
    return GROUPS.map(g => {
      const list = items.filter(i => g.key === 'other' ? !['Attack', 'Skill', 'Power'].includes(i.card.type ?? '') : i.card.type === g.key);
      return { ...g, items: list, n: list.reduce((a, i) => a + i.count, 0) };
    }).filter(g => g.items.length > 0);
  }
</script>

<div class="space-y-6">
  <!-- 1. 貢献ランキング -->
  <ContribRanking />

  <!-- 2. HP 推移 -->
  <Panel title="HP 推移" scope="全員" help="各階を出た時点の HP。点を押すとその階の詳細を開きます。">
    {#if anyFloors}
      <HpRunChart series={perPlayer.filter(p => p.floors.length > 0)} highlight={s.player}
                  onSelect={(floor) => navigate({ name: 'floor', floor })} />
    {:else}
      <EmptyState title="階ごとの記録がありません" hint="1 階目を出ると表示されます。" />
    {/if}
  </Panel>

  <!-- 3. プレイヤーごとのカード -->
  <div class="grid grid-cols-1 {perPlayer.length > 1 ? 'lg:grid-cols-2 xl:grid-cols-3' : ''} gap-4 items-start">
    {#each perPlayer as p (p.pid)}
      {@const selected = s.playerIds.length > 1 && p.pid === s.player}
      <section class="bg-bg-1 border rounded-lg min-w-0 {selected ? 'border-slate-500' : 'border-bg-3'}" aria-label={`${p.name} の最終状態`}>
        <header class="px-4 pt-4 pb-3 border-b border-bg-3">
          <div class="flex items-center gap-2">
            <span class="w-2.5 h-2.5 rounded-full shrink-0" style:background-color={p.color}></span>
            <h2 class="text-base font-semibold text-slate-100 truncate">{p.name}</h2>
            {#if p.character}<span class="text-xs text-slate-400">{p.character}</span>{/if}
          </div>
          <dl class="mt-3 grid grid-cols-3 gap-2 text-center">
            <div class="bg-bg-2 rounded-md py-1.5"><dt class="text-[11px] text-slate-400">HP</dt><dd class="text-sm font-semibold tabular text-slate-100">{p.last ? `${p.last.hp_out}/${p.last.max_hp_out}` : '—'}</dd></div>
            <div class="bg-bg-2 rounded-md py-1.5"><dt class="text-[11px] text-slate-400">ゴールド</dt><dd class="text-sm font-semibold tabular text-yellow-300">{p.last?.gold_out ?? '—'}</dd></div>
            <div class="bg-bg-2 rounded-md py-1.5"><dt class="text-[11px] text-slate-400">デッキ</dt><dd class="text-sm font-semibold tabular text-slate-100">{p.final.deck ? `${p.final.deck.length} 枚` : '—'}</dd></div>
          </dl>
        </header>

        <div class="px-4 py-3 space-y-4">
          <div>
            <h3 class="text-xs font-medium text-slate-400 mb-1.5">バッジ</h3>
            {#if p.final.badges == null}
              <div class="text-xs text-slate-500">ラン終了後に表示</div>
            {:else if p.final.badges === 'none'}
              <div class="text-xs text-slate-500">記録がありません (新しい mod で遊んだランから表示されます)</div>
            {:else if p.final.badges.length === 0}
              <div class="text-xs text-slate-500">なし</div>
            {:else}
              <div class="flex flex-wrap gap-1.5">
                {#each p.final.badges as b (b.id)}
                  {@const r = BADGE_RARITY[b.rarity] ?? { label: '', color: '#94a3b8' }}
                  <CardTooltip descriptionHtml={b.description}>
                    <span class="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-xs border cursor-help"
                          style:color={r.color} style:border-color={r.color + '66'} style:background-color={r.color + '14'}>
                      <span aria-hidden="true">●</span>{b.name}{#if r.label}<span class="sr-only">（{r.label}）</span>{/if}
                    </span>
                  </CardTooltip>
                {/each}
              </div>
            {/if}
          </div>

          {#if p.final.deck == null}
            <EmptyState title="デッキ・レリックの記録がありません" hint="新しい mod で遊んだランから表示されます。" />
          {:else}
            <div>
              <h3 class="text-xs font-medium text-slate-400 mb-1.5">レリック <span class="tabular text-slate-500">{p.final.relics?.length ?? 0}</span></h3>
              <div class="flex flex-wrap gap-1.5">
                {#each p.final.relics ?? [] as r, i (r.id + i)}<Chip label={r.name || r.id} tip={relicTip(s.catalog, r.id)} />{/each}
              </div>
            </div>
            {#if (p.final.potions?.length ?? 0) > 0}
              <div>
                <h3 class="text-xs font-medium text-slate-400 mb-1.5">ポーション</h3>
                <div class="flex flex-wrap gap-1.5">
                  {#each p.final.potions ?? [] as po, i (po.id + i)}<Chip label={po.name || po.id} tip={potionTip(s.catalog, po.id)} />{/each}
                </div>
              </div>
            {/if}
            <div class="space-y-2.5">
              <h3 class="text-xs font-medium text-slate-400">デッキ</h3>
              {#each groupDeck(p.final.deck) as g (g.key)}
                <div>
                  <div class="text-[11px] text-slate-500 mb-1">{g.label} <span class="tabular">{g.n}</span></div>
                  <div class="flex flex-wrap gap-1.5">
                    {#each g.items as it (it.card.id + (it.card.upgrade_level ?? 0) + (it.card.enchantment_id ?? ''))}
                      <CardChip label={it.card.name || it.card.id} rarity={it.card.rarity} upgraded={(it.card.upgrade_level ?? 0) > 0}
                                count={it.count} tip={cardTip(s.catalog, it.card.id, (it.card.upgrade_level ?? 0) > 0)} />
                    {/each}
                  </div>
                </div>
              {/each}
            </div>
          {/if}
        </div>
      </section>
    {/each}
  </div>
</div>
