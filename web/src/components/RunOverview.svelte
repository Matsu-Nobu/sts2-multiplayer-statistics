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

        <!-- 階の詳細: 入手物 / イベント / 戦闘結果 (戦闘統計は出さない) -->
        <div class="space-y-2">
          {#if selected.cards_obtained.length > 0}
            <div>
              <h4 class="text-xs uppercase text-slate-400 tracking-wide">カード入手</h4>
              <ul class="text-sm">
                {#each selected.cards_obtained as c}<li>{c.card_name ?? cardLabel(c.card_id)}</li>{/each}
              </ul>
            </div>
          {/if}
          {#if selected.card_choices.length > 0}
            <div>
              <h4 class="text-xs uppercase text-slate-400 tracking-wide">提示されたカード選択肢</h4>
              {#each selected.card_choices as group, gi}
                <div class="text-sm flex flex-wrap gap-2 mt-1">
                  {#if selected.card_choices.length > 1}<span class="text-slate-500 text-xs">#{gi + 1}</span>{/if}
                  {#each group.choices as c}
                    <span class="px-2 py-0.5 rounded {c.was_picked ? 'bg-accent/20 text-accent border border-accent/40' : 'bg-bg-2 text-slate-400 border border-bg-3'}">
                      {c.was_picked ? '✓ ' : ''}{c.card_name || cardLabel(c.card_id)}
                    </span>
                  {/each}
                </div>
              {/each}
            </div>
          {/if}
          {#if selected.relics_obtained.length > 0}
            <div>
              <h4 class="text-xs uppercase text-slate-400 tracking-wide">レリック入手</h4>
              <ul class="text-sm">{#each selected.relics_obtained as r}<li>{r.relic_name ?? r.relic_id}</li>{/each}</ul>
            </div>
          {/if}
          {#if selected.potions_obtained.length > 0}
            <div>
              <h4 class="text-xs uppercase text-slate-400 tracking-wide">ポーション入手</h4>
              <ul class="text-sm">{#each selected.potions_obtained as p}<li>{p.potion_name ?? p.potion_id}</li>{/each}</ul>
            </div>
          {/if}
          {#if selected.cards_upgraded.length > 0}
            <div>
              <h4 class="text-xs uppercase text-slate-400 tracking-wide">カードアップグレード</h4>
              <ul class="text-sm">
                {#each selected.cards_upgraded as c}<li>{c.card_name ?? cardLabel(c.card_id)}</li>{/each}
              </ul>
            </div>
          {/if}
          {#if selected.cards_enchanted.length > 0}
            <div>
              <h4 class="text-xs uppercase text-slate-400 tracking-wide">エンチャント付与</h4>
              <ul class="text-sm">
                {#each selected.cards_enchanted as e}
                  <li>{e.card_name ?? cardLabel(e.card_id)} ← {e.enchantment_id}</li>
                {/each}
              </ul>
            </div>
          {/if}
          {#if selected.cards_removed.length > 0}
            <div>
              <h4 class="text-xs uppercase text-slate-400 tracking-wide">カード除去</h4>
              <ul class="text-sm">
                {#each selected.cards_removed as c}<li>{c.card_name ?? cardLabel(c.card_id)}</li>{/each}
              </ul>
            </div>
          {/if}
          {#if selected.shop_purchases.length > 0}
            <div>
              <h4 class="text-xs uppercase text-slate-400 tracking-wide">購入履歴</h4>
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
            </div>
          {/if}
          {#if selected.rest_options.length > 0}
            <div>
              <h4 class="text-xs uppercase text-slate-400 tracking-wide">休憩所</h4>
              <div class="text-sm">{selected.rest_options.join(', ')}</div>
            </div>
          {/if}
          {#if selected.event_choices.length > 0}
            <div>
              <h4 class="text-xs uppercase text-slate-400 tracking-wide">イベント選択</h4>
              <ul class="text-sm">
                {#each selected.event_choices as c}
                  <li>{c.title || c.history_name || c.text_key}</li>
                {/each}
              </ul>
            </div>
          {/if}
        </div>
      </section>
    {/if}

  {/if}
</div>
