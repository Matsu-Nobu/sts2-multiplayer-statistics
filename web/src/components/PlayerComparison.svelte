<script lang="ts">
  // プレイヤー比較 (全員。spec ui.md §3.4): プレイヤーを行にした表、各列に横棒。
  import type { PlayerCombatSummary } from '../lib/types';
  import type { RdpsTable } from '../lib/rdps';
  import type { RmitTable } from '../lib/rmit';
  import { useSession } from '../lib/session';
  import { playerColor } from '../lib/players';
  import Panel from './ui/Panel.svelte';

  interface Props { stats: Record<string, PlayerCombatSummary | undefined>; rdps: RdpsTable; rmit: RmitTable }
  let { stats, rdps, rmit }: Props = $props();
  const s = useSession();

  const COLS: { key: string; label: string; get: (pid: string) => number }[] = [
    { key: 'eff', label: '有効与ダメージ', get: pid => stats[pid]?.effective_damage_dealt ?? 0 },
    { key: 'rec', label: '被ダメージ',     get: pid => stats[pid]?.damage_received ?? 0 },
    { key: 'blk', label: '有効ブロック',   get: pid => stats[pid]?.effective_block ?? 0 },
    { key: 'rdps', label: '与ダメ貢献',    get: pid => rdps.byPlayer[pid]?.total ?? 0 },
    { key: 'rmit', label: '被ダメ軽減貢献', get: pid => rmit.byPlayer[pid]?.total ?? 0 },
    { key: 'max', label: '最大カードダメージ', get: pid => stats[pid]?.max_single_hit ?? 0 },
  ];
  let maxes = $derived(Object.fromEntries(COLS.map(c => [c.key, Math.max(1, ...s.playerIds.map(c.get))])));
</script>

<Panel title="プレイヤー比較" scope="全員" flush>
  <div class="dt-wrap">
    <table class="dt">
      <thead><tr><th>プレイヤー</th>{#each COLS as c (c.key)}<th class="num">{c.label}</th>{/each}</tr></thead>
      <tbody>
        {#each s.playerIds as pid (pid)}
          {@const color = playerColor(s.playerIds, pid)}
          <tr class={pid === s.player ? 'bg-bg-2/60' : ''}>
            <td class="whitespace-nowrap">
              <span class="inline-flex items-center gap-1.5">
                <span class="w-2 h-2 rounded-full" style:background-color={color}></span>
                <span class={pid === s.player ? 'text-slate-100 font-medium' : 'text-slate-200'}>{s.playerNames[pid] ?? pid}</span>
              </span>
            </td>
            {#each COLS as c (c.key)}
              {@const v = c.get(pid)}
              <td class="num min-w-[7rem]">
                <div class="text-slate-100">{v}</div>
                <div class="mt-1 h-1 bg-bg-3 rounded-full overflow-hidden"><div class="h-full rounded-full" style:width={`${(v / maxes[c.key]) * 100}%`} style:background-color={color}></div></div>
              </td>
            {/each}
          </tr>
        {/each}
      </tbody>
    </table>
  </div>
</Panel>
