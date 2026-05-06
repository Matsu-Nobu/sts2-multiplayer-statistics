<script lang="ts">
  import type { EventRecord } from '../lib/types';
  import { formatPowerName } from '../lib/powers';

  interface Props {
    events: EventRecord[];          // 戦闘内 event のみ（combat_index 設定済）を想定
    playerNames: Record<string, string>;
    powerNames?: Record<string, string>;
  }
  let { events, playerNames, powerNames = {} }: Props = $props();

  // turn_number ごとにグループ化
  let groups = $derived(() => {
    const m = new Map<number, EventRecord[]>();
    for (const ev of events) {
      if (ev.turn_number == null) continue;
      if (!m.has(ev.turn_number)) m.set(ev.turn_number, []);
      m.get(ev.turn_number)!.push(ev);
    }
    return [...m.entries()]
      .sort((a, b) => a[0] - b[0])
      .map(([turn, evs]) => ({
        turn,
        events: evs.sort((a, b) => (a.sequence ?? 0) - (b.sequence ?? 0)),
      }));
  });

  let collapsed: Record<number, boolean> = $state({});
  function toggle(t: number) { collapsed = { ...collapsed, [t]: !collapsed[t] }; }
  function pname(pid: string | undefined): string {
    if (!pid) return '—';
    return playerNames[pid] ?? pid;
  }

  function eventLabel(ev: EventRecord): string {
    const p = ev.payload as any;
    switch (ev.event_type) {
      case 'card_played':     return `${pname(ev.player_id)} → ${p?.card_name ?? p?.card_id ?? 'card'}`;
      case 'card_drawn':      return `${pname(ev.player_id)} drew ${p?.card_id ?? '?'}`;
      case 'damage_dealt':    return `${pname(ev.player_id)} dmg ${p?.amount} (${p?.source_card_id ?? '?'})`;
      case 'damage_received': return `${pname(ev.player_id)} took ${p?.amount}`;
      case 'block_gained':    return `${pname(ev.player_id)} block +${p?.amount}`;
      case 'energy_spent':    return `${pname(ev.player_id)} energy -${p?.amount}`;
      case 'power_changed': {
        const t = p?.target_player_id ? `→ player ${pname(p.target_player_id)}` : '→ enemy';
        const sign = (p?.delta ?? 0) >= 0 ? '+' : '';
        return `${pname(ev.player_id)} ${formatPowerName(p?.power_id ?? '', powerNames)} ${sign}${p?.delta} ${t}`;
      }
      case 'potion_used':     return `${pname(ev.player_id)} potion ${p?.potion_id ?? '?'}`;
      default:                return ev.event_type;
    }
  }

  function eventKindClass(t: string): string {
    switch (t) {
      case 'damage_dealt':    return 'text-ok';
      case 'damage_received': return 'text-bad';
      case 'block_gained':    return 'text-accent';
      case 'card_played':     return 'text-slate-200';
      case 'power_changed':   return 'text-purple-300';
      case 'card_drawn':
      case 'energy_spent':    return 'text-slate-500';
      default:                return 'text-slate-400';
    }
  }

  function activeOnLabel(snap: any[]): string {
    if (!snap || snap.length === 0) return '';
    return snap
      .map((s: any) => `${formatPowerName(s.power_id, powerNames)}×${s.stacks}` + (s.applier ? ` by ${pname(s.applier)}` : ''))
      .join(', ');
  }
</script>

<div class="bg-bg-1 border border-bg-3 rounded-lg overflow-hidden">
  <div class="px-3 py-2 text-sm uppercase tracking-wide text-slate-400 border-b border-bg-3">
    タイムライン
  </div>
  <div class="divide-y divide-bg-3">
    {#each groups() as g (g.turn)}
      <div>
        <button
          type="button"
          class="w-full flex items-center justify-between px-3 py-2 bg-bg-2 hover:bg-bg-3 text-left text-sm"
          onclick={() => toggle(g.turn)}
        >
          <span>
            <span class="text-slate-500 mr-2">{collapsed[g.turn] ? '▸' : '▾'}</span>
            <span class="font-medium">ターン {g.turn}</span>
            <span class="text-slate-500 ml-2">({g.events.length} 件)</span>
          </span>
        </button>
        {#if !collapsed[g.turn]}
          <ol class="font-mono text-xs">
            {#each g.events as ev}
              <li class="px-3 py-1 border-t border-bg-3 hover:bg-bg-2 flex gap-3">
                <span class="text-slate-500 w-8 text-right">{ev.sequence}</span>
                <span class="text-slate-500 w-32 truncate">{ev.event_type}</span>
                <span class="flex-1 {eventKindClass(ev.event_type)}">{eventLabel(ev)}</span>
                {#if ev.event_type === 'damage_dealt'}
                  {@const aot = activeOnLabel((ev.payload as any).active_on_target ?? [])}
                  {#if aot}<span class="text-slate-500">[on target: {aot}]</span>{/if}
                {/if}
              </li>
            {/each}
          </ol>
        {/if}
      </div>
    {/each}
  </div>
</div>
