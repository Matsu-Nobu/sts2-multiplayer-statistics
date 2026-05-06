<script lang="ts">
  import type { RmitTable } from '../lib/rmit';

  interface Props {
    rmit: RmitTable;
    playerNames: Record<string, string>;
    title?: string;
  }
  let { rmit, playerNames, title = '被ダメ軽減貢献度 (rMit)' }: Props = $props();

  let rows = $derived(
    Object.entries(rmit.byPlayer)
      .map(([pid, b]) => ({ pid, ...b }))
      .sort((a, b) => b.total - a.total)
  );
  let max = $derived(rows.reduce((m, r) => Math.max(m, r.total), 0) || 1);

  function toBreakdown(rec: typeof rows[number]): { source: string; amount: number; recipients: string[] }[] {
    const m = new Map<string, { amount: number; recipients: Set<string> }>();
    for (const t of rec.to) {
      if (!m.has(t.source)) m.set(t.source, { amount: 0, recipients: new Set() });
      const e = m.get(t.source)!;
      e.amount += t.amount;
      e.recipients.add(t.recipient);
    }
    return [...m.entries()].map(([source, v]) => ({
      source,
      amount: v.amount,
      recipients: [...v.recipients].map(r => playerNames[r] ?? r),
    }));
  }

  function fromBreakdown(rec: typeof rows[number]): { source: string; amount: number; appliers: string[] }[] {
    const m = new Map<string, { amount: number; appliers: Set<string> }>();
    for (const f of rec.from) {
      if (!m.has(f.source)) m.set(f.source, { amount: 0, appliers: new Set() });
      const e = m.get(f.source)!;
      e.amount += f.amount;
      e.appliers.add(f.applier);
    }
    return [...m.entries()].map(([source, v]) => ({
      source,
      amount: v.amount,
      appliers: [...v.appliers].map(a => playerNames[a] ?? a),
    }));
  }

  function sourceLabel(s: string): string {
    switch (s) {
      case 'weak':           return '弱体 (Weak)';
      case 'strength_down':  return '筋力低下';
      case 'self':           return '自力';
      default:               return s;
    }
  }

  const help = `各 damage_received について、敵に乗っているデバフが「減らした分」を撒いた人へ加算する。
・弱体(Weak): 敵が Weak のとき与ダメは 0.75 倍。本来 1.0 倍受けていた量との差 = 受けたダメ ÷ 3 を Weak applier に加算。
・筋力低下(Strength<0): 敵の筋力がマイナスのとき、1 ヒットあたり |stacks| を applier に加算。`;
</script>

<section class="space-y-3">
  <h3 class="text-xs uppercase tracking-wide text-slate-500 flex items-center gap-1">
    <span>{title}</span>
    <span class="relative inline-flex group">
      <span
        class="inline-flex items-center justify-center w-3.5 h-3.5 rounded-full bg-bg-3 text-slate-400 text-[10px] leading-none cursor-help select-none"
        aria-label={help}
      >?</span>
      <span
        class="pointer-events-none absolute left-0 top-full mt-1 w-80 z-20 bg-bg-0 border border-bg-3 rounded shadow-lg px-2 py-1.5 text-[11px] text-slate-200 normal-case tracking-normal whitespace-pre-line opacity-0 group-hover:opacity-100 transition-opacity"
      >{help}</span>
    </span>
  </h3>

  {#if rows.length === 0}
    <div class="text-slate-500 text-sm">データなし</div>
  {:else}
    <div class="bg-bg-1 border border-bg-3 rounded-lg overflow-hidden">
      <table class="w-full text-sm">
        <thead class="bg-bg-2 text-slate-400 text-xs uppercase">
          <tr>
            <th class="text-left  py-2 px-3">プレイヤー</th>
            <th class="text-right py-2 px-3">rMit 合計</th>
            <th class="text-right py-2 px-3">自力</th>
            <th class="text-left  py-2 px-3">他人へ貢献（rMit加算）</th>
            <th class="text-left  py-2 px-3">他人から（参考）</th>
            <th class="text-left  py-2 px-3 w-1/4">バー</th>
          </tr>
        </thead>
        <tbody>
          {#each rows as r (r.pid)}
            {@const toList = toBreakdown(r)}
            {@const fromList = fromBreakdown(r)}
            <tr class="border-t border-bg-3 hover:bg-bg-2 align-top">
              <td class="py-2 px-3 font-medium">{playerNames[r.pid] ?? r.pid}</td>
              <td class="py-2 px-3 text-right tabular text-accent">{r.total}</td>
              <td class="py-2 px-3 text-right tabular text-slate-300">{r.self}</td>
              <td class="py-2 px-3">
                {#if toList.length === 0}
                  <span class="text-slate-500">—</span>
                {:else}
                  <ul class="space-y-0.5">
                    {#each toList as t}
                      <li class="text-xs">
                        <span class="text-accent">+{t.amount}</span>
                        <span class="text-slate-400">{sourceLabel(t.source)}</span>
                        <span class="text-slate-500">→ {t.recipients.join(', ')}</span>
                      </li>
                    {/each}
                  </ul>
                {/if}
              </td>
              <td class="py-2 px-3">
                {#if fromList.length === 0}
                  <span class="text-slate-500">—</span>
                {:else}
                  <ul class="space-y-0.5">
                    {#each fromList as f}
                      <li class="text-xs text-slate-500">
                        +{f.amount} {sourceLabel(f.source)} ← {f.appliers.join(', ')}
                      </li>
                    {/each}
                  </ul>
                {/if}
              </td>
              <td class="py-2 px-3">
                <div class="w-full bg-bg-2 rounded h-2 overflow-hidden">
                  <div class="bg-accent h-full" style:width={`${(r.total / max) * 100}%`}></div>
                </div>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</section>
