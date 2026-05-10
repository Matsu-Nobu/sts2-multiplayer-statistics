<script lang="ts">
  import type { EventRecord } from '../lib/types';
  import type { CombatInfo } from '../lib/aggregate';
  import { buildFloorSummaries, roomVisual, type FloorSummary } from '../lib/runOverview';
  import { formatPowerName } from '../lib/powers';
  import { renderCardDescription, type CatalogLookup } from '../lib/catalog';
  import HpChart from './HpChart.svelte';
  import CombatView from './CombatView.svelte';
  import CardTooltip from './CardTooltip.svelte';

  interface Props {
    events: EventRecord[];
    combats: CombatInfo[];
    playerIds: string[];
    playerNames: Record<string, string>;
    powerNames: Record<string, string>;
    cardNames: Record<string, string>;
    catalog: CatalogLookup | null;
    onJumpToCombat?: (combatIndex: number) => void;
  }
  let { events, combats, playerIds, playerNames, powerNames, cardNames, catalog, onJumpToCombat }: Props = $props();

  // カタログから rarity / description を引いて tooltip 用 HTML を作る helpers
  function cardTip(id: string, upgraded: boolean) {
    const c = catalog?.card(id, upgraded);
    return {
      title: c?.name ?? id,
      html: c ? renderCardDescription(c.description) : '',
    };
  }
  function relicTip(id: string) {
    const r = catalog?.relic(id);
    return {
      title: r?.name ?? id,
      html: r ? renderCardDescription(r.description) : '',
    };
  }
  function potionTip(id: string) {
    const p = catalog?.potion(id);
    return {
      title: p?.name ?? id,
      html: p ? renderCardDescription(p.description) : '',
    };
  }
  function enchantmentTip(id: string) {
    const e = catalog?.enchantment(id);
    return {
      title: e?.name ?? id,
      html: e ? renderCardDescription(e.description) : '',
    };
  }

  // プレイヤー別タブ。最初は最初のプレイヤー
  let activePlayer: string = $state('');
  $effect.pre(() => {
    if (!playerIds.includes(activePlayer)) {
      activePlayer = playerIds[0] ?? '';
    }
  });

  // 単一プレイヤーセッションでは player_id でフィルタしない:
  // mod が emit する player_id (Player.NetId) は SP で "1" のような値になり、
  // session.players の steam_id とフォーマットが異なるため、一致しない event が
  // 全部除外されて報酬等が tooltip に出ない問題が発生する。
  let floors = $derived(buildFloorSummaries(
    events,
    playerIds.length > 1 ? (activePlayer || undefined) : undefined,
  ));
  let selectedFloor: number | null = $state(null);
  let selected = $derived(
    selectedFloor != null ? floors.find(f => f.floor === selectedFloor) ?? null : null
  );

  // グラフからのクリックは「選択」(toggle なし)
  function selectFloor(f: number) { selectedFloor = f; }

  function onFloorSelect(e: Event) {
    const v = (e.target as HTMLSelectElement).value;
    selectedFloor = v === '' ? null : Number(v);
  }

  // 該当階の戦闘 (CombatView を流用)
  let selectedCombat = $derived(
    selected?.combat_index != null
      ? combats.find(c => c.combat_index === selected!.combat_index) ?? null
      : null
  );
  let selectedCombatEvents = $derived(
    selected?.combat_index != null
      ? events.filter(e => e.combat_index === selected!.combat_index)
      : []
  );

  let hasAcq     = $derived(!!selected && (selected.cards_obtained.length + selected.relics_obtained.length + selected.potions_obtained.length > 0));
  let hasDeckMod = $derived(!!selected && (selected.cards_upgraded.length + selected.cards_enchanted.length + selected.cards_removed.length > 0));
  let hasShop    = $derived(!!selected && selected.shop_purchases.length > 0);
  let hasChoice  = $derived(!!selected && (selected.rest_options.length + selected.event_choices.length + selected.card_choices.length > 0));

  function cardLabel(id: string): string {
    return cardNames[id] ?? id;
  }

  function itemKindLabel(kind: string): string {
    switch (kind) {
      case 'MerchantCardEntry':         return 'カード';
      case 'MerchantPotionEntry':       return 'ポーション';
      case 'MerchantRelicEntry':        return 'レリック';
      case 'MerchantCardRemovalEntry':  return 'カード除去';
      default:                          return kind;
    }
  }

