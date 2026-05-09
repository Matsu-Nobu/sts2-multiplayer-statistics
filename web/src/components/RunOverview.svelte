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
  }
  let { events, combats, playerIds, playerNames, powerNames, cardNames }: Props = $props();

  // プレイヤー別タブ。最初は最初のプレイヤー
  let activePlayer: string = $state('');
  $effect.pre(() => {
    if (!playerIds.includes(activePlayer)) {
      activePlayer = playerIds[0] ?? '';
    }
  });

  let floors = $derived(buildFloorSummaries(events, activePlayer || undefined));
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
            {v.emoji} 階 {f.floor} — {v.label}{f.encounter_name ? ` (${f.encounter_name})` : ''}
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
          <button type="button" class="text-xs text-slate-400 hover:text-slate-200" onclick={() => selectedFloor = null}>閉じる ✕</button>
        </header>

        <!-- 共通サマリ -->
        <div class="grid grid-cols-2 sm:grid-cols-4 gap-2 text-xs">
          <div class="bg-bg-2 border border-bg-3 rounded px-3 py-2">
            <div class="text-slate-500 uppercase tracking-wide">HP</div>
            <div class="text-slate-200 tabular">{selected.hp_in}/{selected.max_hp_in} → {selected.hp_out}/{selected.max_hp_out}</div>
          </div>
          <div class="bg-bg-2 border border-bg-3 rounded px-3 py-2">
            <div class="text-slate-500 uppercase tracking-wide">ゴールド</div>
            <div class="text-slate-200 tabular">{selected.gold_in} → {selected.gold_out} ({selected.gold_out - selected.gold_in >= 0 ? '+' : ''}{selected.gold_out - selected.gold_in})</div>
          </div>
          <div class="bg-bg-2 border border-bg-3 rounded px-3 py-2">
            <div class="text-slate-500 uppercase tracking-wide">与ダメ / 被ダメ</div>
            <div class="text-slate-200 tabular">{selected.damage_dealt} / {selected.damage_taken}</div>
          </div>
          <div class="bg-bg-2 border border-bg-3 rounded px-3 py-2">
            <div class="text-slate-500 uppercase tracking-wide">入手</div>
            <div class="text-slate-200">
              {#if selected.cards_obtained.length === 0 && selected.relics_obtained.length === 0 && selected.potions_obtained.length === 0}
                —
              {:else}
                {selected.cards_obtained.length} card / {selected.relics_obtained.length} relic / {selected.potions_obtained.length} potion
              {/if}
            </div>
          </div>
        </div>

        <!-- room_type ごとの詳細 -->
        {#if selectedCombat}
          <CombatView
            combat={selectedCombat}
            {playerIds}
            {playerNames}
            {powerNames}
            {cardNames}
            combatEvents={selectedCombatEvents}
          />
        {:else if selected.room_type === 'Shop'}
          <div class="space-y-2">
            <h4 class="text-xs uppercase text-slate-400 tracking-wide">購入履歴</h4>
            {#if selected.shop_purchases.length === 0}
              <div class="text-slate-500 text-sm">購入なし</div>
            {:else}
              <ul class="text-sm space-y-1">
                {#each selected.shop_purchases as p}
                  <li class="flex items-center gap-2">
                    <span class="text-slate-500 w-24 truncate">{itemKindLabel(p.item_kind)}</span>
                    <span class="flex-1 text-slate-200">
                      {#if p.card_id}{p.card_name ?? cardLabel(p.card_id)}
                      {:else if p.relic_id}{p.relic_name ?? p.relic_id}
                      {:else if p.potion_id}{p.potion_name ?? p.potion_id}
                      {:else}—{/if}
                    </span>
                    <span class="tabular text-yellow-400">-{p.gold_spent}g</span>
                  </li>
                {/each}
              </ul>
            {/if}
            {#if selected.cards_removed.length > 0}
              <h4 class="text-xs uppercase text-slate-400 tracking-wide pt-2">カード除去</h4>
              <ul class="text-sm">
                {#each selected.cards_removed as c}
                  <li>{c.card_name ?? cardLabel(c.card_id)}</li>
                {/each}
              </ul>
            {/if}
          </div>
        {:else if selected.room_type === 'RestSite'}
          <div class="space-y-2">
            <h4 class="text-xs uppercase text-slate-400 tracking-wide">休憩所</h4>
            <div class="text-sm">選択: {selected.rest_options.join(', ') || '—'}</div>
            {#if selected.cards_upgraded.length > 0}
              <h4 class="text-xs uppercase text-slate-400 tracking-wide pt-2">アップグレード</h4>
              <ul class="text-sm">
                {#each selected.cards_upgraded as c}
                  <li>{c.card_name ?? cardLabel(c.card_id)}</li>
                {/each}
              </ul>
            {/if}
          </div>
        {:else if selected.room_type === 'Treasure'}
          <div class="space-y-2">
            <h4 class="text-xs uppercase text-slate-400 tracking-wide">宝箱</h4>
            {#if selected.relics_obtained.length === 0}
              <div class="text-slate-500 text-sm">入手なし</div>
            {:else}
              <ul class="text-sm">{#each selected.relics_obtained as r}<li>{r.relic_name ?? r.relic_id}</li>{/each}</ul>
            {/if}
          </div>
        {:else if selected.room_type === 'Event'}
          <div class="space-y-2">
            <h4 class="text-xs uppercase text-slate-400 tracking-wide">イベント</h4>
            {#if selected.event_choices.length === 0}
              <div class="text-sm text-slate-500">選択ログなし</div>
            {:else}
              <ul class="text-sm space-y-1">
                {#each selected.event_choices as c}
                  <li>
                    <span class="text-slate-200">選択:</span>
                    <span class="text-slate-300">{c.title || c.history_name || c.text_key}</span>
                  </li>
                {/each}
              </ul>
            {/if}
            {#if selected.cards_enchanted.length > 0}
              <h4 class="text-xs uppercase text-slate-400 tracking-wide pt-2">エンチャント付与</h4>
              <ul class="text-sm">
                {#each selected.cards_enchanted as e}
                  <li>{e.card_name ?? cardLabel(e.card_id)} ← {e.enchantment_id} (×{e.amount})</li>
                {/each}
              </ul>
            {/if}
            {#if selected.cards_obtained.length > 0}
              <h4 class="text-xs uppercase text-slate-400 tracking-wide pt-2">カード入手</h4>
              <ul class="text-sm">
                {#each selected.cards_obtained as c}<li>{c.card_name ?? cardLabel(c.card_id)}</li>{/each}
              </ul>
            {/if}
            {#if selected.relics_obtained.length > 0}
              <h4 class="text-xs uppercase text-slate-400 tracking-wide pt-2">レリック入手</h4>
              <ul class="text-sm">{#each selected.relics_obtained as r}<li>{r.relic_name ?? r.relic_id}</li>{/each}</ul>
            {/if}
            {#if selected.potions_obtained.length > 0}
              <h4 class="text-xs uppercase text-slate-400 tracking-wide pt-2">ポーション入手</h4>
              <ul class="text-sm">{#each selected.potions_obtained as p}<li>{p.potion_name ?? p.potion_id}</li>{/each}</ul>
            {/if}
            {#if selected.cards_removed.length > 0}
              <h4 class="text-xs uppercase text-slate-400 tracking-wide pt-2">カード除去</h4>
              <ul class="text-sm">{#each selected.cards_removed as c}<li>{c.card_name ?? cardLabel(c.card_id)}</li>{/each}</ul>
            {/if}
          </div>
        {/if}
      </section>
    {/if}

  {/if}
</div>
