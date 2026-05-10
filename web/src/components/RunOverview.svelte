<script lang="ts">
  import type { EventRecord } from '../lib/types';
  import type { CombatInfo } from '../lib/aggregate';
  import { buildFloorSummaries, roomVisual, type FloorSummary } from '../lib/runOverview';
  import { formatPowerName } from '../lib/powers';
  import HpChart from './HpChart.svelte';
  import CombatView from './CombatView.svelte';

  interface Props {
    events: EventRecord[];
    combats: CombatInfo[];
    playerIds: string[];
    playerNames: Record<string, string>;
    powerNames: Record<string, string>;
    cardNames: Record<string, string>;
    onJumpToCombat?: (combatIndex: number) => void;
  }
  let { events, combats, playerIds, playerNames, powerNames, cardNames, onJumpToCombat }: Props = $props();

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
        {#snippet genericChip(label: string, sub: string = '')}
          <span class="px-2 py-0.5 rounded text-sm border bg-bg-2 border-bg-3 text-slate-200">
            {label}{#if sub}<span class="text-slate-400 ml-1">{sub}</span>{/if}
          </span>
        {/snippet}

        {#snippet row(label: string)}
          <div class="text-xs uppercase text-slate-400 tracking-wide pt-1">{label}</div>
        {/snippet}

        <div class="space-y-2">
          {#if selected.cards_obtained.length > 0}
            <div class="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 items-start">
              {@render row('カード入手')}
              <div class="flex flex-wrap gap-1.5">
                {#each selected.cards_obtained as c}
                  {@const bg = c.card_rarity === 'Common' ? 'bg-slate-700/60 border-slate-500' : c.card_rarity === 'Uncommon' ? 'bg-sky-900/60 border-sky-500' : c.card_rarity === 'Rare' ? 'bg-yellow-900/60 border-yellow-500' : 'bg-bg-2 border-bg-3'}
                  {@const fg = c.is_upgraded ? 'text-lime-300' : 'text-slate-200'}
                  <span class="px-2 py-0.5 rounded text-sm border {bg} {fg}">{c.card_name ?? cardLabel(c.card_id)}</span>
                {/each}
              </div>
            </div>
          {/if}

          {#if selected.relics_obtained.length > 0}
            <div class="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 items-start">
              {@render row('レリック入手')}
              <div class="flex flex-wrap gap-1.5">
                {#each selected.relics_obtained as r}{@render genericChip(r.relic_name ?? r.relic_id)}{/each}
              </div>
            </div>
          {/if}

          {#if selected.potions_obtained.length > 0}
            <div class="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 items-start">
              {@render row('ポーション入手')}
              <div class="flex flex-wrap gap-1.5">
                {#each selected.potions_obtained as p}{@render genericChip(p.potion_name ?? p.potion_id)}{/each}
              </div>
            </div>
          {/if}

          {#if selected.cards_upgraded.length > 0}
            <div class="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 items-start">
              {@render row('カードアップグレード')}
              <div class="flex flex-wrap gap-1.5">
                {#each selected.cards_upgraded as c}
                  {@const bg = c.card_rarity === 'Common' ? 'bg-slate-700/60 border-slate-500' : c.card_rarity === 'Uncommon' ? 'bg-sky-900/60 border-sky-500' : c.card_rarity === 'Rare' ? 'bg-yellow-900/60 border-yellow-500' : 'bg-bg-2 border-bg-3'}
                  <span class="px-2 py-0.5 rounded text-sm border {bg} text-lime-300">{c.card_name ?? cardLabel(c.card_id)}</span>
                {/each}
              </div>
            </div>
          {/if}

          {#if selected.cards_enchanted.length > 0}
            <div class="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 items-start">
              {@render row('エンチャント付与')}
              <div class="flex flex-wrap gap-1.5">
                {#each selected.cards_enchanted as e}{@render genericChip(e.card_name ?? cardLabel(e.card_id), `← ${e.enchantment_id}`)}{/each}
              </div>
            </div>
          {/if}

          {#if selected.cards_removed.length > 0}
            <div class="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 items-start">
              {@render row('カード除去')}
              <div class="flex flex-wrap gap-1.5">
                {#each selected.cards_removed as c}{@render genericChip(c.card_name ?? cardLabel(c.card_id))}{/each}
              </div>
            </div>
          {/if}

          {#if selected.shop_purchases.length > 0}
            <div class="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 items-start">
              {@render row('ショップ購入')}
              <div class="flex flex-wrap gap-1.5">
                {#each selected.shop_purchases as p}
                  {#if p.card_id}
                    {@const bg = (p as any).card_rarity === 'Common' ? 'bg-slate-700/60 border-slate-500' : (p as any).card_rarity === 'Uncommon' ? 'bg-sky-900/60 border-sky-500' : (p as any).card_rarity === 'Rare' ? 'bg-yellow-900/60 border-yellow-500' : 'bg-bg-2 border-bg-3'}
                    {@const fg = (p as any).is_upgraded ? 'text-lime-300' : 'text-slate-200'}
                    <span class="px-2 py-0.5 rounded text-sm border {bg} {fg}">
                      {p.card_name ?? cardLabel(p.card_id)}<span class="text-yellow-400 ml-1">({p.gold_spent}G)</span>
                    </span>
                  {:else}
                    {@render genericChip(
                      p.relic_id ? (p.relic_name ?? p.relic_id) :
                      p.potion_id ? (p.potion_name ?? p.potion_id) :
                      itemKindLabel(p.item_kind),
                      `${p.gold_spent} ゴールド`,
                    )}
                  {/if}
                {/each}
              </div>
            </div>
          {/if}

          {#if selected.rest_options.length > 0}
            <div class="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 items-start">
              {@render row('休憩所')}
              <div class="flex flex-wrap gap-1.5">
                {#each selected.rest_options as o}{@render genericChip(o)}{/each}
              </div>
            </div>
          {/if}

          {#if selected.event_choices.length > 0}
            <div class="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 items-start">
              {@render row('イベント選択')}
              <div class="flex flex-wrap gap-1.5">
                {#each selected.event_choices as c}{@render genericChip(c.title || c.history_name || c.text_key)}{/each}
              </div>
            </div>
          {/if}

          <!-- 提示されたカード選択肢: pick / skip + rarity / upgraded を視覚化 -->
          {#if selected.card_choices.length > 0}
            <div class="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1 items-start">
              {@render row('提示カード選択肢')}
              <div class="space-y-1">
                {#each selected.card_choices as group, gi}
                  <div class="flex flex-wrap items-center gap-1.5">
                    {#if selected.card_choices.length > 1}<span class="text-slate-500 text-xs">#{gi + 1}</span>{/if}
                    {#each group.choices as c}
                      {@const bg = c.card_rarity === 'Common' ? 'bg-slate-700/60 border-slate-500' : c.card_rarity === 'Uncommon' ? 'bg-sky-900/60 border-sky-500' : c.card_rarity === 'Rare' ? 'bg-yellow-900/60 border-yellow-500' : 'bg-bg-2 border-bg-3'}
                      {@const fg = c.is_upgraded ? 'text-lime-300' : (c.was_picked ? 'text-slate-100' : 'text-slate-500')}
                      {@const skipMark = c.was_picked ? '' : 'opacity-60 line-through'}
                      <span class="px-2 py-0.5 rounded text-sm border {bg} {fg} {skipMark}">{c.card_name || cardLabel(c.card_id)}</span>
                    {/each}
                  </div>
                {/each}
              </div>
            </div>
          {/if}
        </div>
      </section>
    {/if}

  {/if}
</div>