</script>

<div class="space-y-4">

  {#if floors.length === 0}
    <div class="bg-bg-1 border border-bg-3 rounded-lg p-6 text-center text-slate-500">
      このセッションはラン全体ビュー未対応の旧形式です。最新 mod でプレイすればここに HP 推移が表示されます。
    </div>
  {:else}

    <!-- プレイヤータブ (複数人いる場合のみ) -->
    {#if playerIds.length > 1}
      <div class="flex gap-2">
        {#each playerIds as pid (pid)}
          <button
            type="button"
            class="px-3 py-1 rounded text-sm border {activePlayer === pid ? 'bg-accent text-bg-0 border-accent' : 'bg-bg-2 border-bg-3 text-slate-300 hover:bg-bg-3'}"
            onclick={() => { activePlayer = pid; selectedFloor = null; }}
          >
            {playerNames[pid] ?? pid}
          </button>
        {/each}
      </div>
    {/if}

    <!-- HP 推移グラフ -->
    <HpChart {floors} {selectedFloor} onSelect={selectFloor} {cardNames} />

    <!-- 階セレクタ（プルダウン） -->
    <div class="flex items-center gap-3">
      <label for="floor-select" class="text-xs uppercase tracking-wide text-slate-400">階詳細</label>
      <select
        id="floor-select"
        class="bg-bg-1 border border-bg-3 rounded px-3 py-2 text-sm text-slate-200 hover:bg-bg-2 focus:outline-none focus:ring-1 focus:ring-accent min-w-[280px]"
        value={selectedFloor == null ? '' : String(selectedFloor)}
        onchange={onFloorSelect}
      >
        <option value="">— 階を選んでください —</option>
        {#each floors as f (f.floor)}
          {@const v = roomVisual(f.room_type)}
          <option value={String(f.floor)}>
            {f.floor} - {f.encounter_name ? `${v.label} (${f.encounter_name})` : v.label}
          </option>
        {/each}
      </select>
    </div>

    <!-- インライン階詳細 -->
    {#if selected}
      {@const v = roomVisual(selected.room_type)}
      <section class="bg-bg-1 border border-bg-3 rounded-lg p-4 space-y-3">
        <header class="flex items-center justify-between">
          <h3 class="text-base font-medium">
            <span class="mr-2">{v.emoji}</span>
            <span class="text-slate-200">階 {selected.floor}</span>
            <span class="ml-2 text-slate-400">{v.label}</span>
            {#if selected.encounter_name}
              <span class="ml-2 text-slate-500">— {selected.encounter_name}</span>
            {/if}
          </h3>
          <div class="flex items-center gap-2">
            {#if selected.combat_index != null && onJumpToCombat}
              <button
                type="button"
                class="text-xs px-3 py-1 rounded bg-accent text-bg-0 hover:opacity-90"
                onclick={() => onJumpToCombat!(selected!.combat_index!)}
              >この戦闘の統計を見る →</button>
            {/if}
            <button type="button" class="text-xs text-slate-400 hover:text-slate-200" onclick={() => selectedFloor = null}>閉じる ✕</button>
          </div>
        </header>

        <!-- 共通サマリ (HP / Gold のみ) -->
        <div class="grid grid-cols-2 gap-2 text-xs">
          <div class="bg-bg-2 border border-bg-3 rounded px-3 py-2">
            <div class="text-slate-500 uppercase tracking-wide">HP</div>
            <div class="text-slate-200 tabular">{selected.hp_in}/{selected.max_hp_in} → {selected.hp_out}/{selected.max_hp_out}</div>
          </div>
          <div class="bg-bg-2 border border-bg-3 rounded px-3 py-2">
            <div class="text-slate-500 uppercase tracking-wide">ゴールド</div>
            <div class="text-slate-200 tabular">{selected.gold_in} → {selected.gold_out} ({selected.gold_out - selected.gold_in >= 0 ? '+' : ''}{selected.gold_out - selected.gold_in})</div>
          </div>
        </div>

        <!-- 階の詳細: 全 section 統一 chip 形式 -->
        <!--
          card chip: rarity で背景色 + 枠 / is_upgraded で文字色 (黄緑)
            Common  → グレー
            Uncommon → 水色
            Rare     → 黄色
            Curse / Status / Token / Basic / 等 → デフォルト (グレー)
          generic chip: rarity 概念なし
        -->
        {#snippet genericChip(label: string, sub: string = '', tip: { title: string; html: string } | null = null)}
          {#if tip && (tip.title || tip.html)}
            <CardTooltip titleText={tip.title} descriptionHtml={tip.html}>
              <span class="px-2 py-0.5 rounded text-sm border bg-bg-2 border-bg-3 text-slate-200 cursor-help">
                {label}{#if sub}<span class="text-slate-400 ml-1">{sub}</span>{/if}
              </span>
            </CardTooltip>
          {:else}
            <span class="px-2 py-0.5 rounded text-sm border bg-bg-2 border-bg-3 text-slate-200">
              {label}{#if sub}<span class="text-slate-400 ml-1">{sub}</span>{/if}
            </span>
          {/if}
        {/snippet}

        {#snippet row(label: string)}
          <div class="text-xs uppercase text-slate-400 tracking-wide pt-1">{label}</div>
        {/snippet}

        {#snippet cardChip(label: string, rarity: string | undefined, isUpgraded: boolean | undefined, suffix: string = '', skipped: boolean = false, tip: { title: string; html: string } | null = null)}
          {@const bg = rarity === 'Common' ? 'bg-slate-700/60 border-slate-500' : rarity === 'Uncommon' ? 'bg-sky-900/60 border-sky-500' : rarity === 'Rare' ? 'bg-yellow-900/60 border-yellow-500' : 'bg-bg-2 border-bg-3'}
          {@const fg = isUpgraded ? 'text-lime-300' : (skipped ? 'text-slate-500' : 'text-slate-200')}
          {@const skipMark = skipped ? 'opacity-60 line-through' : ''}
          {#if tip && (tip.title || tip.html)}
            <CardTooltip titleText={tip.title} descriptionHtml={tip.html}>
              <span class="px-2 py-0.5 rounded text-sm border {bg} {fg} {skipMark} cursor-help">
                {label}{#if suffix}<span class="text-yellow-400 ml-1">{suffix}</span>{/if}
              </span>
            </CardTooltip>
          {:else}
            <span class="px-2 py-0.5 rounded text-sm border {bg} {fg} {skipMark}">
              {label}{#if suffix}<span class="text-yellow-400 ml-1">{suffix}</span>{/if}
            </span>
          {/if}
        {/snippet}

        {#snippet groupHeader(title: string)}
          <h4 class="text-[11px] uppercase tracking-widest text-slate-500 font-semibold border-b border-bg-3 pb-1 mb-2">{title}</h4>
        {/snippet}

        {#snippet kvRow(label: string)}
          <div class="text-xs text-slate-400 pt-1 whitespace-nowrap">{label}</div>
        {/snippet}

        <!-- 階の詳細: 意味グループに整理 -->
        <div class="space-y-4">

          <!-- グループ1: 入手 -->
          {#if hasAcq}
            <div class="bg-bg-0/30 border border-bg-3 rounded p-3">
              {@render groupHeader('入手')}
              <div class="grid grid-cols-[6rem_1fr] gap-x-3 gap-y-1.5 items-start">
                {#if selected.cards_obtained.length > 0}
                  {@render kvRow('カード')}
                  <div class="flex flex-wrap gap-1.5">
                    {#each selected.cards_obtained as c}{@render cardChip(c.card_name ?? cardLabel(c.card_id), c.card_rarity, c.is_upgraded, '', false, cardTip(c.card_id, !!c.is_upgraded))}{/each}
                  </div>
                {/if}
                {#if selected.relics_obtained.length > 0}
                  {@render kvRow('レリック')}
                  <div class="flex flex-wrap gap-1.5">
                    {#each selected.relics_obtained as r}{@render genericChip(r.relic_name ?? r.relic_id, '', relicTip(r.relic_id))}{/each}
                  </div>
                {/if}
                {#if selected.potions_obtained.length > 0}
                  {@render kvRow('ポーション')}
                  <div class="flex flex-wrap gap-1.5">
                    {#each selected.potions_obtained as p}{@render genericChip(p.potion_name ?? p.potion_id, '', potionTip(p.potion_id))}{/each}
                  </div>
                {/if}
              </div>
            </div>
          {/if}

          <!-- グループ2: デッキ改造 -->
          {#if hasDeckMod}
            <div class="bg-bg-0/30 border border-bg-3 rounded p-3">
              {@render groupHeader('デッキ改造')}
              <div class="grid grid-cols-[6rem_1fr] gap-x-3 gap-y-1.5 items-start">
                {#if selected.cards_upgraded.length > 0}
                  {@render kvRow('アップグレード')}
                  <div class="flex flex-wrap gap-1.5">
                    {#each selected.cards_upgraded as c}{@render cardChip(c.card_name ?? cardLabel(c.card_id), c.card_rarity, true, '', false, cardTip(c.card_id, true))}{/each}
                  </div>
                {/if}
                {#if selected.cards_enchanted.length > 0}
                  {@render kvRow('エンチャント')}
                  <div class="flex flex-wrap gap-1.5">
                    {#each selected.cards_enchanted as e}{@render genericChip(e.card_name ?? cardLabel(e.card_id), `← ${e.enchantment_id}`, enchantmentTip(e.enchantment_id))}{/each}
                  </div>
                {/if}
                {#if selected.cards_removed.length > 0}
                  {@render kvRow('除去')}
                  <div class="flex flex-wrap gap-1.5">
                    {#each selected.cards_removed as c}{@render genericChip(c.card_name ?? cardLabel(c.card_id), '', cardTip(c.card_id, false))}{/each}
                  </div>
                {/if}
              </div>
            </div>
          {/if}

          <!-- グループ3: ショップ取引 -->
          {#if hasShop}
            <div class="bg-bg-0/30 border border-bg-3 rounded p-3">
              {@render groupHeader('ショップ購入')}
              <div class="flex flex-wrap gap-1.5">
                {#each selected.shop_purchases as p}
                  {#if p.card_id}
                    {@render cardChip(p.card_name ?? cardLabel(p.card_id), (p as any).card_rarity, (p as any).is_upgraded, `${p.gold_spent}G`, false, cardTip(p.card_id, !!(p as any).is_upgraded))}
                  {:else if p.relic_id}
                    {@render genericChip(p.relic_name ?? p.relic_id, `${p.gold_spent} ゴールド`, relicTip(p.relic_id))}
                  {:else if p.potion_id}
                    {@render genericChip(p.potion_name ?? p.potion_id, `${p.gold_spent} ゴールド`, potionTip(p.potion_id))}
                  {:else}
                    {@render genericChip(itemKindLabel(p.item_kind), `${p.gold_spent} ゴールド`)}
                  {/if}
                {/each}
              </div>
            </div>
          {/if}

          <!-- グループ4: 選択ログ -->
          {#if hasChoice}
            <div class="bg-bg-0/30 border border-bg-3 rounded p-3">
              {@render groupHeader('選択')}
              <div class="grid grid-cols-[6rem_1fr] gap-x-3 gap-y-1.5 items-start">
                {#if selected.rest_options.length > 0}
                  {@render kvRow('休憩所')}
                  <div class="flex flex-wrap gap-1.5">
                    {#each selected.rest_options as o}{@render genericChip(o)}{/each}
                  </div>
                {/if}
                {#if selected.event_choices.length > 0}
                  {@render kvRow('イベント')}
                  <div class="flex flex-wrap gap-1.5">
                    {#each selected.event_choices as c}{@render genericChip(c.title || c.history_name || c.text_key)}{/each}
                  </div>
                {/if}
                {#if selected.card_choices.length > 0}
                  {@render kvRow('カード選択肢')}
                  <div class="space-y-1">
                    {#each selected.card_choices as group, gi}
                      <div class="flex flex-wrap items-center gap-1.5">
                        {#if selected.card_choices.length > 1}<span class="text-slate-500 text-xs">#{gi + 1}</span>{/if}
                        {#each group.choices as c}{@render cardChip(c.card_name || cardLabel(c.card_id), c.card_rarity, c.is_upgraded, '', !c.was_picked, cardTip(c.card_id, !!c.is_upgraded))}{/each}
                      </div>
                    {/each}
                  </div>
                {/if}
              </div>
            </div>
          {/if}

          {#if !hasAcq && !hasDeckMod && !hasShop && !hasChoice}
            <div class="text-slate-500 text-sm">この階に記録された変化はありません。</div>
          {/if}
        </div>
      </section>
    {/if}

  {/if}
</div>
