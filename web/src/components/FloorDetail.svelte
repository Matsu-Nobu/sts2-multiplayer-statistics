<script lang="ts">
  // 1 階分の詳細 (spec run-overview.md §2.4): 入手・デッキ改造・ショップ購入・選択。空のグループは出さない (CLAUDE.md §3.3)。
  import type { FloorSummary } from '../lib/runOverview';
  import { useSession } from '../lib/session';
  import { cardTip, relicTip, potionTip, enchantmentTip } from '../lib/tips';
  import CardChip from './ui/CardChip.svelte';
  import Chip from './ui/Chip.svelte';
  import EmptyState from './ui/EmptyState.svelte';

  interface Props { f: FloorSummary }
  let { f }: Props = $props();
  const s = useSession();
  const name = (id: string, n?: string) => n ?? s.cardNames[id] ?? id;

  let hasAcq     = $derived(f.cards_obtained.length + f.relics_obtained.length + f.potions_obtained.length > 0);
  let hasDeckMod = $derived(f.cards_upgraded.length + f.cards_enchanted.length + f.cards_transformed.length + f.cards_removed.length > 0);
  let hasShop    = $derived(f.shop_purchases.length > 0);
  let hasChoice  = $derived(f.rest_options.length + f.event_choices.length + f.card_choices.length > 0);
</script>

{#snippet group(title: string, body: import('svelte').Snippet)}
  <section class="bg-bg-1 border border-bg-3 rounded-lg p-4">
    <h2 class="text-base font-semibold text-slate-100 mb-3">{title}</h2>
    {@render body()}
  </section>
{/snippet}

{#snippet kv(label: string, body: import('svelte').Snippet)}
  <div class="grid grid-cols-1 sm:grid-cols-[7rem_1fr] gap-x-3 gap-y-1 items-start py-1">
    <div class="text-xs text-slate-400 sm:pt-1">{label}</div>
    <div class="flex flex-wrap gap-1.5">{@render body()}</div>
  </div>
{/snippet}

<div class="space-y-4">
  {#if hasAcq}
    {#snippet acq()}
      {#if f.cards_obtained.length > 0}
        {#snippet b()}{#each f.cards_obtained as c}<CardChip label={name(c.card_id, c.card_name)} rarity={c.card_rarity} upgraded={!!c.is_upgraded} tip={cardTip(s.catalog, c.card_id, !!c.is_upgraded)} />{/each}{/snippet}
        {@render kv('カード', b)}
      {/if}
      {#if f.relics_obtained.length > 0}
        {#snippet b()}{#each f.relics_obtained as r}<Chip label={r.relic_name ?? r.relic_id} tip={relicTip(s.catalog, r.relic_id)} />{/each}{/snippet}
        {@render kv('レリック', b)}
      {/if}
      {#if f.potions_obtained.length > 0}
        {#snippet b()}{#each f.potions_obtained as p}<Chip label={p.potion_name ?? p.potion_id} tip={potionTip(s.catalog, p.potion_id)} />{/each}{/snippet}
        {@render kv('ポーション', b)}
      {/if}
    {/snippet}
    {@render group('入手', acq)}
  {/if}

  {#if hasDeckMod}
    {#snippet mod()}
      {#if f.cards_upgraded.length > 0}
        {#snippet b()}{#each f.cards_upgraded as c}<CardChip label={name(c.card_id, c.card_name)} rarity={c.card_rarity} upgraded tip={cardTip(s.catalog, c.card_id, true)} />{/each}{/snippet}
        {@render kv('アップグレード', b)}
      {/if}
      {#if f.cards_enchanted.length > 0}
        {#snippet b()}{#each f.cards_enchanted as e}<Chip label={name(e.card_id, e.card_name)} sub={`← ${e.enchantment_name ?? s.catalog?.enchantment(e.enchantment_id)?.name ?? e.enchantment_id}`} tip={enchantmentTip(s.catalog, e.enchantment_id)} />{/each}{/snippet}
        {@render kv('エンチャント', b)}
      {/if}
      {#if f.cards_transformed.length > 0}
        {#snippet b()}{#each f.cards_transformed as t}
          <span class="inline-flex items-center gap-1">
            <CardChip label={name(t.from.card_id, t.from.card_name)} rarity={t.from.card_rarity} upgraded={!!t.from.is_upgraded} tip={cardTip(s.catalog, t.from.card_id, !!t.from.is_upgraded)} />
            <span class="text-slate-500" aria-label="から">→</span>
            <CardChip label={name(t.to.card_id, t.to.card_name)} rarity={t.to.card_rarity} upgraded={!!t.to.is_upgraded} tip={cardTip(s.catalog, t.to.card_id, !!t.to.is_upgraded)} />
          </span>
        {/each}{/snippet}
        {@render kv('変化', b)}
      {/if}
      {#if f.cards_removed.length > 0}
        {#snippet b()}{#each f.cards_removed as c}<Chip label={name(c.card_id, c.card_name)} tip={cardTip(s.catalog, c.card_id, false)} />{/each}{/snippet}
        {@render kv('除去', b)}
      {/if}
    {/snippet}
    {@render group('デッキ改造', mod)}
  {/if}

  {#if hasShop}
    {#snippet shop()}
      <div class="flex flex-wrap gap-1.5">
        {#each f.shop_purchases as p}
          {@const price = p.gold_spent != null ? `${p.gold_spent}G` : ''}
          {#if p.kind === 'card'}
            <CardChip label={p.name || name(p.id)} rarity={p.rarity} upgraded={!!p.is_upgraded} suffix={price} tip={cardTip(s.catalog, p.id, !!p.is_upgraded)} />
          {:else if p.kind === 'relic'}
            <Chip label={p.name || p.id} sub={price} subClass="text-yellow-400" tip={relicTip(s.catalog, p.id)} />
          {:else}
            <Chip label={p.name || p.id} sub={price} subClass="text-yellow-400" tip={potionTip(s.catalog, p.id)} />
          {/if}
        {/each}
      </div>
    {/snippet}
    {@render group('ショップ購入', shop)}
  {/if}

  {#if hasChoice}
    {#snippet choice()}
      {#if f.rest_options.length > 0}
        {#snippet b()}{#each f.rest_options as o}<Chip label={o} />{/each}{/snippet}
        {@render kv('休憩所', b)}
      {/if}
      {#if f.event_choices.length > 0}
        {#snippet b()}{#each f.event_choices as c}<Chip label={c.title} />{/each}{/snippet}
        {@render kv('イベント', b)}
      {/if}
      {#if f.card_choices.length > 0}
        {#snippet b()}{#each f.card_choices as g}{#each g.choices as c}<CardChip label={c.card_name || name(c.card_id)} rarity={c.card_rarity} upgraded={!!c.is_upgraded} skipped={!c.was_picked} tip={cardTip(s.catalog, c.card_id, !!c.is_upgraded)} />{/each}{/each}{/snippet}
        {@render kv('カード選択肢', b)}
      {/if}
    {/snippet}
    {@render group('選択', choice)}
  {/if}

  {#if !hasAcq && !hasDeckMod && !hasShop && !hasChoice}
    <div class="bg-bg-1 border border-bg-3 rounded-lg"><EmptyState title="変化なし" hint="この階に記録された入手・改造・選択はありません。" /></div>
  {/if}
</div>
